#!/usr/bin/env python3
"""Assemble the embedded input for ONEXTweaks Setup from a versioned runtime bundle."""

import argparse
import hashlib
import json
import re
import ssl
import subprocess
import sys
from pathlib import Path, PureWindowsPath
from zipfile import ZIP_DEFLATED, ZipFile

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from build_runtime_bundle import digest, read_appx_manifest, widget_identity  # noqa: E402

BETA_PUBLISHER = "CN=ONEXTweaks Development"
BETA_CERTIFICATE_SHA256 = "3528C48A4FDA614E65867847122ACC776F4AAC1127FF898259D9D9DBCF2D7263"


def verify_beta_certificate(package):
    with ZipFile(package) as msix:
        signature = msix.read("AppxSignature.p7x")
    if signature[:4] != b"PKCX":
        raise ValueError(f"Invalid MSIX signature header: {package.name}")
    pem = subprocess.check_output(["openssl", "pkcs7", "-inform", "DER", "-print_certs"], input=signature[4:])
    certificates = re.findall(rb"-----BEGIN CERTIFICATE-----.*?-----END CERTIFICATE-----", pem, re.S)
    if len(certificates) != 1:
        raise ValueError(f"Expected the single ONEXTweaks beta signing certificate: {package.name}")
    der = ssl.PEM_cert_to_DER_cert(certificates[0].decode("ascii"))
    if hashlib.sha256(der).hexdigest().upper() != BETA_CERTIFICATE_SHA256:
        raise ValueError(f"MSIX signing certificate does not match the ONEXTweaks beta certificate: {package.name}")


def relative_path(value):
    path = PureWindowsPath(value)
    if path.anchor or any(
        not re.fullmatch(r"[A-Za-z0-9_ .-]+", part)
        or part == ".." or part != part.strip() or part.endswith(".")
        for part in path.parts
    ):
        raise ValueError(f"Invalid package path: {value}")
    return path.as_posix()


def build(bundle, output, development, beta=False):
    source = Path(__file__).resolve().parent
    metadata = json.loads((bundle / "test-bundle.json").read_text(encoding="utf-8-sig"))
    widget = relative_path(metadata["WidgetFile"])
    if "/" in widget:
        raise ValueError("Place the widget MSIX in the root of the bundle.")
    manifest = read_appx_manifest(bundle / widget)
    identity = manifest.find("{*}Identity").attrib
    version = identity["Version"]
    if not re.fullmatch(r"[0-9]+(?:\.[0-9]+){3}", version):
        raise ValueError("Invalid widget version.")
    if widget_identity(manifest, metadata["PackageFamilyName"]) != metadata["PackageFullName"]:
        raise ValueError("Widget metadata does not match the package identity.")
    if beta and identity["Publisher"] != BETA_PUBLISHER:
        raise ValueError("The beta trust prompt requires the ONEXTweaks beta publisher.")
    if identity["Publisher"] == BETA_PUBLISHER and not (development or beta):
        raise ValueError("Select --beta or --development for this MSIX, or supply a release-signed MSIX.")

    inputs = {}

    def add(name, source_file, expected=None):
        if name.casefold() in inputs or source_file.is_symlink() or not source_file.is_file():
            raise ValueError(f"Missing or duplicate payload file: {name}")
        if expected is not None and digest(source_file) != expected.upper():
            raise ValueError(f"Payload file checksum differs: {name}")
        inputs[name.casefold()] = (name, source_file)

    add(widget, bundle / widget, metadata["WidgetSha256"])
    if beta:
        verify_beta_certificate(bundle / widget)
    manifest_name = "Runtime/runtime-manifest.json"
    add(manifest_name, bundle / manifest_name, metadata["RuntimeManifestSha256"])
    runtime = json.loads((bundle / manifest_name).read_text(encoding="utf-8-sig"))
    if runtime["PackageFullName"] != metadata["PackageFullName"] or len(runtime["Files"]) != metadata["RuntimeFiles"]:
        raise ValueError("Runtime metadata does not match the widget.")
    for row in runtime["Files"]:
        name = "Runtime/" + relative_path(row["Path"])
        add(name, bundle / name, row["Sha256"])
    for framework in metadata["Frameworks"]:
        name = relative_path(framework["File"])
        if "/" in name:
            raise ValueError("Expected a framework filename.")
        files = [f"Dependencies/{arch}/{name}" for arch in ("x64", "neutral")
                 if (bundle / f"Dependencies/{arch}/{name}").is_file()]
        if len(files) != 1:
            raise ValueError(f"Expected exactly one copy of {name}.")
        add(files[0], bundle / files[0], framework["Sha256"])
    scripts = ["Initialize-ONEXTweaksReleaseRuntime.ps1", "Install-ONEXTweaksWidget.ps1",
               "Uninstall-ONEXTweaksRelease.ps1", "DriverPrerequisites.ps1", "OneXConsoleSetup.ps1",
               "OneXConsoleHandoff.ps1", "GameBarPrerequisite.ps1"]
    if "RollbackFile" in metadata:
        rollback = relative_path(metadata["RollbackFile"])
        add(rollback, bundle / rollback, metadata["RollbackSha256"])
        if beta:
            verify_beta_certificate(bundle / rollback)
        scripts.extend(("Update-ONEXTweaksRelease.ps1", "ONEXTweaksLegacy1109.ps1"))
    for name in ["Setup/Install-Startup.ps1", "test-bundle.json", *(f"Setup/Installer/{item}" for item in scripts)]:
        add(name, bundle / name)
    add("Invoke-ONEXTweaksSetup.ps1", source / "Invoke-ONEXTweaksSetup.ps1")

    output.mkdir(parents=True)
    archive = output / "Payload.zip"
    with ZipFile(archive, "x", compression=ZIP_DEFLATED, compresslevel=6) as payload:
        for name, path in sorted(inputs.values()):
            payload.write(path, name)
    archive_hash = digest(archive)
    (output / "PayloadInfo.cs").write_text(
        'using System.Reflection;\n'
        '[assembly: AssemblyTitle("ONEXTweaks Setup")]\n'
        '[assembly: AssemblyCompany("ONEXTweaks")]\n'
        f'[assembly: AssemblyFileVersion("{version}")]\n'
        'internal static class PayloadInfo {\n'
        f'    internal const string Sha256 = "{archive_hash}";\n'
        f'    internal const string WidgetFile = "{widget}";\n'
        f'    internal const string BetaCertificateSha256 = "{BETA_CERTIFICATE_SHA256 if beta else ""}";\n'
        '}\n',
        encoding="utf-8",
    )
    for name in ("Setup.cs", "setup.manifest", "Build-ONEXTweaksSetup.ps1"):
        (output / name).write_bytes((source / name).read_bytes())
    (output / "build.json").write_text(json.dumps({
        "PackageFullName": metadata["PackageFullName"], "Publisher": identity["Publisher"],
        "Development": development, "Beta": beta, "PayloadSha256": archive_hash,
    }, indent=2) + "\n", encoding="utf-8")
    print(f"{archive} ({len(inputs)} files, SHA-256 {archive_hash})")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("bundle", type=Path)
    parser.add_argument("output", type=Path)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--development", action="store_true", help="Build for machines that already trust the test certificate")
    mode.add_argument("--beta", action="store_true", help="Prompt to trust the pinned ONEXTweaks certificate on first install")
    options = parser.parse_args()
    build(options.bundle, options.output, options.development, options.beta)
