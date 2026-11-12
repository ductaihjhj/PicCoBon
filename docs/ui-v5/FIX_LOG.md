# Spike Pickleball Club — UI/UX & Database Compatibility V5 Fix Log

**Ngày thực hiện:** 2026-11-10  
**Phiên bản:** V5 (`UI_UX_Database_Compatibility`)  
**Trạng thái:** Đã sửa trực tiếp mã nguồn & kiểm chứng thực tế

---

## 1. Nhóm A — Data Integrity & UI Truthfulness (`UI-A01` → `UI-A08`)

### `UI-A01` (P0 / Critical) — Ngăn chặn hiển thị dữ liệu giả định trước khi `/api/db/state` xác nhận
- **Nguyên nhân gốc (`Root Cause`):** `wwwroot/js/shared.js` khởi tạo `let COURTS = [...DEFAULT_COURTS]` (4 sân mặc định, `BOOKINGS = []`) ngay khi tải file JS. Trước khi `/api/db/state` phản hồi hoặc khi API lỗi/offline, UI hiển thị `"4 / 4 sân trống sẵn sàng"`, cho phép chọn giờ và bấm đặt sân trên dữ liệu chưa xác thực.
- **Files đã sửa:**
  - `wwwroot/js/shared.js` (`COURTS = []`, `SPIKE_SYNC_STATE`, `HAS_SERVER_STATE`, `renderUiStateBanner`, `updateGlobalSyncBanner`, `retryServerSync`)
  - `Views/Shared/_Layout.cshtml` (`#globalSyncBannerHost`)
  - `Views/Home/Index.cshtml`, `Views/Booking/Index.cshtml`, `wwwroot/js/booking.js`, `wwwroot/js/dashboard.js`, `wwwroot/js/menu.js`, `wwwroot/js/management.js`, `wwwroot/js/checkin.js`
- **Giải pháp kỹ thuật:**
  - Khởi tạo `COURTS = []` và `HAS_SERVER_STATE = false`.
  - Khi `!HAS_SERVER_STATE`, mọi màn hình hiển thị trạng thái `loading` / `offline` / `error` rõ ràng kèm nút `"Thử lại kết nối"` và khóa nút gửi đơn (`#btnSubmitBooking.disabled = true`).
  - Khi làm mới nền thất bại sau khi đã có dữ liệu trước đó (`HAS_SERVER_STATE === true`), chuyển sang trạng thái `stale` và hiển thị banner cảnh báo thời điểm đồng bộ gần nhất.

---

### `UI-A02` (P1 / High) — Đồng bộ tức thì & khóa báo giá stale trên `/Booking`
- **Nguyên nhân gốc (`Root Cause`):** `refreshServerBookingQuote()` dùng debounce `80ms` nhưng không hủy (`abort`) request cũ và không khóa nút đặt sân trong lúc chờ `/api/bookings/quote` phản hồi; đồng thời endpoint `/api/bookings/quote` chưa nhận tham số `date`.
- **Files đã sửa:**
  - `Controllers/ApiDbController.cs` (`GetBookingQuoteGet`, `GetBookingQuotePost`, `ComputeQuoteResponseAsync`)
  - `Views/Booking/Index.cshtml` (`#bookingQuoteSourceBadge`)
  - `wwwroot/js/booking.js` (`invalidateServerQuote`, `AbortController`, `quoteRequestSeq`, `hasFreshServerQuote`)
- **Giải pháp kỹ thuật:**
  - Bổ sung tham số `date` (`YYYY-MM-DD`) và kiểm tra hợp lệ `[today, today+30d]` trên `/api/bookings/quote`, trả về `courtId`, `date`, `time`, `durationMinutes`, `pricingVersion`.
  - Bất kỳ thao tác đổi sân, ngày, giờ bắt đầu hoặc thời lượng nào đều gọi ngay `invalidateServerQuote()`, hủy request cũ qua `AbortController`, chuyển badge sang `"Đang tính báo giá máy chủ..."` và vô hiệu hóa `#btnSubmitBooking` cho đến khi nhận được báo giá khớp đúng `(courtId, date, time, duration)`.

---

