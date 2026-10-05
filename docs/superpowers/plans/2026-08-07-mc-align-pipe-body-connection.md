# MC-Align Pipe Body Connection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Nâng cấp `MC-Align` để ưu tiên nối đầu hở–đầu hở, đồng thời tự chèn Tee/Y vào thân Pipe mốc kín hai đầu khi Pipe được chọn thứ hai còn đầu hở.

**Architecture:** Giữ `MepConnectorAlignmentRunner` làm bộ điều phối, thêm một policy thuần để chọn chiến lược, một helper hình học thuần để chiếu điểm nội bộ và một service Revit chuyên lập/thi hành body connection. Service di chuyển toàn bộ connected cluster, sau đó tái sử dụng `PipeUtilities.TeeMaster` để hưởng cơ chế reducing Tee/Y và auto-transition hiện có; mọi thao tác model vẫn nằm trong transaction chính của runner.

**Tech Stack:** C# 10, .NET Framework 4.8, Autodesk Revit API 2024, PowerShell regression tests, build/package bằng `Build-TungMEP2.ps1`.

## Global Constraints

- Phần tử chọn thứ nhất là mốc và không di chuyển; phần tử thứ hai xác định cụm di chuyển.
- Chỉ `MC-Align` được phép nối vào thân, và chỉ khi cả fixed và moving selected element đều là `Pipe` hợp lệ.
- Chỉ xét đầu Pipe hở trên chính phần tử thứ hai được chọn; không tìm đầu hở ở phần tử khác trong cụm.
- Luôn ưu tiên cặp connector hở–hở hơn body connection.
- Body connection giữ hướng và độ dốc hiện tại của branch; hình học song song/gần song song phải từ chối.
- Không tự tháo kết nối, không tự nạp family và không sửa Routing Preferences.
- Direct reducing Tee/Y được thử trước; nếu không được thì dùng Tee/Y cùng DN main + transition hiện có.
- Mọi biến đổi, break, fitting và transition nằm trong một transaction; lỗi phải rollback toàn bộ.
- Không đổi command name, ribbon button, icon hoặc khả năng gán phím tắt.
- Source hiện tại không phải Git repository; không khởi tạo Git. Dùng backup timestamp và checkpoint copy thay cho commit.
- Không sửa hoặc xóa bất kỳ backup nào.

---

## File Map

- Create `src/TungMEP2/Core/Internal/Common/MepPipeBodyConnectionPolicy.cs`: chọn `Endpoint`, `PipeBody` hoặc `AlignOnly` bằng dữ liệu boolean thuần.
- Create `src/TungMEP2/Core/Internal/Common/MepPipeBodyConnectionGeometry.cs`: chiếu điểm lên đoạn tim, kiểm tra khoảng hở đầu đoạn và hướng song song.
- Create `src/TungMEP2/Core/Internal/Common/MepPipeBodyConnectionService.cs`: chọn đầu hở trên moving Pipe, lập plan, dịch cluster, gọi Tee/Y helper và kiểm tra hậu điều kiện.
- Modify `src/TungMEP2/Core/Internal/Common/MepConnectorAlignmentRunner.cs`: chọn strategy và rẽ sang body service trước luồng connector fallback hiện tại.
- Modify `src/TungMEP2/Core/Application.cs:28`: cập nhật tooltip `MC-Align`.
- Modify `tools/Test-MepAlignment.ps1`: regression tests cho policy, geometry và source contracts.
- Reuse unchanged `src/TungMEP2/Core/Helper/PipeUtilities.cs:142-201,674-770`: `TeeMaster`, direct reducing attempt và auto-transition fallback.
- Create timestamped backup under `C:/Users/tungnt2303/Downloads/260515_Release/backups/Before-MCAlign-PipeBody-<timestamp>` and a delivery ZIP under `C:/Users/tungnt2303/Downloads/260515_Release`.

---

### Task 1: Baseline verification and recoverable backup

