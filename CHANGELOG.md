# Changelog

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
