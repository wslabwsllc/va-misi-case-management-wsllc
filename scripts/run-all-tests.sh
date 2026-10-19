#!/usr/bin/env bash
# The single documented command for the complete automated suite against the Docker-hosted application:
#   scripts/run-all-tests.sh [all|unit|integration|ui]
# It records the git branch, commit and a build ID with the run, rebuilds the image, runs the tests service (which
# starts an isolated web-e2e copy of the production app), attaches the redacted server log of web-e2e to the run, and
# exits with the suite's exit code. Results persist on the testresults volume and appear in the app under
# Administration > Quality assurance > Test results (imported within a minute; or use "Import new runs").
# Plain `docker compose run --rm tests` also works; it records the same results without git metadata or server logs.
set -euo pipefail
cd "$(dirname "$0")/.."
# Git Bash on Windows would rewrite container paths such as /src/scripts/run-tests.sh into Windows paths.
export MSYS_NO_PATHCONV=1
selection="${1:-all}"
export GIT_COMMIT="${GIT_COMMIT:-$(git rev-parse HEAD 2>/dev/null || echo unknown)}"
export GIT_BRANCH="${GIT_BRANCH:-$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo unknown)}"
export BUILD_ID="${BUILD_ID:-local-$(date -u +%Y%m%d%H%M%S)}"
export TEST_RUN_KEY="${TEST_RUN_KEY:-$(date -u +%Y%m%dT%H%M%SZ)-${GIT_COMMIT:0:8}}"
export CAPTURE_SERVER_LOGS=1

echo "Building images (commit ${GIT_COMMIT:0:12}, branch ${GIT_BRANCH}, build ${BUILD_ID})"
if ! docker compose --profile tests build; then
  echo "RESULT: FAIL (the images could not be built, so no test ran)"; exit 3
fi

set +e
docker compose --profile tests run --rm tests /src/scripts/run-tests.sh "$selection"
code=$?
set -e

# Attach the application log of web-e2e (redacted again when imported), then publish the manifest.
log="$(mktemp)"
docker compose --profile tests logs --no-color --no-log-prefix web-e2e > "$log" 2>/dev/null || true
docker compose --profile tests run --rm --no-deps -T --entrypoint bash -e TEST_RUN_KEY tests -c '
  dir="/results/runs/${TEST_RUN_KEY}"
  if [ -d "$dir" ]; then
    cat > "$dir/server-web-e2e.log"
    if [ -f "$dir/run.json.pending" ]; then mv "$dir/run.json.pending" "$dir/run.json"; fi
  else
    cat > /dev/null
  fi' < "$log" || {
  echo "WARNING: the server log could not be attached to run ${TEST_RUN_KEY}; publishing the run without it"
  docker compose --profile tests run --rm --no-deps -T --entrypoint bash -e TEST_RUN_KEY tests -c \
    'f="/results/runs/${TEST_RUN_KEY}/run.json.pending"; [ -f "$f" ] && sed "s/\"serverLogs\": \[[^]]*\]/\"serverLogs\": []/" "$f" > "${f%.pending}" && rm "$f"' || true
}
rm -f "$log"

echo "Run ${TEST_RUN_KEY} finished with exit code ${code}. Results: Administration > Quality assurance > Test results (http://localhost:8080/test-results)."
exit "$code"
