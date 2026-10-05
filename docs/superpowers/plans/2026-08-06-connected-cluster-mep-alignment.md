# Connected-Cluster MEP Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cho phép `MC-Align` và `AlignMEP3D` căn chỉnh phần tử MEP đã nối một hoặc hai đầu bằng cách xoay/dịch toàn bộ cụm liên thông, không phá các kết nối hiện hữu.

**Architecture:** Tách chính sách chọn connector thành một helper thuần dữ liệu có thể kiểm thử tự động; tách việc duyệt graph connector và kiểm tra an toàn thành một collector riêng. `MepConnectorAlignmentRunner` dùng hai thành phần này, biến đổi toàn bộ `ElementId` của cụm trong một transaction và chỉ tạo kết nối mới khi cặp connector mục tiêu đều đang hở.

**Tech Stack:** C# 12, .NET Framework 4.8, Autodesk Revit API 2024, PowerShell smoke tests, WPF/Nice3point command infrastructure hiện có.

## Global Constraints

- Không sửa, xóa hoặc ghi đè backup đã tạo.
- Backup đầy đủ mã nguồn liên quan, DLL, ZIP/release và manifest đang cài trước mọi thay đổi production.
- Phần tử thứ nhất là mốc; phần tử thứ hai xác định cụm di chuyển.
- Nếu phần tử mốc thuộc cụm di chuyển, dừng và không thay đổi model.
- Không tự ngắt connector, không thay fitting và không thay Routing Preferences.
- `MC-Align` chỉ tạo kết nối mới khi cả hai connector mục tiêu đang hở.
- `AlignMEP3D` không tạo kết nối mới.
- Mọi biến đổi dùng một transaction; lỗi phải rollback toàn bộ.
- Giữ nguyên hành vi cũ của `MoveConn` và trường hợp `MC-Align` có hai connector hở.
- Thư mục `C:\Users\tungnt2303\Downloads\tungmep2` không phải Git repository; thay bước commit bằng snapshot/checksum có dấu thời gian trong thư mục backup.

## File Structure

- Create: `src/TungMEP2/Core/Internal/Common/MepConnectorAlignmentPolicy.cs` — chọn cặp connector và quyết định có được kết nối mới hay không.
- Create: `src/TungMEP2/Core/Internal/Common/MepConnectedClusterCollector.cs` — duyệt graph connector vật lý, thu thập cụm và kiểm tra Pin/Group/cùng mạng.
- Modify: `src/TungMEP2/Core/Internal/Common/MepConnectorAlignmentRunner.cs` — tích hợp policy/collector và biến đổi nhiều phần tử.
- Modify: `tools/Test-MepAlignment.ps1` — kiểm thử policy, contract collector và contract tích hợp runner.
- Modify: `src/TungMEP2/Core/Application.cs` — mô tả ribbon đúng hành vi mới.
- Modify: `README.md` và `release/TungMEP2/README.txt` — hướng dẫn người dùng và giới hạn an toàn.

---

### Task 1: Backup có thể khôi phục và baseline xanh

**Files:**
- Create: `C:/Users/tungnt2303/Downloads/260515_Release/backups/Before-Connected-Cluster-Alignment-20260806-113104/RESTORE.txt`
- Copy: các file nguồn, test, build/release và manifest hiện hành vào cùng thư mục backup.

**Interfaces:**
- Consumes: mã nguồn và bản cài TungMEP2 hiện tại.
- Produces: backup bất biến, file checksum và hướng dẫn restore trước khi sửa mã.

