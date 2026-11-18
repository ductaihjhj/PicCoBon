# SAFE FIX RESULTS — SPIKE PICKLEBALL CLUB

Báo cáo tổng kết kết quả kiểm tra, sửa lỗi và bằng chứng đối chiếu cho toàn bộ 20 hạng mục (`FIX-01` .. `FIX-20`), Risk Audit bảo mật/dữ liệu và 3 lỗi nghiêm trọng phát hiện thêm trong quá trình chạy E2E (`FIX-R1` .. `FIX-R3`).

---

## 1. Bảng kết quả tổng hợp (`FIX-01` .. `FIX-20` + Phát hiện bổ sung)

| ID | Root cause | Files changed | Tests | Status | Remaining risk |
|---|---|---|---|---|---|
| **FIX-01** | `shared.js` vẫn giữ mảng 4 sân mẫu tĩnh `DEFAULT_COURTS` và fallback `CMS_CONFIG` dễ che giấu lỗi API | `wwwroot/js/shared.js` | `js_unit_tests.mjs` (`DEFAULT_COURTS.length === 0`); Playwright Home/Booking/Management | **PASS** | Phụ thuộc kết nối `/api/db/state`; khi lỗi hiển thị banner trạng thái rỗng/lỗi |
| **FIX-02** | `management.js` fallback `{ id: 1 }` nếu `COURTS` rỗng khi khóa sân; chưa khóa nút `#btnCreateCourtLock` | `wwwroot/js/management.js`, `Areas/Admin/Views/Home/Management.cshtml` | Chrome E2E `Management` kiểm tra đồng bộ sân khóa từ `COURTS` | **PASS** | Rất thấp (chặn ngay phía client nếu danh sách sân rỗng) |
| **FIX-03** | `menu.js` `populateActiveBookingOptions()` cho chọn cả đơn ngày tương lai (`b.date >= today`) và đơn đã hết giờ, gây lỗi 409 khi gửi lên `OrderService` | `wwwroot/js/menu.js`, `Views/Product/Index.cshtml` | `js_unit_tests.mjs` (4 TZ); `SafeRemediationRegressionTests.Fix03_04`; Playwright `/Product` | **PASS** | Khách dùng nhập tay mã đặt sân vãng lai vẫn được kiểm tra bởi `OrderService` phía máy chủ (409) |
| **FIX-04** | Backend đã có `POST /api/admin/orders/{code}/fulfill` nhưng `Management` chưa hiển thị `fulfillmentStatus`, thiếu nút giao đồ và bỏ sót đơn gộp sân | `Areas/Admin/Views/Home/Management.cshtml`, `wwwroot/js/management.js` | `SafeRemediationRegressionTests.Fix03_04` (200 fulfill & 409 cancelled); Playwright `/Management` | **PASS** | `GET /api/admin/orders` giới hạn `Take(200)` gần nhất ở backend (giữ nguyên contract API) |
| **FIX-05** | `submitLedgerPayment()` không kiểm tra `finishRes.ok`, dễ nuốt lỗi đóng ca hoặc khiến thu ngân bấm thu lại gây rủi ro thao tác | `wwwroot/js/management.js`, `Areas/Admin/Views/Home/Management.cshtml` | Playwright intercept `/finish` trả `500` sau `/payments` -> bấm `#btnRetryLedgerFinishOnly` (`paymentCalls === 1`, `finishCalls === 2`) | **PASS** | Rất thấp (`IdempotencyKey` phía backend cũng bảo vệ chống ghi trùng) |
| **FIX-06** | `reports.js` thiếu `AbortController` timeout khi mạng treo và cần banner `stale`/retry giữ ngữ cảnh | `wwwroot/js/reports.js` | Playwright `/Reports` `route.abort()` hiện `[data-testid="ui-state-stale"]` | **PASS** | Timeout đặt 15 giây cho báo cáo sổ cái lớn |
| **FIX-07** | `renderReportCourts()` kẹp `Math.max(0, Number(c.collectedRevenue))`, che giấu số thu ròng âm khi hoàn tiền > thu thành `0 đ` | `wwwroot/js/reports.js` | `js_unit_tests.mjs`; `SafeRemediationRegressionTests.Fix07`; Playwright `/Reports` (`data-value="-100000"`) | **PASS** | Chiều cao cột âm giữ ở mức `0%` track dương kèm chữ đỏ & nhãn âm |
| **FIX-08** | `makeQuoteKey()` không chứa `PRICING_VERSION`; khi CMS đổi giá, cache quote client vẫn khớp key cũ | `wwwroot/js/booking.js` | `js_unit_tests.mjs`; `SafeRemediationRegressionTests.Fix08_18_20`; Chrome `/Booking` & `/Cms` | **PASS** | Nếu người dùng giữ tab rất lâu chưa sync lại, backend vẫn chặn bằng `PRICING_STALE` (409) |
| **FIX-09** | `submitBooking()` chưa kiểm tra lại giờ qua lượt hôm nay (`UTC+7`) & biên mở/đóng cửa ngay trước lúc POST | `wwwroot/js/booking.js` | Kiểm tra tĩnh `node --check` & logic giới hạn trong `booking.js`; `/Booking` E2E | **PASS** | `BookingService.CreateBookingAsync` vẫn kiểm tra lại phía máy chủ |
| **FIX-10** | `approveAllPending()` duyệt cả đơn quá hạn hold TTL, thiếu confirm, thiếu khóa nút và không thống kê lỗi | `wwwroot/js/management.js` | Playwright `Management` (`PB-EXPHOLD` bị loại, dismiss confirm=0 đơn duyệt, accept confirm duyệt đúng đơn hợp lệ) | **PASS** | Thao tác duyệt tuần tự từng HTTP POST cho từng đơn |
| **FIX-11** | `#lockDuration` có `<option value="960">` cố định khi `#lockStart = 12:00` (vượt `closeTime`); cần gắn `full_day` đồng bộ giờ mở/đóng cửa CLB | `Areas/Admin/Views/Home/Management.cshtml`, `wwwroot/js/management.js` | Playwright `/Management` chọn `full_day` -> `#lockStart='06:00'` & khóa `06:00→22:00` thành công | **PASS** | Không làm thay đổi backend `CreateCourtLockAsync` |
| **FIX-12** | Đổi sân/ngày/giờ/thời lượng trên `/Booking` không đồng bộ query URL -> F5 mất lựa chọn | `wwwroot/js/booking.js` | Playwright `/Booking` click `duration=90` kiểm tra URL cập nhật `?duration=90` | **PASS** | Chạy an toàn với `history.replaceState` trong try/catch |
| **FIX-13** | JS dùng `new Date()` theo múi giờ cục bộ trình duyệt gây lệch ngày/phút giữa trình duyệt `UTC`/`US` và máy chủ `Asia/Ho_Chi_Minh` | `wwwroot/js/shared.js`, `wwwroot/js/reports.js` | `js_unit_tests.mjs` trên 4 TZ (`UTC`, `America/New_York`, `Asia/Tokyo`, `Asia/Ho_Chi_Minh`); Chrome TZ `America/New_York` khớp ngày VN | **PASS** | `SERVER_TIME_OFFSET_MS` tự đồng bộ từ `serverTimeUtc` mỗi lần sync |
| **FIX-14** | `management.js` cắt cứng `orders.slice(0, 25)` làm mất mọi đơn từ thứ 26, thiếu tìm kiếm & bộ lọc | `Areas/Admin/Views/Home/Management.cshtml`, `wwwroot/js/management.js` | Playwright với 28 đơn: tìm kiếm/lọc & bấm `#btnLoadMoreShopOrders` hiển thị đủ >=28 đơn | **PASS** | Tối đa 200 đơn gần nhất từ `/api/admin/orders` |
| **FIX-15** | Click backdrop modal bỏ qua `closeModal()` (thiếu `aria-hidden` & trả focus); `.paddle-chip` dùng `<span>`; `.timeline-seg` thiếu hỗ trợ bàn phím | `wwwroot/js/shared.js`, `wwwroot/js/booking.js`, `Views/Account/Profile.cshtml` | Chrome kiểm tra backdrop `#modalQR` đặt `aria-hidden="true"` và `.paddle-chip` là `<button>` nhấn `Enter` cập nhật vợt | **PASS** | Các view ít tương tác khác giữ nguyên theo cấu trúc hiện hữu |
| **FIX-16** | Nhãn `#shopOrderBooking` ghi sai nghiệp vụ `"Hình thức thanh toán / Gộp vào sân"` | `Views/Product/Index.cshtml` | Playwright `/Product` xác minh nhãn mới rõ ràng | **PASS** | Không |
| **FIX-17** | Tiêu đề & toast đặt sân luôn ghi `"Đặt Sân Thành Công"` kể cả khi đơn đang `pending` chờ duyệt | `Views/Booking/Index.cshtml`, `wwwroot/js/booking.js` | Chrome tạo đơn `pending` kiểm tra `#bookingModalTitle` ghi rõ `"Chờ Duyệt"` & đếm ngược TTL | **PASS** | Không |
| **FIX-18** | Nhiều Razor View chứa giá trị tĩnh (`120.000 đ`, `150.000 đ`, `4 sân`) trên SSR trước khi JS hydrate | `Views/Shared/_Layout.cshtml`, `Views/Home/Index.cshtml`, `Views/Booking/Index.cshtml`, `Views/Account/Login.cshtml`, `Areas/Admin/Views/Home/Cms.cshtml`, `wwwroot/js/dashboard.js` | `SafeRemediationRegressionTests.Fix08_18_20` + Chrome `/`, `/Cms` sau khi đổi `peakSurcharge=0` | **PASS** | Yêu cầu DB kết nối được khi render SSR Razor |
| **FIX-19** | Bảo toàn toàn bộ tính năng Pro Shop V6 (ảnh thật, `placeholder-default.svg` fallback, animation) và QR | Không phá vỡ module V6/QR | `ProShopV6ImageAndApiTests`, `V4ReleaseBlockersAndQrTests` (`105/105` QR decode), Chrome `/Product` fallback & `/Checkin` ZXing PNG decode | **PASS** | Không |
| **FIX-20** | `shared.js`, `dashboard.js`, `booking.js`, `Cms.cshtml` dùng `\|\| 30000` (biến `0` thành `30000`) hoặc `Number(undefined) ?? 30000` (ra `NaN`) | `wwwroot/js/shared.js`, `wwwroot/js/booking.js`, `wwwroot/js/dashboard.js`, `Areas/Admin/Views/Home/Cms.cshtml` | `js_unit_tests.mjs` (`0`, `'0'`, `undefined`, `null`); `SafeRemediationRegressionTests.Fix08_18_20`; Chrome `/Cms` + `/` | **PASS** | Không |
| **FIX-R1** *(Phát hiện mới)* | `booking.js` đọc sai tên trường từ `BookingQuoteBreakdown` (`standardRate`/`peakRate`/`normalMinutes`/`courtFee` thay vì `standardHourlyRate`/`peakHourlyRate`/`standardMinutes`/`courtFeeBeforeDiscount`) khiến bảng tóm tắt nhảy về `0 đ / giờ` sau quote | `wwwroot/js/booking.js` (`normalizeServerQuotePayload`) | `js_unit_tests.mjs`; Playwright `/Booking` xác nhận badge & tổng tiền hiển thị đúng sau báo giá máy chủ | **PASS** | Không |
| **FIX-R2** *(Phát hiện mới)* | `menu.js` `submitShopOrder()` gọi route không tồn tại `/api/orders` (404), gửi `deliveryNote`/`quantity` sai tên trường `address`/`qty`, và gọi hàm chưa định nghĩa `isValidVietnamPhone()` | `wwwroot/js/menu.js`, `wwwroot/js/shared.js` (`isValidVietnamPhone`) | `js_unit_tests.mjs`; Playwright `/Product` tạo đơn thật qua `POST /api/orders/create` trả `200 OK` | **PASS** | Đã tái sử dụng helper SĐT thống nhất với `booking.js` |
| **FIX-R3** *(Phát hiện mới)* | `shared.js` `saveCmsConfig()` gửi nguyên `CMS_CONFIG` (chứa `pricingVersion`, `openHours`, `address`) khiến backend `AllowedCmsKeys` từ chối mọi thao tác Lưu/Reset CMS bằng HTTP 400 | `wwwroot/js/shared.js` (`WRITABLE_CMS_KEYS`) | Playwright `/Cms` lưu `peakSurcharge=0` thành công `200 OK` và phản ánh ngay tại `/` | **PASS** | Không |

