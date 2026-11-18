# SAFE REMEDIATION PLAN — SPIKE PICKLEBALL CLUB

**Tài liệu:** Deliverable 1 — Master Prompt 01 (Safe Logic Remediation, Data Integrity & Zero-Regression Release Gate)  
**Workspace:** `e:\pickleball`  
**Branch khôi phục & thực thi:** `fix/safe-logic-remediation` (tạo từ `fix/v4-release-blockers-and-qr` @ `49ceb36`, bảo toàn nguyên vẹn toàn bộ thay đổi V7 UI & QA QR chưa commit; bản sao lưu diff nằm tại artifact scratch `pre_remediation_diff.patch`)  
**Target Stack:** ASP.NET Core 10.0 MVC (`net10.0`), EF Core 10.0.13, SQL Server Express (`.\SQLEXPRESS` / DB `PickleballClub`), Vanilla JS, QRCoder 1.6.0, ZXing.Net 0.16.9, SixLabors.ImageSharp 3.1.12.

---

## 1. Phạm vi & Nguyên tắc an toàn

- **KHÔNG** redesign giao diện, **KHÔNG** chuyển stack khỏi ASP.NET Core MVC + Razor + Vanilla JS.
- **KHÔNG** sửa database schema, không tạo/xóa migration, không seed/reset/drop database `PickleballClub`.
- **KHÔNG** sửa thuật toán tính tiền tách khung giờ peak-hour, sổ cái `TbPaymentTransaction`, cơ chế chống đặt trùng sân, hay bất kỳ tham số nào của mã QR (`SPK1.*`, HMAC-SHA256, ECC, vị trí trên vé, xuất SVG/PNG, giải mã ZXing).
- **KHÔNG** thay đổi logic phân quyền `Admin (RoleId=1)`, `Staff (RoleId=3)`, `Customer (RoleId=2)`.

---

## 2. Inventory toàn bộ hệ thống (Codebase Map)

### 2.1. Backend Services & Utilities
- `Services/BookingService.cs` — Chuẩn giờ Việt Nam (`GetVietnamNow`), kiểm tra giữ chỗ pending TTL (`IsPendingHoldActive`, `HOLD_TTL=15m`, `PENDING_CAP=2`), `CheckSlotConflictAsync`, `CreateBookingAsync`, `CreateCourtLockAsync`, `ApproveBookingAsync`, `CheckInBookingAsync`, `CancelBookingAsync` (cùng atomic restock POS), `ExtendBookingDurationAsync`, `MoveBookingCourtAsync`, `AddPosItemToBookingAsync`, `FinishBookingSessionAsync`, `RecordBookingPaymentAsync`, `RefundBookingPaymentAsync`, `ReconcileBookingLifecycleAsync`.
- `Services/OrderService.cs` — Đơn Pro Shop lẻ & gộp sân (`CreateOrderAsync`, `RecordStandaloneOrderPaymentAsync`, `RefundStandaloneOrderPaymentAsync`, `FulfillOrderAsync`, `CancelOrderAndRestockAsync`).
- `Services/PricingService.cs` — Tính giá tách segment Standard/Peak + áp hạng hội viên (`ResolveMemberTier`), truy vấn doanh thu ròng đã thu qua `TbPaymentTransaction`.
- `Services/ClubSettingsService.cs` — Đọc `TbCmsConfig` + băm `PricingVersion` dùng cho kiểm tra báo giá.
- `Services/QrTokenService.cs` — Token `SPK1.<payload>.<hmac>` + sinh QR SVG/PNG/Logo QR.
- `Services/IdempotencyService.cs` — Chống gửi lặp (`tb_IdempotencyRecord`, replay/409 conflict).
- `Services/ProductImageService.cs` — Validate & lưu ảnh sản phẩm vào `wwwroot/uploads/products/`.
- `Services/PasswordService.cs` — PBKDF2 + nâng cấp MD5 cũ.
- `Services/BookingLifecycleWorker.cs` — `BackgroundService` tự động giải phóng đơn quá hạn giữ chỗ / no-show.
- `Utilities/Function.cs` — Claims auth per-request qua `IHttpContextAccessor` (`IsLogin`, `IsAdmin`, `IsStaffOrAdmin`, `SecurityStamp`, `SignInAccountAsync`).