**Files:**
- Copy: `src/TungMEP2/Core/Internal/Common/MepConnectorAlignmentRunner.cs`
- Copy: `src/TungMEP2/Core/Internal/Common/MepConnectorAlignmentPolicy.cs`
- Copy: `src/TungMEP2/Core/Internal/Common/MepConnectedClusterCollector.cs`
- Copy: `src/TungMEP2/Core/Helper/PipeUtilities.cs`
- Copy: `src/TungMEP2/Core/Application.cs`
- Copy: `tools/Test-MepAlignment.ps1`
- Copy: `build/TungMEP2.dll`
- Copy: `release/TungMEP2`
- Copy: `TungMEP2-Revit2024.zip`
- Copy when present: `%APPDATA%/Autodesk/Revit/Addins/2024/TungMEP2.addin`
- Create: `C:/Users/tungnt2303/Downloads/260515_Release/backups/Before-MCAlign-PipeBody-<timestamp>/RESTORE.txt`

**Interfaces:**
- Consumes: current source, build, package and installed manifest.
- Produces: immutable backup path, baseline hashes and a known-good automated-test result.

- [ ] **Step 1: Record current process and build state**

Run:

```powershell
Get-Process Revit -ErrorAction SilentlyContinue | Select-Object Id, ProcessName, StartTime
Get-FileHash build\TungMEP2.dll -Algorithm SHA256
Get-FileHash release\TungMEP2\net48\TungMEP2.dll -Algorithm SHA256
Get-Content "$env:APPDATA\Autodesk\Revit\Addins\2024\TungMEP2.addin" -ErrorAction SilentlyContinue
```

Expected: hashes and current manifest are captured; a running Revit process is informational and must not be killed.

- [ ] **Step 2: Create the timestamped backup without deleting anything**

Run the following after resolving `$backupRoot` and verifying it begins with the exact backups directory:

```powershell
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupBase = 'C:\Users\tungnt2303\Downloads\260515_Release\backups'
$backupRoot = Join-Path $backupBase "Before-MCAlign-PipeBody-$stamp"
$resolvedBase = [IO.Path]::GetFullPath($backupBase)
$resolvedBackup = [IO.Path]::GetFullPath($backupRoot)
if (-not $resolvedBackup.StartsWith($resolvedBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Backup path is outside the approved backup directory.'
}
New-Item -ItemType Directory -Path $backupRoot | Out-Null
Copy-Item src\TungMEP2\Core\Internal\Common\MepConnectorAlignmentRunner.cs $backupRoot
Copy-Item src\TungMEP2\Core\Internal\Common\MepConnectorAlignmentPolicy.cs $backupRoot
Copy-Item src\TungMEP2\Core\Internal\Common\MepConnectedClusterCollector.cs $backupRoot
Copy-Item src\TungMEP2\Core\Helper\PipeUtilities.cs $backupRoot
Copy-Item src\TungMEP2\Core\Application.cs $backupRoot
Copy-Item tools\Test-MepAlignment.ps1 $backupRoot
Copy-Item build\TungMEP2.dll $backupRoot
Copy-Item release\TungMEP2 (Join-Path $backupRoot 'release-TungMEP2') -Recurse
Copy-Item TungMEP2-Revit2024.zip $backupRoot
$addin = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2024\TungMEP2.addin'
if (Test-Path -LiteralPath $addin) { Copy-Item -LiteralPath $addin -Destination $backupRoot }
```

Expected: one new backup directory exists; no source or installed file has changed.

- [ ] **Step 3: Add restore instructions using `apply_patch`**

Create `RESTORE.txt` inside the resolved backup directory with this exact content, replacing `<BACKUP>` by the actual absolute path:

```text
Backup before MC-Align Pipe body connection.

Source root: C:\Users\tungnt2303\Downloads\tungmep2
Backup root: <BACKUP>

Restore source files by copying the backed-up .cs/.ps1 files to their original paths.
Restore package by copying release-TungMEP2 back to release\TungMEP2.
Restore the previous installed build by running release-TungMEP2\Install-TungMEP2.ps1 from this backup.
If Revit is open, close and reopen Revit after restoring so the DLL is reloaded.
Do not delete this backup.
```

- [ ] **Step 4: Verify backup hashes match the recorded current build**

Run:

```powershell
Get-FileHash build\TungMEP2.dll -Algorithm SHA256
Get-FileHash (Join-Path $backupRoot 'TungMEP2.dll') -Algorithm SHA256
Get-ChildItem -LiteralPath $backupRoot -Force
```

Expected: the two DLL hashes match and `RESTORE.txt`, source files, package copy and ZIP are present.

