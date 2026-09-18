#!/usr/bin/env python3
# Copyright (c) 2026 VSCN-Studio
# SPDX-License-Identifier: MIT

"""Portable native/managed build and packaging. Python 3.10+, no pip packages."""
import argparse
import hashlib
import json
import os
import platform
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parent
REVISION = "3045e6e3510425ef2870f7647b3f59d3ae9970f5"
FILES = {"win": "vsrmlui_native.dll", "linux": "libvsrmlui_native.so", "osx": "libvsrmlui_native.dylib"}
SUPPORTED = ("win-x64", "linux-x64", "osx-x64", "osx-arm64")
VERSION = "1.0.0"
REQUIRED = ("win-x64", "linux-x64")


def bridge_hash():
    digest = hashlib.sha256()
    for name in ("native/CMakeLists.txt", "native/bridge.h", "native/bridge.cpp"):
        digest.update(name.encode())
        # Git/Windows checkout newline conversion must not change the fingerprint.
        digest.update((ROOT / name).read_text(encoding="utf-8").encode())
    return digest.hexdigest()


def host_rid():
    os_name = {"Windows": "win", "Linux": "linux", "Darwin": "osx"}.get(platform.system())
    arch = {"AMD64": "x64", "x86_64": "x64", "aarch64": "arm64", "arm64": "arm64"}.get(platform.machine())
    rid = f"{os_name}-{arch}"
    if struct.calcsize("P") != 8 or rid not in SUPPORTED:
        raise RuntimeError(f"Unsupported build host: {platform.system()} {platform.machine()}")
    return rid


def run(*args):
    print("+", subprocess.list2cmdline([str(a) for a in args]), flush=True)
    subprocess.run([str(a) for a in args], cwd=ROOT, check=True)


def copy(source, destination):
    if source.is_dir():
        shutil.copytree(source, destination, dirs_exist_ok=True)
    else:
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)


