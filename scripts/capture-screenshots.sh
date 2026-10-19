#!/usr/bin/env bash
# Captures documentation screenshots of the running demo stack into docs/screenshots (desktop, tablet, phone).
# Usage: docker compose up -d && scripts/capture-screenshots.sh
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p docs/screenshots
MSYS_NO_PATHCONV=1 docker run --rm --network caseintake_default \
  -v "$(pwd)/scripts/capture-screenshots.js:/tmp/capture.js:ro" -v "$(pwd)/docs/screenshots:/out" \
  --user root --entrypoint bash caseintake-eval:local -c \
  "/src/tests/CaseIntake.E2E/bin/Release/net10.0/.playwright/node/linux-x64/node /tmp/capture.js"