- [ ] **Step 5: Run the baseline automated checks after backup is safe**

Run:

```powershell
.\Build-TungMEP2.ps1
.\tools\Test-MepAlignment.ps1
.\tools\Test-DN125-AutoTransition.ps1
```

Expected: build succeeds; both test scripts print a green pass message. Any regenerated package is now recoverable from the backup made in Step 2.

---

### Task 2: Strategy and projection logic under tests

**Files:**
- Create: `src/TungMEP2/Core/Internal/Common/MepPipeBodyConnectionPolicy.cs`
- Create: `src/TungMEP2/Core/Internal/Common/MepPipeBodyConnectionGeometry.cs`
- Modify: `tools/Test-MepAlignment.ps1`

**Interfaces:**
- Consumes: `MepConnectorAlignmentMode` from `MepConnectorAlignmentRunner.cs` and `Autodesk.Revit.DB.XYZ`.
- Produces: `MepAlignmentConnectionStrategy`, `MepPipeBodyConnectionPolicy.ChooseStrategy(...)`, `MepPipeBodyConnectionGeometry.TryProjectInteriorPoint(...)`, and `MepPipeBodyConnectionGeometry.AreParallel(...)`.

- [ ] **Step 1: Add failing reflection tests for strategy priority**

Append tests that resolve the two new types and call this signature:

```csharp
internal static MepAlignmentConnectionStrategy ChooseStrategy(
    MepConnectorAlignmentMode mode,
    bool hasOpenOpenPair,
    bool fixedIsPipe,
    bool fixedHasOpenEnd,
    bool movingSelectedIsPipe,
    bool movingSelectedHasOpenEnd)
```

The PowerShell cases must assert exactly:

```powershell
$cases = @(
    @{ Mode='MoveConnectAlign'; OpenPair=$true;  FixedPipe=$true; FixedOpen=$false; MovingPipe=$true; MovingOpen=$true;  Expected='Endpoint' },
    @{ Mode='MoveConnectAlign'; OpenPair=$false; FixedPipe=$true; FixedOpen=$false; MovingPipe=$true; MovingOpen=$true;  Expected='PipeBody' },
    @{ Mode='MoveConnectAlign'; OpenPair=$false; FixedPipe=$true; FixedOpen=$false; MovingPipe=$true; MovingOpen=$false; Expected='AlignOnly' },
    @{ Mode='MoveConnectAlign'; OpenPair=$false; FixedPipe=$false;FixedOpen=$false; MovingPipe=$true; MovingOpen=$true;  Expected='AlignOnly' },
    @{ Mode='AlignIn3D';        OpenPair=$true;  FixedPipe=$true; FixedOpen=$false; MovingPipe=$true; MovingOpen=$true;  Expected='AlignOnly' }
)
```

- [ ] **Step 2: Add failing geometry tests**

Reflect these exact signatures:

```csharp
internal static bool TryProjectInteriorPoint(
    XYZ start,
    XYZ end,
    XYZ source,
    double endClearance,
    out XYZ projectedPoint);

internal static bool AreParallel(
    XYZ firstDirection,
    XYZ secondDirection,
    double cosineTolerance = 0.999);
```

Test these inputs:

```powershell
# Horizontal: (4,3,7) projects to (4,0,0) inside (0,0,0)-(10,0,0).
# Vertical: (4,3,7) projects to (0,0,7) inside (0,0,0)-(0,0,10).
# Sloped: (5,3,5) projects to (5,0,5) on (0,0,0)-(10,0,10).
# Endpoint rejection: source near x=0 fails when endClearance=1.
# Parallel treats +X/+X and +X/-X as parallel; +X/+Y is not parallel.
```

- [ ] **Step 3: Run the test and verify the new tests fail**

Run:

```powershell
.\tools\Test-MepAlignment.ps1
```

Expected: FAIL because `MepPipeBodyConnectionPolicy` and `MepPipeBodyConnectionGeometry` do not exist.

- [ ] **Step 4: Implement the minimal policy**

Create `MepPipeBodyConnectionPolicy.cs` with this implementation:

