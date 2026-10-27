#!/usr/bin/env python3
"""PdxNext 应用图标生成器。

图形：石墨炭圆角方（squircle）底 + 金色「P」字标，P 的碗内计白（counter）里
放一枚金色播放三角 —— P = Pdx，三角 = 启动/Next。全部图形都由本文件里的常量决定，
没有外部矢量素材（本机没有 SVG 栅格化工具），所以本文件就是图标的唯一真理源。

输出（默认写进仓库）：
    src/PdxLauncher.App/Assets/AppIcon.ico    9 帧：16/20/24/32/40/48/64/128/256
    src/PdxLauncher.App/Assets/*.png          8 张 MS 模板同名素材（替换模板占位图）
    docs/app-icon.png                         深/浅底接触表（文档用）

用法：
    python tools\\gen-app-icon.py                    正式生成
    python tools\\gen-app-icon.py --preview          只写 .tmp/icon-preview/，不碰 Assets
    python tools\\gen-app-icon.py --preview --variant plain

变体：play（默认，碗内带金色播放三角）/ plain（纯 P 环）。

ICO 帧策略：<128 用 32 位 BMP（带 AND mask，兼容老 shell），>=128 用 PNG 帧；
容器手写，保证每一帧都是按该尺寸单独绘制/调光的，而不是从大图缩下来的。
"""

from __future__ import annotations

import argparse
import io
import math
import struct
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

# ---------------------------------------------------------------------------
# 画布与配色
# ---------------------------------------------------------------------------

#: 几何使用 1024x1024 的设计画布，渲染时按目标尺寸换算。
UNIT = 1024.0

#: 底色渐变（135°：左上亮、右下暗）。中间那个值就是 App.xaml 的 PdxWindowBackgroundBrush。
TILE_STOPS = (
    (0.00, (0x2E, 0x2E, 0x36)),
    (0.55, (0x1B, 0x1B, 0x1F)),
    (1.00, (0x0E, 0x0E, 0x12)),
)

#: 金色渐变（斜向：高光偏左上，暗部压右下）。
GOLD_STOPS = (
    (0.00, (0xFF, 0xE3, 0xA3)),
    (0.48, (0xF2, 0xC1, 0x4E)),
    (1.00, (0xC0, 0x8A, 0x2B)),
)

#: 浅色任务栏上的保命描边（白 13%），只在 >=32px 画。
EDGE_ALPHA = 33

#: 底色四角轻微压暗，避免大尺寸时一片死平。
VIGNETTE = 0.10

# ---------------------------------------------------------------------------
# 几何（1024 设计画布，全部以「单位」表示）
# ---------------------------------------------------------------------------

#: 圆角方：内缩 40（约 3.9%），圆角半径 210（约 22%）。<=24px 时内缩归零、圆角降到 20%。
TILE_INSET = 40.0
TILE_RADIUS = 210.0
TILE_INSET_SMALL = 0.0
TILE_RADIUS_SMALL_RATIO = 0.20

#: 「P」的骨架。帽高 560、竖笔宽 122、碗外高 380、碗环宽 82
#: （圆碗的计白补偿：环比竖笔细一点，跟字体排印里的做法一致）。
CAP_TOP = 232.0
CAP_BOTTOM = 792.0
STEM_X0 = 258.0
STEM_WIDTH = 122.0
BOWL_TOP = 232.0
BOWL_BOTTOM = 612.0
BOWL_RIGHT = 766.0
BOWL_RING = 82.0
STEM_CORNER = 8.0

#: 碗内计白里的播放三角（三个顶点 + 顶点圆角半径）；不参与计算计白，只做装饰。
PLAY_TRI = ((410.0, 352.0), (410.0, 492.0), (600.0, 422.0))
PLAY_CORNER = 20.0

#: 字标投影（仅 >=48px）：下移 6、模糊 18、黑 45%。
SHADOW_OFFSET = 6.0
SHADOW_BLUR = 18.0
SHADOW_ALPHA = 0.45

ICO_SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)

#: 方形 MS 模板素材（文件名 -> 边长）。全是当前模板占位图的同名替换。
SQUARE_ASSETS = (
    ("Square44x44Logo.scale-200.png", 88),
    ("Square150x150Logo.scale-200.png", 300),
    ("StoreLogo.png", 50),
    ("LockScreenLogo.scale-200.png", 48),
    ("Square44x44Logo.targetsize-24_altform-unplated.png", 24),
    ("Square44x44Logo.targetsize-48_altform-lightunplated.png", 48),
)

