#!/usr/bin/env bash
# Repeats one test filter N times against the Docker-hosted app to expose flakiness (review F12), for example:
#   scripts/repeat-tests.sh "FullyQualifiedName~A_stale_save_reports_a_conflict" 10 CaseIntake.E2E
# Uses the images built by scripts/run-all-tests.sh. Prints one line per run and a summary; exits non-zero if any run
# failed. Results of these runs are not imported into the test results module.
set -euo pipefail
cd "$(dirname "$0")/.."
export MSYS_NO_PATHCONV=1
filter="${1:?test filter, for example FullyQualifiedName~A_stale_save}"
count="${2:-10}"
project="${3:-CaseIntake.E2E}"
docker compose --profile tests run --rm -e REPEAT_FILTER="$filter" -e REPEAT_COUNT="$count" -e REPEAT_PROJECT="$project" \
  --entrypoint bash tests -c '
  passed=0; failed=0
  for i in $(seq 1 "$REPEAT_COUNT"); do
    if dotnet test "/src/tests/$REPEAT_PROJECT/$REPEAT_PROJECT.csproj" -c Release --no-build --no-restore --filter "$REPEAT_FILTER" \
         --results-directory /tmp/repeat-$i > /tmp/repeat-$i.log 2>&1; then
      passed=$((passed + 1)); echo "run $i: passed ($(grep -oE "Passed: +[0-9]+" /tmp/repeat-$i.log | tail -1))"
    else
      failed=$((failed + 1)); echo "run $i: FAILED"; grep -E "Failed |Error Message" -A2 /tmp/repeat-$i.log | head -8
    fi
  done
  echo "RESULT: $passed passed, $failed failed out of $REPEAT_COUNT runs of $REPEAT_FILTER"
  [ "$failed" -eq 0 ]'
