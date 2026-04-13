import sys
import threading
import time
import logging

from flask import Flask, request, jsonify

import state
import stt_engine
import translate_engine
import tts_engine

logging.basicConfig(
    level=logging.INFO,
    format="[%(asctime)s] %(levelname)s %(message)s",
    stream=sys.stdout,
)
log = logging.getLogger("fluxhelper")
app = Flask(__name__)

@app.route("/health")
def health():
    return jsonify({"status": "ok"})


@app.route("/status")
def get_status():
    return jsonify(state.get_status())


@app.route("/start", methods=["POST"])
def start():
    request_body = request.get_json(silent=True) or {}
    language = request_body.get("language", "pl-PL")
    max_recording_seconds = int(
        request_body.get("max_recording_seconds",
            request_body.get("phrase_time_limit", request_body.get("initial_silence_timeout", 30)))
    )

    state.request_stop()
    time.sleep(0.05)
    state.reset_stop_signal()

    session_id = state.begin_new_session()
    state.update_status("starting", "Starting recording...")
    threading.Thread(
        target=stt_engine.worker,
        args=(session_id, language, max_recording_seconds),
        daemon=True,
    ).start()
    return jsonify({"ok": True, "session_id": session_id})


@app.route("/stop", methods=["POST"])
def stop():
    state.request_stop()
    state.update_status("idle", "Recording stopped.", is_final=True)
    return jsonify({"ok": True})

@app.route("/translate", methods=["POST"])
def translate():
    request_body = request.get_json(silent=True) or {}
    text = request_body.get("text", "").strip()
    source_language = request_body.get("source_lang", "pl")
    target_language = request_body.get("target_lang", "en")
    models_dir = request_body.get("models_dir", "models")

    if not text:
        return jsonify({"ok": False, "error": "No text provided."}), 400

    try:
        result = translate_engine.translate(text, source_language, target_language, models_dir)
        log.info("Translated [%s->%s]: %r -> %r", source_language, target_language, text, result)
        return jsonify({"ok": True, "result": result})
    except Exception as exc:
        log.error("Translation error: %s", exc)
        return jsonify({"ok": False, "error": str(exc)}), 500


@app.route("/models")
def list_models():
    models_dir = request.args.get("models_dir", "models")
    return jsonify({"models": translate_engine.list_models(models_dir)})


@app.route("/models/download", methods=["POST"])
def download_model():
    body       = request.get_json(silent=True) or {}
    model_name = body.get("model_name", "").strip()
    models_dir = body.get("models_dir", "models")

    if not model_name:
        return jsonify({"ok": False, "error": "model_name is required."}), 400

    ok, msg = translate_engine.start_download(model_name, models_dir)
    return jsonify({"ok": ok, "message": msg}), (200 if ok else 409)


@app.route("/models/download/status")
def download_status():
    return jsonify(translate_engine.get_download_status())


# tts endpoints

@app.route("/tts/voices")
def list_voices():
    try:
        voices = tts_engine.list_voices_sync()
        return jsonify({"ok": True, "voices": voices})
    except Exception as exc:
        log.error("Error listing TTS voices: %s", exc)
        return jsonify({"ok": False, "error": str(exc)}), 500


@app.route("/tts/languages")
def list_languages():
    try:
        languages = tts_engine.get_available_languages()
        return jsonify({"ok": True, "languages": languages})
    except Exception as exc:
        log.error("Error listing TTS languages: %s", exc)
        return jsonify({"ok": False, "error": str(exc)}), 500


@app.route("/tts/voice/auto", methods=["POST"])
def get_auto_voice():
    body = request.get_json(silent=True) or {}
    target_language = body.get("target_language", "en")
    
    try:
        voice = tts_engine.get_voice_for_language(target_language)
        return jsonify({"ok": True, "voice": voice})
    except Exception as exc:
        log.error("Error getting auto voice: %s", exc)
        return jsonify({"ok": False, "error": str(exc)}), 500


@app.route("/tts/devices")
def list_devices():
    try:
        devices = tts_engine.list_audio_devices()
        return jsonify({"ok": True, "devices": devices})
    except Exception as exc:
        log.error("Error listing audio devices: %s", exc)
        return jsonify({"ok": False, "error": str(exc)}), 500


@app.route("/tts/speak", methods=["POST"])
def speak():
    body = request.get_json(silent=True) or {}
    text = body.get("text", "").strip()
    voice = body.get("voice", "en-US-EmmaMultilingualNeural")
    device_id = body.get("device_id")  # none = default
    rate = body.get("rate", "+0%")
    volume = body.get("volume", "+0%")
    pitch = body.get("pitch", "+0Hz")
    
    if not text:
        return jsonify({"ok": False, "error": "No text provided."}), 400
    
    try:
        threading.Thread(
            target=tts_engine.speak_text,
            args=(text, voice, device_id, rate, volume, pitch),
            daemon=True,
        ).start()
        
        log.info("TTS started: voice=%s, text=%r", voice, text[:50])
        return jsonify({"ok": True, "message": "TTS started"})
    except Exception as exc:
        log.error("Error starting TTS: %s", exc)
        return jsonify({"ok": False, "error": str(exc)}), 500


@app.route("/tts/stop", methods=["POST"])
def stop_tts():
    try:
        tts_engine.stop_speaking()
        return jsonify({"ok": True, "message": "TTS stopped"})
    except Exception as exc:
        log.error("Error stopping TTS: %s", exc)
        return jsonify({"ok": False, "error": str(exc)}), 500


@app.route("/tts/status")
def tts_status():
    try:
        speaking = tts_engine.is_currently_speaking()
        return jsonify({"ok": True, "speaking": speaking})
    except Exception as exc:
        log.error("Error getting TTS status: %s", exc)
        return jsonify({"ok": False, "error": str(exc)}), 500

if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 5001
    log.info("FluxHelper server starting on port %d", port)
    app.run(host="127.0.0.1", port=port, debug=False, use_reloader=False)
