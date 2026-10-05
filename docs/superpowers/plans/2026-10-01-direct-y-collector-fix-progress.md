# DirectY required native collector filter repair

Later native cleanup Center Line issue: see `2026-10-01-direct-y-centerline-fix-progress.md`. This document records the collector-only9CE3D3A16B77 handoff.

User reported the exact missing-filter exception from Revit2024 DirectY. This is a focused correction to the last installed build5F469080DEC3, not a new feature/design.

## Evidence / root cause

- Screenshot matches installed RevitAPI.xml: FilteredElementCollector.ToElementIds throws when no native filter has been applied.
- Two bare collectors were introduced in DirectYModelBaseline capture and ElementsSurvive (formerly embedded in DirectYTemporaryCleanup.cs).
- Baseline constructor executes before transactions open; latest real log contained only its header. Failure was before rotation/model modification.
- Independent read-only audit found no additional unfiltered collector in DirectY or immediate FitSpace callees.

## Repair

- Extract baseline class unchanged to DirectYModelBaseline.cs to compile/test the actual production inventory boundary independently.
- Replace both bare collectors with CaptureAllIds using separate WhereElementIsNotElementType and WhereElementIsElementType collectors, then union their IDs. Dispose both collectors. Keep managed-system merge exception and every original cleanup/topology check.
- Do not apply complementary filters sequentially on one collector (that would return an empty intersection), and do not narrow inventory to instances only (that would stop protecting old types).

## Tests / limitations

- A test-only Revit inventory boundary substitute follows the primaryAPI filter contract because no native Document can be instantiated outside Revit. It is not a native integration test.
- RED: actual production baseline class failed with the screenshot's missing-filter exception before repair.
- GREEN:11 contract assertions pass, covering full instance/type capture, unchanged inventory, missing old instance/type rejection, native managed-system merge allowance and immutable baseline IDs when new objects are added.
- Existing DirectY186 + failure policy17 + FitSpace32 + PipeSpacing86 + AutoTag219 pass; with inventory contract11, total551 assertions. Native net48/Revit2024 build0warnings/0errors.
- Native model smoke test still required after reopening Revit. No model or Revit process was manipulated by Codex.

## Backup

`backups/DirectY-before-collector-fix-20261001` contains original DirectY source, releaseZIP and manifest. BackupZIP SHA256 `F80105AB28AAE8B9CD9B937CD190A2F7BC29B1D9D4452D17C66CC3D7F606D738`.
Earlier backups and hash-versioned DLLs must remain intact.

## Installed handoff

- Final independent narrow review: no Critical/Important/Minor findings; full cleanup class body is unchanged from backup. Contract tests explicitly do not assert native integration.
- Build/package/ZIP-entry/installed DLL hash matches `9CE3D3A16B77116FD564032D04C19A9E619956E832E3D9AA329C197BB1B69842`.
- Add-in manifest points to `%LOCALAPPDATA%/TungMEP2/Revit2024/9CE3D3A16B77/net48/TungMEP2.dll`.
- Previously installed5F469080DEC3 DLL hash and the before-collector-fix backupZIP hash verified unchanged after installation.
- User must save/reopen Revit and rerun DirectY on a model copy to confirm the native path progresses beyond inventory. Do not automatically close Revit or manipulate the model.
