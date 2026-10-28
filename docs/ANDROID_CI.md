# Android APK CI

> **Unity Personal users:** Please use [PERSONAL_ANDROID_BUILD.md](PERSONAL_ANDROID_BUILD.md) instead. It uses the official Unity Build Automation free tier and the [Android native plugin workflow](../.github/workflows/android-native.yml). The GameCI APK workflow below requires a supported CI activation method and is not the recommended route for Unity Personal.


Workflow: [Android APK (RmlUnity MVP)](../.github/workflows/android-apk.yml)

**Status:** This repository is still a prototype. The workflow compiles a Linux-hosted Android ARM64 C++ plugin, then invokes Unity 2022.3.62f1 to build a debug-signed Android APK. An actual successful build must be confirmed in GitHub Actions; committing a workflow does not demonstrate that an APK was produced.

## Steps performed automatically

1. GitHub-hosted Ubuntu runner installs Temurin JDK 17 and the Android SDK/NDK.
2. CMake compiles FreeType 2.13.3 and RmlUi 6.3 statically into `libRmlUnityBridge.so` for `arm64-v8a`.
3. The resulting `.so` is uploaded and then copied into the Unity project's `Assets/Plugins/Android/libs/arm64-v8a/`.
4. GameCI starts Unity 2022.3.62f1 with Android Build Support and runs `RmlUnity.Editor.AndroidBuild.Build`.
5. Unity generates the demo scene, enables Android + IL2CPP + ARM64, and attempts to produce `build/Android/RmlUnity-MVP.apk`.
6. On success, GitHub stores an artifact named `RmlUnity-MVP-Android-APK` for 14 days.

The APK is a **debug-signed demonstration**, not an app-store production release.

## Unity license is required

GitHub-hosted Unity is not license-free. Do not put Unity credentials or license files in source control.

Under GitHub repository → Settings → Secrets and variables → Actions, configure:

- `UNITY_EMAIL`: Unity ID email address.
- `UNITY_PASSWORD`: Unity ID password.
- `UNITY_SERIAL`: a valid serial-based Unity license **or** `UNITY_LICENSE`: a valid supported `.ulf` license file.

Unity officially does **not** support manually activating **Unity Personal** via `.alf`/`.ulf`. The older GameCI activation guides may still mention this process but should not be treated as guaranteed or officially supported. If you only have Unity Personal, consider a self-hosted build machine where Unity Hub has activated your account, while ensuring Unity license compliance. See the current [Unity licensing documentation](https://docs.unity.com/en-us/engine/6000.6/manual/get-started/install-and-upgrade/licenses-and-activation/manual-license-activation/manual-activation-guide).

## Run from a phone

1. Open the private repository's **Actions** tab.
2. Select **Android APK (RmlUnity MVP)**.
3. Tap **Run workflow**, select `main` and confirm.
4. Inspect the two jobs: **Compile RmlUi for Android arm64-v8a** and **Build installable Unity APK**.
5. When the run is green, scroll to **Artifacts**, download `RmlUnity-MVP-Android-APK`, extract the ZIP, then install the APK on an ARM64 Android phone.

If the Unity job fails at **Check Unity activation secrets**, the native C++ job may still have succeeded, but **there is no APK**.

## Current limitations

- No automation for signing a Play Store bundle (AAB) or publishing an application.
- Android touch input currently maps a single touch to a mouse pointer.
- No rich CSS effects/clip masks/IME; basic rectangular clipping is implemented in the shader.
- No baseline performance comparison against uGUI or UI Toolkit yet.
