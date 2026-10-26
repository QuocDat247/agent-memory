# Personal Learning Agent

Ứng dụng desktop WPF chạy cục bộ để ghi nhớ và tìm lại kiến thức. Ứng dụng không dùng model AI, dịch vụ Internet hay thư viện bên ngoài.

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
2. Chọn **Đọc**, nhập nội dung hoặc từ khóa rồi nhấn Enter hoặc **Gửi**. Ứng dụng hiển thị ghi nhớ phù hợp nhất, gồm cả ID và điểm tương đồng ước tính.
3. Chọn lại **Ghi nhớ** bất cứ lúc nào để tiếp tục thêm ghi nhớ.
4. Có thể kéo chuột để chọn văn bản trong khung chat rồi nhấn Ctrl+C để sao chép tin nhắn của bạn hoặc câu trả lời của trợ lý.
5. Khi đã chọn một câu trả lời tìm thấy ghi nhớ, nút **Xóa** được bật. Nhấn nút sẽ hỏi xác nhận rồi xóa toàn bộ ghi nhớ tương ứng khỏi tệp dữ liệu; hủy thao tác sẽ giữ nguyên dữ liệu.
6. Mở **Cài đặt** để nhập màu nền theo mã HEX `#RRGGBB`. Nhấn **Lưu** để áp dụng màu mới.

Mỗi ghi nhớ được nối thêm thành một dòng JSON trong `data\memories.jsonl`; dữ liệu hiện có được giữ nguyên. Khi chạy từ thư mục dự án, vị trí lưu là `D:\PersonalLearningAgent\data\memories.jsonl`. Nếu chạy bản phát hành không kèm thư mục dự án, dữ liệu được lưu trong thư mục dữ liệu ứng dụng thuộc Local AppData.

Màu nền được lưu riêng trong `%LOCALAPPDATA%\PersonalLearningAgent\settings.json`.

Việc tìm kiếm hiện dựa trên từ khóa đã chuẩn hóa (không phân biệt chữ hoa/thường, hỗ trợ bỏ dấu tiếng Việt và một số lỗi gõ gần đúng). Đây không phải tìm kiếm ngữ nghĩa: ứng dụng chỉ có thể tìm nội dung trong các ghi nhớ đã lưu và không tự tạo câu trả lời từ kiến thức chưa có trong tệp.
