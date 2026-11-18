# Kết Quả Kiểm Thử Tự Động & Nghiệm Thu V7 (`TEST_RESULTS.md`)

**Ngày chạy cuối cùng (sau chỉnh sửa cuối cùng):** `2026-11-17`
**Bản build kiểm thử:** `dotnet build PickleballClub.csproj -c Release` (`0 Warning(s)`, `0 Error(s)`)
**Cơ sở dữ liệu:** SQL Server Express (`.\SQLEXPRESS`, Database `PickleballClub`)

---

## 1. Tóm Tắt Thực Thi Tổng Thể

| Hạng mục | Lệnh / Công cụ | Kết quả | Chi tiết |
|---|---|---|---|
| **Cú pháp JavaScript** | `node --check` trên toàn bộ `wwwroot/js/*.js` | **PASS** | `0` lỗi cú pháp trên tất cả 8 tệp JS (`booking`, `checkin`, `dashboard`, `lookup`, `management`, `menu`, `reports`, `shared`) |
| **Biên dịch .NET Release** | `dotnet build PickleballClub.csproj -c Release` | **PASS** | `0 Warning(s)`, `0 Error(s)` |
| **Bộ test tự động xUnit** | `dotnet test PickleballClub.Tests.csproj -c Release` | **PASS (`35 / 35`)** | `Passed: 35`, `Failed: 0`, `Skipped: 0` (thời gian chạy `40 s`) |
| **Playwright E2E & Responsive** | `verify-ui-v7.js` (`http://127.0.0.1:5299`) | **PASS (`11 / 11`)** | `99 / 99` kiểm tra viewport (`0` overflow, `0` broken images), `0` console error, `0` page error, `0` HTTP 4xx/5xx ngoài ý muốn |

---

## 2. Kết Quả Chi Tiết Bộ Test xUnit (`35 / 35 PASSED`)

Tất cả 6 lớp kiểm thử tích hợp & đơn vị trong `PickleballClub.Tests` đều đạt `100%` sau lần chỉnh sửa cuối cùng:

1. **`ShadcnAdminV7ParityTests.cs` (4 tests — MỚI V7):**
   - Xác minh Admin Shell V7 (`#shAdminShell`, `#shAdminSidebar`, `#shAdminTopbar`, `#shCommandModal`) hiện diện đúng trên `/Admin`, `/Management`, `/Checkin`, `/Reports`, `/Cms`, `/Admin/Products`.
   - Xác minh RBAC hiển thị nhóm menu theo vai trò (`Admin` thấy `data-testid="sh-nav-group-admin-only"`, `Staff` bị ẩn các mục cấu hình hệ thống nhưng vẫn vận hành được `/Management`, `/Checkin`, `/Reports`).
   - Xác minh khách ẩn danh hoặc vai trò không đủ quyền bị chuyển hướng / chặn đúng chính sách bảo mật.
   - Xác minh các route công khai (`/`, `/Booking`, `/Product`, `/Lookup`, `/Account/Login`) hoạt động ổn định cùng `#siteThemeToggleBtn`.
2. **`ProShopV6ImageAndApiTests.cs` (5 tests):** Upload ảnh PNG/JPG/WebP, giữ kênh alpha, chặn file giả mạo/quá 5MB, thay/giữ/xóa ảnh sản phẩm, đồng bộ `imageUrl` và cập nhật `ETag` trên `/api/db/state`.
3. **`HttpSecurityAndApiIntegrationTests.cs` (4 tests):** Ẩn PII cho khách ẩn danh, hỗ trợ `ETag 304`, bắt buộc Anti-Forgery Token, check-in thực tế bằng giải mã `ZXing` trên ảnh QR PNG.
4. **`SqlServerConcurrencyAndMigrationTests.cs` (6 tests):** Chống đặt trùng lịch song song (`409 Conflict`), khóa sân bảo trì, trừ tồn kho nguyên tử, sổ cái thanh toán/hoàn tiền tách biệt đóng ca.
5. **`V4ReleaseBlockersAndQrTests.cs` (6 tests):** Luồng đặt sân hoàn chỉnh, xuất QR SVG/PNG chuẩn và check-in cổng.
6. **`UnitAndQrVerificationTests.cs` (10 tests):** Thuật toán tính giá phân đoạn 30 phút, chiết khấu hội viên, băm mật khẩu PBKDF2, ký và xác thực chữ ký QR `SPK1...`.