#: 宽/横版素材（文件名 -> 宽, 高, 字标高度）。底用纯 #1B1B1F，居中放字标。
WIDE_ASSETS = (
    ("SplashScreen.scale-200.png", 1240, 600, 300),
    ("Wide310x150Logo.scale-200.png", 620, 300, 150),
)

WIDE_BG = (0x1B, 0x1B, 0x1F, 0xFF)

#: 接触表底色：一行深、一行浅。
SHEET_DARK = (0x1B, 0x1B, 0x1F, 0xFF)
SHEET_LIGHT = (0xF3, 0xF3, 0xF3, 0xFF)


# ---------------------------------------------------------------------------
# numpy 上色工具
# ---------------------------------------------------------------------------


def _lerp_stops(t: np.ndarray, stops) -> np.ndarray:
    """按插值点表把 t（0..1）映射成 RGB float32 数组。"""
    ts = np.array([s[0] for s in stops], dtype=np.float32)
    cs = np.array([s[1] for s in stops], dtype=np.float32)
    out = np.empty(t.shape + (3,), dtype=np.float32)
    for c in range(3):
        out[..., c] = np.interp(t, ts, cs[:, c])
    return out


def _grid(canvas: int):
    yy, xx = np.mgrid[0:canvas, 0:canvas].astype(np.float32)
    return xx / max(canvas - 1, 1), yy / max(canvas - 1, 1)


def _linear_gradient(canvas: int, stops, direction=(1.0, 1.0)) -> np.ndarray:
    u, v = _grid(canvas)
    dx, dy = direction
    t = u * dx + v * dy
    t = (t - float(t.min())) / float(t.max() - t.min() + 1e-9)
    return _lerp_stops(t.astype(np.float32), stops)


def _vignette(canvas: int, strength: float) -> np.ndarray:
    u, v = _grid(canvas)
    x = (u - 0.5) * 2.0
    y = (v - 0.5) * 2.0
    r2 = np.clip((x * x + y * y) * 0.5, 0.0, 1.0)
    return 1.0 - strength * r2


# ---------------------------------------------------------------------------
# 蒙版绘制工具（全部在超采样画布上画，最后 LANCZOS 缩到目标尺寸）
# ---------------------------------------------------------------------------


def _new_mask(canvas: int) -> Image.Image:
    return Image.new("L", (canvas, canvas), 0)


def _rounded(mask: Image.Image, k: float, box, radius: float, fill: int = 255) -> None:
    draw = ImageDraw.Draw(mask)
    x0, y0, x1, y1 = (box[0] * k, box[1] * k, box[2] * k, box[3] * k)
    # ImageDraw 要求 radius 不超过短边的一半，否则会抛错。
    r = min(max(radius * k, 0.0), (x1 - x0) / 2.0, (y1 - y0) / 2.0)
    draw.rounded_rectangle((x0, y0, x1, y1), radius=r, fill=fill)


def _ellipse(mask: Image.Image, k: float, cx: float, cy: float, radius: float, fill: int = 255) -> None:
    draw = ImageDraw.Draw(mask)
    draw.ellipse(
        ((cx - radius) * k, (cy - radius) * k, (cx + radius) * k, (cy + radius) * k),
        fill=fill,
    )


def _inset_polygon(points, radius: float):
    """把多边形顶点沿角平分线内缩 radius/sin(θ/2)，结果即各角的圆弧圆心。"""
    out = []
    n = len(points)
    for i in range(n):
        prev = points[(i - 1) % n]
        cur = points[i]
        nxt = points[(i + 1) % n]
        v1 = np.array([prev[0] - cur[0], prev[1] - cur[1]], dtype=np.float64)
        v2 = np.array([nxt[0] - cur[0], nxt[1] - cur[1]], dtype=np.float64)
        v1 /= max(np.linalg.norm(v1), 1e-9)
        v2 /= max(np.linalg.norm(v2), 1e-9)
        bis = v1 + v2
        norm = np.linalg.norm(bis)
        if norm < 1e-6:
            out.append(cur)
            continue
        bis /= norm
        cos_half = float(np.clip(float(np.dot(bis, v1)), -1.0, 1.0))
        sin_half = max((1.0 - cos_half * cos_half) ** 0.5, 1e-4)
        d = radius / sin_half
        out.append((cur[0] + bis[0] * d, cur[1] + bis[1] * d))
    return out


