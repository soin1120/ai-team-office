# 한/글(HWP) MCP 서버: 실행 중인 한/글 창에 붙어서(pyhwpx) 문서를 읽고 고친다.
# 닫힌 파일은 한/글 없이 ~/.claude/tools/hwp.py 로 읽는다. 저장은 사용자가 요청했을 때만 한다.
import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
# 한/글 2020은 WINDIR이 없으면 글꼴 폴더 경로를 못 만들어 시작하자마자 꺼진다(.NET UriFormatException)
for _k in ("WINDIR", "SystemRoot"):
    os.environ.setdefault(_k, os.environ.get("SystemRoot") or os.environ.get("WINDIR") or r"C:\Windows")

from mcp.server.mcpserver import MCPServer

from com_worker import on_com_thread

# 같은 폴더의 hwp.py를 먼저 쓰고(AI 팀 사무실이 설치한 경우), 없으면 ~/.claude/tools/hwp.py
HWP_PY = os.path.join(os.path.dirname(os.path.abspath(__file__)), "hwp.py")
if not os.path.exists(HWP_PY):
    HWP_PY = os.path.join(os.path.expanduser("~"), ".claude", "tools", "hwp.py")

server = MCPServer(
    "hwp",
    instructions=(
        "실행 중인 한컴 한/글을 조작한다. 사용자가 편집 중인 문서일 수 있으니 저장(hwp_save)은 사용자가 요청할 때만 하고, "
        "닫힌 .hwp 파일을 읽기만 할 때는 한/글을 띄우지 않는 hwp_read_file을 쓴다."
    ),
)

_hwp = None


def _hwp_running():
    import pythoncom

    ctx = pythoncom.CreateBindCtx(0)
    return any(m.GetDisplayName(ctx, m).startswith("!HwpObject.") for m in pythoncom.GetRunningObjectTable().EnumRunning())


def _launch_hwp():
    """COM 자동화 실행(hwp.exe -Automation)은 한/글 2020에서 글꼴 캐시 오류로 시작이 멈춰서, 일반 실행 후 붙는다."""
    import time
    import winreg

    clsid = winreg.QueryValue(winreg.HKEY_CLASSES_ROOT, r"HWPFrame.HwpObject\CLSID")
    cmd = ""
    for view in (winreg.KEY_WOW64_32KEY, winreg.KEY_WOW64_64KEY):  # 한/글은 32비트라 32비트 레지스트리에 등록된다
        try:
            with winreg.OpenKey(winreg.HKEY_CLASSES_ROOT, rf"CLSID\{clsid}\LocalServer32", 0, winreg.KEY_READ | view) as k:
                cmd = winreg.QueryValueEx(k, "")[0]
                break
        except OSError:
            pass
    if not cmd:
        raise RuntimeError("레지스트리에서 한/글 실행 파일 경로를 찾지 못했습니다. 한/글을 직접 실행한 뒤 다시 시도하세요.")
    exe = cmd.split(" -")[0].strip('"')
    # 서버의 자식으로 띄우면 MCP 서버가 끝날 때 한/글도 같이 꺼져서(저장 안 된 작업 손실) 탐색기를 통해 띄운다
    subprocess.Popen(["explorer.exe", exe])
    for _ in range(60):
        time.sleep(0.5)
        if _hwp_running():
            return
    raise RuntimeError("한/글을 실행했지만 30초 안에 연결되지 않았습니다. 한/글 창에 대화상자가 떠 있는지 확인하세요.")


def _app():
    """실행 중인 한/글에 붙는다. 없으면 새로 띄운다."""
    global _hwp
    if _hwp is not None:
        try:
            _hwp.hwp.XHwpDocuments.Count
            return _hwp
        except Exception:
            _hwp = None
    if not _hwp_running():
        _launch_hwp()
    from pyhwpx import Hwp
    _hwp = Hwp(visible=True)
    return _hwp


def _doc_info(docs, i):
    d = docs.Item(i)
    info = {"index": i}
    for key in ("FullName", "Path", "Modified"):
        try:
            info[key] = getattr(d, key)
        except Exception:
            pass
    return info