### `UI-A03` (P1 / High) — Tách biệt "Thực thu trong ngày (Sổ cái)" và "Phải thu ca sân hôm nay" trên `/Dashboard`
- **Nguyên nhân gốc (`Root Cause`):** `wwwroot/js/dashboard.js` tính KPI doanh thu bằng `todayBookings.reduce((s, b) => s + Number(b.paidAmount), 0)` (tổng tiền đã trả của các booking có `BookingDate == hôm nay`), lệch với `/Reports` vốn tính theo `TbPaymentTransactions.CreatedAt` (Sổ cái thu ngân).
- **Files đã sửa:**
  - `Controllers/ApiDbController.cs` (`todayLedgerSummary` trong `/api/db/state`)
  - `Views/Shared/Components/Court/Default.cshtml` (`#kpiRevenue`, `#kpiRevenueSub`)
  - `wwwroot/js/shared.js` (`TODAY_LEDGER_SUMMARY`)
  - `wwwroot/js/dashboard.js` (`renderDashboard`)
- **Giải pháp kỹ thuật:**
  - Backend `/api/db/state` tính `todayLedgerSummary` trực tiếp từ `TbPaymentTransactions` trong ngày hiện tại (`Asia/Ho_Chi_Minh`): `netCollectedToday`, `grossCollectedToday`, `refundedToday`, `todayReceivableTotal`, `todayUnpaidReceivable`.
  - `/Dashboard` hiển thị chính xác `TODAY_LEDGER_SUMMARY.netCollectedToday` cho KPI `"Thực thu trong ngày (Sổ cái)"`, kèm dòng phụ đối chiếu `"Phải thu ca hôm nay"` và `"Công nợ"`.

---

### `UI-A04` (P1 / High) — Ngăn thông báo thành công giả khi `/api/admin/reports/summary` lỗi
- **Nguyên nhân gốc (`Root Cause`):** `renderReports()` trong `wwwroot/js/reports.js` nuốt ngoại lệ trong `catch (_) {}` và không trả trạng thái lỗi; `filterReport()` luôn gọi `showToast('Đã đồng bộ Sổ cái', ..., 'success')` ngay cả khi API trả HTTP 500 hoặc mất mạng.
- **Files đã sửa:**
  - `Areas/Admin/Views/Home/Reports.cshtml` (`#reportStatusBanner`, `#btnRefreshReport`)
  - `wwwroot/js/reports.js` (`renderReports`, `filterReport`, `renderReportBanner`)
- **Giải pháp kỹ thuật:**
  - `renderReports()` sử dụng `AbortController` và trả về `{ ok, status, error, isStale }`.
  - Khi lỗi API hoặc mất mạng, hiển thị `#reportStatusBanner` (cảnh báo dữ liệu cũ kèm mốc thời gian hoặc báo lỗi khi chưa có dữ liệu) và tuyệt đối chỉ hiển thị toast `success` khi `res.ok === true`.

---

### `UI-A05` (P2 / Medium) — Cột biểu đồ doanh thu `0 đ` có chiều cao `0%`
- **Nguyên nhân gốc (`Root Cause`):** `wwwroot/js/reports.js` ép `Math.max(8, ...)` cho chiều cao cột biểu đồ 7 ngày khiến ngày `0 đ` vẫn có cột màu cam cao `8%`.
- **Files đã sửa:**
  - `wwwroot/js/reports.js`
  - `wwwroot/css/styles.css` (`.chart-bar-track`, `.chart-bar.is-zero`)
- **Giải pháp kỹ thuật:**
  - Khi `collected <= 0`, render `.chart-bar.is-zero` với `height: 0%; min-height: 0;`, vạch baseline mảnh tại đáy và nhãn `0 đ` màu trung tính.

---

