#!/usr/bin/env bash
# Docker smoke test for framework JavaScript delivery (the "blazor.web.js does not work in Docker" regression).
# Run against a running stack: docker compose up --build -d && scripts/smoke-docker.sh [base-url]
# Checks, without signing in: the framework script and app assets answer 200 with the right content type (not the
# sign-in redirect), the sign-in page references exactly one framework script, the container's content root is its
# publish folder, and the published image contains the static assets.
set -euo pipefail
base="${1:-http://localhost:8080}"
fail=0
check() { # path expected-content-type-fragment
  local out code type
  out="$(curl -s -o /dev/null -w '%{http_code} %{content_type}' --max-redirs 0 "$base$1")" || out="000 none"
  code="${out%% *}"; type="${out#* }"
  if [[ "$code" == "200" && "$type" == *"$2"* ]]; then echo "OK   $1 -> $code $type"; else echo "FAIL $1 -> $code $type (expected 200 $2)"; fail=1; fi
}
check /_framework/blazor.web.js javascript
check /app.css text/css
check /js/app.js javascript
check /js/interop.js javascript
check /Components/Layout/ReconnectModal.razor.js javascript
check /manifest.webmanifest manifest
check /account/login text/html

refs="$(curl -s "$base/account/login" | grep -o '_framework/blazor\.web[.a-z0-9]*\.js' | wc -l | tr -d ' ')"
if [[ "$refs" == "1" ]]; then echo "OK   sign-in page references one framework script"; else echo "FAIL sign-in page references $refs framework scripts"; fail=1; fi

if command -v docker >/dev/null 2>&1 && docker compose ps web --format '{{.State}}' 2>/dev/null | grep -q running; then
  root="$(docker compose logs web 2>/dev/null | grep -o '"ContentRoot":"[^"]*"' | tail -1)"
  if [[ "$root" == *'"/app/web"'* || "$root" == *'"/app/web/"'* ]]; then echo "OK   content root $root"; else echo "FAIL content root is ${root:-unknown} (expected /app/web)"; fail=1; fi
  # MSYS_NO_PATHCONV: Git Bash on Windows would otherwise rewrite the container path.
  if MSYS_NO_PATHCONV=1 docker compose exec -T web test -f /app/web/wwwroot/_framework/blazor.web.js; then echo "OK   published blazor.web.js present in the image"; else echo "FAIL blazor.web.js missing from /app/web/wwwroot/_framework"; fail=1; fi
fi

if [[ $fail -eq 0 ]]; then echo "SMOKE: PASS"; else echo "SMOKE: FAIL"; fi
exit $fail
