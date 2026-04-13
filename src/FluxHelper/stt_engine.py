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
SPEECH_RMS_FLOOR = 300.0
SPEECH_RMS_MULTIPLIER = 3.0


class SpeechTimeoutError(RuntimeError):
    pass



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


def _pcm_rms(pcm: bytes) -> float:
    if not pcm:
        return 0.0

    samples = np.frombuffer(pcm, dtype=np.int16).astype(np.float32)
    if samples.size == 0:
        return 0.0
    return float(np.sqrt(np.mean(np.square(samples))))


def _chunk_rms(chunk: np.ndarray) -> float:
    if chunk.size == 0:
        return 0.0

    mono = chunk.astype(np.float32)
    return float(np.sqrt(np.mean(np.square(mono))))


def _record_phrase(
    max_duration: float,
    stop: threading.Event,
    initial_silence_timeout: float,
    silence_timeout: float,
    speech_threshold: float,
    manual_mode: bool,
) -> bytes:
    frames: list[np.ndarray] = []
    chunk = int(SAMPLE_RATE * CHUNK_SECS)
    start_time = time.monotonic()
    speech_started = False
    last_speech_at = start_time

    with sd.InputStream(
        samplerate=SAMPLE_RATE,
        channels=CHANNELS,
        dtype="int16",
        blocksize=chunk,
    ) as stream:
        while True:
            now = time.monotonic()
            if now - start_time >= max_duration:
                break

            if stop.is_set():
                if state.should_finalize_on_stop():
                    break
                return b""

            data, _ = stream.read(chunk)
            data = np.copy(data)
            frames.append(data)

            chunk_rms = _chunk_rms(data)
            chunk_time = time.monotonic()
            if chunk_rms >= speech_threshold:
                speech_started = True
                last_speech_at = chunk_time
                continue

            if not speech_started and chunk_time - start_time >= initial_silence_timeout:
                raise SpeechTimeoutError("No speech detected before timeout.")

            if not manual_mode and speech_started and chunk_time - last_speech_at >= silence_timeout:
                break

    if stop.is_set() and not state.should_finalize_on_stop():
        return b""

    if not speech_started:
        if manual_mode:
            raise SpeechTimeoutError("No speech detected before manual stop.")
        raise SpeechTimeoutError("No speech detected before auto-stop.")

    return np.concatenate(frames, axis=0).tobytes() if frames else b""


def _to_audio_data(pcm: bytes) -> sr.AudioData:
    return sr.AudioData(pcm, SAMPLE_RATE, SAMPLE_WIDTH)


#1worker

def worker(
    session_id: int,
    language: str,
    max_secs: int,
    initial_silence_timeout: float,
    silence_timeout: float,
    manual_mode: bool,
) -> None:
    recognizer = sr.Recognizer()
    stop = state.get_stop_signal()

    try:
        state.update_status("calibrating", "Opening microphone…")
        warmup_pcm = _record(0.3, threading.Event())
        speech_threshold = max(_pcm_rms(warmup_pcm) * SPEECH_RMS_MULTIPLIER, SPEECH_RMS_FLOOR)

        if stop.is_set() or session_id != state.get_active_session_id():
            return

        if manual_mode:
            state.update_status("listening", f"Speak now ({language}) Manual mode…")
        else:
            state.update_status("listening", f"Speak now ({language}) Auto mode…")

        pcm = _record_phrase(
            max_secs,
            stop,
            initial_silence_timeout,
            silence_timeout,
            speech_threshold,
            manual_mode,
        )

    except OSError as exc:
        state.update_status("error", "No microphone found or access denied.", is_error=True, is_final=True)
        log.error("Microphone OSError: %s", exc)
        return
    except SpeechTimeoutError as exc:
        state.update_status("error", str(exc), is_error=True, is_final=True)
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
