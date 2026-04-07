import sys
import threading
import time
import logging

from flask import Flask, request, jsonify

import state
import stt_engine
import translate_engine

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

if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 5001
    log.info("FluxHelper server starting on port %d", port)
    app.run(host="127.0.0.1", port=port, debug=False, use_reloader=False)
