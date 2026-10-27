# Personal Learning Agent

Ứng dụng desktop WPF chạy cục bộ để ghi nhớ và tìm lại kiến thức. Ứng dụng không dùng model AI hoặc dịch vụ Internet khi sử dụng; thư viện Open XML được đóng gói để xuất tài liệu Word/Excel.

## Chạy ứng dụng

Cần cài .NET 9 SDK. Mở PowerShell rồi chạy:

```powershell
Set-Location D:\PersonalLearningAgent
dotnet run
```

## Tạo bộ cài Windows

Sau khi cài .NET 9 SDK và Inno Setup 7, chạy từ thư mục dự án:

```powershell
dotnet publish .\PersonalLearningAgent.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\publish\win-x64
& "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe" .\installer\PersonalLearningAgent.iss
```

Bộ cài tạo tại `artifacts\installer\Personal-Learning-Agent-Setup-1.0.0.exe` và cài theo tài khoản hiện tại, không cần quyền quản trị. Bản cài không kèm dữ liệu ghi nhớ hay cài đặt cá nhân; chúng tiếp tục nằm trong `%LOCALAPPDATA%\PersonalLearningAgent`.

Lưu ý: bộ cài và ứng dụng tự build này chưa được ký bởi nhà phát hành được Windows tin cậy. Máy đang thực thi Smart App Control có thể chặn cả bộ cài hoặc chương trình sau khi cài. Đóng gói thành `.exe` không thay thế được yêu cầu ký/reputation của Windows.

Quy trình build mặc định không tự ký ứng dụng. Không đưa chứng chỉ hoặc khóa ký riêng vào repository; chỉ ký qua quy trình được bạn và Windows tin cậy.

## Sử dụng

1. Chọn **Ghi nhớ**, nhập nội dung vào khung phía dưới rồi nhấn Enter hoặc **Gửi**. Nhấn Shift+Enter để xuống dòng.
2. Chọn **Đọc**, nhập nội dung hoặc từ khóa rồi nhấn Enter hoặc **Gửi**. Ứng dụng hiển thị nhiều ghi nhớ phù hợp, gồm ID và điểm tương đồng ước tính. Chỉnh số lượng bằng danh sách hoặc nhập một số nguyên dương; tìm theo ID chính xác chỉ trả về một ghi nhớ.
3. Chọn lại **Ghi nhớ** bất cứ lúc nào để tiếp tục thêm ghi nhớ.
4. Tin nhắn Agent nằm bên trái, tin nhắn người dùng nằm bên phải; đường dọc giữa khung chat phân chia hai phía. Mỗi tin nhắn hiển thị ngày và giờ gửi.
5. Nút **Dữ liệu** mở tệp `memories.jsonl` bằng ứng dụng mặc định của Windows.
6. Các nút **Word** và **Excel** trên mỗi bong bóng xuất riêng nội dung và thời gian của tin nhắn thành `.docx` hoặc `.xlsx`. Màu chữ đã tô được giữ trong tài liệu xuất.
7. Kéo chọn văn bản trong bong bóng chat rồi nhấn Ctrl+C để sao chép, hoặc chọn **Tô màu** để áp dụng một trong chín màu. Màu trên nội dung ghi nhớ được lưu cùng bản ghi JSONL và vẫn còn khi đọc lại; định dạng của dữ liệu cũ không có màu vẫn được hỗ trợ.
8. Với bong bóng ghi nhớ, chọn **Sửa** để chỉnh nội dung/danh mục hoặc **Ghim/Bỏ ghim** để ưu tiên ghi nhớ khi tìm kiếm. Có thể nhập danh mục tùy chọn lúc tạo ghi nhớ.
9. Khi đã chọn bong bóng chứa ghi nhớ, nút **Xóa** sẽ xóa ghi nhớ sau khi xác nhận. Nút **Hoàn tác xóa** xuất hiện trong 10 giây để khôi phục.
10. **Sao lưu** lưu dữ liệu thành JSONL. **Khôi phục** kiểm tra toàn bộ tệp rồi gộp ghi nhớ theo ID; ID đã tồn tại không bị ghi đè.
11. Mở **Cài đặt** để nhập màu nền theo mã HEX `#RRGGBB`. Nhấn **Lưu** để áp dụng màu mới.

Mỗi ghi nhớ được nối thêm thành một dòng JSON trong `data\memories.jsonl`; dữ liệu hiện có được giữ nguyên. Khi chạy từ thư mục dự án, vị trí lưu là `D:\PersonalLearningAgent\data\memories.jsonl`. Nếu chạy bản phát hành không kèm thư mục dự án, dữ liệu được lưu trong thư mục dữ liệu ứng dụng thuộc Local AppData.

Màu nền được lưu riêng trong `%LOCALAPPDATA%\PersonalLearningAgent\settings.json`.

Việc tìm kiếm hiện dựa trên từ khóa đã chuẩn hóa (không phân biệt chữ hoa/thường, hỗ trợ bỏ dấu tiếng Việt và một số lỗi gõ gần đúng). Đây không phải tìm kiếm ngữ nghĩa: ứng dụng chỉ có thể tìm nội dung trong các ghi nhớ đã lưu và không tự tạo câu trả lời từ kiến thức chưa có trong tệp.
