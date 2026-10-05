# PipeSpacing: dàn khoảng cách dãy ống theo khoảng hở thực

Ngày: 2026-09-29

## Mục tiêu

Bổ sung lệnh `PipeSpacing` vào panel `PLUMBING` của TungMEP2 cho Revit 2024. Lệnh nhận một dãy Pipe gần song song, giữ cố định một ống biên theo quy tắc đã chốt, rồi dàn các cụm ống còn lại để đạt khoảng hở mép ống có kiểm soát. Khi Pipe đang nối với fitting hoặc nhánh liên quan, lệnh di chuyển cụm kết nối trong một ranh giới an toàn thay vì chỉ dịch riêng Pipe và làm đứt mạng.

Khoảng hở trong tài liệu này là khoảng cách ngắn nhất giữa hai envelope ngoài của hai tuyến kề nhau trong mặt phẳng dàn ống. Giá trị mặc định là cấu hình thực hành của dự án, không được trình bày như một trị số bắt buộc của TCVN.

## Cơ sở tiêu chuẩn và nguyên tắc cấu hình

- TCVN 4474:1987 là tiêu chuẩn thiết kế thoát nước bên trong; TCVN 4519:1988 là quy phạm thi công và nghiệm thu hệ thống cấp thoát nước bên trong.
- Các nội dung đã kiểm tra quy định đường kính, độ dốc, khoảng cách lỗ thăm/thông tắc, giá treo/điểm neo, khoảng cách đến kết cấu và yêu cầu thi công. Không dùng các khoảng cách này làm khoảng hở mặc định giữa hai ống song song.
- Phiên bản đầu không gắn nhãn `Theo TCVN` cho preset khoảng hở giữa các ống. Mọi preset được ghi rõ là `Project preset` hoặc `Custom`.
- Giá trị mặc định ban đầu là 75 mm vì phù hợp case study đã duyệt, nhưng người dùng có thể sửa và TungMEP2 ghi nhớ giá trị hợp lệ lần cuối.

Nguồn tra cứu:

- Viện Tiêu chuẩn Chất lượng Việt Nam, TCVN 4474:1987: <https://tieuchuan.vsqi.gov.vn/tieuchuan/view?sohieu=TCVN+4474%3A1987>
- Viện Tiêu chuẩn Chất lượng Việt Nam, TCVN 4519:1988: <https://tieuchuan.vsqi.gov.vn/tieuchuan/view?sohieu=TCVN+4519%3A1988>

## Phạm vi phần tử và lựa chọn

- Phiên bản đầu hỗ trợ `Autodesk.Revit.DB.Plumbing.Pipe` có `LocationCurve` là đường thẳng.
- Cho phép preselection rồi chạy lệnh hoặc chạy lệnh rồi chọn từ hai Pipe trở lên.
- Các Pipe phải gần song song trong dung sai góc cấu hình nội bộ. Pipe không đạt điều kiện được liệt kê trong preview/báo cáo và không tham gia tính vị trí.
- Hỗ trợ Pipe nằm ngang hoặc có slope. Không hỗ trợ Pipe đứng trong phiên bản đầu vì phương dàn trong active view không xác định ổn định theo cùng quy tắc.
- Không sửa Pipe trong Revit Group, Design Option không active, linked model hoặc phần tử không có quyền chỉnh sửa.
- Pipe bị Pin được coi là bất động. Nếu Pipe bị Pin là ống neo hợp lệ thì có thể dùng làm mốc; nếu thuộc cụm phải di chuyển thì cụm đó bị bỏ qua.

## Các chế độ khoảng cách

### 1. Equal Clear Gap

Đây là chế độ mặc định. Với hai tuyến kề nhau `i` và `j`, khoảng cách tim đích là:

`CenterDistance(i,j) = EffectiveRadius(i) + ClearGap(i,j) + EffectiveRadius(j)`

Trong đó:

- `EffectiveRadius` mặc định bằng một nửa đường kính ngoài thực tế của Pipe. Lệnh ưu tiên tham số built-in đường kính ngoài của Pipe/segment; nếu Revit type không cung cấp giá trị hợp lệ thì mới fallback sang đường kính lấy từ geometry. Không dùng DN danh nghĩa thay OD mà không cảnh báo.
- Khi bật `Include insulation`, cộng chiều dày insulation hiện hữu vào bán kính hiệu dụng.
- Khi bật `Include fitting envelope`, bán kính hiệu dụng của cụm theo phương dàn là giá trị lớn nhất giữa envelope Pipe và fitting thuộc vùng chạy song song đang xét.
- `ClearGap` mặc định là 75 mm và phải lớn hơn hoặc bằng 0.