### 2.2. Controllers & Routes
- **Public MVC:**
  - `Controllers/HomeController.cs` (`/` — Landing)
  - `Controllers/BookingController.cs` (`/Booking` — Đặt sân)
  - `Controllers/ProductController.cs` (`/Product` — Pro Shop)
  - `Controllers/LookupController.cs` (`/Lookup` — Tra cứu vé & Vé QR)
  - `Controllers/AccountController.cs` (`/Account/Login`, `/Account/Register`, `/Account/Profile`, `/Account/UpdateProfile`, `/Account/ChangePassword`, `/Account/Logout`, `POST /api/auth/forgot-password`)
- **Admin/Staff Area (`[Area("Admin")]`):**
  - `Areas/Admin/Controllers/HomeController.cs` (`/Admin`, `/Management`, `/Checkin`, `/Reports`, `/Cms`)
  - `Areas/Admin/Controllers/BookingsController.cs` (`/Admin/Bookings`)
  - `Areas/Admin/Controllers/CourtsController.cs` (`/Admin/Courts` — Admin-only)
  - `Areas/Admin/Controllers/ProductsController.cs` (`/Admin/Products` — Admin-only)
  - `Areas/Admin/Controllers/AccountsController.cs` (`/Admin/Accounts` — Admin-only)
  - `Areas/Admin/Controllers/MenusController.cs` (`/Admin/Menus` — Admin-only)
  - `Areas/Admin/Controllers/RegisterController.cs` (`/Admin/Register` — Admin-only)
  - `Areas/Admin/Controllers/LoginController.cs` (`/Admin/Login`)
- **REST JSON API (`Controllers/ApiDbController.cs`):**
  - `/api/db/state`, `/api/security/csrf`, `/api/auth/*`
  - `/api/bookings/quote`, `/api/bookings`, `/api/bookings/lookup`, `/api/bookings/me`, `/api/bookings/{code}/qr.svg`, `/api/bookings/{code}/qr.png`
  - `/api/admin/bookings/{code}/{approve,reject,checkin,finish,extend,move,pos,payments,refunds,transactions}`, `/api/admin/bookings/approve-all` (check), `/api/admin/court-locks`
  - `/api/orders/create`, `/api/admin/orders`, `/api/admin/orders/{code}/{payments,refunds,fulfill,cancel}`
  - `/api/admin/reports/summary`, `/api/admin/cms`, `/api/admin/checkin`

### 2.3. Frontend Modules & Razor Views
- `wwwroot/js/`: `shared.js`, `booking.js`, `menu.js`, `management.js`, `reports.js`, `dashboard.js`, `checkin.js`, `lookup.js`
- `Views/`: `Home/Index.cshtml`, `Booking/Index.cshtml`, `Product/Index.cshtml`, `Lookup/Index.cshtml`, `Account/Login.cshtml`, `Account/Profile.cshtml`, `Shared/_Layout.cshtml`, `Shared/Components/Court/Default.cshtml`
- `Areas/Admin/Views/`: `Shared/_Layout.cshtml`, `Home/{Index,Management,Checkin,Reports,Cms}.cshtml`, `{Bookings,Courts,Products,Accounts,Menus,Login,Register}/*.cshtml`

### 2.4. Database Migrations (Đã xác minh đồng bộ với SQL Server `PickleballClub`)
1. `20261109010000_V2_BaselineAndSafeExpand`
2. `20261109010500_V2_LedgerTypedColumnsAndRowVersion`
3. `20261109123000_V4_HoldTtlIdempotencyAndOrderFulfillment`

---

## 3. Baseline Verification (Trước khi sửa)

| Kiểm tra | Lệnh | Kết quả Baseline |
|---|---|---|
| JS syntax check (8 files) | `node --check wwwroot/js/*.js` | **8/8 PASS** (0 syntax errors) |
| NuGet restore & Build | `dotnet restore PickleballClub.slnx && dotnet build PickleballClub.slnx -c Release` | **PASS** (0 Warning(s), 0 Error(s)) |
| Automated Test Suite | `dotnet test PickleballClub.Tests\PickleballClub.Tests.csproj -c Release` | **35/35 PASS**, 0 Failed, 0 Skipped (41s) |
| SQL Migrations Status | `SELECT MigrationId FROM __EFMigrationsHistory` | **3/3 đã áp dụng** |

---

## 4. Phân loại lỗi & Kế hoạch xử lý chi tiết (`FIX-01` .. `FIX-20` + Risk Audit)