- [ ] **Step 1: Chạy baseline build và test**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Users\tungnt2303\Downloads\tungmep2\Build-TungMEP2.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Users\tungnt2303\Downloads\tungmep2\tools\Test-MepAlignment.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Users\tungnt2303\Downloads\tungmep2\tools\Test-PrintSheets.ps1"
```

Expected: build `0 Error(s)`; hai test báo `passed`.

- [ ] **Step 2: Tạo backup có dấu thời gian**

Tạo chính xác thư mục:

```text
C:\Users\tungnt2303\Downloads\260515_Release\backups\Before-Connected-Cluster-Alignment-20260806-113104
```

Copy nguyên trạng:

```text
src\TungMEP2\Core\Internal\Common\MepConnectorAlignmentRunner.cs
src\TungMEP2\Core\Commands\Common\Command_MoveConnectAlign.cs
src\TungMEP2\Core\Commands\Common\Command_AlignMep3D.cs
src\TungMEP2\Core\Application.cs
tools\Test-MepAlignment.ps1
README.md
build\TungMEP2.dll
release\TungMEP2
TungMEP2-Revit2024.zip
%APPDATA%\Autodesk\Revit\Addins\2024\TungMEP2.addin
```

Đọc đường dẫn `<Assembly>` từ manifest và copy cả thư mục build đang cài dưới `%LOCALAPPDATA%\TungMEP2\Revit2024`.

- [ ] **Step 3: Ghi checksum và hướng dẫn restore**

Tạo `SHA256.txt` bằng `Get-FileHash -Algorithm SHA256` cho toàn bộ file backup. Ghi `RESTORE.txt` với đúng các bước:

```text
1. Đóng Revit 2024.
2. Copy lại các file source từ thư mục source-backup về C:\Users\tungnt2303\Downloads\tungmep2.
3. Copy TungMEP2.addin về %APPDATA%\Autodesk\Revit\Addins\2024.
4. Không cần xóa build mới; manifest cũ sẽ trỏ lại thư mục DLL đã backup.
5. Mở Revit 2024 và kiểm tra MC-Align/AlignMEP3D.
```

- [ ] **Step 4: Xác nhận backup độc lập với source**

Run `Get-FileHash` trên source và bản copy; expected hash từng cặp giống nhau. Ghi đường dẫn backup vào `backup-path.txt` trong `C:\Users\tungnt2303\Downloads\260515_Release` để các task sau không phải đoán timestamp.

---

### Task 2: Chính sách chọn connector — TDD

**Files:**
- Create: `src/TungMEP2/Core/Internal/Common/MepConnectorAlignmentPolicy.cs`
- Modify: `tools/Test-MepAlignment.ps1`

**Interfaces:**
- Consumes: danh sách candidate đã được runner tạo từ connector thật.
- Produces:
  - `MepConnectorPairCandidate(int FixedIndex, int MovingIndex, bool FixedIsConnected, bool MovingIsConnected, bool IsDomainCompatible, bool IsDirectionCompatible, double Distance)`
  - `MepConnectorPairDecision(int FixedIndex, int MovingIndex, bool ShouldConnect, bool UsesConnectedFallback)`
  - `MepConnectorAlignmentPolicy.TryChoosePair(IReadOnlyList<MepConnectorPairCandidate>, bool requireDirectionCompatibility, bool allowConnectedFallback, out MepConnectorPairDecision)`

- [ ] **Step 1: Viết failing tests cho ưu tiên connector hở**

Trong `Test-MepAlignment.ps1`, load hai type bằng reflection, tạo typed array `MepConnectorPairCandidate[]` và kiểm tra:

```powershell
# Candidate 0 gần hơn nhưng đã kết nối; candidate 1 xa hơn và cả hai đầu hở.
# Expected: chọn candidate 1, ShouldConnect=True, UsesConnectedFallback=False.
$arguments = [object[]]@($candidates, $false, $true, $null)
$success = [bool]$choosePair.Invoke($null, $arguments)
if (-not $success) { throw 'Open pair should be selected.' }
$decision = $arguments[3]
if ($decision.FixedIndex -ne 1 -or -not $decision.ShouldConnect) {
    throw 'Open-open pair must win over a closer occupied pair.'
}
```

Thêm hai case:

```text
Chỉ có pair đã kết nối + allowConnectedFallback=True  => success, ShouldConnect=False.
Chỉ có pair đã kết nối + allowConnectedFallback=False => false.
```

- [ ] **Step 2: Chạy test và xác nhận RED**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Users\tungnt2303\Downloads\tungmep2\tools\Test-MepAlignment.ps1"
```