def _inward_normals(points):
    """每条边的朝向质心的单位法线（屏幕坐标，y 向下）。"""
    count = len(points)
    cx = sum(p[0] for p in points) / count
    cy = sum(p[1] for p in points) / count
    normals = []
    for i in range(count):
        ax, ay = points[i]
        bx, by = points[(i + 1) % count]
        dx, dy = bx - ax, by - ay
        length = max((dx * dx + dy * dy) ** 0.5, 1e-9)
        nx, ny = -dy / length, dx / length
        mx, my = (ax + bx) / 2.0, (ay + by) / 2.0
        if (cx - mx) * nx + (cy - my) * ny < 0.0:
            nx, ny = -nx, -ny
        normals.append((nx, ny))
    return normals


def _rounded_polygon(points, radius: float, steps: int = 20):
    """精确圆角多边形：每个角替换成与相邻两边相切的圆弧。"""
    count = len(points)
    normals = _inward_normals(points)
    centers = _inset_polygon(points, radius)
    outline = []
    for i in range(count):
        cur = points[i]
        center = centers[i]
        prev_normal = normals[(i - 1) % count]
        next_normal = normals[i]
        start = (center[0] - prev_normal[0] * radius, center[1] - prev_normal[1] * radius)
        end = (center[0] - next_normal[0] * radius, center[1] - next_normal[1] * radius)
        a0 = math.atan2(start[1] - center[1], start[0] - center[0])
        a1 = math.atan2(end[1] - center[1], end[0] - center[0])
        delta = (a1 - a0 + math.pi) % (2.0 * math.pi) - math.pi
        # 弧必须朝原顶点方向鼓出，否则取反向的小弧。
        mid = a0 + delta * 0.5
        toward = math.atan2(cur[1] - center[1], cur[0] - center[0])
        if math.cos(mid - toward) < 0.0:
            a0, delta = a1, -delta
        for s in range(steps + 1):
            angle = a0 + delta * (s / steps)
            outline.append(
                (center[0] + radius * math.cos(angle), center[1] + radius * math.sin(angle))
            )
    return outline


def _rounded_triangle_mask(canvas: int, k: float, points, corner: float) -> Image.Image:
    mask = _new_mask(canvas)
    draw = ImageDraw.Draw(mask)
    draw.polygon([(p[0] * k, p[1] * k) for p in _rounded_polygon(points, corner)], fill=255)
    return mask


def _alpha(mask: Image.Image) -> np.ndarray:
    return np.asarray(mask, dtype=np.float32) / 255.0


def _rgba_layer(rgb: np.ndarray, alpha: np.ndarray) -> Image.Image:
    a = np.clip(alpha * 255.0, 0.0, 255.0).astype(np.uint8)
    arr = np.dstack([np.clip(rgb, 0.0, 255.0).astype(np.float32), a.astype(np.float32)]).astype(np.uint8)
    return Image.fromarray(arr, "RGBA")


# ---------------------------------------------------------------------------
# 单个尺寸的渲染
# ---------------------------------------------------------------------------


def _weight(size: int) -> float:
    """小尺寸光学加粗：照抄大图会糊掉细笔画。"""
    if size <= 24:
        return 1.08
    if size <= 40:
        return 1.04
    return 1.0


def _supersample(size: int) -> int:
    return 16 if size <= 32 else 8


