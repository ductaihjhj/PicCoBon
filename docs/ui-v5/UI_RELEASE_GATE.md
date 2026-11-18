# Spike Pickleball Club — UI/UX & Database Compatibility V5 Release Gate (`UI_RELEASE_GATE.md`)

**Ngày nghiệm thu:** 2026-11-10  
**Phiên bản:** V5 (`Antigravity_Master_Prompt_V5_UI_UX_Database_Compatibility`)  
**Môi trường kiểm thử thực:** Windows 11 · ASP.NET Core MVC (`.NET 10.0`) · SQL Server Express (`.\SQLEXPRESS`, Database `PickleballClub`) · Chromium / Playwright Automated Browser Suite  
**Quyết định Release Gate:** **PASS (ĐẠT TOÀN BỘ TIÊU CHÍ NGHIỆM THU V5)**

---

## 1. Tóm tắt Chỉ số Nghiệm thu Cốt lõi (Executive Gate Metrics)

| Hạng mục kiểm định | Tiêu chí bắt buộc (Master Prompt V5) | Kết quả thực tế | Trạng thái |
|---|---|---|---|
| **Build .NET 10 (`Release`)** | `0 errors` | `0 errors, 0 warnings` | **PASS** |
| **Kiểm tra cú pháp JS (`node --check`)** | `0 syntax errors` trên toàn bộ 8 file JS | `8 / 8 PASS (0 errors)` | **PASS** |
| **Bộ test tích hợp & tương tranh SQL Server (`dotnet test`)** | `100% PASS`, bảo toàn toàn bộ chốt chặn V4 + test mới V5 | `26 / 26 PASS (0 failed)` | **PASS** |
| **Khắc phục 19 vấn đề `UI-A01` → `UI-C03` + `UI-NEW-01`** | Sửa trực tiếp mã nguồn, đối chứng UI ↔ API ↔ SQL Server | `20 / 20 PASS` | **PASS** |
| **Ma trận Responsive 11 Breakpoints (`320px` → `1920px`)** | `overflowPx <= 1` trên toàn bộ 8 route chính (`88` lượt kiểm tra) | `88 / 88 PASS (overflowPx = 0)` | **PASS** |
| **Tài nguyên hình ảnh & Console Runtime** | `0 broken images`, `0 console errors`, `0 uncaught page errors` | `0 broken images, 0 console/page errors` | **PASS** |
| **Độ tương phản & Accessibility (WCAG AA)** | Nút Dark Ticket `>= 4.5:1`, Radio/Tab ARIA, Focus Trap Modal | Dark Ticket `.btn-outline` đạt `13.98:1` | **PASS** |
| **An toàn Database & Chính sách Kinh doanh** | Không xóa DB, không đổi schema trái phép, không tự deploy prod | Bảo toàn nguyên vẹn schema & dữ liệu gốc | **PASS** |

---

## 2. Bảng Trạng thái Nghiệm thu Chi tiết (`UI-A01` → `UI-C03` + `UI-NEW-01`)