Expected: FAIL vì type `MepConnectorAlignmentPolicy` chưa tồn tại.

- [ ] **Step 3: Viết implementation tối thiểu**

Tạo `MepConnectorAlignmentPolicy.cs` với thuật toán:

```csharp
IEnumerable<MepConnectorPairCandidate> eligible = candidates
    .Where(candidate => candidate.IsDomainCompatible)
    .Where(candidate => !requireDirectionCompatibility || candidate.IsDirectionCompatible);

MepConnectorPairCandidate? openChoice = eligible
    .Where(candidate => !candidate.FixedIsConnected && !candidate.MovingIsConnected)
    .OrderBy(candidate => candidate.Distance)
    .Select(candidate => (MepConnectorPairCandidate?)candidate)
    .FirstOrDefault();

if (openChoice.HasValue)
{
    decision = new MepConnectorPairDecision(
        openChoice.Value.FixedIndex,
        openChoice.Value.MovingIndex,
        ShouldConnect: true,
        UsesConnectedFallback: false);
    return true;
}

if (!allowConnectedFallback)
{
    decision = default;
    return false;
}

MepConnectorPairCandidate? fallback = eligible
    .OrderBy(candidate => candidate.Distance)
    .Select(candidate => (MepConnectorPairCandidate?)candidate)
    .FirstOrDefault();
```

Nếu có fallback, trả decision với `ShouldConnect: false`, `UsesConnectedFallback: true`; nếu không có candidate phù hợp, trả `false`.

- [ ] **Step 4: Build và xác nhận GREEN**

Run build rồi chạy `Test-MepAlignment.ps1`.

Expected: ba case policy PASS; build `0 Error(s)`.

- [ ] **Step 5: Tạo checkpoint**

Copy hai file Task 2 vào `C:\Users\tungnt2303\Downloads\260515_Release\backups\Before-Connected-Cluster-Alignment-20260806-113104\checkpoints\Task-2-policy-green` và ghi SHA256. Không ghi đè baseline backup.

---

### Task 3: Collector cho cụm MEP liên thông — TDD contract

**Files:**
- Create: `src/TungMEP2/Core/Internal/Common/MepConnectedClusterCollector.cs`
- Modify: `tools/Test-MepAlignment.ps1`

**Interfaces:**
- Consumes: `Element movingElement`, `Element fixedElement`.
- Produces:
  - `MepConnectedCluster(IReadOnlyList<Element> Elements, ICollection<ElementId> ElementIds)`
  - `MepConnectedClusterCollector.TryCollect(Element movingElement, Element fixedElement, out MepConnectedCluster cluster, out string error)`
  - `MepConnectedClusterCollector.GetPhysicalConnectors(Element element)` dùng chung với runner.

- [ ] **Step 1: Viết failing contract tests**

Mở rộng `Test-MepAlignment.ps1` để kiểm tra bằng reflection:

```powershell
$collectorType = $assembly.GetType(
    'VP_RevitAPI.Internal.Common.MepConnectedClusterCollector',
    $false)
if ($null -eq $collectorType) {
    throw 'Connected-cluster collector is missing.'
}
foreach ($methodName in @('TryCollect', 'GetPhysicalConnectors')) {
    if ($null -eq $collectorType.GetMethod(
        $methodName,
        [Reflection.BindingFlags]'Static,NonPublic')) {
        throw "Collector method $methodName is missing."
    }
}
```

Đọc source và assert có các guard thực tế: `connector.AllRefs`, `connector.IsConnectedTo(reference)`, `element.Pinned`, `element.GroupId`, và thông báo `cùng mạng liên thông`.

- [ ] **Step 2: Chạy test và xác nhận RED**

Expected: FAIL `Connected-cluster collector is missing.`

- [ ] **Step 3: Viết collector tối thiểu**

Implement BFS:

