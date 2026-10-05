# DirectY noncoincident preserved binding: diagnosis handoff

## Scope

Continues authorized DirectY work in TungMEP2 PLUMBING. User supplied native screenshot/log, then requested comparison with 1E1Y and other tools. This patch adds measurements only; it does NOT claim to fix the unknown bad binding and does not relax geometry/topology/rollback safeguards.

## Native evidence

- Log C:/Users/tungnt2303/AppData/Local/TungMEP2/Logs/DirectY-20261001-043643-686-2c3c5de2.log: 578 lines, 14 prototype orientations, 22 direct-angle rejections recovered by perpendicular fitting creation, 22 accepted temporary cleanup operations.
- Exactly eight native connection attempts: +40 through +75 mm in 5mm increments. The +/-100mm message describes the search envelope, not 41 actual native attempts; pure geometry filtering selected these eight.
- Every attempt fails DirectYRunner.TryCreatePlan preserved-binding preflight because current connected-port origin separation exceeds 0.5mm. TryApply/branch rotation/source-to-Y connection are never reached.
- Inserted Y-main/split connections passed main-run validation immediately beforehand. Failure is narrowed to a branch-side preserved binding, but the old log lacks exact pair ID and baseline measurements. Cannot distinguish a pre-existing gap from an insertion/regeneration side effect.

## Comparison with working tools

- 1E1Y creates a new connecting Pipe/elbow and then a Tee/Y, rather than rotating and preserving an existing fitting cluster. CreateElbowDirect adjusts open ends to a virtual intersection and lets native NewElbowFitting create takeouts.
- TeeMasterDirect uses a perpendicular temporary Pipe/native NewTeeFitting strategy already present in DirectY. Writable family-angle search differs from Connector.Angle; could be reused for a demonstrated family-angle failure, not this preflight failure.
- 1E1Y connects the branch and nudges Tee +/-0.0001ft. Its command discards some exceptions and can assimilate an earlier Pipe/elbow transaction after Tee failure. No equivalent all-old-binding geometry audit: absence of its error dialog is not proof of an equivalent valid result.
- FitRotate's external boundary guard is tighter (0.0001ft vs0.5mm) but it does not scan internal preserved pairs. DirectY already uses the same boundary disconnect/rotate/regenerate/reacquire/reconnect pattern. Align3D moves the selected element, without connecting or auditing old bindings.
- Do not copy silent catch blocks, unconditional deletion, tiny nudges or remove guards as a guessed fix. Need the exact native pair and original-versus-post-insertion measurements first.

## Instrumentation and tests

- Before native transaction/probes, log the branch tail/anchor and optional outer/upstream/fixed-far connected bindings. At final plan preflight, log each checked binding and mark the exact invalid pair. Guard message includes both owner:connector identities.
- Reacquire by owner UID/connector ID. Log origins, axes, forward/reverse connection state, gap/tolerance in mm, snapshot gap and owner category/type. Six-decimal mm precision avoids hiding barely-over-tolerance gaps.
- Supplemental initial key/peer reads are best effort: ordinary metadata errors only log unavailable; RegenerationFailedException is never downgraded. Pure Observe callback tests check this transport contract, not native API execution.
- TDD: missing diagnostic payload RED, formatting GREEN210; precision RED, observation no-op RED, final GREEN216 geometry/diagnostic assertions. Inventory11 and failure policy17 pass. Isolated direct-DLL regressions: FitSpace32, PipeSpacing86, AutoTag219. Total581 assertions; no native model integration tests.
- Initial naive dotnet run for the three old regression projects reused shared obj/bin artifacts (wrong PipeSpacing output for other projects). Rebuilt each with its own absolute intermediate/output directory and executed the exact DLL; correct named suites passed. No unrelated project edits.
- Build Revit2024/net48 passed zero warnings/errors. Independent read-only review found no remaining Critical/Important issues after wrapping supplemental key/peer reads in Observe. Reviewer did not rerun tests or native model behavior.

## Backup and next action

- backups/DirectY-before-binding-diagnostics-20261001 preserves source, installed manifest, prior ZIP and native failurelog. Prior ZIP SHA256207ED2CBBFEFA64DD78C627DB9004EC077727B771DA6B504BED18D5C23F9CBD9.
- Keep old B87BAF81B07A DLL and all earlier versions intact. Install only into an absent hash-versioned directory.
- User must reopen Revit2024 and retry on a saved model copy. Compare input/preflight records using unordered owner:port pair (outer/upstream can reverse order). snapshotGapMm at preflight is the plan-stage capture, NOT the initial branch baseline. No corrective geometry change is justified until this native evidence is obtained.

## Installed diagnostic build

- Installed to the previously absent C:/Users/tungnt2303/AppData/Local/TungMEP2/Revit2024/5D3DFC109734/net48/TungMEP2.dll using the normal hash-versioned installer. Manifest now points to that exact DLL.
- SHA2565D3DFC10973443475056CBD863DED7C727D70B6A3BFDBD4576D27563D3A39D26 matches build/release/ZIP-entry/installed DLL. Prior B87BAF81B07A DLL and backup ZIP207ED2CBBFEF verified unchanged.
- Fresh final runs: geometry/diagnostics216, inventory11, failure17; isolated additional regression builds/runs32/86/219. No live Revit execution is available to Codex, and no actual connection fix is claimed.
- Await the user native retry/log. The next work should identify the exact bad branch binding and initial/post-insertion difference, then apply a tested correction at the demonstrated cause rather than copying 1E1Y's silent error handling or relaxing safeguards.
