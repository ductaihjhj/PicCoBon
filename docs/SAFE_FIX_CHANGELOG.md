# SAFE FIX CHANGELOG — SPIKE PICKLEBALL CLUB

**Branch:** `fix/safe-logic-remediation` (nhánh từ `fix/v4-release-blockers-and-qr` @ `49ceb36`)  
**Phạm vi thay đổi:** Chỉ khắc phục lỗi logic, thời gian, hợp đồng API, công nợ/giao hàng, khả năng truy cập & dữ liệu SSR — **không thay đổi schema SQL Server, không thay đổi thuật toán tính giá/sổ cái/tồn kho, không thay đổi định dạng/chữ ký/ảnh QR, và không redesign giao diện**.

---

## 1. Phân tách ranh giới thay đổi

Tập thay đổi trên workspace bao gồm hai nhóm rõ ràng:
1. **Nhóm V7/QA có sẵn trước khi chạy Master Prompt 01** (được bảo toàn nguyên vẹn và sao lưu snapshot diff tại artifact `pre_remediation_diff.patch`):
   - `Areas/Admin/Views/{Accounts,Bookings,Courts,Home/{Checkin,Index,Reports},Login,Products,Register,Shared/_Layout}.cshtml`, `Views/_ViewImports.cshtml`, `Views/Shared/Components/Court/Default.cshtml`, `wwwroot/css/styles.css`, `wwwroot/js/lookup.js`, `PickleballClub.Tests/ShadcnAdminV7ParityTests.cs`, `docs/ui-v7/`, `docs/qa/qr/*`.
2. **Nhóm thay đổi thuộc phạm vi Master Prompt 01 (dưới đây)**:

---

## 2. Chi tiết thay đổi theo Batch

### Batch A — Timezone Việt Nam, Giá trị cấu hình & Trạng thái máy chủ (`FIX-01`, `FIX-02`, `FIX-13`, `FIX-20`)

- `wwwroot/js/shared.js`:
  - Đặt `const DEFAULT_COURTS = [];` để loại bỏ hoàn toàn dữ liệu 4 sân mẫu giả lập khi chưa đồng bộ hoặc lỗi API (`FIX-01`).
  - Bổ sung `resolveNumericConfig(value, fallback)` xử lý an toàn `0`, chuỗi số `"0"`, `null`, `undefined`, chuỗi rỗng và `NaN` (`FIX-20`). Áp dụng thống nhất tại `COURT_PRICE_PER_HOUR`, `applyCmsToFooterAndGlobal()`, `getCourtHourlyRate()` (thay cho toán tử `|| 30000`), và `calculateSegmentedPricing()` (thay cho `Number(undefined) ?? 30000`).
  - Bổ sung `VIETNAM_UTC_OFFSET_MS`, `SERVER_TIME_OFFSET_MS` (cập nhật từ `data.serverTimeUtc` trong `syncDatabaseFromServer()`), `getVietnamNowDate()`, chuẩn hóa toàn bộ `getTodayStr()`, `getOffsetDateStr()`, `getNowMinutes()`, `formatSyncTimeHHMMSS()`, `isPendingBookingHoldActive()`, `getPendingHoldRemainingLabel()` và `reconcileBookingsByRealTime()` theo múi giờ chuẩn Việt Nam `UTC+7` (`FIX-13`).
- `wwwroot/js/dashboard.js`:
  - Áp dụng `resolveNumericConfig(CMS_CONFIG.peakSurcharge, 30000)` (`FIX-20`).
  - Giữ nguyên thẻ `<span data-cms="courtCount">` trong `#heroLiveCountTag` khi hydrate (`FIX-18`).
- `wwwroot/js/reports.js`:
  - Đổi giá trị mặc định `reportFrom` sang `${getTodayISO().slice(0, 7)}-01` để đồng bộ đầu tháng theo giờ Việt Nam (`FIX-13`).
- `Areas/Admin/Views/Home/Cms.cshtml`:
  - Dùng `resolveNumericConfig(...)` cho `#cmsCourtPrice`, `#cmsPeakSurcharge` trong `populateCmsForm()`, `updateLivePreview()`, và `handleSaveCMS()` để giữ nguyên giá trị `0 đ` khi cấu hình không phụ thu giờ vàng (`FIX-20`).

### Batch B — Điều phối, POS/Pro Shop Dispatch, Sổ Cái & Khóa Sân (`FIX-02`, `FIX-04`, `FIX-05`, `FIX-10`, `FIX-11`, `FIX-14`)

