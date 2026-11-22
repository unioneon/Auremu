# Changelog

## 0.1.1 — 2026-11-22

- English desktop interface, first-run wizard, documentation and screenshots.
- Native draggable title area, including restoring a maximized window by dragging.
- Auremu application icon embedded in the executable and WPF windows.
- Direct3D 11 display with automatic hardware video decoding and software fallback.
- Cold boot starts immediately when Quick Boot metadata is missing or marked dirty.
- Closing during startup terminates only the emulator owned by that window.

Validated real Android boot and embedded D3D11VA display on an NVIDIA RTX 3060 Ti,
phone/tablet switching, graceful shutdown and snapshot creation. Screenshots show
the running Android home screen. No game performance improvement is claimed;
the FPS counter measures the changing capture stream.

## 0.1.0 — 2026-11-22

First public preview of Auremu for Windows x64.

- Official Android 17 Google Play image and the standard Android home screen.
- Phone/tablet frames based on the approved layout, light and dark themes,
  aligned side controls, smooth screen corners and subdued icons.
- First-run Android SDK/JDK installation with checksum verification and consent.
- Per-user installer and portable package, without accounts or saved devices.
- APK installation, file transfer, screen capture/recording and GPS controls.
- Isolated data directories, Quick Boot and a cold-start fallback.

Known limits: unsigned binaries; Windows x64/WHPX required; hardware-dependent
performance; first-run downloads require internet. Automated validation covers
SDK protocol fixtures, release isolation and layout/corner geometry. Complete
first-time downloads on a fresh Windows PC have not yet been validated end to end.