| Mã ID | Mức độ | Mô tả vấn đề | Files đã sửa | Bằng chứng kiểm thử (Test / Playwright / SQL) | Trạng thái |
|---|---|---|---|---|---|
| **`UI-A01`** | **P0** | UI hiển thị `DEFAULT_COURTS` (4 sân trống giả định) trước khi `/api/db/state` phản hồi hoặc khi mất mạng | `wwwroot/js/shared.js`, `Views/Shared/_Layout.cshtml`, `Views/Home/Index.cshtml`, `Views/Booking/Index.cshtml`, `wwwroot/js/booking.js`, `wwwroot/js/dashboard.js`, `wwwroot/js/menu.js` | Khởi tạo `COURTS = []`, `HAS_SERVER_STATE = false`, máy trạng thái `SPIKE_SYNC_STATE` (`loading`, `ready`, `stale`, `offline`, `error`), khóa `#btnSubmitBooking` khi chưa có xác nhận máy chủ | **PASS** |
| **`UI-A02`** | **P1** | Debounce báo giá 80ms không hủy request cũ & cho phép bấm đặt sân khi báo giá `/api/bookings/quote` chưa xong; thiếu tham số `date` | `Controllers/ApiDbController.cs`, `Views/Booking/Index.cshtml`, `wwwroot/js/booking.js`, `PickleballClub.Tests/SqlServerConcurrencyAndMigrationTests.cs` | Endpoint `/api/bookings/quote` nhận & kiểm tra `date`; client dùng `AbortController` + `invalidateServerQuote()`, hiển thị `#bookingQuoteSourceBadge` (`✓ Báo giá máy chủ`), khóa nút đặt sân khi đang tải báo giá | **PASS** |
| **`UI-A03`** | **P1** | KPI doanh thu trên `/Dashboard` cộng theo `BookingDate` thay vì Sổ cái `TbPaymentTransactions.CreatedAt` như `/Reports` | `Controllers/ApiDbController.cs`, `Views/Shared/Components/Court/Default.cshtml`, `wwwroot/js/shared.js`, `wwwroot/js/dashboard.js` | `/api/db/state` bổ sung `todayLedgerSummary`; `/Dashboard` hiển thị `"Thực thu trong ngày (Sổ cái)"` khớp 100% với `/Reports`, kèm dòng phụ `"Phải thu hôm nay · Công nợ"` | **PASS** |
| **`UI-A04`** | **P1** | `/Reports` nuốt lỗi API trong `catch` và hiện toast `"Đã đồng bộ Sổ cái"` giả khi máy chủ lỗi 500 | `Areas/Admin/Views/Home/Reports.cshtml`, `wwwroot/js/reports.js` | Kiểm thử Playwright chặn route `/api/admin/reports/summary` trả HTTP 500: hiển thị `#reportStatusBanner` cảnh báo dữ liệu cũ và toast lỗi đỏ (`after/12-reports-error-state-1440.png`) | **PASS** |
| **`UI-A05`** | **P2** | Biểu đồ doanh thu 7 ngày trên `/Reports` ép `Math.max(8, ...)` làm ngày `0 đ` vẫn có cột cam cao `8%` | `wwwroot/js/reports.js`, `wwwroot/css/styles.css` | Ngày `0 đ` render `.chart-bar.is-zero` với `height: 0px` (`after/07-reports-1440.png`) | **PASS** |
| **`UI-A06`** | **P2** | `getCourtById(999)` âm thầm trả về `COURTS[0]` (Sân 1) mà không cảnh báo người dùng | `wwwroot/js/shared.js`, `Views/Booking/Index.cshtml`, `wwwroot/js/booking.js`, `wwwroot/js/dashboard.js`, `wwwroot/js/lookup.js`, `wwwroot/js/menu.js`, `wwwroot/js/management.js`, `wwwroot/js/checkin.js` | `getCourtById` trả `null`; truy cập `/Booking?court=999` hiển thị rõ `#bookingDateNotice` cảnh báo mã sân `#999` không tồn tại (`after/10-booking-invalid-court-1440.png`) | **PASS** |
| **`UI-A07`** | **P2** | `#bookingDatePicker` không đồng bộ khi vào `/Booking?date=YYYY-MM-DD`, lệch múi giờ UTC và không giới hạn `[today, today+30d]` | `wwwroot/js/shared.js`, `wwwroot/js/booking.js` | Hàm `parseLocalDateIso` + `normalizeBookingDateSelection` kiểm tra `[today, today+30d]`, đồng bộ hai chiều `#bookingDatePicker` và `#quickDateStrip` | **PASS** |
| **`UI-A08`** | **P1** | Khi sân kín lịch toàn ngày (`availableSlots.length === 0`), `selectedStartTime` vẫn giữ giờ cũ và nút đặt sân vẫn bấm được | `wwwroot/js/booking.js`, `PickleballClub.Tests/SqlServerConcurrencyAndMigrationTests.cs` | Khi `0` slot trống: xóa `selectedStartTime = ''`, khóa `#bookingStart` và `#btnSubmitBooking`, gợi ý các sân khác còn trống trong ngày (`after/11-booking-zero-slots-1440.png`) | **PASS** |
| **`UI-B01`** | **P1** | Nút `.btn-outline` trên vé `Dark Sport` có chữ tối `#0F172A` trên nền tối `#1E293B` (tương phản `1.39:1`) | `wwwroot/css/styles.css` | `.spk-ticket-dark .btn-outline` dùng chữ `#F8FAFC`, nền `rgba(255,255,255,0.08)`, viền `#64748B`, đạt tỷ lệ tương phản **`13.98:1`** (`after/04-ticket-dark-360.png`) | **PASS** |
| **`UI-B02`** | **P1** | Vé điện tử ở màn hình hẹp `320px–430px` bị ép ngang giữa tên sân và mã vé `PB-XXXXXX` | `wwwroot/js/shared.js`, `wwwroot/css/styles.css` | `@media (max-width: 480px)` xếp dọc `.spk-ticket-hero-row`, giữ nguyên mã vé `PB-XXXXXX` không cắt chữ (`noHorizontalClip: true`) | **PASS** |
| **`UI-B03`** | **P2** | Trang chủ ghi sai `"-15% / toàn bộ khung giờ"` và một số trường giờ mở cửa/giá/số sân chưa gắn `data-cms` | `Views/Home/Index.cshtml`, `Views/Shared/_Layout.cshtml`, `Views/Shared/Components/Court/Default.cshtml`, `wwwroot/js/shared.js`, `wwwroot/js/dashboard.js` | Sửa thành `"Đến -15% / hạng thẻ VIP"` (khớp `PricingService.cs`: Silver 5%, Gold 10%, VIP 15%) và đồng bộ `12` phần tử `[data-cms]` | **PASS** |
| **`UI-B04`** | **P2** | Vé điện tử chỉ hiển thị 1 con số `Tổng thanh toán`, thiếu `Đã thanh toán` và `Còn phải thu` | `wwwroot/js/shared.js`, `wwwroot/css/styles.css` | Bổ sung dải `.spk-ticket-ledger-strip` 3 cột (`Tổng phải thanh toán`, `Đã thanh toán`, `Còn phải thu`) trên cả thẻ HTML và ảnh PNG tải về | **PASS** |
| **`UI-B05`** | **P2** | Trên mobile (`<= 960px`), giỏ hàng `/Product` bị đẩy xuống cuối trang sau 12 thẻ sản phẩm | `Views/Product/Index.cshtml`, `wwwroot/js/menu.js`, `wwwroot/css/styles.css` | Bổ sung thanh `#mobileShopCartBar` cố định dưới đáy màn hình (`position: fixed`), cập nhật tức thì số món & tổng tiền (`after/05-product-mobile-375.png`) | **PASS** |
| **`UI-B06`** | **P3** | Tabs lọc danh mục `/Product` thiếu `role="tablist"`, `aria-selected` và cao dưới `44px` trên mobile | `Views/Product/Index.cshtml`, `wwwroot/js/menu.js`, `wwwroot/css/styles.css` | Bổ sung `role="tablist"`, `role="tab"`, `aria-selected`, phím mũi tên Trái/Phải và `min-height: 44px` | **PASS** |
| **`UI-B07`** | **P1** | Sân số 4 (`CourtId = 4`) trong SQL Server lưu `/assets/hero-pickleball-club.png` gây lỗi HTTP 404 | `Controllers/ApiDbController.cs`, `Areas/Admin/Controllers/CourtsController.cs`, `wwwroot/js/shared.js`, `PickleballClub.Tests/SqlServerConcurrencyAndMigrationTests.cs` | Chuẩn hóa qua `NormalizeCourtImageUrl` ở cả backend và frontend; `0` ảnh lỗi trên toàn bộ 88 lượt kiểm tra Playwright (`after/01-home-1440.png`) | **PASS** |
| **`UI-B08`** | **P2** | Trang `/Admin` sử dụng class `.stat-card` chưa được định nghĩa trong `wwwroot/css/styles.css`; thiếu `prefers-reduced-motion` | `wwwroot/css/styles.css` | Định nghĩa đầy đủ `.stat-card` (`border-radius: 16px`, nền trắng, đổ bóng, icon box) và `@media (prefers-reduced-motion: reduce)` (`after/08-admin-1440.png`) | **PASS** |
| **`UI-C01`** | **P2** | Modal xác nhận đặt sân (`#modalQR`) và modal tra cứu (`#modalTicketQR`) render trùng 2 ảnh QR | `Views/Booking/Index.cshtml`, `wwwroot/js/booking.js`, `Views/Lookup/Index.cshtml`, `wwwroot/js/lookup.js` | Gỡ bỏ khối QR trùng lặp, giữ đúng `1` mã QR duy nhất của `SpikeTicket` trong mỗi modal (`qrCountInModal: 1`) | **PASS** |
| **`UI-C02`** | **P2** | Thẻ chọn sân, ô trống ma trận `/Dashboard`, món POS dùng `<div>` thiếu ngữ nghĩa bàn phím; modal thiếu Focus Trap | `wwwroot/js/shared.js`, `wwwroot/js/booking.js`, `wwwroot/js/dashboard.js`, `wwwroot/js/management.js`, `wwwroot/css/styles.css` | Chuyển sang `<button type="button">` / `role="radio"`, bổ sung `:focus-visible`, Focus Trap (`Tab`/`Shift+Tab`), đóng bằng `Escape` và hoàn trả focus | **PASS** |
| **`UI-C03`** | **P2** | Thiếu sự phân biệt rõ giữa dữ liệu rỗng thật (`empty`), rỗng do bộ lọc (`filtered-empty`), đang tải (`loading`), lỗi (`error`), mất mạng (`offline`) và dữ liệu cũ (`stale`) | `wwwroot/js/shared.js`, `wwwroot/js/booking.js`, `wwwroot/js/menu.js`, `wwwroot/js/dashboard.js`, `wwwroot/js/reports.js`, `wwwroot/js/management.js`, `wwwroot/js/checkin.js`, `wwwroot/css/styles.css` | Chuẩn hóa qua `renderUiStateBanner(state, options)` kèm nút hành động cụ thể (`Thử lại kết nối`, `Xóa bộ lọc`) | **PASS** |
| **`UI-NEW-01`** | **P1** | `BookingService.CreateCourtLockAsync` và `ExtendBookingDurationAsync` chưa truyền `referenceNow` xuống `CheckSlotConflictAsync` | `Services/BookingService.cs`, `PickleballClub.Tests/SqlServerConcurrencyAndMigrationTests.cs` | Truyền `referenceNow` xuyên suốt để đồng bộ kiểm tra hết hạn giữ chỗ `HoldExpiresAtUtc`; `26/26` test xUnit PASS | **PASS** |

