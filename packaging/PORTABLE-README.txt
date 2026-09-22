Long Grid / Long方格 Developer Preview
=======================================

This archive is an internal, unsigned, portable Developer Preview for Windows 11 x64.
It is not an MSIX installer, is not a Stable release, and must not be treated as a
signed or broadly distributable product package.

Before launching:

1. Keep the complete extracted folder together.
2. Run this command from PowerShell:

   powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-Preflight.ps1

3. Only after the preflight reports "outcome": "Pass", launch LongGrid.App.exe.

The package must include .NET for the app and both workers, and Windows App SDK
for the app. The preflight checks payload structure, not successful user operation.
Desktop boxes are enabled by product defaults, subject to saved settings and safety
policies; this is not a claim that physical user acceptance has passed. This ZIP
does not install the Explorer context-menu registration or a service/driver.
License selection, code signing, MSIX installation, upgrade,
uninstall, multi-user behavior, SmartScreen reputation, and public distribution
remain release blockers and require separate approval and evidence.