Ví dụ với OD110, OD110, OD220, OD220 và khoảng hở 75 mm, các khoảng cách tim liên tiếp là 185 mm, 240 mm và 295 mm.

### 2. Equal Center Spacing

- Người dùng nhập một khoảng cách tim duy nhất.
- Mọi cặp tuyến kề nhau dùng cùng khoảng cách đó.
- Preview cảnh báo nếu khoảng hở thực của bất kỳ cặp nào nhỏ hơn 0 hoặc nhỏ hơn ngưỡng clearance cảnh báo.

### 3. DN Rule Table

- Người dùng khai báo các khoảng DN và khoảng hở tương ứng.
- Quy tắc cho một cặp được chọn theo đường kính danh nghĩa lớn hơn của hai Pipe.
- Nếu không có dòng nào khớp, dùng `Fallback clear gap` và hiển thị cảnh báo.
- Bảng là cấu hình TungMEP2 theo người dùng/project; không được ghi là bảng TCVN.

## Xác định trục dãy và thứ tự

1. Chuẩn hóa hướng tim của các Pipe để các vector không đối dấu tùy theo chiều vẽ.
2. Tính hướng chạy chung từ các Pipe hợp lệ. Nếu độ phân tán góc vượt dung sai thì dừng trước khi sửa model.
3. Chiếu hướng chạy chung lên mặt phẳng ngang. Vector dàn là vector ngang vuông góc với hướng chạy chung.
4. Chiếu midpoint hoặc điểm đại diện ổn định của từng Pipe lên vector dàn để lấy tọa độ có dấu.
5. Sắp các cụm theo tọa độ này và giữ nguyên thứ tự hình học hiện tại; lệnh không cho phép các tuyến vượt qua nhau.
6. Các Pipe thuộc cùng một cụm kết nối chỉ xuất hiện một lần trong danh sách dàn. Nếu người dùng chọn nhiều Pipe trong cùng cụm, chúng được hợp nhất và báo rõ trong preview.

## Quy tắc chọn ống neo

- Lấy đúng hai cụm ngoài cùng sau khi sắp thứ tự.
- So sánh đường kính ngoài hiệu dụng của Pipe đại diện ở hai biên.
- Giữ cố định cụm biên có đường kính ngoài lớn hơn.
- Nếu hai biên có cùng đường kính trong dung sai, dùng hướng hiển thị ổn định:
  - Tuyến gần ngang trong view: giữ biên phía dưới.
  - Tuyến gần đứng trong view: giữ biên phía trái.
  - Trường hợp xiên: giữ biên có tọa độ nhỏ hơn theo vector dàn đã chuẩn hóa từ trục X/Y của active view.
- Từ cụm neo, tính lần lượt vị trí đích của từng cụm kề tiếp theo sang phía đối diện. Cụm neo không được dịch chuyển hoặc thay đổi hình học.

## Thu thập cụm kết nối có ranh giới

Mỗi Pipe được chọn xác định một cụm có thể di chuyển:

- Bắt đầu từ Pipe đã chọn và duyệt qua connector vật lý trong domain piping.
- Thu thập các Pipe thẳng hàng/tiếp nối, coupling, union, transition/reducer và elbow thuộc cùng nhánh.
- Có thể đi qua nhiều đoạn Pipe và fitting liên tiếp nếu không gặp phần tử ranh giới.
- Dừng duyệt tại Tee, Wye, Cross, tap/takeoff, Pipe Accessory có nhiều hơn hai nhánh, Mechanical Equipment, Plumbing Fixture và mọi family có connector làm phát sinh nhánh dùng chung.
- Phần tử ranh giới và mạng phía sau nó không được đưa vào cụm di chuyển.
- Nếu hai cụm dự kiến chồng lấn phần tử hoặc dùng chung một phần tử di chuyển, lệnh dừng preview và yêu cầu người dùng chọn lại; không cố gắng tách một mạng mơ hồ.

Kết quả thu thập được chia thành hai tập rõ ràng:

- `Rigid core`: các Pipe/fitting được dịch cùng một vector và giữ nguyên tương quan hình học nội bộ.
- `Boundary stretch segments`: các đoạn Pipe nối từ rigid core tới phần tử ranh giới cố định. Các đoạn này không thuộc tập ID dịch cứng; chúng được hiệu chỉnh hai đầu sau khi rigid core đã dịch.

## Xử lý kết nối tại ranh giới