---

## 3. Ma trận Kiểm thử Responsive 11 Breakpoints (`320px` → `1920px`)

Toàn bộ 8 route trọng tâm đã được kiểm thử tự động bằng Playwright trên 11 kích thước màn hình (`320, 360, 375, 390, 414, 430, 768, 1024, 1280, 1440, 1920`):

| Route | `320` | `360` | `375` | `390` | `414` | `430` | `768` | `1024` | `1280` | `1440` | `1920` | Broken Images |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `/` (Trang chủ) | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0` |
| `/Booking` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0` |
| `/Product` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0` |
| `/Lookup` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0` |
| `/Account/Login` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0` |
| `/Dashboard` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0` |
| `/Reports` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0` |
| `/Admin` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0px` | `0` |

---

## 4. Danh mục Tài liệu Nghiệm thu V5 (`docs/ui-v5/`)

1. `docs/ui-v5/UI_V5_IMPLEMENTATION_PLAN.md` — Kế hoạch triển khai kỹ thuật V5.
2. `docs/ui-v5/DESIGN_SYSTEM.md` — Hệ thống nhận diện thương hiệu Athletic Kinetic Brutalism (`#FF5520`, `#D8F826`, `#0B111E`, `Oswald` + `Plus Jakarta Sans` + `JetBrains Mono`).
3. `docs/ui-v5/ROUTE_MATRIX.md` — Ma trận toàn bộ các route Khách hàng, Vận hành và Quản trị.
4. `docs/ui-v5/DATA_CONTRACT_MATRIX.md` — Ma trận hợp đồng dữ liệu UI ↔ API ↔ SQL Server.
5. `docs/ui-v5/FIX_LOG.md` — Nhật ký sửa lỗi chi tiết cho từng mã `UI-A01` → `UI-C03` và `UI-NEW-01`.
6. `docs/ui-v5/TEST_EVIDENCE.md` — Bằng chứng kiểm thử cú pháp JS, `dotnet test`, SQL Server và Playwright.
7. `docs/ui-v5/UI_RELEASE_GATE.md` — Báo cáo cổng nghiệm thu phát hành V5 (tài liệu này).
8. `docs/ui-v5/playwright-audit-results.json` — Kết quả JSON xuất trực tiếp từ Playwright.
9. `docs/ui-v5/screenshots/before/` & `docs/ui-v5/screenshots/after/` — Bộ ảnh chụp màn hình đối chứng trước và sau khi nâng cấp.
