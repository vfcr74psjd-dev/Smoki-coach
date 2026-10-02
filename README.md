# Smoki-coach

Sm0ki Solo Coach is a personal CS2 companion app using CS2 Game State Integration.

## Update system
- `source.zip` contains the current app source.
- `VERSION` controls the app version.
- GitHub Actions builds a self-contained Windows EXE.
- The build is written to `dist/Sm0kiSoloCoach.exe`.
- `update.json` is generated with version, download URL and SHA256.
- The app's **Update** button checks `update.json`, downloads the EXE, verifies SHA256, replaces itself and restarts.

No memory reading, DLL injection or input automation is used.
