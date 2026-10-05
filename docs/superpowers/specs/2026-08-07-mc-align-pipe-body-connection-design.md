# MC-Align: nối nhánh vào thân ống kín hai đầu

Ngày: 2026-08-07

## Mục tiêu

Mở rộng riêng lệnh `MC-Align` để một nhánh Pipe còn đầu hở có thể được căn và nối vào thân của một ống mốc dù hai đầu ống mốc đã có kết nối. Lệnh vẫn ưu tiên kết nối đầu hở với đầu hở như hiện tại, không tự tháo bất kỳ kết nối đang tồn tại nào và không thay đổi hành vi của `MoveConn` hoặc `AlignMEP3D`.

## Thứ tự chọn và phạm vi

- Phần tử chọn thứ nhất là phần tử mốc và không di chuyển.
- Phần tử chọn thứ hai xác định cụm liên thông cần xoay/dịch chuyển.
- Hành vi nối vào thân chỉ kích hoạt khi phần tử mốc là `Autodesk.Revit.DB.Plumbing.Pipe` có `LocationCurve` thẳng và phần tử thứ hai được chọn cũng là Pipe có connector đầu mút còn hở. Không tự tìm một đầu hở bất kỳ trên phần tử khác ở xa trong cụm.
- Duct, Conduit, Cable Tray, FabricationPart và MEP family vẫn dùng cơ chế connector-to-connector hiện tại; không tự cắt thân các loại này trong phạm vi nâng cấp này.
- `MoveConn` giữ nguyên hành vi cũ. `AlignMEP3D` tiếp tục chỉ căn hình học, không tạo kết nối.

## Chính sách lựa chọn chế độ kết nối

`MC-Align` chọn đúng một trong ba chiến lược theo thứ tự sau:

1. **Kết nối đầu hở:** nếu tồn tại cặp connector vật lý cùng domain và cả hai đều hở, chọn cặp phù hợp gần nhất, xoay/dịch cụm và nối như phiên bản hiện tại. Chiến lược này luôn ưu tiên hơn nối vào thân, kể cả khi thân ống nằm gần hơn.
2. **Nối vào thân Pipe:** nếu ống mốc không còn connector đầu mút hở phù hợp nhưng Pipe được chọn thứ hai có connector đầu mút còn hở, căn cụm tới một điểm nội bộ trên tim ống mốc, cắt ống mốc và chèn Tee/Y.
3. **Chỉ căn cụm:** nếu phần tử thứ hai không có đầu Pipe hở, dùng fallback connector đã kết nối hiện tại để căn cả cụm nhưng không tạo kết nối mới. Không tự cắt, tháo hoặc thay các kết nối của cụm.

Connector logical bị loại khỏi cả ba chiến lược. Domain phải là piping và tương thích.

## Xác định điểm nối trên thân ống

- Chiếu origin của connector Pipe hở thuộc cụm di chuyển lên đoạn tim hữu hạn của ống mốc.
- Điểm nối là điểm chiếu gần nhất nằm thực sự trong phần thân ống, không phải hai đầu mút.
- Cao độ của điểm nối lấy chính xác theo tim ống mốc tại vị trí đó. Vì vậy với ống ngang, nhánh được đưa về cùng cao độ; với ống đứng hoặc ống dốc, cao độ lấy theo điểm tương ứng trên tim ống.
- Không tự dồn điểm nối sang một vị trí tùy ý nếu điểm chiếu quá sát đầu ống hoặc không đủ chiều dài để đặt fitting. Lệnh báo rõ rằng vị trí không đủ khoảng trống và rollback.
- Phiên bản này không thêm bước pick point thứ ba; vị trí được suy ra tự động từ đầu hở gần nhất của cụm di chuyển.

## Căn hướng và di chuyển cụm

- Ống mốc luôn đứng yên.
- Cụm di chuyển được thu thập bằng cơ chế `MepConnectedClusterCollector` hiện tại và biến đổi như một khối cứng.
- Chỉ các connector đầu mút hở trên Pipe được chọn thứ hai được xét; connector gần điểm nối nhất được chọn làm connector nhánh.
- Khi nối vào thân, giữ nguyên hướng và độ dốc hiện tại của nhánh; chỉ dịch cả cụm để origin connector nhánh tới điểm nối. Không tự xoay cụm sang một hướng tùy ý vì thân ống không có connector mốc để xác định duy nhất hướng xoay.
- Nếu hướng nhánh song song/gần song song với tim ống mốc hoặc Routing Preferences không hỗ trợ góc hiện tại, lệnh báo lỗi và rollback. Tool không tự bẻ nhánh hoặc tạo đoạn ống trung gian trong phiên bản này.
- Origin của connector nhánh được đưa đúng tới điểm nối trên tim ống mốc. Sau mỗi phép xoay/dịch, Revit regenerate và connector được tìm lại theo owner, domain và vị trí dự kiến.
- Mọi kết nối nội bộ đang có trong cụm phải được giữ nguyên. Nếu cụm chứa phần tử Pin, phần tử trong Group, phần tử mốc hoặc phần tử không thể biến đổi an toàn, lệnh dừng trước khi sửa model.

## Chèn Tee/Y và xử lý khác đường kính

- Cắt ống mốc bằng `PlumbingUtils.BreakCurve` tại điểm nối.
- Lấy hai connector mới của hai đoạn ống chính và connector hở của nhánh để tạo fitting bằng `Document.Create.NewTeeFitting` theo Routing Preferences của Pipe Type trong project.
- Ưu tiên reducing Tee/Y khi family và bảng size trong Routing Preferences hỗ trợ trực tiếp.
- Nếu reducing Tee/Y không tạo được, tạo Tee/Y theo DN ống chính rồi chèn `NewTransitionFitting` trên nhánh bằng cơ chế auto-transition đã có trong `PipeUtilities`.
- Tool không nạp family, không đổi Pipe Type, không chỉnh Routing Preferences và không tự thay tên fitting.
- Nếu cả direct fitting và auto-transition đều thất bại, trả thông báo có nguyên nhân khả dụng (thiếu family/size/routing, góc không hợp lệ hoặc thiếu chiều dài) và rollback toàn bộ.

