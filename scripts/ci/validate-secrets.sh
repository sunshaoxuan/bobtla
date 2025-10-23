#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")"/../.. && pwd)"
PROJECT="$REPO_ROOT/scripts/SmokeTests/Stage5SmokeTests"
OVERRIDE_PATH="$REPO_ROOT/deploy/stage.appsettings.override.json"

if [[ ! -f "$OVERRIDE_PATH" ]]; then
  echo "override 文件 $OVERRIDE_PATH 不存在，请确认部署目录是否完整。" >&2
  exit 2
fi

dotnet run --project "$PROJECT" -- secrets --override "$OVERRIDE_PATH" --verify-readiness "$@"
