import asyncio
import logging
import os
import shutil
import subprocess
import tempfile
import threading
import time
import wave
from typing import Optional

import edge_tts
import numpy as np
import sounddevice as sd

from tts_voices import DEFAULT_VOICE, TTS_LANGUAGES, VOICE_FALLBACKS

log = logging.getLogger(__name__)

TTS_SAMPLE_RATE = 24000


# ── Device resolution ───────────────────────────────────────────────


def _resolve_device_id(device_id) -> Optional[int]:
    if device_id is None:
        return None
    if isinstance(device_id, int):
        return device_id

    if isinstance(device_id, str):
        try:
            return int(device_id)
        except ValueError:
            pass

        target = device_id.strip()
        try:
            for idx, dev in enumerate(sd.query_devices()):
                dev_name = dev.get("name", "")
                if target in dev_name or dev_name in target:
                    return idx
        except Exception as exc:
            log.warning("Failed to query devices for ID resolution: %s", exc)

        log.warning("Could not resolve device ID '%s' to an integer index", device_id)

    return None


def _validate_speak_request(device_id) -> None:
    if shutil.which("ffmpeg") is None:
        raise RuntimeError("FFmpeg not found. Please install FFmpeg.")

    resolved_id = _resolve_device_id(device_id)
    if resolved_id is None:
        return

    try:
        device = sd.query_devices(resolved_id, "output")
    except Exception as exc:
        raise RuntimeError(f"Invalid output device: {device_id}") from exc

    if device["max_output_channels"] <= 0:
        raise RuntimeError(f"Device {device_id} does not support audio output.")


# ── Voice / language queries ────────────────────────────────────────


def list_voices_sync() -> list[dict]:
    try:
        voices = asyncio.run(edge_tts.list_voices())
        return [
            {
                "name": v["ShortName"],
                "locale": v["Locale"],
                "gender": v["Gender"],
                "friendly_name": v.get("FriendlyName", v["ShortName"]),
            }
            for v in voices
        ]
    except Exception as exc:
        log.error("Error listing voices: %s", exc)
        return []


def get_available_languages() -> list[dict]:
    return [
        {"code": code, "name": data["name"], "voices": data["voices"]}
        for code, data in TTS_LANGUAGES.items()
    ]


def get_voice_for_language(lang_code: str) -> str:
    lang = lang_code.strip().lower()

    if lang in TTS_LANGUAGES:
        return TTS_LANGUAGES[lang]["voices"][0]

    for code, data in TTS_LANGUAGES.items():
        if lang.startswith(code):
            return data["voices"][0]

    log.warning("No voice found for language '%s', defaulting to English", lang)
    return TTS_LANGUAGES["en"]["voices"][0]


def list_output_devices() -> list[dict]:
    devices = []
    try:
        dev_list = sd.query_devices()
        default_output = sd.default.device[1]
        for idx, dev in enumerate(dev_list):
            if dev.get("max_output_channels", 0) > 0:
                devices.append({
                    "index": idx,
                    "name": dev.get("name", ""),
                    "channels": dev.get("max_output_channels"),
                    "sample_rate": dev.get("default_samplerate"),
                    "is_default": idx == default_output if default_output is not None else False,
                })
    except Exception as exc:
        log.error("Error listing output devices: %s", exc)
    return devices


# ── Audio conversion & playback ─────────────────────────────────────


def _get_device_sample_rate(device_id: Optional[int]) -> int:
    if device_id is None:
        return TTS_SAMPLE_RATE
    try:
        info = sd.query_devices(device_id, "output")
        rate = int(info.get("default_samplerate", TTS_SAMPLE_RATE))
        return rate if rate > 0 else TTS_SAMPLE_RATE
    except Exception:
        return TTS_SAMPLE_RATE


def _decode_mp3_to_wav(mp3_path: str, wav_path: str, target_rate: int = TTS_SAMPLE_RATE) -> tuple[np.ndarray, int]:
    try:
        result = subprocess.run(
            [
                "ffmpeg",
                "-i", mp3_path,
                "-ar", str(target_rate),
                "-ac", "1",
                "-sample_fmt", "s16",
                "-y",
                wav_path,
            ],
            capture_output=True,
            timeout=30,
        )

        if result.returncode != 0:
            stderr = result.stderr.decode()
            log.error("FFmpeg failed: %s", stderr)
            raise RuntimeError(f"FFmpeg failed: {stderr}")

        with wave.open(wav_path, "rb") as wf:
            assert wf.getnchannels() == 1, f"Expected mono, got {wf.getnchannels()} channels"
            assert wf.getsampwidth() == 2, f"Expected 16-bit, got {wf.getsampwidth() * 8}-bit"
            assert wf.getframerate() == target_rate, f"Expected {target_rate}Hz, got {wf.getframerate()}Hz"

            raw_data = wf.readframes(wf.getnframes())
            samples = np.frombuffer(raw_data, dtype=np.int16).astype(np.float32) / 32768.0
            return samples, wf.getframerate()

    except FileNotFoundError:
        raise RuntimeError("FFmpeg not found. Please install FFmpeg.")