### Bảng phân loại & hiện trạng sau kiểm tra thực tế

| Priority | ID | Hiện trạng thực tế qua audit code | Nguyên nhân gốc & Hành động khắc phục |
|---|---|---|---|
| **P0** | **FIX-01** | `DEFAULT_PRODUCTS = []` đã sạch nhưng `shared.js:6-47` vẫn còn `DEFAULT_COURTS` 4 sân mẫu tĩnh; `DEFAULT_CMS` có `courtPrice: 120000, peakSurcharge: 30000` dễ che lỗi khi API fail (`CMS_CONFIG = { ...DEFAULT_CMS, ... }`) | Xóa dữ liệu mẫu `DEFAULT_COURTS` khỏi runtime khi lỗi API (đặt `DEFAULT_COURTS = []`), gắn cờ `DB_SYNC_OK` / banner lỗi rõ ràng nếu `/api/db/state` thất bại thay vì hiển thị dữ liệu sân giả. |
| **P0** | **FIX-02** | `management.js:685` fallback `{ id: 1 }` nếu `COURTS` rỗng; chưa khóa nút `#btnCreateCourtLock` hoặc cảnh báo khi `COURTS.length === 0`. | Chặn submit khóa sân với thông báo lỗi rõ ràng khi `COURTS.length === 0`; bảo vệ các helper đọc `COURTS[0]` trên frontend. |
| **P0** | **FIX-04** | Backend `OrderService.FulfillOrderAsync` + `POST /api/admin/orders/{code}/fulfill` đã có, **nhưng `management.js:256-302` KHÔNG hiển thị trạng thái `fulfillmentStatus`, KHÔNG có nút giao hàng / hoàn tất đơn (`fulfill`)**, và chỉ lấy `standaloneOnly=true` nên đơn Pro Shop gộp vào sân không thấy nút giao đồ. | Hiển thị rõ badge `fulfillmentStatus` (`pending` / `delivered` / `cancelled`) cho cả đơn lẻ và đơn gộp sân, bổ sung nút "Đã giao đồ / Hoàn tất" gọi `POST /api/admin/orders/{code}/fulfill` (có xác nhận, khóa double-click, toast rõ và cập nhật ngay). |
| **P0** | **FIX-05** | `management.js:583-609` gọi `await apiFetch('/api/admin/bookings/.../finish')` **mà KHÔNG kiểm tra `finishRes.ok`**, nuốt lỗi im lặng và báo toast "Đã ghi sổ thu tiền!" dù ca chơi chưa đóng thành công. | Kiểm tra `finishRes.ok`: nếu thu tiền thành công nhưng `finish` thất bại, giữ modal mở (hoặc mở panel phục hồi), **chặn gửi lại bút toán thu tiền**, hiển thị thông báo rõ tiền đã vào sổ cái kèm nút **"Thử đóng ca lại (Finish)"** gọi riêng `/finish`. Khóa nút `#btnSubmitLedgerPay` chống double-click. |
| **P0** | **FIX-08** | Backend đã tăng `PricingVersion` khi CMS đổi giá (`PricingService`), nhưng `booking.js:108` `makeQuoteKey` KHÔNG đưa `PRICING_VERSION` vào key, nên nếu CMS đổi giá giữa lúc user đang chọn giờ, cache client vẫn tưởng quote khớp. | Thêm `PRICING_VERSION` vào `makeQuoteKey(...)` và đồng bộ lại quote tự động khi `pickleball:db-synced` báo `PRICING_VERSION` thay đổi; xử lý dứt điểm mã 409/STALE_PRICE khi submit. |
| **P0** | **FIX-13** | Backend dùng `BookingService.GetVietnamNow()` (`SE Asia Standard Time`), nhưng frontend (`shared.js:212-239, 432, 485`) dùng `new Date()` theo local timezone trình duyệt (gây lệch ngày `getTodayStr()`, lệch phút `getNowMinutes()`, sai `getPendingHoldRemainingLabel` khi client ở UTC / US / Japan). | Tính offset đồng bộ từ `data.serverTimeUtc` của `/api/db/state` (`SERVER_TIME_OFFSET_MS`), sau đó quy đổi tuyệt đối sang múi giờ `UTC+7` (`Asia/Ho_Chi_Minh`) bằng `Date.UTC` / `getUTC*()` trong `getVietnamNowDate()`, `getTodayStr()`, `getOffsetDateStr()`, `getNowMinutes()`, `getPendingHoldRemainingLabel()`. |
| **P0** | **FIX-20** | Backend `PricingService` hỗ trợ `PeakSurcharge = 0`, nhưng `shared.js:860` viết `Number(CMS_CONFIG.peakSurcharge) || 30000` biến `0` thành `30000`; `shared.js:811, 871`, `dashboard.js:49`, `booking.js:355` viết `Number(undefined) ?? 30000` trả ra `NaN`. | Viết helper chung `resolveNumericConfig(value, fallback)` xử lý an toàn `0`, chuỗi số `"0"`, `null`, `undefined`, `NaN`; áp dụng thống nhất tại `shared.js`, `booking.js`, `dashboard.js`, `Cms.cshtml`. |
| **P1** | **FIX-03** | `OrderService.cs:223-246` yêu cầu booking gộp đồ phải có `BookingDate == todayIso` (VN) và chưa kết thúc (`nowMins < bEnd`), nhưng `menu.js:311-319` cho phép chọn cả `b.date >= today` (tương lai) và booking đã kết thúc trong ngày -> bấm đặt là lỗi 400 từ server! | Đồng bộ điều kiện lọc `populateActiveBookingOptions()` trong `menu.js` khớp 100% với `OrderService`: `b.date === getTodayStr()`, `status === 'playing' || 'approved'`, và `getBookingTimingState(b).phase !== 'ended'`. Hiển thị chú thích giải thích khi có đơn tương lai/đã kết thúc không thể gộp. |
| **P1** | **FIX-06** | `reports.js` có banner lỗi `stale`, cần kiểm tra thêm khả năng treo/timeout khi API chậm hoặc mạng chập chờn và giữ thông tin khoảng ngày đang lỗi. | Bổ sung `AbortController` timeout hợp lý (15s) trong `reports.js`, vô hiệu hóa nút lọc khi đang tải, xử lý lỗi mạng/timeout/401/403/500 kèm nút thử lại. |
| **P1** | **FIX-07** | `reports.js:217` dùng `Math.max(0, Number(c.collectedRevenue) || 0)` biến số thu ròng âm (khi tiền hoàn > tiền thu trong kỳ) thành `0 đ` trên biểu đồ chi tiết theo sân. | Giữ nguyên giá trị có dấu `rawCollected = Number(c.collectedRevenue) || 0`: cột bar tối đa `0%` nếu âm, nhãn tiền hiển thị đúng dấu âm (`-50.000 đ`) kèm highlight cảnh báo "Thực thu âm". |
| **P1** | **FIX-09** | `booking.js` chỉ vô hiệu hóa slot quá giờ ở UI nhưng `submitBooking()` chưa tự chặn lại lần cuối nếu đồng hồ nhảy qua giờ cắt/đóng cửa trong lúc khách điền form. | Kiểm tra lại giới hạn ngày đặt, giờ mở/đóng cửa, và phút hiện tại VN (`getNowMinutes()`) ngay đầu `submitBooking()` trước khi gọi POST `/api/bookings`. |
| **P1** | **FIX-10** | `management.js:337-347` `approveAllPending()`: (1) duyệt cả đơn `pending` đã hết TTL giữ chỗ (gây lỗi 409 hàng loạt), (2) không có hộp thoại xác nhận, (3) không khóa nút chống double-click, (4) chỉ đếm `res.ok` mà không báo rõ số đơn trùng/hết hạn giữ chỗ. | Lọc đúng `b.status === 'pending' && isPendingBookingHoldActive(b)`, hỏi xác nhận kèm số lượng, khóa nút `#approveAllBtn` khi chạy, thống kê chính xác `approved / conflictOrExpired / failed`. |
| **P1** | **FIX-11** | `Management.cshtml:110` có `<option value="960">Cả ngày (16h)</option>` nhưng `#lockStart` mặc định `12:00` -> tổng `12:00 + 16h` vượt quá 24h & giờ đóng cửa; backend `BookingService.CreateCourtLockAsync` tự clamp xuống `1440` thay vì khớp giờ hoạt động CLB. | Khi chọn "Cả ngày" trên UI, tự động set `#lockStart = CMS_CONFIG.openTime`, tính `duration = closeMins - openMins` theo CMS thực tế, hiển thị khoảng giờ khóa rõ ràng và chặn trước khi gửi nếu `start + duration` vượt quá cuối ngày `24:00` (giữ nguyên contract API/Service cho ca khóa hợp lệ). |
| **P1** | **FIX-12** | `booking.js` đọc query param `?courtId=&date=&time=&duration=` khi init nhưng khi user đổi sân/ngày/giờ/thời lượng, URL không cập nhật lại -> F5 làm mất lựa chọn vừa thao tác. | Cập nhật đồng bộ URL query params bằng `window.history.replaceState` qua helper `syncBookingUrlState()` mỗi khi đổi sân/ngày/giờ/duration (bỏ qua nếu giá trị rỗng). |
| **P1** | **FIX-14** | `management.js:285` dùng `orders.slice(0, 25)` cắt cụt âm thầm mọi đơn từ thứ 26 trở đi; không có ô tìm kiếm, bộ lọc trạng thái thanh toán/giao hàng hay điều khiển "Xem thêm / Phân trang". | Bổ sung thanh tìm kiếm (mã đơn, khách hàng, SĐT, mã sân), filter trạng thái thanh toán & trạng thái giao đồ (`pending`/`delivered`), phân trang / nút "Tải thêm" minh bạch ("Đang hiển thị X / Y đơn"). |
| **P1** | **FIX-16** | `Views/Product/Index.cshtml:72` ghi nhãn `"Hình thức thanh toán / Gộp vào sân"` dù đây KHÔNG phải hình thức thanh toán (đơn lẻ là đơn thu tại quầy `UNPAID`, đơn gộp chỉ đẩy vào hóa đơn sân). | Đổi nhãn & mô tả ngắn thành `"Gắn vào ca sân đang chơi (hoặc đặt thanh toán tại quầy)"` — rõ ràng về nghiệp vụ thanh toán & đồng bộ với `menu.js`. |
| **P1** | **FIX-17** | `Views/Booking/Index.cshtml:201` cố định tiêu đề `"Đặt Sân Thành Công"` ngay cả khi booking của khách tạo ra với trạng thái `pending` (chờ lễ tân duyệt + đếm ngược giữ chỗ 15 phút). | Cập nhật tiêu đề, badge và thông điệp modal động trong `booking.js` dựa theo `created.status`: `pending` -> `"Đã Tiếp Nhận Yêu Cầu — Chờ Duyệt"`, chỉ hiển thị `"Đặt Sân Thành Công"` khi `approved`. |
| **P2** | **FIX-15** | `shared.js:1783-1787` click backdrop chỉ gỡ class `.open` (bỏ qua `closeModal()` nên không restore focus, không đặt `aria-hidden`); `Profile.cshtml:521-524` dùng `<span class="paddle-chip" onclick=...>`; `dashboard.js:476` dùng `<td onclick=...>`; `booking.js:459` dùng `<div class="timeline-seg" onclick=...>`. | Sửa click backdrop gọi `closeModal(overlay.id)`; chuyển `<span class="paddle-chip">` thành `<button type="button">`; chuyển `<div class="timeline-seg">` click được & `<td class="matrix-cell free">` hỗ trợ `role="button"`, `tabindex="0"`, `aria-label` và phím `Enter`/`Space` (giữ nguyên toàn bộ CSS layout/class). |
| **P2** | **FIX-18** | SSR Razor chứa chữ/số tĩnh: `Views/Home/Index.cshtml:149,161` (`120.000 đ`, `150.000 đ`), `Views/Booking/Index.cshtml:68` (`120.000 đ / giờ`), `Views/Shared/_Layout.cshtml:121,122` (`120.000 đ/h`, `150.000 đ/h`), `Views/Account/Login.cshtml:1271,1288` (`4 sân`), `Areas/Admin/Views/Home/Cms.cshtml:124`. | `@inject ClubSettingsService` và `@inject PickleballContext` (chỉ đọc nhẹ) trong các Razor Views công khai để render giá mở cửa/giờ vàng & tổng số sân đang hoạt động trực tiếp từ SQL Server ngay lúc SSR, xóa con số cứng lệch với DB. |
| **P2** | **FIX-19** | Kiểm tra Pro Shop V6 & QR (`wwwroot/uploads/products`, `TbProduct.Image`, animation giỏ hàng, `QrTokenService`, SVG/PNG 512/1024). | Đảm bảo không làm hỏng V6 Pro Shop & QR: bổ sung fallback `onerror` sạch cho ảnh sản phẩm nếu file bị thiếu + xác minh lại bằng bộ test Pro Shop & QR benchmark/decode. |