def archive(source, destination):
    # Forward-slash member names on every host. Only replace after a successful zip.
    with tempfile.NamedTemporaryFile(dir=destination.parent, suffix=".zip", delete=False) as temp:
        temporary = Path(temp.name)
    try:
        with zipfile.ZipFile(temporary, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as output:
            for path in sorted(source.rglob("*")):
                if path.is_file():
                    relative = path.relative_to(source)
                    output.write(path, relative.as_posix())
        temporary.replace(destination)
    finally:
        temporary.unlink(missing_ok=True)
    print(f"{hashlib.sha256(destination.read_bytes()).hexdigest()}  {destination}")


def package_merged(native_root):
    """Require Windows/Linux; include validated macOS builds and the optional test mod."""
    native_root = native_root.resolve()
    missing = [rid for rid in REQUIRED if not (native_root / rid / FILES[rid.split('-')[0]]).is_file()]
    if missing:
        raise RuntimeError("Cannot create the Windows/Linux package. Missing native builds: " + ", ".join(missing) + ". Build them on the target hosts with --native-only, then run --package-only.")
    managed = ROOT / "artifacts" / "managed"
    info = json.loads((managed / "manifest.json").read_text(encoding="utf-8"))
    dll = managed / "VSRmlUi.dll"
    if info.get("version") != VERSION or info.get("sha256") != hashlib.sha256(dll.read_bytes()).hexdigest():
        raise RuntimeError("Managed build does not match 1.0.0. Run --prepare-only first.")
    if json.loads((ROOT / "src/VSRmlUi/modinfo.json").read_text(encoding="utf-8"))["version"] != VERSION:
        raise RuntimeError("modinfo.json version must be fixed at 1.0.0")
    with tempfile.TemporaryDirectory(prefix="package-", dir=ROOT / "build") as temporary:
        mod = Path(temporary)
        for name in ("VSRmlUi.dll",):
            copy(managed / name, mod / name)
        for name in ("modinfo.json", "modicon.png", "assets"):
            copy(ROOT / "src/VSRmlUi" / name, mod / name)
        included = {}
        for rid in SUPPORTED:
            folder = native_root / rid
            if not folder.exists():
                continue
            manifest = json.loads((folder / "manifest.json").read_text(encoding="utf-8"))
            binary = folder / FILES[rid.split('-')[0]]
            if manifest.get("rid") != rid or manifest.get("rmlui") != REVISION or manifest.get("bridgeSource") != bridge_hash() or manifest.get("sha256") != hashlib.sha256(binary.read_bytes()).hexdigest():
                raise RuntimeError(f"Native artifact manifest mismatch: {folder}")
            if not manifest.get("nativeSmoke"):
                raise RuntimeError(f"Native ABI smoke test is required: {folder}")
            copy(binary, mod / "native" / rid / binary.name)
            included[rid] = manifest
        # The game only needs the assembly, metadata, assets and native binaries.
        # Copyright, attribution and license texts accompany the distributed components.
        copy(ROOT / "LICENSE", mod / "LICENSE")
        copy(ROOT / "COPYRIGHT.txt", mod / "COPYRIGHT.txt")
        license_stage = mod / "licenses"
        for license_file in (ROOT / "licenses").iterdir():
            if license_file.is_file():
                copy(license_file, license_stage / license_file.name)
        archive(mod, ROOT / "artifacts" / f"vsrmlui_{VERSION}.zip")
        print("Packaged native RIDs:", ", ".join(included))
    package_test_mod()


def package_test_mod():
    """Package the example/input diagnostics mod when a managed build staged it."""
    staged = ROOT / "artifacts" / "example"
    required = (staged / "VSRmlUi.Example.dll", staged / "modinfo.json")
    if not all(path.is_file() for path in required):
        return
    with tempfile.TemporaryDirectory(prefix="package-test-", dir=ROOT / "build") as temporary:
        mod = Path(temporary)
        for path in required:
            copy(path, mod / path.name)
        if (staged / "assets").is_dir():
            copy(staged / "assets", mod / "assets")
        copy(ROOT / "LICENSE", mod / "LICENSE")
        copy(ROOT / "COPYRIGHT.txt", mod / "COPYRIGHT.txt")
        copy(ROOT / "licenses", mod / "licenses")
        archive(mod, ROOT / "artifacts" / f"vsrmlui-test_{VERSION}.zip")
    print("Packaged input diagnostics mod: vsrmlui-test_" + VERSION + ".zip")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-directory", type=Path, default=ROOT.parent / "Vintagestory")
    parser.add_argument("--game-native-directory", type=Path, help="Game GLFW/Skia folder; default GameDirectory/Lib")
    parser.add_argument("--rmlui-source", type=Path, default=ROOT.parent / "RmlUi")
    parser.add_argument("--rid", choices=SUPPORTED, help="Defaults to host; cross compilation is rejected")
    parser.add_argument("--native-only", action="store_true", help="Build and smoke-test the C ABI without game/.NET")
    parser.add_argument("--prepare-only", action="store_true", help="Build/test and stage managed/native files without creating a ZIP")
    parser.add_argument("--package-only", action="store_true", help="Combine staged 1.0.0 managed files and target-host native builds without recompiling")
    parser.add_argument("--skip-tests", action="store_true")
    parser.add_argument("--headless-tests", action="store_true", help="Skip only managed OpenGL checks")
    parser.add_argument("--bundle-native", type=Path, default=ROOT / "artifacts/native", help="Collected <rid>/<library> artifacts; defaults to artifacts/native")
    parser.add_argument("--jobs", type=int, default=min(os.cpu_count() or 2, 8))
    args = parser.parse_args()
    if sum((args.native_only, args.prepare_only, args.package_only)) > 1:
        parser.error("--native-only, --prepare-only and --package-only are mutually exclusive")
    if args.package_only:
        package_merged(args.bundle_native)
        return
    rid = args.rid or host_rid()
    if rid != host_rid():
        parser.error("Build on the target OS/process architecture; combine builds with --bundle-native.")
    if args.jobs < 1:
        parser.error("--jobs must be positive")
    os_name = rid.split("-")[0]
    source = args.rmlui_source.resolve()
    revision = subprocess.check_output(["git", "-C", str(source), "rev-parse", "HEAD"], text=True).strip()
    if revision != REVISION:
        raise RuntimeError(f"RmlUi must be pinned to {REVISION}; got {revision}")
    cmake = shutil.which("cmake")
    if not cmake and os_name == "win":
        candidates = sorted(Path("C:/Program Files/Microsoft Visual Studio").glob("*/*/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe"))
        if candidates:
            cmake = str(candidates[-1])
    if not cmake:
        raise RuntimeError("Install CMake 3.24+ and put it on PATH.")
    native_build = ROOT / "build" / "native" / rid
    configure = [cmake, "-S", ROOT / "native", "-B", native_build, "-DCMAKE_BUILD_TYPE=Release", f"-DRMLUI_SOURCE_DIR={source}"]
    configure += ["-A", "x64"] if os_name == "win" else ["-G", "Ninja"]
    if os_name == "osx":
        configure += [f"-DCMAKE_OSX_ARCHITECTURES={'arm64' if rid.endswith('arm64') else 'x86_64'}", "-DCMAKE_OSX_DEPLOYMENT_TARGET=11.0"]
    run(*configure)
    run(cmake, "--build", native_build, "--config", "Release", "--target", "vsrmlui_native", "--parallel", args.jobs)
    native_file = native_build / "out" / "Release" / FILES[os_name]
    if os_name == "osx":
        run("codesign", "--force", "--sign", "-", native_file)
        run("codesign", "--verify", native_file)
    if not args.skip_tests:
        run(sys.executable, ROOT / "tests" / "native_smoke.py", native_file)
    native_stage = ROOT / "artifacts" / "native" / rid
    copy(native_file, native_stage / native_file.name)
    manifest = {"rid": rid, "rmlui": revision, "bridgeSource": bridge_hash(), "sha256": hashlib.sha256(native_file.read_bytes()).hexdigest(), "nativeSmoke": not args.skip_tests}
    (native_stage / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    if args.native_only:
        return
    game = args.game_directory.resolve()
    if not (game / "VintagestoryAPI.dll").is_file():
        raise RuntimeError("--game-directory must contain VintagestoryAPI.dll")
    props = [f"-p:GameDirectory={game}"]
    for project in ("src/VSRmlUi/VSRmlUi.csproj", "examples/VSRmlUi.Example/VSRmlUi.Example.csproj"):
        run("dotnet", "build", ROOT / project, "-c", "Release", *props, "--nologo")
    if not args.skip_tests:
        test_props = [*props, f"-p:NativeRid={rid}", f"-p:NativeBuildDirectory={native_build}"]
        if args.game_native_directory:
            test_props += [f"-p:GameNativeDirectory={args.game_native_directory.resolve()}"]
        run("dotnet", "run", "--project", ROOT / "tests/VSRmlUi.Tests", "-c", "Release", *test_props, "--", ROOT, f"--game={game}", *(["--headless"] if args.headless_tests else []))
    mod_output = ROOT / "src/VSRmlUi/bin/Release/net10.0"
    managed = ROOT / "artifacts/managed"
    for name in ("VSRmlUi.dll", "VSRmlUi.xml"):
        copy(mod_output / name, managed / name)
        copy(mod_output / name, ROOT / "artifacts/sdk" / name)
    example_output = ROOT / "examples/VSRmlUi.Example/bin/Release/net10.0"
    for name in ("VSRmlUi.Example.dll", "modinfo.json"):
        copy(example_output / name, ROOT / "artifacts/example" / name)
    copy(example_output / "assets", ROOT / "artifacts/example/assets")
    metadata = {"version": VERSION, "sha256": hashlib.sha256((managed / "VSRmlUi.dll").read_bytes()).hexdigest(), "managedTests": not args.skip_tests, "graphicsTests": not args.skip_tests and not args.headless_tests}
    (managed / "manifest.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    if args.prepare_only:
        print("Prepared managed/native build inputs; no release ZIP created.")
        return
    package_merged(args.bundle_native)


if __name__ == "__main__":
    try:
        main()
    except (OSError, RuntimeError, subprocess.CalledProcessError) as error:
        sys.exit(str(error))
