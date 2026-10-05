# FitSpace: chuẩn hóa khoảng cách cụm hai elbow

Ngày: 2026-09-29

## Mục tiêu

Bổ sung lệnh `FitSpace` vào panel `PLUMBING` của TungMEP2 cho Revit 2024. Lệnh tự động chỉnh một hoặc nhiều cụm hiện hữu có cấu trúc:

`Pipe ngoài 1 – Elbow 1 – Pipe giữa – Elbow 2 – Pipe ngoài 2`

Hai elbow và ba Pipe được giữ nguyên ElementId, type, DN và system. Tool chỉ bố trí lại hai elbow và đầu gần của ba Pipe để cụm có tỷ lệ giống kết quả do `E90to45` tạo ra, không yêu cầu người dùng chuyển cụm về elbow 90° rồi chạy lại `E90to45`.

## Phạm vi phiên bản đầu

- Hỗ trợ Pipe thẳng và đúng hai elbow hai-port ở hai đầu Pipe giữa.
- Hỗ trợ cụm nằm ngang hoặc có slope/không gian 3D nếu ba trục Pipe đồng phẳng trong dung sai hình học.
- Cho phép preselection Pipe giữa, một hoặc hai elbow, hoặc chọn sau khi gọi lệnh.
- Cho phép chọn nhiều cụm độc lập trong một lần chạy; mỗi cụm được phân tích và xử lý riêng.
- Không xử lý Tee, Wye, Cross, tap, reducer, coupling, transition, pipe accessory, equipment, plumbing fixture hoặc chuỗi nhiều hơn hai elbow trong phiên bản đầu.
- Không tự đổi routing preference, family, type, DN, slope parameter hoặc piping system.

## Nhận diện cụm

1. Từ phần tử được chọn, tìm Pipe có đúng hai đầu nối lần lượt tới hai `OST_PipeFitting`.
2. Mỗi fitting phải có đúng hai connector vật lý trong domain piping và phải là elbow theo PartType/family geometry, không chỉ dựa vào tên family.
3. Mỗi elbow phải nối với Pipe giữa và đúng một Pipe ngoài khác.
4. Ba Pipe phải có `LocationCurve` là `Line`; các connector đang nối phải trùng vị trí trong dung sai Revit.
5. Hai Pipe ngoài không được là cùng một ElementId. Pipe giữa và hai elbow không được thuộc nhiều cụm được xử lý trong cùng lượt.
6. Hai trục Pipe ngoài kéo dài phải có một giao điểm ảo hoặc một cặp điểm gần nhất đủ nhỏ để xác định góc tổng thể.
7. Trục Pipe giữa phải phù hợp với hướng hai connector phía trong của hai elbow. Cụm méo hoặc không đồng phẳng bị loại trước transaction.

Các selection trùng một cụm được hợp nhất. Hai cụm dùng chung Pipe/fitting bị đánh dấu xung đột và không được sửa hai lần.

## Hình học tham chiếu

### Giao điểm ảo và tâm elbow

- Gọi `A` là giao điểm ảo của hai trục Pipe ngoài.
- Tâm hình học của một elbow được tính bằng giao điểm của hai đường thẳng đi qua hai connector của fitting theo trục `BasisZ` tương ứng. Tâm này tương đương điểm giao của hai tim Pipe trước khi Revit chèn elbow.
- Gọi `B` và `C` là tâm đích của Elbow 1 và Elbow 2 trên hai tia Pipe ngoài đi ra từ `A`.
- Chiều các tia được xác định từ `A` về phía đầu xa bất động của từng Pipe ngoài; tool không cho phép tâm elbow vượt qua `A` hoặc vượt qua đầu xa.

### Khoảng cách mục tiêu

Phiên bản đầu dùng cùng quy tắc thực hành hiện có trong `PipeUtilities.Elbow90To45` và cùng bảng `ElbowData`:

`ReferenceElbowLength = max(ElbowData theo DN của ba Pipe)`

`RawMiddleLength = 2 × ReferenceElbowLength × 1.10`

Gọi `θ` là góc nhỏ hơn giữa hai tia Pipe ngoài hướng từ `A` về hai đầu xa, yêu cầu `15° ≤ θ ≤ 165°`. Với hai elbow có orientation tương thích và cùng chia góc đổi hướng, khoảng đặt tâm là:

`Standoff(A,B) = Standoff(A,C) = RawMiddleLength / (2 × sin(θ/2))`

Khi `θ = 90°`, công thức trở về đúng quy tắc case study:

`Standoff(A,B) = Standoff(A,C) = RawMiddleLength / √2`

Đối với cụm 3D, phép tính được thực hiện trong mặt phẳng chung của ba Pipe. Target chỉ hợp lệ khi:

- tâm mỗi elbow nằm trên trục Pipe ngoài tương ứng;
- đường nối hai connector phía trong cùng phương với trục Pipe giữa ban đầu;
- khoảng cách tâm `B–C` bằng `RawMiddleLength` trong dung sai;
- hai elbow dịch cân bằng quanh giao điểm ảo;
- các trục connector sau khi chỉ tịnh tiến vẫn trùng với ba trục Pipe mục tiêu, tức là không cần xoay fitting.

