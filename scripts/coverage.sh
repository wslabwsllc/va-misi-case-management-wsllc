#!/usr/bin/env bash
# Measures line coverage of the application assemblies across the unit, bUnit component and PostgreSQL integration
# suites (the browser suite runs the app out of process, so it is not measured), and fails when the total or any
# assembly is below the threshold. Exclusions are in coverage.runsettings. Requires Docker for the integration suite.
#
# Usage: scripts/coverage.sh [threshold-percent]       default 80, or COVERAGE_THRESHOLD
# Output: artifacts/coverage/<Project>/**/*.cobertura.xml and artifacts/coverage/summary.md
set -uo pipefail
cd "$(dirname "$0")/.."
threshold="${1:-${COVERAGE_THRESHOLD:-80}}"
out="artifacts/coverage"
rm -rf "$out"; mkdir -p "$out"

projects=(); while IFS= read -r line; do projects+=("$line"); done < <(
  grep -rl --include='*.csproj' '<IsTestProject>true</IsTestProject>' tests src | grep -v 'CaseIntake.E2E' | sort)
if [[ ${#projects[@]} -eq 0 ]]; then echo "FAIL: no test projects discovered"; exit 2; fi

status=0
for project in "${projects[@]}"; do
  name="$(basename "$project" .csproj)"
  echo "=== ${name} ==="
  dotnet test "$project" --settings coverage.runsettings --collect "Code Coverage" --results-directory "$out/$name" || status=1
done
[[ $status -ne 0 ]] && echo "WARNING: at least one suite had failing tests; coverage below reflects the tests that ran."

dotnet run scripts/coverage-report.cs -- "$out" "$threshold" || status=1
exit $status
