# ONEXTweaks

An Xbox Game Bar widget and Windows x64 companion tools for ONEXPLAYER 3.

## Windows CI

The **Windows build and tests** check runs for pull requests targeting `main` and after changes land on `main`. It runs .NET model tests and installer tests, builds the widget, both launchers, Bridge, DependencyManager and PowerAgent, then packages a development Setup EXE. A temporary certificate signs the test MSIX for packaging. The runner does not install the widget or publish binaries.

The CI commands can also run locally with Windows PowerShell 5.1, Python 3, .NET SDKs 10.0.403 and 6.0.423, Visual Studio 2022 with Universal Windows Platform development, and Windows SDK 10.0.26100.0.

```powershell
.\tools\ci\Test-Windows.ps1
.\tools\ci\Build-SetupSmoke.ps1 -OutputRoot "$env:TEMP\onextweaks-ci"
```

Use a new output directory for each run.

## Release approval

1. Pick a commit on `main` with a successful Windows CI run and record its SHA.
2. Build the Native widget with `tools/Build-Widget.ps1 -Native` and assemble the release using the [installer tools](tools/installer/README.md). Sign on the trusted release machine. Record the SHA-256 of the Setup EXE and keep the signing key off GitHub.
3. Test that exact EXE on Windows 11 x64 with OneXConsole present. Check fresh install, reboot, preserved profiles during upgrade, rollback, uninstall, and restoration of the original OneXConsole autostart. Check Game Bar and controller functionality on physical ONEXPLAYER before publishing. Record the results against the commit and EXE hashes.
4. Publish the verified Setup from a tag on the tested commit, with matching source and [license notices](third-party/NOTICE.txt). A release is published separately after the checks above.

## Source baseline

The imported sources bring together the Widget frontend used in 1109, the launcher changes used when repackaging that frontend as 1110, and the Bridge, PowerAgent and installer used in 1110. The build compiles the embedded DependencyManager from the tracked DependencyManager sources. The archived 1110 MSIX had an older prebuilt copy of this helper. A new release must be tested from the new sources.

The ClawTweaks frontend license and notices are in [the frontend directory](src/ONEXTweaks.Widget). Other dependency notices are in [third-party/NOTICE.txt](third-party/NOTICE.txt).
