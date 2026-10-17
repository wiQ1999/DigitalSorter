#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
sdk_dir=${ANDROID_HOME:?Android SDK is required}
export PATH="$sdk_dir/platform-tools:$sdk_dir/emulator:$PATH"
manager="$sdk_dir/cmdline-tools/latest/bin/sdkmanager"
avd_manager="$sdk_dir/cmdline-tools/latest/bin/avdmanager"
image='system-images;android-36;android-tv;x86_64'
mkdir -p test-results
emulator_state_dir=$(mktemp -d "${RUNNER_TEMP:-/tmp}/digitalsorter-tv.XXXXXX")
export ANDROID_AVD_HOME="$emulator_state_dir/avd"
mkdir -p "$ANDROID_AVD_HOME"
set +o pipefail
yes | "$manager" --licenses >/dev/null
license_status=${PIPESTATUS[1]}
set -o pipefail
test "$license_status" -eq 0
"$manager" --install "$image" emulator platform-tools
printf 'no\n' | "$avd_manager" create avd --force --name DigitalSorterTV --package "$image" --device tv_1080p --path "$ANDROID_AVD_HOME/DigitalSorterTV.avd"
printf 'avd.ini.encoding=UTF-8\npath=%s\ntarget=android-36\n' "$ANDROID_AVD_HOME/DigitalSorterTV.avd" >"$ANDROID_AVD_HOME/DigitalSorterTV.ini"
adb start-server
serial=emulator-5554
export ANDROID_SERIAL="$serial"
emulator_pid=
cleanup() {
  local result=$?
  timeout 15s adb -s "$serial" logcat -d >test-results/native-emulator-logcat.txt 2>&1 || true
  timeout 5s adb -s "$serial" emu kill >/dev/null 2>&1 || true
  if [[ -n "$emulator_pid" ]]; then kill -KILL "$emulator_pid" 2>/dev/null || true; wait "$emulator_pid" 2>/dev/null || true; fi
  rm -rf "$emulator_state_dir"
  exit "$result"
}
trap cleanup EXIT
"$sdk_dir/emulator/emulator" -avd DigitalSorterTV -port 5554 -no-window -accel on \
  -gpu swangle -feature -Vulkan,-HardwareDecoder -no-audio -no-boot-anim -no-snapshot -no-metrics \
  -memory 3072 -cores 2 >test-results/native-emulator.log 2>&1 &
emulator_pid=$!
stable=0
boot_deadline=$((SECONDS + 360))
while [[ "$SECONDS" -lt "$boot_deadline" ]]; do
  if ! kill -0 "$emulator_pid" 2>/dev/null; then cat test-results/native-emulator.log; exit 1; fi
  boot=$(timeout 8s adb -s "$serial" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r' || true)
  package=$(timeout 8s adb -s "$serial" shell pm path android 2>/dev/null || true)
  if [[ "$boot" == 1 && "$package" == package:* ]]; then
    stable=$((stable + 1))
    if [[ "$stable" -ge 4 ]]; then break; fi
  else
    stable=0
    timeout 8s adb reconnect offline >/dev/null 2>&1 || true
  fi
  sleep 2
done
if [[ "$stable" -lt 4 ]]; then cat test-results/native-emulator.log; exit 1; fi
adb -s "$serial" shell settings put global device_provisioned 1
adb -s "$serial" shell settings put secure user_setup_complete 1
adb -s "$serial" shell settings put secure tv_user_setup_complete 1
adb -s "$serial" shell settings put system screen_off_timeout 2147483647
adb -s "$serial" shell input keyevent KEYCODE_WAKEUP
adb -s "$serial" shell wm dismiss-keyguard
scripts/test-native-tv.sh
