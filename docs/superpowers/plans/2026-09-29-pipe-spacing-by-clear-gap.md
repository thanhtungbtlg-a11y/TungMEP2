# PipeSpacing by Clear Gap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Revit 2024 `PipeSpacing` command to TungMEP2 that spaces selected parallel Pipe clusters by real outside-clearance while anchoring the larger outermost pipe and preserving bounded connected branches.

**Architecture:** Keep the spacing calculation and bounded-graph partition policy independent from Revit so they can be regression-tested in a console project. Revit-facing analysis converts selected Pipes into ordered clusters and a scalar target plan; the executor moves each rigid core, stretches only classified boundary Pipe segments, verifies connectors, and commits the whole operation as one Undo unit after optional visual preview.

**Tech Stack:** C# 12, .NET Framework 4.8, Autodesk Revit 2024 API, WPF code-built UI, System.Text.Json, PowerShell regression tests, .NET 8 console test harness.

**Spec:** `docs/superpowers/specs/2026-09-29-pipe-spacing-by-clear-gap-design.md`

## Global Constraints

- Target Revit 2024 and `net48`; do not introduce another runtime dependency.
- Support straight non-vertical `Autodesk.Revit.DB.Plumbing.Pipe` only in v1.
- Default mode is Equal Clear Gap with a 75 mm project preset; never label that number as TCVN.
- Use real OD, optionally plus insulation/fitting envelope; warn instead of silently replacing OD with DN.
- Anchor the larger effective-OD cluster among the two outermost clusters; deterministic lower/left tie-break.
- Stop traversal at Tee/Wye/Cross, multi-port accessories, equipment, fixtures, and other branch owners.
- Never move a stationary boundary fitting or propagate through it into the shared network.
- Preserve main Pipe Z/slope and internal connector topology; any unsafe boundary reconnect rolls back the Apply.
- One Apply must produce one Revit Undo item; Preview and Cancel must leave no model changes or overrides.
- Create a timestamped restorable backup before source edits, including source, build/package DLL, ZIP, installed add-in manifest, and `RESTORE.txt`.
- This workspace currently has no `.git`; task-end “commit” steps therefore create named backup checkpoints. If execution moves to a Git clone, replace each checkpoint with the listed commit message.

## Review Focus

- A selected Pipe directly touching a fixed Tee/Wye has no stretchable transition segment: mark the cluster `Constrained`, do not distort the selected run (Task 2 graph-policy test).
- Two selections resolving to one cyclic/shared connector graph must merge once or be rejected as overlapping, never be moved twice (Task 2 traversal and Task 3 overlap tests).
- A near-vertical or angularly mixed set must fail analysis before opening a model transaction (Task 3 geometry tests).
- Missing OD, deleted elements, pin/group/worksharing ownership changes between preview and Apply must abort safely with no partial move (Task 3 OD test and Task 5 executor contract test).
- Corrupt/old settings JSON and incomplete DN bands must normalize to versioned defaults/fallback gap without crashing (Task 4 config tests).

---

## File Structure