### `UI-A06` (P2 / Medium) — Không âm thầm đổi `?court=999` về Sân 1
- **Nguyên nhân gốc (`Root Cause`):** `getCourtById(courtId)` trong `shared.js` viết `COURTS.find(...) || COURTS[0]`, khiến mọi mã sân không tồn tại (`?court=999`) bị ép âm thầm về Sân 1.
- **Files đã sửa:**
  - `wwwroot/js/shared.js` (`getCourtById` trả `null`, bổ sung `getCourtDisplayName`)
  - `Views/Booking/Index.cshtml` (`#bookingCourtWarningBanner`)
  - `wwwroot/js/booking.js`, `wwwroot/js/dashboard.js`, `wwwroot/js/lookup.js`, `wwwroot/js/menu.js`, `wwwroot/js/management.js`, `wwwroot/js/checkin.js`
- **Giải pháp kỹ thuật:**
  - `getCourtById(id)` trả `null` khi không tìm thấy sân.
  - Khi URL có `?court=999` không hợp lệ, `/Booking` hiển thị banner cảnh báo rõ ràng (`#bookingCourtWarningBanner`: *"Mã sân #999 trên đường dẫn không tồn tại hoặc đang tạm ngưng..."*) trước khi chọn sân khả dụng đầu tiên.

---

### `UI-A07` (P2 / Medium) — Đồng bộ `#bookingDatePicker` & kiểm tra khoảng ngày `[today, today+30d]`
- **Nguyên nhân gốc (`Root Cause`):** Khi truy cập `/Booking?date=YYYY-MM-DD`, `#bookingDatePicker` vẫn giữ giá trị mặc định `getTodayStr()`, dải `#quickDateStrip` chỉ hiển thị 7 ngày đầu nên không có pill nào active nếu ngày nằm ở tuần sau, và `new Date(isoStr)` bị lệch múi giờ UTC.
- **Files đã sửa:**
  - `wwwroot/js/booking.js` (`parseLocalDateIso`, `validateAndClampBookingDate`, `renderDateStrip`, `onCustomDateChange`)
- **Giải pháp kỹ thuật:**
  - Phân tích ngày theo giờ địa phương `parseLocalDateIso(isoStr)`, kiểm tra hợp lệ trong khoảng `[today, today + 30 ngày]`.
  - Đồng bộ hai chiều giữa `#bookingDatePicker.value` và `#quickDateStrip` (tự động thêm pill ngày đang chọn nếu nằm ngoài 7 ngày đầu tiên).

---

### `UI-A08` (P1 / High) — Khóa đặt sân khi hết sạch khung giờ trống (`availableSlots.length === 0`)
- **Nguyên nhân gốc (`Root Cause`):** Khi sân đã kín lịch hoặc bị khóa bảo trì toàn bộ ngày, `#timeSlotGrid` báo hết chỗ nhưng `selectedStartTime` vẫn giữ giá trị cũ (`18:00`) và nút `#btnSubmitBooking` vẫn cho phép bấm.
- **Files đã sửa:**
  - `wwwroot/js/booking.js` (`renderTimeSlots`, `renderSummary`, `submitBooking`)
- **Giải pháp kỹ thuật:**
  - Khi `availableSlots.length === 0`, đặt `selectedStartTime = ''`, vô hiệu hóa `#bookingStart` và `#btnSubmitBooking`, đồng thời gợi ý các sân khác còn trống trong cùng ngày hoặc nút chuyển sang ngày tiếp theo.

---

## 2. Nhóm B — Visual Hierarchy, Responsive & Component Fidelity (`UI-B01` → `UI-B08`)

### `UI-B01` (P1 / High) — Tương phản nút `.btn-outline` trên vé Dark Sport (`>= 10:1`)
- **Files đã sửa:** `wwwroot/css/styles.css` (`.spk-ticket-dark .btn-outline`)
- **Giải pháp kỹ thuật:** Thiết lập `color: #F8FAFC; background: rgba(255, 255, 255, 0.08); border-color: #64748B;` kèm trạng thái `:hover`, `:active`, `:focus-visible` (`outline: 2px solid var(--color-volt)`), đạt tỷ lệ tương phản `> 11:1` trên nền `#1E293B`.

### `UI-B02` (P1 / High) — Bố cục vé điện tử trên màn hình hẹp `320px–430px`
- **Files đã sửa:** `wwwroot/js/shared.js`, `wwwroot/css/styles.css` (`@media (max-width: 480px)`)
- **Giải pháp kỹ thuật:** Chuyển `.spk-ticket-hero-row` sang bố cục xếp dọc gọn gàng trên mobile, giữ mã vé `PB-XXXXXX` (`white-space: nowrap`) không bị cắt chữ hoặc đè lên tên sân, chia đều các nút thao tác thành lưới 2 cột dễ bấm.

