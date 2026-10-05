# DirectY auto-create Y progress

User approved bounded in-chat design: select main Pipe, then terminal open branch Pipe; automatically calculate insertion on the selected main segment, create Y using Routing Preferences, rotate/elevation-adjust the bounded branch and connect directly; no near-Y elbow. Preserve existing fittings and fixed network ends; rollback the complete insertion/connection if any check fails. Native implementation. No new design/approval gate is required for this approved bounded flow.

Latest user refinement: rotate first at original elevation; only if that fails try elevation changes in **5 mm** increments (user corrected 10 mm to 5 mm), retry rotation at every height. Sequence 0,+5,-5,+10,-10,...; conservative default cap ±100 mm. Rotation-induced Z changes are geometric, not intentional elevation slides. Prevent hidden vertical inner-anchor translation during the zero-height pass.

Backup of installed build6E559480FAEC before edits: `backups/DirectY-before-auto-create-20261001` (DirectY source, Application.cs, ZIP, manifest).

## Work sequence

- [x] Pure insertion candidate geometry and regression tests (analytical one-swivel insertion; projected/bounded two-swivel candidates;45-degree branch orientations). Final169 assertions passing, including locked-elevation guards and off-grid roots.
- [x] Native Y insertion using BreakCurve + temporary branch Pipe + NewTeeFitting; delete only temporary stub; capture main/end topology; rollback-safe attempts in one transaction. Static implementation complete; native model smoke test remains unavailable.
- [x] Refactor existing DirectY apply into an open-transaction helper, integrate existing-Y-or-auto-create flow.
- [x] Validate all main ends, split Pipe type/DN/slope, Y direct adjacency, fixed neighbors, branch rigid shape; tests/build and independent review. Both paths revalidate after Transaction.Commit inside TransactionGroup before assimilating. Five Important review findings fixed; final reviewer found no blocking static issue. Native smoke test still required.
- [x] Update docs, package/install distinct hash DLL, verify old DLL and backup preserved.

Live Revit execution is unavailable. Report this explicitly; require model-copy smoke test after reopening Revit.

Current checks before final package: net48/Revit2024 build passes, 0 warnings/errors. Other suites: FitSpace32, PipeSpacing86, AutoTag219 assertions passing. Independent read-only reviewer `directy_auto_y_review` completed two passes; no remaining Critical/Important static findings. Permanent regression fixtures now include off-grid 31°,31.234°,37° roll roots in addition to the reviewer's 30° case. Documentation and PLUMBING tooltip updated.

Native insertion first probes direct45 NewTeeFitting, then uses a perpendicular temporary stub + writable NEW Y Connector.Angle45/135 fallback. Never modify shared family/type parameters or existing fitting angles. Neutral midpoint and interior fallback probe positions; measured run takeouts tighten final center bounds. All temporary probes are rolled back. Position-derived roll search brackets/refines signed residual roots and tangent minima, then verifies the complete bounded solution before a native attempt; finite numerical search is not a global mathematical completeness proof.

## Handoff — implementation installed, native smoke test pending

- Final Revit2024 build: 0 warnings, 0 errors. Pure regression total506 assertions (DirectY169 + FitSpace32 + PipeSpacing86 + AutoTag219).
- Build/package/ZIP/installed DLL SHA256 all match: `BCD17B71EAC3B8DA297BB01AEFD7FFE39C9DD2D22CEFF2AE407625D0A484B596`.
- Add-in manifest points to `%LOCALAPPDATA%/TungMEP2/Revit2024/BCD17B71EAC3/net48/TungMEP2.dll`.
- Prior installed6E559480FAEC and v1 61F0E309ECA6 DLL hashes unchanged. Backup ZIP SHA256 unchanged: `0834D99831EF34DE7C154C3263BA81300C6E1A3AA4BD1B43D7EE76A27A5DDAC9`.
- Revit was open (PID47940, started2026-10-01 08:23 local). Do not close it automatically; user must save/reopen to load new DLL.
- No live/native model execution was performed. Release is ready for controlled smoke testing, NOT a claim of verified production behavior. User should test docs/DirectY-Revit2024-smoke-test.md on model copy and report failures/screenshots.
- Previous reset heartbeat was already paused after flexible upgrade. Do not resume automatically or redo completed implementation on a later heartbeat; next work should follow native test feedback.
