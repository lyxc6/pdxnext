#!/usr/bin/env python3
"""Build the PdxNext installer and update manifest.

The payload is the already-published, self-contained directory produced by
publish.cmd (dist/PdxNext). This script never rebuilds the app; it only:
  1. validates the XAML artifacts are present,
  2. locates Inno Setup 7's ISCC.exe,
  3. compiles installer/PdxNext.iss,
  4. writes dist/installer/update.json for GitHub Releases.

Usage:
  python tools/build-installer.py
  python tools/build-installer.py --version 1.1.0 --notes "Fixed ..."
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = Path(__file__).resolve().parent.parent
VERSION_PROPS = ROOT / "version.props"
ISS = ROOT / "installer" / "PdxNext.iss"
DEFAULT_PAYLOAD = ROOT / "dist" / "PdxNext"
DEFAULT_OUTPUT = ROOT / "dist" / "installer"
DEFAULT_REPO = "lyxc6/pdxnext"
REQUIRED_PAYLOAD = [
    "PdxLauncher.App.exe",
    "PdxLauncher.App.pri",
    "App.xbf",
    "Views/HomePage.xbf",
    "Views/SettingsPage.xbf",
    "Controls/ConfirmOverlay.xbf",
    "Assets/AppIcon.ico",
]


def read_props_version() -> tuple[str, str]:
    text = VERSION_PROPS.read_text(encoding="utf-8")
    version = re.search(r"<Version>([^<]+)</Version>", text)
    file_version = re.search(r"<FileVersion>([^<]+)</FileVersion>", text)
    if not version:
        raise SystemExit("version.props does not contain <Version>")
    return version.group(1).strip(), (file_version.group(1).strip() if file_version else version.group(1) + ".0")


def find_iscc() -> Path:
    candidates: list[Path] = []
    env = os.environ.get("INNO_SETUP_ISCC")
    if env:
        candidates.append(Path(env))
    for env_name in ("ProgramFiles", "ProgramFiles(x86)", "LOCALAPPDATA"):
        base = os.environ.get(env_name)
        if not base:
            continue
        candidates.append(Path(base) / "Inno Setup 7" / "ISCC.exe")
        candidates.append(Path(base) / "Programs" / "Inno Setup 7" / "ISCC.exe")
    for candidate in candidates:
        if candidate.is_file():
            return candidate
    raise SystemExit(
        "ISCC.exe (Inno Setup 7) not found. Install it with:\n"
        "  winget install --id JRSoftware.InnoSetup.7 -e -s winget\n"
        "or set INNO_SETUP_ISCC to the full path."
    )


def validate_payload(payload: Path) -> None:
    if not payload.is_dir():
        raise SystemExit(f"payload directory not found: {payload}\nRun publish.cmd first.")
    missing = [name for name in REQUIRED_PAYLOAD if not (payload / name).is_file()]
    if missing:
        raise SystemExit("payload is missing XAML artifacts: " + ", ".join(missing) + "\nRun publish.cmd again.")


def stage_payload(payload: Path, root: Path) -> Path:
    """Copy only program files; data, logs and caches must never enter the installer."""
    staged = root / ".tmp" / "installer-payload"
    if staged.exists():
        shutil.rmtree(staged)
    ignore = shutil.ignore_patterns(
        "data",
        "data-root.txt",
        "update-cache",
        "install-mode.txt",
        "*.log",
        "*.pdb",
    )
    shutil.copytree(payload, staged, ignore=ignore)
    validate_payload(staged)
    return staged


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", help="override version.props")
    parser.add_argument("--file-version", help="override FileVersion")
    parser.add_argument("--payload", type=Path, default=DEFAULT_PAYLOAD)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--repo", default=DEFAULT_REPO, help="GitHub release repository")
    parser.add_argument("--tag", help="release tag, default v<version>")
    parser.add_argument("--notes", default="", help="short release notes for update.json")
    parser.add_argument("--notes-url", help="release page URL")
    parser.add_argument("--mandatory", action="store_true")
    args = parser.parse_args()

    props_version, props_file_version = read_props_version()
    version = args.version or props_version
    file_version = args.file_version or (props_file_version if not args.version else version + ".0")
    tag = args.tag or ("v" + version)
    payload = args.payload.resolve()
    output = args.output.resolve()
    validate_payload(payload)
    payload = stage_payload(payload, ROOT)
    output.mkdir(parents=True, exist_ok=True)

    iscc = find_iscc()
    setup_name = f"PdxNext-Setup-{version}.exe"
    setup_path = output / setup_name
    print(f"[installer] Inno Setup: {iscc}")
    print(f"[installer] payload:   {payload}")
    print(f"[installer] version:   {version} ({file_version})")

    command = [
        str(iscc),
        f"--define=Version={version}",
        f"--define=FileVersion={file_version}",
        f"--define=PayloadDir={payload}",
        f"--define=OutputDir={output}",
        str(ISS),
    ]
    completed = subprocess.run(command, cwd=ROOT)
    if completed.returncode != 0:
        return completed.returncode
    if not setup_path.is_file():
        raise SystemExit(f"ISCC finished but did not produce {setup_path}")

    digest = sha256(setup_path)
    size = setup_path.stat().st_size
    notes_url = args.notes_url or f"https://github.com/{args.repo}/releases/tag/{tag}"
    manifest = {
        "schemaVersion": 1,
        "version": version,
        "setupUrl": f"https://github.com/{args.repo}/releases/download/{tag}/{setup_name}",
        "sha256": digest,
        "size": size,
        "releaseNotesUrl": notes_url,
        "releaseNotes": args.notes or f"PdxNext {version}",
        "mandatory": bool(args.mandatory),
        "publishedUtc": datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
    }
    manifest_path = output / "update.json"
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    print()
    print(f"[installer] setup:    {setup_path}")
    print(f"[installer] size:     {size} bytes")
    print(f"[installer] sha256:   {digest}")
    print(f"[installer] manifest: {manifest_path}")
    print(json.dumps(manifest, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
