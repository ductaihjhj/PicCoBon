# Báo Cáo Nghiệm Thu Hoàn Tất 40 Hạng Mục (`P0`, `P1`, `P2`) — Spike Pickleball Club

- **Ngày hoàn thành:** 08/11/2026
- **Nhánh Git:** `fix/audit-40-items`
- **Kết quả Build (`dotnet build`):** `0 Error(s), 0 Warning(s)`
- **Kết quả Kiểm tra cú pháp JS (`node --check wwwroot/js/*.js`):** `8/8 PASS`
- **Kết quả Kiểm thử Tự động xUnit (`dotnet test`):** `Passed: 9/9, Failed: 0`

---

## 1. Tổng Hợp Trạng Thái 40 Hạng Mục (`P0`, `P1`, `P2`)

### Nhóm P0 — Bảo Mật Nghiêm Trọng, Phân Quyền & Toàn Vẹn Dữ Liệu (12/12 DONE)

| Mã | Nội dung khắc phục | Giải pháp kỹ thuật & File thực thi | Trạng thái |
|---|---|---|---|
| **P0-01** | Xóa bỏ biến `static` dùng chung `Function._AccountId` gây rò rỉ phiên đăng nhập giữa nhiều người dùng | Đọc thông tin người dùng theo từng HTTP Request thông qua `IHttpContextAccessor` và `ClaimsPrincipal` trong [`Utilities/Function.cs`](file:///e:/pickleball-ui/Utilities/Function.cs). | **DONE** |
| **P0-02** | Xóa bỏ cơ chế đọc cookie không chữ ký `pb_admin_uid` cho phép chiếm quyền Admin | Cấu hình ASP.NET Core Cookie Authentication (`spike_auth`, `HttpOnly`, `SameSite=Lax`, mã hóa) trong [`Program.cs`](file:///e:/pickleball-ui/Program.cs). | **DONE** |
| **P0-03** | Khóa trang `/Admin/Register` trước khách vãng lai và ép `RoleId = 2` (Customer) khi đăng ký công khai | Gắn `[Authorize(Roles = "Admin")]` trên [`Areas/Admin/Controllers/RegisterController.cs`](file:///e:/pickleball-ui/Areas/Admin/Controllers/RegisterController.cs) và ép cứng `RoleId = 2` trong [`Controllers/AccountController.cs`](file:///e:/pickleball-ui/Controllers/AccountController.cs). | **DONE** |
| **P0-04** | Bảo vệ toàn bộ các route Admin (`/Management`, `/CheckIn`, `/Reports`, `/Cms`, `/Admin/*`) | Gắn `[Authorize(Roles = "Admin,Staff")]` / `[Authorize(Roles = "Admin")]` và `[ValidateAntiForgeryToken]` trên toàn bộ Controllers quản trị. | **DONE** |
| **P0-05** | Vô hiệu hóa cờ `clearAll` xóa sạch DB và khóa `/api/db/sync` trước người dùng chưa xác thực | Loại bỏ nhánh xóa dữ liệu hàng loạt, yêu cầu `[Authorize(Roles = "Admin")]` cho `/api/db/sync` trong [`Controllers/ApiDbController.cs`](file:///e:/pickleball-ui/Controllers/ApiDbController.cs). | **DONE** |
| **P0-06** | Ẩn thông tin cá nhân (PII: Họ tên, SĐT, Email, Mã vé, QR Token) trên `/api/db/state` | Trong [`Controllers/ApiDbController.cs`](file:///e:/pickleball-ui/Controllers/ApiDbController.cs), khách vãng lai chỉ nhận trạng thái khung giờ bận/trống (`Đã đặt`), chỉ chủ đơn hoặc Admin/Staff mới thấy chi tiết vé. | **DONE** |
| **P0-07** | Chống IDOR khi tra cứu vé (`/api/bookings/lookup`) và hủy vé (`/api/bookings/{code}/cancel`) | Yêu cầu xác minh Số điện thoại khớp với đơn đặt sân đối với người dùng không phải chủ tài khoản / Admin trong [`Controllers/ApiDbController.cs`](file:///e:/pickleball-ui/Controllers/ApiDbController.cs) và [`Services/BookingService.cs`](file:///e:/pickleball-ui/Services/BookingService.cs). | **DONE** |
| **P0-08** | Chống đặt trùng lịch (Double-booking) ở tầng Server & Database | Kiểm tra giao thoa khung giờ trong Transaction `IsolationLevel.Serializable` tại [`Services/BookingService.cs`](file:///e:/pickleball-ui/Services/BookingService.cs) kèm Index trên `tb_Booking(CourtId, BookingDate, Status)`. | **DONE** |
| **P0-09** | Nâng cấp băm mật khẩu từ MD5 sang PBKDF2 (`PasswordHasher<TbAccount>`) | [`Services/PasswordService.cs`](file:///e:/pickleball-ui/Services/PasswordService.cs) băm mật khẩu mới bằng PBKDF2 và tự động nâng cấp mật khẩu MD5 cũ ngay khi đăng nhập thành công. | **DONE** |
| **P0-10** | Chống thao túng giá từ phía Client khi đặt sân và mua hàng Pro Shop | Server tự tính lại 100% đơn giá sân, phụ thu giờ vàng, chiết khấu hội viên và giá sản phẩm từ DB trong [`Services/PricingService.cs`](file:///e:/pickleball-ui/Services/PricingService.cs) và [`Controllers/ApiDbController.cs`](file:///e:/pickleball-ui/Controllers/ApiDbController.cs). | **DONE** |
| **P0-11** | Lưu tài khoản đăng ký mới trực tiếp vào bảng `tb_Account` trong SQL Server | Gỡ bỏ cơ chế lưu tài khoản giả ở `localStorage`; kết nối form đăng ký trên [`Views/Account/Login.cshtml`](file:///e:/pickleball-ui/Views/Account/Login.cshtml) với `/api/auth/register`. | **DONE** |
| **P0-12** | Khử trùng XSS trên toàn bộ 8 file JS và whitelist `iconify-icon` | Bổ sung `escapeHtml()`, `escapeAttr()`, `renderSafeIcon()` trong [`wwwroot/js/shared.js`](file:///e:/pickleball-ui/wwwroot/js/shared.js) và `ApiDbController.FormatIconHtml()`. | **DONE** |

---

### Nhóm P1 — Nghiệp Vụ Cốt Lõi: Đặt Sân, Tính Giá, Thanh Toán, POS & QR Check-in (18/18 DONE)

| Mã | Nội dung khắc phục | Giải pháp kỹ thuật & File thực thi | Trạng thái |
|---|---|---|---|
| **P1-01** | Mã QR Check-in có chữ ký số HMAC-SHA256 chống làm giả | [`Services/QrTokenService.cs`](file:///e:/pickleball-ui/Services/QrTokenService.cs) sinh và xác thực mã `SPK1.{code}.{courtId}.{date}.{exp}.{sig}` tại `/api/checkin/verify`. | **DONE** |
| **P1-02** | Gỡ bỏ các nút đăng nhập MXH giả (`Google`, `Zalo`, `Apple`) | Đã gỡ bỏ hoàn toàn khỏi [`Views/Account/Login.cshtml`](file:///e:/pickleball-ui/Views/Account/Login.cshtml). | **DONE** |
| **P1-03** | Gỡ bỏ luồng OTP khôi phục mật khẩu giả ở Client | Thay thế bằng hướng dẫn liên hệ xác minh chính chủ qua Hotline CLB / Quầy lễ tân trên [`Views/Account/Login.cshtml`](file:///e:/pickleball-ui/Views/Account/Login.cshtml). | **DONE** |
| **P1-04** | Lưu trữ và hiển thị đúng phương thức thanh toán (`cash`, `transfer`, `vnpay`, `card`) | Bổ sung cột `PaymentMethod`, `PaymentStatus` vào `tb_Booking`, `tb_Order` và bảng `tb_PaymentTransaction`. | **DONE** |
| **P1-05** | Đơn đặt sân mới khởi tạo có `PaidAmount = 0` (`unpaid`), không tự cộng doanh thu ảo | Sửa trong [`Services/BookingService.cs`](file:///e:/pickleball-ui/Services/BookingService.cs): chỉ khi thu ngân bấm thu tiền hoặc tất toán ca chơi thì `PaidAmount` mới tăng. | **DONE** |
| **P1-06** | Tách biệt **Doanh thu thực thu (Collected Revenue)** và **Công nợ chưa thu (Receivables)** | Cập nhật [`wwwroot/js/reports.js`](file:///e:/pickleball-ui/wwwroot/js/reports.js), [`wwwroot/js/dashboard.js`](file:///e:/pickleball-ui/wwwroot/js/dashboard.js) và [`Areas/Admin/Views/Home/Index.cshtml`](file:///e:/pickleball-ui/Areas/Admin/Views/Home/Index.cshtml). | **DONE** |
| **P1-07** | Máy trạng thái thời gian thực chuẩn xác: Đơn `approved` không tự nhảy sang `playing` nếu chưa Check-in | Sửa trong [`Services/BookingService.cs`](file:///e:/pickleball-ui/Services/BookingService.cs) và [`wwwroot/js/shared.js`](file:///e:/pickleball-ui/wwwroot/js/shared.js): chỉ chuyển sang `playing` khi có thao tác Check-in QR hoặc Nhận sân tại quầy. | **DONE** |
| **P1-08** | Tính giá phân đoạn theo từng 30 phút khi vắt qua mốc 17:00 | [`Services/PricingService.cs`](file:///e:/pickleball-ui/Services/PricingService.cs) và `calculateSegmentedPricing()` trong `shared.js` chia chính xác từng lát 30 phút. | **DONE** |
| **P1-09** | Áp dụng chiết khấu hạng thẻ Hội viên (`Vàng VIP` -15%, `Bạc` -5%) vào công thức tính giá | Đồng bộ giữa `/api/bookings/quote` (`PricingService.cs`) và giao diện đặt sân (`booking.js`). | **DONE** |
| **P1-10** | Gia hạn thêm giờ (`+30 phút`) trên POS kiểm tra trùng lịch với ca kế tiếp | `BookingService.ExtendBookingAsync` kiểm tra xung đột với cả đơn đặt sân và lịch bảo trì trước khi cộng thêm 30 phút. | **DONE** |
| **P1-11** | Khóa sân bảo trì kiểm tra trùng lịch và chỉ hiển thị bảo trì đúng khung giờ khóa | `/api/admin/court-locks` kiểm tra xung đột trước khi khóa; `dashboard.js` chỉ hiển thị "Đang bảo trì" khi giờ hiện tại nằm trong khung giờ khóa. | **DONE** |
| **P1-12** | Quy tắc hủy lịch: Đơn `approved` chỉ được khách tự hủy trước giờ chơi tối thiểu 2 tiếng | Kiểm tra trên Server trong `BookingService.CancelBookingAsync` và hiển thị rõ trên `/Lookup`. | **DONE** |
| **P1-13** | Kiểm tra hợp lệ dữ liệu đầu vào (SĐT VN, thời lượng 30–480p, ngày, giá >= 0) | Validate chặt chẽ tại `BookingService`, `ApiDbController` và toàn bộ Admin Controllers. | **DONE** |
| **P1-14** | Kiểm tra và trừ tồn kho (`UnitInStock`) khi đặt hàng Pro Shop hoặc thêm món POS | Thực hiện trong Transaction tại `/api/orders/create` và `BookingService.AddPosItemToBookingAsync`. | **DONE** |
| **P1-15** | Xác minh ca chơi hợp lệ khi khách chọn "Ghi nợ vào hóa đơn sân đang chơi" | Kiểm tra sân đang có ca `playing`/`approved` trong ngày và khớp SĐT chủ sân tại `/api/orders/create`. | **DONE** |
| **P1-16** | Xét hạng thẻ Hội viên (`MemberTier`) dựa trên doanh số thực thu (`PaidAmount`) | `PricingService.GetVerifiedCustomerPaidSpendAsync` chỉ tính tổng tiền đã thanh toán thực tế (`PaidAmount`), chặn spam đơn ảo. | **DONE** |
| **P1-17** | Chống lỗi khóa ngoại khi xóa Sân, Sản phẩm, Tài khoản đã phát sinh giao dịch | Tự động chuyển sang ngưng kích hoạt (`IsActive = false`) nếu bản ghi đã có đơn đặt sân hoặc hóa đơn liên kết. | **DONE** |
| **P1-18** | Đồng bộ trang `/Admin/Bookings` với `BookingService` | Tạo đơn tại quầy (`CreateWalkIn`), duyệt vé, nhận sân, thu tiền hoàn tất trên `/Admin/Bookings` gọi trực tiếp `BookingService`. | **DONE** |

---

### Nhóm P2 — Kiến Trúc, Kiểm Toán, Múi Giờ, UX & Kiểm Thử Tự Động (10/10 DONE)

| Mã | Nội dung khắc phục | Giải pháp kỹ thuật & File thực thi | Trạng thái |
|---|---|---|---|
| **P2-01** | Phân tách vai trò `Admin` (`RoleId = 1`) và `Staff` (`RoleId = 3`) | Staff vận hành POS, Check-in, Đơn đặt sân; chỉ Admin mới được sửa cấu hình sân, giá, CMS và quản lý tài khoản. | **DONE** |
| **P2-02** | Thay thế cơ chế ghi đè toàn bộ DB bằng các REST API chuyên biệt | Xây dựng đầy đủ `/api/bookings/quote`, `/api/bookings`, `/api/orders/create`, `/api/checkin/verify`, `/api/admin/bookings/{code}/*`, `/api/admin/court-locks`. | **DONE** |
| **P2-03** | Ghi nhận Nhật ký Kiểm toán (`TbAuditLog`) cho mọi thao tác quan trọng | Tự động lưu `TbAuditLog` khi duyệt đơn, hủy đơn, check-in, gia hạn, thu tiền, khóa sân, sửa CMS. | **DONE** |
| **P2-04** | Đồng bộ múi giờ Việt Nam (`UTC+7`) trên Server và Client | Sử dụng `BookingService.GetVietnamNow()` trên Server và `Asia/Ho_Chi_Minh` trên Client (`shared.js`). | **DONE** |
| **P2-05** | Đồng bộ cấu hình CMS lên Header/Footer và trang chủ | Gắn `data-cms="hotline"`, `data-cms="openHours"` trên `_Layout.cshtml` và cập nhật qua API `/api/admin/cms`. | **DONE** |
| **P2-06** | Trang Hồ sơ cá nhân (`/Account/Profile`) cập nhật thông tin & đổi mật khẩu thật | Lưu trực tiếp vào `tb_Account` với băm mật khẩu PBKDF2 và làm mới Cookie xác thực. | **DONE** |
| **P2-07** | Chuẩn hóa nội dung giới thiệu trên giao diện trung thực với tính năng thực tế | Đã rà soát và làm sạch trên `Views/Home/Index.cshtml` và `Views/Account/Login.cshtml`. | **DONE** |
| **P2-08** | Tối ưu responsive và modal vé điện tử QR trên trang `/Lookup` | Bổ sung form xác minh SĐT tra cứu vé và hiển thị chi tiết bảng tách giá / chiết khấu / QR chữ ký số. | **DONE** |
| **P2-09** | Rate Limiting & Security Headers | Cấu hình `FixedWindowLimiter` (`AuthAndLookup`) và Security Headers trong `Program.cs`. | **DONE** |
| **P2-10** | Bộ kiểm thử tự động xUnit (`PickleballClub.Tests`) | 9 bài kiểm thử tích hợp trong [`PickleballClub.Tests/MasterAuditTests.cs`](file:///e:/pickleball-ui/PickleballClub.Tests/MasterAuditTests.cs) đạt **9/9 PASS**. | **DONE** |
