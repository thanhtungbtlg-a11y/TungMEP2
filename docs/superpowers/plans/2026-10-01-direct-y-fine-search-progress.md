# DirectY fine height and Y-roll search

## User-confirmed scope

User's Revit screenshots confirm prior FAC4E4C4965A corrective build created Y and connected successfully using an intentional elevation step40mm. User then requested smaller elevation increments0.5–1mm and finer angular search; explicitly chose0.5° instead of the proposed1°. Approved bounded implementation: 0.5mm elevation, 0.5° Y-roll discovery, retain rotation-first, full±100mm and all preservation/connection/rollback guards.

## Implementation and semantics

- DirectYRunner.HeightSteps now0.5/304.8ft to100/304.8ft: order0,+0.5,−0.5,+1,−1…±100mm,401 levels. Existing-Y and AutoY paths consume the same levels; all visible 5mm step text updated to0,5mm.
- ElevationSteps previously capped at100 increments, silently reducing .5mm's range to±50mm. Removed silent truncation. Finite increment count≤10000 is enumerated fully; overflowing/impractically large requests return no levels, rather than pretending to cover a shortened range.
- PositionRolls coarse divisions144→720 over−180°…+180°, discovery step2.5°→0.5°. Continuous bisection/golden refinement48 iterations retained. Elbow angles remain analytical acos/atan2; final roll/rotation is not quantized to multiples of0.5°. Axis acceptance tolerance2° is not an angular step and is unchanged.
- No change to immutable baseline, old internal offset exception, strict external/new-binding coincidence, fitting geometry/ID/type/port protection, native Y insertion/cleanup, system-warning handling or transaction rollback.

## TDD evidence

- Height range RED: requested401 levels, old cap returned201. Impractical enumeration RED: expected reject, old implementation returned201 truncated levels. GREEN after full finite bounded enumeration.
- Angular behavior RED using real production geometry: two45° bends have a narrow feasible directional window touching at Yroll0.5°, with 2.5° samples unable to form a finite residual bracket. Old grid found no matching roll. GREEN720 grid discovers and validates a zero-height connection; this is not a test of a constant or a mock.
- Full geometry1099 assertions initially pass, including401-level order/bounds, native feet conversion, zero-height narrow-roll solution and all prior preservation regressions. Independent review found no Critical/Important findings. Its Minor note correctly identified100/1e-300 as finite1e302; corrected the test label and added a true overflow case100/double.Epsilon. Final geometry count1100, plus inventory11/failure17/FitSpace32/PipeSpacing86/AutoTag219 =1465 assertions. Final rerun/package/install results follow below.

## Cost and verification boundary

401 heights×721 coarse samples versus41×145 previously is approximately49× worst-case coarse measurement count before refinement/native attempts. Actual time depends on early valid candidates, domain filters and native Revit behavior. User's chosen density is intentional; do not replace it with1° or1mm. Do not claim globally minimum height, all feasible angles discovered, or every point's Z unchanged. Native model retry/performance remains user-executed; no live Revit executor is available.

## Backup

`backups/DirectY-before-fine-search-20261001` contains previous DirectY sources/tests/smoke-test/installed manifest and prior ZIP. ZIP SHA256FB0291A8D020BA5EECE1D25BE52948A472AE6562B930BAACDD81DF98F65B044B. Prior installed DLL FAC4E4C4965A SHA256FAC4E4C4965A69884D5F6978ADCD3EF3437BE78B2F1378B64484DA59B1F68636. Preserve both and all older backups; install only into a previously absent hash-versioned folder after final verification. Keep heartbeat paused.

## Final verification and installed build

- Fresh final suites: geometry1100, inventory11, failure17, isolated FitSpace32, PipeSpacing86, AutoTag219 =1465 assertions, all successful. Regression projects use independent absolute obj/output paths and exact DLL execution to avoid shared-artifact reuse. Full Revit2024/net48 Rebuild and packaging both0warnings/errors.
- Independent review approved the bounded implementation with no Critical/Important findings. Minor overflow-test labeling/coverage corrected as above. Reviewer did not execute tests/build/Revit; parent executed all commands. Production hash comparison confirms only DirectYRunner.cs/DirectYAutoYRunner.cs/DirectYInsertionGeometry.cs changed; all other DirectY source files are byte-identical to the confirmed working backup.
- Package deletion target resolved exactly to `C:/Users/tungnt2303/Downloads/tungmep2/release/TungMEP2/net48`; workspace/release/package ancestors checked non-reparse before build script cleanup. Updated smoke-test included in ZIP.
- Installed into previously absent `C:/Users/tungnt2303/AppData/Local/TungMEP2/Revit2024/D22141BBFA22/net48/TungMEP2.dll`; manifest oldFAC4E4C4965A target verified before update, then new exact target verified.
- Build/release/ZIP-entry/installed DLL SHA256 all `D22141BBFA227D83A1FDFED5687D43EB4BCCF766DD1C6350279E10861C82EC9E`. ZIP container SHA256 `429497FE9F2C13ED76916608C04648372B9ED14F0A434D92F9F7DB3D5D4F2A7F`.
- Old FAC4E4C4965A DLL and backup ZIP FB0291A8D020 verified unchanged after install. Revit was not closed, no model edited by Codex, heartbeat remains untouched/paused.
- Implementation/review/build/package/install complete; next action is user save/reopen and same-model-copy native retry. Await evidence for achieved height and responsiveness; do not automatically rerun finished work or claim guaranteed improvement below40mm.
