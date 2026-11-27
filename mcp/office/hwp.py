# -*- coding: utf-8 -*-
"""
HWP 5.0(OLE) 문서 추출기 - 외부 라이브러리 없이 olefile 하나만 사용

사용법:
    python hwp.py text <file.hwp>              본문 텍스트만 출력
    python hwp.py flow <file.hwp>              본문 텍스트 + 그림 위치를 [그림 #N] 으로 표시
    python hwp.py img  <file.hwp> <outdir>     본문 이미지를 원본 해상도로 추출

flow 의 [그림 #N] 번호는 BinData 스트림 BIN{N:04X} 와 1:1 대응한다.
즉 "[그림 #18]" 이면 outdir/BIN0012.png 가 그 그림이다.
(HWP 그림 레코드의 BinItem ID 는 payload offset 72, UINT16)

문서가 표 중심이면 셀 텍스트가 문단 단위로 순서대로 나열된다.
"""
import olefile, zlib, struct, sys, os, io

HWPTAG_PARA_TEXT = 67
HWPTAG_SHAPE_COMPONENT_PICTURE = 85
BINITEM_ID_OFFSET = 72

INLINE  = {4, 5, 6, 7, 8, 9, 19, 20}
EXTEND  = {1, 2, 3, 11, 12, 14, 15, 16, 17, 18, 21, 22, 23}
CHARCTL = {0, 10, 13}


def _decode(payload):
    out, i, n = [], 0, len(payload)
    while i + 1 < n:
        c = struct.unpack_from('<H', payload, i)[0]
        if c in CHARCTL:
            if c == 13:
                out.append('\n')
            i += 2
        elif c in INLINE:
            if c == 9:
                out.append('\t')
            i += 16
        elif c in EXTEND:
            i += 16
        else:
            out.append(chr(c))
            i += 2
    return ''.join(out)


def _sections(path):
    f = olefile.OleFileIO(path)
    compressed = bool(f.openstream('FileHeader').read()[36] & 1)
    secs = [d for d in f.listdir() if d[0] == 'BodyText']
    secs.sort(key=lambda x: int(''.join(c for c in x[1] if c.isdigit()) or 0))
    for s in secs:
        raw = f.openstream(s).read()
        if compressed:
            raw = zlib.decompress(raw, -15)
        yield raw
    f.close()


def _records(data):
    i, n = 0, len(data)
    while i + 4 <= n:
        h = struct.unpack_from('<I', data, i)[0]
        tag = h & 0x3FF
        ln = (h >> 20) & 0xFFF
        i += 4
        if ln == 0xFFF:
            ln = struct.unpack_from('<I', data, i)[0]
            i += 4
        yield tag, data[i:i + ln]
        i += ln


def get_flow(path, with_images):
    out = []
    for sec in _sections(path):
        for tag, payload in _records(sec):
            if tag == HWPTAG_PARA_TEXT:
                t = _decode(payload).strip()
                if t:
                    out.append(t)
            elif with_images and tag == HWPTAG_SHAPE_COMPONENT_PICTURE and len(payload) >= BINITEM_ID_OFFSET + 2:
                bid = struct.unpack_from('<H', payload, BINITEM_ID_OFFSET)[0]
                out.append('[그림 #%d]' % bid)
    return '\n'.join(out)


def extract_images(path, outdir):
    os.makedirs(outdir, exist_ok=True)
    f = olefile.OleFileIO(path)
    compressed = bool(f.openstream('FileHeader').read()[36] & 1)
    n = 0
    for d in f.listdir():
        if d[0] == 'BinData':
            raw = f.openstream(d).read()
            if compressed:
                try:
                    raw = zlib.decompress(raw, -15)
                except Exception:
                    pass
            with open(os.path.join(outdir, d[1]), 'wb') as w:
                w.write(raw)
            print('%s  %d bytes' % (d[1], len(raw)))
            n += 1
    f.close()
    print('total %d' % n)


if __name__ == '__main__':
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)

    mode, path = sys.argv[1], sys.argv[2]
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

    if mode == 'text':
        print(get_flow(path, False))
    elif mode == 'flow':
        print(get_flow(path, True))
    elif mode == 'img':
        extract_images(path, sys.argv[3])
    else:
        print(__doc__)
        sys.exit(1)
