# PipeLayer — TungMEP2 / PLUMBING

## Cách dùng

1. Lưu model; sau khi cài bản mới, đóng hẳn rồi mở lại Revit 2024 để nạp DLL.
2. Chọn trước ít nhất hai Pipe. Có thể chọn ống khác phương, kể cả vuông góc; hỗ trợ ống thẳng nằm ngang/ống dốc, không yêu cầu song song.
3. Chạy **TungMEP2 → PLUMBING → PipeLayer**.
4. Bấm một Pipe trong vùng chọn làm ống gốc, tại vị trí muốn so cao độ.
5. Chọn **BOP** (đáy ngoài, mặc định), **COP** (tim), hoặc **TOP** (đỉnh ngoài). Cancel/đóng hộp thoại không làm đổi model.
6. Nếu có ống cần dịch, chọn cách xử lý kết nối:
   - **Giữ kết nối** (mặc định): mất nối hoặc lệch cổng thêm thì hoàn tác toàn bộ.
   - **Ưu tiên căn cao độ — Cho phép đầu hở**: giữ kết quả căn khi Revit làm mất nối hoặc thay đổi khe/hướng cổng. Báo số cặp cổng cần kiểm tra, tối đa 8 cặp trong hộp thoại, đầy đủ trong log. Không chủ động tháo nối và không tự nối lại.

Cả hai chế độ vẫn từ chối sai cao độ đích, lệch ngang/độ dốc ống đã chọn, thay đổi ống gốc, phần tử bị xóa/đổi loại, đổi host, kéo phần tử bị khóa hoặc nối sang cổng mới. Chế độ cho phép đầu hở không đảm bảo mọi cụm đều dịch được: lỗi thực sự do Revit hoặc ràng buộc hình học vẫn có thể buộc hoàn tác.

Log kết nối: `%LOCALAPPDATA%\TungMEP2\Logs\PipeLayer-*.log` (file UTF-8 riêng mỗi lần có vấn đề). Ghi ID hai cổng, trạng thái còn nối/mất nối, khe hở trước/sau (mm), góc lệch trước/sau (độ) và độ đổi vector lệch. Nếu lệnh tự rollback, số đo được chụp trước hoàn tác. Nếu Revit đã tự rollback lúc commit/lưu nhóm, log dùng snapshot gần nhất trước bước đó và ghi rõ thời điểm; không khẳng định đã chụp được hình học trung gian bên trong solver. Lỗi ghi log không làm đổi quyết định; khi thành công có vấn đề mà log không ghi được, hộp thoại báo lỗi lưu và hiển thị đủ các cặp thay vì cắt ở 8. Đây là log văn bản nhỏ, không chứa bản sao model/PDF.

Ống gốc đứng yên. Trục ống gốc được chiếu theo mặt bằng tới điểm bấm và chặn trong đoạn thật. Mỗi Pipe còn lại lấy điểm gần nhất theo mặt bằng tới điểm trên trục gốc đó, cũng chặn trong đoạn thật. Tool căn cao độ tại những điểm đo này, không kéo dài trục ảo và không hứa toàn bộ chiều dài hai ống khác độ dốc có cùng đáy/đỉnh.

Công thức cao độ tại vị trí đo: BOP = Z tim − OD/2; COP = Z tim; TOP = Z tim + OD/2. OD là đường kính ngoài của Pipe, không phải DN và không bao gồm bảo ôn. Lệnh chủ động tịnh tiến theo Z; Revit được phép co/giãn hai đầu **ống cần căn** dọc trục 3D gốc, giữ hướng/độ dốc, trục mặt bằng và điểm đo ban đầu. Không giữ cứng chiều dài các ống cần căn nữa. **Ống gốc luôn giữ nguyên cả hai đầu**, không cho co/giãn hoặc dịch. Sai số giữ hình học/điểm nối: 0,05 mm.

Từ bản sửa ngày 2026-10-05, người dùng cho phép Revit kéo theo các Pipe/fitting/phụ kiện liên quan ngoài vùng chọn, kể cả co/giãn và đổi độ dốc của **Pipe ngoài vùng chọn** để giữ kết nối. Bản bổ sung cùng ngày cho phép **ống cần căn đã chọn** co/giãn dọc trục như trên; không cho đổi hướng/độ dốc hoặc lệch ngang. Tool không chủ động dịch toàn mạng; nó giữ lại điều chỉnh mà Revit thực hiện nếu các kiểm tra an toàn vẫn đạt. Thông báo thành công ghi số Pipe/family liên quan đã thay đổi và tối đa 8 ID.