- Mỗi connector từ cụm di chuyển tới phần tử ranh giới được ghi lại trước khi sửa model.
- Khi cụm dịch chuyển, phần tử ranh giới đứng yên.
- Đoạn Pipe cuối cùng nối cụm với ranh giới được phân loại là `boundary stretch segment`: đầu phía ranh giới giữ nguyên, đầu phía cụm đi theo vector dịch chuyển. Nếu Pipe người dùng chọn chính là đoạn nối duy nhất giữa hai ranh giới cố định thì nó không có rigid core hợp lệ và cụm bị đánh dấu `Constrained`.
- Lệnh điều chỉnh `LocationCurve` của boundary stretch segment để nối lại hai đầu, thay vì dịch đoạn này như một khối cứng.
- Không tự ngắt hoặc thay Tee/Wye/Cross; không thay Routing Preferences và không nạp family.
- Nếu việc co giãn tạo đoạn quá ngắn, đổi hướng ngoài dung sai, tạo self-intersection, làm connector không đồng vị trí hoặc Revit từ chối tái kết nối, toàn bộ cụm đó rollback.
- Nếu một cụm có hai hoặc nhiều ranh giới cố định khiến phép dịch không thể thỏa đồng thời, preview đánh dấu `Constrained` và mặc định bỏ qua cụm đó.

## Biến đổi hình học

- Mỗi cụm được dịch bằng một vector duy nhất song song với vector dàn; không xoay cụm.
- Thành phần Z của vector dịch bằng 0 để bảo toàn cao độ, BOP/TOP và slope của các Pipe chính.
- Sau khi dịch cụm, lệnh regenerate, hiệu chỉnh các boundary stretch segment và regenerate lần nữa.
- Kiểm tra hậu điều kiện:
  - Kết nối nội bộ của cụm vẫn còn.
  - Mọi connector ranh giới cần giữ đều được nối.
  - Slope của Pipe chính không đổi trong dung sai.
  - Thứ tự cụm không đổi.
  - Khoảng hở đạt mục tiêu trong dung sai hình học.
- Không tự thêm offset, elbow mới hoặc đoạn ống bù trong phiên bản đầu.

## Preview và giao diện

Hộp thoại `Tung MEP - Pipe Spacing` gồm:

- `Mode`: Equal Clear Gap / Equal Center Spacing / DN Rule Table.
- `Clear gap (mm)` hoặc `Center spacing (mm)` tùy mode.
- `Include insulation`.
- `Include fitting envelope`.
- `Angle tolerance` hiển thị trong Advanced settings.
- `Preview result before Apply`, mặc định bật.
- Bảng preview: thứ tự, Pipe Id đại diện, DN, OD hiệu dụng, vị trí hiện tại, vị trí đích, độ dịch, trạng thái kết nối và cảnh báo.
- Các nút `Preview`, `Apply`, `Cancel`, `Reset defaults`.

Preview dùng hình học tạm/transaction rollback, không để lại phần tử model. Màu preview:

- Xanh: cụm hợp lệ và sẽ di chuyển.
- Xám: cụm neo cố định.
- Vàng: có cảnh báo nhưng có thể xử lý.
- Đỏ: cụm bị bỏ qua hoặc ràng buộc không an toàn.

## Transaction, rollback và Undo

- Thu thập selection, phân cụm, kiểm tra ranh giới và tính toàn bộ vị trí đích trước khi sửa model.
- Apply chạy trong một `TransactionGroup` để người dùng chỉ cần một lần Undo.
- Mỗi cụm di chuyển trong một `SubTransaction`. Cụm lỗi rollback riêng, các cụm độc lập còn lại có thể tiếp tục nếu không phụ thuộc vị trí của cụm lỗi.
- Nếu lỗi ở cụm làm mất chuỗi thứ tự hoặc khiến khoảng cách của các cụm sau không còn xác định đúng, rollback toàn bộ Apply.
- Preview luôn rollback hoàn toàn.
- Không để lại pipe/fitting tạm, connector hở mới hoặc transaction chưa đóng.

## Thông báo kết quả

Sau Apply hiển thị tóm tắt:

- Số Pipe người dùng chọn và số cụm duy nhất.
- Cụm neo và lý do được chọn.
- Số cụm/Pipe/fitting đã di chuyển.
- Khoảng hở nhỏ nhất sau xử lý.
- Số boundary segment đã co giãn.
- Danh sách cụm bị bỏ qua kèm lý do: không song song, Pipe đứng, Pin/Group, chung cụm, ranh giới kép, đoạn nối quá ngắn, connector không khôi phục được hoặc Revit từ chối hình học.

