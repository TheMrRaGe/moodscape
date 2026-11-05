#!/usr/bin/env bash
# Builds moodscape-mirror.apk from ../mirror.html without Gradle: aapt2 + javac + d8 + apksigner.
#   ANDROID_HOME=/path/to/sdk ./android/build.sh [versionCode] [versionName]
# Needs a JDK (17+), Python 3 with Pillow, and an Android SDK with platforms;android-34 and build-tools;35.0.0:
#   sdkmanager "platforms;android-34" "build-tools;35.0.0"
# The signing key is made on the first build (android/moodscape-release.keystore + keystore.properties, both
# git-ignored). Keep them: Android only installs an update over the old app if it's signed with the same key.
set -euo pipefail
cd "$(dirname "$0")"
SDK=${ANDROID_HOME:-${ANDROID_SDK_ROOT:-$HOME/android-sdk}}
BT=$SDK/build-tools/35.0.0   # 34.0.0's d8 crashes on this code
JAR=$SDK/platforms/android-34/android.jar
VCODE=${1:-1}
VNAME=${2:-1.0}
[ -f "$JAR" ] && [ -x "$BT/aapt2" ] || { echo "Android SDK not found at $SDK (set ANDROID_HOME)"; exit 1; }

echo "== app $VNAME ($VCODE) from ../mirror.html"
rm -rf build && mkdir -p build/res build/assets build/classes build/dex build/gen
cp ../mirror.html ../icon-192.png build/assets/
python3 - ../icon-512.png build/res <<'PY'
import sys
from pathlib import Path
from PIL import Image
src, out = Image.open(sys.argv[1]).convert("RGBA"), Path(sys.argv[2])
for d, px in {"mdpi": 48, "hdpi": 72, "xhdpi": 96, "xxhdpi": 144, "xxxhdpi": 192}.items():
    p = out / f"mipmap-{d}"; p.mkdir(parents=True, exist_ok=True)
    src.resize((px, px), Image.LANCZOS).save(p / "ic_launcher.png")
PY
"$BT/aapt2" compile --dir build/res -o build/res.zip
"$BT/aapt2" link -o build/unsigned.apk -I "$JAR" --manifest AndroidManifest.xml -A build/assets build/res.zip \
    --min-sdk-version 26 --target-sdk-version 34 --version-code "$VCODE" --version-name "$VNAME" --java build/gen
javac -nowarn -source 11 -target 11 -classpath "$JAR" -d build/classes $(find src build/gen -name '*.java') 2>&1 | grep -v "bootstrap classpath" || true
"$BT/d8" --release --lib "$JAR" --min-api 26 --output build/dex $(find build/classes -name '*.class')
(cd build/dex && zip -qj ../unsigned.apk classes.dex)
"$BT/zipalign" -f -p 4 build/unsigned.apk build/aligned.apk

if [ ! -f moodscape-release.keystore ]; then
  PASS=$(python3 -c "import secrets; print(secrets.token_urlsafe(18))")
  printf 'storePassword=%s\nkeyAlias=moodscape\n' "$PASS" > keystore.properties
  keytool -genkeypair -keystore moodscape-release.keystore -alias moodscape -keyalg RSA -keysize 3072 -validity 10000 \
      -storepass "$PASS" -keypass "$PASS" -dname "CN=Moodscape Mirror, O=Heath Dale" >/dev/null 2>&1
  echo "== made a new signing key: android/moodscape-release.keystore (keep it, with keystore.properties)"
fi
PASS=$(sed -n 's/^storePassword=//p' keystore.properties)
"$BT/apksigner" sign --ks moodscape-release.keystore --ks-key-alias moodscape --ks-pass "pass:$PASS" --key-pass "pass:$PASS" \
    --out moodscape-mirror.apk build/aligned.apk
"$BT/apksigner" verify moodscape-mirror.apk
echo "== built android/moodscape-mirror.apk ($(du -h moodscape-mirror.apk | cut -f1))"
