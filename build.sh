#!/usr/bin/env bash
# ./build.sh [Emby programdata directory containing plugins/]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/src/Emby.Plugin.RdZurg"
VERSION="${VERSION:-$(python3 -c 'import sys,xml.etree.ElementTree as E; print(E.parse(sys.argv[1]).findtext(".//Version"))' "$PROJECT/Emby.Plugin.RdZurg.csproj")}"

python3 - "$VERSION" <<'CHECK'
import re, sys
version = sys.argv[1]
if not re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", version) or any(int(x) > 65534 for x in version.split('.')):
    raise SystemExit('VERSION must contain four integer components between 0 and 65534')
CHECK

dotnet build "$PROJECT" -c Release -p:Version="$VERSION" -p:AssemblyVersion="$VERSION" -p:FileVersion="$VERSION"

OUT="$ROOT/artifacts/rd-zurg-for-emby_$VERSION"
rm -rf "$OUT" && mkdir -p "$OUT"
cp "$PROJECT/bin/Release/net8.0/Emby.Plugin.RdZurg.dll" "$OUT/"
(cd "$OUT" && shasum -a 256 Emby.Plugin.RdZurg.dll > Emby.Plugin.RdZurg.dll.sha256)
echo "Packaged $OUT/Emby.Plugin.RdZurg.dll"

if [[ $# -gt 1 ]]; then
  echo "Usage: ./build.sh [Emby programdata directory containing plugins/]" >&2
  exit 2
fi
if [[ $# -eq 1 ]]; then
  install -m 644 "$OUT/Emby.Plugin.RdZurg.dll" "$1/plugins/Emby.Plugin.RdZurg.dll"
  echo "Installed into $1/plugins. Restart Emby, then run the RD zurg sync."
fi
