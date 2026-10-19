#!/usr/bin/env bash
# Runs the test suites with the already-built Release outputs (no restore, no build, no downloads) and fails if a test
# project is missing from the run, produced no results, or discovered zero tests. Uses the VSTest runner through
# standard `dotnet test`.
#
# Every run is written to its own folder and earlier runs are never deleted:
#   <results-root>/runs/<run-key>/<Project>.trx        one TRX per suite
#   <results-root>/runs/<run-key>/evidence/...         browser evidence (screenshots, traces, logs) from CaseIntake.E2E
#   <results-root>/runs/<run-key>/run.json             manifest, written last (the web app imports complete runs only)
#
# Usage: scripts/run-tests.sh [all|unit|integration|ui] [results-root]
#   unit         unit and bUnit component suites      integration  CaseIntake.IntegrationTests
#   ui           CaseIntake.E2E (Playwright + axe)    all          everything (default)
set -uo pipefail
cd "$(dirname "$0")/.."
selection="${1:-${TEST_SUITES:-all}}"
root="${2:-${RESULTS_ROOT:-/results}}"
config="${CONFIGURATION:-Release}"
case "$selection" in all|unit|integration|ui) ;; *) echo "FAIL: unknown suite selection '$selection' (use all, unit, integration or ui)"; exit 2 ;; esac

started_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
run_key="${TEST_RUN_KEY:-$(date -u +%Y%m%dT%H%M%SZ)-$(head -c4 /dev/urandom | od -An -tx1 | tr -d ' \n')}"
if [[ ! "$run_key" =~ ^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$ ]]; then echo "FAIL: invalid run key '$run_key'"; exit 2; fi
run_dir="$root/runs/$run_key"
if ! mkdir -p "$run_dir"; then
  echo "FAIL: tests could not start: the results folder $run_dir cannot be created (is the testresults volume mounted and writable?)"
  exit 2
fi
export TEST_RUN_DIR="$run_dir"
echo "Run $run_key -> $run_dir (selection: $selection)"

json_escape() { printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' | tr -d '\000-\037'; }

suite_type() {
  case "$1" in
    *.Web.Tests) echo "Component" ;;
    *.IntegrationTests) echo "Integration" ;;
    *.E2E) echo "EndToEnd" ;;
    *) echo "Unit" ;;
  esac
}

selected() {
  local type="$1"
  case "$selection" in
    all) return 0 ;;
    unit) [[ "$type" == "Unit" || "$type" == "Component" ]] ;;
    integration) [[ "$type" == "Integration" ]] ;;
    ui) [[ "$type" == "EndToEnd" ]] ;;
  esac
}

# Is the application under test reachable? (bash only; no curl in the image)
reachable() {
  local url="${1#http://}"; local hostport="${url%%/*}"; local host="${hostport%%:*}"; local port="${hostport##*:}"
  [[ "$port" == "$host" ]] && port=80
  timeout 5 bash -c "exec 3<>/dev/tcp/${host}/${port}" 2>/dev/null
}

