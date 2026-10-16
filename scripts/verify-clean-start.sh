#!/usr/bin/env bash
# Brings the stack up from scratch, waits for readiness, and probes the health endpoints. Run at G1/G3/G4.
set -euo pipefail
cd "$(dirname "$0")/.."
docker compose down -v --remove-orphans
docker compose up --build -d
for _ in $(seq 1 90); do
  if curl -fsS http://localhost:8080/health/ready >/dev/null 2>&1; then break; fi
  sleep 2
done
curl -fsS http://localhost:8080/health/live && echo " live OK"
curl -fsS http://localhost:8080/health/ready && echo " ready OK"
curl -fsS -o /dev/null -w "GET / -> %{http_code}\n" http://localhost:8080/
docker compose ps
