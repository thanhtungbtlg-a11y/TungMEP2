# PipeLayer connection policies — installed 2026-10-05

## Approved bounded change

The user accepted disconnected outcomes and approved two separate choices:

- Keep connections (default): existing topology and connector-offset/axis checks remain mandatory.
- Prioritize alignment, allow open ends: tolerate loss of existing piping edges and binding geometry changes. Do not proactively disconnect or reconnect; reject new/replaced joins.

Both choices preserve the reference endpoints, selected-Pipe target datum/station/original axis/slope, element identity/type/diameter/system type, locked elements and host relations. Existing End-port-only network support limits remain. No guarantee that native Revit can solve every constrained network.

## Implementation

- `PipeLayerModeDialog`: native two-choice dialog, default strict, safe cancel before mutation.
- `PipeLayerRunner`: carry explicit choice through both validation passes and failure policy; report final connection issues, show eight when a full log exists or all if log writing fails.
- `PipeLayerSafety`: allow a subset of original peers only; skip binding geometric rejection only for explicit allow-open. Keep other structural/geometric checks.
- Revit warnings in allow-open are not deleted/silenced; native modal handling remains. Errors/corruption request rollback. Strict still rejects warnings.
- UTF-8 diagnostic logs record actual pair IDs, connectivity, before/after gap in mm, axis deviation in degrees and offset-vector change. Success logs follow successful group assimilation. For native rollback, cached snapshots are labelled before commit/group, not claimed to show discarded solver-intermediate geometry. Log failure does not alter the transaction decision.
- Application tooltip and native smoke checklist updated. No other product source changed.

## Evidence

- Red: actual linked Runner rejected the explicit allow-open fixture on connector loss.
- Red: missing persisted report path rejected the new report test.
- Red: native group rollback erased connector evidence until the final pre-group cache was retained.
- Ten freshly built console projects passed: Auto90 498; AutoTag collision 219; DirectY failure 17, geometry 1100, model inventory 11; FitSpace 32; PipeLayer dialog 20, math 1516, runner/safety 148; PipeSpacing 86. Total 3647 assertions.
- Fresh PowerShell checks passed: FitRotate, MepAlignment, PipeSpacing, FitSpace, Auto90 metadata and PrintSheets20261003 package/ribbon dispatch.
- Product build/package: zero warnings/errors.
- Independent read-only code review: no remaining Critical/Important finding. Added final-commit-only disconnect + failed-assimilation coverage and snapshot label checks for its last Minor suggestion.
- All 38 installed payload files matched release. ZIP main DLL matched build and installed main hash.

These checks exercise application policy/branching with real production code and a narrow native boundary double. They do not certify Revit fitting solving, modal UI, rollback/Undo, worksharing or hosting behavior. Native model-copy smoke testing remains outstanding. Log IO-denial fallback inspected, not runtime simulated.

## Installed result and restore

Revit was confirmed closed before running the existing installer.

- Installed main: `%LOCALAPPDATA%\TungMEP2\Revit2024\BE51A7DBBC22-899CFE674CE1\net48\TungMEP2.dll`.
- Main SHA256: `BE51A7DBBC2288EF932E181C5261DD7069EE7F5A298DAC7FA3EB79BBDFE74C1A`.
- Manifest: `%APPDATA%\Autodesk\Revit\Addins\2024\TungMEP2.addin`.
- PrintSheets 2026.10.03.1 unchanged, SHA256 `899CFE674CE15794AB28CD28BDB3D3B41AD9477652F1A7A91C976D60EEC768B7`.
- Backup: `backups\PipeLayer-connection-modes-20261005-111839`, original main SHA256 `B8290B12CBAA7F97AA24E238C79CCA6F83966CE76E72EE3CF8A7C54B71717688`. Contains source/tools/release, old ZIP, original manifest and complete installed payload. Old hash installations were not overwritten.

To restore: fully close Revit; extract the backup ZIP into a separate directory, run its `Install-TungMEP2.cmd`, reopen Revit. For new testing: reopen Revit 2024, use PipeLayer on a saved model copy, select BOP/COP/TOP then the desired connection choice, inspect reported ports and test Undo.
