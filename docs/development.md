# ONEXTweaks development

An Xbox Game Bar widget and Windows x64 companion tools for ONEXPLAYER 3.

## Windows CI

The **Windows build and tests** check runs for pull requests targeting `main` and after changes land on `main`. It runs .NET model tests and installer tests, builds the widget, both launchers, Bridge, DependencyManager and PowerAgent, then packages a development Setup EXE. A temporary certificate signs the test MSIX for packaging. The runner does not install the widget or publish binaries.

The CI commands can also run locally with Windows PowerShell 5.1, Python 3, .NET SDKs 10.0.403 and 6.0.423, Visual Studio 2022 with Universal Windows Platform development, and Windows SDK 10.0.26100.0.

```powershell
.\tools\ci\Test-Windows.ps1
.\tools\ci\Build-SetupSmoke.ps1 -OutputRoot "$env:TEMP\onextweaks-ci"
```

Use a new output directory for each run.

## Merge a pull request

Use Python 3.11 or later and an authenticated GitHub CLI.

```sh
python3 tools/ci/github_ops.py merge 42
```

The command waits for the exact PR head to pass **Windows build and tests** and passes that SHA to GitHub as part of the squash merge. If the PR changes while it waits, run the command again for the new SHA. Use `--check-only` to inspect the result without merging. Use this command for merges while the private repository is on GitHub Free.

The CI report links to the Windows job and shows the model tests, installer tests and Setup build result. You can refresh the report directly with `python3 tools/ci/github_ops.py report 42`. For changes to the interface, capture the affected screen in a light and dark theme on a trusted Windows device. Attach the two screenshots to the PR and pass their HTTPS URLs via `report 42 --head <PR_SHA> --light <URL> --dark <URL>`. Running Windows CI alone gives build and test coverage for those changes.

## Prepare a beta release draft

Run the release command on a trusted Windows x64 build machine with the CI toolchain, Git, GitHub CLI, Python 3.11 or later, OpenSSL and the pinned beta signing certificate in `Cert:\CurrentUser\My`. Supply the signed 1109 rollback MSIX and a trusted runner for a Windows 11 x64 VM with Xbox Game Bar and OneXConsole installed. Put signing credentials on the build machine outside the repository and keep the VM runner outside PR-controlled code.

```powershell
.\tools\ci\Prepare-Release.ps1 -Commit $commit -Tag 'v0.29.1110-beta.1' `
    -OutputRoot $candidateDirectory -BetaCertificateThumbprint $thumbprint `
    -RollbackWidget $previousMsix -QaRunner $vmRunner
```

The command checks for green Windows CI on the chosen `main` commit, builds Native Setup from an isolated checkout, and gives the signed EXE to the VM runner. It passes `-Setup`, `-Commit`, `-Sha256` and `-Receipt` to that runner. Configure the runner to use your lab's VM transport, existing OneXConsole startup fixture and signed 1109 baseline. Use `-FrameworkDirectory` when the framework APPX files are supplied separately from the MSIX build.

The runner must check the EXE digest in the guest and return a UTF-8 JSON receipt keyed by the supplied commit and hash. Run the actual EXE in the signed-in Windows account for install and upgrade. Verify readiness again after reboot. Exercise and verify the 1109 rollback from the candidate payload, then verify the upgraded profiles and restored OneXConsole startup after uninstall. Only emit a passing receipt when every scenario passes.

```json
{
  "schema": 1,
  "commit": "<full SHA>",
  "sha256": "<Setup SHA-256>",
  "machine": {"os": "Windows 11", "architecture": "x64", "virtualMachine": true, "oneXConsole": true},
  "checks": {
    "cleanInstall": true, "reboot": true, "upgrade1109": true,
    "profilesPreserved": true, "rollback1109": true,
    "uninstall": true, "oemStartupRestored": true
  }
}
```

After validating the receipt and hash, the command drafts a GitHub prerelease with generated notes, the verified Setup and its SHA-256. Use `-CheckOnly` to run the complete local acceptance without creating a GitHub release. Before publishing the draft, verify Game Bar, both virtual controller modes and the OneXConsole startup handoff on physical ONEXPLAYER. See the [installer tools](../tools/installer/README.md) and [license notices](../third-party/NOTICE.txt).

## Source baseline

The imported sources bring together the Widget frontend used in 1109, the launcher changes used when repackaging that frontend as 1110, and the Bridge, PowerAgent and installer used in 1110. The build compiles the embedded DependencyManager from the tracked DependencyManager sources. The archived 1110 MSIX had an older prebuilt copy of this helper. A new release must be tested from the new sources.

The ClawTweaks frontend license and notices are in [the frontend directory](../src/ONEXTweaks.Widget). Other dependency notices are in [third-party/NOTICE.txt](../third-party/NOTICE.txt).
