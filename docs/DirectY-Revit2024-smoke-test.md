# Kiểm tra DirectY trong Revit 2024

Bản nâng cấp đã qua build và kiểm thử toán/guard; các bước dưới đây cần chạy trong Revit trên **bản sao model**, chưa được tự động xác nhận bằng model thực tế.

## Cách chạy

1. Lưu công việc rồi đóng/mở lại Revit để nạp DLL mới.
2. Mở view 3D. Pipe nhánh cần có một đầu hở và chuỗi fitting dẫn tới elbow/ống trục. Tuyến chính có thể có Y nhánh nghiêng đang hở, hoặc là Pipe thẳng chưa có Y.
3. Chạy `TungMEP2 → PLUMBING → DirectY`.
4. Chọn **Pipe trên tuyến chính** (không cần chọn tee/Y), sau đó chọn **Pipe nhánh có đúng một đầu hở**.
5. Xem thông báo kết quả; kiểm tra kết nối bằng Tab/system inspection và Undo để so sánh vị trí.

## Các trường hợp cần kiểm tra

| Trường hợp | Kết quả cần thấy |
| --- | --- |
| Pipe nhánh → elbow → Pipe trục | Nối trực tiếp vào Y, giữ elbow; Y và đầu xa đứng yên. |
| Pipe nhánh → con thu/union → Pipe → elbow | Không báo bắt nối trực tiếp elbow; cả chuỗi theo cụm, con thu/union giữ nguyên. |
| Hai elbow, nhánh khác cao độ/hướng | Phối hợp xoay hai khớp và chỉnh chiều dài/cao độ trong phạm vi hình học; không đổi góc/takeout elbow, không mất kết nối phía trước. |
| Tuyến chính chưa có Y hở phù hợp | Chia đoạn Pipe đã chọn, tạo Y 45° theo Routing Preferences và nối trực tiếp Pipe cuối nhánh; giữ hai đầu xa tuyến chính, DN/type/level/slope. Không thêm elbow sát Y. |
| Có phương án nối bằng xoay ở cao độ ban đầu | Phải chọn phương án này trước khi thử thay đổi cao độ; thông báo bước cao độ 0 mm. |
| Không nối được ở cao độ ban đầu | Thử +0,5/−0,5 mm, rồi +1/−1 mm…, thử các phương án xoay tại mỗi mức; dừng ở phương án hợp lệ đầu tiên, giới hạn đầy đủ ±100 mm (401 mức gồm 0). Không nhảy thẳng tới mức cao độ xa. |
| Phương án cần góc roll Y nằm trong vùng hẹp giữa các mẫu cũ | Dò roll Y bằng lưới 0,5° thay cho 2,5°, rồi tiếp tục tinh chỉnh nghiệm. Góc xoay elbow vẫn tính trực tiếp từ hình học; góc kết quả không bị ép về bội số 0,5°. Không nhầm dung sai đối hướng 2° với bước dò góc. |
| 90° tee gần hơn Y nghiêng trên cùng run | Bỏ qua tee không hỗ trợ, tìm Y nghiêng phù hợp; không dừng vì chọn nhầm tee. |
| DN khác, hai Y gần tương đương, Pin/Group/tap, hoặc không đủ chiều dài | Báo rõ lý do và model không đổi. |
| Nhánh và tuyến chính khác System Type nhưng Revit cho phép nối | Không bị chặn bởi so sánh System Type; dùng cơ chế native như 1E1Y. Kiểm tra System Type/Classification sau nối vì Revit có thể hợp nhất hệ thống. Không ép đổi parameter trên toàn mạng. |
| Revit cảnh báo khác System Classification/không thể flow analysis | Rollback và thông báo nguyên nhân Revit; không bỏ qua cảnh báo tính toán hệ thống. |
| Xóa Pipe tạm phát sinh thêm transition/insulation/system tạm | Ngắt nhánh tạm khỏi Y trước; thử xóa rồi rollback, duyệt từng ID. Chỉ dọn phần tử mới đã chứng minh thuộc nhánh tạm. Y vẫn nối trực tiếp hai Pipe chính, không để lại Pipe thử. |
| Thử xóa ảnh hưởng phần tử model cũ, Y, Pipe chính/nhánh hoặc ID chưa xác nhận | Rollback toàn bộ. Log phải chỉ rõ ID/class/category và bước thất bại, không quy chung lỗi cho Routing Preferences. |
| Bắt đầu đường tự tạo Y và kiểm tra inventory sau regenerate | Không có lỗi `The collector does not have a filter applied`. Bản sửa dùng hai collector được lọc rõ ràng và hợp ID instance/type, vẫn bảo vệ cả phần tử model và type cũ. |
| Xóa Pipe tạm trả thêm Element nội bộ category Center Line | Chỉ cho dọn Center Line mới, đúng builtin category Pipe/Transition và được GetDependentElements xác nhận là con của đúng một root tạm; không có trong dependency của Y/main/split/source. Log phải ghi category ID và owner ID. Không bỏ chặn mọi Element/Center Line mới. |
| Hai connector Pipe/con thu bên trong cụm đã nối nhưng lệch origin từ trước (ví dụ cặp 42978276:0 ↔ 42978759:2, gap 2,101722 mm) | Được giữ nguyên khi cả hai owner thuộc cụm InnerIds ban đầu, nối hai chiều duy nhất, cùng DN tròn và trục đối hướng ≤5°. Không tự sửa gap cũ. Log ghi `connection/preserved-original-internal-offset`; sau xoay mọi cổng trong cụm (kể cả đầu Pipe nối con thu) và toàn bộ vector lệch phải đúng phép xoay/tịnh tiến đã tính trong 0,1 mm/0,5°. |
| Độ lệch phát sinh sau tạo Y, hoặc thay đổi chiều dài Pipe trung gian/vector lệch/hướng thân fitting khi xoay | Rollback. Bản ghi nhánh được lấy trước transaction/probe, không lấy lại sau tạo Y để hợp thức hóa thay đổi. Chỉ vị trí đầu Pipe hở được phép kéo/cắt tới Y; đầu còn lại phải theo đúng phép biến đổi cứng. Cả BasisX/Y fitting cũng phải đúng phép xoay (không tự roll thêm quanh tim); không bắt roll-frame X/Y của Pipe tròn sau chỉnh endpoint. |
| Connector lệch origin ở ranh giới cụm, tuyến ngoài, Y–Pipe chính hoặc Pipe nhánh–Y mới | Vẫn bắt trùng vị trí trong 0,5 mm và nối hai chiều duy nhất; không được dùng ngoại lệ của kết nối bên trong cụm. |

