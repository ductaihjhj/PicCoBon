# Nhật Ký Triển Khai (Worklog) — Spike Pickleball Club

## 1. Khởi tạo & Chuẩn bị nhánh làm việc
- Tạo nhánh Git: `fix/audit-40-items`.
- Khảo sát cơ sở dữ liệu SQL Server `.\SQLEXPRESS` (`PickleballClub`) thực tế và đối chiếu toàn bộ 40 hạng mục trong `Antigravity_Master_Prompt_Spike_Pickleball.md` & `Spike_Pickleball_Audit.md`.

## 2. Batch 1 — Nền tảng Database, Xác thực, Phân quyền & Bảo mật API (`P0-01`..`P0-12`, `P2-01`..`P2-03`, `P2-09`)
- **`Services/DatabaseSchemaUpdater.cs`**:
  - Tạo cơ chế tự động nâng cấp schema SQL Server không phá hủy dữ liệu (`Non-destructive Idempotent Updater`) khi khởi động ứng dụng.
  - Mở rộng `tb_Account.Password` lên `NVARCHAR(256)` để chứa chuỗi băm PBKDF2, bổ sung vai trò `Staff (RoleId = 3)` vào `tb_Role`.
  - Bổ sung các cột tài chính & vận hành vào `tb_Booking`: `PaymentMethod`, `DiscountAmount`, `PosTotal`, `AmountDue`, `PaidAmount`, `PaymentStatus`, `PricingBreakdownJson`, `CheckInToken`, `CheckedInAt`, `CompletedAt`, `CancelledAt`, `CancelReason`.
  - Bổ sung `BookingId`, `BookingCode`, `PaidAmount`, `PaymentStatus`, `PaymentMethod`, `IdempotencyKey` vào `tb_Order`.
  - Tạo bảng sổ cái thu ngân `tb_PaymentTransaction` và bảng nhật ký kiểm toán `tb_AuditLog` cùng các index chống trùng lịch.
- **`Utilities/Function.cs` & `Program.cs`**:
  - Loại bỏ hoàn toàn các trường `static` lưu thông tin đăng nhập dùng chung giữa mọi request (`P0-01`).
  - Loại bỏ cơ chế đọc cookie không chữ ký `pb_admin_uid` (`P0-02`), chuyển sang ASP.NET Core Cookie Authentication (`spike_auth`, `HttpOnly`, `SameSite=Lax`).
  - Cấu hình Rate Limiting (`AuthAndLookup`) và Security Headers (`X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`) (`P2-09`).
- **`Services/PasswordService.cs`**:
  - Tích hợp `PasswordHasher<TbAccount>` (PBKDF2) và tự động nâng cấp mật khẩu MD5 cũ lên PBKDF2 ngay khi người dùng đăng nhập thành công (`P0-09`).
- **`Controllers/AccountController.cs` & `Areas/Admin/Controllers/*`**:
  - Khóa lỗ hổng tự đăng ký Admin tại `/Admin/Register` (`P0-03`) và ép `RoleId = 2` (Customer) cho mọi lượt đăng ký công khai (`P0-11`).
  - Gắn `[Authorize(Roles = "Admin,Staff")]` / `[Authorize(Roles = "Admin")]` và `[ValidateAntiForgeryToken]` trên các Controller quản trị (`P0-04`, `P2-01`).
  - Chuyển thao tác xóa Sân, Sản phẩm, Tài khoản đã có lịch sử giao dịch sang cơ chế Soft-Delete (`IsActive = false`) để bảo vệ khóa ngoại (`P1-17`).

## 3. Batch 2 — Logic Đặt sân, Tính giá phân đoạn, POS, Tồn kho & Soát vé QR (`P1-01`..`P1-18`)
- **`Services/PricingService.cs`**:
  - Tính giá theo từng lát cắt 30 phút khi khung giờ đặt vắt qua mốc `17:00` (`P1-08`).
  - Áp dụng chiết khấu hạng thẻ Hội viên (`Hội viên Vàng VIP` -15%, `Hội viên Bạc` -5%) dựa trên doanh số thực thu (`PaidAmount`) được kiểm chứng từ Server (`P1-09`, `P1-16`).
- **`Services/QrTokenService.cs`**:
  - Sinh và xác thực mã QR có chữ ký số HMAC-SHA256 (`SPK1.{bookingCode}.{courtId}.{date}.{expUnix}.{sigHex16}`) (`P1-01`).
- **`Services/BookingService.cs` & `Controllers/ApiDbController.cs`**:
  - Kiểm tra trùng lịch đặt sân và lịch khóa bảo trì trong Transaction `Serializable` (`P0-08`, `P1-11`).
  - Máy trạng thái thời gian thực chuẩn xác: Đơn `approved` không tự nhảy sang `playing` khi chưa Check-in (`P1-07`); đơn mới khởi tạo có `PaidAmount = 0`, `PaymentStatus = "unpaid"` (`P1-05`).
  - Chặn `clearAll` và khóa `/api/db/sync` (`P0-05`), ẩn PII trên `/api/db/state` đối với người dùng không phải chủ đơn (`P0-06`).
  - Bảo vệ chống IDOR khi tra cứu vé (`/api/bookings/lookup`) và hủy vé (`/api/bookings/{code}/cancel`) kèm quy tắc chỉ tự hủy đơn đã duyệt trước giờ chơi tối thiểu 2 tiếng (`P0-07`, `P1-12`).
  - Kiểm tra trùng lịch khi gia hạn `+30 phút` trên POS (`P1-10`), kiểm tra & trừ tồn kho `UnitInStock` khi mua hàng Pro Shop / gọi món POS (`P1-14`), và xác minh chủ sân khi chọn ghi nợ vào sân đang chơi (`P1-15`).

## 4. Batch 3 — Đồng bộ Giao diện Razor Views & 8 File JavaScript (`P0-12`, `P1-02`, `P1-03`, `P1-06`, `P2-04`..`P2-08`)
- Cập nhật toàn bộ 8 file JS (`shared.js`, `booking.js`, `menu.js`, `lookup.js`, `management.js`, `checkin.js`, `dashboard.js`, `reports.js`):
  - Áp dụng `escapeHtml()` và `renderSafeIcon()` xuyên suốt để chặn XSS (`P0-12`).
  - Chuyển mọi thao tác tạo đơn, duyệt đơn, hủy đơn, gia hạn, gọi món POS, thu tiền, khóa sân, soát vé QR sang gọi trực tiếp các REST API chuyên biệt trên Server (`P2-02`).
  - Tách bạch **Doanh thu thực thu (`paidAmount`)** và **Công nợ chưa thu (`Receivables`)** trên `reports.js`, `dashboard.js` và `/Admin` (`P1-06`).
- Cập nhật `Views/Account/Login.cshtml`, `Views/Lookup/Index.cshtml`, `Views/Shared/_Layout.cshtml`, `Areas/Admin/Views/Shared/_Layout.cshtml`, `Areas/Admin/Views/Bookings/Index.cshtml`, `Areas/Admin/Views/Home/Index.cshtml`.
- Kiểm tra cú pháp JavaScript bằng `node --check` cho cả 8 file: **8/8 PASS**.

## 5. Batch 4 — Kiểm thử Tự động xUnit (`P2-10`) & Nghiệm thu
- Tạo project kiểm thử `PickleballClub.Tests/PickleballClub.Tests.csproj` và `MasterAuditTests.cs` kiểm tra toàn bộ ma trận bảo mật & nghiệp vụ.
- Chạy `dotnet build` và `dotnet test` xác nhận toàn bộ bài kiểm thử đạt **100% PASS**.
