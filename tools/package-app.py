#!/usr/bin/env python3
"""把 tools/manual-build.py app 产出的 dll 装配成一个可直接运行的目录（Lead 专用）。

背景：本机没有 Windows App Runtime 框架包（MSIX 未注册，Add-AppxPackage 被拒），
所以必须走「非打包 + 自包含」路线：
  1. 复制 .NET 10 运行时 + WindowsAppSDK 原生 payload 到输出目录
  2. 从 MSBuild 的 ReferencePath 里挑出托管的投影程序集（不能靠 glob，会漏）
  3. 用 package.appxfragment 生成 reg-free WinRT 的 SxS 清单，mt.exe 合并进 exe
  4. 用 apphost.exe 模板 + 占位符替换生成 exe，并写 deps.json / runtimeconfig.json

用法:
    python tools/package-app.py [输出目录]

注意：这是「离线兜底」路径。用户自己机器上直接 dotnet build 会得到更标准的产物。
"""
import glob
import json
import os
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

WS = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRATCH = os.path.join(WS, ".scratch")
OBJ = os.path.join(SCRATCH, "obj")
APP_PROJ = os.path.join(WS, "src", "PdxLauncher.App")
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(SCRATCH, "publish")

NUGET = r"C:/Users/17147/.nuget/packages"
SHARED = r"C:/Program Files/dotnet/shared"
APPHOST = r"C:/Program Files/dotnet/packs/Microsoft.NETCore.App.Host.win-x64/10.0.13/runtimes/win-x64/native/apphost.exe"
MT = os.path.join(NUGET, "microsoft.windows.sdk.buildtools/10.0.28000.2705/bin/10.0.28000.0/x64/mt.exe")

APP_NAME = "PdxLauncher.App"
APP_DLL = APP_NAME + ".dll"
NET_VERSION = "10.0.13"

# 这些包提供原生 payload（dll/pri/winmd），必须整体复制
NATIVE_PKGS = [
    ("microsoft.windowsappsdk.winui/2.3.9", "runtimes-framework/win-x64/native"),
    ("microsoft.windowsappsdk.foundation/2.3.12", "runtimes-framework/win-x64/native"),
    ("microsoft.windowsappsdk.dwrite/2.1.0", "runtimes-framework/win-x64/native"),
    ("microsoft.windowsappsdk.interactiveexperiences/2.1.9", "runtimes-framework/win-x64/native"),
    ("microsoft.windowsappsdk.widgets/2.0.5", "runtimes-framework/win-x64/native"),
]

# 托管投影程序集：只认这些路径关键字（glob 会漏，必须从 refs.json 里筛）
MANAGED_KEYS = (
    "microsoft.winui",
    "winrt.runtime",
    "microsoft.windows.sdk.net",
    "communitytoolkit",
    "webview2",
    "microsoft.interactiveexperiences",
    "microsoft.windows.applicationmodel",
    "microsoft.windows.appruntime",
    "microsoft.windows.ai",
    "microsoft.windows.widgets",
    "microsoft.windows.foundation",
    "microsoft.windows.storage",
)

FRAGMENT_PKGS = [
    ("microsoft.windowsappsdk.foundation/2.3.12", "runtimes-framework/package.appxfragment"),
    ("microsoft.windowsappsdk.winui/2.3.9", "runtimes-framework/package.appxfragment"),
    ("microsoft.windowsappsdk.interactiveexperiences/2.1.9", "runtimes-framework/package.appxfragment"),
]

FRAGMENT_NS = "http://schemas.microsoft.com/appx/manifest/foundation/windows10"

problems = []


def log(msg):
    print(msg, flush=True)


def fail(msg):
    problems.append(msg)
    log("  !! " + msg)


def run(args, logpath, cwd=None):
    os.makedirs(os.path.dirname(logpath), exist_ok=True)
    with open(logpath, "w", encoding="utf-8", errors="replace") as f:
        return subprocess.call(args, stdout=f, stderr=f, stdin=subprocess.DEVNULL, cwd=cwd)


def copy_file(src, dst):
    shutil.copy2(src, dst)
    return 1


def copy_tree_files(src_dir, dst_dir):
    n = 0
    for entry in sorted(os.listdir(src_dir)):
        src = os.path.join(src_dir, entry)
        if os.path.isfile(src):
            n += copy_file(src, dst_dir)
        elif os.path.isdir(src):
            sub = os.path.join(dst_dir, entry)
            os.makedirs(sub, exist_ok=True)
            for f in sorted(os.listdir(src)):
                p = os.path.join(src, f)
                if os.path.isfile(p):
                    n += copy_file(p, sub)
    return n