### 4.1. Risk Audit Checkpoints (Xác minh liên tục)
- **A. Database / Migration / Concurrency**: Không thêm cột, kiểm tra `RowVersion`, SQL transaction `Serializable`/`RepeatableRead` và `sp_getapplock`/idempotency.
- **B. Auth / RBAC / Session / CSRF / IDOR**: Kiểm tra `[Authorize(Roles = "Admin")]` vs `[Authorize(Roles = "Admin,Staff")]`, `[ValidateAntiForgeryToken]`, `SecurityStamp`, `LookupBookings` (chặn tra cứu bằng SĐT ẩn danh), `CallerCanAccessBookingTicket`.
- **C. Booking Lifecycle**: Pending TTL 15 phút, cap 2 đơn pending/actor, chống trùng `CheckSlotConflictAsync`, Check-in chỉ trước ≤ 30p và chưa kết thúc, không extend đè ca kế tiếp hoặc qua giờ đóng cửa.
- **D. Finance & Pro Shop**: Atomic stock (`UPDATE WHERE UnitInStock >= qty` hoặc locked row), restock khi cancel, ledger payment/refund không cho hoàn vượt net paid, không chỉnh sửa đơn giá phía client.
- **E. QR Protected Component**: Không sửa `QrTokenService.cs`, `GenerateQrSvg`, `GenerateQrPngBytes`, giữ nguyên vùng `#lookupDigitalTicketHost` & `#bookingQrCodeWrap`.

