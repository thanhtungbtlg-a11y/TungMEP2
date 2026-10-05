# PipeLayer — co/giãn ống cần căn (2026-10-05)

## Phạm vi đã được người dùng đồng ý

Cho phép Pipe đã chọn **không phải ống gốc** co/giãn dọc trục 3D để giữ kết nối; giữ ống gốc nguyên cả hai đầu, giữ hướng/độ dốc/trục mặt bằng và cao độ BOP/COP/TOP tại điểm đo ban đầu. Không cho hở cổng, mất điểm đo hoặc kéo phần tử khóa. Đây là sửa giới hạn trong Runner/Safety/Math hiện có, không thêm lệnh hoặc thay Auto90/DirectY.

## Bằng chứng / giới hạn chẩn đoán native

Đọc journal Revit 2024 `journal.0240.txt` (read-only, không thao tác model/UI):

- Lần chạy 10:05:00 ngày 2026-10-05 dùng DLL `300FBA41938D-899CFE674CE1`.
- Ngay sau PickObject, candidate duy nhất được bấm là Pipe **43090390**; tiếp theo hộp thoại BOP/COP/TOP mở. Đây là ống gốc.
- Lỗi sau thao tác báo Pipe **43089744**, thuộc nhóm cần căn, không phải ống gốc.
- Lỗi cũ gộp endpoint XY/hướng/độ dốc/cao độ; journal không có coordinates trước/sau nên không thể kết luận sai lệch cụ thể của Pipe43089744.
- Lần chạy sau 10:08 có một lỗi khác ở cặp connector **43078075:1 ↔ 43078082:2**. Không bỏ kiểm tra cặp cổng và không tuyên bố co/giãn sẽ giải mọi lỗi native.
- Journal có cảnh báo version System.Memory 4.0.2.0 vs module đã nạp 4.0.1.1. Command vẫn chạy tới transaction/validation; chưa xác định ảnh hưởng của cảnh báo này. Không thay dependency trong đợt sửa co/giãn.

## Thay đổi

- Capture CandidateParameter của từng ống trước thao tác; explicit referenceId phân biệt ống gốc với ống cần căn đã có delta0.
- Math đọc dịch Z tại cùng XY trên trục 3D gốc. Không lấy trung bình Z hai đầu vì co/giãn ống dốc làm thay đổi Z endpoint mà không phải dịch cao độ.
- Hai đầu ống cần căn phải nằm trên trục gốc đã dịch Z trong tolerance0,05 mm; giữ slope property, loại, DN, System Type, identity và các chốt kết nối hiện có.
- Cao độ đích và điểm đo thật được kiểm tra. Co ngắn/trượt vượt điểm đo bị từ chối, không extrapolate trục ảo để báo thành công.
- Ống gốc vẫn kiểm tra rigid endpoint với delta0. Pipe khóa vẫn giữ strict endpoint geometry. Unselected related movement policy và host/ancestor protection giữ nguyên.
- Pre/post-commit validation đều nhận referenceId và station map. Thông báo phân biệt “Ống gốc”/“Ống cần căn”, loại/mức lệch và giai đoạn trước dịch/sau dịch/sau commit khi có dữ liệu.
- Tooltip, giải thích trong hộp thoại và smoke guide cập nhật; Auto90 button/code không đổi.

## RED / GREEN

Test real Runner/Safety/Math, với Revit boundary double chỉ cung cấp quan sát, không giải fitting:

1. RED: selected Pipe co/giãn dọc trục an toàn bị real Runner từ chối bằng chính rigid-endpoint error cũ.
2. GREEN: giữ lại selected resize, kể cả sau commit và ở ống dốc đã co/giãn khi được kéo theo trước lượt dịch của nó.
3. Negative cases giữ lại: reference resize, lệch ngang, đổi slope, sai datum, mất station, ngắt cổng. Selected delta0 không nhầm reference; Pin/Group/OwnedByOtherUser vẫn được bảo vệ.
4. RED bổ sung: sai lệch XY và Z riêng đều nhỏ hơn tolerance nhưng Euclidean residual gộp vượt tolerance. GREEN sau kiểm tra residual từng đầu trong một quả cầu tolerance.
5. GREEN bổ sung: real Runner BOP/TOP với OD khác nhau, kiểm tra delta −2,1/−1,9 và datum sau co/giãn; TaskDialog selection double được khôi phục trong finally. Review không còn blocker nguồn; Minor về coverage BOP/TOP đã đóng. Các giới hạn native vẫn giữ nguyên.

