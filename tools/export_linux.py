#!/usr/bin/env python3
"""Export a self-contained Linux .NET build and check package metadata without playing.

The check never constructs the world. GPU rendering, traversal and performance
are separate acceptance checks. Each attempt retains its own logs and user data.
"""
from __future__ import annotations
import argparse
import os
from pathlib import Path
import re
import shutil
import signal
import struct
import subprocess
import sys
import time
import uuid

REPO = Path(__file__).resolve().parents[1]
EXECUTABLE = "the-last-camp.x86_64"
DATA_DIR = "data_LastCamp_linuxbsd_x86_64"
RUNTIME_FILES = ("LastCamp.dll", "LastCamp.runtimeconfig.json", "GodotSharp.dll", "libhostfxr.so", "libcoreclr.so")
RUNTIME_PACK = re.compile(r'"runtimepack\.Microsoft\.NETCore\.App\.Runtime\.linux-x64/([0-9][0-9A-Za-z.\-]*)"')
RUNTIME_NOTICES = ("LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT")
DIAGNOSTIC = re.compile(r"(?im)^\s*(?:ERROR:|SCRIPT ERROR:|Unhandled exception\b)")
ANSI = re.compile(r"\x1b\[[0-?]*[ -/]*[@-~]")

class ExportFailure(RuntimeError):
    pass


def fresh_output(requested: str | None, repo: Path | None = None) -> Path:
    repo = repo or REPO
    local = repo / "local-data"
    if local.is_symlink():
        raise ExportFailure("local-data must not be a symlink")
    local.mkdir(exist_ok=True)
    ignore = local / ".gdignore"
    if ignore.is_symlink() or ignore.is_dir():
        raise ExportFailure("local-data/.gdignore must be a regular file")
    ignore.touch()
    if requested:
        candidate = Path(os.path.abspath(repo / requested))
    else:
        candidate = local / f"export-linux-{time.strftime('%Y%m%d-%H%M%S')}-{uuid.uuid4().hex[:8]}"
    if local not in candidate.parents:
        raise ExportFailure(f"output must be beneath this repository's local-data: {candidate}")
    if candidate.exists() or candidate.is_symlink():
        raise ExportFailure(f"output must be fresh; refusing to overwrite: {candidate}")
    parent = candidate.parent
    if not parent.is_dir():
        raise ExportFailure(f"output parent must already exist: {parent}")
    while parent != local:
        if parent.is_symlink():
            raise ExportFailure(f"output parent must not be a symlink: {parent}")
        parent = parent.parent
    candidate.mkdir()
    return candidate


class Budget:
    def __init__(self, seconds: int) -> None:
        self.deadline = time.monotonic() + seconds
        self.seconds = seconds

    def run(self, label: str, command: list[str], cwd: Path, logs: Path, env: dict[str, str] | None = None) -> str:
        remaining = self.deadline - time.monotonic()
        if remaining <= 0:
            raise TimeoutError(f"{label}: attempt exceeded {self.seconds}s")
        # A private session, so this invocation's descendants can be stopped without matching names.
        process = subprocess.Popen(command, cwd=cwd, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                   text=True, start_new_session=True)
        try:
            stdout, stderr = process.communicate(timeout=remaining)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGKILL)
            stdout, stderr = process.communicate()
            (logs / f"{label}.out.txt").write_text(stdout)
            (logs / f"{label}.err.txt").write_text(stderr)
            raise TimeoutError(f"{label} exceeded the {self.seconds}s attempt budget")
        finally:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
        (logs / f"{label}.out.txt").write_text(stdout)
        (logs / f"{label}.err.txt").write_text(stderr)
        text = ANSI.sub("", stdout + "\n" + stderr)
        # Godot may report an import or packing failure while its process exits 0.
        if process.returncode != 0 or DIAGNOSTIC.search(text):
            tail = "\n".join(text.strip().splitlines()[-16:])
            raise ExportFailure(f"{label} failed (exit {process.returncode}); logs: {logs}\n{tail}")
        return stdout


def confirm_package(game: Path) -> Path:
    executable = game / EXECUTABLE
    if not executable.is_file():
        raise ExportFailure("no executable produced")
    header = executable.read_bytes()[:64]
    if (len(header) < 64 or header[:7] != b"\x7fELF\x02\x01\x01"
            or struct.unpack_from("<H", header, 16)[0] not in (2, 3) or struct.unpack_from("<H", header, 18)[0] != 62):
        raise ExportFailure("expected a little-endian ELF64 x86-64 executable")
    if not os.access(executable, os.X_OK):
        raise ExportFailure("Linux executable is missing its execute bit")
    data = game / DATA_DIR
    for required in RUNTIME_FILES:
        if not (data / required).is_file():
            raise ExportFailure(f"the self-contained .NET payload lacks {required}")
    for item in game.rglob("*"):
        if item.is_symlink() or item.suffix.lower() in {".cs", ".pdb", ".csproj", ".sln"}:
            raise ExportFailure(f"unexpected linked, source or debug package content: {item.relative_to(game)}")
        managed = item.suffix.lower() == ".dll" or re.search(r"\.(runtimeconfig|deps)\.json$|^lib(hostfxr|hostpolicy|coreclr)\.so$",
                                                             item.name)
        if managed and data not in item.parents:
            raise ExportFailure(f".NET runtime artifact outside {DATA_DIR}: {item.relative_to(game)}")
    return executable


