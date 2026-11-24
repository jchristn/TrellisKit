# Local device testing

How to build the mobile app as a development build and run it on the iOS simulator (iPhone and iPad), the Android emulator, and a physical iPhone, against the local Docker stack. Everything here is engine-generic: the property is chosen only by `TK_PROPERTY_DIR` (absolute, or relative to the engine root). The commands below write it as `$TK_PROPERTY_DIR`; leave it unset to build the neutral sample (`samples/example`).

```bash
export TK_PROPERTY_DIR=/absolute/path/to/your-property     # or unset for the sample
cd mobile
npm install
```

## Prerequisites

| Tool | Notes |
|---|---|
| Node.js 24.3+ | See README "Prerequisites" |
| Xcode with iOS simulators, CocoaPods | `pod` on the `PATH` (Homebrew `/opt/homebrew/bin/pod` works) |
| JDK 17 | For Gradle. Homebrew `openjdk@17` (`export JAVA_HOME=/opt/homebrew/opt/openjdk@17/libexec/openjdk.jdk/Contents/Home`) or the JDK bundled with Android Studio (`/Applications/Android Studio.app/Contents/jbr/Contents/Home`) |
| Android SDK | `export ANDROID_HOME=$HOME/Library/Android/sdk`, with `platform-tools`, `emulator`, `cmdline-tools/latest`, a platform, build tools, the NDK, and an emulator system image |
| The local Docker stack | `cd docker && ./dev.sh <property folder>/deploy/local.env`. The API listens on `http://127.0.0.1:8000` (loopback only) |

## 1. Generate the native projects

The app uses Continuous Native Generation: `ios/` and `android/` are generated from `app.config.js` and the property, and are git ignored (`mobile/.gitignore`). Regenerate them whenever `TK_PROPERTY_DIR`, a config plugin, or a native dependency changes.

```bash
npx expo prebuild --clean --no-install
git checkout -- package.json   # prebuild rewrites the "ios" and "android" scripts; keep the committed ones
```

Without `<property>/mobile/updates/certificate.pem` the build prints "Live updates are disabled" and builds without expo-updates checks; that is expected locally (see README "Live updates").

## 2. Start Metro

One Metro server serves every simulator, emulator, and device. Start it from `mobile/` with the same `TK_PROPERTY_DIR`, and restart it with `--clear` after changing `TK_PROPERTY_DIR` or `EXPO_PUBLIC_API_URL`.

```bash
npx expo start --dev-client --port 8081 --clear
```

## 3. iOS simulator (iPhone)

```bash
cd ios && pod install && cd ..
# Resolve Swift packages first (MapLibre and the IAP library ship as Swift packages; see "Known local issues")
xcodebuild -resolvePackageDependencies -workspace ios/*.xcworkspace -scheme "$(basename ios/*.xcworkspace .xcworkspace)" -derivedDataPath ios/build/dd
xcodebuild -workspace ios/*.xcworkspace -scheme "$(basename ios/*.xcworkspace .xcworkspace)" \
  -configuration Debug -sdk iphonesimulator -destination 'generic/platform=iOS Simulator' \
  -derivedDataPath ios/build/dd ARCHS=arm64 ONLY_ACTIVE_ARCH=NO
```

`npx expo run:ios --device "iPhone 18 Pro"` does the same in one step (and starts Metro), if you prefer it.

Install and open the dev client on a simulator (`xcrun simctl list devices available` lists them):

```bash
UDID=<simulator udid>
SCHEME=$(node -p "require('./app.config.js')().scheme")
xcrun simctl boot $UDID; open -a Simulator
xcrun simctl install $UDID ios/build/dd/Build/Products/Debug-iphonesimulator/*.app
xcrun simctl openurl $UDID "$SCHEME://expo-development-client/?url=http%3A%2F%2F127.0.0.1%3A8081"
```

The simulator shares the Mac's loopback, so the app reaches the API at `http://127.0.0.1:8000` with no configuration.

## 4. iPad simulator

The same simulator build runs on iPad (`ios.supportsTablet`). Boot an iPad simulator (for example "iPad Pro 13-inch") and repeat the `install` and `openurl` steps with its UDID. At a window width of 768 points or more the app switches to tablet layouts: Feed and Forums show a list with the selected item beside it, and Forums uses three panes (forums, threads, thread).

## 5. Android emulator

Create an emulator once (any installed system image; `sdkmanager --list_installed` shows them):

```bash
$ANDROID_HOME/cmdline-tools/latest/bin/avdmanager create avd -n Dev_Phone \
  -k "system-images;android-36.1;google_apis_playstore;arm64-v8a" -d pixel_9
$ANDROID_HOME/emulator/emulator -avd Dev_Phone &
```

Build, install, and open the dev client:

```bash
cd android && ./gradlew :app:assembleDebug -PreactNativeArchitectures=arm64-v8a && cd ..
adb install -r android/app/build/outputs/apk/debug/app-debug.apk
adb reverse tcp:8081 tcp:8081      # Metro
adb reverse tcp:8000 tcp:8000      # optional: the API at 127.0.0.1 too
PACKAGE=$(node -p "require('./app.config.js')().android.package")
SCHEME=$(node -p "require('./app.config.js')().scheme")
adb shell am start -a android.intent.action.VIEW \
  -d "$SCHEME://expo-development-client/?url=http%3A%2F%2F127.0.0.1%3A8081" $PACKAGE
```

