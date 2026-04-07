"""
translate_engine.py — Offline translation using Helsinki-NLP / CTranslate2.

Models are downloaded from HuggingFace (Helsinki-NLP/opus-mt-*) and stored
locally in CTranslate2 format.  A lightweight in-process cache keeps the
most-recently-used model resident in RAM so repeated translations do not
reload from disk on every call.

Public API
----------
list_models(models_dir)               → list[str]
translate(text, src, tgt, models_dir) → str
start_download(hf_name, models_dir)   → (ok: bool, msg: str)
get_download_status()                 → dict
"""

import gc
import logging
import os
import shutil
import threading
from pathlib import Path

log = logging.getLogger("translate_engine")

# ── Lazy library imports ───────────────────────────────────────────────────────
# ctranslate2 and transformers are optional; the server starts fine without
# them — it will just return errors for translation requests.

_ct2: object      = None
_tf:  object      = None
_HAS_LIBS: bool | None = None


def _ensure_libs() -> bool:
    """Import ctranslate2 / transformers once; cache the result."""
    global _ct2, _tf, _HAS_LIBS
    if _HAS_LIBS is not None:
        return _HAS_LIBS

    try:
        import ctranslate2 as ct2
        import transformers as tf_mod

        # Register MarianTokenizer so AutoTokenizer resolves Helsinki models
        try:
            from transformers import AutoTokenizer, MarianConfig, MarianTokenizer

            try:
                AutoTokenizer.register(MarianConfig, slow_tokenizer_class=MarianTokenizer)
            except Exception:
                pass
        except ImportError:
            pass

        _ct2 = ct2
        _tf  = tf_mod
        _HAS_LIBS = True
        log.info("CTranslate2 %s + transformers loaded.", ct2.__version__)

    except ImportError as exc:
        _HAS_LIBS = False
        log.warning(
            "CTranslate2 not available (%s). "
            "Install with: pip install ctranslate2 transformers sentencepiece huggingface_hub",
            exc,
        )

    return _HAS_LIBS  # type: ignore[return-value]


# ── In-memory model cache ──────────────────────────────────────────────────────
# Stores at most one loaded model to keep RAM usage low.
# key: str(model_path) → (Translator, AutoTokenizer)

_cache: dict = {}
_cache_lock = threading.Lock()


# ── Download state ─────────────────────────────────────────────────────────────

_dl_lock = threading.Lock()
_dl: dict = {
    "active":   False,
    "model":    "",
    "progress": "",
    "success":  None,   # None = not finished; True / False = final result
    "error":    "",
}


def _set_dl(**kw) -> None:
    with _dl_lock:
        _dl.update(kw)


def get_download_status() -> dict:
    """Thread-safe snapshot of the current download state."""
    with _dl_lock:
        return dict(_dl)


# ── Internal helpers ───────────────────────────────────────────────────────────

def _ensure_dir(models_dir: str) -> Path:
    p = Path(models_dir)
    p.mkdir(parents=True, exist_ok=True)
    return p


def _hf_name(source_lang: str, target_lang: str) -> tuple[str, str, str]:
    """Return (HuggingFace repo id, src_code, tgt_code)."""
    src = source_lang.split("-")[0].lower()
    tgt = target_lang.split("-")[0].lower()
    return f"Helsinki-NLP/opus-mt-{src}-{tgt}", src, tgt


def _safe_dirname(hf_name: str) -> str:
    """Convert 'Helsinki-NLP/opus-mt-pl-en' → 'Helsinki-NLP_opus-mt-pl-en'."""
    return hf_name.replace("/", "_")


# ── Model loading (cached) ─────────────────────────────────────────────────────

def _load(model_path: Path):
    """Load (or return cached) CTranslate2 translator + tokenizer.

    Only one model is kept in RAM at a time.  Loading a new one evicts the old.
    """
    key = str(model_path)
    with _cache_lock:
        if key in _cache:
            return _cache[key]

        # Evict the previous model to free GPU/CPU RAM
        if _cache:
            log.info("Evicting cached model to free memory.")
            _cache.clear()
            gc.collect()

        if not model_path.exists() or not (model_path / "model.bin").exists():
            raise RuntimeError(f"Model not found at {model_path}")

        log.info("Loading model from %s …", model_path)
        translator = _ct2.Translator(str(model_path), device="cpu", compute_type="int8")  # type: ignore[union-attr]

        # Try loading the tokenizer from local dir first, then from HF hub
        candidates = [
            str(model_path),
            # e.g.  Helsinki-NLP_opus-mt-pl-en  →  Helsinki-NLP/opus-mt-pl-en
            model_path.name.replace("_", "/", 1),
        ]
        tokenizer = None
        last_err: Exception | None = None
        for candidate in candidates:
            try:
                local_only = candidate == str(model_path)
                tokenizer = _tf.AutoTokenizer.from_pretrained(  # type: ignore[union-attr]
                    candidate, local_files_only=local_only
                )
                log.info("Tokenizer loaded from '%s'.", candidate)
                break
            except Exception as exc:
                last_err = exc
                log.debug("Tokenizer candidate '%s' failed: %s", candidate, exc)

        if tokenizer is None:
            raise RuntimeError(
                f"Could not load tokenizer for {model_path.name}: {last_err}"
            )

        _cache[key] = (translator, tokenizer)
        return _cache[key]