Sau thành công: ID/type fitting cũ không đổi, các Pipe trung gian không đổi chiều dài, chỉ các Pipe trục/ranh giới và Pipe cuối nhánh được phép chỉnh chiều dài. Khi dùng Y có sẵn, không thêm fitting mới. Khi tự tạo Y, chỉ có Y và đoạn Pipe tách mới; Pipe thử không được để lại. Hai đầu xa của tuyến chính và connector các phần tử bên ngoài phải đứng yên. Không được làm Pipe đảo chiều/quá ngắn. Nếu Revit tự đổi hình học fitting, mất kết nối, chèn fitting trung gian hoặc di chuyển tuyến ngoài, toàn bộ lệnh phải rollback.

Giới hạn: Y mới là nhánh 45°, phụ thuộc family/DN trong Routing Preferences của Pipe chính. Hai Pipe **không bắt buộc cùng System Type**; khả năng nối/hợp nhất hệ thống do Revit quyết định. Chỉ cảnh báo đúng `MismatchedSystemTypeWarning` ở mức Warning được cho qua và ghi nhận; khác System Classification hoặc cảnh báo/lỗi khác vẫn rollback. Tool không đổi góc/takeout **fitting cũ** để ép nối, không tự xóa elbow sát Y, không nối tee nhánh 90°. Nếu Revit không tạo trực tiếp Y45, tool thử Pipe tạm vuông góc rồi chỉnh connector Angle45/135 **chỉ trên Y mới**; family phải có liên kết tới parameter instance có thể ghi. Không sửa family/type chung. Y mới chỉ chèn trong đoạn Pipe được chọn, có khoảng lùi hai đầu tối thiểu 2 DN tuyến chính và thêm giới hạn theo takeout fitting thực. Khớp thứ hai hiện cần nối trực tiếp elbow–Pipe phía trước. Bước cao độ 0,5 mm chỉ thực hiện bằng thay đổi chiều dài dọc Pipe ranh giới hoặc Pipe trục nhánh để giữ đầu xa, không dịch chuyển cả mạng. Nếu trục không cho phép hoặc không đủ chiều dài thì bỏ phương án đó. Giới hạn chỉnh chiều dài cục bộ 5 m, giới hạn bước cao độ ±100 mm. Với hệ phức tạp hơn, lệnh phải từ chối an toàn.