```csharp
namespace VP_RevitAPI.Internal.Common;

internal enum MepAlignmentConnectionStrategy
{
    Endpoint,
    PipeBody,
    AlignOnly
}

internal static class MepPipeBodyConnectionPolicy
{
    internal static MepAlignmentConnectionStrategy ChooseStrategy(
        MepConnectorAlignmentMode mode,
        bool hasOpenOpenPair,
        bool fixedIsPipe,
        bool fixedHasOpenEnd,
        bool movingSelectedIsPipe,
        bool movingSelectedHasOpenEnd)
    {
        if (mode == MepConnectorAlignmentMode.AlignIn3D)
        {
            return MepAlignmentConnectionStrategy.AlignOnly;
        }

        if (hasOpenOpenPair)
        {
            return MepAlignmentConnectionStrategy.Endpoint;
        }

        if (mode == MepConnectorAlignmentMode.MoveConnectAlign &&
            fixedIsPipe &&
            !fixedHasOpenEnd &&
            movingSelectedIsPipe &&
            movingSelectedHasOpenEnd)
        {
            return MepAlignmentConnectionStrategy.PipeBody;
        }

        return MepAlignmentConnectionStrategy.AlignOnly;
    }
}
```

- [ ] **Step 5: Implement bounded projection and parallel checks**

Create `MepPipeBodyConnectionGeometry.cs` with guarded vector math:

```csharp
using System;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.Internal.Common;

internal static class MepPipeBodyConnectionGeometry
{
    private const double Epsilon = 1E-09;

    internal static bool TryProjectInteriorPoint(
        XYZ start,
        XYZ end,
        XYZ source,
        double endClearance,
        out XYZ projectedPoint)
    {
        projectedPoint = null;
        if (start == null || end == null || source == null || endClearance < 0.0)
        {
            return false;
        }

        XYZ axis = end - start;
        double lengthSquared = axis.DotProduct(axis);
        if (lengthSquared < Epsilon)
        {
            return false;
        }

        double parameter = (source - start).DotProduct(axis) / lengthSquared;
        if (parameter <= 0.0 || parameter >= 1.0)
        {
            return false;
        }

        XYZ candidate = start + axis.Multiply(parameter);
        if (candidate.DistanceTo(start) < endClearance ||
            candidate.DistanceTo(end) < endClearance)
        {
            return false;
        }

        projectedPoint = candidate;
        return true;
    }

    internal static bool AreParallel(
        XYZ firstDirection,
        XYZ secondDirection,
        double cosineTolerance = 0.999)
    {
        if (firstDirection == null || secondDirection == null ||
            firstDirection.GetLength() < Epsilon ||
            secondDirection.GetLength() < Epsilon)
        {
            return true;
        }

        double cosine = Math.Abs(firstDirection.Normalize()
            .DotProduct(secondDirection.Normalize()));
        return cosine >= cosineTolerance;
    }
}
```

- [ ] **Step 6: Build and run the focused tests**

Run:

```powershell
.\Build-TungMEP2.ps1
.\tools\Test-MepAlignment.ps1
```

Expected: build succeeds and all old/new policy and geometry assertions pass.

- [ ] **Step 7: Create a source checkpoint in the Task 1 backup**

Copy the two new `.cs` files and modified test script to `<BACKUP>/checkpoint-task2`, preserving source-relative folders. Expected: the checkpoint contains only Task 2 deliverables.

---

### Task 3: Pipe-body connection planning and execution service

**Files:**
- Create: `src/TungMEP2/Core/Internal/Common/MepPipeBodyConnectionService.cs`
- Modify: `tools/Test-MepAlignment.ps1`
- Reuse: `src/TungMEP2/Core/Helper/PipeUtilities.cs:674-770`

**Interfaces:**
- Consumes: `MepPipeBodyConnectionGeometry`, `MepConnectedClusterCollector.GetPhysicalConnectors`, `ConnectorUtilities`, and `PipeUtilities.TeeMaster(Pipe mainPipe, Pipe branchPipe)`.
- Produces: `MepPipeBodyConnectionPlan`, `TryCreatePlan(...)`, and `TryExecute(...)` for the runner.

