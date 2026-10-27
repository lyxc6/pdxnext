#!/usr/bin/env bash
# 串行化的 dotnet 构建：多个智能体同时 build 同一个 obj/bin 会互相占用文件。
# 用法：bash tools/build-app.sh [额外的 dotnet build 参数...]
# 默认构建应用工程（Release / x64 由工程自身的 Platforms 决定）。
set -u

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOCK="${TMPDIR:-/tmp}/pdxnext-build.lock"
DEADLINE=$(( $(date +%s) + 900 ))

while ! mkdir "$LOCK" 2>/dev/null; do
    if [ "$(date +%s)" -gt "$DEADLINE" ]; then
        echo "[build-lock] 等待超时，直接构建（可能与其他构建冲突）" >&2
        break
    fi
    sleep 2
done
trap 'rmdir "$LOCK" 2>/dev/null' EXIT INT TERM

cd "$ROOT" || exit 1
python tools/gen-i18n.py || exit 1
dotnet build "$ROOT/src/PdxLauncher.App/PdxLauncher.App.csproj" -c Release -m:1 --nologo "$@" 2>&1 | tail -n 20
exit "${PIPESTATUS[0]}"
