# Auto90 — TungMEP2 / PLUMBING

## Mục đích và cách dùng

Tự xoay cụm nhánh, căn trục trong 3D rồi tạo Tee 90° trên Pipe tuyến chính và nối vật lý. Không nhập góc, không phải tạo Tee/Y sẵn. Không dùng phép chiếu mặt bằng để giả định góc 90°.

1. Lưu model. Sau khi cài DLL mới, đóng hẳn và mở lại Revit 2024.
2. Chạy **TungMEP2 → PLUMBING → Auto90**.
3. Chọn **Pipe tuyến chính** muốn chèn Tee (không chọn Tee).
4. Chọn **Pipe cuối nhánh có đúng một đầu hở**, đầu còn lại nối qua elbow hiện có. Chuỗi Pipe–con thu/union–Pipe tới elbow được nhận như DirectY.

Có thể chọn trước một Pipe làm tuyến chính. Nếu chọn trước hai Pipe và chỉ một Pipe có đúng một đầu hở, tool tự nhận Pipe đó là nhánh. Nếu không xác định rõ thì vẫn yêu cầu bấm chọn tuyến chính/nhánh. Esc hủy chọn không sửa model.

## Ưu tiên và phạm vi chỉnh

- Thử các phương án xoay tìm được ở **0 mm chỉnh cao độ bổ sung** trước. Với hai elbow, tận dụng cả hai khớp xoay khi cần.
- Chỉ sau khi không tìm được kết nối ở cao độ đó mới thử **+0,5; −0,5; +1; −1; ... mm**, tới ±100 mm. Mỗi cao độ đều thử xoay trước khi sang mức lớn hơn. Kết quả đầu tiên đạt kiểm tra có mức chỉnh cao độ nhỏ nhất trong lưới 0,5 mm đã thử; không tuyên bố tối ưu liên tục dưới 0,5 mm.
- Góc xoay được giải tự động/liên tục. Phần tìm hướng roll dùng bước phát hiện 0,5° rồi tinh chỉnh nghiệm; không làm tròn góc kết quả về 0,5°.
- Với một elbow và trục nhánh song song tuyến chính, giải góc roll từ điều kiện đồng phẳng thực, kể cả offset lệch tâm; không chỉ thử các góc mẫu 0/45/90°.
- Chỉ chỉnh chiều dài các Pipe cục bộ cần thiết (giới hạn an toàn 5 m, không tạo Pipe quá ngắn). Giữ elbow/con thu/union và các Pipe trung gian dưới phép chuyển động cứng; giữ đầu xa của ống trục/phía trước.
- Tuyến chính được chia để chèn Tee nhưng giữ nguyên tim 3D, cao độ, độ dốc, hai đầu ngoài và kết nối ngoài phạm vi. Đây không phải lệnh kéo tuyến chính theo nhánh.
- Xoay có thể tự làm thay đổi cao độ của một số điểm trên cụm. Con số trong hộp thoại là **dịch cao độ bổ sung** do co/giãn ống trục, không phải tổng độ dịch Z của mọi điểm sau xoay.

## Tạo fitting / System Type

### Khoảng chèn trên đoạn chính — sửa 2026-10-05

Auto90 không còn loại đoạn chính theo ngưỡng `4 × đường kính`. Trước khi thử Tee, chỉ yêu cầu chia được hai đoạn ống thử an toàn: mỗi đoạn lớn hơn `max(2 × ShortCurveTolerance của Revit, 10 mm)` và thêm dự phòng số học 0,1 mm.

Sau khi tạo Tee native, tool đo vị trí hai cổng run theo trục tuyến chính, tính khoảng tâm chèn sao cho hai phía Tee vẫn chừa đủ Pipe. Ngưỡng mới phụ thuộc takeout của family/size thực, không có nghĩa mọi đoạn ngắn đều lắp được. Thử thêm tâm/biên khoảng đo được cho Tee bất đối xứng hoặc cửa sổ chèn hẹp nằm giữa các vị trí mẫu; tối đa 20 vị trí thử cho mỗi hướng. Các chốt giữ tim, đầu ngoài và kết nối vẫn áp dụng trước/sau commit.

