#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${TEST_CERTIFICATE:?Set TEST_CERTIFICATE to your HTTPS PFX file.}"
test -f "$TEST_CERTIFICATE"
test_dir=$(mktemp -d)
host_pid=
cleanup() {
  if [[ -n "$host_pid" ]]; then kill "$host_pid" 2>/dev/null || true; wait "$host_pid" 2>/dev/null || true; fi
  rm -rf "$test_dir"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
npm run assets
dotnet publish src/DigitalSorter.Auth --configuration Release -o "$test_dir/published" -p:RestoreLockedMode=true
dotnet build tests/DigitalSorter.BrowserHost --configuration Release -p:RestoreLockedMode=true
TEST_PORT="${TEST_PORT:-7043}" TEST_BIND_ADDRESS="${TEST_BIND_ADDRESS:-127.0.0.1}" TEST_WEB_ROOT="$test_dir/published/wwwroot" TEST_FIXTURES="$PWD/tests/fixtures" TEST_DATA_DIRECTORY="$test_dir/data" \
  dotnet tests/DigitalSorter.BrowserHost/bin/Release/net10.0/DigitalSorter.BrowserHost.dll &
host_pid=$!
wait "$host_pid"
