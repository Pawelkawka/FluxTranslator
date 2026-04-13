import asyncio
import logging
import threading
import tempfile
import os
import shutil
import time
import wave
import subprocess
from typing import Optional

import edge_tts
import sounddevice as sd
import numpy as np

log = logging.getLogger(__name__)

TTS_SAMPLE_RATE = 24000

# fallback voices
VOICE_FALLBACKS = {
    "no-NO-FinnNeural": "no-NO-PernilleNeural",
    "no-NO-PernilleNeural": "no-NO-FinnNeural",
    "sv-SE-SofieNeural": "sv-SE-MattiasNeural",
    "sv-SE-MattiasNeural": "sv-SE-SofieNeural",
    "da-DK-ChristelNeural": "da-DK-JeppeNeural",
    "da-DK-JeppeNeural": "da-DK-ChristelNeural",
    "fi-FI-NooraNeural": "fi-FI-HarriNeural",
    "fi-FI-HarriNeural": "fi-FI-NooraNeural",
}

# tts languages
TTS_LANGUAGES = {
    "en": {
        "name": "English",
        "voices": [
            "en-US-EmmaMultilingualNeural",
            "en-US-AvaMultilingualNeural",
            "en-US-AndrewMultilingualNeural",
            "en-US-BrianMultilingualNeural",
            "en-US-JennyNeural",
            "en-US-GuyNeural",
            "en-GB-SoniaNeural",
            "en-GB-RyanNeural",
        ],
    },
    "pl": {
        "name": "Polish",
        "voices": [
            "pl-PL-ZofiaNeural",
            "pl-PL-MarekNeural",
        ],
    },
    "de": {
        "name": "German",
        "voices": [
            "de-DE-KatjaNeural",
            "de-DE-ConradNeural",
            "de-DE-AmalaNeural",
            "de-DE-BerndNeural",
        ],
    },
    "ru": {
        "name": "Russian",
        "voices": [
            "ru-RU-SvetlanaNeural",
            "ru-RU-DmitryNeural",
        ],
    },
    "fr": {
        "name": "French",
        "voices": [
            "fr-FR-DeniseNeural",
            "fr-FR-HenriNeural",
            "fr-FR-EloiseNeural",
        ],
    },
    "it": {
        "name": "Italian",
        "voices": [
            "it-IT-ElsaNeural",
            "it-IT-DiegoNeural",
            "it-IT-IsabellaNeural",
        ],
    },
    "es": {
        "name": "Spanish",
        "voices": [
            "es-ES-ElviraNeural",
            "es-ES-AlvaroNeural",
            "es-ES-AbrilNeural",
        ],
    },
    "cs": {
        "name": "Czech",
        "voices": [
            "cs-CZ-VlastaNeural",
            "cs-CZ-AntoninNeural",
        ],
    },
    "uk": {
        "name": "Ukrainian",
        "voices": [
            "uk-UA-PolinaNeural",
            "uk-UA-OstapNeural",
        ],
    },
    "zh": {
        "name": "Chinese",
        "voices": [
            "zh-CN-XiaoxiaoNeural",
            "zh-CN-YunxiNeural",
            "zh-CN-YunjianNeural",
            "zh-CN-XiaoyiNeural",
        ],
    },
    "ja": {
        "name": "Japanese",
        "voices": [
            "ja-JP-NanamiNeural",
            "ja-JP-KeitaNeural",
        ],
    },
    "ko": {
        "name": "Korean",
        "voices": [
            "ko-KR-SunHiNeural",
            "ko-KR-InJoonNeural",
        ],
    },
    "pt": {
        "name": "Portuguese",
        "voices": [
            "pt-PT-RaquelNeural",
            "pt-PT-DuarteNeural",
            "pt-BR-FranciscaNeural",
            "pt-BR-AntonioNeural",
        ],
    },
    "nl": {
        "name": "Dutch",
        "voices": [
            "nl-NL-ColetteNeural",
            "nl-NL-FennaNeural",
            "nl-NL-MaartenNeural",
        ],
    },
    "sv": {
        "name": "Swedish",
        "voices": [
            "sv-SE-SofieNeural",
            "sv-SE-MattiasNeural",
        ],
    },
    "fi": {
        "name": "Finnish",
        "voices": [
            "fi-FI-NooraNeural",
            "fi-FI-HarriNeural",
        ],
    },
    "da": {
        "name": "Danish",
        "voices": [
            "da-DK-ChristelNeural",
            "da-DK-JeppeNeural",
        ],
    },
    "no": {
        "name": "Norwegian",
        "voices": [
            "no-NO-PernilleNeural",
            "no-NO-FinnNeural",
        ],
    },
    "tr": {
        "name": "Turkish",
        "voices": [
            "tr-TR-EmelNeural",
            "tr-TR-AhmetNeural",
        ],
    },
    "ar": {
        "name": "Arabic",
        "voices": [
            "ar-SA-ZariyahNeural",
            "ar-SA-HamedNeural",
        ],
    },
}

