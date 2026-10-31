# DropSort final release hardening — 2026-10-13

## Decision

The user accepted current functionality. Keep the immutable 0.10.0-rc.1 candidate. No application, installer or schema defect was confirmed, and no payload or source fix is justified. Do not promote it to an official stable release until disposable-environment installer lifecycle evidence is available and the user approves publication.

Recommend 1.0.0 for the first stable WinUI release after those gates. The repository already has a legacy Python v1.0.0 tag; preserve it. A separate native tag namespace such as winui-v1.0.0 needs approval at publication. Product V2 milestones do not require a 2.0.0 software version. Current artifact version remains 0.10.0-rc.1 / numeric 0.10.0.0; no rc.2 is needed because no code/payload revision is being shipped.

## Verified work

- Source commit a5291496d664de308716b6ac528c5e053b7ac7c0; existing implementation checkpoint 572bfa1d27f06a95c784461b3f047c532cfa0245; branch codex. Unrelated changes preserved; main untouched.
- Fresh full Release .NET suite:734 passed, 0 failed, 0 skipped. Focused TV/import/UI/startup/migration suite:165 passed, 0 failed, 0 skipped.
- Initial restricted full run:733 passed, 1 failed because CredWriteW was unavailable to the isolated synthetic credential fixture. The unchanged full suite passed in the normal user session; no test was skipped or weakened. Seven existing xUnit analyzer warnings remain (5 xUnit1031, 2 xUnit2013).
- Fresh Visual Studio 18 MSBuild Rebuild:Release / x64/win-x64, SelfContained=true, WindowsAppSDKSelfContained=true, restore, empty payload directory. Exit 0; no application build warnings/errors.
- Installer compilation repeated with the established Inno 6 script into a separate QA output. Exit 0; original RC1 installer was not replaced or installed.
- Original RC1 installer/ZIP/EXE/DLL SHA256 values match their original manifest. Portable inventory: 534 files includes .NET/WinUI/SQLite/PRI and 15 XBF resources; no databases, logs, PDBs, publish subtree or private/test data.
- Fresh ZIP extraction matched all 534 original entries. Exact RC1 executable launched with a new isolated QA profile; synthetic batch registered 1 movie, 1 show, 2 seasons, 3 episodes and 4 media files. Movie Details, TV hierarchy, Settings version and isolated data path were verified. Synthetic watch history was recorded (1 watch event). User then asked to reduce app testing; further UI/relaunch work stopped.
- QA SQLite schema 15, quick_check=ok, no foreign-key violations. Fixture hashes are compared in the final QA safety evidence. No real profile was launched and no real installation/media mutation was performed.

## Rebuild reproducibility

Fresh DropSort.UI.dll has a different hash from RC1, while the other 533 payload files match. Read-only PE/IL comparison found differences confined to generated WinRT/XAML metadata/vtable ordering and build identity; application method bodies match. Do not claim byte-identical rebuild. The recompiled QA installer uses that fresh payload and is not a replacement RC1 candidate. Original artifacts and manifest remain unchanged. Any future shipped rebuild or signature requires a new candidate revision, updated checksums and packaged-runtime verification.

## Installer evidence boundary

Host:Windows 11 Pro 25H2,build 26200. WindowsSandbox.exe and local Hyper-V/VirtualBox/VMware/QEMU tooling were unavailable. No feature was enabled and no reboot/system/security change was made.

Fresh installation, production-AppId upgrade from 0.9.0-preview, same-version reinstallation, uninstall/data retention, installed shortcuts/icons and clean-machine runtime independence are NOT EXECUTED in this pass. Static script checks confirm the unchanged per-user AppId, separate install/data folders and no UninstallDelete/media deletion. These checks do not prove lifecycle behavior.

A separate AppId on the host would not be genuine clean-machine validation. The real installation was not installed/upgraded/uninstalled. The prepared read-only Sandbox package contains original installers/ZIP and synthetic media only: artifacts/qa/final-hardening-20261013/ReleaseValidation.wsb and sandbox-input/INSTALLER_ACCEPTANCE.md. It is prepared, not executed. Use it only once a disposable Windows environment is available.

## Signing and security

Get-AuthenticodeSignature reports NotSigned for application EXE and installer. No certificate was created, imported, used or purchased; Windows security was not changed. Signing is not a local functional-test prerequisite, but unsigned public distribution must disclose the security/reputation limits.

An unsigned download can show an unknown publisher/SmartScreen warning; Smart App Control can block untrusted unsigned files. Signing identifies the publisher and enables reputation but does not guarantee immediate SmartScreen acceptance, including EV certificates. Source: [Microsoft SmartScreen developer guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation).

Options require an authorized verified identity and signing credentials: a publicly trusted CA-issued Authenticode code-signing certificate, or [Microsoft Artifact Signing](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation) (formerly Trusted Signing, subject to eligibility). Self-signed certificates do not establish public trust. Store distribution is a separate packaging/publication decision and is not introduced here.

With authorized signing material, sign the app/payload first with SHA256 and an RFC3161 SHA256 timestamp; compile the installer with Inno SignTool/SignedUninstaller support; verify installer and uninstaller signatures and chains. Repackage/re-hash after signing. References: [Microsoft SignTool](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool), [Inno SignTool](https://jrsoftware.org/ishelp/topic_setup_signtool.htm), [Inno signed uninstaller](https://jrsoftware.org/ishelp/topic_setup_signeduninstaller.htm). Signing credentials must stay outside source and artifacts.

## Unchanged candidate

- EXE: artifacts/release/0.10.0-rc.1/portable/DropSort.UI.exe
- Installer: artifacts/release/0.10.0-rc.1/DropSortSetup.exe
- ZIP: artifacts/release/0.10.0-rc.1/DropSort-0.10.0-rc.1-win-x64.zip
- Checksums: artifacts/release/0.10.0-rc.1/SHA256SUMS.txt
- Release notes: packaging/RELEASE_NOTES_0.10.0-rc.1.md
- Exact original source/payload provenance:artifact BUILD_MANIFEST.json. Hardening evidence:artifacts/qa/final-hardening-20261013. A documentation-only hardening checkpoint does not replace the candidate's original source identity.

Close existing DropSort instances before manual launch. For portable use extract the complete ZIP and run DropSort.UI.exe. Remove DROPSORT_TEST_DATA_ROOT from the launching shell to use the normal local user library. For installation follow the existing per-user Inno wizard; automated host installation is prohibited by this task. No tag, GitHub Release or binary upload is authorized. Broad Windows, real SMB/removable-drive and real-media playback acceptance limits remain open.
