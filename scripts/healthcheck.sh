#!/usr/bin/env bash
# Container health probe using only bash (no curl/wget in the image): GET http://host:port/path, succeed on HTTP 200.
set -euo pipefail
host="${1:-127.0.0.1}"; port="${2:-8080}"; path="${3:-/health/ready}"
exec 3<>"/dev/tcp/${host}/${port}"
printf 'GET %s HTTP/1.0\r\nHost: %s\r\nConnection: close\r\n\r\n' "$path" "$host" >&3
read -r -t 5 status_line <&3
[[ "$status_line" == HTTP/*" 200 "* ]]