---

## 3. Kết Quả Kiểm Thử Trình Duyệt Thực Tế Playwright (`verify-ui-v7.js`)

Nguồn dữ liệu đối chiếu: `e:\pickleball\docs\ui-v7\playwright-v7-results.json`

| Mã Kiểm Thử | Nội Dung Kiểm Tra | Kết Quả | Số Liệu Xác Thực Từ Runner |
|---|---|---|---|
| **`V7-01`** | Shadcn Sidebar Desktop Collapse (`Ctrl+B`) & Mobile Drawer (`375px` + `Escape`) | **PASS** | Chiều rộng thu gọn `256px → 68px`; Mobile drawer `open="true"` và đóng về `"false"` khi nhấn `Escape` |
| **`V7-02`** | Command Palette (`Ctrl+K`) & Lọc Từ Khóa Nhanh | **PASS** | Tìm từ khóa `"POS"` lọc còn `2` mục tương ứng (`/Management`, `/Admin/Products`); đóng sạch khi nhấn `Escape` |
| **`V7-03`** | Dark Mode Theme Persistence & Độ Tương Phản WCAG AA | **PASS** | `data-theme="dark"` lưu bền vững qua reload; độ tương phản `.stat-card-value` đạt **`16.96:1`**, header bảng đạt **`11.07:1`** (vượt chuẩn `>= 4.5:1`) |
| **`V7-05`** | Đặt Sân Thật, Báo Giá Máy Chủ & Bảo Toàn Vé Điện Tử QR Light/Dark | **PASS** | Badge `✓ Báo giá máy chủ`; đặt thành công vé `PB-DD4C3E` kèm token `bk_...`; QR SVG tải sắc nét, hình vuông; `.spk-qr-well` luôn giữ **`rgb(255, 255, 255)`** cả khi trang chuyển sang Dark Mode; phóng to fullscreen `z-index: 9999` đạt; `/api/bookings/{code}/qr.png` & `.svg` trả HTTP `200` |
| **`V7-06`** | Pro Shop Ảnh Sản Phẩm `1:1` & Bảo Toàn Keyed DOM + Animation V6 | **PASS** | `11/11` ảnh sản phẩm tải sạch (`naturalWidth > 0`); chọn mua giữ nguyên node DOM (`sameNode: true`) và focus (`focusKept: true`) |
| **`V7-07`** | Trạm Soát Vé `/Checkin` Upload Ảnh QR PNG Thật Giải Mã Máy Chủ (`ZXing`) | **PASS** | Tải tệp `.png` sinh từ vé vừa tạo vào `#qrFileInput` → `/api/checkin/decode-qr` giải mã thành công prefix **`SPK1.UEItRDM`** và áp dụng đúng ràng buộc ngày thi đấu (`2026-11-21` vs `2026-11-17`) hiển thị trên UI |
| **`V7-08`** | Modal POS Tại Sân Trong Shadcn Shell (`/Management`) | **PASS** | `11/11` thumbnail sản phẩm POS tải đầy đủ, bố cục rõ ràng, nút `+30p` và nhãn `(Kho: X)` không bị ngắt dòng vỡ layout |
| **`V7-09`** | Responsive Quét Toàn Diện (`320, 375, 768, 1024, 1440, 1920px`) & Zero Errors | **PASS** | **`99 / 99`** lượt đo trên toàn bộ 16+ route Khách & Admin: `overflowFailures: 0`, `brokenImages: 0`, `consoleErrors: 0`, `pageErrors: 0`, `failedHttpRequests: 0` |
| **`V7-10`** | Admin Upload/Edit Ảnh Sản Phẩm (`/Admin/Products/Edit/1`) | **PASS** | Khung preview ảnh `1:1` tải đúng (`naturalWidth > 0`), vùng kéo thả `.sh-upload-dropzone` hoạt động đúng, `0` icon FontAwesome lỗi font |
| **`V7-11`** | Báo Cáo Doanh Thu Sổ Cái SQL Thực Tế (`/Reports`) | **PASS** | Lọc ngày từ SQL Server hiển thị đúng doanh thu ròng, biểu đồ cột sân (`hasBars: true`), `5` dòng bút toán sổ cái thực tế, `0` thông báo lỗi |
| **`V7-12`** | Trang Hồ Sơ Hội Viên `/Account/Profile` Light/Dark Mode & Mobile `320px` | **PASS** | Athlete Passport & card form chuyển đổi Dark Mode (`rgb(17, 24, 39)`, `.paddle-chip` đồng bộ `rgb(22, 31, 51)`), `0px` overflow tại `320px`, `375px`, `1440px` |

