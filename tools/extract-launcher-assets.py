#!/usr/bin/env python3
"""从官方 Paradox 启动器 app.asar 与各游戏 theme-settings.json 提取素材。

用法：
    python tools/extract-launcher-assets.py --out <目录> [--asar <app.asar>] [--theme <theme-settings.json> ...]

行为：
    1. 解析 asar，导出 launcher 自带素材（dist/renderer/assets、dist/assets、apps-metadata 下的
       图片与字体）到 <out>/launcher/<asar 内相对路径>。
    2. 对每个 --theme，读取该 theme-settings.json 引用的素材（背景/字标/图标/按钮图/字体），
       从 json 所在目录拷到 <out>/games/<游戏名>/。
    3. 写 <out>/REPORT.md：素材清单、每个游戏的主题字段原文、图片/字体尺寸。

为什么需要它：官方启动器自身**不含**任何按钮美术 —— 按钮图来自游戏安装目录的
theme-settings.json（联网时由 metadata 下发同样的图）。调试主题外观时先把这些图摊出来看。

注意 asar 头：前 16 字节是 4 个 u32，
    [0]=4, [1]=headerSize, [2]=headerObjectSize, [3]=headerJsonLength
    JSON 在 [16 : 16+h[3]]，**数据区偏移 = 8 + h[1]**（不是 16 + h[3]，那样会偏 2 字节读出垃圾）。
"""

import argparse
import json
import os
import struct
import sys

IMAGE_EXTENSIONS = ('.png', '.jpg', '.jpeg', '.webp', '.gif', '.svg')
FONT_EXTENSIONS = ('.ttf', '.otf', '.woff', '.woff2')
EXTRACT_EXTENSIONS = IMAGE_EXTENSIONS + FONT_EXTENSIONS

ASAR_PREFIXES = ('dist/renderer/assets/', 'dist/assets/', 'apps-metadata/')

THEME_ASSET_FIELDS = (
    ('background.backgroundImage', ('background', 'backgroundImage')),
    ('logo.imagePath', ('logo', 'imagePath')),
    ('appIcon.appIconPath', ('appIcon', 'appIconPath')),
    ('appTaskbarIcon.appIconPath', ('appTaskbarIcon', 'appIconPath')),
    ('button.backgroundImage', ('button', 'backgroundImage')),
    ('button.fontPath', ('button', 'fontPath')),
)

THEME_TEXT_FIELDS = ('button', 'buttonHover', 'buttonDisabled', 'buttonText')

# 主题素材目录名在四个已装游戏里高度重名（assets / launcher / Launcher-assets），
# 需要逐级上溯到真正的游戏安装目录，否则 CK3 与 V3 会写进同一个目录。
GENERIC_THEME_DIRS = ('assets', 'launcher', 'launcher-assets', 'launcherassets', 'data')

QUOTE = chr(96)


def code(text):
    """markdown 行内代码。"""
    return QUOTE + str(text) + QUOTE


def find_asar(explicit):
    """定位最新版本的 app.asar。"""
    if explicit:
        return explicit

    local = os.environ.get('LOCALAPPDATA') or os.path.join(os.path.expanduser('~'), 'AppData', 'Local')
    root = os.path.join(local, 'Programs', 'Paradox Interactive', 'launcher')
    if not os.path.isdir(root):
        raise SystemExit('找不到官方启动器目录：' + root)

    def version_key(path):
        name = os.path.basename(path).replace('launcher-v', '')
        parts = []
        for chunk in name.split('.'):
            parts.append(int(chunk) if chunk.isdigit() else 0)
        return parts

    candidates = sorted(
        (os.path.join(root, item) for item in os.listdir(root) if item.startswith('launcher-v')),
        key=version_key)
    for version_dir in reversed(candidates):
        asar = os.path.join(version_dir, 'resources', 'app.asar')
        if os.path.isfile(asar):
            return asar
    raise SystemExit('在 ' + root + ' 下没找到 resources/app.asar')