Điểm đo/cao độ đích được chụp trước thao tác và kiểm tra lại trước/sau commit. Nếu Pipe co ngắn/trượt đến mức không còn đoạn thật tại điểm đo, phải rollback; không dùng trục kéo dài ảo để báo căn thành công. Với ống dốc đã co/giãn, phần dịch Z còn lại được đo tại cùng XY trên trục gốc, không lấy trung bình Z hai đầu. Một ống đã đúng cao độ (delta0) không tự trở thành ống gốc: nếu không bị khóa, nó vẫn được co/giãn dọc trục khi cần giữ kết nối.

## Kiểm tra trên bản sao model

Các dòng yêu cầu giữ topology/gap/axis trong bảng dưới áp dụng cho **Giữ kết nối**, trừ dòng ghi rõ **Cho phép đầu hở**. Các chốt khác áp dụng cho cả hai chế độ.

Chưa có kiểm chứng tự động trong Revit đang mở. Build và kiểm tra toán học không thay thế các bước dưới đây.

| Trường hợp | Kết quả cần thấy |
| --- | --- |
| Hai ống ngang song song, OD khác nhau | BOP: đáy bằng nhau; COP: tim bằng nhau; TOP: đỉnh bằng nhau. Ống gốc đứng yên. |
| Ống khác phương 30°, 45°, 90°, 135°, gần 90° | Đều tính được cao độ; không âm thầm bỏ qua ống vuông góc. |
| Ống dốc, chọn gốc gần đầu/giữa/cuối | Căn đúng tại vị trí đo; độ dốc giữ nguyên, ống cần căn được co/giãn dọc trục khi vẫn phủ điểm đo. |
| Đoạn ống ngắn không đi qua mặt phẳng cắt cũ | Dùng điểm gần nhất trong đoạn thật, không tính tại phần kéo dài. |
| Chọn BOP/COP/TOP với ống OD 200 và 100, tim ban đầu 1000/1400 mm | Với vị trí đo ngang: delta của ống 100 là −450/−400/−350 mm tương ứng. |
| Cancel khi chọn gốc hoặc đóng hộp thoại kiểu căn | Model không thay đổi; không có Undo mới. |
| Hộp thoại BOP/COP/TOP sau khi chọn gốc | Mở đủ 3 lựa chọn, BOP là mặc định; không còn lỗi `Corresponding button not found / defaultButton`. |
| Các ống đã đúng cao độ | Thông báo đã đúng (≤ 0,05 mm), không mở transaction để tạo Undo rỗng. |
| Ống cần dịch bị Pin/Group hoặc do người khác sở hữu | Từ chối trước khi sửa, ghi Pipe ID. |
| Pipe đứng/không có station mặt bằng duy nhất, Pipe cong hoặc thiếu OD | Từ chối có lý do, không bỏ qua âm thầm. Nếu ống gốc thiếu OD, phải báo đúng nguyên nhân đường kính ngoài và ID, không báo chung lỗi điểm bấm/trục. |
| Mạng nối có tap/cổng Curve/Physical, FlexPipe hoặc FabricationPart | Từ chối trước khi sửa với ID và lý do; bộ bảo vệ hiện chỉ chấp nhận mạng Pipe/family có cổng End. |
| Cụm có fitting nối vào các Pipe đã chọn | Nếu Revit tự điều chỉnh fitting an toàn: giữ đúng topology/port ID/type/DN/System Type. Co/giãn Pipe cần căn dọc trục không phải lỗi; bị hở, lệch thêm, đổi trục/độ dốc/cao độ đích vẫn rollback. |
| Trường hợp ảnh lỗi cũ “Ống ngoài vùng chọn ... bị Revit kéo theo” | Không rollback chỉ vì Pipe liên quan ngoài vùng chọn dịch/co giãn/đổi độ dốc. Nếu topology và ràng buộc còn hợp lệ, phải căn thành công và báo số/ID phần tử liên quan thay đổi. |
| Revit kéo/co giãn/đổi độ dốc ống gốc | Rollback, báo rõ **Ống gốc + ID**; phải giữ nguyên hai đầu. |
| Revit co/giãn dọc trục Pipe cần căn, trước hoặc lúc commit | Chấp nhận nếu còn điểm đo và đúng datum, hướng/độ dốc, kết nối. Báo thành công không buộc chiều dài cũ. |
| Pipe cần căn lệch ngang, sai cao độ, đổi độ dốc hoặc mất điểm đo | Rollback, báo **Ống cần căn + ID** và loại sai lệch/mức lệch khi đo được; không báo chung “bao gồm ống gốc”. |
| Một Pipe đã chọn có delta0 nhưng không phải ống gốc | Có thể co/giãn dọc trục khi không khóa; không phân loại ống gốc chỉ bằng delta0. |
| Pipe/family liên quan bị Pin/Group hoặc do người khác sở hữu | Không cho dịch/đổi hình học phần tử được bảo vệ; nếu Revit kéo theo thì rollback toàn bộ. |
| Phụ kiện không có cổng, gắn bằng Host/HostFace/SuperComponent hoặc quan hệ dependent | Phải nằm trong snapshot. Phụ kiện không khóa được phép theo host; phụ kiện khóa bị kéo, bị xóa, đổi loại hoặc mất/đổi quan hệ host phải rollback. |
| Fitting lồng/shared có family cha là thiết bị độc lập không có cổng | Phải chụp cả family cha qua Host/SuperComponent. Thiết bị cha không được dịch chỉ vì fitting con nối vào Pipe; nếu bị kéo thì rollback. |
| Có khe connector/độ lệch trục từ trước | Không quy lỗi cho độ lệch cũ giữ nguyên; phải giữ nguyên vector lệch trong 0,05 mm và không lệch trục thêm quá 0,1°. Không tự sửa lỗi cũ. |
| Revit phát sinh cảnh báo/lỗi khi commit | Giữ kết nối: mọi cảnh báo/lỗi đều rollback. Cho phép đầu hở: không xóa cảnh báo, chuyển về xử lý native/modal của Revit; lỗi vẫn rollback. Sau commit kiểm tra lại các chốt hình học/cấu trúc. |
| Chọn Cho phép đầu hở, có mất nối trước/sau commit | Nếu các chốt còn đạt: giữ kết quả đúng cao độ, báo ID hai cổng và log đầy đủ; không tự nối lại. |
| Chọn Cho phép đầu hở, cổng còn nối nhưng tăng khe/đổi hướng | Giữ kết quả nếu các chốt còn đạt, báo rõ còn nối nhưng hình học cổng thay đổi cùng số đo. Không khẳng định đây là đầu hở topology. |
| Đóng/Cancel hộp thoại cách xử lý kết nối | Không mở transaction, không thay đổi model. |
| Cho phép đầu hở nhưng ống gốc bị dịch, sai cao độ/độ dốc, xóa fitting hoặc nối cổng mới | Vẫn rollback toàn bộ, không báo thành công chỉ vì người dùng chấp nhận mất nối. |
| Thành công rồi Undo | Một Undo hoàn tác toàn bộ thao tác; ID/type/đường kính/hệ thống và các kết nối vẫn như trước. |