```csharp
internal sealed class MepPipeBodyConnectionPlan
{
    internal Pipe MainPipe { get; }
    internal Pipe BranchPipe { get; }
    internal XYZ OriginalBranchConnectorOrigin { get; }
    internal XYZ InsertionPoint { get; }
}

internal static bool TryCreatePlan(
    Document document,
    Pipe mainPipe,
    Element movingSelectedElement,
    out MepPipeBodyConnectionPlan plan,
    out string error);

internal static bool TryExecute(
    Document document,
    MepPipeBodyConnectionPlan plan,
    ICollection<ElementId> movingElementIds,
    out FamilyInstance junction,
    out string error);
```

- [ ] **Step 1: Add failing source-contract tests for the service**

Extend `tools/Test-MepAlignment.ps1` to require:

```powershell
$serviceType = $assembly.GetType(
    'VP_RevitAPI.Internal.Common.MepPipeBodyConnectionService',
    $false)
if ($null -eq $serviceType) { throw 'Pipe-body connection service is missing.' }
foreach ($methodName in @('TryCreatePlan', 'TryExecute')) {
    if ($null -eq $serviceType.GetMethod($methodName, [Reflection.BindingFlags]'Static,NonPublic')) {
        throw "Pipe-body service is missing $methodName."
    }
}
```

Also read the service source as UTF-8 and assert it contains all of:

```text
MepConnectedClusterCollector.GetPhysicalConnectors
ConnectorType.End
PipeUtilities.TeeMaster
ElementTransformUtils.MoveElements
IsConnected
```

- [ ] **Step 2: Run the test and verify it fails**

Run:

```powershell
.\tools\Test-MepAlignment.ps1
```

Expected: FAIL because the service type is missing.

- [ ] **Step 3: Implement `TryCreatePlan` with exact validation order**

The method must:

```csharp
plan = null;
error = string.Empty;
if (mainPipe == null || movingSelectedElement is not Pipe branchPipe)
{
    error = "Nối vào thân chỉ hỗ trợ khi cả phần tử mốc và phần tử thứ hai đều là Pipe.";
    return false;
}
if (mainPipe.Location is not LocationCurve mainLocation ||
    mainLocation.Curve is not Line mainLine ||
    branchPipe.Location is not LocationCurve branchLocation ||
    branchLocation.Curve is not Line branchLine)
{
    error = "MC-Align chỉ có thể chèn Tee/Y vào đoạn Pipe thẳng.";
    return false;
}
if (MepPipeBodyConnectionGeometry.AreParallel(mainLine.Direction, branchLine.Direction))
{
    error = "Ống nhánh đang song song với ống mốc nên không thể tạo Tee/Y an toàn.";
    return false;
}
```

Then:

- Get only `ConnectorType.End`, piping-domain, `!IsConnected` connectors owned by `branchPipe`.
- Compute `mainDiameter` and `branchDiameter` from `RBS_PIPE_DIAMETER_PARAM`.
- Set `endClearance = Math.Max(document.Application.ShortCurveTolerance * 2.0, Math.Max(mainDiameter, branchDiameter))`.
- For each open connector call `TryProjectInteriorPoint(mainLine.GetEndPoint(0), mainLine.GetEndPoint(1), connector.Origin, endClearance, out point)`.
- Choose the valid candidate with the smallest `connector.Origin.DistanceTo(point)`.
- If none exists, return `"Không tìm được vị trí đủ khoảng trống trên thân ống mốc để đặt Tee/Y."`.
- Store the chosen connector origin and insertion point in an immutable plan.

- [ ] **Step 4: Implement `TryExecute` inside the caller's active transaction**

Use the following sequence without opening a top-level transaction:

```csharp
XYZ move = plan.InsertionPoint - plan.OriginalBranchConnectorOrigin;
if (move.GetLength() > 1E-06)
{
    ElementTransformUtils.MoveElements(document, movingElementIds, move);
    document.Regenerate();
}

Connector movedBranchConnector = MepConnectedClusterCollector
    .GetPhysicalConnectors(plan.BranchPipe)
    .Where(c => c.ConnectorType == ConnectorType.End && !c.IsConnected)
    .OrderBy(c => c.Origin.DistanceTo(plan.InsertionPoint))
    .FirstOrDefault();
if (movedBranchConnector == null ||
    movedBranchConnector.Origin.DistanceTo(plan.InsertionPoint) > 1E-05)
{
    error = "Không thể đưa đầu hở của nhánh tới đúng vị trí trên thân ống mốc.";
    return false;
}

junction = PipeUtilities.TeeMaster(plan.MainPipe, plan.BranchPipe);
document.Regenerate();
```

