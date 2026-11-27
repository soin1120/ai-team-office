# PowerPoint MCP 서버: 실행 중인 PowerPoint에 COM으로 붙어서 열린 발표자료를 읽고 고친다.
# 닫힌 .pptx 파일은 PowerPoint 없이 python-pptx로 읽는다. 저장은 사용자가 요청했을 때만 한다.
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from mcp.server.mcpserver import MCPServer

from com_worker import on_com_thread

server = MCPServer(
    "powerpoint",
    instructions=(
        "실행 중인 Microsoft PowerPoint를 조작한다. 사용자가 편집 중인 발표자료일 수 있으니 저장(ppt_save)은 사용자가 요청할 때만 한다. "
        "슬라이드 번호와 도형 번호는 1부터 센다. 화면 모양을 확인하려면 ppt_export로 슬라이드를 PNG로 내보내 본다. "
        "닫힌 .pptx 파일을 읽기만 할 때는 PowerPoint를 띄우지 않는 ppt_read_file을 쓴다."
    ),
)


def _app():
    import win32com.client  # 시작할 때 불러오면 안내 문구가 stdout(MCP 통신)에 섞여서 COM 스레드에서 불러온다

    try:
        app = win32com.client.GetActiveObject("PowerPoint.Application")
    except Exception:
        app = win32com.client.Dispatch("PowerPoint.Application")
    app.Visible = -1  # msoTrue: 창이 보여야 열기·편집이 된다
    return app


def _pres(presentation: str = ""):
    """presentation: 비우면 현재 활성 발표자료, 숫자면 목록 순번(1부터), 글자면 파일 이름 일부로 찾는다."""
    app = _app()
    if app.Presentations.Count == 0:
        raise RuntimeError("열린 발표자료가 없습니다. ppt_open으로 먼저 여세요.")
    if not presentation:
        return app.ActivePresentation
    if presentation.isdigit():
        return app.Presentations(int(presentation))
    key = presentation.lower()
    for p in app.Presentations:
        if key in p.FullName.lower():
            return p
    raise RuntimeError(f"'{presentation}' 발표자료를 찾지 못했습니다.")


def _shape(slide, shape: str):
    if shape.isdigit():
        return slide.Shapes(int(shape))
    for s in slide.Shapes:
        if s.Name == shape:
            return s
    raise RuntimeError(f"슬라이드 {slide.SlideIndex}에 '{shape}' 도형이 없습니다.")


def _text_ranges(shape):
    """도형 안의 글상자·표 칸·그룹 안 글상자를 모두 돌려준다."""
    if shape.HasTextFrame and shape.TextFrame.HasText:
        yield shape.TextFrame.TextRange
    if shape.HasTable:
        t = shape.Table
        for r in range(1, t.Rows.Count + 1):
            for c in range(1, t.Columns.Count + 1):
                tf = t.Cell(r, c).Shape.TextFrame
                if tf.HasText:
                    yield tf.TextRange
    if shape.Type == 6:  # msoGroup
        for s in shape.GroupItems:
            yield from _text_ranges(s)


def _shape_info(s):
    info = {"index": s.ZOrderPosition, "name": s.Name, "type": s.Type,
            "left": round(s.Left), "top": round(s.Top), "width": round(s.Width), "height": round(s.Height)}
    texts = [tr.Text.replace("\r", "\n") for tr in _text_ranges(s)]
    if texts:
        info["text"] = "\n".join(texts)
    return info


@server.tool()
@on_com_thread
def ppt_list_presentations() -> list[dict]:
    """PowerPoint에 열려 있는 발표자료 목록(순번, 경로, 저장 여부, 슬라이드 수)을 돌려준다."""
    app = _app()
    return [{"index": i, "path": p.FullName, "saved": bool(p.Saved), "slides": p.Slides.Count}
            for i, p in enumerate(app.Presentations, start=1)]


@server.tool()
@on_com_thread
def ppt_open(path: str) -> dict:
    """.pptx 파일을 PowerPoint에서 연다. 이미 열려 있으면 그 창을 앞으로 가져온다."""
    path = os.path.abspath(path)
    if not os.path.exists(path):
        raise FileNotFoundError(path)
    app = _app()
    for p in app.Presentations:
        if os.path.normcase(p.FullName) == os.path.normcase(path):
            p.Windows(1).Activate()
            return {"opened": False, "already_open": p.FullName, "slides": p.Slides.Count}
    p = app.Presentations.Open(path, False, False, True)
    return {"opened": True, "path": p.FullName, "slides": p.Slides.Count}


@server.tool()
@on_com_thread
def ppt_get_slides(presentation: str = "", start: int = 1, end: int = 0, include_notes: bool = True) -> list[dict]:
    """슬라이드별 도형 목록(번호, 이름, 위치·크기 pt, 글자)과 발표자 노트를 돌려준다. end=0이면 마지막 슬라이드까지."""
    p = _pres(presentation)
    end = end or p.Slides.Count
    out = []
    for i in range(start, end + 1):
        sl = p.Slides(i)
        item = {"slide": i, "layout": sl.CustomLayout.Name, "shapes": [_shape_info(s) for s in sl.Shapes]}
        if include_notes:
            try:
                item["notes"] = sl.NotesPage.Shapes.Placeholders(2).TextFrame.TextRange.Text
            except Exception:
                pass
        out.append(item)
    return out