def find_output_json():
    hits = glob.glob(os.path.join(APP_PROJ, "obj", "**", "output.json"), recursive=True)
    return hits[0] if hits else None


def find_input_json():
    hits = glob.glob(os.path.join(APP_PROJ, "obj", "**", "input.json"), recursive=True)
    return hits[0] if hits else None


def main():
    if not os.path.isdir(OUT):
        os.makedirs(OUT, exist_ok=True)

    # ---------- 1. .NET 运行时
    log("[1/6] 复制 .NET 运行时 ...")
    n = 0
    for framework in ("Microsoft.NETCore.App", "Microsoft.WindowsDesktop.App"):
        src = os.path.join(SHARED, framework, NET_VERSION)
        if not os.path.isdir(src):
            fail("缺少运行时目录: " + src)
            continue
        n += copy_tree_files(src, OUT)
    log("  " + str(n) + " 个运行时文件")

    # ---------- 2. WindowsAppSDK 原生 payload
    log("[2/6] 复制 WindowsAppSDK 原生 payload ...")
    for pkg, sub in NATIVE_PKGS:
        src = os.path.join(NUGET, pkg, sub)
        if not os.path.isdir(src):
            fail("缺少原生 payload: " + src)
            continue
        log("  " + pkg + " -> " + str(copy_tree_files(src, OUT)))

    # ---------- 3. 托管投影程序集（必须来自 refs.json）
    log("[3/6] 复制托管投影程序集 ...")
    refs_json = os.path.join(OBJ, "app.refs.json")
    if not os.path.exists(refs_json):
        fail("缺少 " + refs_json + "（先运行 manual-build.py app）")
        return finish()
    items = json.load(open(refs_json, encoding="utf-8-sig")).get("Items", {}).get("ReferencePath", [])
    copied = []
    for it in items:
        p = it.get("Identity", "").replace("\\\\", "/")
        low = p.lower()
        if not low.endswith(".dll") or not os.path.exists(p):
            continue
        if ("/lib/" in low or "/lib_manual/" in low) and any(k in low for k in MANAGED_KEYS):
            copy_file(p, OUT)
            copied.append(os.path.basename(p))
    log("  " + str(len(set(copied))) + " 个投影程序集")

    # ---------- 4. 应用本体（dll + xbf + xaml）
    log("[4/6] 复制应用产物 ...")
    app_dll_src = os.path.join(APP_PROJ, "bin", "manual", APP_DLL)
    if not os.path.exists(app_dll_src):
        fail("缺少 " + app_dll_src + "（先运行 manual-build.py app）")
        return finish()
    copy_file(app_dll_src, OUT)
    out_json = find_output_json()
    xbf_count = 0
    if out_json:
        data = json.load(open(out_json, encoding="utf-8-sig"))
        for key in ("GeneratedXbfFiles", "GeneratedXamlFiles"):
            for f in data.get(key, []):
                if os.path.exists(f):
                    copy_file(f, OUT)
                    if key == "GeneratedXbfFiles":
                        xbf_count += 1
    log("  " + str(xbf_count) + " 个 .xbf")

    # ---------- 5. reg-free WinRT 清单 + mt 合并
    log("[5/6] 生成并合并 SxS 清单 ...")
    manifest = build_manifest(os.path.join(OUT, "WinAppSDK.manifest"))
    user_manifest = os.path.join(APP_PROJ, "app.manifest")
    merged = os.path.join(OUT, "merged.manifest")
    rc = run([MT, "-nologo", "-manifest", manifest, user_manifest, "-out:" + merged],
             os.path.join(OBJ, "mt.merge.log"), cwd=OUT)
    if rc != 0 or not os.path.exists(merged):
        fail("mt 合并清单失败 rc=" + str(rc) + "（见 " + os.path.join(OBJ, "mt.merge.log") + "）")
        return finish()
    log("  merged.manifest " + str(os.path.getsize(merged)) + " B")

    # ---------- 6. exe + deps.json + runtimeconfig.json
    log("[6/6] 生成 apphost 与宿主配置 ...")
    exe = os.path.join(OUT, APP_NAME + ".exe")
    patch_apphost(exe, APP_DLL)
    rc = run([MT, "-nologo", "-manifest", merged, "-outputresource:" + exe],
             os.path.join(OBJ, "mt.embed.log"), cwd=OUT)
    if rc != 0:
        fail("mt 嵌入清单失败 rc=" + str(rc) + "（见 " + os.path.join(OBJ, "mt.embed.log") + "）")
    else:
        log("  " + APP_NAME + ".exe " + str(os.path.getsize(exe)) + " B")

    write_deps(os.path.join(OUT, APP_DLL))
    write_runtimeconfig(os.path.join(OUT, APP_NAME + ".runtimeconfig.json"))

    log("")
    log("输出目录: " + OUT)
    log("启动方式: " + os.path.join(OUT, APP_NAME + ".exe"))
    return finish()


