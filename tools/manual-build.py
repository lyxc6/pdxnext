#!/usr/bin/env python3
"""离线手工构建脚本（Lead 专用）。

背景：本机 DSH 沙箱禁止创建管道，MSBuild 的 Csc/Exec 任务必然失败（error MSB3883）。
本脚本绕过 MSBuild 的 Csc/Exec，直接调用 Roslyn 与 XamlCompiler，实现零网络离线构建。

用法:
    python tools/manual-build.py core      # 只编 PdxLauncher.Core（纯 C#，最快，用于契约/实现校验）
    python tools/manual-build.py app       # 编 Core + App（含 XAML 编译、清单合并、运行时装配）
    python tools/manual-build.py checks    # 编 Core + Checks 自检程序并运行
    python tools/manual-build.py all       # core + app + checks

退出码 0 = 成功。任何编译错误都会打印并返回非 0。
"""
import glob
import json
import os
import shutil
import subprocess
import sys

WS = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRATCH = os.path.join(WS, ".scratch")
BUILD = os.path.join(SCRATCH, "build")
OBJ = os.path.join(SCRATCH, "obj")

DOTNET = "dotnet"
CSC = r"C:/Program Files/dotnet/sdk/10.0.402/Roslyn/bincore/csc.dll"
NETREF = r"C:/Program Files/dotnet/packs/Microsoft.NETCore.App.Ref/10.0.13/ref/net10.0"
# .NET SDK 自带的分析器（源生成器）：[GeneratedRegex] 必须有 regex 生成器才会生成实现部件。
NETREF_ANALYZERS = os.path.join(os.path.dirname(os.path.dirname(NETREF)), "analyzers", "dotnet", "cs")
REGEX_GENERATOR = os.path.join(NETREF_ANALYZERS, "System.Text.RegularExpressions.Generator.dll")
NUGET = os.environ.get("NUGET_PACKAGES") or os.path.join(os.path.expanduser("~"), ".nuget", "packages")
XAMLCOMPILER = os.path.join(NUGET, "microsoft.windowsappsdk.winui/2.3.9/tools/net472/XamlCompiler.exe")
MT = os.path.join(NUGET, "microsoft.windows.sdk.buildtools/10.0.28000.2705/bin/10.0.28000.0/x64/mt.exe")
APPSDK_INC = os.path.join(NUGET, "microsoft.windowsappsdk.foundation/2.3.12/include")
# CommunityToolkit.Mvvm 的源生成器：[ObservableProperty] / [RelayCommand] 必须有它才会生成属性与命令。
# 实测 csc -analyzer 可以直接驱动它（字段式写法通过，partial property 写法在 8.4.2 下不支持）。
MVVM_ANALYZER = os.path.join(
    NUGET, "communitytoolkit.mvvm/8.4.2/analyzers/dotnet/roslyn4.12/cs/CommunityToolkit.Mvvm.SourceGenerators.dll")
MVVM_REF = os.path.join(NUGET, "communitytoolkit.mvvm/8.4.2/lib/net8.0/CommunityToolkit.Mvvm.dll")

# Core 现在引用了本地翻译相关的 NuGet 包（离线缓存里已有）：
#   BergamotTranslatorSharp  -> Bergamot（NMT）本地引擎
#   LLamaSharp               -> Index-Translate-2B（GGUF）进程内推理
#   LLamaSharp.Backend.Cpu   -> llama.cpp 的 CPU 原生后端
# 手工构建不经过 MSBuild 的 NuGet 资产解析，必须自己补 -r: 引用与原生 payload。
NUGET_CORE_REFS = [
    os.path.join(NUGET, "bergamottranslatorsharp/0.7.0/lib/net10.0/BergamotTranslatorSharp.dll"),
    os.path.join(NUGET, "llamasharp/0.30.0/lib/net10.0/LLamaSharp.dll"),
    os.path.join(NUGET, "communitytoolkit.highperformance/8.4.2/lib/net8.0/CommunityToolkit.HighPerformance.dll"),
    os.path.join(NUGET, "microsoft.bcl.asyncinterfaces/10.0.13/lib/netstandard2.0/Microsoft.Bcl.AsyncInterfaces.dll"),
    os.path.join(NUGET, "microsoft.bcl.memory/10.0.13/lib/net10.0/Microsoft.Bcl.Memory.dll"),
    os.path.join(NUGET, "microsoft.extensions.ai.abstractions/10.11.0/lib/net10.0/Microsoft.Extensions.AI.Abstractions.dll"),
    os.path.join(NUGET, "microsoft.extensions.logging.abstractions/10.0.13/lib/net10.0/Microsoft.Extensions.Logging.Abstractions.dll"),
    os.path.join(NUGET, "system.linq.async/7.0.1/lib/net10.0/System.Linq.Async.dll"),
    os.path.join(NUGET, "system.numerics.tensors/10.0.13/lib/net10.0/System.Numerics.Tensors.dll"),
]

