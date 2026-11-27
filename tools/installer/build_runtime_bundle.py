#!/usr/bin/env python3
"""Stage a local Windows test bundle from the reviewed build outputs."""

import argparse
import hashlib
import json
import re
import shutil
import xml.etree.ElementTree as ET
from pathlib import Path
from zipfile import ZipFile


def digest(path):
    sha = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            sha.update(block)
    return sha.hexdigest().upper()


def read_appx_manifest(path):
    with ZipFile(path) as package:
        if package.getinfo("AppxSignature.p7x").file_size == 0:
            raise ValueError(f"The package needs a signature: {path}")
        return ET.fromstring(package.read("AppxManifest.xml"))


def widget_identity(manifest, family):
    identity = manifest.find("{*}Identity")
    if identity.attrib["Name"] != "ONEXTweaks.Widget" or identity.attrib["ProcessorArchitecture"] != "x64":
        raise ValueError("Expected the x64 ONEXTweaks widget.")
    if not re.fullmatch(r"ONEXTweaks\.Widget_[a-z0-9]{13}", family):
        raise ValueError("Supply the package family verified by Windows.")
    return f"ONEXTweaks.Widget_{identity.attrib['Version']}_x64__{family.rsplit('_', 1)[1]}"


def version(value):
    return tuple(int(part) for part in value.split("."))


def find_frameworks(directory, widget_manifest):
    if directory is None:
        return []
    candidates = []
    for path in sorted(directory.glob("*.appx")):
        manifest = read_appx_manifest(path)
        if manifest.findtext("{*}Properties/{*}Framework") == "true":
            candidates.append((path, manifest.find("{*}Identity").attrib))
    frameworks = []
    for item in widget_manifest.findall("{*}Dependencies/{*}PackageDependency"):
        requirement = item.attrib
        matching = [(path, info) for path, info in candidates
                    if info["Name"] == requirement["Name"] and info["Publisher"] == requirement["Publisher"]
                    and info["ProcessorArchitecture"] in ("x64", "neutral")
                    and version(info["Version"]) >= version(requirement["MinVersion"])]
        if not matching:
            continue
        path, info = max(matching, key=lambda match: version(match[1]["Version"]))
        frameworks.append((path, info))
    return frameworks


def copy_regular(source, destination):
    if source.is_symlink() or not source.is_file():
        raise ValueError(f"Expected a regular build artifact: {source}")
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, destination)


def build(args):
    installer = Path(__file__).resolve().parent
    widget_manifest = read_appx_manifest(args.widget)
    full_name = widget_identity(widget_manifest, args.package_family)
    if args.rollback_widget:
        rollback_identity = widget_identity(read_appx_manifest(args.rollback_widget), args.package_family)
        if rollback_identity != "ONEXTweaks.Widget_0.29.1109.0_x64__3a23eh8tyg7na" or (
            full_name != "ONEXTweaks.Widget_0.29.1110.0_x64__3a23eh8tyg7na"
        ):
            raise ValueError("The bundled legacy recovery path supports the reviewed 1109 to 1110 pair.")
    frameworks = find_frameworks(args.framework_dir, widget_manifest)
    outputs = {
        "CompanionLauncher.exe": args.launcher,
        "ONEXTweaks.DependencyManager.exe": args.dependency_manager,
        "PowerAgent/ONEXTweaks.PowerAgent.exe": args.power_agent,
        "PowerAgent/home-oem-seed.json": installer / "PowerAgent.OemSeed.json",
    }
    for source in args.bridge.rglob("*"):
        if source.is_symlink():
            raise ValueError(f"Unexpected link in Bridge publish directory: {source}")
        if source.is_file() and source.suffix.lower() != ".pdb":
            outputs[(Path("WidgetBridge") / source.relative_to(args.bridge)).as_posix()] = source
    for name in ("ONEXTweaks.WidgetBridge.exe", "ONEXTweaks.WidgetBridge.dll", "ONEXTweaks.ControllerObserver.exe", "libVIIPER.dll", "coreclr.dll"):
        if f"WidgetBridge/{name}" not in outputs:
            raise ValueError(f"Self-contained x64 Bridge artifact missing: {name}")
    for name in ("Set-DirectGuideButton.ps1", "Set-GameBarStartPage.ps1"):
        outputs[f"Tools/{name}"] = installer.parent / name
    for name in ("Complete-ONEXTweaksRelease.ps1", "Uninstall-ONEXTweaksRelease.ps1", "OneXConsoleSetup.ps1", "OneXConsoleHandoff.ps1", "DriverPrerequisites.ps1", "GameBarPrerequisite.ps1"):
        outputs[f"Tools/Installer/{name}"] = installer / name

    args.output.mkdir()
    runtime = args.output / "Runtime"
    rows = []
    for relative, source in sorted(outputs.items()):
        destination = runtime / relative
        copy_regular(source, destination)
        rows.append({"Path": relative.replace("/", "\\"), "Sha256": digest(destination)})
    manifest = runtime / "runtime-manifest.json"
    manifest.write_text(json.dumps({"Schema": 1, "PackageFullName": full_name, "Files": rows}, indent=2) + "\n", encoding="utf-8")
    widget = args.output / args.widget.name
    copy_regular(args.widget, widget)
    supplied = []
    for source, info in frameworks:
        destination = args.output / "Dependencies" / info["ProcessorArchitecture"] / source.name
        copy_regular(source, destination)
        supplied.append({"Name": info["Name"], "Version": info["Version"], "File": source.name, "Sha256": digest(destination)})
    copy_regular(installer.parent / "Install-Startup.ps1", args.output / "Setup" / "Install-Startup.ps1")
    for name in ("Initialize-ONEXTweaksReleaseRuntime.ps1", "Install-ONEXTweaksWidget.ps1", "Uninstall-ONEXTweaksRelease.ps1", "Update-ONEXTweaksRuntime.ps1", "OneXConsoleSetup.ps1", "OneXConsoleHandoff.ps1", "DriverPrerequisites.ps1", "GameBarPrerequisite.ps1", "README.md"):
        copy_regular(installer / name, args.output / "Setup" / "Installer" / name)

    metadata = {"PackageFullName": full_name, "PackageFamilyName": args.package_family,
                "WidgetFile": widget.name, "WidgetSha256": digest(widget), "RuntimeManifestSha256": digest(manifest),
                "RuntimeFiles": len(rows), "Frameworks": supplied, "WindowsRegistrationVerified": False}
    if args.rollback_widget:
        rollback = args.output / "Rollback" / "ONEXTweaks-1109.msix"
        copy_regular(args.rollback_widget, rollback)
        for name in ("Update-ONEXTweaksRelease.ps1", "ONEXTweaksLegacy1109.ps1"):
            copy_regular(installer / name, args.output / "Setup" / "Installer" / name)
        metadata["RollbackFile"] = r"Rollback\ONEXTweaks-1109.msix"
        metadata["RollbackSha256"] = digest(rollback)
    (args.output / "test-bundle.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(metadata))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package-family", required=True)
    parser.add_argument("--framework-dir", type=Path)
    parser.add_argument("--rollback-widget", type=Path)
    for name in ("widget", "bridge", "launcher", "dependency-manager", "power-agent", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    build(parser.parse_args())