After creation, collect physical end connectors on `junction` and require at least three connected connectors. If the condition fails, return `"Tee/Y đã được tạo nhưng chưa kết nối đủ ba nhánh."`; the runner will rollback the transaction. Wrap Revit exceptions and return:

```text
Không thể chèn Tee/Y vào thân ống. Kiểm tra Junction/Transition trong Routing Preferences, góc nối, kích thước fitting và chiều dài đoạn ống. Chi tiết: <Revit message>
```

- [ ] **Step 5: Build and run the focused tests**

Run:

```powershell
.\Build-TungMEP2.ps1
.\tools\Test-MepAlignment.ps1
.\tools\Test-DN125-AutoTransition.ps1
```

Expected: build succeeds; service source contracts and all existing transition tests pass.

- [ ] **Step 6: Create the Task 3 source checkpoint**

Copy `MepPipeBodyConnectionService.cs` and the current test script to `<BACKUP>/checkpoint-task3`. Expected: Task 2 checkpoint remains untouched.

---

### Task 4: Integrate the body strategy into MC-Align only

**Files:**
- Modify: `src/TungMEP2/Core/Internal/Common/MepConnectorAlignmentRunner.cs:49-205`
- Modify: `tools/Test-MepAlignment.ps1`

**Interfaces:**
- Consumes: `MepPipeBodyConnectionPolicy.ChooseStrategy`, `MepPipeBodyConnectionService.TryCreatePlan`, `MepPipeBodyConnectionService.TryExecute`.
- Produces: endpoint-first/body-second/align-only runtime orchestration while preserving the existing connector alignment path.

- [ ] **Step 1: Add failing runner source-contract tests**

Require the runner source to contain all of:

```text
MepPipeBodyConnectionPolicy.ChooseStrategy
MepAlignmentConnectionStrategy.PipeBody
MepPipeBodyConnectionService.TryCreatePlan
MepPipeBodyConnectionService.TryExecute
```

Require the test to reject any body call guarded by `MoveConnect` or `AlignIn3D`; body execution must be reached only from a strategy that the policy returns exclusively for `MoveConnectAlign`.

- [ ] **Step 2: Run the test and verify it fails**

Run:

```powershell
.\tools\Test-MepAlignment.ps1
```

Expected: FAIL because the runner does not invoke the body service.

- [ ] **Step 3: Compute strategy before opening the transaction**

After `TryFindClosestPair(...)` succeeds, derive:

```csharp
List<Connector> fixedPhysical = MepConnectedClusterCollector
    .GetPhysicalConnectors(fixedElement);
bool fixedHasOpenEnd = fixedPhysical.Any(c =>
    c.ConnectorType == ConnectorType.End && !c.IsConnected);
bool movingSelectedHasOpenEnd = movingElement is Pipe movingPipe &&
    MepConnectedClusterCollector.GetPhysicalConnectors(movingPipe).Any(c =>
        c.ConnectorType == ConnectorType.End && !c.IsConnected);
MepAlignmentConnectionStrategy strategy =
    MepPipeBodyConnectionPolicy.ChooseStrategy(
        mode,
        decision.ShouldConnect,
        fixedElement is Pipe,
        fixedHasOpenEnd,
        movingElement is Pipe,
        movingSelectedHasOpenEnd);
```

When `strategy == PipeBody`, call `TryCreatePlan(document, (Pipe)fixedElement, movingElement, out bodyPlan, out error)` before starting the transaction. If plan creation fails, return the error; do not fall back silently to occupied-connector overlap.

- [ ] **Step 4: Preserve existing connected-cluster safety checks**

Keep `MepConnectedClusterCollector.TryCollect(movingElement, fixedElement, ...)` before model changes for `MoveConnectAlign` and `AlignIn3D`. The resulting `ElementIds` must be passed unchanged to both the existing alignment path and body service.

- [ ] **Step 5: Execute body connection as an early transaction branch**

Immediately after `transaction.Start()` and inside the existing `try`, add:

```csharp
if (strategy == MepAlignmentConnectionStrategy.PipeBody)
{
    if (!MepPipeBodyConnectionService.TryExecute(
            document,
            bodyPlan,
            movingElementIds,
            out FamilyInstance _,
            out error))
    {
        transaction.RollBack();
        return false;
    }

    transaction.Commit();
    return true;
}
```

