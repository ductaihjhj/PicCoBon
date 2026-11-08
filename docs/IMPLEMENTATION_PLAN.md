# Kế Hoạch Triển Khai 40 Hạng Mục Audit — Spike Pickleball Club

- **Nhánh làm việc:** `fix/audit-40-items`
- **Nguyên tắc:** Sửa tận gốc kiến trúc Auth, Phân quyền, API, Database, Logic Đặt sân / Giá / POS / QR Check-in và Bảo mật XSS theo đúng thứ tự ưu tiên `P0` -> `P1` -> `P2`.

---

## Ma Trận 40 Hạng Mục (`P0`, `P1`, `P2`)

| Mã | Mức | Tên hạng mục | File can thiệp chính | Trạng thái |
|---|---|---|---|---|
| **P0-01** | P0 | Loại bỏ biến tĩnh dùng chung `Function._AccountId` gây rò rỉ phiên giữa các user | `Utilities/Function.cs`, `Program.cs` | **DONE** |
| **P0-02** | P0 | Thay thế cookie giả mạo `pb_admin_uid` bằng ASP.NET Core Cookie Authentication (`HttpOnly`, `SameSite=Lax`, mã hóa) | `Program.cs`, `Controllers/AccountController.cs`, `Areas/Admin/Controllers/LoginController.cs` | **DONE** |
| **P0-03** | P0 | Khóa leo thang đặc quyền tại `/Admin/Register` và ép `RoleId = 2` (Customer) khi đăng ký công khai | `Areas/Admin/Controllers/RegisterController.cs`, `Controllers/AccountController.cs` | **DONE** |
| **P0-04** | P0 | Bảo vệ toàn bộ Admin Controllers và trang vận hành (`/Management`, `/CheckIn`, `/Reports`, `/Cms`, `/Admin/*`) bằng `[Authorize]` + `[ValidateAntiForgeryToken]` | `Areas/Admin/Controllers/*.cs`, `Controllers/HomeController.cs` | **DONE** |
| **P0-05** | P0 | Vô hiệu hóa `clearAll` và khóa endpoint `/api/db/sync` trước người dùng chưa xác thực / không phải Admin | `Controllers/ApiDbController.cs`, `wwwroot/js/shared.js` | **DONE** |
| **P0-06** | P0 | Chặn rò rỉ dữ liệu cá nhân (PII: họ tên, SĐT, email, mã vé, QR token) trên `/api/db/state` cho khách vãng lai | `Controllers/ApiDbController.cs` | **DONE** |
| **P0-07** | P0 | Chống IDOR khi tra cứu vé (`/api/bookings/lookup` yêu cầu khớp SĐT với khách chưa đăng nhập) và khi hủy lịch (`/api/bookings/{code}/cancel`) | `Controllers/ApiDbController.cs`, `Services/BookingService.cs`, `wwwroot/js/lookup.js` | **DONE** |
| **P0-08** | P0 | Kiểm tra trùng lịch đặt sân & lịch khóa bảo trì trên Server trong Transaction `Serializable` | `Services/BookingService.cs`, `Controllers/ApiDbController.cs` | **DONE** |
| **P0-09** | P0 | Nâng cấp băm mật khẩu từ MD5 sang PBKDF2 (`PasswordHasher<TbAccount>`) và tự động migrate khi đăng nhập | `Services/PasswordService.cs`, `Services/DatabaseSchemaUpdater.cs` | **DONE** |
| **P0-10** | P0 | Chống thao túng giá trên `/api/bookings` và `/api/orders/create` — Server tự tính giá từ DB | `Services/PricingService.cs`, `Services/BookingService.cs`, `Controllers/ApiDbController.cs` | **DONE** |
| **P0-11** | P0 | Tạo tài khoản thật vào SQL Server `tb_Account` khi đăng ký thay vì lưu giả ở `localStorage` | `Controllers/AccountController.cs`, `wwwroot/js/shared.js`, `Views/Account/Login.cshtml` | **DONE** |
| **P0-12** | P0 | Khử trùng XSS (`escapeHtml`, whitelist `iconify-icon`) trên toàn bộ 8 file JS và API | `wwwroot/js/*.js`, `Controllers/ApiDbController.cs` | **DONE** |
| **P1-01** | P1 | Mã hóa chữ ký số HMAC-SHA256 cho vé QR Check-in (`SPK1.{code}.{court}.{date}.{exp}.{sig}`) và xác thực tại `/api/checkin/verify` | `Services/QrTokenService.cs`, `Services/BookingService.cs`, `wwwroot/js/checkin.js` | **DONE** |
| **P1-02** | P1 | Gỡ bỏ các nút đăng nhập MXH giả (`Google`, `Zalo`, `Apple` chỉ hiện toast) trên `Login.cshtml` | `Views/Account/Login.cshtml` | **DONE** |
| **P1-03** | P1 | Gỡ bỏ luồng OTP khôi phục mật khẩu giả ở client, thay bằng hướng dẫn xác minh chính chủ qua Hotline/Quầy lễ tân | `Views/Account/Login.cshtml` | **DONE** |
| **P1-04** | P1 | Tách rõ phương thức thanh toán (`cash`, `transfer`, `vnpay`, `card`) và trạng thái thanh toán (`unpaid`, `partial`, `paid`) | `Models/TbBooking.cs`, `Services/BookingService.cs`, `wwwroot/js/booking.js` | **DONE** |
| **P1-05** | P1 | Khởi tạo đơn đặt sân mới với `PaidAmount = 0` (`unpaid`), chỉ ghi nhận doanh thu thực thu khi thu ngân xác nhận thu tiền | `Services/BookingService.cs`, `Models/TbPaymentTransaction.cs` | **DONE** |
| **P1-06** | P1 | Tách biệt **Doanh thu thực thu (Collected Revenue)** và **Công nợ phải thu (Receivables)** trên Báo cáo & Dashboard Admin | `Controllers/ApiDbController.cs`, `Areas/Admin/Controllers/HomeController.cs`, `wwwroot/js/reports.js`, `wwwroot/js/dashboard.js` | **DONE** |
| **P1-07** | P1 | Máy trạng thái thời gian thực chuẩn xác: Đơn `approved` **không** tự nhảy sang `playing` nếu khách chưa Check-in | `Services/BookingService.cs`, `wwwroot/js/shared.js`, `wwwroot/js/dashboard.js` | **DONE** |
| **P1-08** | P1 | Tính giá thuê sân theo từng lát cắt 30 phút khi ca chơi vắt qua mốc 17:00 (ví dụ `16:30–18:30` = 30p thường + 90p giờ vàng) | `Services/PricingService.cs`, `wwwroot/js/shared.js`, `wwwroot/js/booking.js` | **DONE** |
| **P1-09** | P1 | Áp dụng chiết khấu hạng thẻ Hội viên (`Vàng VIP` -15%, `Bạc` -5%) trực tiếp vào công thức tính giá Server & Client | `Services/PricingService.cs`, `wwwroot/js/shared.js`, `wwwroot/js/booking.js` | **DONE** |
| **P1-10** | P1 | Kiểm tra trùng lịch trước khi gia hạn ca chơi (`+30 phút`) trên màn hình Điều phối POS | `Services/BookingService.cs`, `Controllers/ApiDbController.cs`, `wwwroot/js/management.js` | **DONE** |
| **P1-11** | P1 | Khóa sân bảo trì (`TbCourtLock`) kiểm tra xung đột với đơn đã duyệt và chỉ hiển thị "Đang bảo trì" đúng khung giờ khóa | `Services/BookingService.cs`, `Controllers/ApiDbController.cs`, `wwwroot/js/dashboard.js` | **DONE** |
| **P1-12** | P1 | Áp dụng quy tắc hủy lịch: Khách hàng chỉ được tự hủy đơn `approved` trước giờ bóng lăn tối thiểu 2 tiếng | `Services/BookingService.cs`, `wwwroot/js/lookup.js` | **DONE** |
| **P1-13** | P1 | Validate dữ liệu đầu vào (Số điện thoại VN, thời lượng 30–480p, giá >= 0, ngày hợp lệ) trên toàn bộ Controllers | `Services/BookingService.cs`, `Areas/Admin/Controllers/*.cs` | **DONE** |
| **P1-14** | P1 | Kiểm tra và trừ tồn kho (`UnitInStock`) trong Transaction khi tạo đơn Pro Shop hoặc gọi món POS tại sân | `Services/BookingService.cs`, `Controllers/ApiDbController.cs`, `wwwroot/js/menu.js` | **DONE** |
| **P1-15** | P1 | Xác minh sân đang có ca chơi hợp lệ và khớp chủ sân khi khách chọn "Ghi nợ vào hóa đơn sân đang chơi" trên trang `/Product` | `Controllers/ApiDbController.cs`, `wwwroot/js/menu.js` | **DONE** |
| **P1-16** | P1 | Xét hạng thẻ Hội viên (`MemberTier`) dựa trên **tổng chi tiêu thực thu (`PaidAmount`)** trên Server, chống spam đơn ảo | `Services/PricingService.cs`, `Controllers/AccountController.cs` | **DONE** |
| **P1-17** | P1 | Bảo vệ toàn vẹn khóa ngoại khi xóa Sân, Sản phẩm, Tài khoản (chuyển sang Soft-delete / `IsActive = false` nếu đã có giao dịch) | `Areas/Admin/Controllers/CourtsController.cs`, `ProductsController.cs`, `AccountsController.cs` | **DONE** |
| **P1-18** | P1 | Đồng bộ quy trình tạo đơn tại quầy (`CreateWalkIn`) và cập nhật trạng thái trong `/Admin/Bookings` với `BookingService` | `Areas/Admin/Controllers/BookingsController.cs`, `Areas/Admin/Views/Bookings/Index.cshtml` | **DONE** |
| **P2-01** | P2 | Tách quyền `Staff` (`RoleId = 3`) và `Admin` (`RoleId = 1`): Staff vận hành POS/Check-in, chỉ Admin mới sửa giá sân/CMS/xóa tài khoản | `Services/DatabaseSchemaUpdater.cs`, `Areas/Admin/Controllers/*.cs` | **DONE** |
| **P2-02** | P2 | Thay thế việc đẩy toàn bộ DB qua `/api/db/sync` bằng các REST API chuyên biệt cho từng nghiệp vụ | `Controllers/ApiDbController.cs`, `wwwroot/js/*.js` | **DONE** |
| **P2-03** | P2 | Lưu vết kiểm toán (`TbAuditLog`) cho các thao tác nhạy cảm: duyệt đơn, hủy đơn, thu tiền, khóa sân, đổi cấu hình | `Models/TbAuditLog.cs`, `Services/BookingService.cs`, `Controllers/ApiDbController.cs` | **DONE** |
| **P2-04** | P2 | Chuẩn hóa múi giờ Việt Nam (`SE Asia Standard Time` / UTC+7) thống nhất giữa Server và Client | `Services/BookingService.cs`, `wwwroot/js/shared.js` | **DONE** |
| **P2-05** | P2 | Đồng bộ thông tin cấu hình CMS (Hotline, Giờ mở cửa, Phụ thu giờ vàng) lên Header/Footer và bảng giá | `Views/Shared/_Layout.cshtml`, `wwwroot/js/shared.js`, `wwwroot/js/dashboard.js` | **DONE** |
| **P2-06** | P2 | Hoàn thiện trang Hồ sơ cá nhân (`/Account/Profile`) cho phép cập nhật Họ tên, SĐT, Email, Trình độ DUPR và Đổi mật khẩu thật | `Controllers/AccountController.cs`, `Views/Account/Profile.cshtml` | **DONE** |
| **P2-07** | P2 | Chuẩn hóa nội dung truyền thông trên giao diện sát với tính năng thực tế của CLB | `Views/Home/Index.cshtml`, `Views/Account/Login.cshtml` | **DONE** |
| **P2-08** | P2 | Tối ưu trải nghiệm mobile/tablet, modal chi tiết vé QR và chống tràn ngang | `Views/Lookup/Index.cshtml`, `wwwroot/js/lookup.js`, `wwwroot/js/booking.js` | **DONE** |
| **P2-09** | P2 | Bổ sung Rate Limiting (`AuthAndLookup`), HTTP Security Headers (`X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`) | `Program.cs` | **DONE** |
| **P2-10** | P2 | Xây dựng bộ kiểm thử tự động xUnit (`PickleballClub.Tests`) kiểm chứng toàn bộ ma trận bảo mật & nghiệp vụ | `PickleballClub.Tests/MasterAuditTests.cs` | **DONE** |