## Ghi nhớ cấu hình

- Ghi nhớ mode, khoảng hở/khoảng cách tim, hai tùy chọn envelope và bảng DN hợp lệ lần cuối.
- Cấu hình phải có version để nâng cấp an toàn khi schema thay đổi.
- Giá trị sai, âm hoặc file cấu hình hỏng phải quay về mặc định mà không làm command crash.

## Tương thích và ribbon

- Thêm button `PipeSpacing` vào panel `PLUMBING`, không đổi tên hoặc thứ tự hành vi của các command hiện có.
- Tooltip mô tả rõ `Dàn các dãy Pipe song song theo khoảng hở mép hoặc khoảng cách tim; giữ một ống biên và bảo toàn cụm kết nối trong ranh giới an toàn.`
- Command chỉ chạy trong document project có thể chỉnh sửa; không chạy trong family document.
- Không phụ thuộc pyRevit để command hoạt động.

## Kiểm thử tự động

- Công thức khoảng cách cho OD110/OD110/OD220/OD220 với gap 75 mm trả 185/240/295 mm.
- Chọn đúng hai biên và neo biên có OD lớn hơn.
- Tie-break bằng view axis cho hai biên cùng OD.
- Giữ nguyên thứ tự khi tính vị trí từ neo ở một trong hai phía.
- DN Rule Table chọn đúng band và fallback.
- Collector đi qua coupling/reducer/elbow nhưng dừng tại Tee/Wye/Cross/equipment/fixture.
- Hợp nhất nhiều Pipe được chọn thuộc cùng cụm.
- Phát hiện hai cụm chồng lấn hoặc ranh giới kép bị constraint.
- Bảo toàn Z/slope trong phép dịch ngang.
- Parse/validate/upgrade cấu hình và fallback khi cấu hình lỗi.
- Source contract xác nhận button được đăng ký trong panel `PLUMBING`.

## Kiểm thử trong Revit 2024

1. Bốn Pipe độc lập OD110/OD110/OD220/OD220: gap 75 mm cho khoảng cách tim 185/240/295 mm.
2. Hai biên khác OD: biên lớn hơn đứng yên; toàn bộ dãy dàn từ biên đó.
3. Hai biên cùng OD: tie-break đúng theo active view và kết quả lặp lại ổn định.
4. Pipe có slope khác nhau nhưng cùng hướng plan: dịch ngang, giữ nguyên slope và Z từng tuyến.
5. Pipe có insulation: bật/tắt option làm thay đổi đúng khoảng hở tính toán.
6. Cụm có coupling/reducer/elbow: toàn cụm cùng di chuyển và connector nội bộ không đứt.
7. Cụm kết thúc ở Tee/Wye: fitting dùng chung đứng yên, boundary segment co giãn và nối lại hợp lệ.
8. Cụm bị giữ ở hai ranh giới cố định: preview đỏ, model không đổi.
9. Pipe Pin/Group/không song song/Pipe đứng: thông báo đúng và không sửa model.
10. Cancel preview và Undo sau Apply: model trở về đúng trạng thái trước lệnh.
11. Thiếu quyền chỉnh sửa/worksharing conflict: báo rõ và không để transaction dở dang.
12. Chạy lặp lại trên kết quả vừa dàn: không phát sinh dịch chuyển ngoài dung sai.

## Backup và phát hành

- Trước khi sửa mã, tạo backup có timestamp trong `C:\Users\tungnt2303\Downloads\260515_Release\backups`.
- Backup tối thiểu gồm source/ribbon hiện tại, DLL build/release, ZIP/bộ cài, file cấu hình và manifest `TungMEP2.addin` đang cài; kèm `RESTORE.txt`.
- Build và chạy automated checks trước khi đồng bộ bản cài Revit 2024.
- Nếu Revit đang mở và khóa DLL, chỉ công bố lệnh sẵn sàng sau khi người dùng đóng/mở lại Revit hoặc dùng cơ chế Reload TungMEP2 tương thích.

## Ngoài phạm vi phiên bản đầu

- Duct, conduit, cable tray và fabrication parts.
- Pipe cong, flex pipe và Pipe đứng.
- Tự thiết kế offset mới hoặc thêm elbow để giải một ranh giới không thể co giãn.
- Tự đổi DN, slope, Pipe Type, System Type hoặc Routing Preferences.
- Tự suy diễn khoảng hở bắt buộc từ TCVN khi tiêu chuẩn không cung cấp quy tắc đó.
- Dàn cả mạng qua Tee/Wye/Cross hoặc di chuyển equipment/plumbing fixture.