---

## 5. Ma trận ảnh hưởng (Dependency & Impact Analysis)

| Batch | Files dự kiến sửa | Views / Endpoints liên quan | Rủi ro hồi quy & Cách phòng tránh |
|---|---|---|---|
| **Batch A: Time & Pricing Core** (`FIX-01`, `FIX-02`, `FIX-13`, `FIX-20`) | `wwwroot/js/shared.js`, `wwwroot/js/booking.js`, `wwwroot/js/dashboard.js`, `wwwroot/js/management.js`, `Areas/Admin/Views/Home/Cms.cshtml` | Tất cả trang public & admin; `/api/db/state` (đã trả sẵn `serverTimeUtc`) | Toàn bộ hàm tính giờ VN (`getTodayStr`, `getOffsetDateStr`, `getNowMinutes`, `getPendingHoldRemainingLabel`) dùng chung qua `shared.js`. Chạy Node unit tests dưới `TZ=UTC`, `TZ=America/New_York`, `TZ=Asia/Tokyo`, `TZ=Asia/Ho_Chi_Minh`. |
| **Batch B: Operations, Ledger, Pro Shop Dispatch & Locks** (`FIX-04`, `FIX-05`, `FIX-10`, `FIX-11`, `FIX-14`) | `wwwroot/js/management.js`, `Areas/Admin/Views/Home/Management.cshtml` | `/Management`, `POST /api/admin/orders/{code}/fulfill`, `POST /api/admin/bookings/{code}/payments`, `/finish`, `/approve`, `POST /api/admin/court-locks` | Không đổi signature API backend. UI modal thu tiền tách rõ phase 1 (thu tiền) và phase 2 (đóng ca) để tuyệt đối không thu tiền lặp 2 lần nếu phase 2 lỗi. |
| **Batch C: Booking, Pro Shop & Reports Truthfulness** (`FIX-03`, `FIX-06`, `FIX-07`, `FIX-08`, `FIX-09`, `FIX-12`, `FIX-16`, `FIX-17`) | `wwwroot/js/menu.js`, `Views/Product/Index.cshtml`, `wwwroot/js/reports.js`, `wwwroot/js/booking.js`, `Views/Booking/Index.cshtml` | `/Product`, `/Booking`, `/Reports`, `/api/orders/create`, `/api/bookings/quote`, `/api/bookings`, `/api/admin/reports/summary` | Giữ nguyên quote flow & QR rendering trong `#bookingQrCodeWrap`. Kiểm tra `reports.js` vẫn giữ cấu trúc chart DOM và hiển thị âm chuẩn. |
| **Batch D: Accessibility & SSR Truthfulness** (`FIX-15`, `FIX-18`, `FIX-19`) | `wwwroot/js/shared.js`, `wwwroot/js/booking.js`, `wwwroot/js/dashboard.js`, `Views/Account/Profile.cshtml`, `Views/Account/Login.cshtml`, `Views/Home/Index.cshtml`, `Views/Booking/Index.cshtml`, `Views/Shared/_Layout.cshtml`, `Areas/Admin/Views/Home/Cms.cshtml` | Toàn bộ modal overlay, bảng điều hướng bàn phím, SSR HTML | Dùng `@inject ClubSettingsService` & query đọc nhẹ `TbCourts.CountAsync(c => c.IsActive)`; giữ nguyên toàn bộ ID DOM và selector Playwright hiện có. |