_playback_lock = threading.Lock()
_request_condition = threading.Condition()
_is_speaking = False
_current_stop_event: Optional[threading.Event] = None
_worker_thread: Optional[threading.Thread] = None
_pending_request: Optional[tuple[str, str, Optional[int], str, str, str]] = None


def list_voices_sync() -> list[dict]:
    try:
        voices = asyncio.run(edge_tts.list_voices())
        result = []
        for v in voices:
            result.append({
                "name": v["ShortName"],
                "locale": v["Locale"],
                "gender": v["Gender"],
                "friendly_name": v.get("FriendlyName", v["ShortName"]),
            })
        return result
    except Exception as exc:
        log.error("Error listing voices: %s", exc)
        return []


def get_available_languages() -> list[dict]:
    result = []
    for lang_code, lang_data in TTS_LANGUAGES.items():
        result.append({
            "code": lang_code,
            "name": lang_data["name"],
            "voices": lang_data["voices"],
        })
    return result


def get_voice_for_language(lang_code: str) -> str:
    lang = lang_code.strip().lower()

    if lang in TTS_LANGUAGES:
        return TTS_LANGUAGES[lang]["voices"][0]

    for code, data in TTS_LANGUAGES.items():
        if code == lang or lang.startswith(code):
            return data["voices"][0]

    log.warning("No voice found for language '%s', defaulting to English", lang)
    return TTS_LANGUAGES["en"]["voices"][0]


def list_audio_devices() -> list[dict]:
    try:
        devices = sd.query_devices()
        result = []
        for i, dev in enumerate(devices):
            if dev['max_output_channels'] > 0:
                result.append({
                    "id": i,
                    "name": dev['name'],
                    "channels": dev['max_output_channels'],
                    "samplerate": int(dev['default_samplerate']) if dev['default_samplerate'] else 44100,
                    "is_default": i == sd.default.device[1],
                })
        return result
    except Exception as exc:
        log.error("Error listing audio devices: %s", exc)
        return []


def _decode_mp3_to_wav(mp3_path: str, wav_path: str) -> tuple[np.ndarray, int]:
    try:
        result = subprocess.run(
            [
                'ffmpeg',
                '-i', mp3_path,
                '-ar', str(TTS_SAMPLE_RATE),
                '-ac', '1',
                '-sample_fmt', 's16',
                '-y',
                wav_path
            ],
            capture_output=True,
            timeout=30
        )

        if result.returncode != 0:
            log.error("FFmpeg failed: %s", result.stderr.decode())
            raise RuntimeError(f"FFmpeg failed: {result.stderr.decode()}")

        with wave.open(wav_path, 'rb') as wf:
            n_channels = wf.getnchannels()
            sample_width = wf.getsampwidth()
            framerate = wf.getframerate()
            n_frames = wf.getnframes()

            assert n_channels == 1, f"Expected mono, got {n_channels} channels"
            assert sample_width == 2, f"Expected 16-bit, got {sample_width * 8}-bit"
            assert framerate == TTS_SAMPLE_RATE, f"Expected {TTS_SAMPLE_RATE}Hz, got {framerate}Hz"

            raw_data = wf.readframes(n_frames)
            samples = np.frombuffer(raw_data, dtype=np.int16)
            samples_float = samples.astype(np.float32) / 32768.0

            return samples_float, framerate

    except FileNotFoundError:
        raise RuntimeError("FFmpeg not found. Please install FFmpeg.")