def read_asar_index(path):
    with open(path, 'rb') as handle:
        header = handle.read(16)
        if len(header) < 16:
            raise SystemExit('不是 asar 文件：' + path)
        sizes = struct.unpack('<IIII', header)
        header_size = sizes[1]
        json_length = sizes[3]
        handle.seek(0)
        raw_header = handle.read(16 + header_size)
    index = json.loads(raw_header[16:16 + json_length].decode('utf-8'))
    return index, 8 + header_size


def walk_files(node, prefix=''):
    files = node.get('files') or {}
    for name, child in files.items():
        path = prefix + name
        if 'files' in child:
            yield from walk_files(child, path + '/')
        elif 'offset' in child:
            yield path, child


def read_asar_entry(asar_path, data_offset, entry):
    with open(asar_path, 'rb') as handle:
        handle.seek(data_offset + int(entry['offset']))
        return handle.read(int(entry['size']))


def sniff(data):
    if data.startswith(b'\x89PNG\r\n\x1a\n'):
        return 'PNG'
    if data.startswith(b'\xff\xd8\xff'):
        return 'JPEG'
    if data.startswith(b'GIF8'):
        return 'GIF'
    if data[:4] == b'RIFF' and data[8:12] == b'WEBP':
        return 'WEBP'
    head = data[:512].lstrip().lower()
    if head.startswith(b'<?xml') or head.startswith(b'<svg'):
        return 'SVG'
    if data[:4] in (b'\x00\x01\x00\x00', b'true', b'ttcf'):
        return 'TTF'
    if data[:4] == b'OTTO':
        return 'OTF'
    if data[:4] == b'wOFF':
        return 'WOFF'
    if data[:4] == b'wOF2':
        return 'WOFF2'
    return '?'


def image_size(data):
    """不依赖 Pillow 的尺寸读取（PNG / GIF / JPEG / WEBP 的常见分支）。"""
    try:
        if data.startswith(b'\x89PNG\r\n\x1a\n') and len(data) >= 24:
            width, height = struct.unpack('>II', data[16:24])
            return width, height
        if data.startswith(b'GIF8') and len(data) >= 10:
            width, height = struct.unpack('<HH', data[6:10])
            return width, height
        if data[:4] == b'RIFF' and data[8:12] == b'WEBP' and len(data) >= 30:
            chunk = data[12:16]
            if chunk == b'VP8X':
                width = 1 + int.from_bytes(data[24:27], 'little')
                height = 1 + int.from_bytes(data[27:30], 'little')
                return width, height
            if chunk == b'VP8 ':
                width, height = struct.unpack('<HH', data[26:30])
                return width & 0x3FFF, height & 0x3FFF
        if data.startswith(b'\xff\xd8\xff'):
            index = 2
            while index + 9 < len(data):
                if data[index] != 0xFF:
                    index += 1
                    continue
                marker = data[index + 1]
                if marker in (0xD8, 0xD9) or 0xD0 <= marker <= 0xD7:
                    index += 2
                    continue
                length = struct.unpack('>H', data[index + 2:index + 4])[0]
                if 0xC0 <= marker <= 0xCF and marker not in (0xC4, 0xC8, 0xCC):
                    height, width = struct.unpack('>HH', data[index + 5:index + 9])
                    return width, height
                index += 2 + length
    except (struct.error, IndexError):
        pass
    return None


def write_file(out_root, relative, data):
    target = os.path.join(out_root, relative.replace('/', os.sep))
    os.makedirs(os.path.dirname(target), exist_ok=True)
    with open(target, 'wb') as handle:
        handle.write(data)
    return target


def collect_launcher(asar_path, out_root, report):
    index, data_offset = read_asar_index(asar_path)
    count = 0
    for path, entry in walk_files(index):
        if not path.startswith(ASAR_PREFIXES):
            continue
        if not path.lower().endswith(EXTRACT_EXTENSIONS):
            continue
        data = read_asar_entry(asar_path, data_offset, entry)
        kind = sniff(data)
        expected = path.rsplit('.', 1)[-1].upper()
        if expected == 'JPG':
            expected = 'JPEG'
        note = ''
        if kind == '?' or expected != kind:
            note = '（magic=' + kind + '，与扩展名 ' + expected + ' 不符，请核对 asar 数据区偏移）'
        write_file(out_root, 'launcher/' + path, data)
        size = image_size(data)
        size_text = '' if size is None else '  %dx%d' % size
        report.append('| ' + code(path) + ' | %d B | %s%s |%s' % (len(data), kind, size_text, note))
        count += 1
    return count