---

## 6. Kế hoạch kiểm thử hồi quy (Regression Test Plan)

1. **xUnit Integration & Unit Tests (`PickleballClub.Tests/`):**
   - Toàn bộ 35 tests hiện tại phải PASS 100% không skip/relax.
   - Bổ sung tests mới: `PeakSurcharge=0` pricing & API quote/CMS roundtrip, `OrderService` từ chối gộp vào booking ngày mai / đã kết thúc & cho phép booking đang chơi hôm nay, `FulfillOrderAsync` cho cả đơn lẻ lẫn đơn gộp sân + chặn 409 khi đơn đã hủy, `CreateCourtLockAsync` kiểm tra biên cuối ngày & không ghi đè booking trùng.
2. **Node.js Deterministic Unit Tests (`scratch/js_unit_tests.mjs`):**
   - Nạp logic thời gian VN & pricing từ `shared.js`, chạy độc lập dưới 3 timezone (`UTC`, `America/New_York`, `Asia/Tokyo`, `Asia/Ho_Chi_Minh`) với các cấu hình `peakSurcharge` = `0`, `'0'`, `null`, `undefined`, `45000`.
3. **Playwright Full E2E & Cross-Viewport Verification (Chrome thực trên app chạy thật):**
   - **16 luồng nghiệp vụ đầy đủ:** Landing -> Booking (Quote + Create + Conflict) -> Pending Hold TTL -> Approve / Approve All -> QR Download SVG/PNG & Check-in -> POS / Pro Shop (ảnh thật, gộp sân hợp lệ, chặn gộp ngày mai/hết giờ, giao đồ `fulfill`) -> Ledger thu tiền + Finish (bao gồm mô phỏng lỗi Finish sau khi thu tiền để verify không double-charge) -> Refund + Reports âm -> Court Lock cả ngày -> CMS đổi giá (tăng `PricingVersion` & vô hiệu quote cũ) -> Profile + Auth/RBAC Admin/Staff/Customer/Guest -> Lookup bảo mật.
   - **Ma trận bất thường & lỗi:** 400 / 401 / 403 / 404 / 409 / 429 / 500, offline/timeout báo cáo, double-click, chuyển nhanh tab/bộ lọc, F5 giữa chừng, XSS payload an toàn.
   - **Responsive & Accessibility:** `375px`, `768px`, `1024px`, `1440px`, không overflow ngang, `console.error = 0`, `pageerror = 0`, QR rõ nét có thể quét lại bằng endpoint `/api/admin/checkin`.
   - **Cleanup DB thực:** Kiểm tra và xóa sạch mọi bản ghi test phát sinh sau khi chạy E2E trên app thật.

---

## 7. Kế hoạch Rollback & Tiêu chí Release Gate

- **Rollback tức thời:** Vì toàn bộ chỉnh sửa nằm trên branch riêng `fix/safe-logic-remediation`, có thể khôi phục nguyên trạng bằng `git checkout -- <file>` cho từng tệp hoặc quay về snapshot trước sửa. Chi tiết từng file được ghi nhận tại `docs/SAFE_FIX_ROLLBACK.md`.
- **Release Gate (GO / CONDITIONAL GO / NO-GO):**
  - Toàn bộ P0 (`FIX-01`, `FIX-02`, `FIX-04`, `FIX-05`, `FIX-08`, `FIX-13`, `FIX-20`) = **PASS**.
  - Không phát sinh regression trên Auth/RBAC/CSRF/IDOR, Pricing, Ledger, Inventory, QR.
  - `dotnet build -c Release` (0 error), `dotnet test -c Release` (100% PASS), Node timezone tests (100% PASS), Playwright E2E 4 viewports (0 console/page errors).
