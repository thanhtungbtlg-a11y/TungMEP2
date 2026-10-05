# DirectY flexible upgrade progress

User authorized continued implementation, including automatic resumption after quota reset. Heartbeat id: `ti-p-t-c-n-ng-c-p-directy-sau-reset`, daily 00:12 Asia/Saigon; pause it when work is complete. Current quota reset reported 2026-10-01 00:11:09 Asia/Saigon. Do not repeat completed work.

## Requirements

1. Allow elevation adjustment combined with rotation so branch can align with Y.
2. Trace terminal branch through reducers/unions and intermediate Pipes to elbow; do not require selected Pipe immediately connected to elbow.
3. First pick main Pipe on run containing Y, then branch Pipe; automatically resolve the appropriate open Y branch port.

## Current state

Previous installed build/package hash: 61F0E309ECA6. Backup of previous source/package: `backups/DirectY-before-flexible-20260930`. Revit process 30796 is running; install new build in a different hash directory, never overwrite its loaded DLL or close the user's Revit.

Resumed automatically 2026-10-01 00:13 Asia/Saigon. Upgrade source is now integrated:

- `DirectYTopology.cs`: bounded source-tail traversal through 2-port inline fitting/Pipe to elbow and anchor; selected main Pipe collinear-run traversal to existing open Y, DN match, nearest source port, ambiguity rejection.
- `DirectYArticulation.cs`: two-joint analytical orientation and 3-length solve; handles rank-2 shared adjustment and free outer angle determined by target position; finite scalar and positive-length guards.
- `DirectYRunner.cs`: main Pipe first, source Pipe second, generic local IDs/connector snapshots, single-joint preferred, two-joint fallback, bounded disconnect/rotate/slide/resize/reconnect, postchecks, rollback.
- Source and release README and ribbon tooltip updated.
- Pure math/guard tests currently pass 110 assertions. RED→GREEN verified for nonfinite lengths, rank2 shared limits, free-outer-angle position. New nonzero ±two-joint rotation/offset/shortening tests pass.
- Main Revit 2024 build succeeded with 0 warnings / 0 errors after runner integration.
- Independent read-only final review completed by `/root/directy_flexible_final_review`: three Important findings, no Critical or Minor findings. All three fixed in one pass, with executable mutation RED→GREEN proof: unsupported closer tee shadows Y; incomplete main-run search; fitting/intermediate-Pipe shape preservation. `DirectYGuards.cs` provides pure target eligibility/ranking and rigid connector-pair signatures; integrated into topology and runner.
- Regression suites green: DirectY 110 + FitSpace32 + PipeSpacing86 + AutoTagPipe219 =447 assertions; all test builds and main build 0 warnings/errors. Re-run full suite after final fixes before install.

## Implementation approach

- Generalize terminal-tail discovery through straight Pipe and 2-port inline PipeFitting (transition/union), stopping at elbow E2.
- For target main Pipe, walk only collinear Pipe/inline run connectors and find eligible Y with unique open branch; choose candidate nearest source open connector, reject near-tie.
- If anchor Pipe M farther end connects through upstream elbow E1 to Pipe U, use two rotations: whole {E1,M,E2,tail} around U axis, then {E2,tail} around transformed M axis. Slide along U (elevation when U vertical), slide along M, extend terminal source Pipe. Preserve fitting geometry and far U endpoint. Solve full 3D directions/positions with pure math, validate lengths and exact connector bindings.
- Retain original single-joint solver as fallback if second articulation unavailable and original geometry is reachable.
- Use one transaction, disconnect only U–E1 and M–E2 boundaries during transforms, reconnect exact keyed ports, rollback failures. Snapshot fixed Y and external boundary connector frames/size, validate local types/connectivity.

## Next steps

Implementation, verification, packaging and installation are complete. Do not rerun this upgrade automatically.

- Final full suite: DirectY110, FitSpace32, PipeSpacing86, AutoTagPipe219 (447 assertions), all passed.
- Final `Build-TungMEP2.ps1`: build/package succeeded, 0 warnings / 0 errors.
- Build, package, ZIP entry and installed DLL SHA256 all match: `6E559480FAEC805C30BC8630F96B4E9D91B623A59DBFAE4A7A64C2384B21EA25`.
- Installed: `%LOCALAPPDATA%/TungMEP2/Revit2024/6E559480FAEC/net48/TungMEP2.dll`; addin manifest points to this build.
- Old loaded DLL SHA256 remains `61F0E309ECA68EBE1D5E57884AB649E4F47163AD6C7BBB51AA3CE67AE35ECF66`; Revit process30796 remains running and was not closed or modified.
- Prior ZIP/source/manifest retained in `backups/DirectY-before-flexible-20260930`.
- Heartbeat `ti-p-t-c-n-ng-c-p-directy-sau-reset` updated to PAUSED and status verified in automation.toml.
- No live Revit model/API execution has been tested. User must save/reopen Revit and smoke-test a model copy using `docs/DirectY-Revit2024-smoke-test.md`.

## Decisions

Ruling: prefer the original one-joint solution when already feasible, rather than moving upstream unconditionally — minimizes edited geometry — cost if wrong: does not offer user-controlled elevation adjustment if the one-joint result already connects.

Ruling: only use the second joint when the anchor far end directly reaches an elbow and then a straight upstream Pipe — preserves a clear fixed boundary; the selected terminal tail is flexible through reducers/unions — cost if wrong: more complex upstream chains remain unsupported and fail safely.

Ruling: retain existing-Y-only mode and reject perpendicular tee branches — this upgrade extends the confirmed DirectY workflow, not creation/routing of a new tee — cost if wrong: user needs another connection tool for a bare main Pipe or 90-degree tee.

Ruling: repository is not Git-managed; retain source/package backups and this ledger, do not create a worktree or delete the progress ledger — cost if wrong: rollback is file-based rather than Git-based.

Final review declined to judge: live Revit transform propagation/failure processing/connector-ID stability — no live executor/model access — requires model-copy smoke test. Packaging/install/docs are verified by the parent, not the reviewer.