# ── Public API ─────────────────────────────────────────────────────────────────

def list_models(models_dir: str) -> list[str]:
    """Return the directory names of all locally converted models."""
    base = Path(models_dir)
    if not base.exists():
        return []
    return [
        item.name
        for item in sorted(base.iterdir())
        if item.is_dir() and (item / "model.bin").exists()
    ]


def translate(text: str, source_lang: str, target_lang: str, models_dir: str) -> str:
    """Translate *text* offline.

    Parameters
    ----------
    text        : Input text to translate.
    source_lang : Source language code, e.g. ``"pl"`` or ``"pl-PL"``.
    target_lang : Target language code, e.g. ``"en"``.
    models_dir  : Path to the directory that holds downloaded models.

    Raises
    ------
    RuntimeError
        If libraries are not installed or the required model is missing.
    """
    if not _ensure_libs():
        raise RuntimeError(
            "CTranslate2 libraries are not installed.  "
            "Run: pip install ctranslate2 transformers sentencepiece"
        )

    hf_name, src, tgt = _hf_name(source_lang, target_lang)
    model_path = _ensure_dir(models_dir) / _safe_dirname(hf_name)

    if not model_path.exists() or not (model_path / "model.bin").exists():
        raise RuntimeError(
            f"Model for {src}→{tgt} not found locally.  "
            f"Download it first: Helsinki-NLP/opus-mt-{src}-{tgt}"
        )

    translator, tokenizer = _load(model_path)
    tokens     = tokenizer.convert_ids_to_tokens(tokenizer.encode(text))
    results    = translator.translate_batch([tokens])
    out_tokens = results[0].hypotheses[0]
    out_ids    = tokenizer.convert_tokens_to_ids(out_tokens)
    return tokenizer.decode(out_ids)


# ── Model download ─────────────────────────────────────────────────────────────

def _download_worker(hf_name: str, models_dir: str) -> None:
    """Background thread: download + convert a HuggingFace model."""
    _set_dl(active=True, model=hf_name, progress="Preparing download...", success=None, error="")

    if not _ensure_libs():
        _set_dl(
            active=False, success=False,
            error="CTranslate2 libraries not installed.",
        )
        return

    base   = _ensure_dir(models_dir)
    target = base / _safe_dirname(hf_name)

    try:
        # Verify sentencepiece is available (required for Marian tokenizer)
        try:
            import sentencepiece  # noqa: F401
        except ImportError:
            _set_dl(
                active=False, success=False,
                error="Missing 'sentencepiece'.  Install: pip install sentencepiece",
            )
            return

        # Step 1 — download raw weights from HuggingFace
        source: str = hf_name
        try:
            from huggingface_hub import snapshot_download

            _set_dl(progress=f"Downloading {hf_name} from HuggingFace...")
            source = snapshot_download(
                repo_id=hf_name,
                allow_patterns=["*.bin", "*.json", "*.spm", "*.txt", "*.safetensors"],
            )
            log.info("Raw model downloaded to %s", source)
        except ImportError:
            log.warning(
                "huggingface_hub not installed; ctranslate2 will fetch directly."
            )
        except Exception as exc:
            log.warning("huggingface_hub download failed (%s); trying ctranslate2 fallback.", exc)

        # Step 2 — convert to CTranslate2 format
        _set_dl(progress="Converting to CTranslate2 format...")
        converter = _ct2.converters.TransformersConverter(source)  # type: ignore[union-attr]
        converter.convert(str(target), force=True)

        # Step 3 — copy tokenizer sidecar files (source.spm, tokenizer.json, …)
        _set_dl(progress="Copying tokenizer files...")
        if os.path.isdir(source):
            for fname in os.listdir(source):
                if fname.endswith((".json", ".txt", ".spm", ".model")) and not fname.startswith("model"):
                    src_f = os.path.join(source, fname)
                    dst_f = os.path.join(str(target), fname)
                    if not os.path.exists(dst_f):
                        shutil.copy2(src_f, dst_f)

        log.info("Model %s installed to %s.", hf_name, target)
        _set_dl(active=False, success=True, progress="Installation complete.", error="")

    except Exception as exc:
        err = str(exc)
        if any(k in err for k in ("Repository Not Found", "401 Client Error", "valid model identifier")):
            err = (
                f"Model '{hf_name}' was not found on HuggingFace.  "
                "Check the name — use format 'Helsinki-NLP/opus-mt-src-tgt'."
            )
        log.error("Download/install failed: %s", err)
        _set_dl(active=False, success=False, progress="Failed.", error=err)


def start_download(hf_name: str, models_dir: str) -> tuple[bool, str]:
    """Start a background model download/conversion.

    Returns ``(True, info_msg)`` if started, or ``(False, reason)`` if another
    download is already running or the name is invalid.
    """
    hf_name = hf_name.strip()
    if not hf_name:
        return False, "model_name must not be empty."

    with _dl_lock:
        if _dl["active"]:
            return False, f"Download of '{_dl['model']}' is already in progress."

    threading.Thread(
        target=_download_worker,
        args=(hf_name, models_dir),
        daemon=True,
    ).start()
    return True, f"Download started for {hf_name}."
