# Spike Pickleball Club — V5 Test Evidence & Verification Report

**Ngày thực hiện:** 2026-11-10  
**Môi trường kiểm thử thực:** Windows 11 · .NET 10.0 (`net10.0`) · SQL Server Express (`.\SQLEXPRESS`, Database `PickleballClub`) · Chromium / Playwright Automated Browser Suite

---

## 1. Kết quả kiểm tra cú pháp JavaScript (`node --check`)

Toàn bộ 8 tệp JavaScript chính tại `wwwroot/js/` đều được kiểm tra cú pháp tĩnh bằng Node.js `v22.20.0`:

```powershell
node --check wwwroot/js/shared.js
node --check wwwroot/js/booking.js
node --check wwwroot/js/dashboard.js
node --check wwwroot/js/reports.js
node --check wwwroot/js/menu.js
node --check wwwroot/js/lookup.js
node --check wwwroot/js/management.js
node --check wwwroot/js/checkin.js
```

- **Kết quả:** `0 syntax errors` (`100% PASS`).

---

## 2. Kết quả Build & Kiểm thử Tích hợp Backend / SQL Server (`dotnet test`)

Chạy bộ kiểm thử tự động `PickleballClub.Tests` trực tiếp trên SQL Server (`.\SQLEXPRESS`):

```powershell
dotnet test e:\pickleball\PickleballClub.Tests\PickleballClub.Tests.csproj -c Release --no-restore
```

- **Kết quả thực thi:**
  - **Tổng số test:** `26`
  - **Passed:** `26`
  - **Failed:** `0`
  - **Skipped:** `0`
- **Các kịch bản kiểm thử trọng tâm đã PASS:**
  1. `ParallelBookingSubmissions_SameCourtAndOverlappingSlot_OnlyOneSucceeds` — Chống đặt trùng sân khi nhiều thiết bị bấm đặt cùng lúc (`Serializable` transaction).
  2. `ParallelBookingVsLock_And_ParallelExtendVsAdjacentBooking_MutualExclusionHolds` — Khóa bảo trì (`CreateCourtLockAsync`) và gia hạn giờ chơi (`ExtendBookingDurationAsync`) loại trừ tương hỗ tuyệt đối với đặt sân mới (`UI-NEW-01`).
  3. `ParallelPosCheckout_LimitedStock_NeverCreatesNegativeStock` — Chống âm kho Pro Shop khi thanh toán đồng thời.
  4. `ParallelPaymentCollection_SameBooking_NeverExceedsAmountDue` — Chống thu lố công nợ trên sổ cái `TbPaymentTransactions`.
  5. `BookingQuote_MatchesServerCreateBookingCalculation_AndDetectsStalePricingVersion` — Đảm bảo báo giá `/api/bookings/quote` khớp 100% với `CreateBookingAsync` và phát hiện `pricingVersion` cũ (`409 Conflict` + `latestQuote`) (`UI-A02`).
  6. `UiV5_CourtAssetNormalization_And_FullDayLockRejectsStaleSlotSubmission` — Kiểm chứng chuẩn hóa đường dẫn ảnh sân (`NormalizeCourtImageUrl` khử triệt để `/assets/hero-pickleball-club.png` — `UI-B07`) và từ chối đặt sân khi sân bị khóa kín lịch toàn ngày (`UI-A08`).

---

## 3. Đối chiếu dữ liệu UI ↔ API ↔ SQL Server (Database Reconciliation)

