#!/usr/bin/env bash
# Offline (no egress) runtime check (review F07, gate G3). Requires the images built by `scripts/run-all-tests.sh` (or
# `docker compose --profile tests build`) and an internet-connected host only for that build. It then
#   1. starts db, migrator, veteran-lookup and web-e2e on an internal Compose network (docker-compose.offline.yml),
#   2. proves inside the tests container that DNS and outbound connections to the internet fail, and
#   3. runs the complete suite (unit, component, integration and the Playwright + axe browser suite) with no egress.
# Exit code: 0 only when egress is blocked and every suite passes.
set -euo pipefail
cd "$(dirname "$0")/.."
export MSYS_NO_PATHCONV=1
compose=(docker compose -f docker-compose.yml -f docker-compose.offline.yml --profile tests)
selection="${1:-all}"
export TEST_RUN_KEY="${TEST_RUN_KEY:-$(date -u +%Y%m%dT%H%M%SZ)-offline}"

"${compose[@]}" down --remove-orphans >/dev/null 2>&1 || true
set +e
"${compose[@]}" run --rm -e TEST_RUN_KEY tests bash -c '
  set -u
  echo "== Egress probe (must fail) =="
  failed=0
  if getent hosts api.nuget.org >/dev/null 2>&1; then echo "FAIL: api.nuget.org resolves"; failed=1; else echo "ok: api.nuget.org does not resolve"; fi
  if timeout 5 bash -c "exec 3<>/dev/tcp/1.1.1.1/443" 2>/dev/null; then echo "FAIL: outbound TCP to 1.1.1.1:443 connected"; failed=1; else echo "ok: outbound TCP to 1.1.1.1:443 blocked"; fi
  if timeout 5 bash -c "exec 3<>/dev/tcp/web-e2e/8080" 2>/dev/null; then echo "ok: web-e2e reachable inside the stack"; else echo "FAIL: web-e2e not reachable"; failed=1; fi
  if [ "$failed" -ne 0 ]; then echo "RESULT: FAIL (the network is not offline as required)"; exit 4; fi
  echo "== Suites with no egress =="
  /src/scripts/run-tests.sh '"$selection"'
'
code=$?
set -e
"${compose[@]}" down --remove-orphans >/dev/null 2>&1 || true
echo "Offline check exit code: $code (run key $TEST_RUN_KEY)"
exit $code