Kiểm tra kết nối bằng Tab/connector/system inspection, không chỉ nhìn hình 3D. Không có kiểm tra va chạm vật thể hoặc khoảng hở với mạng khác trong PipeLayer này; dùng PipeSpacing/FitSpace cho mục đích khoảng cách.

PipeAccessory/fitting không khóa và các family gắn host/phụ thuộc vào cụm được phép theo Revit khi còn nguyên kết nối, loại, DN, System Type và quan hệ host. Thiết bị độc lập không thuộc các loại trên vẫn là ranh giới cố định, kể cả family cha của fitting lồng. Pipe bị Pin/Group/OwnedByOtherUser phải giữ trục; family được bảo vệ phải giữ transform và vị trí/hướng cổng. Cổng Curve/tap là kết nối vật lý cần bảo vệ, không được bỏ qua như logical refs ([hướng dẫn connector Revit 2024](https://help.autodesk.com/cloudhelp/2024/ENU/Revit-API/files/Revit_API_Developers_Guide/Discipline_Specific_Functionality/MEP_Engineering/Revit_API_Revit_API_Developers_Guide_Discipline_Specific_Functionality_MEP_Engineering_Connectors_html.html)); phiên bản này vẫn từ chối mạng có những cổng đó thay vì dịch không đủ kiểm tra.

Giới hạn snapshot family: kiểm tra riêng vai trò Host/HostFace/SuperComponent và ID phần tử cha, chưa phân biệt hai mặt khác nhau trên cùng một host/host link. Chưa kiểm tra biến dạng tham số tùy ý (ví dụ chiều dài support không có cổng thay đổi trong khi transform giữ nguyên), hoặc topology của cổng ngoài DomainPiping. Các trường hợp này cần kiểm tra thêm trên model-copy; không coi test console là chứng nhận giữ toàn bộ hình học/cổng của mọi family.

## Kiểm tra tự động

`.\.dotnet\dotnet.exe run --project tools\PipeLayerMathConsoleTest.csproj -c Release`

`.\.dotnet\dotnet.exe run --project tools\PipeLayerDialogConsoleTest.csproj -c Release`

`.\.dotnet\dotnet.exe run --project tools\PipeLayerSafetyConsoleTest.csproj -c Release`

Bộ kiểm tra dùng trực tiếp mã toán học/policy trong sản phẩm: các hướng 0–359°, BOP/COP/TOP, độ dốc, chặn station trong đoạn thật, giữ phép dịch Z, gap/trục connector không xấu thêm và dữ liệu không hợp lệ. Revit transaction/solver/connector extraction vẫn cần smoke test native trên model-copy.

Test hộp thoại gọi trực tiếp mã tạo dialog/mapping lựa chọn của sản phẩm qua một contract double hẹp cho TaskDialog (RevitAPIUI không nạp được ngoài host Revit trên máy này). Double chặn đặt default khi nút tương ứng chưa được đăng ký, đúng exception đã thấy trong Revit 2024. Test đã tái hiện lỗi trước sửa và kiểm tra BOP/COP/TOP/Cancel/đóng sau sửa; không tự hiển thị hoặc xác nhận rendering của UI native. Bản sửa đăng ký command links trước khi gán default, cùng thứ tự trong [ví dụ Autodesk Revit 2024](https://help.autodesk.com/cloudhelp/2024/ENU/Revit-API/files/Revit_API_Developers_Guide/Introduction/Add_In_Integration/Revit_API_Revit_API_Developers_Guide_Introduction_Add_In_Integration_Revit_style_Task_Dialogs_html.html).

Test runner/safety liên kết trực tiếp Runner, Safety, Math và các helper connector/snapshot của sản phẩm. Boundary double chỉ cung cấp dữ liệu element/host/connector và thay đổi lúc Move/Commit để kiểm tra quyết định chấp nhận, từ chối, báo lỗi và gọi rollback. Nó không giải fitting, không chứng minh hành vi native Revit/Undo/worksharing/host propagation. RED đã tái hiện chốt cấm Pipe liên quan dịch, host bị khóa bị bỏ sót và mất thông báo OD; GREEN kiểm tra chính những nhánh này cùng các ràng buộc cần giữ.

RED bổ sung: real Runner từ chối Pipe đã chọn co/giãn an toàn với chính lỗi rigid-endpoint trong ảnh. GREEN kiểm tra co/giãn trước/sau commit, trục dốc đã co/giãn trước khi được dịch, delta0 không nhầm ống gốc, BOP/TOP với OD khác nhau qua real Runner, và giữ chốt cho ống gốc/khóa/mất điểm đo/sai datum/hở cổng. Đây là kiểm tra policy trên dữ liệu quan sát, không tái hiện fitting solver của model thật. Nhật ký native đã xác định lần lỗi ảnh có ống gốc 43090390 và ống cần căn 43089744; chưa có số liệu endpoints để xác nhận sai lệch cụ thể của lần đó.

## Backup / khôi phục

Backup trước nâng cấp: `backups\PipeLayer-before-multi-direction-20261001\TungMEP2-Revit2024.zip`, DLL cũ `D22141BBFA22`.
Backup ngay trước bản sửa cho phép cụm liên quan dịch: `backups\PipeLayer-before-related-movement-20261005\TungMEP2-Revit2024.zip`, gồm bản TungMEP2 đang cài và dependency PrintSheets hiện tại.
Backup ngay trước bản sửa cho phép ống cần căn co/giãn: `backups\PipeLayer-before-selected-resize-20261005\TungMEP2-Revit2024.zip`, DLL cũ `300FBA41938D-899CFE674CE1`, đã có Auto90.
Backup trước khi thêm hai chế độ kết nối: `backups\PipeLayer-connection-modes-20261005-111839\TungMEP2-Revit2024.zip`, DLL `B8290B12CBAA-899CFE674CE1`; gồm source/tools/release, manifest và toàn bộ payload đang cài.
Đóng Revit, giải nén ZIP backup vào thư mục riêng, chạy `Install-TungMEP2.cmd`, mở lại Revit. Các thư mục DLL hash cũ không bị ghi đè.