# 原生 payload：本地翻译引擎的 dll 必须随应用一起发布。
#   BergamotTranslatorSharp -> runtimes/win-x64/native/bergamot.dll
#   LLamaSharp.Backend.Cpu  -> LLamaSharpRuntimes/win-x64/native/{avx,avx2,avx512,noavx}/*.dll
NATIVE_CORE_PAYLOADS = [
    (os.path.join(NUGET, "bergamottranslatorsharp/0.7.0/runtimes/win-x64/native"), ""),
    (os.path.join(NUGET, "llamasharp.backend.cpu/0.30.0/LLamaSharpRuntimes/win-x64/native"),
     os.path.join("runtimes", "win-x64", "native")),
]

CORE_PROJ = os.path.join(WS, "src", "PdxLauncher.Core")
IMAGING_PROJ = os.path.join(WS, "src", "PdxLauncher.Imaging")
APP_PROJ = os.path.join(WS, "src", "PdxLauncher.App")
CHECKS_PROJ = os.path.join(WS, "tests", "PdxLauncher.Checks")

problems = []


def log(msg):
    # 自检输出里含 emoji（如 🙂），控制台默认 cp936 会 UnicodeEncodeError 直接崩掉；
    # 统一半途替换成 '?' 而不是抛异常。
    try:
        sys.stdout.reconfigure(errors="replace")
    except (AttributeError, ValueError):
        pass
    print(msg, flush=True)


def fail(msg):
    problems.append(msg)
    log("  !! " + msg)


def run(args, logpath, cwd=None):
    """子进程调用：stdio 全部重定向到文件（沙箱禁止管道）。"""
    os.makedirs(os.path.dirname(logpath), exist_ok=True)
    with open(logpath, "w", encoding="utf-8", errors="replace") as f:
        return subprocess.call(args, stdout=f, stderr=f, stdin=subprocess.DEVNULL, cwd=cwd)


def readlog(path):
    if not os.path.exists(path):
        return ""
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


def netrefs():
    return sorted(glob.glob(os.path.join(NETREF, "*.dll")))


def sources(proj, exclude_dirs=("obj", "bin")):
    out = []
    for root, dirs, files in os.walk(proj):
        dirs[:] = [d for d in dirs if d.lower() not in exclude_dirs]
        for f in sorted(files):
            if f.endswith(".cs"):
                out.append(os.path.join(root, f))
    return sorted(out)


def compile_library(name, srcs, refs, outdir, extra_defines=None, target="library", analyzers=None, resources=None):
    os.makedirs(outdir, exist_ok=True)
    dll = os.path.join(outdir, name + ".dll")
    args = ["-nologo", "-target:" + target, "-nullable:enable", "-langversion:latest",
            "-preferreduilang:en-US", "-nowarn:1591", "-nowarn:1701", "-out:" + dll]
    if extra_defines:
        args += ["-define:" + d for d in extra_defines]
    for a in analyzers or ():
        if os.path.exists(a):
            args.append("-analyzer:" + a)
    # 内嵌资源：-resource:<绝对路径>,<逻辑名>，与 csproj 的 EmbeddedResource+LogicalName 等价。
    for path, logical in resources or ():
        if not os.path.exists(path):
            fail(name + ": 内嵌资源不存在 " + path)
            return None
        args.append("-resource:" + path + "," + logical)
    args += ["-r:" + r for r in refs]
    args += srcs
    # Windows 命令行有 32767 字符上限，Core 的源文件+引用已经顶到边界，
    # 所以统一走 csc 的响应文件（@file），一行一个参数。
    # 响应文件里带空格的路径必须加双引号，否则 csc 会按空格切分（"C:\Program Files\..." 会断）。
    rsp = os.path.join(OBJ, name + ".rsp")
    os.makedirs(OBJ, exist_ok=True)
    with open(rsp, "w", encoding="utf-8") as f:
        for a in args:
            f.write('"' + a.replace('"', '\\"') + '"\n')
    logpath = os.path.join(OBJ, name + ".csc.log")
    rc = run([DOTNET, CSC, "@" + rsp], logpath)
    text = readlog(logpath)
    errs = [l for l in text.splitlines() if ": error " in l]
    if rc != 0 or errs:
        fail(name + ": csc rc=" + str(rc) + " errors=" + str(len(errs)))
        for l in errs[:25]:
            log("     " + l.strip()[:240])
        return None
    return dll