Nếu không tìm được fitting thử an toàn, thông báo ghi chiều dài đoạn chính và yêu cầu theo Tee đã đo tốt nhất (nếu có). Nếu creation/cleanup/size/góc bị Revit từ chối trước khi lấy được số đo hợp lệ, báo rõ chưa đo được, không bịa chiều dài yêu cầu. Log DirectY ghi ID, chiều dài, DN, khoảng lùi tạm, takeout và khoảng tâm chèn.

Sửa này chỉ áp dụng Auto90. DirectY45 giữ ngưỡng preflight cũ. PipeLayer hai chế độ và PrintSheets 2026.10.03.1 giữ nguyên.

Dùng cơ chế `NewTeeFitting`/chia Pipe/ống thử theo template nhánh của 1E1Y, cùng cleanup có phân loại và rollback của DirectY đã sửa. Không ép System Type hai Pipe phải bằng nhau; Revit được phép nối và gộp hệ thống nếu hợp lệ. Chỉ cảnh báo đúng `MismatchedSystemTypeWarning` được chấp nhận và ghi log; lỗi hoặc cảnh báo khác vẫn rollback, kể cả System Classification không tương thích. Không tự đổi System Type trước để ép nối.

Routing Preferences phải có family/size tạo được **Tee90 cùng DN hoặc reducing Tee90** phù hợp. Một Tee90 cố định đã đúng hướng không cần tham số Angle writable. Nếu Revit trả Y45 và family không chỉnh được thành 90° thật thì từ chối có log, không giữ Y45 hoặc giả báo thành công. Bản này không tự tải family mới và không tự thêm con thu ở Tee nếu reducing Tee không có; con thu sẵn trên nhánh vẫn được giữ.

Kiểm tra góc cả cổng nhánh Tee và trục connector Pipe nhánh với hai cổng tuyến chính trong sai số 0,5°. Kiểm tra DN, ID/type, kết nối hai chiều, vector lệch bên trong cụm cũ, đầu ngoài phạm vi và phần tử cũ còn tồn tại, trước và sau commit. Không bỏ qua lỗi ngắt/xóa để giữ một phần kết quả.

Log dùng chung cơ chế DirectY, tại `%LOCALAPPDATA%\TungMEP2\Logs\DirectY-*.log`, dòng `input/mode` xác định Auto90.

## Smoke trên bản sao model (chưa được chạy native tự động)

