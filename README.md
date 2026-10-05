# TungMEP2

TungMEP2 là add-in độc lập cho Autodesk Revit 2024, build bằng .NET Framework 4.8.

## PrintSheets hiện tại — 2026.10.03.1 (03/10/2026)

- Bản độc lập và nút PrintSheets trong TungMEP2 dùng chung engine 2026.10.03.1.
- Giữ giao diện STT thủ công, nhóm nguồn, thứ tự PDF # và in modal của backup
  build 01/08; không đưa lại chế độ vừa in vừa dựng hình.
- Sheet trùng vẫn hiện ở từng nhóm, nhưng chỉ tích một dòng cho mỗi sheet.
  Tích dòng ở nhóm khác sẽ bỏ tích dòng cũ; Chọn tất cả chỉ chọn mỗi sheet một lần.
- Revit/driver in ra Temp local ngắn; chuyển PDF/DWG sang đích bằng Unicode
  Win32 I/O hỗ trợ đường dẫn dài, không thay đổi registry/cấu hình Revit.
- Phiên bản hiển thị trên tiêu đề lệnh; DLL có FileVersion 2026.10.3.1.
- Source engine: `../TungMEP2-PrintSheets/maintenance/2026.10.03.1/src`.
- Kiểm tra engine/UI/dispatch: `tools/Test-PrintSheets20261003.ps1` và
  `../TungMEP2-PrintSheets/maintenance/2026.10.03.1/tools/Test-Maintenance.ps1`.
- `Test-PrintSheets0731.ps1` chỉ dành cho bộ rollback cũ, không dành cho bản mới.
- Giữ nguyên các tool MEP khác. Cần khởi động lại Revit để nạp DLL mới.

## Lịch sử rollback — 2026.07.31.1-STT

- Nút PrintSheets trong TungMEP2 gọi build ngày 01/08/2026, cùng nhãn gốc
  `2026.07.31.1`, đóng gói kèm
  tại `net48/TungMEP2.PrintSheets.dll`; không phụ thuộc thư mục cài bản độc lập.
- SHA-256 engine: `3A854A3351EE2FF00BC9CFFE79CB0E52DFF08FD541023AFDA29433827A866599`.
- DLL gốc ngày 31/07 (hash `7A10E084...`) chỉ có tự đánh STT, thiếu các cột và
  nút thủ công; không dùng DLL đó cho yêu cầu này.
- Có cột `PDF #`, `Nguồn sheet`, `STT`; các nhóm nguồn tách riêng, kể cả sheet
  xuất hiện ở nhiều nguồn. Dòng trùng ở nguồn sau mặc định không tích.
- Giữ thứ tự nhóm đã chọn, cho phép di chuyển nhóm và sheet trong nhóm.
- `Tạo STT theo thứ tự PDF` chỉ tạo giá trị nháp. `Ghi STT vào Sheet` mới ghi
  parameter Text `STT`. Xuất theo `PDF #` và không tự ghi lại STT.
- Bản độc lập và bản tích hợp cần dùng cùng engine này khi được cài đồng thời.
- Giữ nguyên các tool MEP khác; không hạ toàn bộ TungMEP2 về bản tháng 7.
- In bằng hộp thoại modal; không có chế độ vừa in vừa dựng hình của bản tháng 8.
- Mã UI in tích hợp cũ còn lưu để tham khảo nhưng không còn được nút ribbon gọi.
- Kiểm tra đóng gói và binding: `tools/Test-PrintSheets0731.ps1`.
- Installer dùng hash của cả DLL chính và engine in để không ghi đè bản trước
  khi chỉ thay engine. Cần khởi động lại Revit để nạp DLL mới.

## Trạng thái

- Assembly cài đặt: `TungMEP2.dll`.
- Entry point: `TungMEP2.Application`.
- Một Ribbon tab duy nhất tên `TungMEP2`, gồm 7 panel:
  `PLUMBING`, `PCCC`, `VIEW & COORD`, `LAYOUT`, `SHEETS & PRINT`, `MANAGE`, `KHÁC`.
- Không tham chiếu `VP_RevitAPI.dll`; PrintSheets dùng DLL riêng được đóng gói kèm.
- Không có module kích hoạt/license trong binary.
- Không ghi vào WPF `Application.ResourceAssembly`.
- Installer theo user, không cần quyền Administrator.
- 74 icon Ribbon 16×16/32×32 có thể tái tạo bằng `tools\Generate-RibbonIcons.ps1`.

## DirectY (PLUMBING)

`DirectY` nối đầu hở Pipe nhánh vào cổng nhánh hở của fitting Y đã có, không thêm/xóa elbow.

