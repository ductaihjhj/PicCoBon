# Kế Hoạch Triển Khai Nâng Cấp UI/UX & Tương Thích Cơ Sở Dữ Liệu (Master Prompt V5)

**Dự án**: Spike Pickleball Club (`e:\pickleball`)  
**Nền tảng kỹ thuật**: ASP.NET Core MVC (.NET 10), Entity Framework Core, SQL Server (`.\SQLEXPRESS`), Razor Views, Vanilla JavaScript & Custom Athletic Design System CSS  
**Ngày lập kế hoạch**: 2026-11-10  
**Trạng thái**: Đang triển khai & nghiệm thu trực tiếp trên mã nguồn thực

---

## 1. Mục tiêu Cốt lõi (Objectives)

1. **Dữ liệu thật (Single Source of Truth — UI ↔ API ↔ SQL Server)**:
   - Tuyệt đối không dùng dữ liệu mặc định (`DEFAULT_COURTS`, `DEFAULT_CMS`) để hiển thị giả lập `"4 / 4 sân trống"` hoặc cho phép thao tác đặt sân khi `/api/db/state` đang tải, lỗi mạng hoặc trả về HTTP 500 (`UI-A01`).
   - Báo giá đặt sân phải luôn được xác thực từ máy chủ (`/api/bookings/quote`) kèm `AbortController` chống race condition khi đổi sân/ngày/giờ/thời lượng (`UI-A02`).
   - Chỉ số doanh thu trên Dashboard và Báo cáo phải phản ánh đúng sổ cái giao dịch `tb_PaymentTransaction` theo múi giờ `Asia/Ho_Chi_Minh` (`UI-A03`, `UI-A04`, `UI-A05`).
   - Không fallback sân đã xóa/ngừng hoạt động về `COURTS[0]` (`UI-A06`), đồng bộ chọn ngày qua URL deep-link trong phạm vi horizon (`UI-A07`), và khóa nút đặt sân khi hết khung giờ trống (`UI-A08`).

2. **Nâng cấp Thẩm mỹ & Trải nghiệm Giao diện (Visual Polish & Athletic Brand Identity)**:
   - Giữ vững bản sắc **Spike Pickleball — High-Voltage Athletic Club**: Cam Năng Lượng (`#FF5520`), Vàng Neon Trái Bóng (`#D8F826`), Xanh Đêm Sân Đấu (`#0B111E`), hệ font `Oswald` + `Plus Jakarta Sans` + `JetBrains Mono`.
   - Khắc phục tương phản nút trong Dark Sport QR Ticket (`UI-B01`), chống tràn mã vé/badge ở màn hình nhỏ `320px–430px` (`UI-B02`), đồng bộ nội dung trang chủ/footer với cấu hình CMS thực tế (`UI-B03`), và tách bạch rõ **Tổng phải thanh toán / Đã thanh toán / Còn lại** trên vé QR (`UI-B04`).
   - Bổ sung thanh tóm tắt giỏ hàng cố định (Sticky Mobile Cart Bar) trên Pro Shop mobile (`UI-B05`), cải thiện thanh cuộn & điều hướng bàn phím cho bộ lọc danh mục (`UI-B06`), sửa đường dẫn ảnh fallback bị thiếu (`UI-B07`), và chuẩn hóa toàn bộ Design Tokens + `.stat-card` (`UI-B08`).

3. **Dọn dẹp Kiến trúc DOM, Accessibility & Trạng thái Giao diện (`UI-C01..UI-C03`)**:
   - Loại bỏ request ảnh QR trùng lặp từ khối DOM ẩn legacy trong khi vẫn bảo toàn `data-testid` phục vụ kiểm thử (`UI-C01`).
   - Chuẩn hóa điều hướng bàn phím (`Tab`, `Shift+Tab`, `Enter`, `Space`, `Escape`), focus trap trong modal và `aria-*` cho bộ chọn sân, vé QR, POS (`UI-C02`).
   - Phân biệt rõ 6 trạng thái giao diện: `loading`, `empty` (DB rỗng), `filtered-empty` (lọc không khớp), `error` (lỗi HTTP), `offline` (mất kết nối), và `stale` (dữ liệu cũ) (`UI-C03`).

---

## 2. Phân bổ Chi tiết 19 Hạng mục V5 (`UI-A01` → `UI-C03`)