def copy_native_payload(outdir):
    """把本地翻译引擎的原生 dll 复制到输出目录（MSBuild 的 .props 在手工构建里不生效）。"""
    total = 0
    for src, sub in NATIVE_CORE_PAYLOADS:
        if not os.path.isdir(src):
            fail("缺少原生 payload: " + src)
            continue
        dst = os.path.join(outdir, sub) if sub else outdir
        os.makedirs(dst, exist_ok=True)
        n = 0
        for root, dirs, files in os.walk(src):
            dirs[:] = sorted(dirs)
            rel = os.path.relpath(root, src)
            target_dir = dst if rel == "." else os.path.join(dst, rel)
            os.makedirs(target_dir, exist_ok=True)
            for f in sorted(files):
                if f.lower().endswith(".dll"):
                    shutil.copy2(os.path.join(root, f), os.path.join(target_dir, f))
                    n += 1
        total += n
        log("  原生 payload " + os.path.basename(src) + " -> " + str(n) + " 个 dll")
    return total


# ---------------------------------------------------------------- Core
def core_resources():
    """Core 的内嵌补丁素材；逻辑名必须与 Patch/PatchAssets.cs 的常量一致。"""
    assets = os.path.join(CORE_PROJ, "Patch", "Assets")
    return [
        (os.path.join(assets, "steam_api.dll"), "PdxLauncher.Patch.steam_api.dll"),
        (os.path.join(assets, "steam_api64.dll"), "PdxLauncher.Patch.steam_api64.dll"),
        (os.path.join(assets, "cream_api.ini"), "PdxLauncher.Patch.cream_api.ini"),
    ]


def build_core():
    log("[core] 编译 PdxLauncher.Core ...")
    srcs = sources(CORE_PROJ)
    if not srcs:
        fail("core: 没有找到 .cs 源文件")
        return None
    refs = netrefs()
    for r in NUGET_CORE_REFS:
        if not os.path.exists(r):
            fail("core: 缺少 NuGet 引用 " + r)
            return None
        refs.append(r)
    dll = compile_library(
        "PdxLauncher.Core",
        srcs,
        refs,
        BUILD,
        analyzers=[REGEX_GENERATOR],
        resources=core_resources())
    if dll:
        copy_native_payload(BUILD)
        log("  OK  " + dll + "  (" + str(os.path.getsize(dll)) + " B, " + str(len(srcs)) + " 源文件)")
    return dll


# ---------------------------------------------------------------- Imaging
def build_imaging():
    log("[imaging] 编译 PdxLauncher.Imaging ...")
    srcs = sources(IMAGING_PROJ)
    if not srcs:
        fail("imaging: 没有找到 .cs 源文件")
        return None
    dll = compile_library("PdxLauncher.Imaging", srcs, netrefs(), BUILD, analyzers=[REGEX_GENERATOR])
    if dll:
        log("  OK  " + dll + "  (" + str(len(srcs)) + " 源文件)")
    return dll


