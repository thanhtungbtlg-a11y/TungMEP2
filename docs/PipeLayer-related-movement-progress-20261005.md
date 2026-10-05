# PipeLayer related movement — 2026-10-05

## Approved change

User asked to fix the two audit findings and the native screenshot rollback: `Ống ngoài vùng chọn 43077848 bị Revit kéo theo`. In-chat bounded design was presented and explicitly approved with `oke`:

- Fixed reference; selected Pipes retain bounded BOP/COP/TOP alignment and rigid Z-only geometry.
- Allow Revit to adjust related unselected Pipes/fittings/accessories, including unselected Pipe length/slope, to keep connections.
- Capture hosted/dependent families even without piping ports; protect locked elements and existing host relations.
- Preserve identity/type/DN/System Type/physical topology and reject new gaps/axis worsening/deletion. Keep rollback for genuinely unsafe outcomes.
- Give the actual reference Pipe read error and ID; report collateral related changes on success.

No new command or ribbon button. Existing **TungMEP2 → PLUMBING → PipeLayer** only.

## Backup / preserved state

`backups/PipeLayer-before-related-movement-20261005` includes pre-change Runner/Safety/ModeDialog/Application, smoke guide, active manifest, installed DLL and complete package ZIP. No Git checkout is available; backup comparisons are the review baseline.

- Installed baseline: `C42F100255D4-899CFE674CE1`.
- Main DLL SHA256: `C42F100255D499BA4DEE9ABD7E770E8FC4ED9182108FE960F348D83AB34D2A8A`.
- Backup ZIP SHA256: `1D85AC7E0492B8D42C01F245DBBECCAC2FFA67CA144EF1B730528B15E7F2B932`.
- PrintSheets dependency SHA256: `899CFE674CE15794AB28CD28BDB3D3B41AD9477652F1A7A91C976D60EEC768B7`; preserve this dependency and unrelated source work.

## Implementation / evidence so far

- Reference preflight now distinguishes a missing click from a failed Pipe read and includes the actual read reason/ID.
- Connected unselected unlocked Pipe geometry/slope may change; selected/reference/locked Pipe geometry remains checked. Fitting/PipeAccessory may follow; standalone equipment remains fixed.
- Hosted family index uses a filtered FamilyInstance collector and Host/HostFace/SuperComponent; per-element dependent-family traversal captures relations not reported by Host. Relevant FamilyInstance ancestors are also enrolled, including no-port standalone equipment. Identity, ordered host roles/parent IDs and dependent membership are checked at both validation passes. Locked family placement and connector location/direction stay fixed.
- Success reports related changed Pipe/family counts and up to 8 IDs, calculated after post-commit validation while the group is still rollback-capable.
- New `PipeLayerSafetyConsoleTest` links the real Runner/Safety/Math/connector service/snapshot code. The Revit boundary is a test-only double because native element construction and fitting solver are unavailable outside Revit. It supplies observations, not a solver; no claim of native UI/Undo/rollback proof.
- RED: unselected translation rejected with the same screenshot reason; pinned hosted family missed by capture; reference OD error replaced by a generic message. Each failed against the old production code for the intended reason. Related-change reporting also failed before its implementation.
- Independent read-only review found missing ancestor enrollment and unknown test-selector acceptance. Ancestor movement and same-parent host-role replacement failed in RED before fixes; unknown selector previously returned 0 assertions/exit 0 and now exits nonzero. Ancestors do not become movable solely through that relationship. Face-reference stable strings are not blindly frozen without native evidence of reindexing behavior.
- GREEN runner/safety checks include physical-first host ordering, no-port change reporting, hosted Pin/Group/ownership, ancestor equipment and host-role invariants. Fresh net48/x64 rebuild: 0 warnings, 0 errors. One local C# name-shadowing compile error in the report method was corrected before GREEN; no native model was touched.
- Remaining native limits: host-face identity within one host/link, arbitrary parametric/line-location family deformation at unchanged transform, cross-domain connector topology, native solver/rollback/worksharing/Undo. These are not proven by the test boundary; documented in the smoke guide.

## Delivered / verified

- Independent read-only review and scoped re-review: no remaining concrete source-level blocker; native limitations explicitly retained.
- Fresh isolated rebuild/run of 10 console suites: 3,060 assertions passed (DirectY geometry 1,100; inventory 11; failure policy 17; FitSpace math 32; PipeSpacing math 86; AutoTagPipe collision 219; PipeLayer math 1,503; dialog 10; runner/safety 67; elbow-slope math 15). Unknown/multiple safety-test selectors exit nonzero.
- Fresh net48/x64 production rebuild and package build: 0 warnings, 0 errors.
- Fresh Windows PowerShell checks passed: FitRotate, MEP Alignment, PipeSpacing, FitSpace, hash-versioned installer and PrintSheets 2026.10.03.1 package/ribbon dispatch.
- Installed existing command to `%LOCALAPPDATA%\TungMEP2\Revit2024\B027EED99725-899CFE674CE1\net48`; active `%APPDATA%\Autodesk\Revit\Addins\2024\TungMEP2.addin` targets that DLL. No new button or separate add-in.
- Main SHA256 across build, package directory, ZIP entry and installed file: `B027EED9972513B3006723A34DD6848993E7BAB02705FAC8D05AFEB67E28A20E`.
- ZIP SHA256: `D56E0842BD0109C100D2599AC2753F8B67498D629178125458FA794766757A08`; updated smoke guide included.
- PrintSheets dependency matches baseline in lib, package, ZIP and installed directory. Old installed DLL and backup ZIP hashes remain unchanged.

## Native verification remaining

Revit PID 21584 stayed open during work; no native model/UI/restart was performed. Save, fully close and reopen Revit 2024 to load the new DLL, then run the same PipeLayer case on a model copy. Native solver/host propagation/Undo/worksharing and the documented family snapshot limits remain unverified. A safe rejection still rolls back; outside-selection Pipe movement alone is no longer a rejection.
