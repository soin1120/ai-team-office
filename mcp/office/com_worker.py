# COM 호출 전용 스레드: 한/글·PowerPoint COM 객체는 만든 스레드에서만 안정적으로 쓸 수 있어서
# 모든 도구 호출을 이 스레드 하나로 모은다. pyhwpx 등이 print 해도 MCP 통신(stdout)이 깨지지 않게 stderr로 돌린다.
import contextlib
import functools
import sys
from concurrent.futures import ThreadPoolExecutor

import pythoncom
import pywintypes

_pool = ThreadPoolExecutor(max_workers=1, initializer=pythoncom.CoInitialize, thread_name_prefix="com")


def _com_error_text(e):
    if isinstance(e, pywintypes.com_error):
        hr, msg, exc, _ = e.args
        detail = exc[2] if exc and len(exc) > 2 and exc[2] else ""
        return f"COM 오류 {hr}: {msg} {detail}".strip()
    return f"{type(e).__name__}: {e}"


def on_com_thread(fn):
    @functools.wraps(fn)
    def wrapper(*args, **kwargs):
        def run():
            with contextlib.redirect_stdout(sys.stderr):
                try:
                    return fn(*args, **kwargs)
                except Exception as e:  # COM 오류 메시지를 읽을 수 있는 문장으로 바꿔서 돌려준다
                    raise RuntimeError(_com_error_text(e)) from e
        return _pool.submit(run).result(timeout=300)
    return wrapper