```csharp
Queue<Element> queue = new Queue<Element>();
Dictionary<long, Element> visited = new Dictionary<long, Element>();
queue.Enqueue(movingElement);

while (queue.Count > 0)
{
    Element element = queue.Dequeue();
    long id = element.Id.Value;
    if (visited.ContainsKey(id))
        continue;
    if (element.Id == fixedElement.Id)
    {
        error = "Hai phần tử thuộc cùng mạng liên thông; không thể di chuyển cả cụm.";
        return false;
    }
    visited.Add(id, element);
    foreach (Connector connector in GetPhysicalConnectors(element))
    foreach (Connector reference in connector.AllRefs.Cast<Connector>())
    {
        if (reference == null || reference.ConnectorType == ConnectorType.Logical)
            continue;
        if (!connector.IsConnectedTo(reference) || reference.Owner == null)
            continue;
        if (reference.Owner.Id != element.Id)
            queue.Enqueue(reference.Owner);
    }
}
```

Sau khi BFS hoàn tất, validate từng element:

```csharp
if (element.Pinned)
    error = $"Phần tử {element.Id.Value} đang bị Pin.";
if (element.GroupId != ElementId.InvalidElementId)
    error = $"Phần tử {element.Id.Value} đang nằm trong Group.";
if (!(element is MEPCurve) && !(element is FamilyInstance) && !(element is FabricationPart))
    error = $"Phần tử {element.Id.Value} không thể di chuyển an toàn cùng cụm MEP.";
```

Mọi lỗi trả `false` và `cluster = null`; chưa mở transaction, chưa sửa model.

- [ ] **Step 4: Build và xác nhận GREEN**

Run build + `Test-MepAlignment.ps1`.

Expected: collector contract PASS; policy tests vẫn PASS.

- [ ] **Step 5: Tạo checkpoint**

Copy collector và test vào `C:\Users\tungnt2303\Downloads\260515_Release\backups\Before-Connected-Cluster-Alignment-20260806-113104\checkpoints\Task-3-cluster-green`; ghi SHA256.

---

### Task 4: Tích hợp biến đổi cả cụm vào runner — TDD

**Files:**
- Modify: `src/TungMEP2/Core/Internal/Common/MepConnectorAlignmentRunner.cs`
- Modify: `tools/Test-MepAlignment.ps1`

**Interfaces:**
- Consumes: `MepConnectorAlignmentPolicy.TryChoosePair`, `MepConnectedClusterCollector.TryCollect`.
- Produces: hành vi mới cho `MoveConnectAlign` và `AlignIn3D`; không đổi contract command/ribbon.

- [ ] **Step 1: Viết failing integration contract tests**

Đọc source runner và assert:

```powershell
foreach ($required in @(
    'MepConnectorAlignmentPolicy.TryChoosePair',
    'MepConnectedClusterCollector.TryCollect',
    'ElementTransformUtils.RotateElements',
    'ElementTransformUtils.MoveElements',
    'decision.ShouldConnect')) {
    if (-not $runnerSource.Contains($required)) {
        throw "Runner integration is missing: $required"
    }
}
```

Assert `FindMatchingConnector` lấy connector vật lý bất kể trạng thái kết nối, bằng contract source `GetPhysicalConnectors(owner)` thay cho `GetConnectors(owner, openOnly: true)`.

- [ ] **Step 2: Chạy test và xác nhận RED**

Expected: FAIL ở assertion tích hợp đầu tiên.

- [ ] **Step 3: Thay logic chọn pair**

Trong `TryFindClosestPair`:

```csharp
List<Connector> fixedConnectors = MepConnectedClusterCollector
    .GetPhysicalConnectors(fixedElement);
List<Connector> movingConnectors = MepConnectedClusterCollector
    .GetPhysicalConnectors(movingElement);

bool allowConnectedFallback = mode == MepConnectorAlignmentMode.MoveConnectAlign ||
                              mode == MepConnectorAlignmentMode.AlignIn3D;
```

Tạo candidate cho mọi tổ hợp, gọi policy, map `FixedIndex/MovingIndex` về connector thật và trả kèm `MepConnectorPairDecision`.

