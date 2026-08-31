#!/usr/bin/env bash
set -Eeuo pipefail

APP="${1:-/root/alice}"
PROJECT="$APP/myapp.csproj"
ASSETS="$APP/obj/project.assets.json"

[[ -s "$PROJECT" ]] || { echo "missing project: $PROJECT" >&2; exit 1; }
[[ -s "$ASSETS" ]] || { echo "missing assets: $ASSETS" >&2; exit 1; }

cd "$APP"

# Keep the normal MSBuild/Roslyn servers, parallelism and incremental outputs enabled.
# Disabling them was useful while diagnosing deadlocks, but made every deployment a
# cold single-process compilation.
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true

echo "incremental modular build started (no restore, no tests)"
dotnet build "$PROJECT" \
  -c Release \
  --no-restore \
  --nologo \
  -v:minimal

test -s "$APP/bin/Release/net8.0/myapp.dll"
echo "incremental modular build completed: $APP/bin/Release/net8.0/myapp.dll"