def render(size: int, variant: str = "play") -> Image.Image:
    """按目标像素尺寸渲染一张 RGBA 图标。"""
    ss = _supersample(size)
    canvas = size * ss
    k = canvas / UNIT
    w = _weight(size)

    tile_inset = TILE_INSET_SMALL if size <= 24 else TILE_INSET
    tile_radius = TILE_RADIUS if size > 24 else UNIT * TILE_RADIUS_SMALL_RATIO

    # --- 底板 ---
    tile_mask = _new_mask(canvas)
    _rounded(tile_mask, k, (tile_inset, tile_inset, UNIT - tile_inset, UNIT - tile_inset), tile_radius)

    tile_rgb = _linear_gradient(canvas, TILE_STOPS, direction=(1.0, 1.0))
    tile_rgb *= _vignette(canvas, VIGNETTE)[..., None]
    base = _rgba_layer(tile_rgb, _alpha(tile_mask))

    # --- 描边（白 13%，保证浅色任务栏上还看得见方块的边界） ---
    if size >= 32:
        width_px = max(1, round(size / 85.0)) * ss
        edge_mask = _new_mask(canvas)
        draw = ImageDraw.Draw(edge_mask)
        i = tile_inset * k + width_px / 2.0
        draw.rounded_rectangle(
            (i, i, canvas - i, canvas - i),
            radius=max(tile_radius * k - width_px / 2.0, 0.0),
            outline=255,
            width=int(width_px),
        )
        white = np.full((canvas, canvas, 3), 255.0, dtype=np.float32)
        base = Image.alpha_composite(base, _rgba_layer(white, _alpha(edge_mask) * (EDGE_ALPHA / 255.0)))

    # --- 「P」字标 ---
    mark = _new_mask(canvas)
    stem_x1 = STEM_X0 + STEM_WIDTH * w
    _rounded(mark, k, (STEM_X0, CAP_TOP, stem_x1, CAP_BOTTOM), STEM_CORNER * w)
    bowl_r = (BOWL_BOTTOM - BOWL_TOP) / 2.0
    bowl_cx = BOWL_RIGHT - bowl_r
    bowl_cy = (BOWL_TOP + BOWL_BOTTOM) / 2.0
    _rounded(mark, k, (STEM_X0, BOWL_TOP, bowl_cx, BOWL_BOTTOM), 0.0)
    _ellipse(mark, k, bowl_cx, bowl_cy, bowl_r)

    ring = BOWL_RING * w
    counter = _new_mask(canvas)
    _rounded(counter, k, (STEM_X0 + ring, BOWL_TOP + ring, bowl_cx, BOWL_BOTTOM - ring), 0.0)
    _ellipse(counter, k, bowl_cx, bowl_cy, max(bowl_r - ring, 1.0))

    if variant == "play":
        mark = ImageChops.subtract(mark, counter)
        mark = ImageChops.lighter(mark, _rounded_triangle_mask(canvas, k, PLAY_TRI, PLAY_CORNER))
    elif variant == "plain":
        mark = ImageChops.subtract(mark, counter)
    else:
        raise SystemExit("未知 variant：" + variant)

    # --- 投影（仅 >=48px） ---
    if size >= 48:
        shadow = mark.filter(ImageFilter.GaussianBlur(max(SHADOW_BLUR * k, 0.5)))
        shadow = ImageChops.offset(shadow, 0, int(round(SHADOW_OFFSET * k)))
        shadow_alpha = _alpha(shadow) * SHADOW_ALPHA * _alpha(tile_mask)
        base = Image.alpha_composite(
            base, _rgba_layer(np.zeros((canvas, canvas, 3), dtype=np.float32), shadow_alpha)
        )

    # --- 金色（斜向渐变 + 上沿高光） ---
    gold_rgb = _linear_gradient(canvas, GOLD_STOPS, direction=(0.22, 0.78))
    if size >= 32:
        _, gv = _grid(canvas)
        gloss = 0.20 * np.clip(1.0 - gv * 1.45, 0.0, 1.0) ** 1.5
        gold_rgb = gold_rgb + (255.0 - gold_rgb) * gloss[..., None]
    base = Image.alpha_composite(base, _rgba_layer(gold_rgb, _alpha(mark)))

    return base.resize((size, size), Image.LANCZOS)


# ---------------------------------------------------------------------------
# ICO / PNG 编码
# ---------------------------------------------------------------------------


def _and_mask_bytes(alpha: np.ndarray) -> bytes:
    """32 位 DIB 帧要跟一张 1 位 AND mask（1 = 透明），每行按 4 字节对齐。"""
    w = alpha.shape[1]
    rows = (alpha[::-1] == 0).astype(np.uint8)  # 自下而上
    pad = (-w) % 8
    if pad:
        rows = np.pad(rows, ((0, 0), (0, pad)), constant_values=0)
    packed = np.packbits(rows, axis=1, bitorder="big")
    row_bytes = ((w + 31) // 32) * 4
    if packed.shape[1] < row_bytes:
        packed = np.pad(packed, ((0, 0), (0, row_bytes - packed.shape[1])), constant_values=0)
    return packed.tobytes()


def _bmp_frame(img: Image.Image) -> bytes:
    """ICO 里的 32 位 BMP 帧：BITMAPINFOHEADER（高度记两倍）+ BGRA 自下而上 + AND mask。"""
    w, h = img.size
    arr = np.asarray(img.convert("RGBA"), dtype=np.uint8)
    bgra = np.ascontiguousarray(arr[:, :, (2, 1, 0, 3)][::-1])
    pixels = bgra.tobytes()
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, len(pixels), 0, 0, 0, 0)
    return header + pixels + _and_mask_bytes(arr[:, :, 3])


def _png_frame(img: Image.Image) -> bytes:
    buf = io.BytesIO()
    img.save(buf, format="PNG", optimize=True)
    return buf.getvalue()