Do not run `TryAlignConnectorOrientation` or move to an occupied fixed connector after this branch. Leave endpoint and align-only code unchanged except using `strategy == Endpoint` as the final connection gate.

- [ ] **Step 6: Build and run all MEP-focused automated tests**

Run:

```powershell
.\Build-TungMEP2.ps1
.\tools\Test-MepAlignment.ps1
.\tools\Test-DN125-AutoTransition.ps1
```

Expected: all pass; no change to `MoveConn` and `AlignMEP3D` policy assertions.

- [ ] **Step 7: Create the Task 4 source checkpoint**

Copy the updated runner and tests to `<BACKUP>/checkpoint-task4`. Expected: runner diff is limited to strategy selection/body branch and contains no ribbon or unrelated command changes.

---

### Task 5: Tooltip, packaging contracts and full static verification

**Files:**
- Modify: `src/TungMEP2/Core/Application.cs:28`
- Modify: `tools/Test-MepAlignment.ps1`

**Interfaces:**
- Consumes: completed runtime behavior.
- Produces: user-facing ribbon description and final regression coverage.

- [ ] **Step 1: Add a failing tooltip assertion**

Read `Application.cs` as UTF-8 and require the `MC-Align` tooltip text to contain both `đầu hở` and `thân Pipe`.

- [ ] **Step 2: Run the test and verify it fails**

Run:

```powershell
.\tools\Test-MepAlignment.ps1
```

Expected: FAIL because the old tooltip only states that connection is created when two target connectors are open.

- [ ] **Step 3: Update only the MC-Align tooltip**

Use this text:

```csharp
["MC-Align"] = "Căn và di chuyển toàn bộ cụm MEP. Ưu tiên nối hai đầu hở; nếu Pipe mốc kín hai đầu và Pipe thứ hai còn đầu hở, tool chèn Tee/Y vào thân Pipe.",
```

- [ ] **Step 4: Strengthen negative source contracts**

Assert:

- `Command_MoveConnect.cs` and `Command_AlignMep3D.cs` do not reference `MepPipeBodyConnectionService`.
- The service does not contain `DisconnectFrom`, family loading APIs or routing-preference mutation calls.
- The body service gets connector candidates from `plan.BranchPipe`/moving selected Pipe, not by enumerating every element in the moving cluster.
- `PipeUtilities.cs` still contains `TeeMasterWithAutoTransition` and `NewTransitionFitting`.

- [ ] **Step 5: Run complete local automated checks**

Run:

```powershell
.\Build-TungMEP2.ps1
.\tools\Test-MepAlignment.ps1
.\tools\Test-DN125-AutoTransition.ps1
.\tools\Test-PrintSheets.ps1
```

Expected: build and all four regression suites pass. PrintSheets is included as a broad packaging regression even though it is not modified.

- [ ] **Step 6: Scan the implementation against forbidden changes**

Run:

```powershell
rg -n "DisconnectFrom|LoadFamily|RoutingPreferenceManager.*AddRule|RoutingPreferenceManager.*RemoveRule" src\TungMEP2\Core\Internal\Common\MepPipeBodyConnection*.cs
rg -n "MepPipeBodyConnectionService" src\TungMEP2\Core\Commands
```

Expected: both commands return no matches.

- [ ] **Step 7: Create the Task 5 checkpoint**

Copy `Application.cs`, all three new body-connection `.cs` files, the runner and test script into `<BACKUP>/checkpoint-final-source`. Record SHA256 values for these files in a text manifest created with `apply_patch`.

---

### Task 6: Build, install, package and handoff runtime verification

**Files:**
- Regenerate: `build/TungMEP2.dll`
- Regenerate: `release/TungMEP2/net48/TungMEP2.dll`
- Regenerate: `TungMEP2-Revit2024.zip`
- Modify by installer: `%APPDATA%/Autodesk/Revit/Addins/2024/TungMEP2.addin`
- Create: `C:/Users/tungnt2303/Downloads/260515_Release/TungMEP2-MCAlign-PipeBody-20260807.zip`
- Create: `<BACKUP>/FINAL-HASHES.txt`