# Every project marked IsTestProject in the repository, compared against the solution's project list.
discovered=(); while IFS= read -r line; do discovered+=("$line"); done < <(grep -rl --include='*.csproj' '<IsTestProject>true</IsTestProject>' tests src 2>/dev/null | sort)
in_solution=(); while IFS= read -r line; do in_solution+=("$line"); done < <(dotnet sln CaseIntake.sln list | tail -n +3 | sort)
status=0
if [[ ${#discovered[@]} -eq 0 ]]; then echo "FAIL: no test projects discovered"; exit 1; fi
for project in "${discovered[@]}"; do
  if ! printf '%s\n' "${in_solution[@]}" | grep -qx "${project}"; then
    echo "FAIL: ${project} is a test project but is not in CaseIntake.sln"; status=1
  fi
done

summary=("")
suites_json=""
incomplete=0   # a suite that did not run or produced no results (failing tests alone do not make a run partial)
add_suite() { # name type trx exit status message
  local entry
  entry=$(printf '{"name":"%s","type":"%s","trx":%s,"exitCode":%s,"status":"%s","message":%s}' \
    "$(json_escape "$1")" "$2" "$3" "$4" "$5" "$6")
  suites_json="${suites_json:+$suites_json,}$entry"
}

for project in "${discovered[@]}"; do
  name="$(basename "$project" .csproj)"
  type="$(suite_type "$name")"
  selected "$type" || continue
  echo "=== ${name} (${type}) ==="
  trx="$run_dir/${name}.trx"

  if [[ "$type" == "EndToEnd" ]] && ! reachable "${E2E_BASE_URL:-http://localhost:8080}"; then
    message="The application under test (${E2E_BASE_URL:-http://localhost:8080}) is not reachable; the browser suite did not run."
    echo "FAIL: ${name} could not start: ${message}"
    status=1; incomplete=1; summary+=("${name}: FAILED TO START (application not reachable)")
    add_suite "$name" "$type" "null" "null" "FailedToStart" "\"$(json_escape "$message")\""
    continue
  fi

  dotnet test "$project" -c "$config" --no-build --no-restore \
    --logger "trx;LogFileName=${name}.trx" --results-directory "$run_dir"
  code=$?
  if [[ ! -f "$trx" ]]; then
    message="dotnet test exited with code ${code} before writing results."
    echo "FAIL: ${name} produced no result file (${message})"; status=1; incomplete=1
    summary+=("${name}: NO RESULTS (exit=${code})")
    add_suite "$name" "$type" "null" "$code" "FailedToStart" "\"$(json_escape "$message")\""
    continue
  fi
  total=$(grep -o '<Counters [^>]*' "$trx" | sed -n 's/.* total="\([0-9]*\)".*/\1/p' | head -1)
  failed=$(grep -o '<Counters [^>]*' "$trx" | sed -n 's/.* failed="\([0-9]*\)".*/\1/p' | head -1)
  suite_status="Completed"; message="null"
  if [[ -z "$total" || "$total" -eq 0 ]]; then
    echo "FAIL: ${name} discovered zero tests"; status=1; incomplete=1; suite_status="NoResults"; message="\"Zero tests discovered.\""
  fi
  if [[ $code -ne 0 ]]; then status=1; fi
  summary+=("${name}: total=${total:-?} failed=${failed:-?} exit=${code}")
  add_suite "$name" "$type" "\"${name}.trx\"" "$code" "$suite_status" "$message"
done

finished_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
os_name="$(. /etc/os-release 2>/dev/null && echo "${PRETTY_NAME:-Linux}" || uname -s)"
runtime="$(dotnet --list-runtimes 2>/dev/null | grep -m1 'Microsoft.AspNetCore.App' | cut -d' ' -f1-2)"
run_status="Completed"; [[ $incomplete -ne 0 ]] && run_status="Partial"
server_logs="[]"; manifest_name="run.json"
if [[ "${CAPTURE_SERVER_LOGS:-}" == "1" ]]; then
  # scripts/run-all-tests.sh adds the app's log and then renames the manifest, so the run is imported with it.
  server_logs='["server-web-e2e.log"]'; manifest_name="run.json.pending"
fi
cat > "$run_dir/.run.json.tmp" <<EOF
{
  "schemaVersion": 1,
  "runKey": "$(json_escape "$run_key")",
  "startedAtUtc": "$started_at",
  "finishedAtUtc": "$finished_at",
  "runner": "scripts/run-tests.sh ($selection) via docker compose tests service",
  "status": "$run_status",
  "git": { "branch": "$(json_escape "${GIT_BRANCH:-${CASEINTAKE_GIT_BRANCH:-unknown}}")", "commit": "$(json_escape "${GIT_COMMIT:-${CASEINTAKE_GIT_COMMIT:-unknown}}")", "buildId": "$(json_escape "${BUILD_ID:-${CASEINTAKE_BUILD_ID:-local}}")" },
  "imageVersion": "$(json_escape "caseintake-eval:local (${CASEINTAKE_BUILD_ID:-local}, ${CASEINTAKE_GIT_COMMIT:-unknown})")",
  "environment": { "name": "docker compose tests", "os": "$(json_escape "$os_name")", "runtime": "$(json_escape "${runtime:-unknown}")", "baseUrl": "$(json_escape "${E2E_BASE_URL:-}")" },
  "suites": [ ${suites_json} ],
  "serverLogs": ${server_logs}
}
EOF
mv "$run_dir/.run.json.tmp" "$run_dir/$manifest_name"
printf '%s\n' "$run_key" > "$root/latest-run"

echo; echo "===== Test summary (run ${run_key}) ====="
printf '%s\n' "${summary[@]:1}"
echo "Results and evidence: ${run_dir} (imported into Administration > Quality assurance > Test results)"
if [[ $status -eq 0 ]]; then echo "RESULT: PASS"; else echo "RESULT: FAIL"; fi
exit $status
