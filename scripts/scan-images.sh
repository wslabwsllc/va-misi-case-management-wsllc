#!/usr/bin/env bash
# Container image vulnerability scan with Trivy (run in Docker, so nothing is installed on the host).
# Build first, then scan: docker compose build && scripts/scan-images.sh [--all]
# Scans the images docker-compose.yml runs (app, veteran-lookup, veteran-history, db); --all adds the test image (caseintake-eval).
# TRIVY_SEVERITY overrides HIGH,CRITICAL; TRIVY_IGNORE_UNFIXED=1 leaves out findings with no fixed version yet.
# Exits 1 if any image has a HIGH or CRITICAL finding that is not listed in .trivyignore. Reports are written to
# artifacts/security-scan/; record the summary in docs/security-scan.md. Needs network access for the vulnerability
# database (first run and when it is older than a day); the database is cached in the trivy-cache volume.
set -euo pipefail
cd "$(dirname "$0")/.."

# Pinned like every other image (docs/versions.md). Update: docker buildx imagetools inspect aquasec/trivy:<tag>
trivy_image="aquasec/trivy:0.75.0@sha256:af6acf9a6b85dfe389a1941505c0ce9efef52a4719635e1a962f022a3d855daa"
severity="${TRIVY_SEVERITY:-HIGH,CRITICAL}"
out="artifacts/security-scan"

profiles=()
[[ "${1:-}" == "--all" ]] && profiles=(--profile tests)
images="$(docker compose "${profiles[@]+"${profiles[@]}"}" config --images | sort -u)"

mkdir -p "$out"
# Documented exceptions: one CVE ID per line in .trivyignore, each with a comment saying why it is accepted.
mount=() ignore=()
[[ "${TRIVY_IGNORE_UNFIXED:-}" == "1" ]] && ignore+=(--ignore-unfixed)
[[ -f .trivyignore ]] && mount=(-v "$PWD/.trivyignore:/work/.trivyignore:ro") && ignore+=(--ignorefile /work/.trivyignore)

fail=0
for image in $images; do
  if ! docker image inspect "$image" >/dev/null 2>&1; then
    echo "FAIL $image is not built or pulled (run: docker compose build)"; fail=1; continue
  fi
  report="$out/$(echo "$image" | tr '/:@' '___' | cut -c1-80).txt"
  echo "==> $image"
  # MSYS_NO_PATHCONV: Git Bash on Windows would otherwise rewrite the container paths.
  if MSYS_NO_PATHCONV=1 docker run --rm \
      -v /var/run/docker.sock:/var/run/docker.sock \
      -v trivy-cache:/root/.cache/trivy \
      "${mount[@]+"${mount[@]}"}" \
      "$trivy_image" image --quiet --scanners vuln --severity "$severity" --exit-code 1 \
      "${ignore[@]+"${ignore[@]}"}" "$image" | tee "$report"; then
    echo "OK   $image: no $severity findings"
  else
    echo "FAIL $image: $severity findings (report: $report)"; fail=1
  fi
done

if [[ $fail == 0 ]]; then echo "RESULT: PASS"; else echo "RESULT: FAIL"; fi
exit $fail
