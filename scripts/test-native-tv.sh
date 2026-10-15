#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
fixture_dir=$(realpath "${TEST_NATIVE_FIXTURE:-test-results/native-fixture}")
test_dir=$(mktemp -d)
host_pid=
cleanup() {
  if [[ -n "$host_pid" ]]; then kill "$host_pid" 2>/dev/null || true; wait "$host_pid" 2>/dev/null || true; fi
  timeout 15s adb logcat -d >test-results/native-logcat.txt 2>&1 || true
  timeout 15s adb exec-out screencap -p >test-results/native-device.png 2>/dev/null || true
  rm -rf "$test_dir"
}
trap cleanup EXIT
npm run assets
dotnet build tests/DigitalSorter.BrowserHost --configuration Release -p:RestoreLockedMode=true
dotnet publish src/DigitalSorter.Auth --configuration Release --no-restore -o "$test_dir/published" -p:RestoreLockedMode=true
export TEST_WEB_ROOT="$test_dir/published/wwwroot"
export TEST_ORIGIN=https://localhost:7443 TEST_PORT=7443
export TEST_CERTIFICATE_PEM="$fixture_dir/test-ca.pem"
TEST_CERTIFICATE="$fixture_dir/test.pfx" TEST_FIXTURES="$PWD/tests/fixtures" \
  dotnet tests/DigitalSorter.BrowserHost/bin/Release/net10.0/DigitalSorter.BrowserHost.dll >test-results/native-host.log 2>&1 &
host_pid=$!
ready=false
for attempt in {1..60}; do
  if curl --cacert "$TEST_CERTIFICATE_PEM" --fail --silent "$TEST_ORIGIN/health" >/dev/null; then ready=true; break; fi
  if ! kill -0 "$host_pid" 2>/dev/null; then cat test-results/native-host.log; exit 1; fi
  sleep 1
done
if [[ "$ready" != true ]]; then cat test-results/native-host.log; exit 1; fi
adb install -r "$fixture_dir/tv-test.apk"
adb logcat -c
timeout --signal=TERM --kill-after=10s 360s node --test --test-force-exit --test-concurrency=1 tests/native/tv.spec.mjs