# ---------------------------------------------------------------- Checks
def build_checks(core_dll, imaging_dll):
    log("[checks] 编译 PdxLauncher.Checks ...")
    srcs = sources(CHECKS_PROJ)
    # csproj 用 <Compile Include> 链接了 App 层两个纯逻辑文件（不引用 UI 类型），手工构建要自己补上。
    srcs += [
        os.path.join(APP_PROJ, "Services", "PlaySetEditor.cs"),
        os.path.join(APP_PROJ, "Services", "PositionInput.cs"),
    ]
    if not srcs:
        fail("checks: 没有找到 .cs 源文件")
        return None
    refs = netrefs() + [core_dll]
    if imaging_dll:
        refs.append(imaging_dll)
    # 自检程序必须是 exe：Program.Main 是入口点
    dll = compile_library("PdxLauncher.Checks", srcs, refs, BUILD, target="exe", analyzers=[REGEX_GENERATOR])
    if dll:
        log("  OK  " + dll)
    return dll


def run_checks(checks_dll):
    if not checks_dll:
        return
    log("[checks] 运行自检 ...")
    rcfile = os.path.join(BUILD, "PdxLauncher.Checks.runtimeconfig.json")
    with open(rcfile, "w", encoding="utf-8") as f:
        json.dump({"runtimeOptions": {"tfm": "net10.0", "framework": {
            "name": "Microsoft.NETCore.App", "version": "10.0.13"}}}, f)
    logpath = os.path.join(BUILD, "checks.run.log")
    rc = run([DOTNET, checks_dll], logpath, cwd=BUILD)
    text = readlog(logpath)
    log(text.rstrip())
    if rc != 0:
        fail("checks: 自检退出码 " + str(rc))


# ---------------------------------------------------------------- App
def item(p):
    return {"FullPath": p, "ItemSpec": p, "IsSystemReference": False, "IsNuGetReference": True,
            "IsStaticLibraryReference": False, "MSBuild_Link": "", "MSBuild_TargetPath": "",
            "MSBuild_XamlResourceMapName": "", "MSBuild_XamlComponentResourceLocation": ""}


def find_input_json(proj):
    for root, dirs, files in os.walk(os.path.join(proj, "obj")):
        if "input.json" in files:
            return os.path.join(root, "input.json")
    return None