### `UI-B03` (P2 / Medium) — Đồng bộ dữ liệu CMS & chính sách chiết khấu hạng thẻ
- **Files đã sửa:** `Views/Home/Index.cshtml`, `Views/Shared/_Layout.cshtml`, `Views/Shared/Components/Court/Default.cshtml`, `wwwroot/js/shared.js`, `wwwroot/js/dashboard.js`
- **Giải pháp kỹ thuật:** Gắn thuộc tính `data-cms-field` (`openHours`, `address`, `hotline`, `courtPriceFormatted`, `peakSurchargeFormatted`, `vipSurchargeFormatted`, `courtCount`) và cập nhật nội dung chiết khấu hội viên chính xác với `PricingService.cs` (`Silver -5%`, `Gold -10%`, `VIP -15%`).

### `UI-B04` (P2 / Medium) — Hiển thị đủ 3 chỉ số tài chính trên Vé Điện Tử & ảnh PNG tải về
- **Files đã sửa:** `wwwroot/js/shared.js` (`SpikeTicket.renderTicketCardHtml`, `SpikeTicket.downloadTicketPng`), `wwwroot/css/styles.css` (`.spk-ticket-ledger-strip`)
- **Giải pháp kỹ thuật:** Bổ sung dải đối soát 3 cột trên cả thẻ vé HTML và bản vẽ Canvas PNG (`Tổng phải thanh toán`, `Đã thanh toán`, `Còn phải thu` + huy hiệu trạng thái thanh toán).

### `UI-B05` (P2 / Medium) — Thanh giỏ hàng cố định dưới đáy màn hình (`#mobileShopCartBar`) trên `/Product`
- **Files đã sửa:** `Views/Product/Index.cshtml`, `wwwroot/js/menu.js`, `wwwroot/css/styles.css`
- **Giải pháp kỹ thuật:** Hiển thị thanh tóm tắt giỏ hàng cố định ở đáy màn hình (`<= 960px`) với số món, tổng tiền và nút `"Xem giỏ & Đặt ngay"` tự động cuộn mượt xuống `#shopCartPanel`.

### `UI-B06` (P3 / Low) — Tabs danh mục `/Product` chuẩn ARIA `tablist` & vùng chạm `>= 44px`
- **Files đã sửa:** `Views/Product/Index.cshtml`, `wwwroot/js/menu.js`, `wwwroot/css/styles.css`
- **Giải pháp kỹ thuật:** Bổ sung `role="tablist"`, `role="tab"`, `aria-selected`, điều hướng phím mũi tên Trái/Phải, tự động `scrollIntoView({ inline: 'center' })` và `min-height: 44px` trên mobile.

### `UI-B07` (P1 / High) — Sửa triệt để lỗi ảnh `/assets/hero-pickleball-club.png` (HTTP 404)
- **Files đã sửa:** `Controllers/ApiDbController.cs` (`NormalizeCourtImageUrl`), `Areas/Admin/Controllers/CourtsController.cs`, `wwwroot/js/shared.js` (`normalizeCourtAssetUrl`)
- **Giải pháp kỹ thuật:** Chuẩn hóa toàn bộ đường dẫn ảnh sân bị trống hoặc trỏ tới `/assets/hero-pickleball-club.png` sang `/assets/court-vip-arena.png` (sân Indoor/VIP) hoặc `/assets/hero-spike-pickleball.png` (sân Outdoor) ở cả API `/api/db/state`, controller quản trị sân và bộ đọc dữ liệu phía client.

### `UI-B08` (P2 / Medium) — Bổ sung CSS `.stat-card` cho `/Admin` & `@media (prefers-reduced-motion: reduce)`
- **Files đã sửa:** `wwwroot/css/styles.css`
- **Giải pháp kỹ thuật:** Định nghĩa đầy đủ `.stat-card`, `.stat-card-top`, `.stat-card-label`, `.stat-card-icon`, `.stat-card-value`, `.stat-card-meta` đồng bộ với `.stat-widget` và thêm `@media (prefers-reduced-motion: reduce)`.

