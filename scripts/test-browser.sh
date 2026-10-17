#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
test_dir=$(mktemp -d)
mkdir -p test-results
host_pid=
cleanup() {
  if [[ -n "$host_pid" ]]; then kill "$host_pid" 2>/dev/null || true; wait "$host_pid" 2>/dev/null || true; fi
  rm -rf "$test_dir"
}
trap cleanup EXIT
openssl req -x509 -newkey rsa:2048 -sha256 -days 2 -nodes -keyout "$test_dir/key.pem" -out "$test_dir/cert.pem" \
  -subj '/CN=DigitalSorter automated tests' -addext 'subjectAltName=DNS:localhost,IP:127.0.0.1' >"$test_dir/certificate.log" 2>&1
openssl pkcs12 -export -out "$test_dir/test.pfx" -inkey "$test_dir/key.pem" -in "$test_dir/cert.pem" -passout pass:
npm run assets
dotnet build tests/DigitalSorter.BrowserHost/DigitalSorter.BrowserHost.csproj --configuration Release -p:RestoreLockedMode=true
dotnet publish src/DigitalSorter.Auth --configuration Release --no-restore -o "$test_dir/published" -p:RestoreLockedMode=true
export TEST_WEB_ROOT="$test_dir/published/wwwroot"
export TEST_PORT=${TEST_PORT:-7443}
export TEST_ORIGIN="https://localhost:$TEST_PORT"
export TEST_CERTIFICATE_PEM="$test_dir/cert.pem"
TEST_CERTIFICATE="$test_dir/test.pfx" TEST_CERTIFICATE_PASSWORD= TEST_BIND_ADDRESS=0.0.0.0 TEST_FIXTURES="$PWD/tests/fixtures" TEST_DATA_DIRECTORY="$test_dir/data" \
  dotnet tests/DigitalSorter.BrowserHost/bin/Release/net10.0/DigitalSorter.BrowserHost.dll >test-results/browser-host.log 2>&1 &
host_pid=$!
ready=false
for attempt in {1..60}; do
  if curl --cacert "$test_dir/cert.pem" --fail --silent "$TEST_ORIGIN/health" >/dev/null; then ready=true; break; fi
  if ! kill -0 "$host_pid" 2>/dev/null; then cat test-results/browser-host.log; exit 1; fi
  sleep 1
done
if [[ "$ready" != true ]]; then cat test-results/browser-host.log; exit 1; fi
node --test --test-concurrency=1 tests/browser/gallery.spec.mjs
