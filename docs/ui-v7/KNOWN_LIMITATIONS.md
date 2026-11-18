# Giới Hạn Đã Biết & Ghi Chú Vận Hành (`KNOWN_LIMITATIONS.md`)

1. **Quét QR Trực Tiếp Bằng Camera Phần Cứng (`BLOCKED_DEVICE` trong môi trường CI Headless):**
   - Trong môi trường kiểm thử tự động Chromium Headless trên Windows Server/Desktop CI không gắn luồng video camera vật lý, nút **"Bật Camera"** (`navigator.mediaDevices.getUserMedia` + `BarcodeDetector`) trên `/Checkin` không thể tự quét bằng webcam thật và được phân loại trạng thái `BLOCKED_DEVICE`.
   - Tuy nhiên, toàn bộ đường ống xử lý phía sau (tải tệp ảnh `.png` qua `#qrFileInput` → `POST /api/checkin/decode-qr` → `QrTokenService.TryDecodeQrImage` dùng `ZXing.Net` → kiểm tra chữ ký `SPK1...`, trạng thái vé, khung giờ thi đấu và chống dùng lại `409 Conflict`) đã được kiểm thử thông qua **cả Playwright E2E trên UI thực tế lẫn bộ test xUnit**.
   - **Hướng dẫn xác minh thủ công trước khi mở cổng thực tế:** Mở `/Checkin` trên điện thoại hoặc laptop có camera thực, cấp quyền Camera và đưa vé mẫu QR vào khung hình.

2. **Thư Viện Biểu Tượng Iconify Tải Qua CDN Ngoài:**
   - Giao diện sử dụng `<script src="https://code.iconify.design/iconify-icon/2.1.0/iconify-icon.min.js"></script>`. Nếu triển khai trên mạng nội bộ không có kết nối Internet (air-gapped), các icon `<iconify-icon>` sẽ không hiển thị; cần tải gói `iconify-icon` vào `wwwroot/lib/` để phục vụ offline nếu yêu cầu triển khai nội bộ tuyệt đối.

3. **Các Lệnh & Bảng Điều Khiển Phụ Trợ Quản Trị:**
   - Sidebar thu gọn (`spike_sidebar_collapsed`) và chế độ Sáng/Tối (`spike_theme`) được lưu trong `localStorage` của trình duyệt người dùng — không ghi vào bảng cơ sở dữ liệu nhằm tuân thủ ràng buộc **không thay đổi schema SQL Server**.
   - Các trang CRUD chi tiết phụ của CMS Menu (`/Admin/Menus/{Details,Delete}`) vẫn sử dụng layout Admin đồng nhất của V7 (`_Layout.cshtml`) nhưng giữ cấu trúc bảng/form cơ bản ASP.NET Core MVC nguyên bản để tránh phá vỡ binding model hiện có.
