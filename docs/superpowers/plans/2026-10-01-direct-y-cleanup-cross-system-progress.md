# DirectY cleanup / cross-System-Type repair

Follow-up collector exception fixed and installed: see `2026-10-01-direct-y-collector-fix-progress.md` (latest build9CE3D3A16B77). The installed handoff below records the earlier5F469080DEC3 build for history.

User explicitly approved the four-item in-chat repair and comparison with 1E1Y. Scope is TungMEP2 PLUMBING DirectY; no changes to TungMEP or unrelated tools.

## Approved work

1. Identify actual cascaded IDs/classes through a delete-and-rollback probe with diagnostic logging.
2. Disconnect the temporary branch from Y before deleting; retain the direct Y-to-two-main-Pipe connections.
3. Replace the exactly-one-deleted-ID guard with classified, confirmed temporary-only deletion. Protect old model elements, source/main/split Pipes and retained Y. Actual deletion must match the approved probe set.
4. Stage-specific errors rather than attributing all failures to Routing Preferences.
5. Allow differing System Type using the source-side/native mechanism of 1E1Y, without forcing parameters on existing networks. Classification/flow-analysis warnings remain fatal. Preserve all geometric/topological checks and 0,+5,-5,...±100 mm rotate-first search.

## Backup and evidence

- Backup before any production edits: `backups/DirectY-before-cleanup-cross-system-20261001` (baseline DirectY source, Application, ZIP, add-in manifest).
- Baseline installed DLL SHA256: `BCD17B71EAC3B8DA297BB01AEFD7FFE39C9DD2D22CEFF2AE407625D0A484B596`.
- This source tree is not a Git repository. Edits made in place after immutable backup; no checkout/reset actions.
- Pure cleanup policy TDD: failing assertion on safe multi-ID cleanup with old strict guard, then 186 DirectY assertions passing.
- Isolated failure policy TDD: rejected the type-only warning before implementation, then 17 assertions passing.
- Native net48/Revit2024 compile 0 warnings/errors. Fresh regression suites: DirectY186 + failure policy17 + FitSpace32 + PipeSpacing86 + AutoTag219 = **540 assertions** passing.

## Implementation

- `DirectYTemporaryCleanup`: confirm bounded new Transition-to-stub branch, disconnect Y boundary, regenerate/reacquire; native rollback-delete probe; classify restored IDs; actual deletion equality; retained-model and Y/main adjacency validation.
- Allowed extra cascade objects: newly generated insulation hosted by confirmed temporary roots, and newly generated PipingSystem only if network/terminal/base-equipment/retained-connector checks prove it belongs only to temporary roots or is empty. Any existing ID or unknown object remains prohibited.
- Original managed MEPSystem records may be merged by native Revit connection; general survival checks exclude these managed records, but no existing record is approved for explicit cleanup deletion. All old non-system/model IDs must survive.
- `DirectYDiagnostics`: best-effort per-run log under LocalAppData/TungMEP2/Logs. Failed regeneration and unconfirmed rollback escape candidate retry loops.
- Removed both initial System Type equality and main postcondition equality. Pipe type, DN, level, axis/slope, far endpoints, neighbor connectors and branch rigid shape remain guarded.
- Native warning handler permits exactly MismatchedSystemTypeWarning at Warning severity. Everything else, including classification warnings, rolls back with the native reason.
- Owning transaction/group rollback statuses are now checked on both existing-Y and auto-Y paths. Pending/unconfirmed rollback stops retries and does not claim that the model is unchanged. Diagnostic native reads happen before Assimilate; ordinary logging failures do not affect the command.

## Live verification boundary

No live Revit model execution interface is available. Compilation and pure tests do not establish native correctness for the user's model. Release is for controlled model-copy smoke testing after the user saves/reopens Revit. Do not close Revit or manipulate model automatically. Previous reset heartbeat is paused; do not resume or duplicate this work.

## Installed handoff

- Build/package/ZIP-entry/installed DLL SHA256 match: `5F469080DEC372C34E820AF5EA3A5969224A0523268D99BE8CFA26886BB75B6B`.
- Manifest: `%APPDATA%/Autodesk/Revit/Addins/2024/TungMEP2.addin` points to `%LOCALAPPDATA%/TungMEP2/Revit2024/5F469080DEC3/net48/TungMEP2.dll`.
- Prior installed BCD17B71EAC3, 6E559480FAEC, 61F0E309ECA6 DLLs remain intact; before-cleanup backup ZIP SHA256 `FA751B99C6C321F32E5AEDD88337B267B0B15E9C8186764BC690C401BF11FCA8`, containing baseline BCD17 DLL.
- Revit open at last check: PID56440, started2026-10-01 09:25 local. User must save/reopen; no process was closed and no model was modified by this Codex run.
- Independent read-only reviewer reviewed current cleanup, warning policy, diagnostics and rollback changes; no remaining Critical/Important static blocker reported. Native smoke-test required. Non-blocking caveat: repeated full-document survival scans may be slow on large models; profile in Revit before optimizing the fail-closed guards.