- `Areas/Admin/Views/Home/Management.cshtml`:
  - Mở rộng thẻ "Đơn Hàng Pro Shop (`#standaloneOrdersCount`)" bổ sung ô tìm kiếm `#shopOrderSearch`, bộ lọc `#shopOrderFilter` (`all`, `pending_fulfill`, `delivered`, `unpaid`, `cancelled`), và chân trang `#shopOrdersPagination` (`FIX-04`, `FIX-14`).
  - Bổ sung khối phục hồi `#ledgerPartialFailureBanner` kèm nút `#btnRetryLedgerFinishOnly` ("Chỉ thử Đóng ca lại — KHÔNG thu tiền lại") trong `#modalLedgerSettlement` (`FIX-05`).
  - Thay `<option value="960">` cố định ở `#lockDuration` bằng `<option value="full_day" id="lockDurationFullDayOption">`, gắn `onchange` tính khung khóa khớp giờ mở/đóng cửa thực tế, hiển thị `#lockWindowPreview`, và gắn `id="btnCreateCourtLock"` (`FIX-02`, `FIX-11`).
- `wwwroot/js/management.js`:
  - Thay `lockSelectedCourt = 1` bằng độ phân giải động từ `COURTS`; vô hiệu hóa `#btnCreateCourtLock` và chặn thực thi nếu `COURTS` rỗng (`FIX-02`).
  - Tính khung khóa `full_day` theo `CMS_CONFIG.openTime` → `CMS_CONFIG.closeTime`, chặn gửi nếu vượt quá `closeTime` / `24:00` hoặc chọn ngày quá khứ (`FIX-11`).
  - Tải đầy đủ cả đơn lẻ và đơn gộp sân qua `GET /api/admin/orders?standaloneOnly=false` (`loadAdminShopOrders()`), bổ sung lọc, tìm kiếm, phân trang từng đợt 25 đơn (`loadMoreShopOrders()`), huy hiệu trạng thái giao đồ (`pending`/`delivered`), và hàm `fulfillAdminOrder()` gọi `POST /api/admin/orders/{code}/fulfill` có khóa chống double-click (`FIX-04`, `FIX-14`).
  - Chuẩn hóa `approveAllPending()` chỉ duyệt đơn `isPendingBookingHoldActive(b)`, hỏi hộp thoại `confirm()`, khóa `#approveAllBtn` khi xử lý và thống kê chi tiết `đã duyệt / trùng lịch hoặc hết giữ chỗ / lỗi` (`FIX-10`).
  - Tách kiểm tra `finishRes.ok` sau khi thu tiền thành công trong `submitLedgerPayment()`: khi `/payments` đã lưu nhưng `/finish` thất bại, giữ modal mở, cập nhật sổ cái, hiển thị `#ledgerPartialFailureBanner` và nút `retryLedgerFinishOnly()` chỉ gọi `/finish` mà tuyệt đối không gửi lại bút toán thu tiền (`FIX-05`).

### Batch C — Đặt sân, Pro Shop, Báo cáo & Phát hiện hợp đồng API bị lệch (`FIX-03`, `FIX-06`, `FIX-07`, `FIX-08`, `FIX-09`, `FIX-12`, `FIX-16`, `FIX-17`, `FIX-R1..R3`)

- `wwwroot/js/menu.js` & `Views/Product/Index.cshtml`:
  - Sửa bộ lọc `populateActiveBookingOptions()` khớp 100% backend `OrderService`: chỉ hiển thị đơn thuộc `b.date === today` (giờ VN) và chưa kết thúc (`phase !== 'ended'`), đồng thời hiển thị ghi chú giải thích trong `#shopOrderBookingHint` khi hội viên có đơn tương lai/đã kết thúc không thể gộp (`FIX-03`).
  - Sửa nhãn trường gộp đơn trong `Views/Product/Index.cshtml` từ `"Hình thức thanh toán / Gộp vào sân"` thành `"Nhận đồ tại quầy / Gộp vào sân đang chơi"` (`FIX-16`).
  - **[Bug phát hiện qua E2E — `FIX-R2`]:** Sửa `submitShopOrder()` từ `POST /api/orders` (route 404 không tồn tại) sang `POST /api/orders/create`, đổi khóa JSON `deliveryNote` → `address`, `quantity` → `qty`, đưa `idempotencyKey` vào body JSON, đọc `ord.address` ở modal thành công, và gọi helper `isValidVietnamPhone(phone)` vừa trích xuất chung ở `shared.js` (tránh `ReferenceError: isValidVietnamPhone is not defined`).