def build_app(core_dll):
    log("[app] 生成 XAML 编译输入 ...")
    if not os.path.isdir(os.path.join(APP_PROJ, "obj")):
        os.makedirs(os.path.join(APP_PROJ, "obj"), exist_ok=True)
    # 让 MSBuild 走到 MarkupCompilePass1 为止（它会在 Exec 处失败，但会产出 input.json 与 refs.json）
    run([DOTNET, "build", "-t:MarkupCompilePass1", "-p:ManualBuild=true",
         "-p:ManualCoreDll=" + core_dll, "-m:1", "-nodeReuse:false"],
        os.path.join(OBJ, "app.markup.log"), cwd=APP_PROJ)
    run([DOTNET, "msbuild", "PdxLauncher.App.csproj", "-t:ResolveReferences",
         "-p:ManualBuild=true", "-p:ManualCoreDll=" + core_dll,
         "-getItem:ReferencePath", "-m:1", "-nodeReuse:false"],
        os.path.join(OBJ, "app.refs.json"), cwd=APP_PROJ)

    inp = find_input_json(APP_PROJ)
    if not inp:
        fail("app: MSBuild 没有产出 input.json（XAML 文件是否已存在？）")
        return
    objdir = os.path.dirname(inp)
    outp = os.path.join(objdir, "output.json")

    refs = []
    refsraw = os.path.join(OBJ, "app.refs.json")
    if os.path.exists(refsraw):
        data = json.load(open(refsraw, encoding="utf-8-sig"))
        items = data.get("Items", {}).get("ReferencePath", [])
        for it in items:
            p = it.get("Identity", "").replace("\\\\", "/")
            if p.lower().endswith(".dll") and os.path.exists(p):
                refs.append(p)
    refs = sorted(set(refs))
    if not refs:
        fail("app: ReferencePath 为空，无法进行 XAML 编译")
        return
    log("  refs = " + str(len(refs)))

    ra = [item(p) for p in refs]
    outdir = os.path.join(APP_PROJ, "bin", "manual")
    os.makedirs(outdir, exist_ok=True)
    dll = os.path.join(outdir, "PdxLauncher.App.dll")

    # ---- pass 1
    o = json.load(open(inp, encoding="utf-8-sig"))
    o["ReferenceAssemblies"] = ra
    o["IsPass1"] = True
    o["CompileMode"] = "RealBuildPass1"
    json.dump(o, open(inp, "w", encoding="utf-8"), ensure_ascii=False)
    rc1 = run([XAMLCOMPILER, inp, outp], os.path.join(objdir, "p1.log"), cwd=APP_PROJ)
    r1 = json.load(open(outp, encoding="utf-8-sig")) if os.path.exists(outp) else {}
    gen_cs = [f for f in r1.get("GeneratedCodeFiles", []) if os.path.exists(f)]
    log("  pass1 rc=" + str(rc1) + " codegen=" + str(len(gen_cs)))
    if not gen_cs:
        for e in r1.get("MSBuildLogEntries", []):
            if e.get("Type") == 2:
                fail("xaml: " + str(e.get("ErrorCode")) + " " + str(e.get("Message"))[:200])
        if not problems:
            fail("xaml: pass1 没有生成任何代码文件")
        return

    # ---- csc
    srcs = sources(APP_PROJ)
    srcs += [os.path.join(APPSDK_INC, "WindowsAppRuntimeAutoInitializer.cs"),
             os.path.join(APPSDK_INC, "UndockedRegFreeWinRT-AutoInitializer.cs")]
    args = [DOTNET, CSC, "-nologo", "-target:exe", "-nullable:enable", "-langversion:latest",
            "-preferreduilang:en-US", "-nowarn:1591", "-nowarn:1701", "-out:" + dll,
            "-define:MICROSOFT_WINDOWSAPPSDK_AUTOINITIALIZE_UNDOCKEDREGFREEWINRT",
            "-define:DISABLE_XAML_GENERATED_MAIN"]
    if os.path.exists(MVVM_ANALYZER):
        args.append("-analyzer:" + MVVM_ANALYZER)
    args += ["-r:" + p for p in refs + [core_dll]]
    args += srcs + gen_cs
    csclog = os.path.join(objdir, "csc.log")
    rc = run(args, csclog)
    text = readlog(csclog)
    errs = [l for l in text.splitlines() if ": error " in l]
    if rc != 0 or errs:
        fail("app: csc rc=" + str(rc) + " errors=" + str(len(errs)))
        for l in errs[:25]:
            log("     " + l.strip()[:240])
        return
    log("  OK  " + dll)

    # ---- pass 2
    o["IsPass1"] = False
    o["CompileMode"] = "RealBuildPass2"
    o["LocalAssembly"] = [item(dll)]
    o["ReferenceAssemblies"] = ra + [item(dll)]
    json.dump(o, open(inp, "w", encoding="utf-8"), ensure_ascii=False)
    rc2 = run([XAMLCOMPILER, inp, outp], os.path.join(objdir, "p2.log"), cwd=APP_PROJ)
    r2 = json.load(open(outp, encoding="utf-8-sig")) if os.path.exists(outp) else {}
    xbf = [f for f in r2.get("GeneratedXbfFiles", []) if os.path.exists(f)]
    log("  pass2 rc=" + str(rc2) + " xbf=" + str(len(xbf)))
    if rc2 != 0 or not xbf:
        for e in r2.get("MSBuildLogEntries", []):
            if e.get("Type") == 2:
                fail("xaml p2: " + str(e.get("ErrorCode")) + " " + str(e.get("Message"))[:200])
        if not problems:
            fail("xaml: pass2 失败")
        return
    log("  XAML 编译完成：" + ", ".join(sorted(os.path.basename(f) for f in xbf)))


def main():
    what = (sys.argv[1] if len(sys.argv) > 1 else "core").lower()
    os.makedirs(BUILD, exist_ok=True)
    os.makedirs(OBJ, exist_ok=True)

    core = build_core()
    imaging = build_imaging() if what in ("checks", "app", "all") and core else None
    checks = None
    if what in ("checks", "all") and core:
        checks = build_checks(core, imaging or "")
        run_checks(checks)
    if what in ("app", "all") and core:
        build_app(core)

    log("")
    if problems:
        log("FAILED (" + str(len(problems)) + " 个问题)")
        return 1
    log("BUILD OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