Lưu ý: “bước cao độ” là phần tịnh tiến chỉnh cao độ có chủ đích; xoay cụm vẫn có thể làm các điểm trong cụm thay đổi Z theo hình học của phép xoay. Các lần thử dựa trên fitting thực được tạo trong SubTransaction rồi rollback; chỉ kết quả đã kiểm tra mới được commit.

Lưới cao độ/góc mịn hơn có thể chạy lâu hơn. Tool ưu tiên trị tuyệt đối cao độ nhỏ nhất trong các mức đã thử, nhưng không bảo đảm nghiệm tối ưu toàn cục hoặc chắc chắn giảm từ 40 mm về 0 mm: còn phụ thuộc hình học, fitting/takeout, chiều dài và phạm vi chèn Y. Bước dò roll 0,5° dùng 720 khoảng trên một vòng; nghiệm sau dò vẫn được tinh chỉnh bằng bisection/golden-section tối đa 48 lần. Các dung sai/kiểm tra kết nối và rollback của bản trước không đổi.

## Trở lại bản cũ

ZIP ngay trước bản dò mịn 0,5 mm/0,5°: `backups/DirectY-before-fine-search-20261001/TungMEP2-Revit2024.zip` (hash DLL `FAC4E4C4965A`, người dùng đã xác nhận nối thành công).

ZIP ngay trước bản sửa giữ nguyên offset nội bộ: `backups/DirectY-before-internal-binding-fix-20261001/TungMEP2-Revit2024.zip` (hash DLL `5D3DFC109734`, bản chẩn đoán).

ZIP ngay trước bản sửa dọn Pipe tạm/khác System Type: `backups/DirectY-before-cleanup-cross-system-20261001/TungMEP2-Revit2024.zip` (hash DLL `BCD17B71EAC3`).

ZIP ngay trước tính năng tự tạo Y: `backups/DirectY-before-auto-create-20261001/TungMEP2-Revit2024.zip` (hash DLL `6E559480FAEC`). ZIP v1 trước nâng cấp flexible: `backups/DirectY-before-flexible-20260930/TungMEP2-Revit2024.zip` (hash `61F0E309ECA6`).
Đóng Revit, giải nén ZIP cũ vào thư mục riêng, chạy `Install-TungMEP2.cmd`, rồi mở lại Revit.
Các thư mục DLL cũ được giữ nguyên; manifest trước tính năng tự tạo Y được backup trong `backups/DirectY-before-auto-create-20261001/TungMEP2.addin`.

## Log khi gặp lỗi

Mỗi lần tự tạo Y có log riêng tại `%LOCALAPPDATA%\TungMEP2\Logs\DirectY-*.log`. Thông báo lỗi có đường dẫn log nếu ghi được. Log gồm bước tạo/split/ngắt/xóa/commit, ID và lớp/category/type của các phần tử bị thử xóa, cùng System Type trước/sau. Binding ghi owner UID/type, connector ID, origin/trục, gap mm và kết nối hai chiều. `snapshotGapMm` trong payload chẩn đoán là khoảng cách khi key của chính giai đoạn đó được ghi nhận, không nhất thiết là baseline ban đầu. Cơ chế bảo vệ dùng bản ghi riêng bất biến trước transaction, không dùng payload log làm bằng chứng an toàn. So cùng cặp owner:port giữa các giai đoạn, cho phép thứ tự first/second đảo nhau. Khi vẫn thất bại, gửi log gần nhất cùng ảnh trước chạy; không cần gửi toàn bộ model. Log là chẩn đoán, không tự chứng minh kết nối đã đúng; vẫn kiểm tra model-copy theo bảng trên.