---

## 4. Lỗi Đã Phát Hiện & Khắc Phục Trong Chu Trình Kiểm Thử

1. **Card chọn sân (`/Booking`) bị ép thành 4 cột hẹp theo chiều ngang:** `.court-option` thiếu `flex-direction: column` và thiếu style cho `.court-option.active`. Đã chuẩn hóa lại bố cục dọc + trạng thái active/selected ở cả Light và Dark Mode (`wwwroot/css/styles.css`).
2. **Nhãn trợ năng `.sr-only` bị lộ thành văn bản thường** ở góc trên cùng Shell Admin (`Chuyển tới nội dung chính`), hộp tìm kiếm `Ctrl+K` (`Tìm kiếm nhanh điều hướng hệ thống`), trang Đặt sân và Báo cáo (`Từ ngày` / `Đến ngày`). Đã bổ sung `.sr-only` và `a.sr-only:focus-visible` chuẩn trong `wwwroot/css/styles.css`.
3. **2 lỗi Console `404` cho `/favicon.ico`** trên các trang không dùng Layout chung hoặc lần tải đầu. Đã nhúng inline SVG favicon chuẩn vào `Views/Shared/_Layout.cshtml`, `Areas/Admin/Views/Shared/_Layout.cshtml`, `Views/Account/Login.cshtml`, `Areas/Admin/Views/Login/Index.cshtml`, `Areas/Admin/Views/Register/Index.cshtml`.
4. **Tràn ngang `2px` trên màn hình `320px` khi đã đăng nhập** (`27-account-profile` do `.user-pill` + `.site-header-actions`). Đã tinh chỉnh responsive breakpoint `@media (max-width: 400px)` và `minmax(0, 1fr)` cho grid mobile.
5. **Cắt nút `THAO TÁC` trong bảng `/Admin/Courts` ở desktop `1440px` và ngắt dòng đơn giá `đ` ở `/Admin/Products`:** Bỏ `text-nowrap` khỏi toàn bảng `/Admin/Courts`, định nghĩa lớp `.text-nowrap` chuẩn và áp dụng riêng cho các cột mã vé, giá bán, tồn kho, trạng thái.
6. **Cắt chữ nút `+30p`, ngắt ngoặc `(Kho:\n100)` trong Modal POS và xếp cột hẹp `.item-row` ở mobile `375px`:** Đã sửa trong `wwwroot/css/styles.css` và `wwwroot/js/management.js`.
7. **Fullscreen QR Zoom Modal (`SpikeTicket.openFullscreenZoom`)** nay tái sử dụng SVG QR máy chủ ngay lập tức (`shared.js`) để không bị trễ mạng khi mở chế độ phóng to.

*(Toàn bộ dữ liệu thử nghiệm tạm thời phát sinh trong quá trình chạy Playwright đã được dọn dẹp sạch khỏi bảng `tb_Booking` trên SQL Server).*
