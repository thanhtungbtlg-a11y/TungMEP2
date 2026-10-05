# DirectY temporary Center Line cleanup classification

Latest continuation: 2026-10-01-direct-y-binding-diagnostics-progress.md. User native run confirmed Center Line cleanup success; next failure is a branch-side preserved-binding origin mismatch before rotation. Diagnostic build5D3DFC109734 installed; awaiting native pair/baseline evidence. Do not redo the Center Line repair.

User native screenshot reported cleanup/delete-probe-rejected ID43029047. This continues the approved temporary-only cleanup repair; no geometry/system-search redesign.

## Native evidence

- Real log DirectY-20261001-035038-516-acd73120.log contains70 rejected new generic Autodesk.Revit.DB.Element records, category Center Line, typeId-1. Last43029047 accompanies temporaryPipe43029046. No old/retainedID was reported as deleted.
- NewTeeFitting45 rejects connector angles; perpendicular fallback does create family PVC Tee. Cleanup classification blocks before the newY instance-angle adjustment. We cannot yet assert that the subsequent native geometry stage will succeed.
- Native log confirms metadata/cascade membership, NOT dependency ownership. Runtime GetDependentElements queries must prove the latter before any new whitelist accepts an element.

## Narrow repair

- Keep the rollback delete probe and exact actual/approved set equality.
- After confirmed probe rollback, reacquire roots and retained Y/main/split/source; query GetDependentElements(null) and record candidate dependency matches. Missing owner/query errors remain fail-closed.
- Accept only exact runtime Element, invalid typeID, OST_PipeCurvesCenterLine(-2008045) or OST_PipeFittingCenterLine(-2008072). Pair owner category: Pipe centerline to confirmed temporaryPipe, fitting centerline to confirmed temporaryTransition. Flex/Fabrication/unrecognized variants remain rejected.
- Require exactly one matching temporary root and absence from all four successfully queried retained-parent dependency sets. Existing or retained IDs never become disposable.
- This is not a global-exclusive-owner proof across every element in the document. Existing physical/model IDs are independently protected by baseline survival guards; the known retained topology is checked before/after cleanup and connection.
- Log now includes numeric categoryID plus matched temporary/retained ownerIDs. No localized-name or ID-adjacency classification.

## Verification

- Policy TDD RED: safe new centerline rejected by previous no-whitelist guard; GREEN198 assertions. Second RED: multiowner incorrectly accepted; GREEN200 with exactly-one-owner rule.
- Final regression: geometry/cleanup200, model inventory contract11, failure policy17, FitSpace32, PipeSpacing86, AutoTag219; total565 assertions, all passing. These are pure/contract regression tests, not native Revit integration tests.
- Revit2024/net48 build completed with zero warnings/errors. Build, release package and ZIP-entry DLL hashes match.
- Read-only independent final review found no Critical/Important issues. Renamed the policy test and assertion to describe exactly one matching temporary owner and no checked retained dependency, not document-wide exclusive ownership; rerun passed200 assertions.
- No live Revit model execution is available to Codex. Native ownership query and later connection need controlled model-copy smoke testing after reopening Revit.

## Backup

Before production edits: backups/DirectY-before-centerline-fix-20261001 contains source, installed manifest, releaseZIP and native failurelog. ZIP SHA256032E4191C5C307EB561A567FB471A20C88633FFF881E8CF8B9384D1527D1290A. Prior installed DLL9CE3D3A16B77 must be preserved, along with all earlier versions/backups.

## Installed handoff

- Installed through release/TungMEP2/Install-TungMEP2.ps1 into the previously absent hash-versioned folder: C:/Users/tungnt2303/AppData/Local/TungMEP2/Revit2024/B87BAF81B07A/net48/TungMEP2.dll.
- SHA256 B87BAF81B07A7CEF6AE2EDEB48DEEB420BC9F9C6D098758CE129E72BDA048A92 matches build, package, ZIP-entry and installed DLL. Manifest points to this exact new DLL.
- Old installed DLL9CE3D3A16B77 and backupZIP032E4191C5C3 were verified unchanged after installation. No loaded DLL was overwritten and Revit was not closed automatically.
- User must save/reopen Revit2024, then retry DirectY on a saved model copy. Runtime dependency membership/category recognition, deletion/rollback behavior and later angle/connection success are still unverified. A safe rejection should retain the model and produce the new numeric-category/owner diagnostics; do not broaden the whitelist without new evidence.