def _text(h):
    return h.hwp.GetTextFile("UNICODE", "") or ""


def _run_find(h, action, find, replace=None):
    """찾기(RepeatFind)·모두 바꾸기(AllReplace)를 직접 실행한다.
    pyhwpx의 find/find_replace_all은 먼저 FindDlg를 실행하는데, 한/글 2020에서 메뉴 검색칸 오류 창이 떠서 쓰지 않는다."""
    hwp = h.hwp
    hwp.SetMessageBoxMode(0x2FFF1)  # "n개를 바꿨습니다" 같은 알림 창을 자동으로 닫는다
    try:
        pset = hwp.HParameterSet.HFindReplace
        hwp.HAction.GetDefault(action, pset.HSet)
        pset.FindString = find
        if replace is not None:
            pset.ReplaceString = replace
            pset.ReplaceMode = 1
        pset.Direction = hwp.FindDir("AllDoc")
        pset.FindRegExp = 0
        pset.MatchCase = 1
        pset.FindType = 1
        pset.IgnoreMessage = 1
        return bool(hwp.HAction.Execute(action, pset.HSet))
    finally:
        hwp.SetMessageBoxMode(0xFFFFF)


@server.tool()
@on_com_thread
def hwp_list_documents() -> list[dict]:
    """한/글에 열려 있는 문서 목록(index, 파일 경로, 수정 여부)을 돌려준다."""
    docs = _app().hwp.XHwpDocuments
    return [_doc_info(docs, i) for i in range(docs.Count)]


@server.tool()
@on_com_thread
def hwp_open(path: str) -> dict:
    """HWP/HWPX 파일을 한/글에서 연다. 이미 문서가 열려 있으면 새 탭에 연다(기존 문서는 그대로 둔다)."""
    path = os.path.abspath(path)
    if not os.path.exists(path):
        raise FileNotFoundError(path)
    h = _app()
    docs = h.hwp.XHwpDocuments
    for i in range(docs.Count):
        if os.path.normcase(str(getattr(docs.Item(i), "FullName", ""))) == os.path.normcase(path):
            docs.Item(i).SetActive_XHwpDocument()
            return {"opened": False, "already_open_index": i}
    if not h.hwp.IsEmpty:
        h.add_tab()
    ok = h.open(path)
    return {"opened": bool(ok), "index": h.hwp.XHwpDocuments.Count - 1}


@server.tool()
@on_com_thread
def hwp_activate(index: int) -> dict:
    """hwp_list_documents의 index 문서를 활성 문서로 바꾼다. 이후 도구는 활성 문서에 작동한다."""
    docs = _app().hwp.XHwpDocuments
    docs.Item(index).SetActive_XHwpDocument()
    return _doc_info(docs, index)


@server.tool()
@on_com_thread
def hwp_get_text(max_chars: int = 60000) -> str:
    """활성 문서의 본문 텍스트 전체를 돌려준다(표 안 글자 포함). 길면 max_chars에서 자른다."""
    t = _text(_app())
    return t if len(t) <= max_chars else t[:max_chars] + f"\n...(총 {len(t)}자 중 {max_chars}자까지)"


@server.tool()
@on_com_thread
def hwp_find(text: str) -> bool:
    """활성 문서에서 text를 찾아 선택하고 커서를 그 위치로 옮긴다. 다음 호출은 그 다음 위치부터 찾는다."""
    return _run_find(_app(), "RepeatFind", text)


@server.tool()
@on_com_thread
def hwp_replace_all(find: str, replace: str) -> dict:
    """활성 문서 전체에서 find를 replace로 모두 바꾼다(서식 유지). 바꾸기 전·후 find 개수를 돌려준다. 저장은 하지 않는다."""
    h = _app()
    h.hwp.HAction.Run("Cancel")  # 블록이 선택돼 있으면 한/글은 선택 범위 안에서만 바꾼다
    before = _text(h).count(find)
    _run_find(h, "AllReplace", find, replace)
    after = _text(h).count(find)
    return {"found_before": before, "found_after": after}