`MoveConnect` truyền `allowConnectedFallback: false` để giữ hành vi cũ.

- [ ] **Step 4: Thu thập cụm trước transaction**

Trong `TryExecuteOnce`:

```csharp
ICollection<ElementId> movingIds;
if (mode == MepConnectorAlignmentMode.MoveConnect)
{
    movingIds = new List<ElementId> { movingElement.Id };
}
else if (!MepConnectedClusterCollector.TryCollect(
             movingElement,
             fixedElement,
             out MepConnectedCluster cluster,
             out error))
{
    return false;
}
else
{
    movingIds = cluster.ElementIds;
}
```

Không mở transaction nếu collector thất bại.

- [ ] **Step 5: Xoay và dịch toàn cụm**

Đổi signature:

```csharp
TryAlignConnectorOrientation(
    Document document,
    ICollection<ElementId> movingElementIds,
    Element movingConnectorOwner,
    Connector fixedConnector,
    Connector originalMovingConnector,
    out Connector alignedMovingConnector,
    out string error)
```

Thay `RotateElement` bằng:

```csharp
ElementTransformUtils.RotateElements(document, movingElementIds, rotationAxis, angle);
```

Thay move đơn bằng:

```csharp
ElementTransformUtils.MoveElements(document, movingIds, moveVector);
```

Sau rotate/move, gọi `document.Regenerate()` và tìm lại connector từ `movingElement` bằng toàn bộ connector vật lý, không lọc `IsConnected`.

- [ ] **Step 6: Chỉ connect khi policy cho phép**

Thay khối connect bằng điều kiện:

```csharp
bool shouldCreateConnection =
    mode != MepConnectorAlignmentMode.AlignIn3D &&
    decision.ShouldConnect;

if (shouldCreateConnection)
{
    Connect(document, fixedConnector, movingConnector);
}
```

Vẫn kiểm tra origin/hướng sau biến đổi. Khi fallback dùng connector đã kết nối, hai connector được căn và đưa về vị trí theo mode nhưng không gọi `ConnectTo`/`ConnectAndCouple`.

- [ ] **Step 7: Build và xác nhận GREEN**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Users\tungnt2303\Downloads\tungmep2\Build-TungMEP2.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Users\tungnt2303\Downloads\tungmep2\tools\Test-MepAlignment.ps1"
```

Expected: build `0 Error(s)`, toàn bộ policy/collector/integration tests PASS.

- [ ] **Step 8: Tạo checkpoint**

Copy runner, hai helper mới và test vào `C:\Users\tungnt2303\Downloads\260515_Release\backups\Before-Connected-Cluster-Alignment-20260806-113104\checkpoints\Task-4-runner-green`; ghi SHA256.

---

### Task 5: Mô tả sử dụng và thông báo rõ hành vi

**Files:**
- Modify: `src/TungMEP2/Core/Application.cs`
- Modify: `README.md`
- Modify: `release/TungMEP2/README.txt`
- Modify: `tools/Test-MepAlignment.ps1`

**Interfaces:**
- Consumes: hành vi Task 4.
- Produces: tooltip/README thống nhất với tool thực tế.

- [ ] **Step 1: Viết failing documentation assertions**

Trong test, đọc ba file và assert nội dung có đủ cụm từ:

```text
di chuyển cả cụm liên thông
không tạo kết nối mới
hai phần tử cùng mạng
```

- [ ] **Step 2: Chạy test và xác nhận RED**

Expected: FAIL vì tooltip/README hiện vẫn mô tả “connector trống”.

- [ ] **Step 3: Cập nhật nội dung**

Tooltip `MC-Align`:

```text
Căn hướng và di chuyển phần tử thứ hai cùng toàn bộ cụm MEP liên thông. Chỉ tạo kết nối mới khi hai connector mục tiêu đều đang hở.
```

Tooltip `AlignMEP3D`:

```text
Căn thẳng trục connector trong 3D và di chuyển toàn bộ cụm MEP liên thông; không tạo kết nối mới.
```

README ghi rõ thứ tự chọn: chọn phần tử mốc trước, chọn phần tử/cụm cần di chuyển sau; cùng mạng hoặc có phần tử Pin/Group thì lệnh dừng.

- [ ] **Step 4: Chạy test và xác nhận GREEN**

Run build + `Test-MepAlignment.ps1` + `Test-PrintSheets.ps1`.

Expected: tất cả PASS, không có warning/error mới.

- [ ] **Step 5: Tạo checkpoint**

Copy tài liệu, Application.cs và test vào `C:\Users\tungnt2303\Downloads\260515_Release\backups\Before-Connected-Cluster-Alignment-20260806-113104\checkpoints\Task-5-docs-green`; ghi SHA256.

---

### Task 6: Đóng gói, cài đặt và nghiệm thu Revit 2024

**Files:**
- Generate: `build/TungMEP2.dll`
- Generate: `release/TungMEP2/net48/TungMEP2.dll`
- Generate: `TungMEP2-Revit2024.zip`
- Copy: `C:/Users/tungnt2303/Downloads/260515_Release/TungMEP2-ConnectedCluster-20260806.zip`

**Interfaces:**
- Consumes: mã đã xanh ở Task 5 và baseline backup Task 1.
- Produces: DLL đã cài theo build hash, ZIP chia sẻ và kết quả nghiệm thu.

- [ ] **Step 1: Chạy verification đầy đủ**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Users\tungnt2303\Downloads\tungmep2\Build-TungMEP2.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Users\tungnt2303\Downloads\tungmep2\tools\Test-MepAlignment.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Users\tungnt2303\Downloads\tungmep2\tools\Test-DN125-AutoTransition.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Users\tungnt2303\Downloads\tungmep2\tools\Test-PrintSheets.ps1"
```