- `Core/Internal/Ctn/PipeSpacing/PipeSpacingModel.cs`: pure modes, scalar inputs/targets, DN rules, settings, plan/report DTOs.
- `Core/Internal/Ctn/PipeSpacing/PipeSpacingMath.cs`: unit-agnostic spacing/anchor calculation.
- `Core/Internal/Ctn/PipeSpacing/PipeSpacingTraversalPolicy.cs`: pure bounded connector-graph partition policy.
- `Core/Internal/Ctn/PipeSpacing/PipeSpacingClusterCollector.cs`: Revit connector traversal and mapping to rigid/boundary elements.
- `Core/Internal/Ctn/PipeSpacing/PipeSpacingGeometry.cs`: Revit XYZ axis normalization, ordering, OD/envelope projections.
- `Core/Internal/Ctn/PipeSpacing/PipeSpacingAnalysisService.cs`: selection validation and complete immutable plan creation.
- `Core/Internal/Ctn/PipeSpacing/PipeSpacingBoundaryService.cs`: capture/stretch/verify boundary Pipe state.
- `Core/Internal/Ctn/PipeSpacing/PipeSpacingExecutor.cs`: atomic model mutation, preview, rollback, and report.
- `Core/Internal/Ctn/PipeSpacing/PipeSpacingRunner.cs`: end-to-end command orchestration.
- `Core/State/PipeSpacingConfigStore.cs`: versioned per-document settings persistence.
- `Core/UI/UI_PipeSpacingWindow.cs`: settings, mode-dependent controls, and preview table.
- `Core/Commands/CTN/Command_PipeSpacing.cs`: secured external-command entry point.
- `Core/Application.cs`: tooltip and `PLUMBING` ribbon registration.
- `tools/PipeSpacingMathConsoleTest.cs` and `.csproj`: pure planner/traversal regression tests.
- `tools/Test-PipeSpacing.ps1`: built-assembly geometry/source-contract regression tests.

### Task 1: Pure spacing planner and safety baseline

**Files:**
- Create: `src/TungMEP2/Core/Internal/Ctn/PipeSpacing/PipeSpacingModel.cs`
- Create: `src/TungMEP2/Core/Internal/Ctn/PipeSpacing/PipeSpacingMath.cs`
- Create: `tools/PipeSpacingMathConsoleTest.cs`
- Create: `tools/PipeSpacingMathConsoleTest.csproj`
- Verify: `Build-TungMEP2.ps1`

**Interfaces:**
- Produces: `PipeSpacingMode { EqualClearGap, EqualCenterSpacing, DnRuleTable }`.
- Produces: `PipeSpacingScalarInput(long key, double coordinate, double effectiveDiameter, double nominalDiameter)`.
- Produces: `PipeSpacingDnRule(double maxNominalDiameter, double clearGap)`.
- Produces: `PipeSpacingScalarTarget(long key, double originalCoordinate, double targetCoordinate, double delta, bool isAnchor)`.
- Produces: mutable DTO `PipeSpacingSettings` with the properties consumed by Tasks 3–5; defaulting/normalization and persistence are completed in Task 4.
- Produces: `PipeSpacingMath.TryBuildTargets(IReadOnlyList<PipeSpacingScalarInput> orderedInputs, PipeSpacingMode mode, double requestedSpacing, IReadOnlyList<PipeSpacingDnRule> rules, double fallbackClearGap, out PipeSpacingScalarPlan plan, out string error)`.

- [ ] **Step 1: Capture the pre-change baseline and restorable backup**

Run the current `Build-TungMEP2.ps1` and existing regression scripts. Create `C:\Users\tungnt2303\Downloads\260515_Release\backups\PipeSpacing-before-<yyyyMMdd-HHmmss>` containing `src`, `tools`, `Build-TungMEP2.ps1`, `release\TungMEP2`, `TungMEP2-Revit2024.zip` if present, installed `%APPDATA%\Autodesk\Revit\Addins\2024\TungMEP2.addin`, and `RESTORE.txt` with exact restore source/destination paths and SHA256 hashes.

Expected: baseline build passes; failures are recorded before feature code begins; backup contents can be enumerated without missing required artifacts.

- [ ] **Step 2: Write the failing pure-math console tests**

Add tests named `BuildsCaseStudyClearances`, `AnchorsLargerRightOuterPipe`, `UsesLowerCoordinateForEqualOuterDiameters`, `BuildsEqualCenterSpacing`, `SelectsDnRuleAndFallback`, and `RejectsNegativeOrDegenerateInput`. Assert the case-study centers are exactly `185, 240, 295` mm for OD `110,110,220,220` and gap `75` mm; assert the larger of the two outer diameters stays at its original coordinate.

- [ ] **Step 3: Run the console test to verify it fails**

Run: `dotnet run --project tools/PipeSpacingMathConsoleTest.csproj -c Release`