@server.tool()
@on_com_thread
def ppt_set_text(slide: int, shape: str, text: str, presentation: str = "") -> str:
    """도형 글자를 통째로 바꾼다. shape는 도형 이름 또는 번호(ppt_get_slides의 index).
    첫 글자의 서식이 전체에 적용되므로 일부 단어만 바꿀 때는 ppt_replace_text를 쓴다."""
    s = _shape(_pres(presentation).Slides(slide), shape)
    s.TextFrame.TextRange.Text = text.replace("\n", "\r")
    return "ok"


@server.tool()
@on_com_thread
def ppt_replace_text(find: str, replace: str, presentation: str = "", slide: int = 0) -> dict:
    """글자를 찾아 바꾼다(서식 유지, 표·그룹 안 포함). slide=0이면 모든 슬라이드. 바꾼 개수를 돌려준다."""
    p = _pres(presentation)
    slides = [p.Slides(slide)] if slide else list(p.Slides)
    count = 0
    for sl in slides:
        for s in sl.Shapes:
            for tr in _text_ranges(s):
                after = 0
                r = tr.Replace(find, replace, after)
                while r is not None:
                    count += 1
                    after = r.Start + r.Length - 1
                    r = tr.Replace(find, replace, after)
    return {"replaced": count}


@server.tool()
@on_com_thread
def ppt_add_slide(after: int, layout_from_slide: int = 0, presentation: str = "") -> dict:
    """after번 슬라이드 뒤에 새 슬라이드를 넣는다. layout_from_slide를 주면 그 슬라이드와 같은 레이아웃을 쓴다."""
    p = _pres(presentation)
    layout = p.Slides(layout_from_slide or max(after, 1)).CustomLayout
    sl = p.Slides.AddSlide(after + 1, layout)
    return {"slide": sl.SlideIndex, "layout": layout.Name}


@server.tool()
@on_com_thread
def ppt_duplicate_slide(slide: int, presentation: str = "") -> dict:
    """slide번 슬라이드를 복제해 바로 뒤에 넣는다."""
    r = _pres(presentation).Slides(slide).Duplicate()
    return {"new_slide": r.SlideIndex}


@server.tool()
@on_com_thread
def ppt_insert_picture(slide: int, path: str, left: float, top: float, width: float = -1, height: float = -1,
                       presentation: str = "") -> dict:
    """그림을 슬라이드에 넣는다. 위치·크기 단위는 pt(1cm ≈ 28.35pt). width/height를 -1로 두면 원본 비율 유지."""
    path = os.path.abspath(path)
    if not os.path.exists(path):
        raise FileNotFoundError(path)
    s = _pres(presentation).Slides(slide).Shapes.AddPicture(path, False, True, left, top, width, height)
    return _shape_info(s)


@server.tool()
@on_com_thread
def ppt_export(out_path: str, kind: str = "png", slide: int = 0, width_px: int = 1600, presentation: str = "") -> dict:
    """발표자료를 내보낸다. 열린 파일의 저장 상태는 바뀌지 않는다.
    kind=png: slide번 슬라이드(0이면 전체를 out_path 폴더에 slide_N.png)를 그림으로. kind=pdf: 전체를 PDF로."""
    p = _pres(presentation)
    out_path = os.path.abspath(out_path)
    if kind == "pdf":
        p.ExportAsFixedFormat(out_path, 2)  # ppFixedFormatTypePDF
        return {"pdf": out_path}
    h = round(width_px * p.PageSetup.SlideHeight / p.PageSetup.SlideWidth)
    if slide:
        p.Slides(slide).Export(out_path, "PNG", width_px, h)
        return {"png": [out_path]}
    os.makedirs(out_path, exist_ok=True)
    files = []
    for sl in p.Slides:
        f = os.path.join(out_path, f"slide_{sl.SlideIndex}.png")
        sl.Export(f, "PNG", width_px, h)
        files.append(f)
    return {"png": files}


@server.tool()
@on_com_thread
def ppt_save(presentation: str = "", path: str = "") -> dict:
    """발표자료를 저장한다. 사용자가 저장을 요청했을 때만 쓴다. path를 비우면 현재 파일에 덮어쓰고, 주면 다른 이름으로 저장한다."""
    p = _pres(presentation)
    if path:
        p.SaveAs(os.path.abspath(path))
    else:
        p.Save()
    return {"saved": p.FullName}


@server.tool()
def ppt_read_file(path: str) -> list[dict]:
    """닫혀 있는 .pptx 파일을 PowerPoint 없이 읽는다. 슬라이드별 도형 이름과 글자, 발표자 노트를 돌려준다."""
    from pptx import Presentation

    prs = Presentation(path)
    out = []
    for i, sl in enumerate(prs.slides, start=1):
        shapes = []
        for s in sl.shapes:
            item = {"name": s.name}
            if s.has_text_frame and s.text_frame.text.strip():
                item["text"] = s.text_frame.text
            elif getattr(s, "has_table", False) and s.has_table:
                item["table"] = [[c.text for c in row.cells] for row in s.table.rows]
            shapes.append(item)
        page = {"slide": i, "shapes": shapes}
        if sl.has_notes_slide and sl.notes_slide.notes_text_frame.text.strip():
            page["notes"] = sl.notes_slide.notes_text_frame.text
        out.append(page)
    return out


if __name__ == "__main__":
    server.run("stdio")