Nếu hệ phương trình không có nghiệm ổn định, yêu cầu xoay fitting, làm đoạn Pipe ngắn hơn `max(2 × ShortCurveTolerance, 5 mm)` hoặc làm elbow vượt ra ngoài đoạn Pipe ngoài hiện hữu, cụm bị bỏ qua thay vì dựng thêm offset.

## Phần tử bất động và biến đổi

- Đầu xa của hai Pipe ngoài và toàn bộ mạng phía sau chúng là bất động.
- Hai elbow chỉ được tịnh tiến; không xoay, mirror hoặc đổi type.
- Đầu gần của mỗi Pipe ngoài được cập nhật tới connector phía ngoài của elbow sau khi dịch; đầu xa giữ nguyên tuyệt đối.
- Hai đầu Pipe giữa được cập nhật tới hai connector phía trong của hai elbow; Pipe giữa giữ nguyên type, DN, system và slope nếu hình học mục tiêu tương thích.
- Không xóa và không tạo lại hai elbow hoặc ba Pipe.

## Trình tự chỉnh tại chỗ

Mỗi cụm hợp lệ được xử lý trong một `Transaction` riêng bên trong `TransactionGroup` của toàn lệnh:

1. Chụp ElementId, UniqueId, type, DN, system, slope, hai đầu xa, connector owner và trạng thái kết nối.
2. Ngắt đúng bốn cặp connector cục bộ giữa ba Pipe và hai elbow. Không ngắt các đầu xa.
3. Tịnh tiến Elbow 1 và Elbow 2 từ tâm hiện tại tới `B` và `C`.
4. Regenerate và đọc lại connector origin từ chính fitting đã dịch.
5. Thay đổi `LocationCurve` của hai Pipe ngoài, giữ đầu xa và đưa đầu gần tới connector ngoài tương ứng.
6. Thay đổi `LocationCurve` của Pipe giữa theo hai connector trong.
7. Nối lại đúng bốn cặp connector đã ghi, regenerate và kiểm tra hậu điều kiện.

Nếu Revit tự lan truyền phép dịch ra mạng ngoài, từ chối geometry, không reconnect được hoặc bất kỳ hậu điều kiện nào sai, rollback riêng cụm đó.

## Hậu điều kiện bắt buộc

- Hai đầu xa của Pipe ngoài không đổi ngoài dung sai 0.1 mm.
- Hai elbow và ba Pipe giữ nguyên ElementId/UniqueId, type, DN và system.
- Đúng bốn kết nối cục bộ được khôi phục; không tạo connector hở mới.
- Không có phần tử ngoài cụm bị dịch chuyển.
- Hai elbow không đổi orientation ngoài sai số số học.
- Pipe giữa dài hơn chiều dài tối thiểu và trùng hai connector trong.
- Tâm elbow đạt `B`, `C` trong dung sai 0.5 mm.
- Chạy lại lệnh trên kết quả vừa chuẩn hóa không phát sinh dịch chuyển lớn hơn 0.5 mm.

## Giao diện và thao tác

- Nút `FitSpace` đặt cạnh `E90to45` và `E45to90` trong panel `PLUMBING`.
- Không mở cửa sổ cấu hình trong phiên bản đầu.
- Nếu preselection không tạo được cụm hợp lệ, lệnh nhắc: `Chọn Pipe giữa hoặc hai elbow cần chuẩn hóa khoảng cách`.
- Selection filter chỉ nhận Pipe và pipe fitting.
- Có thể chọn nhiều phần tử; lệnh tự hợp nhất thành các cụm duy nhất.
- Kết quả hiển thị số cụm: đã chỉnh, đã đạt sẵn, bị bỏ qua và rollback; danh sách lý do đầu tiên theo từng nhóm lỗi.

## Pin, Group và worksharing

- Cụm chứa phần tử Pin, nằm trong Group, design option không chỉnh sửa được hoặc do người khác sở hữu bị bỏ qua trước khi ngắt connector.
- Không tự Unpin, checkout cưỡng bức, ungroup hoặc chuyển design option.
- Mọi ElementId/UniqueId và trạng thái connector được kiểm tra lại ngay trước transaction.

## Transaction, rollback và Undo

- Phân tích selection và giải toàn bộ target geometry trước khi sửa document.
- Một `TransactionGroup` bao toàn lệnh để tạo một mục Undo tên `TungMEP2 - FitSpace`.
- Mỗi cụm chạy trong một `Transaction` độc lập. Cụm lỗi rollback riêng; cụm không phụ thuộc có thể tiếp tục. Sau cùng `TransactionGroup.Assimilate()` gom các transaction thành một Undo.
- Nếu phát hiện hai cụm chồng lấn sau khi bắt đầu, rollback toàn bộ thay vì sửa một phần tử hai lần.
- Không để lại connector ngắt, transaction đang mở hoặc phần tử tạm.