## Transaction và rollback

- Thu thập cụm, chọn chiến lược và kiểm tra Pin/Group/cùng mạng được thực hiện trước thay đổi hình học khi có thể.
- Xoay cụm, dịch cụm, break ống, tạo fitting và transition nằm trong một transaction chính.
- Có thể dùng `SubTransaction` để thử direct reducing Tee trước rồi fallback sang Tee cùng DN + transition, nhưng không được commit trạng thái trung gian ra ngoài transaction chính.
- Bất kỳ lỗi nào cũng khôi phục đầy đủ: vị trí cụm, ống mốc nguyên vẹn và tất cả kết nối như trước khi chạy lệnh.
- Sau khi tạo fitting, xác nhận ba nhánh của Tee/Y đã thực sự kết nối và connector nhánh không còn hở trước khi commit.

## Thông báo người dùng

- Endpoint thành công: không thêm hộp thoại; vòng chọn tiếp tục như hiện tại.
- Body connection thành công: không thêm hộp thoại; vòng chọn tiếp tục.
- Các lỗi cần phân biệt: nhánh không có đầu Pipe hở, điểm nối không nằm trong thân hữu hạn, không đủ khoảng trống gần đầu ống, main và branch có hình học không phù hợp, thiếu routing family/size, không tạo được reducer, cụm có Pin/Group và hai phần tử thuộc cùng mạng.
- `Esc` kết thúc lệnh như hiện tại.

## Tương thích ngược

- Cặp đầu hở–đầu hở vẫn luôn thắng và chạy đúng luồng hiện tại.
- Trường hợp không có đầu hở ở cụm di chuyển vẫn chỉ căn cụm, không tự sửa mạng.
- Không đổi tên command, ribbon button, icon, tooltip ID hoặc khả năng gán phím tắt hiện có.
- Tooltip của `MC-Align` được cập nhật để nói rõ ưu tiên đầu hở và khả năng chèn Tee/Y vào thân Pipe kín hai đầu.

## Kiểm thử tự động

- Chính sách chiến lược trả về endpoint connection khi có cặp hở–hở, bất kể body candidate gần hơn.
- Chính sách trả về body connection chỉ khi fixed element là Pipe hợp lệ, không có fixed endpoint hở phù hợp và moving side có Pipe endpoint hở.
- Chính sách trả về align-only khi moving side không có đầu hở.
- Hàm hình học thuần xác định đúng điểm chiếu nội bộ cho ống ngang, đứng và dốc; từ chối điểm ngoài đoạn hoặc quá sát đầu.
- Hàm hình học phát hiện hướng nhánh song song/gần song song với main và từ chối body connection.
- Regression tests giữ nguyên chính sách connected-cluster, same-network, Pin/Group và các mode không được tạo kết nối.
- Source contract xác nhận body connection chỉ được gọi trong `MoveConnectAlign` và có `BreakCurve`, `NewTeeFitting`, kiểm tra hậu điều kiện trước commit.

## Kiểm thử trong Revit 2024

1. Main và branch đều có đầu hở: nối đầu–đầu như cũ, không cắt thân main.
2. Main kín hai đầu, branch có một đầu hở, cùng DN: cụm được căn, main bị tách đúng vị trí và Tee/Y được tạo.
3. Main kín hai đầu, branch khác DN: tạo reducing Tee/Y hoặc Tee/Y + transition; không để branch hở.
4. Main ngang, đứng và dốc: điểm nối nằm trên tim main và đúng cao độ tại điểm nối.
5. Điểm dự kiến quá sát fitting/đầu đoạn: lệnh báo lỗi và model không đổi.
6. Branch kín hai đầu: chỉ căn cả cụm theo fallback hiện tại, không cắt main.
7. Main và branch thuộc cùng mạng: từ chối trước thay đổi.
8. Cụm có Pin hoặc Group: từ chối trước thay đổi.
9. Routing Preferences thiếu Tee/Y hoặc thiếu size: direct và fallback thất bại có thông báo rõ, model rollback nguyên vẹn.
10. Chạy lại lệnh nhiều lần và nhấn `Esc`: không để transaction hoặc phần tử tạm.

## Backup và phát hành

- Trước khi sửa mã, tạo backup có timestamp trong `C:\Users\tungnt2303\Downloads\260515_Release\backups`.
- Backup tối thiểu gồm source MC-Align/policy/collector, `PipeUtilities`, test script, DLL/release ZIP hiện hành và manifest add-in đang cài; kèm `RESTORE.txt`.
- Build và kiểm thử trên bản sao làm việc; không sửa hoặc xóa backup.
- Chỉ đồng bộ source chính, build và add-in đang cài sau khi automated checks đạt.
- Do Revit khóa DLL đang nạp, bản mới chỉ có hiệu lực sau khi đóng và mở lại Revit nếu ứng dụng đang chạy.

## Ngoài phạm vi

- Nối body-to-body khi cụm di chuyển cũng kín hai đầu.
- Tự ngắt hoặc thay kết nối hiện hữu.
- Tự tạo nhánh trung gian khi không có Pipe endpoint hở.
- Body connection cho Duct, Conduit, Cable Tray hoặc FabricationPart.
- Thay đổi Routing Preferences, tự nạp family hoặc sửa family fitting.
- Yêu cầu người dùng pick thủ công điểm chèn trên thân trong phiên bản này.
