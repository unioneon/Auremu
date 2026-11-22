# Third-party software

Auremu is an independent Windows interface around the official Android Emulator.
It is not affiliated with or endorsed by Google, Android, Genymobile or Eclipse.
Android is a trademark of Google LLC. Other names belong to their respective owners.

## Components in the Windows packages

The unmodified official **scrcpy 5.0.1 Windows x64 distribution** is included in
`runtime/tools/scrcpy-win64-v5.0.1`. Its Apache License 2.0 is retained as
`LICENSE.txt`. The bundle contains scrcpy, its server, ADB, FFmpeg libraries,
SDL 3 and libusb. Their respective licenses continue to apply.

- [scrcpy release and corresponding source](https://github.com/Genymobile/scrcpy/releases/tag/v5.0.1)
- [scrcpy build instructions and dependencies](https://github.com/Genymobile/scrcpy/blob/v5.0.1/doc/build.md)
- [ADB / Android platform tools source](https://android.googlesource.com/platform/packages/modules/adb/)
- [FFmpeg 9.0.2 corresponding source](https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz) · [licensing](https://ffmpeg.org/legal.html)
- [SDL 3.4.18 source and license](https://github.com/libsdl-org/SDL/tree/release-3.4.18)
- [libusb 1.0.30 source and LGPL license](https://github.com/libusb/libusb/tree/v1.0.30)
- [dav1d source](https://code.videolan.org/videolan/dav1d) · [zlib source](https://zlib.net/)

No third-party executable is modified. The shared libraries remain replaceable.
The official vendor archive SHA-256 is pinned in `packaging/build-release.ps1`.
Dependency versions and build settings are documented in the
[upstream release build scripts](https://github.com/Genymobile/scrcpy/tree/v5.0.1/app/deps).
Full license texts are included in the package's `licenses` folder.

## Downloaded separately after consent

Android SDK Tools, Emulator, ADB and Google Play system images are downloaded
from Google's official repositories after the user accepts the SDK terms.
They are **not redistributed inside Auremu's packages**.

- [Android SDK License Agreement](https://developer.android.com/studio/terms)
- [Eclipse Temurin JDK 21 source and licenses](https://adoptium.net/temurin/releases/?version=21)

Temurin JDK 21 is downloaded by the setup wizard using official Adoptium metadata
and verified against its published SHA-256. The JDK archive retains its licenses.
Google applications and services may communicate with Google independently of Auremu.

## Auremu source and branding

Auremu source is published for inspection. No general license for redistribution
or modification of the Auremu source or brand is granted in this release.
Third-party licenses above are unaffected.
