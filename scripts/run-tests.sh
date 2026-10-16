#!/usr/bin/env bash
# Runs every test project with the already-built Release outputs (no restore, no build, no downloads) and fails if
# a test project is missing from the run, produced no results, or discovered zero tests.
# Uses the VSTest runner through standard `dotnet test`. Usage: scripts/run-tests.sh [results-dir]
set -uo pipefail
cd "$(dirname "$0")/.."
results="${1:-/results}"
config="${CONFIGURATION:-Release}"
mkdir -p "$results"
rm -f "$results"/*.trx

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
for project in "${discovered[@]}"; do
  name="$(basename "$project" .csproj)"
  echo "=== ${name} ==="
  dotnet test "$project" -c "$config" --no-build --no-restore \
    --logger "trx;LogFileName=${name}.trx" --results-directory "$results"
  code=$?
  trx="$results/${name}.trx"
  if [[ ! -f "$trx" ]]; then
    echo "FAIL: ${name} produced no result file"; status=1; summary+=("${name}: NO RESULTS"); continue
  fi
  total=$(grep -o '<Counters [^>]*' "$trx" | sed -n 's/.* total="\([0-9]*\)".*/\1/p' | head -1)
  failed=$(grep -o '<Counters [^>]*' "$trx" | sed -n 's/.* failed="\([0-9]*\)".*/\1/p' | head -1)
  if [[ -z "$total" || "$total" -eq 0 ]]; then echo "FAIL: ${name} discovered zero tests"; status=1; fi
  if [[ $code -ne 0 ]]; then status=1; fi
  summary+=("${name}: total=${total:-?} failed=${failed:-?} exit=${code}")
done

echo; echo "===== Test summary ====="
printf '%s\n' "${summary[@]:1}"
if [[ $status -eq 0 ]]; then echo "RESULT: PASS"; else echo "RESULT: FAIL"; fi
exit $status
