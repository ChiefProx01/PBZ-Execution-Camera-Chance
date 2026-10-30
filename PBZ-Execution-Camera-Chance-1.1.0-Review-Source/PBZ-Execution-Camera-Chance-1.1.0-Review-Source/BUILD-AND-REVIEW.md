# Source and build instructions: Execution Camera Chance 1.1.0

Nexus page: https://www.nexusmods.com/phantombladezero/mods/140

This directory provides the C# manager source, application manifest, Lua mod
source, build script, and original license/credits for the uploaded 1.1.0
standalone release. It is a review source package, not an end-user installer.

## File being reviewed

- Display name: PBZ Execution Camera Chance 1.1.0 Standalone
- Archive: PBZ-Execution-Camera-Chance-1.1.0-Standalone.zip
- Archive SHA256: 290AC7015BEB59FEFD491787930AE531711B51635DBABDF43666E1ED5EC3C810
- Manager source: manager/Manager.cs
- Runtime game logic: mod/PBZExecutionCameraChance/Scripts/
- Compiler and payload construction: tools/build_standalone.py

The outer archive contains a Windows EXE and documentation. The EXE embeds a
ZIP resource named PBZ.Payload and an XML resource named PBZ.Manifest. They
contain seven selected UE4SS runtime/config/license files and five mod files.
These embedded compressed resources are disclosed here for review. There is
no password, encryption, code obfuscator, or online dependency downloader.
The exact quarantine rule and any scanner detections have not been confirmed.

## Build prerequisites

- Windows x64 with .NET Framework 4.8 and its C# compiler:
  C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
- Python 3.10 or newer. Only its standard library is used; no pip packages.
- The original PBZ-compatible UE4SS dependency release listed below.
- The game itself is not needed to compile the manager.

## Build steps (PowerShell, from this directory)

1. Obtain phantombladezero_ue4ss_v3.0.1-1183-gcdbc0b66.zip from:
   https://github.com/KintaroModding/ue4ss/releases/tag/v3.0.1-1183-gcdbc0b66
   The original archive SHA256 is:
   5328F91D13BD93F080FAFAE9E7C07790AE838D65E94545831D6676C4C1870D4D

2. Extract that dependency into deps\ue4ss. This directory should directly
   contain dwmapi.dll and the ue4ss subdirectory. For example:

   ```powershell
   Expand-Archive -LiteralPath '.\phantombladezero_ue4ss_v3.0.1-1183-gcdbc0b66.zip' -DestinationPath '.\deps\ue4ss'
   ```

3. Build:

   ```powershell
   $env:PBZ_RUNTIME_SOURCE = (Resolve-Path -LiteralPath '.\deps\ue4ss').Path
   python .\tools\build_standalone.py
   ```

4. Output is written under dist\PBZ-Execution-Camera-Chance-1.1.0-Standalone,
   together with its ZIP and SHA256 file. The builder invokes the system C#
   compiler with /target:winexe, /platform:x64 and the disclosed resources.

The public build script changes only the default dependency input directory
to a relative path; its payload/compilation behavior is unchanged. A rebuild
is not claimed to be byte-identical: compiler timestamps, environment and ZIP
file metadata can differ. Payload content hashes can be compared using the
embedded XML manifest.

## Third-party provenance

UE4SS upstream source:
https://github.com/UE4SS-RE/RE-UE4SS

PBZ dependency package/configuration:
https://github.com/KintaroModding/ue4ss/releases/tag/v3.0.1-1183-gcdbc0b66

That release identifies the official unchanged UE4SS binary build as
v3.0.1-1183-gcdbc0b66, commit cdbc0b66. The source build checks these identities:

- UE4SS.dll SHA256: C273E7228687B647226FD63D7CA65888D313ECE6BE18996EC1320D00A5EFE1F8
- MemberVariableLayout.ini SHA256: 8C5017D84C4553632B2FFBA320FF62D3998B273640E302A28B60D4BE98703FD0

The binaries and signature/layout files are not modified. ConsoleEnabled and
GuiConsoleEnabled are set to zero in the packaged settings. The original
sample/gameplay/HUD/diagnostic mods are not included in the standalone payload.
Original third-party notices are retained. This source corresponds to the
original MIT-licensed 1.1.0 release; it does not change existing release rights.

## Behavior relevant to safety review

- The manager installs a UE4SS proxy/runtime and this Lua mod into the selected
  game's platform\steam directory. The proxy is used to load UE4SS for modding.
- The manager has no network code, auto-updater, account access, or telemetry.
  Its UI reads Steam installation registry values and library metadata only.
- No services, startup entries, scheduled tasks, registry settings or AppData
  configuration are created by the manager.
- The manager validates the game version, checks embedded file hashes, and
  rejects incompatible existing runtime binaries/layout/signature files.
- Installation uses fixed target paths, with checks against traversal and
  junctions/symlinks, and an in-memory transaction journal for rollback.
- Uninstall removes the dedicated PBZExecutionCameraChance directory and its
  mods.txt entries, including its config and code backups. An installer-owned
  runtime is removed only when its ownership, contents and lack of other mods
  pass conservative checks. Existing/shared runtimes are retained.
- The mod hooks UE4SS game APIs for execution timelines, changes conditions on
  instance-owned camera actions, and leaves unknown structures unchanged.
- Probability defaults to 15%, supports 0-100%, and recognized bosses keep
  their original behavior. The game save files and original asset archives
  are not modified by this manager or Lua mod.

These descriptions explain intended implementation behavior, not an antivirus
certification or a claim that the site's quarantine decision is a false positive.
Please review the sources, upstream runtime and scanner results independently.