def copy_runtime_notices(game: Path) -> list[Path]:
    """The bundled .NET runtime's licence and third-party notices, from the runtime pack named in the package."""
    deps = game / DATA_DIR / "LastCamp.deps.json"
    versions = set(RUNTIME_PACK.findall(deps.read_text(encoding="utf-8"))) if deps.is_file() else set()
    if len(versions) != 1:
        raise ExportFailure(f"expected one .NET runtime pack in {deps.name}, found {sorted(versions) or 'none'}")
    packages = Path(os.environ.get("NUGET_PACKAGES") or Path.home() / ".nuget/packages")
    pack = packages / "microsoft.netcore.app.runtime.linux-x64" / versions.pop()
    copied = []
    for name in RUNTIME_NOTICES:
        source = pack / name
        if not source.is_file():
            raise ExportFailure(f"the .NET runtime pack has no {name}: {pack}")
        target = game / f"DOTNET-RUNTIME-{name}"
        shutil.copyfile(source, target)
        copied.append(target)
    return copied


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", help="fresh directory beneath local-data/")
    parser.add_argument("--godot", default=os.environ.get("GODOT", "godot"), help="Godot 4.8 dev6 .NET executable")
    parser.add_argument("--timeout", type=int, default=900)
    args = parser.parse_args(argv)
    if not 1 <= args.timeout <= 7200:
        parser.error("timeout must be 1–7200 seconds")
    try:
        engine = shutil.which(args.godot)
        if engine is None or shutil.which("dotnet") is None:
            raise ExportFailure("Godot .NET and the .NET 8 SDK must be on PATH")
        output = fresh_output(args.output_dir)
        game, logs = output / "game", output / "logs"
        game.mkdir(); logs.mkdir()
        print(f"Export files retained: {output}", flush=True)
        budget = Budget(args.timeout)
        user = logs / "user"
        env = {**os.environ, "TMPDIR": str(logs), "XDG_DATA_HOME": str(user / "data"),
               "XDG_CONFIG_HOME": str(user / "config"), "XDG_CACHE_HOME": str(user / "cache")}
        templates = Path(os.environ.get("XDG_DATA_HOME") or Path.home() / ".local/share") / "godot/export_templates"
        if not (templates / "4.8.dev6.mono").is_dir():
            raise ExportFailure("Install matching Godot 4.8 dev6 .NET export templates first")
        template_link = user / "data/godot/export_templates"
        template_link.parent.mkdir(parents=True)
        template_link.symlink_to(templates, target_is_directory=True)
        version = budget.run("version", [engine, "--version"], REPO, logs, env).strip()
        if not version.startswith("4.8.dev6.mono."):
            raise ExportFailure(f"Expected Godot 4.8 dev6 .NET, got {version}")
        budget.run("build", ["dotnet", "build", "--nologo"], REPO, logs, env)
        budget.run("export", [engine, "--headless", "--path", str(REPO), "--frame-delay", "1000",
                              "--export-release", "Linux", str(game / EXECUTABLE)], REPO, logs, env)
        executable = confirm_package(game)
        for name in ("LICENSE", "THIRD_PARTY_NOTICES.md"):
            shutil.copyfile(REPO / name, game / name)
        copy_runtime_notices(game)
        text = budget.run("package-check", [str(executable), "--headless", "--log-file", str(logs / "engine.log"),
            "--", "--package-check", f"--notices={game / 'GODOT-NOTICES.txt'}"], game, logs, env)
        if "EXPORT_CHECK result=PASS" not in text or "PACKAGE_MODE world_children=0" not in text:
            raise ExportFailure(f"Package check did not prove its metadata-only path: {logs}")
        credits = ["# The Last Camp — credits\n\nEngine notices: GODOT-NOTICES.txt. "
                   ".NET notices: DOTNET-RUNTIME-LICENSE.TXT and DOTNET-RUNTIME-THIRD-PARTY-NOTICES.TXT.\n"]
        credits.extend((REPO / name).read_text() for name in
            ("textures/SOURCES.md", "audio/SOURCES.md", "models/SOURCES.md", "THIRD_PARTY_NOTICES.md"))
        (game / "credits.md").write_text("\n".join(credits))
        (game / "README.md").write_text("""# The Last Camp — Linux x86-64

Run `./the-last-camp.x86_64` in a graphical session with a Vulkan-capable GPU.
Use `-- --quality=medium` for a fixed lighter preset. Controls and source:
https://github.com/dlprentice/the-last-camp

Keep the executable, data_LastCamp_linuxbsd_x86_64 directory, credits and all
license/notice files together. The .NET runtime is included; no editor or SDK
installation is needed. This package's automated check covers bundled resources
and notices. It does not establish visual quality or gameplay performance.
""")
        print(f"EXPORT_PACKAGE PASS {executable}")
        return 0
    except TimeoutError as error:
        print(f"EXPORT_PACKAGE FAIL: {error}", file=sys.stderr); return 124
    except (ExportFailure, OSError) as error:
        print(f"EXPORT_PACKAGE FAIL: {error}", file=sys.stderr); return 1


if __name__ == "__main__":
    sys.exit(main())