def _validate_speak_request(device_id: Optional[int]) -> None:
    if shutil.which("ffmpeg") is None:
        raise RuntimeError("FFmpeg not found. Please install FFmpeg.")

    if device_id is None:
        return

    try:
        device = sd.query_devices(device_id, "output")
    except Exception as exc:
        raise RuntimeError(f"Invalid output device: {device_id}") from exc

    if device["max_output_channels"] <= 0:
        raise RuntimeError(f"Device {device_id} does not support audio output.")


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
        end = start + chunk_frames
        stream.write(samples[start:end])
    return not stop_event.is_set()


def _speak_text(
    text: str,
    voice: str,
    device_id: Optional[int] = None,
    rate: str = "+0%",
    volume: str = "+0%",
    pitch: str = "+0Hz",
    stop_event: Optional[threading.Event] = None,
) -> None:
    if not text or not text.strip():
        log.warning("No text provided for TTS")
        return

    stop_event = stop_event or threading.Event()

    voices_to_try = [voice]
    if voice in VOICE_FALLBACKS:
        voices_to_try.append(VOICE_FALLBACKS[voice])
    if "en-US-EmmaMultilingualNeural" not in voices_to_try:
        voices_to_try.append("en-US-EmmaMultilingualNeural")

    last_error = None

    for attempt_voice in voices_to_try:
        if stop_event.is_set():
            break

        tmp_mp3 = None
        tmp_wav = None
        try:
            log.info("Starting TTS: voice=%s, device_id=%s, text=%r", attempt_voice, device_id, text[:50])

            comm = edge_tts.Communicate(
                text=text.strip(),
                voice=attempt_voice,
                rate=rate,
                volume=volume,
                pitch=pitch,
            )

            with tempfile.NamedTemporaryFile(suffix=".mp3", delete=False) as tmp:
                tmp_mp3 = tmp.name

            tmp_wav = tmp_mp3.replace('.mp3', '.wav')

            for attempt in range(2):
                try:
                    comm.save_sync(tmp_mp3)
                    break
                except Exception as save_exc:
                    if attempt < 1:
                        log.warning("TTS save attempt %d failed, retrying: %s", attempt + 1, save_exc)
                        time.sleep(0.5)
                    else:
                        raise

            if stop_event.is_set():
                log.info("TTS cancelled before playback")
                break

            samples, sample_rate = _decode_mp3_to_wav(tmp_mp3, tmp_wav)

            if stop_event.is_set():
                log.info("TTS cancelled during decoding")
                break

            if len(samples) == 0:
                log.warning("No audio generated")
                break

            with sd.OutputStream(
                device=device_id,
                samplerate=sample_rate,
                channels=1,
                dtype=np.float32,
            ) as stream:
                if not _write_samples(stream, samples, stop_event, sample_rate):
                    log.info("TTS playback stopped before completion")
                    break

            log.info("TTS playback completed (%.2f seconds)", len(samples) / sample_rate)
            return

        except Exception as exc:
            last_error = exc
            log.warning("TTS failed with voice %s: %s", attempt_voice, exc)
            continue
        finally:
            for tmp_path in (tmp_mp3, tmp_wav):
                if tmp_path and os.path.exists(tmp_path):
                    try:
                        os.unlink(tmp_path)
                    except OSError:
                        pass

    if last_error:
        log.error("TTS failed after trying all voices: %s", last_error, exc_info=True)


def _tts_worker() -> None:
    global _worker_thread, _pending_request, _current_stop_event, _is_speaking

    while True:
        with _request_condition:
            while _pending_request is None:
                notified = _request_condition.wait(timeout=30)
                if _pending_request is None and not notified:
                    _worker_thread = None
                    return

            request = _pending_request
            _pending_request = None

        stop_event = threading.Event()
        with _playback_lock:
            _current_stop_event = stop_event
            _is_speaking = True

        try:
            _speak_text(*request, stop_event=stop_event)
        finally:
            with _playback_lock:
                if _current_stop_event is stop_event:
                    _current_stop_event = None
                _is_speaking = False


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
        has_pending_request = _pending_request is not None

    with _playback_lock:
        return _is_speaking or has_pending_request