def patch_apphost(dst, managed_name):
    data = bytearray(open(APPHOST, "rb").read())
    placeholder = b"c3ab8ff13720e8ad9047dd39466b3c8974e592c2fa383d4a3960714caef0c4f2"
    idx = data.find(placeholder)
    if idx < 0:
        fail("apphost 模板里找不到占位符")
        return
    region = 1024
    name = managed_name.encode("utf-8")
    data[idx:idx + region] = name + b"\x00" * (region - len(name))
    open(dst, "wb").write(bytes(data))


def write_deps(dll_path):
    """deps.json 只列 .dll 资产（列 .xbf/.xaml 会让 host 报 already found with different extension）。"""
    out_dir = os.path.dirname(dll_path)
    runtime = {}
    for entry in sorted(os.listdir(out_dir)):
        if entry.lower().endswith(".dll"):
            runtime[entry] = {"assemblyVersion": "", "fileVersion": ""}
    deps = {
        "runtimeTarget": {"name": ".NETCoreApp,Version=v10.0", "signature": ""},
        "compilationOptions": {},
        "targets": {
            ".NETCoreApp,Version=v10.0": {
                APP_NAME + "/1.0.0": {"runtime": runtime}
            }
        },
        "libraries": {APP_NAME + "/1.0.0": {"type": "project", "serviceable": False, "sha512": ""}},
    }
    path = os.path.join(out_dir, APP_NAME + ".deps.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(deps, f, indent=2)
    log("  deps.json " + str(len(runtime)) + " 个 dll")


def write_runtimeconfig(path):
    config = {
        "runtimeOptions": {
            "tfm": "net10.0",
            "frameworks": [
                {"name": "Microsoft.NETCore.App", "version": NET_VERSION},
                {"name": "Microsoft.WindowsDesktop.App", "version": NET_VERSION},
            ],
            "configProperties": {"System.Runtime.TieredPGO": True},
        }
    }
    with open(path, "w", encoding="utf-8") as f:
        json.dump(config, f, indent=2)


def build_manifest(dest):
    """从 package.appxfragment 生成 SxS 清单；跳过磁盘上不存在的 dll。"""
    lines = [
        "<?xml version='1.0' encoding='utf-8' standalone='yes'?>",
        "<assembly manifestVersion='1.0'",
        "    xmlns:asmv3='urn:schemas-microsoft-com:asm.v3'",
        "    xmlns:winrtv1='urn:schemas-microsoft-com:winrt.v1'",
        "    xmlns='urn:schemas-microsoft-com:asm.v1'>",
    ]
    out_dir = os.path.dirname(dest)
    kept, skipped = [], []
    for pkg, sub in FRAGMENT_PKGS:
        path = os.path.join(NUGET, pkg, sub)
        if not os.path.exists(path):
            continue
        root = ET.parse(path).getroot()
        for ext in root.iter("{" + FRAGMENT_NS + "}Extension"):
            for server in ext:
                p = server.find("{" + FRAGMENT_NS + "}Path")
                if p is None:
                    continue
                dll = (p.text or "").strip()
                classes = [c.get("ActivatableClassId")
                           for c in server.findall("{" + FRAGMENT_NS + "}ActivatableClass")]
                if not classes:
                    continue
                if not os.path.exists(os.path.join(out_dir, dll)):
                    skipped.append(dll)
                    continue
                kept.append(dll)
                lines.append("    <asmv3:file name='%s'>" % dll)
                for c in classes:
                    lines.append("        <winrtv1:activatableClass name='%s' threadingModel='both'/>" % c)
                lines.append("    </asmv3:file>")
    lines.append("</assembly>")
    with open(dest, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
    log("  WinAppSDK.manifest " + str(os.path.getsize(dest)) + " B, " + str(len(kept)) + " 个 dll")
    if skipped:
        log("  跳过（磁盘上没有）: " + ", ".join(sorted(set(skipped))))
    return dest


def finish():
    log("")
    if problems:
        log("FAILED (" + str(len(problems)) + " 个问题)")
        return 1
    log("PACKAGE OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