| Hạng mục đối chiếu | Nguồn SQL Server (`PickleballClub`) | Endpoint API | Hiển thị trên UI | Kết quả |
|---|---|---|---|---|
| Danh sách sân & ảnh sân (`UI-B07`) | `SELECT CourtId, Name, CourtType, BasePrice, Surcharge, Image FROM tb_Court WHERE IsActive = 1` (`4` sân; `CourtId = 4` có `Image = '/assets/hero-pickleball-club.png'` trong DB gốc) | `GET /api/db/state` (`NormalizeCourtImageUrl` chuẩn hóa thành `/assets/hero-spike-pickleball.png`) | Toàn bộ 4 thẻ sân trên `/`, `/Booking`, `/Dashboard`, `/Admin/Courts` tải ảnh thành công (`naturalWidth > 0`, `0` lỗi 404) | **PASS** |
| Báo giá bậc thang 30 phút (`UI-A02`) | `tb_ClubSettings` (`CourtPrice = 120000`, `PeakSurcharge = 30000`, `PeakStart = '17:00'`, `PricingVersion`) + `tb_Court` (`Surcharge`) | `GET /api/bookings/quote?courtId=1&date=...&time=...&duration=60` | `#bookingQuoteSourceBadge` hiển thị `✓ Báo giá máy chủ`, khóa nút submit khi đang tải báo giá hoặc khi mạng lỗi | **PASS** |
| Đối soát Thực thu Sổ cái vs Phải thu (`UI-A03`) | `SELECT SUM(CASE WHEN Direction='in' THEN Amount ELSE -Amount END) FROM tb_PaymentTransaction WHERE CreatedAt >= @todayStartUtc` vs `SELECT SUM(AmountDue), SUM(PaidAmount) FROM tb_Booking WHERE BookingDate = @today` | `GET /api/db/state` (`todayLedgerSummary.netCollectedToday`, `todayReceivableTotal`, `todayUnpaidReceivable`) | `/Dashboard` hiển thị KPI `"Thực thu trong ngày (Sổ cái)"` khớp 100% với `/Reports`, kèm dòng phụ `"Phải thu hôm nay: ... · Công nợ: ..."` | **PASS** |
| Chi tiết 3 chỉ số thanh toán trên Vé QR (`UI-B04`) | `tb_Booking.AmountDue`, `tb_Booking.PaidAmount`, `RemainingDue = AmountDue - PaidAmount`, `PaymentStatus` | `POST /api/bookings`, `GET /api/bookings/lookup` | Vé điện tử (`SpikeTicket`) hiển thị đủ 3 ô: `Tổng phải thanh toán`, `Đã thanh toán`, `Còn phải thu` và huy hiệu trạng thái sổ cái | **PASS** |
| Trạng thái lỗi API Báo cáo (`UI-A04`) | Giả lập lỗi `HTTP 500` trên `/api/admin/reports/summary` | `GET /api/admin/reports/summary` | Hiển thị `#reportStatusBanner` cảnh báo dữ liệu cũ + toast lỗi màu đỏ; không bao giờ hiện toast `"Đã đồng bộ Sổ cái"` giả | **PASS** |
| Cột biểu đồ ngày doanh thu `0 đ` (`UI-A05`) | Ngày trong chuỗi 7 ngày không có phát sinh `tb_PaymentTransaction` (`collected = 0`) | `GET /api/admin/reports/summary` (`dailySeries`) | Cột `.chart-bar.is-zero` có chiều cao `0px` (`height: 0%`), không còn cột màu cam giả `8%` | **PASS** |

---

## 4. Kiểm thử Tự động Trình duyệt Thực với Playwright (`verify-ui-v5-after.js`)

Kịch bản Playwright (`docs/ui-v5/playwright-audit-results.json`) đã kiểm tra trực tiếp trên trình duyệt Chromium đối với máy chủ ASP.NET Core đang chạy kết nối SQL Server:

- **Console Errors:** `0`
- **Uncaught Page Errors:** `0`
- **Broken Images (`!img.complete || img.naturalWidth === 0`):** `0` trên toàn bộ `88` lượt kiểm tra (`8` routes × `11` breakpoints).
- **Ma trận Responsive 11 Breakpoints (`320px`, `360px`, `375px`, `390px`, `414px`, `430px`, `768px`, `1024px`, `1280px`, `1440px`, `1920px`):**
  - Các route kiểm tra: `/`, `/Booking`, `/Product`, `/Lookup`, `/Account/Login`, `/Dashboard`, `/Reports`, `/Admin`.
  - Kết quả: `88 / 88` lượt kiểm tra đạt `overflowPx = 0` (`scrollWidth <= innerWidth`).

---

## 5. Danh mục Ảnh Chụp Màn Hình Thực Tế (Before / After Screenshots)