## Kiến trúc mã

- `Core/Internal/Ctn/FitSpace/FitSpaceModel.cs`: snapshot, cluster, target geometry và report DTO.
- `Core/Internal/Ctn/FitSpace/FitSpaceCollector.cs`: nhận diện và hợp nhất cụm từ selection.
- `Core/Internal/Ctn/FitSpace/FitSpaceGeometry.cs`: tâm elbow, giao điểm ảo, target `B/C` và kiểm tra idempotence.
- `Core/Internal/Ctn/FitSpace/FitSpaceExecutor.cs`: disconnect, translate, resize, reconnect, verify và rollback.
- `Core/Internal/Ctn/FitSpace/FitSpaceRunner.cs`: preselection/pick, orchestration và kết quả.
- `Core/Commands/CTN/Command_FitSpace.cs`: secured external command.
- `Core/Application.cs`: tooltip và ribbon button.
- Logic hình học thuần được tách khỏi Revit mutation để kiểm thử bằng console.

Không gọi `E45to90` hoặc `E90to45` như một vòng xóa/dựng lại ngầm. Có thể chia sẻ hàm tính `ReferenceSize`/target spacing với `PipeUtilities`, nhưng FitSpace phải giữ nguyên element identity.

## Thông báo lỗi

Các lý do bỏ qua phải phân biệt được:

- không tìm thấy đúng `2 elbow + 1 Pipe giữa`;
- fitting không phải elbow hai-port;
- connector đang hở hoặc nối sai owner;
- hình học không đồng phẳng/không giải được giao điểm;
- chiều dài Pipe ngoài không đủ;
- target làm Pipe giữa quá ngắn;
- Pin, Group, design option hoặc worksharing;
- Revit từ chối LocationCurve;
- reconnect hoặc hậu kiểm thất bại.

Không hiển thị exception stack trace cho lỗi hình học thông thường; stack trace chỉ ghi vào diagnostics nếu có cơ chế log.

## Kiểm thử tự động

- Nhận diện đúng một cluster từ Pipe giữa, một elbow hoặc cả hai elbow.
- Hợp nhất selection trùng và phát hiện hai cluster chồng lấn.
- Từ chối fitting không phải elbow, fitting nhiều port, Pipe cong và connector hở.
- Công thức 45°/90° cho `RawMiddleLength = 2.2 × ElbowData[DN]` và standoff đối xứng.
- Solver giữ đầu xa, orientation elbow và thứ tự connector.
- Từ chối đoạn ngắn, giao điểm không ổn định và geometry không đồng phẳng.
- Source contract xác nhận không gọi delete/create fitting và có rollback/reconnect verification.
- Ribbon contract xác nhận `FitSpace` cạnh `E90to45`/`E45to90`.

## Kiểm thử trong Revit 2024

1. Cụm 45° nằm ngang bị quá sát: đạt spacing giống E90to45.
2. Cụm quá thưa: co lại đúng spacing.
3. Cụm đã đẹp: báo `đã đạt`, không tạo thay đổi đáng kể.
4. Cụm có slope/3D đồng phẳng: giữ hai đầu xa và slope hợp lệ.
5. Các DN 50/75/100/150: target thay đổi đúng theo DN lớn nhất.
6. Chọn Pipe giữa, chọn một elbow và chọn cả cụm đều nhận cùng cluster.
7. Chọn nhiều cluster độc lập: mỗi cluster xử lý một lần, một Undo chung.
8. Pin/Group/worksharing conflict: bỏ qua trước khi disconnect.
9. Cụm chứa reducer/coupling/Tee hoặc connector hở: không sửa model.
10. Cụm không đủ chiều dài: rollback, bốn connector vẫn nối.
11. Undo khôi phục đúng hình học ban đầu.
12. Chạy lại ngay: dịch chuyển không vượt 0.5 mm.

## Backup và phát hành

- Trước khi sửa mã, tạo backup timestamped trong `C:\Users\tungnt2303\Downloads\260515_Release\backups` gồm source, build/release DLL, ZIP, manifest cài đặt và `RESTORE.txt` có SHA256.
- Không ghi đè bản DLL đang được Revit tải. Nếu Revit đang mở, chỉ build/package và chờ đóng/mở lại hoặc dùng cơ chế Reload TungMEP2 đã được xác nhận tương thích.
- Chạy toàn bộ regression hiện có của TungMEP2 cùng test FitSpace trước khi tạo bộ cài.

## Ngoài phạm vi

- Tự đổi elbow angle/type/family.
- Xóa và dựng lại fitting để tăng tỷ lệ thành công.
- Chuẩn hóa reducer, coupling, transition hoặc chuỗi nhiều fitting.
- Thêm offset/elbow/Pipe mới khi đoạn hiện hữu không đủ.
- Dịch mạng phía sau hai Pipe ngoài.
- Gán khoảng cách theo tiêu chuẩn thay cho quy tắc thực hành hiện có của E90to45.