1. Chạy `TungMEP2 → PLUMBING → DirectY`, chọn **Pipe tuyến chính chứa Y/tee**, không chọn fitting.
2. Chọn **Pipe nhánh có đúng một đầu hở**. Đầu còn lại có thể qua con thu/union và các Pipe thẳng trước khi tới elbow.
3. Tool tự tìm Y trên cùng tuyến chính gần thẳng, có cổng nhánh hở cùng DN và gần đầu nhánh nhất; sau đó chỉnh cụm và báo kết quả.

Có thể chọn sẵn một Pipe tuyến chính. Nếu chọn sẵn hai Pipe, tool chỉ tự nhận thứ tự
khi đúng một Pipe có một đầu hở; trường hợp không rõ sẽ yêu cầu chọn lại.
Y/tee phải là fitting 3 cổng với nhánh nghiêng rõ ràng, hai cổng chính đã nối và đúng một cổng nhánh hở.
Không tạo mới Y, không xử lý tee nhánh vuông góc trong phiên bản này.

Tool ưu tiên xoay/trượt một khớp khi đủ. Nếu chưa khớp và Pipe trục phía trước có
elbow thứ hai nối với một Pipe thẳng, tool phối hợp **hai khớp xoay** và chỉnh chiều dài
ba Pipe cục bộ. Cách này cho phép thay đổi cao độ/vị trí nhánh trong phạm vi hình học
của hai elbow hiện có; không dịch chuyển tùy ý cả mạng ống. Y và đầu xa của Pipe ranh giới
đứng yên, các con thu/union trong cụm được giữ lại. Mỗi chiều dài chỉ được chỉnh tối đa 5 m
và không được làm Pipe đảo chiều/quá ngắn.

Nếu thiếu cổng hở, DN khác, có tap/nhánh rẽ trong cụm, nhiều Y không phân biệt được,
Pipe/cút bị Pin hoặc Group, hoặc hình học không thể khớp, tool dừng và không đổi model.
Sau chỉnh, tool kiểm tra lại ID/type, DN, từng cặp connector cũ, kết nối trực tiếp Pipe–Y
và các đầu ngoài phạm vi; sai khác hoặc cảnh báo Revit sẽ rollback toàn bộ.
Hãy thử trước trên bản sao model Revit 2024; kiểm thử tự động hiện chỉ xác minh
phần toán hình học và biên dịch add-in, chưa thay thế thử nghiệm trong Revit.

Bản trước nâng cấp được giữ ở `backups/DirectY-before-flexible-20260930`.
DLL cài theo thư mục hash; cài bản mới khi Revit đang mở không thay DLL đã nạp, cần mở lại Revit.

## MEP alignment nâng cấp ngày 2026-07-29

Năm lệnh mới nằm trực tiếp trong panel `PLUMBING`, được xây dựng độc lập bằng Revit API:

- `MoveConn`: dịch chuyển Pipe/MEP element thứ hai để cặp connector trống gần nhất đã đối hướng gặp nhau và kết nối.
- `MC-Align`: căn hướng rồi di chuyển phần tử thứ hai cùng toàn bộ cụm MEP liên thông. Nếu hai connector mục tiêu đều hở, tool tiếp tục tạo kết nối như bản cũ; nếu một trong hai đã kết nối, tool chỉ căn và không tạo kết nối mới.
- `AlignMEP3D`: căn thẳng hai trục connector trong 3D và di chuyển toàn bộ cụm MEP liên thông, không tạo kết nối mới.
- `AlignBranch`: đưa tim nhánh về giao tim chính trong 3D mà không đổi phương hoặc độ dốc.
- `AlignBranch+`: xoay nhánh vuông góc với tim chính theo mặt bằng, lấy cùng độ dốc rồi đưa hai tim về giao nhau.

Các lệnh chạy liên tục cho đến khi nhấn `Esc`. `MoveConn`, `MC-Align` và `AlignMEP3D`
nhận Pipe, Duct, Conduit, Cable Tray, MEP family và Fabrication Part có connector;
hai lệnh Branch nhận MEP curve thẳng.

Với `MC-Align` và `AlignMEP3D`, chọn phần tử mốc trước rồi chọn phần tử thuộc cụm
cần di chuyển. Các ống, fitting, accessory và MEP family nối vật lý với phần tử thứ hai
được xoay/dịch cùng nhau để giữ kết nối. Nếu hai phần tử đã thuộc cùng mạng, hoặc cụm
có phần tử bị Pin/nằm trong Group, tool dừng trước khi thay đổi model.

