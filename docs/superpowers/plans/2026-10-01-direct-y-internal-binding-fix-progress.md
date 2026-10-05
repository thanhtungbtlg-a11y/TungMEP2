# DirectY original internal connector offset preservation

## Authorized scope and evidence

User explicitly requested the connection fix in TungMEP2 PLUMBING. Do not modify TungMEP or auto-heal the existing family/model.

Native log `C:/Users/tungnt2303/AppData/Local/TungMEP2/Logs/DirectY-20261001-063104-590-53720bdb.log` proves Pipe42978276:0 ↔ PVC NoiGiam reducer42978759:2 already has gap2.101722mm, reciprocal connection and axisDot−1 before any native transaction. The other reducer port ↔ Pipe42978749:1 has the same magnitude and opposite offset. First pair origins are unchanged at eight post-insertion preflight attempts (+40…+75mm). Strict coincidence check rejected before branch rotation/apply. This evidence proves a pre-existing offset, not why the family uses it; no claim of legitimate eccentricity or corrupt family is made.

## Implemented invariant

- Immutable `DirectYInnerClusterBaseline` captures exact InnerIds membership, element UID/type, physical port-ID sets, port position/frame/profile/size, sourceOpen identity and original internal adjacency. Capture occurs before AutoY transaction/probes, and once before the existing-Y height loop. Missing baseline is rejected; capture inside an open transaction is rejected.
- Original anchor/outer elbow/upstream/fixed neighbor ports and topology are also frozen for pre-motion validation. These context elements are NEVER granted an offset exemption.
- Validate baseline during plan creation and immediately before mutation. Native insertion must not change original branch geometry/topology. Original binding identity is unordered ownerUID/connectorID; post-motion comparison uses the original signed order.
- Permit noncoincident bindings ONLY if they were original internal tail bindings, both owners are original InnerIds, original/current unchanged, reciprocal uniquely connected, round equal DN, opposed axes within5°, and original gap>0.5mm. No shared FitSpace tolerance or service change.
- Apply/postcommit validates every original inner port against exact outer rotation/slide followed by inner rotation/slide. Only sourceOpen position can extend/shorten; its axis/profile/DN and sourceNear remain protected. Fitting BasisX/Y are also checked against the expected rotation within0.5°; round Pipe transverse frames are not constrained after endpoint reconstruction. Preserve full signed 3D binding offset within0.1mm, so equal-length offsets with changed azimuth fail.
- Normal coincident internal connections, all boundaries, Y/main and new source/Y retain coincidence≤0.5mm. All old bindings and new source/Y require two-way unique physical adjacency. Existing rigid signatures supplement the exact motion check, not replace it.
- Rotation-first and 5mm height search≤±100mm, native family insertion/cleanup, system warning policy and owned rollback behavior are unchanged.

## Verification so far

TDD pure production helper: offset-policy RED, then transform-order RED, port-preservation RED, signed-offset RED; GREEN265 geometry assertions. Independent review identified an Important fitting-roll validation gap (collinear round fitting can roll without changing origins/BasisZ/offset). Fixed with transformed transverse-frame check for FamilyInstance ports only. Added frame regression: RED scaffold at valid-frame acceptance, then GREEN280 including wrong roll, second-axis drift, zero/NaN/tolerance and composed fitting frame rotations.

Fresh additional suites: inventory contract11, failure policy17, isolated FitSpace32, PipeSpacing86, AutoTag219. Total645 assertions after frame tests. Old projects are built with separate absolute obj/output directories and exact DLL execution to avoid their shared-cache bug.

Revit2024/net48 initial build passed0warnings/errors. No live Revit model execution is available; mathematical/contract tests and build do NOT confirm final native connection success.

## Backup / remaining steps

Backup `backups/DirectY-before-internal-binding-fix-20261001` was created before source changes and must not be overwritten. ZIP SHA256120E6C4BAE1E7FC59520A99A0CF5ECA90B414D5B24344E2D6767D3044F9D3952. Old installed diagnostic DLL5D3DFC109734 SHA2565D3DFC10973443475056CBD863DED7C727D70B6A3BFDBD4576D27563D3A39D26 verified intact.

Independent final review found no remaining Critical/Important issues after verifying the fitting-roll guard fix. Verdict: ready for controlled native validation, not native-certified. Reviewer did not rerun tests/build or execute Revit; parent ran fresh geometry280/inventory11/failure17/FitSpace32/PipeSpacing86/AutoTag219 (645 assertions) and full net48 Rebuild, all successful with0warnings/errors.

User must save/reopen Revit2024 and retry on a model copy. Keep prior heartbeat paused.

## Installed corrective build

- Safe package target resolved exactly to workspace `release/TungMEP2/net48`; workspace/release/package ancestors checked non-reparse before the package script's recursive removal. Smoke-test document copied into release before compression. Build/package successful0warnings/errors.
- Installed into previously absent `C:/Users/tungnt2303/AppData/Local/TungMEP2/Revit2024/FAC4E4C4965A/net48/TungMEP2.dll`. Installer manifest's old5D3DFC109734 target was verified before replacement, and new manifest points to this exact DLL.
- Build/release/ZIP-entry/installed SHA256 all match `FAC4E4C4965A69884D5F6978ADCD3EF3437BE78B2F1378B64484DA59B1F68636`. ZIP container SHA256 `FB0291A8D020BA5EECE1D25BE52948A472AE6562B930BAACDD81DF98F65B044B`.
- Prior installed5D3DFC109734 DLL and backup ZIP120E6C4BAE1E verified unchanged after install. No Revit process was closed and no user model was modified.
- This is a corrective preservation-policy patch, not merely diagnostics. Native model connection success is still awaiting the user's saved model-copy retry after reopening Revit. If it fails, inspect the new log for `connection/preserved-original-internal-offset` and later stage/port guard: do not globally loosen coincidence or recapture a mutated baseline.
- Implementation/review/build/package/install work in this turn is complete. Do not automatically rerun finished work; native trial is the next evidence needed.