def _write_samples(
    stream: sd.OutputStream,
    samples: np.ndarray,
    stop_event: threading.Event,
    sample_rate: int,
) -> bool:
    chunk_frames = max(sample_rate // 10, 1)
    for start in range(0, len(samples), chunk_frames):
        if stop_event.is_set():
            return False
        stream.write(samples[start : start + chunk_frames])
    return not stop_event.is_set()


def _cleanup_temp_files(*paths: Optional[str]) -> None:
    for path in paths:
        if path and os.path.exists(path):
            try:
                os.unlink(path)
            except OSError:
                pass


# ── Core TTS synthesis ──────────────────────────────────────────────


def _build_voice_candidates(voice: str) -> list[str]:
    candidates = [voice]
    if voice in VOICE_FALLBACKS:
        candidates.append(VOICE_FALLBACKS[voice])
    if DEFAULT_VOICE not in candidates:
        candidates.append(DEFAULT_VOICE)
    return candidates


def _synthesize_and_play(
    text: str,
    voice: str,
    resolved_device_id: Optional[int],
    rate: str,
    volume: str,
    pitch: str,
    stop_event: threading.Event,
) -> None:
    log.info(
        "Starting TTS: voice=%s, device_id=%s, text=%r",
        voice, resolved_device_id, text[:50],
    )

    comm = edge_tts.Communicate(
        text=text.strip(), voice=voice,
        rate=rate, volume=volume, pitch=pitch,
    )

    device_rate = _get_device_sample_rate(resolved_device_id)
    log.info("Using sample rate %d Hz for device %s", device_rate, resolved_device_id)

    with tempfile.NamedTemporaryFile(suffix=".mp3", delete=False) as tmp:
        tmp_mp3 = tmp.name
    tmp_wav = tmp_mp3.replace(".mp3", ".wav")

    try:
        for attempt in range(2):
            try:
                comm.save_sync(tmp_mp3)
                break
            except Exception as exc:
                if attempt == 0:
                    log.warning("TTS save attempt 1 failed, retrying: %s", exc)
                    time.sleep(0.5)
                else:
                    raise

        if stop_event.is_set():
            log.info("TTS cancelled before playback")
            return

        samples, sample_rate = _decode_mp3_to_wav(tmp_mp3, tmp_wav, device_rate)
        if stop_event.is_set() or len(samples) == 0:
            return

        with sd.OutputStream(
            device=resolved_device_id,
            samplerate=sample_rate,
            channels=1,
            dtype=np.float32,
        ) as stream:
            if not _write_samples(stream, samples, stop_event, sample_rate):
                log.info("TTS playback stopped before completion")
                return

        log.info("TTS playback completed (%.2f seconds)", len(samples) / sample_rate)
    finally:
        _cleanup_temp_files(tmp_mp3, tmp_wav)


def _speak_text(
    text: str,
    voice: str,
    device_id=None,
    rate: str = "+0%",
    volume: str = "+0%",
    pitch: str = "+0Hz",
    stop_event: Optional[threading.Event] = None,
) -> None:
    if not text or not text.strip():
        log.warning("No text provided for TTS")
        return

    stop_event = stop_event or threading.Event()
    resolved_device_id = _resolve_device_id(device_id)
    last_error = None

    for attempt_voice in _build_voice_candidates(voice):
        if stop_event.is_set():
            break
        try:
            _synthesize_and_play(
                text, attempt_voice, resolved_device_id,
                rate, volume, pitch, stop_event,
            )
            return
        except Exception as exc:
            last_error = exc
            log.warning("TTS failed with voice %s on device %s: %s", attempt_voice, resolved_device_id, exc)

    if last_error and resolved_device_id is not None:
        log.warning(
            "All attempts failed on device %d, retrying on system default device",
            resolved_device_id,
        )
        try:
            _synthesize_and_play(text, voice, None, rate, volume, pitch, stop_event)
            return
        except Exception as exc:
            log.error("TTS failed even on default device: %s", exc, exc_info=True)
            return

    if last_error:
        log.error("TTS failed after trying all voices: %s", last_error, exc_info=True)


# ── Worker thread management ────────────────────────────────────────

_playback_lock = threading.Lock()
_request_condition = threading.Condition()
_is_speaking = False
_current_stop_event: Optional[threading.Event] = None
_worker_thread: Optional[threading.Thread] = None
_pending_request: Optional[tuple[str, str, Optional[int], str, str, str]] = None


def _tts_worker() -> None:
    global _worker_thread, _pending_request, _current_stop_event, _is_speaking

    while True:
        with _request_condition:
            while _pending_request is None:
                notified = _request_condition.wait(timeout=30)
                if _pending_request is None and not notified:
                    _worker_thread = None
                    return

            current_request = _pending_request
            _pending_request = None

        stop_event = threading.Event()
        with _playback_lock:
            _current_stop_event = stop_event
            _is_speaking = True

        try:
            _speak_text(*current_request, stop_event=stop_event)
        finally:
            with _playback_lock:
                if _current_stop_event is stop_event:
                    _current_stop_event = None
                _is_speaking = False


# ── Public API ──────────────────────────────────────────────────────


def start_speaking(
    text: str,
    voice: str,
    device_id: Optional[int] = None,
    rate: str = "+0%",
    volume: str = "+0%",
    pitch: str = "+0Hz",
) -> None:
    global _worker_thread, _pending_request

    _validate_speak_request(device_id)
    request = (text, voice, device_id, rate, volume, pitch)

    with _request_condition:
        with _playback_lock:
            if _current_stop_event is not None:
                _current_stop_event.set()

        _pending_request = request

        if _worker_thread is None or not _worker_thread.is_alive():
            _worker_thread = threading.Thread(target=_tts_worker, name="tts-worker", daemon=True)
            _worker_thread.start()

        _request_condition.notify()


def stop_speaking() -> None:
    global _pending_request

    with _request_condition:
        _pending_request = None
        _request_condition.notify_all()

    with _playback_lock:
        if _current_stop_event is not None:
            _current_stop_event.set()
        if _is_speaking:
            log.info("Stopping TTS playback")
            _is_speaking = False


def is_currently_speaking() -> bool:
    with _request_condition:
        has_pending = _pending_request is not None
    with _playback_lock:
        return _is_speaking or has_pending