| Mã ID | Nhóm | Tập tin tác động chính | Giải pháp kỹ thuật |
|---|---|---|---|
| `UI-A01` | P0 Data | `wwwroot/js/shared.js`, `dashboard.js`, `booking.js`, `menu.js`, `management.js`, `checkin.js` | Thiết lập state machine `SPIKE_SYNC_STATE` (`initial`, `loading`, `ready`, `refreshing`, `stale`, `offline`, `error`), `HAS_SERVER_STATE`. Không khởi tạo `COURTS = DEFAULT_COURTS`; hiển thị skeleton/error/retry rõ ràng khi chưa có dữ liệu từ `/api/db/state`. |
| `UI-A02` | P0 Data | `wwwroot/js/booking.js`, `Views/Booking/Index.cshtml`, `Controllers/ApiDbController.cs` | Reset `latestServerQuote = null` ngay khi đổi sân/ngày/giờ/thời lượng; dùng `AbortController` hủy request cũ; cập nhật badge `#bookingQuoteSourceBadge`; khóa `#btnSubmitBooking` cho tới khi quote khớp `{courtId, date, time, duration}`. |
| `UI-A03` | P0 Data | `Controllers/ApiDbController.cs`, `wwwroot/js/dashboard.js`, `Views/Home/Index.cshtml`, `Areas/Admin/Views/Home/Index.cshtml` | Bổ sung `todayLedgerSummary` từ `tb_PaymentTransaction` (theo ngày `Asia/Ho_Chi_Minh`) vào `/api/db/state` cho Staff/Admin; đổi nhãn KPI thành `"Thực thu trong ngày (Sổ cái)"` kèm tooltip giải thích Thực thu ròng / Phải thu / Công nợ / Hoàn tiền. |
| `UI-A04` | P0 Data | `wwwroot/js/reports.js`, `Areas/Admin/Views/Home/Reports.cshtml` | `renderReports()` trả về `{ ok, status, error, isStale }`; `filterReport()` chỉ hiện toast thành công khi `res.ok === true`; hiển thị cảnh báo dữ liệu cũ hoặc trạng thái lỗi kèm nút Thử lại. |
| `UI-A05` | P1 Chart | `wwwroot/js/reports.js`, `wwwroot/css/styles.css` | Khi `collected <= 0`, đặt `pct = 0` (`height: 0%`), hiển thị `0 đ` nhất quán, bổ sung `aria-label` cho từng cột biểu đồ. |
| `UI-A06` | P1 Data | `wwwroot/js/shared.js`, `booking.js`, `dashboard.js`, `lookup.js`, `management.js`, `checkin.js`, `menu.js` | `getCourtById(courtId)` trả về `null` khi không tìm thấy; bổ sung `getCourtDisplayName(courtId, fallbackName)` hiển thị tên snapshot hoặc `"Sân ngừng hoạt động (#id)"`. |
| `UI-A07` | P1 UX | `wwwroot/js/booking.js`, `Views/Booking/Index.cshtml`, `wwwroot/css/styles.css` | Parse `YYYY-MM-DD` theo local date không lệch UTC; kiểm tra giới hạn `[today, today + 30d]`; tự động đưa ngày được chọn vào dải `dateStrip` + đồng bộ với `<input type="date" id="bookingDatePicker">`. |
| `UI-A08` | P1 UX | `wwwroot/js/booking.js`, `Views/Booking/Index.cshtml` | Khi `availableSlots.length === 0`, đặt `selectedStartTime = ''`, làm rỗng/vô hiệu hóa `#bookingStart`, hiển thị thông báo hết giờ kèm gợi ý sân/ngày còn trống, và khóa hoàn toàn `#btnSubmitBooking`. |
| `UI-B01` | P1 Visual | `wwwroot/css/styles.css` | Bổ sung style `.spk-ticket-dark .btn-outline` với nền tối bán trong suốt, chữ `#F8FAFC` (tương phản > 11:1), viền `#64748B`, cùng các trạng thái `:hover`, `:focus-visible`, `:active`, `:disabled`. |
| `UI-B02` | P1 Responsive | `wwwroot/css/styles.css`, `wwwroot/js/shared.js` | Chuyển `.spk-ticket-code-pill` thành `<button type="button">`; ở màn hình `<= 480px`, xếp dọc `.spk-ticket-header` và `.spk-ticket-hero-row` gọn gàng, không cắt mã `PB-XXXXXX` ở `320px`. |
| `UI-B03` | P1 Content | `Views/Home/Index.cshtml`, `Views/Shared/_Layout.cshtml`, `wwwroot/js/shared.js`, `wwwroot/js/dashboard.js` | Đồng bộ số lượng sân động (`@Model.Courts.Count` / `COURTS.length`), gắn `data-cms` cho địa chỉ, giờ mở cửa, hotline, giá sân, phụ thu giờ vàng và mô tả chính xác mức ưu đãi hội viên. |
| `UI-B04` | P1 Ledger UI | `wwwroot/js/shared.js`, `wwwroot/css/styles.css` | Trên `SpikeTicket` (cả HTML và ảnh PNG tải về), hiển thị tách biệt 3 chỉ số: **Tổng phải TT**, **Đã thanh toán**, **Còn lại** kèm nhãn trạng thái thanh toán. |
| `UI-B05` | P2 Mobile UX | `Views/Product/Index.cshtml`, `wwwroot/js/menu.js`, `wwwroot/css/styles.css` | Thêm thanh `#mobileShopCartBar` cố định dưới đáy màn hình ở `<= 960px`, hiển thị số món + tổng tiền + nút cuộn mượt tới giỏ hàng/thanh toán. |
| `UI-B06` | P2 A11y/UX | `Views/Product/Index.cshtml`, `wwwroot/js/menu.js`, `wwwroot/css/styles.css` | Gắn `role="tablist"`, `role="tab"`, `aria-selected`, điều hướng phím mũi tên Trái/Phải, tự động `scrollIntoView`, chiều cao chạm `>= 44px` và hiệu ứng fade báo hiệu cuộn ngang. |
| `UI-B07` | P2 Asset | `wwwroot/js/shared.js`, `Controllers/ApiDbController.cs` | Thay thế toàn bộ tham chiếu `/assets/hero-pickleball-club.png` bằng `/assets/hero-spike-pickleball.png` và chuẩn hóa đường dẫn ảnh sân trong `/api/db/state`. |
| `UI-B08` | P2 Design | `wwwroot/css/styles.css`, `Areas/Admin/Views/Home/Index.cshtml`, `docs/ui-v5/DESIGN_SYSTEM.md` | Chuẩn hóa hệ thống biến CSS (Design Tokens), bổ sung định nghĩa `.stat-card` đồng bộ với `.stat-widget`, chuẩn hóa `:focus-visible` và `@media (prefers-reduced-motion: reduce)`. |
| `UI-C01` | P2 DOM | `Views/Booking/Index.cshtml`, `Views/Lookup/Index.cshtml`, `wwwroot/js/booking.js`, `wwwroot/js/lookup.js` | Loại bỏ phần tử `.qr-placeholder` ẩn gây tải trùng ảnh QR; gắn `data-testid` trực tiếp lên phần tử hiển thị của `SpikeTicket`. |
| `UI-C02` | P2 A11y | `wwwroot/js/shared.js`, `booking.js`, `dashboard.js`, `management.js`, `wwwroot/css/styles.css` | Bổ sung `role="dialog"`, `aria-modal="true"`, focus trap (`Tab`/`Shift+Tab`), đóng modal bằng `Escape`, trả focus về nút kích hoạt; chuyển các thẻ `div[onclick]` sang `<button type="button">` hoặc gắn `tabindex="0"` + `keydown`. |
| `UI-C03` | P2 States | `wwwroot/js/shared.js`, `dashboard.js`, `booking.js`, `menu.js`, `lookup.js`, `management.js`, `checkin.js`, `reports.js`, `wwwroot/css/styles.css` | Xây dựng helper `renderUiStateBanner()` chuẩn hóa hiển thị 6 trạng thái (`loading`, `empty`, `filtered-empty`, `error`, `offline`, `stale`) trên toàn bộ ứng dụng. |

