Long Grid / Long方格 unsigned MSIX Developer Preview
====================================================

This package is an internal structure-validation artifact only.

- Package identity: Longyuyeee.LongGrid.DeveloperPreview
- Publisher placeholder: CN=LongGrid Development
- Architecture: x64
- Minimum OS: Windows 11 build 22000
- Capability: runFullTrust only
- Signed: no
- Public distribution approved: no

Windows will not treat an unsigned MSIX as a trusted installable package. Do not
disable system security policy to install it. Code signing must happen in a
protected release environment, and the signing certificate subject must exactly
match the manifest Publisher. License approval, SBOM, certificate custody,
SmartScreen reputation, installation, upgrade, downgrade, uninstall, rollback,
multi-user and enterprise-offline evidence remain mandatory release blockers.
