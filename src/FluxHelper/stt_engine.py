import threading
import time
import logging

import numpy as np
import sounddevice as sd
import speech_recognition as sr

import state

log = logging.getLogger("stt_engine")

#audio constants
SAMPLE_RATE  = 16_000
CHANNELS     = 1
SAMPLE_WIDTH = 2
CHUNK_SECS   = 0.1



def _record(duration: float, stop: threading.Event) -> bytes:
    frames: list[np.ndarray] = []
    chunk = int(SAMPLE_RATE * CHUNK_SECS)
    with sd.InputStream(
        samplerate=SAMPLE_RATE,
        channels=CHANNELS,
        dtype="int16",
        blocksize=chunk,
    ) as stream:
        end = time.monotonic() + duration
        while time.monotonic() < end and not stop.is_set():
            data, _ = stream.read(chunk)
            frames.append(data)
    return np.concatenate(frames, axis=0).tobytes() if frames else b""


def _to_audio_data(pcm: bytes) -> sr.AudioData:
    return sr.AudioData(pcm, SAMPLE_RATE, SAMPLE_WIDTH)


#1worker

def worker(session_id: int, language: str, max_secs: int) -> None:
    recognizer = sr.Recognizer()
    stop = state.get_stop_signal()

    try:
        state.update_status("calibrating", "Opening microphone…")
        _record(0.3, threading.Event())

        if stop.is_set() or session_id != state.get_active_session_id():
            return

        state.update_status("listening", f"Speak now ({language})…")
        pcm = _record(max_secs, stop)

    except OSError as exc:
        state.update_status("error", "No microphone found or access denied.", is_error=True, is_final=True)
        log.error("Microphone OSError: %s", exc)
        return
    except Exception as exc:
        log.exception("Unexpected error during recording")
        state.update_status("error", str(exc), is_error=True, is_final=True)
        return

    if not pcm or session_id != state.get_active_session_id():
        return

    #2sppech recognition
    try:
        state.update_status("processing", "Recognising speech…")
        audio = _to_audio_data(pcm)
        text  = recognizer.recognize_google(audio, language=language)
        state.update_status("done", text or "Empty result.", is_final=True)

    except sr.UnknownValueError:
        state.update_status("error", "Could not understand audio.", is_error=True, is_final=True)
    except sr.RequestError as exc:
        state.update_status("error", f"Google API error: {exc}", is_error=True, is_final=True)
    except OSError as exc:
        state.update_status(
            "error",
            f"Network error during recognition: {exc}",
            is_error=True,
            is_final=True,
        )
        log.error("Recognition network OSError: %s", exc)
    except Exception as exc:
        log.exception("Unexpected error during recognition")
        state.update_status("error", str(exc), is_error=True, is_final=True)