`-PreactNativeArchitectures=arm64-v8a` builds only the emulator's ABI (much faster on Apple silicon). `npx expo run:android` also works. Without `EXPO_PUBLIC_API_URL` the app calls `http://10.0.2.2:8000`, the emulator's alias for the Mac's loopback.

## 6. Physical iPhone over USB

1. Enable Developer Mode on the iPhone (Settings > Privacy and Security) and trust the Mac when you connect the cable.
2. Open `ios/<App>.xcworkspace` in Xcode, select the app target > Signing and Capabilities, tick "Automatically manage signing", and choose your team.
   - A paid Apple Developer team can keep the property's bundle id (register it, with Sign in with Apple and In-App Purchase enabled, or let Xcode do it).
   - A free personal team cannot use Sign in with Apple or In-App Purchase. Remove those two capabilities in Xcode for the local build, and if the bundle id is already registered by another team, change it locally (for example append `.dev`). These are local edits to the generated `ios/` folder; they disappear on the next prebuild and must never be committed.
3. Point the app at your Mac on the LAN. Docker publishes the API on `127.0.0.1` only, so forward a LAN port to it, for example with `socat` (`brew install socat`):

   ```bash
   LAN_IP=$(ipconfig getifaddr en0)
   socat TCP-LISTEN:8000,bind=$LAN_IP,reuseaddr,fork TCP:127.0.0.1:8000 &
   EXPO_PUBLIC_API_URL=http://$LAN_IP:8000 npx expo start --dev-client --clear
   ```

4. Build and install on the device: `npx expo run:ios --device` (pick the iPhone), or Product > Run in Xcode. The dev client finds Metro on the LAN (or scan the QR code Metro prints). The first launch asks for local network access; allow it.
5. Profile > About this app shows the API address in use and the health check result.

Plain `http://` works in development builds. Release builds must point at HTTPS.

## Demo accounts

Seed the stack with the property's seeding tool (`tools/seed`); it writes the accounts and their local passwords to a git-ignored file in the property folder. Never copy those passwords into committed files. A member with an active membership opens the tabs; a member without one lands on the paywall; a new sign-up walks the waiver and the profile setup steps.

## Known local limitations

| Area | What happens locally | What it needs |
|---|---|---|
| Push notifications | "Turn on notifications" asks for permission and continues; no push token is registered (no EAS project id), and simulators cannot receive remote pushes | `EAS_PROJECT_ID` (or `mobile.easProjectId`), a physical device, and on Android `GOOGLE_SERVICES_JSON` with FCM credentials. The in-app notification center works without push |
| Store purchases | The paywall shows "Purchases are not available in this build" while the server's `appleIap` and `googlePlay` providers are disabled, and the app keeps running | A StoreKit configuration file in the Xcode scheme (simulator) or a sandbox account on a device; a Play internal testing track on Android |
| Google, Facebook, and Apple sign-in | Hidden unless the server enables the provider and, for Google on iOS and Facebook, the build variables are set (see README). The native Google module is left unlinked on iOS without `EXPO_PUBLIC_GOOGLE_IOS_URL_SCHEME`, and the app checks for it before loading the package | Provider credentials in the server settings and the build variables |
| Maps | Without a configured map style (and, for the platform map on Android, a Google Maps key) the directory shows the list only; "Near me" still works | A MapLibre style URL or a platform map key in the property configuration |
| Location | The iOS simulator has no position until you set one: `xcrun simctl location <udid> set <lat>,<lon>` (Features > Location in the Simulator app). Android: `adb emu geo fix <lon> <lat>` | |
| Live updates | Disabled without the property's code signing certificate (a build warning); the embedded bundle runs | `node scripts/generate-updates-certificate.js <property folder>` |
| Palette and AI switch | Edits in the admin console reach the app at the next launch or when it returns to the foreground (`GET /api/v1/property` is revalidated by ETag) | |

## Known local issues and tips

- **Swift package download stalls.** `xcodebuild` can hang at "Resolve Package Graph" while downloading a binary Swift package (MapLibre's xcframework), with no CPU use. Stop it, delete the stale lock (`rm -f $TMPDIR/*maplibre*lock`), and run `xcodebuild -resolvePackageDependencies` on its own before building. If it still stalls, download the zip named in `ios/build/dd/SourcePackages/checkouts/maplibre-gl-native-distribution/Package.swift` with `curl` into `~/Library/Caches/org.swift.swiftpm/artifacts/` (file name: the URL with every non-alphanumeric character replaced by `_`) and resolve again.
- **Jest after a native build.** `jest.config.js` ignores `ios/` and `android/`; without that, the copies of JavaScript packages in those folders break every render test.
- **The dev tools button** (the floating gear) covers the header's notification bell. Hide it from the dev menu ("Tools button"); shake the device, press Cmd+Ctrl+Z in the iOS simulator, or Cmd+M in the Android emulator to open the menu.
- **Fast Refresh and navigation.** After an edit, Expo Router can reset the current stack to its first screen. Navigate again, or relaunch the app.
- **UI automation.** Maestro (`maestro --device <udid or emulator id> test flow.yaml`) drives both platforms. Cards expose one combined accessibility label, so match them with a regular expression (`".*Card title.*"`) or a `testID`.