---

## 2. Kết quả kiểm tra cuối cùng (sau lần sửa code cuối)

- `node --check wwwroot/js/*.js`: **8/8 PASS**
- `js_unit_tests.mjs` đa múi giờ (`UTC`, `America/New_York`, `Asia/Tokyo`, `Asia/Ho_Chi_Minh`): **4/4 PASS**
- `dotnet build PickleballClub.slnx -c Release`: **0 Warning(s), 0 Error(s)**
- `dotnet test PickleballClub.Tests\PickleballClub.Tests.csproj -c Release`: **38/38 PASS, 0 Failed, 0 Skipped** (bao gồm QR Benchmark **105/105 PASS**)
- Playwright Chrome E2E (`run_e2e_isolated.ps1`): **PASS** toàn bộ 16 luồng, `375/768/1024/1440px`, `0` console errors, `0` page errors, `PickleballClub` DB thực nguyên vẹn 100%.

---

## 3. Các hạng mục phụ thuộc tích hợp ngoài (`BLOCKED_EXTERNAL` — giữ nguyên từ V4)

Các tính năng hạ tầng ngoài nằm ngoài phạm vi sửa lỗi an toàn nội bộ (được hiển thị trung thực 503 / thông báo quầy lễ tân thay vì giả lập):
- `P1-02`: Cổng SMS/Email OTP tự động cho Quên mật khẩu (`/api/auth/forgot-password` trả `503 OTP_PROVIDER_NOT_CONFIGURED`).
- `P2-06`: Đăng nhập OAuth Google / Zalo (cần ClientId/Secret nhà cung cấp).
- `P2-07`: Webhook chuyển khoản ngân hàng / VietQR tự động (cần merchant credentials thật).
- `V4-QR-MOBILE`: Đã vượt benchmark giả lập hạ độ phân giải/nén (`220px`, JPEG Q80) đạt 105/105; khuyến nghị kiểm tra quét camera vật lý trên điện thoại thực tế khi triển khai hiện trường.