- `wwwroot/js/reports.js`:
  - Thêm `AbortController` timeout 15 giây trong `renderReports()` kèm thông báo quá hạn/mất mạng và giữ banner `stale` khi đã có dữ liệu lần trước (`FIX-06`).
  - Xóa bỏ `Math.max(0, Number(c.collectedRevenue))` trong `renderReportCourts()` để bảo toàn doanh thu ròng âm khi hoàn tiền vượt thu (`height: 0%`, nhãn âm `-100.000 đ`, class `.is-negative`) (`FIX-07`).
- `wwwroot/js/booking.js` & `Views/Booking/Index.cshtml`:
  - Đưa `PRICING_VERSION` vào `makeQuoteKey(...)` để vô hiệu ngay báo giá cũ khi Admin đổi bảng giá CMS (`FIX-08`).
  - Bổ sung kiểm tra hạn ngày đặt, giờ qua lượt hôm nay (`UTC+7`), và giới hạn mở/đóng cửa trong `submitBooking()` trước khi gọi POST (`FIX-09`).
  - Đồng bộ trạng thái URL (`?court=&date=&time=&duration=`) qua `syncBookingUrlState()` bằng `window.history.replaceState` (`FIX-12`).
  - Cập nhật động `#bookingModalTitle` và toast trong `modalQR`: phân biệt rõ `"Đã Tiếp Nhận — Chờ Duyệt"` (với đồng hồ giữ chỗ `pending`) và `"Đặt Sân Thành Công"` (`approved`) (`FIX-17`).
  - Dùng `resolveNumericConfig` cho giá hiển thị trên thẻ sân (`FIX-20`).
  - **[Bug phát hiện qua xUnit/E2E — `FIX-R1`]:** Bổ sung `normalizeServerQuotePayload(q)` để ánh xạ đúng thuộc tính camelCase từ backend `BookingQuoteBreakdown` (`standardHourlyRate`, `peakHourlyRate`, `standardMinutes`, `courtFeeBeforeDiscount`, `segments`) thay vì tên cũ không tồn tại (`standardRate`, `peakRate`, `normalMinutes`, `normalAmount`, `courtFee`) từng gây hiển thị `0 đ / giờ` sau khi nhận báo giá máy chủ.
- `wwwroot/js/shared.js`:
  - Bổ sung `isValidVietnamPhone()` dùng chung cho `menu.js` và `booking.js`.
  - **[Bug phát hiện qua E2E — `FIX-R3`]:** Lọc payload trong `saveCmsConfig()` theo danh sách `WRITABLE_CMS_KEYS` khớp `ClubSettingsService.AllowedCmsKeys`, loại bỏ `pricingVersion`, `openHours`, `address` khiến `PUT /api/admin/cms` trước đó luôn trả về `400 Bad Request`.

### Batch D — Trợ năng (Accessibility) & Tính trung thực dữ liệu SSR (`FIX-15`, `FIX-18`, `FIX-19`)

- `wwwroot/js/shared.js`: Sửa click backdrop trên `.modal-overlay` gọi `closeModal(overlay.id)` để khôi phục focus và đặt `aria-hidden="true"` (`FIX-15`).
- `wwwroot/js/booking.js`: Bổ sung `role="button"`, `tabindex="0"`, `aria-label` và handler `Enter`/`Space` cho từng ô `.timeline-seg` khả dụng (`FIX-15`).
- `Views/Account/Profile.cshtml`: Chuyển `<span class="paddle-chip" onclick=...>` sang `<button type="button" class="paddle-chip" ...>` kèm `font-family: inherit;` (`FIX-15`).
- `Views/Shared/_Layout.cshtml`, `Views/Home/Index.cshtml`, `Views/Booking/Index.cshtml`, `Views/Account/Login.cshtml`, `Areas/Admin/Views/Home/Cms.cshtml`: Inject read-only `ClubSettingsService` & `PickleballContext` (`AsNoTracking()`) để render giá tiêu chuẩn, giá giờ vàng, phụ thu VIP, giờ mở cửa và số lượng sân đang hoạt động (`@activeCourtCount sân`) ngay từ SSR HTML (`FIX-18`), thay các con số tĩnh `120.000 đ`, `150.000 đ`, `4 sân`.
- Xác nhận V6 Pro Shop (`menu.js`: giữ nguyên `onerror="...placeholder-default.svg"`, in-place DOM update) và QRCoder/ZXing không bị ảnh hưởng (`FIX-19`).

### Kiểm thử hồi quy tự động mới bổ sung

- `PickleballClub.Tests/SafeRemediationRegressionTests.cs` (3 integration tests mới nâng tổng số xUnit lên 38).
- Bộ script tạm kiểm thử ngoài repo trong artifact scratch (`js_unit_tests.mjs`, `e2e_safe_remediation_verify.mjs`, `run_e2e_isolated.ps1`) dùng SQL Express cô lập và dọn sạch sau khi chạy.