---

## 3. Nhóm C — Duplication, Accessibility & State Feedback (`UI-C01` → `UI-C03`)

### `UI-C01` (P2 / Medium) — Loại bỏ render trùng 2 ảnh QR trong cùng modal
- **Files đã sửa:** `Views/Booking/Index.cshtml`, `wwwroot/js/booking.js`, `Views/Lookup/Index.cshtml`, `wwwroot/js/lookup.js`
- **Giải pháp kỹ thuật:** Gỡ bỏ việc gọi `renderBookingQrHtml(...)` lần thứ hai trong khối ẩn `.qr-placeholder`, chỉ giữ duy nhất thẻ QR chính thức của `SpikeTicket` trong khi vẫn bảo toàn các `span` text (`#confirmBookingCode`, `#ticketQrCode`) phục vụ kiểm thử tự động.

### `UI-C02` (P2 / Medium) — Nâng cấp Accessibility bàn phím, ngữ nghĩa `<button>`, ARIA & Focus Trap Modal
- **Files đã sửa:** `wwwroot/js/shared.js`, `wwwroot/js/booking.js`, `wwwroot/js/dashboard.js`, `wwwroot/js/management.js`, `wwwroot/css/styles.css`
- **Giải pháp kỹ thuật:**
  - Chuyển `.court-option` sang `<button type="button" role="radio" aria-checked="...">` bên trong `#courtSelector[role="radiogroup"]` hỗ trợ phím mũi tên.
  - Chuyển ô trống trên bảng ma trận `/Dashboard` sang `<button type="button" class="matrix-slot-btn">+ Đặt</button>`, món POS sang `<button type="button" class="pos-menu-item">`, mã vé & khung phóng to QR sang `<button type="button">`.
  - Bổ sung Focus Trap (`Tab` / `Shift+Tab`), đóng bằng phím `Escape`, `role="dialog"`, `aria-modal="true"` và trả lại focus về phần tử kích hoạt khi đóng modal trong `openModal()` / `closeModal()` và `SpikeTicket.openZoomModal()`.

### `UI-C03` (P2 / Medium) — Chuẩn hóa 6 trạng thái giao diện (`loading`, `empty`, `filtered-empty`, `error`, `offline`, `stale`)
- **Files đã sửa:** `wwwroot/js/shared.js` (`renderUiStateBanner`), `wwwroot/js/booking.js`, `wwwroot/js/menu.js`, `wwwroot/js/dashboard.js`, `wwwroot/js/reports.js`, `wwwroot/js/management.js`, `wwwroot/js/checkin.js`, `wwwroot/css/styles.css`
- **Giải pháp kỹ thuật:** Xây dựng helper `renderUiStateBanner(state, options)` hiển thị rõ nguyên nhân và nút hành động tương ứng (`Thử lại kết nối` cho `error`/`offline`/`stale`, `Xóa bộ lọc` cho `filtered-empty`).

---

## 4. Vấn đề phát hiện thêm trong quá trình kiểm thử (`UI-NEW-01`)

### `UI-NEW-01` — Đồng bộ mốc thời gian tham chiếu `referenceNow` trong `CreateCourtLockAsync`
- **Nguyên nhân gốc (`Root Cause`):** `BookingService.CreateCourtLockAsync` gọi `CheckSlotConflictAsync` với `referenceNow = null` (dùng `DateTime.UtcNow` thực tế), khiến kiểm thử tương tranh với mốc thời gian giả lập `ReferenceNow` có thể đánh giá sai thời hạn giữ chỗ (`HoldExpiresAtUtc`) của đơn `pending`.
- **Files đã sửa:** `Services/BookingService.cs`, `PickleballClub.Tests/SqlServerConcurrencyAndMigrationTests.cs`
- **Giải pháp kỹ thuật:** Bổ sung tham số tùy chọn `DateTime? referenceNow = null` cho `CreateCourtLockAsync` và truyền xuống `CheckSlotConflictAsync`.
