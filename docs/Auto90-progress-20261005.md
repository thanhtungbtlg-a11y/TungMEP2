# Auto90 — bàn giao 2026-10-05

## Yêu cầu đã triển khai

- Nút native `Auto90` trong TungMEP2 → PLUMBING, ngay sau DirectY.
- Chọn Pipe tuyến chính, rồi Pipe cuối nhánh có một đầu hở; không nhập góc hoặc chọn Tee có sẵn.
- Tự tạo Tee90 và nối bằng pipeline split/probe/cleanup/connect/rollback đã dùng cho DirectY; giữ các elbow/con thu/union hiện có.
- Ưu tiên các phương án xoay ở cao độ hiện tại, rồi thử `+0,5; −0,5; +1; −1; … ±100 mm`. Co/giãn các Pipe cục bộ theo giới hạn hiện có. Giữ tim 3D, hai đầu ngoài và kết nối ngoài của tuyến chính.
- Auto90 kiểm tra góc thật 3D của cả cổng Tee và cổng Pipe nhánh với hai cổng tuyến chính. Không giữ Y45 thay cho Tee90.
- Không chặn khác System Type ở preflight. Revit vẫn quyết định tương thích; chỉ cảnh báo đúng MismatchedSystemTypeWarning được cho phép, cảnh báo/lỗi khác rollback.
- Các tham số mode mới mặc định false; DirectY45 vẫn theo đường chạy cũ. Không sửa PipeLayer hoặc dependency PrintSheets trong đợt này.

## Review / RED → GREEN

Test liên kết mã hình học sản phẩm thật, không dùng grep làm bằng chứng hành vi.

1. Test hướng/góc Tee90 RED trên mã cũ trả Y45; GREEN sau mode90.
2. Reviewer tìm ca một elbow có anchor song song main bỏ sót roll liên tục. Fixture main X, pivot `(0,10,10)`, đầu hở `(0,13,10)` RED vì không tìm được nghiệm zero-height từ góc mẫu.
3. Thêm SinglePositionRolls: giải coplanarity `A cos φ + B sin φ + C = 0`, bao gồm takeout lệch tâm. Mỗi nghiệm phải qua TryFitSingle có bounds. Helper chỉ gọi trong Auto90, vẫn ở vòng height ngoài cùng.
4. Regression GREEN; thêm trục nghiêng 3D, song song/đối song song, offset lệch tâm, bounds sai. Re-review không còn blocker chứng minh được từ mã nguồn.

Test metadata ban đầu phát hiện script tìm private CreateRibbon trên lớp entry point kế thừa nên trả null; sửa script đọc lớp triển khai VP_RevitAPI.Application và kiểm tra quan hệ kế thừa. Không phải lỗi native Auto90.

## Kiểm chứng đã chạy

Rebuild production net48/x64/Revit2024: 0 warning, 0 error.

| Console suite | Assertions |
| --- | ---: |
| DirectY geometry | 1100 |
| DirectY model inventory | 11 |
| DirectY failure policy | 17 |
| FitSpace math | 32 |
| PipeSpacing math | 86 |
| AutoTagPipe collision layout | 219 |
| PipeLayer math | 1503 |
| PipeLayer dialog | 10 |
| PipeLayer runner/safety | 67 |
| Auto90 geometry | 498 |
| ElbowRunSlope | 15 |
| Tổng | 3558 |

Cả 11 suite build/run exit0; output cô lập tại `backups/Auto90-before-rotate-align-20261005/Verification`.

PowerShell exit0: Test-FitRotate, Test-MepAlignment, Test-PipeSpacing, Test-FitSpace, Test-PrintSheetsInstaller, Test-PrintSheets20261003, Test-Auto90Package. Auto90 metadata test chạy trên build, package và installed DLL: command native, ribbon dispatch, explicit Tee90 pipeline mode. Đây không phải bằng chứng UI/native model đã chạy.

## Build, package, cài đặt đã kiểm tra hash

- Main DLL SHA256: `300FBA41938D82F13CFB5A2610C340692FE43EF4AF641A1123D78E7CEFAB36E0`.
- PrintSheets DLL SHA256 giữ nguyên: `899CFE674CE15794AB28CD28BDB3D3B41AD9477652F1A7A91C976D60EEC768B7`.
- ZIP SHA256: `56BE4A20FA3E806AE390BCBAEB0A2ECA3E434C3A9688D8B6C5E016FC4C27CC72`.
- Build/release/ZIP entry/installed main hash khớp; PrintSheets hash khớp ở lib/release/ZIP/installed.
- Active manifest: `%APPDATA%/Autodesk/Revit/Addins/2024/TungMEP2.addin`.
- Installed assembly: `%LOCALAPPDATA%/TungMEP2/Revit2024/300FBA41938D-899CFE674CE1/net48/TungMEP2.dll`.
- ZIP có `Auto90-Revit2024-smoke-test.md`.

Backup trước đợt này: `backups/Auto90-before-rotate-align-20261005`.
Old installed DLL `B027EED99725-899CFE674CE1` vẫn SHA256 `B027EED9972513B3006723A34DD6848993E7BAB02705FAC8D05AFEB67E28A20E`.
Backup ZIP vẫn SHA256 `D56E0842BD0109C100D2599AC2753F8B67498D629178125458FA794766757A08`.

## Cần kiểm tra trên bản sao model

Revit PID 21584 đang mở; không đóng/restart hoặc thao tác model/UI trong đợt này. Người dùng lưu model, đóng hẳn và mở lại Revit để nạp DLL mới, rồi chạy bảng smoke trong `Auto90-Revit2024-smoke-test.md`.

Chưa kiểm chứng native: family/size/Routing Preferences, Connector.Angle thật, nối khác System Type, Pending/rollback/Undo, worksharing, host propagation/phần tử phụ, UI/selection/ribbon/loading, hiệu năng và va chạm. Không tuyên bố completeness/tối ưu toàn cục trên mọi model; mức cao độ là nhỏ nhất trong những mức/phương án đã thử. Chỉ hỗ trợ nhánh một đầu hở có elbow hiện có, không phải Pipe rời hai đầu hở. Tee90/reducing Tee90 phù hợp phải có sẵn trong Routing Preferences.

Không làm lại phần đã hoàn tất. Nếu người dùng gửi lỗi tiếp theo, lấy log DirectY có dòng input/mode Auto90 và chẩn đoán trên bản DLL/hash nêu trên; không tự thay model của họ để thử.