| Tình huống | Kết quả cần kiểm tra |
| --- | --- |
| Một elbow + Pipe nhánh hở, đủ không gian, không cần chỉnh cao độ | Tự tính góc, Tee/nhánh 90° trong 3D, mức chỉnh bổ sung 0 mm; giữ đầu xa. |
| Một elbow, trục phía trước song song tuyến chính nhưng cao độ/lệch ngang khác nhau | Tìm được góc roll ngoài các góc mẫu nếu có nghiệm; không chỉnh cao độ chỉ vì bỏ sót góc đó. |
| Hai elbow, tuyến chính nằm ngang/đứng/dốc | Phối hợp xoay đúng trục; không dùng 90° mặt bằng cho một góc không vuông thật trong 3D. |
| Nhánh có Pipe–con thu/union–Pipe tới elbow | Nhận chuỗi, giữ ID/type/takeout con thu và các kết nối cũ; không bắt đầu nhánh nối trực tiếp elbow. |
| Có phương án xoay 0 mm và phương án khác cần dịch cao độ | Chọn 0 mm trước, không nhảy sang cao độ chỉ vì góc khác khó hơn. |
| Chỉ nối được sau chỉnh nhẹ cao độ | Thử lưới 0,5 mm theo độ lớn tăng dần; báo đúng mức đã dùng, không ẩn thêm dịch cao độ của trục. |
| Hai Pipe khác System Type nhưng Revit cho phép nối | Không chặn ở preflight chỉ vì khác tên/type; connector nối vật lý và log ghi cảnh báo được phép nếu có. |
| System Classification/error không tương thích | Rollback toàn bộ, báo nguyên nhân; không báo nối thành công chỉ vì hình 3D chạm nhau. |
| Tee90 fixed-angle, không có tham số Angle writable | Nếu đúng DN/hướng, tạo và nối được mà không cố set Angle. |
| Routing Preferences chỉ có Y45 không thể chỉnh90 | Báo thiếu family/size/góc phù hợp, rollback; không tạo Y45 thay Tee90. |
| Tee90 DN khác nhau, family reducing có size phù hợp | Nhánh giữ DN và kết nối đúng; nếu thiếu size thì rollback. |
| Pipe chính/nhánh Pin, Group, người khác sở hữu, option không hoạt động | Từ chối trước sửa, không tự mở khóa/đổi option. |
| Tap/Curve/Physical, vòng, Flex/Fabrication, Tee/thiết bị trong chuỗi trước elbow | Từ chối phạm vi chưa hỗ trợ; không tự kéo toàn mạng. |
| Revit kéo phần tử ngoài phạm vi hoặc đổi/hở kết nối sau commit | Rollback và giữ lại toàn bộ model cũ. |
| Thành công rồi Undo | Một Undo hoàn tác toàn bộ Tee/split/rotate/length; không còn ống thử. |
| Đoạn chính ngắn hơn 4D nhưng Tee thực tế đủ chỗ | Được đi đến bước thử Tee; nếu các kiểm tra khác đạt thì nối thành công, không báo ngưỡng 4D cũ. |
| Đoạn chính không đủ cho Tee thực tế | Rollback; báo chiều dài hiện có/yêu cầu đo được, hoặc rõ chưa có số đo hợp lệ nếu native không tạo/giữ được Tee thử. |
| Tee có takeout hai phía không bằng nhau hoặc cửa sổ tâm chèn rất hẹp | Thử tâm/biên khoảng đo được, không bỏ sót chỉ vì sát điểm mẫu không hợp lệ. |
| DirectY45 / PipeLayer / FitRotate sau cài | Hành vi cũ vẫn như trước; Auto90 là nút riêng. |

Không có kiểm tra va chạm/khoảng hở hoặc chứng nhận quy tắc thiết kế thoát nước. Lệnh tạo hình học được yêu cầu, không quyết định Tee90 có phù hợp tiêu chuẩn thiết kế của dự án hay không.

## Kiểm tra tự động và giới hạn

`tools\Auto90GeometryConsoleTest.csproj` liên kết mã sản phẩm thật: hướng Tee90 ở các trục 3D, guard90 riêng với default Y45 không đổi, giải góc liên tục/co giãn, hai khớp và thứ tự mức cao độ. RED đã thấy mã cũ trả hướng45 và từ chối Tee90; GREEN kiểm tra hành vi mới. Các test DirectY gốc tiếp tục chạy.

Review bổ sung một regression RED: một elbow, anchor song song main, pivot `(0,10,10)`, đầu hở `(0,13,10)` — các góc mẫu bỏ sót nghiệm zero-height. Sau sửa, test kiểm tra nghiệm analytic, anchor song song/đối song song, trục main nghiêng 3D và fitting có takeout lệch tâm.

Console/build không giải fitting native, không kiểm chứng family/Routing Preferences thật, UI chọn đối tượng, host propagation, Worksharing hay Undo. Cần thực hiện bảng smoke trên bản sao model. Chỉ hỗ trợ nhánh có một đầu hở, đầu còn lại qua elbow; chưa xử lý một Pipe rời có cả hai đầu hở.

Backup trước Auto90: `backups\Auto90-before-rotate-align-20261005\TungMEP2-Revit2024.zip` (bao gồm PipeLayer đã sửa), thư mục DLL cũ `B027EED99725-899CFE674CE1` không bị ghi đè khi cài bản mới.
Backup trước sửa khoảng chèn: `backups\Auto90-actual-tee-clearance-20261005-134117\TungMEP2-Revit2024.zip`, main `BE51A7DBBC22-899CFE674CE1`, đã có PipeLayer hai chế độ. Đóng Revit, giải nén ZIP backup riêng, chạy `Install-TungMEP2.cmd` để khôi phục.