def game_directory(base_dir):
    """把 .../Crusader Kings III/launcher/assets 这类重名目录收敛到游戏安装目录。"""
    current = base_dir.rstrip('\\/')
    while True:
        name = os.path.basename(current)
        parent = os.path.dirname(current)
        if name.lower() not in GENERIC_THEME_DIRS or parent in ('', current):
            return current
        current = parent


def collect_theme(theme_path, out_root, report):
    with open(theme_path, 'r', encoding='utf-8-sig') as handle:
        settings = json.load(handle)

    base_dir = os.path.dirname(os.path.abspath(theme_path))
    game_dir = game_directory(base_dir)
    game_name = os.path.basename(game_dir) or 'game'
    report.append('')
    report.append('## ' + game_name + '（' + code(theme_path) + '）')
    report.append('')
    report.append('~~~json')
    report.append(json.dumps(settings, ensure_ascii=False, indent=2, sort_keys=True))
    report.append('~~~')
    report.append('')
    report.append('| 字段 | 引用文件 | 尺寸 |')
    report.append('| --- | --- | --- |')

    for label, keys in THEME_ASSET_FIELDS:
        node = settings
        for key in keys:
            node = node.get(key) if isinstance(node, dict) else None
        if not isinstance(node, str) or not node.strip():
            continue
        source = os.path.normpath(os.path.join(base_dir, node.replace('/', os.sep)))
        if not os.path.isfile(source):
            report.append('| ' + code(label) + ' | ' + code(node) + ' | 缺失 |')
            continue
        with open(source, 'rb') as handle:
            data = handle.read()
        write_file(out_root, 'games/' + game_name + '/' + os.path.basename(source), data)
        size = image_size(data)
        size_text = '—' if size is None else '%dx%d' % size
        report.append('| ' + code(label) + ' | ' + code(node) + ' | %s（%d B，%s） |'
                      % (size_text, len(data), sniff(data)))

    report.append('')
    report.append('按钮相关字段原文：')
    report.append('')
    report.append('~~~json')
    for name in THEME_TEXT_FIELDS:
        if name in settings:
            report.append(name + ': ' + json.dumps(settings[name], ensure_ascii=False, sort_keys=True))
    report.append('~~~')
    return game_name


def main(argv):
    parser = argparse.ArgumentParser(description='提取官方启动器与游戏主题素材')
    parser.add_argument('--out', required=True, help='输出目录')
    parser.add_argument('--asar', default=None, help='app.asar 路径（省略时自动找最新版本）')
    parser.add_argument('--theme', action='append', default=[],
                        help='某游戏的 theme-settings.json 路径，可重复')
    args = parser.parse_args(argv)

    out_root = os.path.abspath(args.out)
    os.makedirs(out_root, exist_ok=True)

    report = ['# 启动器与游戏主题素材清单', '']
    asar_path = find_asar(args.asar)
    report.append('- 启动器包：' + code(asar_path) + '（%d B）' % os.path.getsize(asar_path))
    report.append('')
    report.append('## 官方启动器自带素材')
    report.append('')
    report.append('| asar 内路径 | 大小 | 类型 |')
    report.append('| --- | --- | --- |')
    count = collect_launcher(asar_path, out_root, report)
    report.append('')
    report.append('合计 %d 个文件。' % count)

    game_names = []
    for theme_path in args.theme:
        game_names.append(collect_theme(theme_path, out_root, report))
    if len(set(game_names)) != len(game_names):
        report.append('')
        report.append('> 警告：有游戏目录同名（' + ', '.join(game_names) + '），后写的会覆盖先写的。')

    report_path = os.path.join(out_root, 'REPORT.md')
    with open(report_path, 'w', encoding='utf-8') as handle:
        handle.write('\n'.join(report) + '\n')

    print('asar      :', asar_path)
    print('launcher  :', count, 'files ->', os.path.join(out_root, 'launcher'))
    print('games     :', ', '.join(game_names), '->', os.path.join(out_root, 'games'))
    print('report    :', report_path)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
