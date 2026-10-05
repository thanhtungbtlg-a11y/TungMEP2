# MC-Align và AlignMEP3D cho cụm MEP đã kết nối

Ngày: 2026-08-06

## Mục tiêu

Mở rộng hai lệnh `MC-Align` và `AlignMEP3D` để làm việc với Pipe/MEP element đã kết nối một hoặc hai đầu. Khi phần tử thứ hai đã nối với các phần tử khác, toàn bộ cụm liên thông của nó phải xoay và dịch chuyển như một khối cứng để giữ nguyên các kết nối hiện có.

## Hành vi đã chốt

- Phần tử được chọn thứ nhất là mốc và không di chuyển.
- Phần tử được chọn thứ hai xác định cụm cần di chuyển.
- Cụm di chuyển gồm các owner liên thông qua connector vật lý, bao gồm `MEPCurve`, `FamilyInstance`, `FabricationPart` và các MEP family có connector vật lý.
- Nếu cả hai connector mục tiêu đang hở, `MC-Align` giữ hành vi cũ: căn hướng, đưa hai connector gặp nhau và tạo kết nối mới.
- Nếu một hoặc cả hai connector mục tiêu đã kết nối, `MC-Align` căn hướng và di chuyển cả cụm nhưng không gọi `ConnectTo` và không tạo kết nối mới.
- `AlignMEP3D` luôn căn hai trục connector trong không gian 3D, không tạo kết nối mới; khi cụm đã kết nối, toàn bộ cụm cùng biến đổi.
- Nếu phần tử mốc nằm trong cụm liên thông của phần tử thứ hai, lệnh dừng và không thay đổi model.
- Nếu cụm có phần tử bị Pin, nằm trong Group hoặc không thể biến đổi an toàn, lệnh dừng trước khi sửa model và nêu rõ phần tử gây cản.
- Tất cả thay đổi diễn ra trong một transaction. Bất kỳ lỗi nào cũng rollback toàn bộ.

## Lựa chọn connector

1. Lấy toàn bộ connector vật lý trên hai phần tử được chọn; bỏ connector logical.
2. Lọc theo domain tương thích.
3. Ưu tiên cặp connector hở-hở gần nhất để duy trì hành vi cũ.
4. Nếu không có cặp hở-hở, chọn cặp connector vật lý tương thích gần nhất, kể cả connector đang kết nối.
5. Trạng thái kết nối của cặp đã chọn quyết định `MC-Align` có được tạo kết nối mới hay không.

Không tự ngắt connector hiện có và không tự thay fitting/routing preference.

## Thu thập cụm liên thông

- Bắt đầu từ owner của phần tử thứ hai.
- Duyệt theo chiều rộng qua `Connector.AllRefs` và chỉ theo các quan hệ kết nối vật lý thực sự (`IsConnectedTo`).
- Bỏ qua self-reference và connector logical.
- Dùng `ElementId` để chống lặp.
- Nếu gặp phần tử mốc trong quá trình duyệt, trả lỗi “hai phần tử thuộc cùng mạng liên thông”.
- Thu thập xong mới kiểm tra Pin, Group và khả năng biến đổi; chưa mở transaction sửa đổi ở giai đoạn này.

## Biến đổi hình học

### Xoay

- Pivot là origin của connector mục tiêu trên phần tử thứ hai.
- Tính phép xoay để `BasisZ` của connector di chuyển đối hướng với `BasisZ` của connector mốc.
- Với connector không tròn, giữ bước căn roll hiện có.
- Áp dụng cùng một phép xoay cho toàn bộ `ElementId` trong cụm.

### Dịch chuyển

- `MC-Align`: dịch toàn bộ cụm bằng vector từ origin connector di chuyển đến origin connector mốc.
- `AlignMEP3D`: dùng thuật toán chiếu hiện có để đưa trục connector di chuyển thẳng hàng với trục connector mốc, rồi dịch toàn bộ cụm bằng cùng một vector.
- Sau mỗi bước biến đổi, regenerate và tìm lại connector theo owner, domain và vị trí dự kiến; không phụ thuộc connector còn hở hay đã kết nối.

## Kiểm soát an toàn và lỗi

- Kiểm tra trước khi biến đổi: phần tử mốc không thuộc cụm; không phần tử nào bị Pin hoặc thuộc Group.
- Nếu Revit từ chối xoay/dịch bất kỳ phần tử nào, rollback transaction.
- Không để lại trạng thái xoay một phần, dịch một phần hoặc đứt kết nối.
- Thông báo lỗi phải phân biệt: cùng mạng, phần tử Pin, phần tử trong Group, không có connector cùng domain và Revit từ chối biến đổi.
- Lệnh tiếp tục vòng chọn sau lỗi; `Esc` thoát như hiện tại.

## Tương thích ngược

- Hai connector hở vẫn chọn và kết nối giống bản hiện tại.
- Các loại Pipe, Duct, Conduit, Cable Tray, FabricationPart và MEP family đang hỗ trợ không bị loại bỏ.
- Không đổi tên lệnh, tab, ribbon icon hoặc phím tắt Revit hiện có.

## Kiểm thử

### Kiểm thử tự động

- Chính sách chọn cặp ưu tiên hở-hở, sau đó mới dùng cặp đã kết nối.
- Xác định đúng khi nào `MC-Align` được phép gọi connect.
- Phát hiện phần tử mốc thuộc cụm di chuyển.
- Phát hiện phần tử Pin/Group trước transaction.
- Bảo toàn hành vi tính góc, roll và vector dịch chuyển hiện có.
- Smoke test xác nhận hai command vẫn được đăng ký trên ribbon.

### Kiểm thử trong Revit 2024

1. Hai phần tử đều có đầu hở: MC-Align căn và nối như bản cũ.
2. Phần tử thứ hai nối một đầu: toàn bộ cụm liên quan cùng di chuyển, đầu hở có thể nối mới.
3. Phần tử thứ hai nối kín hai đầu: toàn bộ cụm cùng di chuyển, không tạo kết nối mới.
4. AlignMEP3D với cụm có Pipe, elbow, tee và accessory: cụm căn trong 3D, mọi kết nối giữ nguyên.
5. Hai phần tử thuộc cùng mạng: lệnh từ chối, model không đổi.
6. Cụm có phần tử Pin hoặc Group: lệnh từ chối, model không đổi.
7. Hai phần tử khác domain: lệnh từ chối rõ ràng.

## Backup và khôi phục

Trước khi sửa mã hoặc build:

- Tạo thư mục backup có dấu thời gian trong `C:\Users\tungnt2303\Downloads\260515_Release\backups`.
- Sao lưu tối thiểu: `MepConnectorAlignmentRunner.cs`, hai command, test hiện tại, DLL build/release, ZIP/bộ cài hiện tại và manifest `TungMEP2.addin` đang cài.
- Ghi file `RESTORE.txt` nêu đường dẫn nguồn và đích để khôi phục.
- Không xóa hoặc ghi đè backup trong quá trình triển khai.

## Ngoài phạm vi

- Không tự ngắt và nối lại các connector đang dùng.
- Không tự tạo elbow, tee, reducer hoặc thay routing preference.
- Không xử lý trường hợp chỉ di chuyển một nhánh bên trong cùng một mạng liên thông; trường hợp này phải dừng an toàn.
- Không thay đổi các tool khác ngoài phần dùng chung cần thiết cho `MC-Align` và `AlignMEP3D`.
