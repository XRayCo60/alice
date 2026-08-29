#!/usr/bin/env bash
set -Eeuo pipefail

APP="${1:-/root/alice}"
ASSETS="$APP/obj/project.assets.json"
PROJECT="$APP/myapp.csproj"
OUT="$APP/bin/Release/net8.0"
WORK="$(mktemp -d /tmp/alice-fast-build.XXXXXX)"
trap 'rm -rf "$WORK"' EXIT

[[ -s "$PROJECT" ]] || { echo "missing project: $PROJECT" >&2; exit 1; }
[[ -s "$ASSETS" ]] || { echo "missing assets: $ASSETS" >&2; exit 1; }

REFDIR="$(find /usr/share/dotnet/packs/Microsoft.NETCore.App.Ref -type d -path '*/ref/net8.0' | sort -V | tail -n 1)"
CSC="$(find /usr/share/dotnet/sdk -path '*/Roslyn/bincore/csc.dll' | sort -V | tail -n 1)"
[[ -d "$REFDIR" ]] || { echo "NET 8 reference pack not found" >&2; exit 1; }
[[ -s "$CSC" ]] || { echo "Roslyn compiler not found" >&2; exit 1; }

mkdir -p "$OUT"

python3 - "$APP" "$PROJECT" "$ASSETS" "$REFDIR" "$WORK/compile.rsp" "$WORK/myapp.dll" <<'PY'
import glob
import json
import os
import sys
import xml.etree.ElementTree as ET

app, project_path, assets_path, refdir, rsp_path, output = sys.argv[1:]
root = ET.parse(project_path).getroot()
sources = [node.attrib["Include"] for node in root.findall(".//Compile")]
missing = [name for name in sources if not os.path.isfile(os.path.join(app, name))]
if missing:
    raise SystemExit("missing source files: " + ", ".join(missing))

with open(assets_path, encoding="utf-8") as stream:
    assets = json.load(stream)

target_key = next((key for key in assets.get("targets", {})
                   if key == "net8.0" or key.startswith("net8.0/") or ".NETCoreApp,Version=v8.0" in key), None)
if target_key is None:
    raise SystemExit("net8.0 target not found in project.assets.json")

packages = assets.get("project", {}).get("restore", {}).get("packagesPath", "/root/.nuget/packages/")
references = set(glob.glob(os.path.join(refdir, "*.dll")))
for library_name, metadata in assets["targets"][target_key].items():
    library_path = assets.get("libraries", {}).get(library_name, {}).get("path")
    if not library_path:
        continue
    for relative in metadata.get("compile", {}):
        if relative == "_._" or relative.endswith("/_._"):
            continue
        candidate = os.path.join(packages, library_path, relative)
        if os.path.isfile(candidate):
            references.add(candidate)

global_usings = os.path.join(os.path.dirname(rsp_path), "ImplicitUsings.g.cs")
with open(global_usings, "w", encoding="utf-8") as stream:
    stream.write("global using System;\n")
    stream.write("global using System.Collections.Generic;\n")
    stream.write("global using System.IO;\n")
    stream.write("global using System.Linq;\n")
    stream.write("global using System.Net.Http;\n")
    stream.write("global using System.Threading;\n")
    stream.write("global using System.Threading.Tasks;\n")

optimize = os.environ.get("ALICE_CSC_OPTIMIZE", "false").lower() in ("1", "true", "yes")
with open(rsp_path, "w", encoding="utf-8") as stream:
    stream.write("/nologo\n/target:exe\n/langversion:latest\n/nullable:enable\n")
    stream.write(("/optimize+\n" if optimize else "/optimize-\n") + "/deterministic+\n/parallel+\n/utf8output\n")
    stream.write(f"/out:{output}\n")
    for reference in sorted(references):
        stream.write(f"/reference:{reference}\n")
    stream.write(f"{global_usings}\n")
    for source in sources:
        stream.write(f"{os.path.join(app, source)}\n")

print(f"fast build: {len(sources)} source files, {len(references)} references")
PY

set +e
timeout "${ALICE_BUILD_TIMEOUT:-900}" /usr/share/dotnet/dotnet "$CSC" "@$WORK/compile.rsp"
result=$?
set -e
if [[ $result -eq 124 ]]; then
    echo "direct compilation timed out" >&2
    exit 124
elif [[ $result -ne 0 ]]; then
    exit "$result"
fi
[[ -s "$WORK/myapp.dll" ]] || { echo "compiler produced no output" >&2; exit 1; }

cp -f "$WORK/myapp.dll" "$OUT/myapp.dll.new"
mv -f "$OUT/myapp.dll.new" "$OUT/myapp.dll"
echo "fast build completed: $OUT/myapp.dll"