Expected: build `0 Error(s)`; bốn test PASS.

- [ ] **Step 2: Kiểm tra DLL/package**

So sánh SHA256 của `build\TungMEP2.dll` và `release\TungMEP2\net48\TungMEP2.dll`; expected giống nhau. Kiểm tra ZIP chứa DLL, Store và installer scripts hiện hành.

- [ ] **Step 3: Cài bản mới**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Users\tungnt2303\Downloads\tungmep2\release\TungMEP2\Install-TungMEP2.ps1"
```

Đọc manifest sau cài và xác nhận đường dẫn DLL có build ID bằng 12 ký tự đầu của SHA256 DLL release. Không đóng Revit tự động; nếu Revit đang mở, bản mới có hiệu lực sau khi mở lại.

- [ ] **Step 4: Tạo ZIP giao nhận**

Copy `TungMEP2-Revit2024.zip` thành:

```text
C:\Users\tungnt2303\Downloads\260515_Release\TungMEP2-ConnectedCluster-20260806.zip
```

Ghi SHA256 và dung lượng vào `TungMEP2-ConnectedCluster-20260806-SHA256.txt`.

- [ ] **Step 5: Nghiệm thu trong Revit**

Chạy đúng ma trận:

```text
A. MC-Align, hai đầu hở: nối như bản cũ.
B. MC-Align, cụm di chuyển nối một đầu: cả cụm di chuyển; đầu hở được nối.
C. MC-Align, cụm kín hai đầu: cả cụm di chuyển; không có kết nối mới.
D. AlignMEP3D, cụm có pipe + elbow + tee: toàn cụm căn 3D, connector giữ nguyên.
E. Hai phần tử cùng mạng: báo lỗi, model không đổi.
F. Cụm có phần tử Pin/Group: báo phần tử gây cản, model không đổi.
G. Khác domain: báo không có cặp connector tương thích.
```

Sau mỗi case, Undo một lần phải hoàn tác toàn bộ thao tác thành công; case lỗi không tạo Undo item sửa model.

- [ ] **Step 6: Kiểm tra khả năng restore**

Không thực hiện restore thật sau nghiệm thu, nhưng xác nhận tất cả đường dẫn trong `RESTORE.txt` còn tồn tại và hash baseline vẫn đúng với `SHA256.txt`.
