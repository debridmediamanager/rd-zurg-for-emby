#!/usr/bin/env python3
"""Checks that the built plugin is one self-contained DLL and nothing else.

Emby loads a plugin as a single assembly from its plugins directory: anything else the build emits
would either be ignored or, worse, shadow one of Emby's own assemblies.
"""
import pathlib
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

root = pathlib.Path(__file__).resolve().parent.parent
project = root / "src/Emby.Plugin.RdZurg/Emby.Plugin.RdZurg.csproj"
version = ET.parse(project).findtext(".//Version")
build = root / "src/Emby.Plugin.RdZurg/bin/Release/net8.0"
package = root / f"artifacts/rd-zurg-for-emby_{version}"

problems = []

extra = sorted(p.name for p in build.glob("*.dll") if p.name != "Emby.Plugin.RdZurg.dll")
if extra:
    problems.append(f"the build emitted assemblies besides the plugin: {extra}")

shipped = sorted(p.name for p in package.iterdir()) if package.exists() else []
if shipped != ["Emby.Plugin.RdZurg.dll", "Emby.Plugin.RdZurg.dll.sha256"]:
    problems.append(f"the package holds {shipped}")

dll = package / "Emby.Plugin.RdZurg.dll"
if dll.exists():
    if dll.read_bytes() != (build / "Emby.Plugin.RdZurg.dll").read_bytes():
        problems.append("the packaged DLL is not the one that was built")
    digest = subprocess.run(["shasum", "-a", "256", dll.name], cwd=package, capture_output=True, text=True).stdout
    if digest.split()[0] != (package / "Emby.Plugin.RdZurg.dll.sha256").read_text().split()[0]:
        problems.append("the checksum does not match the DLL")
    blob = dll.read_bytes()
    if version.encode("utf-16-le") not in blob and version.encode() not in blob:
        problems.append(f"the DLL does not carry version {version}")
    for name in (b"MediaBrowser.Controller", b"Emby.Web.GenericEdit"):
        if re.search(rb"%s, Version" % name, blob) is None and name not in blob:
            problems.append(f"the DLL does not reference {name.decode()}")
else:
    problems.append("the package has no DLL")

for problem in problems:
    print("FAIL:", problem)
print("checked", package)
sys.exit(1 if problems else 0)