@server.tool()
@on_com_thread
def hwp_insert_text(text: str) -> str:
    """현재 커서 위치에 text를 넣는다. 줄바꿈(\\n)은 문단 나눔으로 넣는다."""
    h = _app()
    h.hwp.HAction.Run("Cancel")  # 선택된 블록을 덮어쓰지 않게 선택을 푼다
    lines = text.split("\n")
    for i, line in enumerate(lines):
        if line:
            h.insert_text(line)
        if i < len(lines) - 1:
            h.hwp.HAction.Run("BreakPara")
    return "ok"


@server.tool()
@on_com_thread
def hwp_insert_picture(path: str, width_mm: float = 0, height_mm: float = 0) -> str:
    """현재 커서 위치에 그림을 글자처럼 넣는다. 크기를 0으로 두면 원본 크기(문서 폭을 넘으면 한/글 기본 처리)."""
    path = os.path.abspath(path)
    if not os.path.exists(path):
        raise FileNotFoundError(path)
    h = _app()
    if width_mm or height_mm:
        h.insert_picture(path, sizeoption=1, width=int(width_mm), height=int(height_mm))
    else:
        h.insert_picture(path)
    return "ok"


@server.tool()
@on_com_thread
def hwp_run_action(action: str) -> bool:
    """한/글 액션 ID를 실행한다. 예: MoveDocBegin, MoveDocEnd, MoveLineEnd, BreakPara, BreakPage, Undo, Redo, SelectAll, Copy, Paste.
    돌려주는 값은 한/글이 주는 값이라 커서 이동 액션은 성공해도 false일 수 있다."""
    return bool(_app().hwp.HAction.Run(action))


@server.tool()
@on_com_thread
def hwp_save(path: str = "", format: str = "HWP") -> dict:
    """활성 문서를 저장한다. 사용자가 저장을 요청했을 때만 쓴다.
    path를 비우면 현재 파일에 덮어쓴다. path를 주면 '다른 이름으로 저장'이라 열린 문서가 새 파일로 바뀐다.
    format=PDF는 열린 문서를 건드리지 않고 저장된 원본 파일을 새 탭에서 열어 PDF로 내보낸다(저장 안 된 변경이 있으면 거절)."""
    h = _app()
    if not path:
        return {"saved": bool(h.save())}
    path = os.path.abspath(path)
    if format.upper() != "PDF":
        return {"saved": bool(h.save_as(path, format)), "path": path}
    docs = h.hwp.XHwpDocuments
    cur = h.hwp.XHwpDocuments.Active_XHwpDocument
    src = cur.FullName
    if not src or cur.Modified:
        raise RuntimeError("저장되지 않은 변경이 있거나 저장된 적 없는 문서입니다. PDF로 바로 내보내면 열린 문서가 원래 파일과 연결이 끊기니, "
                           "먼저 사용자에게 저장 여부를 확인하세요.")
    h.add_tab()
    try:
        h.open(src)
        ok = h.save_as(path, "PDF")
    finally:
        h.hwp.XHwpDocuments.Active_XHwpDocument.Close(False)  # PDF용으로 연 탭만 저장 없이 닫는다
        cur.SetActive_XHwpDocument()
    return {"saved": bool(ok), "path": path, "documents": docs.Count}


@server.tool()
def hwp_read_file(path: str, mode: str = "text") -> str:
    """닫혀 있는 .hwp 파일을 한/글 없이 읽는다(HWP 5.0 형식).
    mode: text(본문 텍스트) | flow(텍스트 + 그림 위치 [그림 #N] 표시)."""
    if mode not in ("text", "flow"):
        raise ValueError("mode는 text 또는 flow")
    r = subprocess.run([sys.executable, HWP_PY, mode, path], capture_output=True, timeout=120,
                       env={**os.environ, "PYTHONIOENCODING": "utf-8"})
    if r.returncode != 0:
        raise RuntimeError(r.stderr.decode("utf-8", "replace")[-2000:])
    return r.stdout.decode("utf-8", "replace")


if __name__ == "__main__":
    server.run("stdio")
