import threading
import logging

log = logging.getLogger("state")

_status_lock = threading.Lock()
_stop_signal = threading.Event()
_active_session_id = 0
_status_snapshot: dict = {"state": "idle", "text": "", "is_error": False, "is_final": True}

def get_status() -> dict:
    with _status_lock:
        return dict(_status_snapshot)


def update_status(state: str, text: str, *, is_error: bool = False, is_final: bool = False) -> None:
    with _status_lock:
        _status_snapshot.update(state=state, text=text, is_error=is_error, is_final=is_final)
    log.info("[%s] %s", state, text)

def get_active_session_id() -> int:
    with _status_lock:
        return _active_session_id


def begin_new_session() -> int:
    global _active_session_id
    with _status_lock:
        _active_session_id += 1
        return _active_session_id

def get_stop_signal() -> threading.Event:
    return _stop_signal


def reset_stop_signal() -> threading.Event:
    global _stop_signal
    _stop_signal = threading.Event()
    return _stop_signal


def request_stop() -> None:
    _stop_signal.set()


def get() -> dict:
    return get_status()


def set(state: str, text: str, *, is_error: bool = False, is_final: bool = False) -> None:
    update_status(state, text, is_error=is_error, is_final=is_final)


def get_session_id() -> int:
    return get_active_session_id()


def new_session() -> int:
    return begin_new_session()


def get_stop_event() -> threading.Event:
    return get_stop_signal()


def reset_stop_event() -> threading.Event:
    return reset_stop_signal()


def set_stop() -> None:
    request_stop()
