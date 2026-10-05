# Auto90: actual Tee clearance — installed 2026-10-05

## Scope

- Only Auto90 switches from the provisional 4D rejection to safe temporary Pipe lengths and measured native Tee takeouts.
- Adaptive probes include the measured midpoint and boundaries, with at most 20 positions per direction. Numerical equality does not discard a narrow valid interval or reject an exact-fit interval after unit conversion.
- Main outer geometry, native minimum Pipe length, connection/topology checks and full rollback remain enforced.
- DirectY45 behavior, PipeLayer two policies and PrintSheets 2026.10.03.1 remain unchanged.

## Verification performed

- Product build: 0 errors, 0 warnings.
- Ten console regression projects: 3,678 assertions passed, including 529 Auto90 assertions.
- PowerShell checks passed: FitRotate, MepAlignment, PipeSpacing, FitSpace, Auto90Package (metadata only), PrintSheets20261003.
- Source comparison against the pre-change backup: only DirectYAutoYRunner.cs and DirectYInsertionGeometry.cs changed, excluding generated build files.
- Revit process count immediately before installation: 0.
- Installed payload: all 38 files match the release payload.
- Main DLL in build, release, installation and ZIP: SHA256 `5FB7C91FD0B348718D2F9125651C93094E80D9DCD52EFAB355DDB75047FA7A20`.
- PrintSheets dependency unchanged: SHA256 `899CFE674CE15794AB28CD28BDB3D3B41AD9477652F1A7A91C976D60EEC768B7`.

Installed assembly:
`C:\Users\tungnt2303\AppData\Local\TungMEP2\Revit2024\5FB7C91FD0B3-899CFE674CE1\net48\TungMEP2.dll`

## Native verification still required

The checks above do not execute fitting creation inside Revit. Open Revit 2024 and test Auto90 on a copy of the affected model using `Auto90-Revit2024-smoke-test.md`. A short run can still fail if the actual family does not fit or native creation fails; the tool must roll back and report the measured requirement or that no valid measurement was obtained.

## Restore

Pre-change package:
`C:\Users\tungnt2303\Downloads\tungmep2\backups\Auto90-actual-tee-clearance-20261005-134117\TungMEP2-Revit2024.zip`

The old installed directory `BE51A7DBBC22-899CFE674CE1` remains intact. Its main SHA256 matches the saved installed payload: `BE51A7DBBC2288EF932E181C5261DD7069EE7F5A298DAC7FA3EB79BBDFE74C1A`.

To restore, close Revit, extract the backup ZIP into a separate folder and run its `Install-TungMEP2.cmd`. Restart Revit afterward.