Expected: FAIL because `PipeSpacingMath` and its models do not exist.

- [ ] **Step 4: Implement the minimal scalar model and planner**

Implement the exact interfaces above. Require inputs already sorted by coordinate, choose only index `0` or `Count-1` as anchor, calculate outward targets cumulatively from the anchor, keep ordering, compute `MinimumClearGap`, and return a useful Vietnamese validation error instead of throwing for user input.

- [ ] **Step 5: Run the pure tests and full build**

Run: `dotnet run --project tools/PipeSpacingMathConsoleTest.csproj -c Release`

Run: `powershell -ExecutionPolicy Bypass -File .\Build-TungMEP2.ps1`

Expected: console prints `PipeSpacing math tests passed`; Revit add-in build/package succeeds.

- [ ] **Step 6: Create checkpoint `PipeSpacing-task1-math`**

Copy the four created files plus build hashes into a timestamped subfolder of the feature backup. In a Git clone, commit message: `feat: add pipe spacing scalar planner`.

### Task 2: Bounded connected-cluster partition

**Files:**
- Create: `src/TungMEP2/Core/Internal/Ctn/PipeSpacing/PipeSpacingTraversalPolicy.cs`
- Create: `src/TungMEP2/Core/Internal/Ctn/PipeSpacing/PipeSpacingClusterCollector.cs`
- Modify: `tools/PipeSpacingMathConsoleTest.cs`
- Modify: `tools/PipeSpacingMathConsoleTest.csproj`

**Interfaces:**
- Consumes: pure model types from Task 1.
- Produces: `PipeSpacingTraversalNodeKind { Pipe, TwoPortFitting, Boundary, Unsupported }`.
- Produces: `PipeSpacingTraversalNode(long id, PipeSpacingTraversalNodeKind kind, IReadOnlyList<long> neighborIds)`.
- Produces: `PipeSpacingTraversalPartition(IReadOnlyList<long> rigidIds, IReadOnlyList<long> boundaryStretchIds, IReadOnlyList<long> boundaryIds, bool isConstrained)`.
- Produces: `PipeSpacingTraversalPolicy.TryPartition(long seedId, Func<long, PipeSpacingTraversalNode> getNode, out PipeSpacingTraversalPartition partition, out string error)`.
- Produces: `PipeSpacingClusterCollector.TryCollect(Document document, Pipe seedPipe, out PipeSpacingCluster cluster, out string error)`.
- `PipeSpacingCluster` exposes `SeedPipeId`, `RigidElementIds`, `BoundaryStretchSegments`, `AllOwnedElementIds`, and status/warnings.

- [ ] **Step 1: Add failing traversal-policy tests**