## Kiểm tra fresh

Production rebuild net48/x64/Revit2024: 0 warning, 0 error.

11 console suite rebuild/run exit0:
DirectY geometry1100, inventory11, failure17; FitSpace32; PipeSpacing86; AutoTagPipe219; PipeLayer math1516, dialog10, safety88; Auto90 geometry498; ElbowRunSlope15. **Tổng3592 assertions**.

Unknown/multiple PipeLayer safety selectors đều exit nonzero. Verification output cô lập tại `backups/PipeLayer-before-selected-resize-20261005/Verification`.

Windows PowerShell fresh exit0: Test-FitRotate, Test-MepAlignment, Test-PipeSpacing, Test-FitSpace, Test-PrintSheetsInstaller, Test-PrintSheets20261003, Test-Auto90Package (native command/ribbon mode metadata, không phải model proof).

## Backup / bản build

Backup `backups/PipeLayer-before-selected-resize-20261005` chứa source/test/doc trước sửa, active manifest, installed DLL và full ZIP đã có Auto90.

- Baseline main SHA256: `300FBA41938D82F13CFB5A2610C340692FE43EF4AF641A1123D78E7CEFAB36E0`.
- Baseline ZIP SHA256: `56BE4A20FA3E806AE390BCBAEB0A2ECA3E434C3A9688D8B6C5E016FC4C27CC72`.
- New main SHA256: `B8290B12CBAA7F97AA24E238C79CCA6F83966CE76E72EE3CF8A7C54B71717688`.
- PrintSheets dependency giữ nguyên SHA256: `899CFE674CE15794AB28CD28BDB3D3B41AD9477652F1A7A91C976D60EEC768B7`.

## Delivery đã kiểm tra

- Đóng gói và cài bản `B8290B12CBAA-899CFE674CE1`; manifest active trỏ đúng DLL mới trong `%LOCALAPPDATA%\TungMEP2\Revit2024\B8290B12CBAA-899CFE674CE1\net48`.
- SHA256 main ở build/release/ZIP/install đều đúng `B8290B12CBAA7F97AA24E238C79CCA6F83966CE76E72EE3CF8A7C54B71717688`. PrintSheets ở lib/release/ZIP/install giữ hash cũ nêu trên.
- ZIP mới SHA256: `092A2B0611340597BD32E42097AD66EFCC1A363E49333406746DF6E4764158BC`.
- 38 file payload đã đối chiếu hash release/install; smoke guide trong ZIP khớp source. DLL install cũ `300FBA41938D-899CFE674CE1` và backup ZIP giữ nguyên hash baseline.
- Metadata từ DLL đã cài: PipeLayer và Auto90 là native external command, ribbon/runner dispatch đúng một lần; các helper selected-resize có trong DLL. Test-PrintSheets20261003 chạy trên install exit0. Đây không phải chứng minh Revit đã nạp DLL mới hoặc native model đã thành công.
- Revit PID32856 vẫn mở từ 10:00:49; không đóng/restart/sửa model. Người dùng cần lưu, đóng hẳn/mở lại để nạp bản mới.

## Native smoke còn cần

Revit PID32856 mở từ 10:00:49. Không đóng/restart hoặc sửa model/UI. Sau khi cài DLL mới người dùng lưu, đóng hẳn/mở lại Revit rồi thử cùng cụm trên bản sao model theo `PipeLayer-Revit2024-smoke-test.md`.

Chưa xác nhận solver/host propagation/worksharing/Pending/rollback/Undo thật, UI/loading phiên bản sau cài, hoặc cặp cổng native nêu trên. Không tuyên bố đã sửa mọi lỗi kết nối, không tự nới tolerance/bỏ topology guard nếu lần thử mới còn rollback.
