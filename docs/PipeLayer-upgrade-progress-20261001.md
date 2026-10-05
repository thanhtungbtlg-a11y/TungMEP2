# PipeLayer upgrade handoff — 2026-10-01

## Approved scope

User approved upgrading **TungMEP2 → PLUMBING → PipeLayer**, not TungMEP:

- Selected straight horizontal/sloped Pipes may have different plan directions, including perpendicular.
- Preselect at least two Pipes; pick an included Pipe as fixed reference at the desired station; then choose BOP (default), COP, or TOP.
- Project the click to the bounded reference plan axis, then sample each candidate at the nearest bounded plan station to that reference point.
- BOP/COP/TOP datum = center Z − OD/2, center Z, center Z + OD/2; OD excludes insulation.
- Translate only Z; preserve selected Pipe XY, length, direction, slope, identity/type/diameter/system type. Reference shift is zero.
- Preserve connection topology and perform full rollback for unsafe movement, including solver changes at Commit.

Bounded in-chat design approval was completed before implementation. No architectural spec or implementation-plan document was needed.

## Changes

- `src/TungMEP2/Core/Internal/Ctn/PipeLayerRunner.cs`: bounded station planning, native BOP/COP/TOP choice, selected-Pipe preflight, residual Z movement for already-moved connected Pipes, transactional execution and pre/post-commit safety validation.
- `src/TungMEP2/Core/Internal/Ctn/PipeLayerMath.cs`: independent-of-Revit numeric geometry and validation policies.
- `src/TungMEP2/Core/Internal/Ctn/PipeLayerSafety.cs`: connected End-port network snapshots, fixed unselected Pipes/accessories/devices, identity/geometry/port/topology checks, baseline connector offset-vector preservation (0.05 mm) and axis worsening limit (0.1 degrees), failure preprocessor that rolls back posted failures.
- `src/TungMEP2/Core/Application.cs`: PipeLayer tooltip only; existing PLUMBING button/command retained.
- `tools/PipeLayerMathConsoleTest.cs` and `.csproj`: isolated output paths and direct production-math tests.
- `docs/PipeLayer-Revit2024-smoke-test.md`: native usage, scope/limitations, model-copy checks, restore instructions. Included in package ZIP.

No DirectY/FitSpace/PipeSpacing/AutoTagPipe product source changes. No user model edits. Revit was not closed or restarted.

## Safety boundary and native verification outstanding

Only End-port Pipe/FamilyInstance networks are accepted. Raw owner/peer connector inspection runs during capture and every validation pass. Curve/Physical/tap ports, FlexPipe/FabricationPart or other unsupported connected elements, and networks above 10,000 elements are rejected before mutation. Pipe curves and vertical selected Pipes do not have the required supported bounded plan station.

Unselected Pipe geometry and PipeAccessory/device transforms stay fixed. Only unpinned, ungrouped PipeFitting instances may auto-adjust under Revit while all constraints remain valid. The command does not reroute, replace fittings, disconnect/reconnect, or promise clearance/clash avoidance. A connected constrained layout may rollback; the user may need to select all Pipe segments intended to move.

Native Revit transaction/solver tests are **not yet performed**. User must save their model, fully close/reopen Revit 2024, then run the smoke matrix on a model copy. Revit PID 52944 was still open with start time 2026-10-01 14:20:18 local during installation; this session cannot reload a hash-versioned add-in automatically.

## Verification

- RED→GREEN: first perpendicular alignment case initially failed against the unimplemented numeric API; implemented bounded geometry passed.
- RED→GREEN: a new 1-degree connector-axis worsening regression initially passed the incorrect 5-degree-floor predicate (test failure), then passed after removing the floor.
- RED→GREEN: changed connector-offset direction at unchanged magnitude initially escaped the scalar-gap policy (test failure), then was rejected by full-vector preservation.
- Final PipeLayerMath: **1,503 assertions passed**.
- DirectY geometry: 1,100; model inventory contract: 11; failure policy: 17; FitSpaceMath: 32; PipeSpacingMath: 86; AutoTagPipeCollision: 219; freshly compiled ElbowRunSlopeMath: 15. **Total console assertions: 2,983 passed**.
- Fresh `Test-FitRotate.ps1`, `Test-MepAlignment.ps1`, `Test-PipeSpacing.ps1`, `Test-FitSpace.ps1`: all passed. Invoked with Windows PowerShell; no model mutations.
- Fresh full net48/x64 Rebuild and `Build-TungMEP2.ps1`: 0 warnings, 0 errors.
- Independent read-only code review initially found four Important safety gaps (axis floor, accessory movement, omitted tap ports, unprotected FlexPipe). All addressed; focused reviewer approved packaging with no remaining Critical/Important/Minor findings. Native behavior still unverified.

## Installed result

- Build ID: **C57E695A630B**.
- DLL SHA256: `C57E695A630BF08246883C7D9220DD3684770D122E4182224D7565F3C5C8BC5D`.
- ZIP SHA256: `CC7FA3C4AFC02A7ACC88E4C1BA303B0EE307FE3510108CF502ADF06B019339DD`.
- Manifest: `%APPDATA%/Autodesk/Revit/Addins/2024/TungMEP2.addin` points to `%LOCALAPPDATA%/TungMEP2/Revit2024/C57E695A630B/net48/TungMEP2.dll`.
- Build, release package, ZIP DLL entry and installed DLL hashes independently matched; smoke guide entry present in ZIP.
- Installer itself completed. The first wrapper incorrectly checked stale `$LASTEXITCODE` after the PowerShell-only installer and reported `Install failed`; no reinstall was performed. Independent read-only verification then confirmed correct manifest, files and hashes with exit 0. This was a wrapper exit-check error, not an installation error.

## Preserved backup

`backups/PipeLayer-before-multi-direction-20261001` contains original PipeLayerRunner, Application, add-in manifest and ZIP.

- Old DLL ID: **D22141BBFA22**.
- Old DLL SHA256: `D22141BBFA227D83A1FDFED5687D43EB4BCCF766DD1C6350279E10861C82EC9E`.
- Backup ZIP SHA256: `429497FE9F2C13ED76916608C04648372B9ED14F0A434D92F9F7DB3D5D4F2A7F`.
- Old installed DLL and backup ZIP verified unchanged after installation.

Do not repeat completed implementation/build/install on continuation. Next meaningful step is native user smoke feedback; investigate a specific new failure without resuming stale DirectY work or changing unrelated tools.