Kiểm thử hình học:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-MepAlignment.ps1
```

## PrintSheets nâng cấp ngày 2026-07-29

- Giao diện PrintSheets mới mặc định `920 × 820`, cho phép resize đến tối thiểu `800 × 640`.
- PDF và DWG/CAD nằm trong hai khung màu riêng; mỗi khung có nhãn đường dẫn lớn, mô tả
  loại file và nút `Chọn...` riêng để tránh chọn nhầm output folder.
- Danh sách sheet tự giãn theo kích thước cửa sổ; cột tên sheet dùng toàn bộ phần rộng còn lại.
- Nguồn danh sách sheet hỗ trợ cả Revit Print Set và Sheet List Schedule. Schedule chỉ
  lấy các sheet đang xuất hiện sau filter; danh sách xuất vẫn sắp theo `STT` rồi Sheet Number.
- Có thể chọn đồng thời nhiều Print Set và Sheet List Schedule. Tool hợp nhất các nguồn,
  tự loại sheet trùng và vẫn cho phép bỏ chọn từng sheet trước khi xuất.
- PDF dùng Revit Native PDF Export API, không cần máy in PDF trên Windows.
- `Revit Native PDF` vẫn là công cụ mặc định. Hộp `Công cụ xuất PDF` đồng thời liệt kê
  các máy in PDF ảo được phát hiện trên Windows như Bluebeam PDF, Microsoft Print to PDF,
  PDF-XChange Lite, Foxit PDF Printer, Adobe PDF, CutePDF, doPDF, Nitro và PDF24.
- Khi chọn máy in ảo, hộp thiết lập chuyển từ DPI sang các Revit Print Setup của model.
  Khổ giấy, zoom và cách xử lý vector/raster khi đó do Print Setup và driver máy in quyết định.
- PDF từ máy in ảo được in vào staging local ngắn, kiểm tra chữ ký `%PDF-`, chờ tối đa
  30 giây rồi mới ghi đè an toàn sang PDF Folder. Một số driver vẫn có thể mở hộp thoại Save As riêng.
- Có hai preset: `300 DPI - Fast` mặc định và `600 DPI - Quality`.
- Có hai chế độ xử lý PDF: `Vector` cho nét sắc/file nhẹ và `Font-safe Raster` để ưu tiên
  giữ hình dáng chữ khi Revit Native PDF gặp lỗi font. Raster làm file lớn hơn và chữ khó tìm kiếm/copy hơn.
- PDF luôn dùng Color, Fit to Page và tự nhận khổ giấy/hướng giấy.
- Trước khi Native PDF chạy, tool tạm xóa Revit selection và refresh active view để
  highlight xanh/hồng không lọt vào PDF; selection được khôi phục sau khi xuất.
- Hỗ trợ từng PDF hoặc Combine thành một PDF theo đúng thứ tự sheet.
- Chế độ PDF riêng lẻ export theo batch tối đa 10 sheet trong mỗi API call để cân bằng tốc độ và độ phản hồi.
- Nếu batch lỗi hoặc thiếu file, tool tự chia đôi phần còn thiếu đến khi xác định được sheet lỗi.
- PDF batch và DWG đều dùng staging folder local ngắn trong `%TEMP%`, chỉ chuyển file hợp lệ
  vào output folder rồi tự dọn staging.
- Danh sách file đã có được cache một lần; DWG Export Setup được tái sử dụng cho cả lượt export.
- DWG dùng nguyên toàn bộ options của setup đã chọn; TungMEP2 không còn ép `MergedViews`.
- DWG được Revit xuất vào vùng local ngắn trước, sau đó TungMEP2 chép DWG cùng toàn bộ
  JPG/PCP/Xref phụ sang thư mục CAD đã chọn và xác minh kích thước file. Cơ chế này
  tránh lỗi RealDWG bỏ title block/raster khi xuất thẳng tới đường dẫn mạng dài.
- PDF và DWG có hai ô output folder độc lập; PDF combine đi vào PDF Folder, CAD đi vào DWG Folder.
- Trong lúc xuất, nút Export đổi thành `Tạm dừng`/`Tiếp tục`; nút Close đổi thành `Ngưng xuất`.
- Tạm dừng hoặc ngưng được xử lý an toàn giữa các batch PDF và giữa từng sheet DWG.
- Cửa sổ PrintSheets tự đóng sau khi hoàn thành hoặc sau khi đã ngưng; output folder vẫn tự mở nếu đã tạo được file.
- Sheet được sắp theo parameter `STT`, sau đó theo Sheet Number dạng natural sort.
- PDF, DWG và sidecar phụ tự động ghi đè đúng tên nếu file cũ đã tồn tại; file mới
  được xác minh trong staging trước khi thay thế để không làm mất file cũ khi export lỗi.
- File tạm dùng tên ngắn cố định, không lặp lại tên DWG/PDF nên không làm đường dẫn mạng
  vượt giới hạn 260 ký tự trong lúc ghi đè.
- Tên file được làm sạch và giới hạn theo độ dài thực tế của output folder. Tên cuối được
  giữ nguyên khi hợp lệ; chỉ rút gọn kèm mã hash khi chính đường dẫn file cuối vượt giới hạn Windows.
- Selection không còn bị xóa khi chạy lệnh.
- Selection không hợp lệ sẽ chuyển về chế độ chọn Print Set thay vì dừng command.
- Output folder mặc định: `Desktop\TungMEP2\PrintSheets`; state cũ được tự dùng để khởi tạo cả hai đường dẫn.
- Nhấp đúp hoặc nhấp phải vào từng ô PDF Folder/DWG Folder để chọn thư mục riêng.
- State được lưu riêng tại `%LOCALAPPDATA%\TungMEP2\State\PrintSheets`.
- Chức năng đổi tên PDF legacy yêu cầu xác nhận và không ghi đè file.

## Build và kiểm tra

```powershell
.\Build-TungMEP2.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-PrintSheets.ps1
```

Build tạo package tại `release\TungMEP2`.

## Cài đặt

Chạy:

```text
release\TungMEP2\Install-TungMEP2.cmd
```

Installer cài vào `%LOCALAPPDATA%\TungMEP2\Revit2024\<build-hash>` và tạo manifest tại `%APPDATA%\Autodesk\Revit\Addins\2024\TungMEP2.addin`.

## Backup và rollback PrintSheets

Snapshot trước khi nâng cấp:

```text
backups\PrintSheets-before-upgrade-20260729-081238
```

Khôi phục source:

```powershell
.\backups\PrintSheets-before-upgrade-20260729-081238\Restore-PrintSheetsBackup.ps1
```

Khôi phục cả source và package cũ:

```powershell
.\backups\PrintSheets-before-upgrade-20260729-081238\Restore-PrintSheetsBackup.ps1 -RestoreRelease
```

Đóng Revit trước khi restore hoặc cài lại package cũ.

Checkpoint sau Native PDF và trước tối ưu batch:

```text
backups\PrintSheets-before-batch-performance-20260729-083917
```

Khôi phục checkpoint này:

```powershell
.\backups\PrintSheets-before-batch-performance-20260729-083917\Restore-PrintSheetsBackup.ps1 -RestoreRelease
```

Checkpoint trước khi thêm tạm dừng, ngưng xuất và tự đóng:

```text
backups\PrintSheets-before-pause-cancel-20260729-091217
```

Khôi phục checkpoint này:

```powershell
.\backups\PrintSheets-before-pause-cancel-20260729-091217\Restore-PrintSheetsBackup.ps1 -RestoreRelease
```

Checkpoint trước khi tách PDF/DWG folder và bỏ ghi đè `MergedViews`:

```text
backups\PrintSheets-before-separated-paths-dwg-fix-20260729-101240
```

Khôi phục checkpoint này:

```powershell
.\backups\PrintSheets-before-separated-paths-dwg-fix-20260729-101240\Restore-PrintSheetsBackup.ps1 -RestoreRelease
```

Checkpoint trước khi thêm vùng local staging cho DWG:

```text
backups\PrintSheets-before-local-dwg-staging-20260729-113759
```

Checkpoint trước khi sửa selection highlight lọt vào PDF:

```text
backups\PrintSheets-before-selection-highlight-fix-20260729-120937
```

Checkpoint trước khi bật tự động ghi đè file cùng tên:

```text
backups\PrintSheets-before-overwrite-existing-20260729-140053
```

Checkpoint trước khi sửa đường dẫn file tạm ghi đè và chuyển PDF batch về local staging:

```text
backups\PrintSheets-before-short-replacement-path-20260729-141116
```

Checkpoint trước khi thiết kế lại giao diện PrintSheets:

```text
backups\PrintSheets-before-ui-redesign-20260729-142009
```

Checkpoint trước khi đồng bộ nguồn Sheet List Schedule:

```text
backups\PrintSheets-before-sheet-schedule-sources-20260729-150232
```

Checkpoint trước khi thêm Font-safe PDF và chọn nhiều nguồn:

```text
backups\PrintSheets-before-font-safe-multi-source-20260729-165422
```

Checkpoint trước khi bổ sung lại máy in PDF ảo:

```text
backups\PrintSheets-before-virtual-pdf-printers-20260729-173436
```

Checkpoint trước khi thêm bộ lệnh MEP alignment:

```text
backups\Before-NaviateLT-style-tools-20260729-183505
```

## Lưu ý kiểm thử

Project đã được build và kiểm tra tĩnh. Smoke-test cục bộ kiểm tra đặt tên file,
ghi đè an toàn từ staging, rename legacy và đường dẫn state mà không mở Revit.
Cần chạy thử PrintSheets trên một file Revit test trước khi dùng với dự án thật.