Test pass-through chains `Pipe → Elbow → Pipe`, stop-at-boundary chains `Pipe → Elbow → Pipe → Tee`, a graph cycle visited once, a seed Pipe that itself becomes the only boundary stretch segment, two fixed boundaries, and unsupported owners. Assert the Tee is never in `RigidIds`, the adjacent Pipe is in `BoundaryStretchIds`, and direct-boundary/no-rigid cases return `Constrained`.

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet run --project tools/PipeSpacingMathConsoleTest.csproj -c Release`

Expected: FAIL because traversal policy is missing.

- [ ] **Step 3: Implement pure graph partitioning**

Implement breadth-first traversal with an `ElementId`/`long` visited set. Traverse only `Pipe` and `TwoPortFitting`; record boundary edges without traversing into `Boundary`; identify every Pipe adjacent to a boundary as a stretch candidate; remove stretch candidates from the rigid set; reject an empty rigid core or a stretch Pipe constrained by two stationary boundaries.

- [ ] **Step 4: Implement Revit node mapping and cluster collection**

Map straight Pipe to `Pipe`; map only two-physical-piping-connector `OST_PipeFitting` coupling/union/reducer/elbow families to `TwoPortFitting`; map Tee/Wye/Cross, Pipe Accessory, Mechanical Equipment, Plumbing Fixture, multi-port owners, and branch-generating owners to `Boundary`. Reject Pin/Group in the rigid core, record boundary connector origins/directions, and never modify the document in collection.

- [ ] **Step 5: Run tests and build**

Run the Task 1 console command and `Build-TungMEP2.ps1`.

Expected: traversal tests pass; add-in compiles without changing existing commands.

- [ ] **Step 6: Create checkpoint `PipeSpacing-task2-clusters`**

Checkpoint the traversal/collector/test files and hashes. Git equivalent: `feat: collect bounded pipe spacing clusters`.

### Task 3: Revit geometry analysis and immutable plan

**Files:**
- Create: `src/TungMEP2/Core/Internal/Ctn/PipeSpacing/PipeSpacingGeometry.cs`
- Create: `src/TungMEP2/Core/Internal/Ctn/PipeSpacing/PipeSpacingAnalysisService.cs`
- Create: `tools/Test-PipeSpacing.ps1`
- Modify: `src/TungMEP2/Core/Internal/Ctn/PipeSpacing/PipeSpacingModel.cs`

**Interfaces:**
- Consumes: `PipeSpacingClusterCollector.TryCollect` and `PipeSpacingMath.TryBuildTargets`.
- Produces: `PipeSpacingGeometry.TryBuildAxes(IReadOnlyList<XYZ> directions, XYZ viewRight, XYZ viewUp, double parallelDotTolerance, out XYZ runAxis, out XYZ spacingAxis, out string error)`.
- Produces: `PipeSpacingGeometry.ProjectExtent(BoundingBoxXYZ box, XYZ axis)`.
- Produces: `PipeSpacingAnalysisService.TryCreatePlan(UIDocument uiDocument, IReadOnlyCollection<ElementId> selectedIds, PipeSpacingSettings settings, out PipeSpacingPlan plan, out PipeSpacingAnalysisReport report, out string error)`.
- `PipeSpacingPlan` is immutable and stores active view ID, anchor cluster, ordered valid clusters, move vectors, captured source element IDs/UniqueIds, requested settings, and excluded rows.

- [ ] **Step 1: Write failing built-assembly geometry/source tests**

In `Test-PipeSpacing.ps1`, use reflection against `build\TungMEP2.dll` to assert axis creation for horizontal and sloped-parallel directions, deterministic sign from view right/up, rejection of a near-vertical direction, rejection of mixed nonparallel directions, and bounding-box projection. Add a source contract that analysis reads `RBS_PIPE_OUTER_DIAMETER` before any fallback and checks insulation only when requested.

- [ ] **Step 2: Run test and verify failure**

Run: `powershell -ExecutionPolicy Bypass -File .\tools\Test-PipeSpacing.ps1`

Expected: FAIL because geometry/analysis types are absent.

- [ ] **Step 3: Implement axis, extent, and OD resolution**

Use normalized plan projections, reject plan length below `1e-9`, and require absolute direction dot product at least the settings tolerance. Resolve OD from `RBS_PIPE_OUTER_DIAMETER`; if invalid, project actual Pipe geometry and attach a warning; add insulation thickness and fitting envelope only when their settings are enabled.

- [ ] **Step 4: Implement complete analysis**

Validate project document/editability and at least two straight Pipes, collect clusters, merge duplicate selections belonging to the same `AllOwnedElementIds`, reject intersecting ownership between otherwise distinct clusters, order by spacing-axis coordinate, call scalar planning, and build move vectors with `Z = 0`. Re-read cluster state before returning the immutable plan and list excluded/constrained selections explicitly.

- [ ] **Step 5: Run pure tests, geometry tests, and build**

Expected: both test scripts pass; build/package succeeds; no document transaction is opened by analysis.

- [ ] **Step 6: Create checkpoint `PipeSpacing-task3-analysis`**

Git equivalent: `feat: analyze pipe spacing geometry`.

### Task 4: Versioned settings and WPF configuration UI

**Files:**
- Create: `src/TungMEP2/Core/State/PipeSpacingConfigStore.cs`
- Create: `src/TungMEP2/Core/UI/UI_PipeSpacingWindow.cs`
- Modify: `src/TungMEP2/Core/Internal/Ctn/PipeSpacing/PipeSpacingModel.cs`
- Modify: `tools/PipeSpacingMathConsoleTest.cs`
- Modify: `tools/Test-PipeSpacing.ps1`

**Interfaces:**
- Produces: `PipeSpacingSettings.CreateDefault()`, `Normalize()`, and properties `Version`, `Mode`, `ClearGapMm`, `CenterSpacingMm`, `FallbackClearGapMm`, `IncludeInsulation`, `IncludeFittingEnvelope`, `ParallelAngleToleranceDegrees`, `PreviewBeforeApply`, `DnRules`.
- Produces: `PipeSpacingConfigStore.Load(Document document)` and `Save(Document document, PipeSpacingSettings settings)` using `%USERPROFILE%\Desktop\VP_RevitAPI\pipe_spacing_config.json` and the existing document-key convention.
- Produces: testable `PipeSpacingConfigStore.DeserializeForDocument(string json, string documentKey)` returning normalized defaults for missing/old/malformed data without touching the user's real config file.
- `PipeSpacingSettings.Normalize()` returns a new normalized settings instance and never mutates the caller's instance.
- Produces: `UI_PipeSpacingWindow(PipeSpacingSettings initialSettings, Func<PipeSpacingSettings, PipeSpacingDialogPreview> previewProvider)`; after `ShowDialog() == true`, read `SelectedSettings` and `PreviewBeforeApply`.
- Produces: `PipeSpacingDialogPreview` with immutable `Rows`, `MinimumClearGapMm`, `AnchorKey`, and `Warnings` for the WPF grid.

- [ ] **Step 1: Add failing settings normalization tests**

Assert defaults are version `1`, Equal Clear Gap, `75 mm`, preview enabled; negative/NaN values normalize; unsorted/overlapping DN bands normalize deterministically; missing bands retain fallback; old version and malformed JSON load defaults without throwing.

- [ ] **Step 2: Run tests and verify failure**

Run the console and PowerShell test commands.

Expected: FAIL on missing settings/config/UI contracts.

- [ ] **Step 3: Implement settings and atomic JSON persistence**

Key project entries with `ThreeDSyncSessionStore.GetDocumentKey(document)`, write through `.tmp` then replace, keep other project records, catch I/O/JSON errors and return defaults. Store millimetres, not internal feet.

- [ ] **Step 4: Implement the code-built WPF dialog**

Add mode selector, numeric fields, insulation/fitting checkboxes, DN-rule editor, Advanced angle tolerance, preview checkbox, and a read-only preview grid with status/warning color. Mode changes enable only relevant fields. `Apply` validates through `Normalize()` and returns true; `Cancel` returns false; `Reset defaults` restores 75 mm defaults.

- [ ] **Step 5: Run tests and build**

Expected: settings contracts pass, build includes the code-only Window without new BAML resource requirements.

- [ ] **Step 6: Create checkpoint `PipeSpacing-task4-ui-config`**

Git equivalent: `feat: add pipe spacing settings dialog`.

### Task 5: Boundary stretching, atomic execution, and preview

**Files:**
- Create: `src/TungMEP2/Core/Internal/Ctn/PipeSpacing/PipeSpacingBoundaryService.cs`
- Create: `src/TungMEP2/Core/Internal/Ctn/PipeSpacing/PipeSpacingExecutor.cs`
- Modify: `src/TungMEP2/Core/Internal/Ctn/PipeSpacing/PipeSpacingModel.cs`
- Modify: `tools/Test-PipeSpacing.ps1`

**Interfaces:**
- Produces: `PipeSpacingBoundaryService.TryCapture(Document document, PipeSpacingPlan plan, out IReadOnlyList<PipeSpacingBoundaryState> states, out string error)`.
- Produces: `PipeSpacingBoundaryService.TryStretchAndVerify(Document document, PipeSpacingBoundaryState state, XYZ rigidMove, out string error)`.
- Produces: `PipeSpacingExecutor.TryApply(UIDocument uiDocument, PipeSpacingPlan plan, bool previewBeforeApply, out PipeSpacingApplyReport report, out string error)`.

- [ ] **Step 1: Add failing boundary/executor contract tests**

Add reflection/source checks for: captured fixed/moving endpoints; boundary Pipe excluded from `ElementTransformUtils.MoveElements`; one transaction named `TungMEP2 - PipeSpacing` with one `SubTransaction` per dependent cluster; pre-commit revalidation of element existence, Pin/Group/editability, UniqueId, connector coincidence, slope/Z, order, and target clearance; rollback path for any failed cluster; preview override restoration before commit and rollback on Cancel.

- [ ] **Step 2: Run test and verify failure**

Run: `powershell -ExecutionPolicy Bypass -File .\tools\Test-PipeSpacing.ps1`

Expected: FAIL because boundary/executor contracts are absent.

- [ ] **Step 3: Implement boundary capture/stretch verification**

Capture the two original endpoints, fixed-boundary owner/connector, moving-core owner/connector, original slope, and minimum allowed length. After rigid movement, replace only the boundary Pipe `LocationCurve` so its fixed endpoint stays and its moving endpoint follows; regenerate; require both endpoint connectors to coincide/connect and reject too-short, self-intersecting, or Revit-refused geometry. Do not move or replace Tee/Wye/Cross.

- [ ] **Step 4: Implement all-or-nothing execution**

Immediately before transaction, re-resolve every source ID/UniqueId and checkout/editability condition. Start one outer transaction and one `SubTransaction` per cluster, move rigid core IDs once, stretch boundaries, regenerate, and validate that cluster. A failed subtransaction rolls back before the outer transaction is rolled back; the whole Apply still fails because later target coordinates depend on earlier clusters.

- [ ] **Step 5: Implement visual preview decision**

When enabled, retain the tentative transaction, temporarily override moving elements green and anchor gray in the active view, refresh the view, and show Apply/Cancel. On Apply, restore every prior `OverrideGraphicSettings` before commit; on Cancel/close, rollback. Invalid/excluded rows remain red/yellow in the WPF preview table rather than being added to the model.

- [ ] **Step 6: Run all tests and build**

Expected: tests pass; build succeeds; executor has no path that commits partial state.

- [ ] **Step 7: Create checkpoint `PipeSpacing-task5-executor`**

Git equivalent: `feat: execute pipe spacing with safe boundaries`.

### Task 6: Command orchestration and PLUMBING ribbon integration

**Files:**
- Create: `src/TungMEP2/Core/Internal/Ctn/PipeSpacing/PipeSpacingRunner.cs`
- Create: `src/TungMEP2/Core/Commands/CTN/Command_PipeSpacing.cs`
- Modify: `src/TungMEP2/Core/Application.cs`
- Modify: `tools/Test-PipeSpacing.ps1`

**Interfaces:**
- Consumes: analysis, dialog/config, and executor from Tasks 3–5.
- Produces: `PipeSpacingRunner.Run(UIDocument uiDocument, Document document)`.
- Produces: secured external command `VP_RevitAPI.Commands.CTN.Command_PipeSpacing`.

- [ ] **Step 1: Add failing runner/ribbon source contracts**

Assert `Command_PipeSpacing` exists and inherits `SecuredExternalCommand`; `Application.cs` contains tooltip key `PipeSpacing` and `AddPushButton<Command_PipeSpacing>("PipeSpacing")` inside the `PLUMBING` stack next to `PipeLayer`; runner accepts preselection, falls back to `PickObjects` with a straight-Pipe filter, requires at least two Pipes, loads/saves settings, analyzes, shows preview, applies, and reports result.

- [ ] **Step 2: Run the contract test and verify failure**

Expected: FAIL on missing command/ribbon registration.

- [ ] **Step 3: Implement runner and secured command**

Reject family documents. Prefer selected straight Pipes; if fewer than two, prompt `Chọn các ống song song cần dàn khoảng cách`. Build preview rows through `TryCreatePlan`, show the dialog, persist normalized settings only after confirmation, rebuild the final plan from current model state, call executor, then show counts, anchor reason, minimum clearance, stretched segments, and skipped/error details.

- [ ] **Step 4: Register the PLUMBING button**

Add tooltip text from the spec and place `PipeSpacing` next to `PipeLayer`, reusing `LayerPipe_16.png`/`LayerPipe_32.png` for v1 so no resource manifest change is needed.

- [ ] **Step 5: Run regression suite and package build**

Run:

`dotnet run --project tools/PipeSpacingMathConsoleTest.csproj -c Release`

`powershell -ExecutionPolicy Bypass -File .\tools\Test-PipeSpacing.ps1`

`powershell -ExecutionPolicy Bypass -File .\tools\Test-MepAlignment.ps1`

`powershell -ExecutionPolicy Bypass -File .\tools\Test-FitRotate.ps1`

`powershell -ExecutionPolicy Bypass -File .\Build-TungMEP2.ps1`

Expected: all tests pass, package and `TungMEP2-Revit2024.zip` are regenerated.

- [ ] **Step 6: Create checkpoint `PipeSpacing-task6-command`**

Git equivalent: `feat: expose pipe spacing in plumbing ribbon`.

### Task 7: Revit 2024 acceptance, installation, and restore proof

**Files:**
- Modify if defects are found: only files introduced/changed by Tasks 1–6.
- Verify: `release/TungMEP2/Install-TungMEP2.ps1`
- Verify: `TungMEP2-Revit2024.zip`
- Update: feature backup `RESTORE.txt` with final hashes and install path.

**Interfaces:**
- Consumes: complete `PipeSpacing` command/package.
- Produces: installed Revit 2024 build and recorded acceptance evidence.

- [ ] **Step 1: Install the packaged build**

Close Revit if the DLL is loaded, run `release\TungMEP2\Install-TungMEP2.ps1`, record the SHA256 of packaged/installed `TungMEP2.dll`, and verify the add-in manifest points to the hash-versioned install folder.

- [ ] **Step 2: Execute the 12 Revit cases from the spec**

At minimum prove the OD110/OD110/OD220/OD220 case gives `185/240/295 mm`, larger outer anchor stays fixed, equal-OD tie is deterministic, slopes/Z remain, insulation changes spacing, coupling/reducer/elbow moves as one core, Tee boundary stretches/reconnects, two-boundary constraint previews red, invalid selections do not change the model, Cancel/Undo restore exactly, worksharing denial is safe, and immediate rerun produces no movement outside tolerance.

- [ ] **Step 3: Inspect connector and Undo postconditions**

For each connected case, use Revit connector inspection/model behavior to confirm no new open connector, no moved stationary Tee/Wye, no altered System Type/DN/slope on primary runs, and exactly one Undo item named `TungMEP2 - PipeSpacing`.

- [ ] **Step 4: Re-run automated verification after any acceptance fix**

Repeat all Task 6 commands and reinstall; no manual-only fix may ship without the complete suite passing.

- [ ] **Step 5: Prove restore procedure**

Validate every source/destination in `RESTORE.txt`, compare recorded baseline hashes, and confirm the prior package/add-in manifest can be restored without deleting unrelated user data. Do not perform the restore unless acceptance fails or the user requests rollback.

- [ ] **Step 6: Create final checkpoint `PipeSpacing-release`**

Record final source/package/installed hashes and Revit acceptance results. Git equivalent: `release: ship pipe spacing for Revit 2024`.
