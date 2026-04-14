import gc
import logging
import os
import shutil
import threading
from pathlib import Path

log = logging.getLogger("translate_engine")

# ── Lazy library loading ────────────────────────────────────────────

_ct2: object = None
_tf: object = None
_HAS_LIBS: bool | None = None


def _ensure_libs() -> bool:
    global _ct2, _tf, _HAS_LIBS
    if _HAS_LIBS is not None:
        return _HAS_LIBS

    try:
        import ctranslate2 as ct2
        import transformers as tf_mod

        try:
            from transformers import AutoTokenizer, MarianConfig, MarianTokenizer
            try:
                AutoTokenizer.register(MarianConfig, slow_tokenizer_class=MarianTokenizer)
            except Exception:
                pass
        except ImportError:
            pass

        _ct2 = ct2
        _tf = tf_mod
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


# ── Model cache ─────────────────────────────────────────────────────

_cache: dict = {}
_cache_lock = threading.Lock()


# ── Download state ──────────────────────────────────────────────────

_dl_lock = threading.Lock()
_dl: dict = {
    "active": False,
    "model": "",
    "progress": "",
    "success": None,
    "error": "",
}


def _set_dl(**kw) -> None:
    with _dl_lock:
        _dl.update(kw)


def get_download_status() -> dict:
    with _dl_lock:
        return dict(_dl)


# ── Path helpers ────────────────────────────────────────────────────


def _ensure_dir(models_dir: str) -> Path:
    p = Path(models_dir)
    p.mkdir(parents=True, exist_ok=True)
    return p


def _hf_name(source_lang: str, target_lang: str) -> tuple[str, str, str]:
    src = source_lang.split("-")[0].lower()
    tgt = target_lang.split("-")[0].lower()
    return f"Helsinki-NLP/opus-mt-{src}-{tgt}", src, tgt


def _safe_dirname(hf_name: str) -> str:
    return hf_name.replace("/", "_")


# ── Model loading ───────────────────────────────────────────────────


def _load_tokenizer(model_path: Path):
    candidates = [
        str(model_path),
        model_path.name.replace("_", "/", 1),
    ]
    last_err: Exception | None = None

    for candidate in candidates:
        try:
            local_only = candidate == str(model_path)
            tokenizer = _tf.AutoTokenizer.from_pretrained(
                candidate, local_files_only=local_only,
            )
            log.info("Tokenizer loaded from '%s'.", candidate)
            return tokenizer
        except Exception as exc:
            last_err = exc
            log.debug("Tokenizer candidate '%s' failed: %s", candidate, exc)

    raise RuntimeError(f"Could not load tokenizer for {model_path.name}: {last_err}")


def _load(model_path: Path):
    key = str(model_path)
    with _cache_lock:
        if key in _cache:
            return _cache[key]

        if _cache:
            log.info("Evicting cached model to free memory.")
            _cache.clear()
            gc.collect()

        if not model_path.exists() or not (model_path / "model.bin").exists():
            raise RuntimeError(f"Model not found at {model_path}")

        log.info("Loading model from %s …", model_path)
        translator = _ct2.Translator(str(model_path), device="cpu", compute_type="int8")
        tokenizer = _load_tokenizer(model_path)

        _cache[key] = (translator, tokenizer)
        return _cache[key]


# ── Public translation API ──────────────────────────────────────────


def list_models(models_dir: str) -> list[str]:
    base = Path(models_dir)
    if not base.exists():
        return []
    return [
        item.name
        for item in sorted(base.iterdir())
        if item.is_dir() and (item / "model.bin").exists()
    ]


def translate(text: str, source_lang: str, target_lang: str, models_dir: str) -> str:
    if not _ensure_libs():
        raise RuntimeError(
            "CTranslate2 libraries are not installed. "
            "Run: pip install ctranslate2 transformers sentencepiece"
        )

    hf_name, src, tgt = _hf_name(source_lang, target_lang)
    model_path = _ensure_dir(models_dir) / _safe_dirname(hf_name)

    if not model_path.exists() or not (model_path / "model.bin").exists():
        raise RuntimeError(
            f"Model for {src}→{tgt} not found locally. "
            f"Download it first: Helsinki-NLP/opus-mt-{src}-{tgt}"
        )

    translator, tokenizer = _load(model_path)
    tokens = tokenizer.convert_ids_to_tokens(tokenizer.encode(text))
    results = translator.translate_batch([tokens])
    out_tokens = results[0].hypotheses[0]
    out_ids = tokenizer.convert_tokens_to_ids(out_tokens)
    return tokenizer.decode(out_ids, skip_special_tokens=True)


# ── Model download ──────────────────────────────────────────────────


def _copy_tokenizer_files(source_dir: str, target_dir: str) -> None:
    if not os.path.isdir(source_dir):
        return
    for fname in os.listdir(source_dir):
        if fname.endswith((".json", ".txt", ".spm", ".model")) and not fname.startswith("model"):
            src_f = os.path.join(source_dir, fname)
            dst_f = os.path.join(target_dir, fname)
            if not os.path.exists(dst_f):
                shutil.copy2(src_f, dst_f)


def _download_worker(hf_name: str, models_dir: str) -> None:
    _set_dl(active=True, model=hf_name, progress="Preparing download...", success=None, error="")

    if not _ensure_libs():
        _set_dl(active=False, success=False, error="CTranslate2 libraries not installed.")
        return

    base = _ensure_dir(models_dir)
    target = base / _safe_dirname(hf_name)

    try:
        try:
            import sentencepiece  # noqa: F401
        except ImportError:
            _set_dl(
                active=False, success=False,
                error="Missing 'sentencepiece'. Install: pip install sentencepiece",
            )
            return

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
            log.warning("huggingface_hub not installed; ctranslate2 will fetch directly.")
        except Exception as exc:
            log.warning("huggingface_hub download failed (%s); trying ctranslate2 fallback.", exc)

        _set_dl(progress="Converting to CTranslate2 format...")
        converter = _ct2.converters.TransformersConverter(source)
        converter.convert(str(target), force=True)

        _set_dl(progress="Copying tokenizer files...")
        _copy_tokenizer_files(source, str(target))

        log.info("Model %s installed to %s.", hf_name, target)
        _set_dl(active=False, success=True, progress="Installation complete.", error="")

    except Exception as exc:
        err = str(exc)
        if any(k in err for k in ("Repository Not Found", "401 Client Error", "valid model identifier")):
            err = (
                f"Model '{hf_name}' was not found on HuggingFace. "
                "Check the name — use format 'Helsinki-NLP/opus-mt-src-tgt'."
            )
        log.error("Download/install failed: %s", err)
        _set_dl(active=False, success=False, progress="Failed.", error=err)


def start_download(hf_name: str, models_dir: str) -> tuple[bool, str]:
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