**Interfaces:**
- Consumes: verified source and build scripts.
- Produces: installed hash-versioned add-in, portable ZIP and explicit Revit 2024 test checklist.

- [ ] **Step 1: Perform the final clean build/package pass**

Run:

```powershell
.\Build-TungMEP2.ps1
.\tools\Test-MepAlignment.ps1
.\tools\Test-DN125-AutoTransition.ps1
.\tools\Test-PrintSheets.ps1
```

Expected: every command exits 0; `TungMEP2-Revit2024.zip` is recreated.

- [ ] **Step 2: Install the new hash-versioned build**

Run:

```powershell
.\release\TungMEP2\Install-TungMEP2.ps1
```

Expected: installer prints the new `%LOCALAPPDATA%\TungMEP2\Revit2024\<12-char-hash>` path and rewrites only `TungMEP2.addin` to that version. It does not overwrite or remove prior hash directories.

- [ ] **Step 3: Verify installed and packaged DLL identity**

Run:

```powershell
$packageDll = 'C:\Users\tungnt2303\Downloads\tungmep2\release\TungMEP2\net48\TungMEP2.dll'
[xml]$manifest = Get-Content "$env:APPDATA\Autodesk\Revit\Addins\2024\TungMEP2.addin"
$installedDll = $manifest.RevitAddIns.AddIn.Assembly
$packageHash = (Get-FileHash -LiteralPath $packageDll -Algorithm SHA256).Hash
$installedHash = (Get-FileHash -LiteralPath $installedDll -Algorithm SHA256).Hash
if ($packageHash -ne $installedHash) { throw 'Installed DLL hash does not match package.' }
$packageHash
$installedDll
```

Expected: hashes are identical and the manifest points to the new hash-versioned directory.

- [ ] **Step 4: Create the portable delivery ZIP without deleting the canonical ZIP**

Run:

```powershell
$sourceZip = 'C:\Users\tungnt2303\Downloads\tungmep2\TungMEP2-Revit2024.zip'
$deliveryZip = 'C:\Users\tungnt2303\Downloads\260515_Release\TungMEP2-MCAlign-PipeBody-20260807.zip'
Copy-Item -LiteralPath $sourceZip -Destination $deliveryZip -Force
Get-FileHash -LiteralPath $deliveryZip -Algorithm SHA256
```

Expected: both ZIP files remain present and share the same hash.

- [ ] **Step 5: Record final hashes using `apply_patch`**

Create `<BACKUP>/FINAL-HASHES.txt` with resolved absolute paths and SHA256 values for:

```text
build\TungMEP2.dll
release\TungMEP2\net48\TungMEP2.dll
installed TungMEP2.dll from the manifest
TungMEP2-Revit2024.zip
TungMEP2-MCAlign-PipeBody-20260807.zip
```

- [ ] **Step 6: Hand off the Revit 2024 acceptance checklist**

If Revit was running during installation, instruct the user to close and reopen it first. Then test exactly:

1. Main open + branch open: select main then branch; endpoint connection succeeds and main is not split.
2. Main closed at both ends + branch open: whole branch cluster moves, branch direction/slope is unchanged, main is split and Tee/Y connects all three paths.
3. Main and branch have different DN: reducing Tee/Y or Tee/Y + transition appears.
4. Branch closed at both ends: cluster align-only behavior remains; main is not split.
5. Projection near a main endpoint: clear error appears and all geometry remains unchanged.
6. Parallel main/branch: clear error appears and all geometry remains unchanged.
7. Main/branch in the same network, or cluster contains Pin/Group: operation is rejected without changes.
8. Press `Esc`: command exits without lingering transaction.

Expected: automated verification is complete; runtime verification remains explicitly marked pending until these Revit model cases are exercised.

---

## Completion Criteria

- Baseline and final automated checks pass.
- Backup, restore instructions, per-task checkpoints and final hashes exist.
- Only MC-Align can choose `PipeBody`; endpoint behavior remains first priority.
- Body connection only uses the open endpoint of the selected moving Pipe.
- Successful Tee/Y has at least three connected physical end connectors.
- Failure paths rollback with no break, partial move, orphan fitting or temporary pipe left behind.
- Package DLL, installed DLL and portable delivery ZIP identities are verified.
- Final report distinguishes automated success from the Revit 2024 manual cases still awaiting user validation.
