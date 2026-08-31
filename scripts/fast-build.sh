#!/usr/bin/env bash
set -Eeuo pipefail

APP="${1:-/root/alice}"
PROJECT="$APP/myapp.csproj"
ASSETS="$APP/obj/project.assets.json"

[[ -s "$PROJECT" ]] || { echo "missing project: $PROJECT" >&2; exit 1; }
[[ -s "$ASSETS" ]] || { echo "missing assets: $ASSETS" >&2; exit 1; }

cd "$APP"
MSBUILD_USER_DIR="/tmp/$(basename "$APP")-msbuild-user"
mkdir -p "$MSBUILD_USER_DIR"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true
export MSBUILDUSESERVER=0
export MSBuildEnableWorkloadResolver=false
export MSBuildUserExtensionsPath="$MSBUILD_USER_DIR"

echo "traditional modular build started (no restore, no tests)"
dotnet build "$PROJECT" \
  -c Release \
  --no-restore \
  --nologo \
  -m:1 \
  -v:minimal \
  /nodeReuse:false \
  /p:UseSharedCompilation=false \
  /p:ImportUserLocationsByWildcardBeforeMicrosoftCommonProps=false \
  /p:ImportUserLocationsByWildcardAfterMicrosoftCommonTargets=false

test -s "$APP/bin/Release/net8.0/myapp.dll"
echo "traditional modular build completed: $APP/bin/Release/net8.0/myapp.dll"
