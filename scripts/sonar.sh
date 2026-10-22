#!/usr/bin/env bash
# Local static analysis with SonarQube Community Build (bugs, code smells, security hotspots, duplication, coverage).
# Starts the server from docker-compose.sonar.yml if needed, builds the solution under the pinned SonarScanner for .NET
# (dotnet-tools.json), runs the same test suites as scripts/coverage.sh for coverage, and uploads the analysis.
# Requires Docker. No Java on the host: the scanner downloads the JRE the server provides.
#
# Usage: scripts/sonar.sh [--skip-tests]       --skip-tests: analysis only, no coverage (faster; no Docker for tests)
# Output: http://localhost:9000/dashboard?id=caseintake (sign in as admin with the local-only password below)
#         The analysis token is cached in artifacts/sonar/token.
# Stop:   docker compose -f docker-compose.sonar.yml stop        (down -v removes the server and its history)
set -euo pipefail
cd "$(dirname "$0")/.."

port="${SONAR_PORT:-9000}"
url="http://localhost:${port}"
project_key="caseintake"
# Synthetic and local-only, like the demo credentials; the server listens on 127.0.0.1 only.
admin_password="${SONAR_ADMIN_PASSWORD:-CaseIntake-Sonar-local-only1!}"
out="artifacts/sonar"
token_file="$out/token"
run_tests=1
[[ "${1:-}" == "--skip-tests" ]] && run_tests=0
mkdir -p "$out"

echo "==> Starting SonarQube at $url"
docker compose -f docker-compose.sonar.yml up -d
for _ in $(seq 1 120); do
  status="$(curl -fsS "$url/api/system/status" 2>/dev/null | sed -n 's/.*"status":"\([A-Z_]*\)".*/\1/p' || true)"
  [[ "$status" == "UP" ]] && break
  sleep 3
done
if [[ "${status:-}" != "UP" ]]; then
  echo "FAIL: SonarQube did not start (status: ${status:-unreachable}); see: docker compose -f docker-compose.sonar.yml logs"
  exit 1
fi

token_valid() {
  [[ -s "$token_file" ]] && curl -fsS -u "$(cat "$token_file"):" "$url/api/authentication/validate" | grep -q '"valid":true'
}
if ! token_valid; then
  # A fresh server only accepts admin/admin and requires a new password before anything else.
  if ! curl -fsS -u "admin:$admin_password" "$url/api/authentication/validate" | grep -q '"valid":true'; then
    echo "==> First run: setting the local admin password"
    curl -fsS -u admin:admin -X POST "$url/api/users/change_password" \
      --data-urlencode login=admin --data-urlencode previousPassword=admin --data-urlencode "password=$admin_password" \
      || { echo "FAIL: cannot sign in as admin; set SONAR_ADMIN_PASSWORD to the password you chose"; exit 1; }
  fi
  echo "==> Creating an analysis token"
  curl -fsS -u "admin:$admin_password" -X POST "$url/api/user_tokens/generate" \
    --data-urlencode "name=scripts-sonar-$(date +%Y%m%d%H%M%S)" \
    | sed -n 's/.*"token":"\([^"]*\)".*/\1/p' > "$token_file"
  token_valid || { echo "FAIL: could not create an analysis token"; exit 1; }
fi
token="$(cat "$token_file")"

dotnet tool restore >/dev/null
# Migrations and the design-time factory are generated or tool-only code (same exclusions as coverage.runsettings).
dotnet tool run dotnet-sonarscanner begin \
  /k:"$project_key" /n:"CaseIntake" \
  /d:sonar.host.url="$url" /d:sonar.token="$token" \
  /d:sonar.exclusions="**/Migrations/**,**/obj/**,**/bin/**,artifacts/**" \
  /d:sonar.coverage.exclusions="**/Migrations/**,**/DesignTimeDbContextFactory.cs,tests/**" \
  /d:sonar.cs.vscoveragexml.reportsPaths="$out/coverage/**/*.xml"

status=0
dotnet build CaseIntake.sln --no-incremental || status=1

if [[ $status -eq 0 && $run_tests -eq 1 ]]; then
  rm -rf "$out/coverage"
  projects=(); while IFS= read -r line; do projects+=("$line"); done < <(
    grep -rl --include='*.csproj' '<IsTestProject>true</IsTestProject>' tests src | grep -v 'CaseIntake.E2E' | sort)
  for project in "${projects[@]}"; do
    name="$(basename "$project" .csproj)"
    echo "=== ${name} ==="
    # Same suites and module filters as scripts/coverage.sh, in the XML format SonarQube reads for C#.
    dotnet test "$project" --no-build --settings coverage.runsettings --collect "Code Coverage;Format=xml" \
      --results-directory "$out/coverage/$name" \
      || echo "WARNING: ${name} had failing tests; coverage reflects the tests that ran."
  done
fi

# Always close the analysis, so the scanner's build hooks are removed even when the build failed.
dotnet tool run dotnet-sonarscanner end /d:sonar.token="$token" || status=1

if [[ $status -eq 0 ]]; then
  echo "RESULT: analysis uploaded, see $url/dashboard?id=$project_key"
else
  echo "RESULT: FAIL (see the output above)"
fi
exit $status