def ico_bytes(frames: dict[int, Image.Image]) -> bytes:
    blobs = []
    for size in sorted(frames):
        blobs.append((size, _png_frame(frames[size]) if size >= 128 else _bmp_frame(frames[size])))

    out = bytearray(struct.pack("<HHH", 0, 1, len(blobs)))
    cursor = 6 + 16 * len(blobs)
    for size, blob in blobs:
        dim = 0 if size >= 256 else size
        out += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(blob), cursor)
        cursor += len(blob)
    for _, blob in blobs:
        out += blob
    return bytes(out)


def png_bytes(img: Image.Image) -> bytes:
    buf = io.BytesIO()
    img.save(buf, format="PNG", optimize=True)
    return buf.getvalue()


# ---------------------------------------------------------------------------
# 接触表（文档用）
# ---------------------------------------------------------------------------


def _font(size: int) -> ImageFont.ImageFont:
    try:
        return ImageFont.load_default(size=size)
    except Exception:  # 老 Pillow 兜底
        return ImageFont.load_default()


def contact_sheet(frames: dict[int, Image.Image]) -> Image.Image:
    pad = 34
    label_h = 28
    sizes = sorted(frames)
    strip_h = max(sizes) + label_h + pad
    width = pad * 2 + sum(s + pad for s in sizes)
    sheet = Image.new("RGBA", (width, strip_h * 2 + pad), (0x24, 0x24, 0x2A, 0xFF))

    for row, bg in enumerate((SHEET_DARK, SHEET_LIGHT)):
        top = pad // 2 + row * (strip_h + pad // 2)
        ImageDraw.Draw(sheet).rounded_rectangle(
            (pad // 2, top, width - pad // 2, top + strip_h), radius=18, fill=bg
        )
        draw = ImageDraw.Draw(sheet)
        x = pad
        text_color = (0xC8, 0xC8, 0xD0) if row == 0 else (0x50, 0x50, 0x58)
        for s in sizes:
            y = top + (strip_h - label_h - s) // 2
            sheet.alpha_composite(frames[s], (x, y))
            label = str(s)
            tw = draw.textlength(label, font=_font(15))
            draw.text((x + s / 2 - tw / 2, top + strip_h - label_h + 4), label, font=_font(15), fill=text_color)
            x += s + pad
    return sheet


# ---------------------------------------------------------------------------
# 主流程
# ---------------------------------------------------------------------------


def build(variant: str):
    """渲染全部产物：{仓库相对路径: 字节}、各尺寸帧、接触表。"""
    cache: dict[int, Image.Image] = {}

    def frame(size: int) -> Image.Image:
        if size not in cache:
            cache[size] = render(size, variant)
        return cache[size]

    frames = {s: frame(s) for s in ICO_SIZES}
    outputs = {"src/PdxLauncher.App/Assets/AppIcon.ico": ico_bytes(frames)}
    for name, size in SQUARE_ASSETS:
        outputs["src/PdxLauncher.App/Assets/" + name] = png_bytes(frame(size))
    for name, width, height, mark in WIDE_ASSETS:
        canvas = Image.new("RGBA", (width, height), WIDE_BG)
        canvas.alpha_composite(render(mark, variant), ((width - mark) // 2, (height - mark) // 2))
        outputs["src/PdxLauncher.App/Assets/" + name] = png_bytes(canvas)
    return outputs, frames, contact_sheet(frames)


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="生成 PdxNext 应用图标")
    parser.add_argument("--variant", default="play", choices=("play", "plain"))
    parser.add_argument("--preview", action="store_true", help="只写 .tmp/icon-preview/，不覆盖 Assets")
    args = parser.parse_args(argv)

    root = Path(__file__).resolve().parents[1]
    outputs, frames, sheet = build(args.variant)

    if args.preview:
        out_root = root / ".tmp" / "icon-preview"
        sheet_path = out_root / ("app-icon-" + args.variant + ".png")
    else:
        out_root = root
        sheet_path = root / "docs" / "app-icon.png"

    for rel, data in outputs.items():
        path = out_root / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        print("  写出 %-78s %7d B" % (rel, len(data)))

    sheet_path.parent.mkdir(parents=True, exist_ok=True)
    sheet_path.write_bytes(png_bytes(sheet))
    print("  写出 %-78s %7d B" % (str(sheet_path.relative_to(root)), len(sheet_path.read_bytes())))

    print()
    print("  ICO 帧：" + " / ".join(str(s) for s in ICO_SIZES))
    print("  variant：" + args.variant + ("（预览，未覆盖 Assets）" if args.preview else ""))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
