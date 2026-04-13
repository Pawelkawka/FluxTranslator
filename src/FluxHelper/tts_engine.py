import asyncio
import logging
import threading
import tempfile
import os
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
_is_speaking = False
_stop_event = threading.Event()


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
        raise RuntimeError("FFmpeg not found/install ffmpeg.")


def speak_text(
    text: str,
    voice: str,
    device_id: Optional[int] = None,
    rate: str = "+0%",
    volume: str = "+0%",
    pitch: str = "+0Hz",
) -> None:
    global _is_speaking

    if not text or not text.strip():
        log.warning("No text provided for TTS")
        return

    _stop_event.clear()
    tmp_mp3 = None
    tmp_wav = None

    voices_to_try = [voice]
    if voice in VOICE_FALLBACKS:
        voices_to_try.append(VOICE_FALLBACKS[voice])
    if "en-US-EmmaMultilingualNeural" not in voices_to_try:
        voices_to_try.append("en-US-EmmaMultilingualNeural")

    last_error = None

    for attempt_voice in voices_to_try:
        if _stop_event.is_set():
            break

        try:
            with _playback_lock:
                _is_speaking = True

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

            if _stop_event.is_set():
                log.info("TTS cancelled before playback")
                break

            samples, sample_rate = _decode_mp3_to_wav(tmp_mp3, tmp_wav)

            if _stop_event.is_set():
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
                stream.write(samples)

            log.info("TTS playback completed (%.2f seconds)", len(samples) / sample_rate)
            return

        except Exception as exc:
            last_error = exc
            log.warning("TTS failed with voice %s: %s", attempt_voice, exc)

            for tmp_path in [tmp_mp3, tmp_wav]:
                if tmp_path and os.path.exists(tmp_path):
                    try:
                        os.unlink(tmp_path)
                    except:
                        pass
            tmp_mp3 = None
            tmp_wav = None
            continue
        finally:
            with _playback_lock:
                _is_speaking = False

    if last_error:
        log.error("TTS failed after trying all voices: %s", last_error, exc_info=True)


def stop_speaking() -> None:
    global _is_speaking

    _stop_event.set()

    with _playback_lock:
        if _is_speaking:
            log.info("Stopping TTS playback")
            _is_speaking = False


def is_currently_speaking() -> bool:
    with _playback_lock:
        return _is_speaking