---

## 3. Quy trình Thực thi & Kiểm định (Execution & Verification Workflow)

1. **Bước 0**: Chụp ảnh màn hình baseline (`docs/ui-v5/screenshots/before/`) và khởi tạo bộ tài liệu kiến trúc V5.
2. **Bước 1**: Sửa lỗi `CreateCourtLockAsync` `referenceNow` trong `BookingService.cs` & test `SqlServerConcurrencyAndMigrationTests.cs`; nâng cấp `/api/db/state` và `/api/bookings/quote` trong `ApiDbController.cs`.
3. **Bước 2**: Triển khai toàn bộ nâng cấp logic dữ liệu (`UI-A01..UI-A08`), giao diện & responsive (`UI-B01..UI-B08`), dọn dẹp DOM & accessibility (`UI-C01..UI-C03`).
4. **Bước 3**: Kiểm tra cú pháp JS (`node --check`), biên dịch .NET (`dotnet build -c Release`), chạy bộ test xUnit (`dotnet test`) kèm bộ test tích hợp V5 mới.
5. **Bước 4**: Chạy Playwright E2E & Visual Audit trên toàn bộ route và breakpoint (`320px` → `1920px`), chụp ảnh `docs/ui-v5/screenshots/after/`, kiểm tra tự động `overflow = 0`, `brokenImage = 0`, `consoleError = 0`, đối chiếu DB thực và hoàn tất `UI_RELEASE_GATE.md`.