### Ảnh chụp trước khi sửa (`docs/ui-v5/screenshots/before/`)
- `docs/ui-v5/screenshots/before/01-home-1440.png` — Trang chủ `/` (Court #4 bị gãy ảnh `/assets/hero-pickleball-club.png`, thẻ hội viên ghi sai `-15% / toàn bộ khung giờ`).
- `docs/ui-v5/screenshots/before/02-booking-1440.png` — Trang `/Booking` desktop trước khi nâng cấp badge báo giá máy chủ.
- `docs/ui-v5/screenshots/before/03-booking-mobile-360.png` — Trang `/Booking` ở `360px`.
- `docs/ui-v5/screenshots/before/04-ticket-dark-360.png` — Vé điện tử chế độ `Dark Sport` ở `360px` (chữ nút `.btn-outline` tối đen trên nền tối `1.39:1`, mã vé ép ngang).
- `docs/ui-v5/screenshots/before/05-product-mobile-375.png` — Trang `/Product` ở `375px` (chưa có thanh giỏ hàng cố định dưới đáy màn hình).
- `docs/ui-v5/screenshots/before/06-dashboard-1440.png` — Trang `/Dashboard` trước khi tách biệt Thực thu Sổ cái và Phải thu ca sân.
- `docs/ui-v5/screenshots/before/07-reports-1440.png` — Trang `/Reports` (ngày `0 đ` vẫn vẽ cột cam cao `8%`).
- `docs/ui-v5/screenshots/before/08-admin-1440.png` — Trang `/Admin` (thẻ `.stat-card` thiếu CSS trong `styles.css`).

### Ảnh chụp sau khi sửa (`docs/ui-v5/screenshots/after/`)
- `docs/ui-v5/screenshots/after/01-home-1440.png` — Trang chủ `/` (`4/4` ảnh sân hiển thị sắc nét, thẻ hội viên hiển thị đúng `"Đến -15% / hạng thẻ VIP"`).
- `docs/ui-v5/screenshots/after/02-booking-1440.png` — Trang `/Booking` với huy hiệu `"✓ Báo giá máy chủ"` và `role="radiogroup"` chọn sân.
- `docs/ui-v5/screenshots/after/03-booking-mobile-360.png` — Trang `/Booking` responsive ở `360px`.
- `docs/ui-v5/screenshots/after/04-ticket-dark-360.png` — Vé điện tử `Dark Sport` ở `360px` (nút `.btn-outline` sáng rõ `13.98:1`, đủ 3 chỉ số thanh toán, 1 mã QR duy nhất, không tràn ngang).
- `docs/ui-v5/screenshots/after/05-product-mobile-375.png` — Trang `/Product` ở `375px` với thanh giỏ hàng cố định `#mobileShopCartBar` dưới đáy màn hình và tabs danh mục chuẩn `44px`.
- `docs/ui-v5/screenshots/after/06-dashboard-1440.png` — Trang `/Dashboard` với KPI `"Thực thu trong ngày (Sổ cái)"` kèm dòng đối soát `"Phải thu hôm nay · Công nợ"` và nút `<button class="matrix-slot-btn">+ Đặt</button>`.
- `docs/ui-v5/screenshots/after/07-reports-1440.png` — Trang `/Reports` với cột ngày `0 đ` có chiều cao `0px`.
- `docs/ui-v5/screenshots/after/08-admin-1440.png` — Trang `/Admin` với hệ thống `.stat-card` đồng bộ.
- `docs/ui-v5/screenshots/after/09-home-mobile-320.png` — Trang chủ `/` tại khung nhìn hẹp nhất `320px` (`0px` tràn ngang).
- `docs/ui-v5/screenshots/after/10-booking-invalid-court-1440.png` — Trang `/Booking?court=999` hiển thị rõ banner cảnh báo mã sân không tồn tại (`UI-A06`).
- `docs/ui-v5/screenshots/after/11-booking-zero-slots-1440.png` — Trang `/Booking` khi sân kín lịch toàn ngày: khóa nút đặt sân và gợi ý chọn sân khác còn trống (`UI-A08`).
- `docs/ui-v5/screenshots/after/12-reports-error-state-1440.png` — Trang `/Reports` khi API lỗi 500: hiển thị banner cảnh báo dữ liệu cũ và toast báo lỗi trung thực (`UI-A04`).
- `docs/ui-v5/screenshots/after/13-lookup-ticket-light-1440.png` — Modal vé QR trên `/Lookup` hiển thị duy nhất 1 mã QR và đủ 3 trường sổ cái (`UI-B04`, `UI-C01`).
