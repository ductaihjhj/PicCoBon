# BẰNG CHỨNG KIỂM THỬ THỰC TẾ — SPIKE PICKLEBALL CLUB (MASTER PROMPT V2)

- **Thời điểm thực thi kiểm thử cuối**: `2026-11-09T01:38:45+07:00`
- **Nhánh Git**: `fix/remediation-v2`
- **Môi trường thực thi**:
  - **OS**: Windows (PowerShell)
  - **.NET SDK / Runtime**: `.NET 10.0.402` (`net10.0`, xUnit.net `2.8.2` trên `.NET 10.0.13` 64-bit)
  - **Cơ sở dữ liệu**: Microsoft SQL Server 2022 Express (`.\SQLEXPRESS`, Windows Integrated Auth, `READ_COMMITTED_SNAPSHOT` + `UPDLOCK/HOLDLOCK/ROWLOCK` & `Serializable`)
  - **Node.js**: Installed (`node --check` kiểm tra cú pháp toàn bộ JS)
  - **Trình duyệt tự động**: Google Chrome Headless qua Chrome DevTools Protocol (CDP WebSocket)

---

## 1. Kiểm Tra Biên Dịch & Cú Pháp Tĩnh (`Build & Static Gate`)

### 1.1 Biên dịch Release toàn dự án & bộ kiểm thử
- **Lệnh chạy**:
  ```powershell
  dotnet build "e:\pickleball-ui\PickleballClub.csproj" -c Release
  dotnet build "e:\pickleball-ui\PickleballClub.Tests\PickleballClub.Tests.csproj" -c Release
  ```
- **Kết quả thực tế**: `PASS` — **`0 Warning(s), 0 Error(s)`**.

### 1.2 Kiểm tra cú pháp JavaScript (`wwwroot/js/*.js`)
- **Lệnh chạy**:
  ```powershell
  Get-ChildItem "e:\pickleball-ui\wwwroot\js\*.js" | ForEach-Object { node --check $_.FullName }
  ```
- **Kết quả thực tế**: `PASS` (`8/8` file hợp lệ: `booking.js`, `checkin.js`, `dashboard.js`, `lookup.js`, `management.js`, `menu.js`, `reports.js`, `shared.js`).

### 1.3 Kiểm tra làm sạch chuỗi nhạy cảm / quảng cáo chưa kiểm chứng / OAuth & OTP giả
- **Lệnh chạy**:
  ```powershell
  Select-String -Path "Views\**\*.cshtml","Areas\**\*.cshtml","wwwroot\js\*.js" -Pattern "handleSocialLogin|1,250|1\.250|USAPA|pb_auth_user|Đã gửi mã OTP"
  ```
- **Kết quả thực tế**: `PASS` — Không còn bất kỳ nút OAuth giả, toast OTP giả, tuyên bố `"1,250+"` hay `"USAPA"` nào trên giao diện (chỉ còn lệnh dọn `localStorage.removeItem('pb_auth_user')` và bộ lọc `.replace(/USAPA\s*/gi, '')` làm sạch dữ liệu cũ trong DB).

---

## 2. Bộ Kiểm Thử Tự Động xUnit (`19/19 PASS` Trên SQL Server Thật & HTTP Pipeline)

- **Lệnh chạy**:
  ```powershell
  dotnet test "e:\pickleball-ui\PickleballClub.Tests\PickleballClub.Tests.csproj" -c Release --logger "console;verbosity=normal"
  ```
- **Tổng kết kết quả**:
  ```text
  Test Run Successful.
  Total tests: 19
       Passed: 19
       Failed: 0
      Skipped: 0
   Total time: 14.1024 Seconds
  ```

### Bảng Chi Tiết 19 Bài Kiểm Thử Đã Chạy

| STT | Bộ Test & Tên Test Thực Tế | Nhóm Kiểm Chứng (Audit IDs) | Kỳ Vọng (Expected) | Kết Quả Thực Tế (Actual) | Trạng Thái |
|---|---|---|---|---|---|
| 1 | `UnitAndQrVerificationTests.AuthIsolation_ForgedCookieIsIgnored_And_RequestsAreIsolated` | **A. Security** (`P0-01`, `P0-02`) | Cookie giả `pb_admin_uid=1` không cấp quyền; 2 `HttpContext` song song cô lập tuyệt đối `ClaimsPrincipal`. | Cookie giả trả `IsAuthenticated() == false`; 2 context độc lập hoàn toàn (`1 ms`). | **PASS** |
| 2 | `UnitAndQrVerificationTests.PasswordService_Pbkdf2AndLegacyMd5Upgrade_WorkCorrectly` | **A. Security** (`P0-09`) | Hash mới chuẩn PBKDF2 (`AQAAAA...`), tự động phát hiện và nâng cấp hash MD5 cũ khi đăng nhập đúng. | Verify PBKDF2 đúng, phát hiện `needsUpgrade=true` với MD5 legacy và từ chối sai mật khẩu (`192 ms`). | **PASS** |
| 3 | `UnitAndQrVerificationTests.QrTokenService_ProductionMissingOrShortKey_FailsFast_DevAllowsEphemeral` | **A. Security** (`P0-09`) | Môi trường `Production` thiếu key, key `< 32` ký tự hoặc chứa `CHANGE_ME`/`DevOnly` phải ném `InvalidOperationException` ngay khi khởi tạo. | `Production` fail-fast đúng thiết kế; `Development` sinh khóa tạm trong RAM an toàn (`< 1 ms`). | **PASS** |
| 4 | `UnitAndQrVerificationTests.QrTokenService_GeneratePng_And_IndependentZXingDecode_ExactRoundtrip_TamperAndExpiryRejected` | **D. QR / Camera** (`P1-01`) | Ảnh PNG sinh từ `QRCoder` phải được giải mã độc lập bằng `ZXing.Net` + `ImageSharp` ra đúng chuỗi `SPK1...`; sửa 1 ký tự hoặc hết hạn bị từ chối. | `ZXing.Net` đọc chính xác `100%` chuỗi token gốc; token sửa chữ ký / hết TTL trả `Valid == false` (`14 ms`). | **PASS** |
| 5 | `UnitAndQrVerificationTests.GuestAccessToken_Sha256ConstantTimeVerify_RejectsWrongOrEmpty` | **A. Security** (`P0-07`, `P1-12`) | Mã bí mật `bk_...` băm SHA-256 kiểm tra hằng số thời gian (`FixedTimeEquals`), từ chối mã sai/rỗng. | Khớp đúng hash gốc, từ chối mọi biến thể sai (`< 1 ms`). | **PASS** |
| 6 | `UnitAndQrVerificationTests.IconSanitizer_BlocksXssPayloads_AllowsWhitelistedIcons` | **A. XSS** (`P0-12`) | Chặn `<script>`, `<img onerror=...>`, attribute injection; chỉ cho phép tên `iconify-icon` hợp lệ hoặc emoji an toàn. | Mọi payload XSS bị trung hòa về icon mặc định an toàn (`4 ms`). | **PASS** |
| 7 | `UnitAndQrVerificationTests.PricingService_SegmentedCrossBoundary_UnauthenticatedZeroDiscount_VerifiedAccountGetsTierDiscount` | **B. Pricing / VIP** (`P1-08`, `P1-16`, `P1-18`) | Ca `16:30–18:00` tách chuẩn `30p` thường + `60p` vàng; khách tự khai SĐT VIP hưởng `0%`; tài khoản VIP có doanh thu thực thu trong `TbPaymentTransaction` hưởng `15%`. | Tách đoạn chính xác từng phút; SĐT chưa xác thực `DiscountPercent = 0`; hội viên xác thực theo net ledger hưởng đúng hạng thẻ (`379 ms`). | **PASS** |
| 8 | `SqlServerConcurrencyAndMigrationTests.Migrations_ApplyCleanlyOnEmptyDb_And_UpgradeLegacyPreV2SchemaWithoutDataLoss` | **E. DB Migration** (`P2-01`, `P2-02`) | Chạy `Database.MigrateAsync()` trên DB trống và DB legacy trước V2: nâng cấp thêm `RowVersion`, `Decimal`, `DateOnly/TimeOnly`, `SecurityStamp`, trigger bất biến mà không mất bản ghi cũ. | Cả 2 kịch bản DB SQL Server thật đều migrate sạch, giữ nguyên `100%` dữ liệu legacy (`1 s`). | **PASS** |
| 9 | `SqlServerConcurrencyAndMigrationTests.ParallelBookings_SameCourtOverlappingTime_ExactlyOneSucceeds_OthersGet409` | **B. SQL Concurrency** (`P0-08`, `P0-11`) | `10` luồng `Task.WhenAll` với `PickleballContext` riêng đặt trùng sân & khung giờ trên SQL Server: đúng `1` đơn thành công, `9` luồng bị chặn `409`. | Đúng `1` commit thành công, `9` luồng nhận lỗi xung đột lịch (`409`), DB chỉ có duy nhất `1` vé active (`1 s`). | **PASS** |
| 10 | `SqlServerConcurrencyAndMigrationTests.ParallelBookingVsLock_And_ParallelExtendVsAdjacentBooking_MutualExclusionHolds` | **B. SQL Concurrency** (`P0-08`, `P1-10`, `P1-11`) | Đua song song giữa Đặt sân vs Khóa sân bảo trì, và Gia hạn `+30p` POS vs Đặt sân ca kế tiếp trên SQL Server: loại trừ tương hỗ tuyệt đối. | Chỉ tối đa `1` bên thắng ở mỗi cặp xung đột, không bao giờ tồn tại vừa vé active vừa khóa sân hoặc lấn giờ ca sau (`728 ms`). | **PASS** |
| 11 | `SqlServerConcurrencyAndMigrationTests.ParallelProShopOrders_LastStockItems_NeverNegative_IdempotencyAndCancelRestockWork` | **C. Shop / Stock** (`P0-10`, `P1-14`, `P1-15`) | Kho còn `3` sản phẩm, `10` người mua song song trên SQL Server: tối đa `3` đơn thành công, `7` đơn `409`, không deadlock, `UnitInStock == 0` (không bao giờ âm); gửi lặp `IdempotencyKey` trả lại đúng đơn cũ; hủy đơn hoàn kho `+1`. | `3` thắng, `7` bị từ chối `409`, `UnitInStock == 0`, retry idempotent không trừ kho lần 2, hủy đơn hoàn kho lên `1` và ghi `TbInventoryMovement` (`1 s`). | **PASS** |
| 12 | `SqlServerConcurrencyAndMigrationTests.PaymentLedger_SeparateFinishVsPartialCollectVsRefund_TransferRefRequired_And_LifecycleReconcile` | **C. Ledger / Money** (`P1-04`, `P1-05`, `P1-07`) | Kết thúc ca chơi chưa thu tiền giữ `PaidAmount == 0` (`unpaid`); chuyển khoản thiếu `ReferenceCode` bị chặn; thu từng phần + thu đủ + hoàn tiền ghi bút toán đối ứng `refund`; ca quá hạn chuyển `no_show` không tự cộng tiền. | `FinishBookingSessionAsync` giữ `PaidAmount = 0`; thu `100k` + `140k` - hoàn `40k` => thực thu ròng `200k` chuẩn sổ cái; trigger SQL chặn sửa/xóa lịch sử (`990 ms`). | **PASS** |
| 13 | `HttpSecurityAndApiIntegrationTests.RemovedDbSyncReturns404_HealthReturns200_And_SecurityHeadersPresent` | **A & E. API / Health** (`P0-05`, `P2-08`) | `POST /api/db/sync` bị gỡ bỏ (`404`); `GET /health` trả `200 OK` kiểm tra kết nối SQL; phản hồi có `X-Content-Type-Options: nosniff`, `X-Frame-Options: SAMEORIGIN`. | `/api/db/sync` trả `404`; `/health` trả `200` (`status: healthy`); đầy đủ security headers (`20 ms`). | **PASS** |
| 14 | `HttpSecurityAndApiIntegrationTests.MutatingApi_WithoutOrForgedCsrfReturns400_WithValidCsrfSucceeds` | **A. CSRF** (`P1-17`) | Gọi POST JSON ghi dữ liệu không kèm hoặc giả mạo `RequestVerificationToken` bị chặn `400`; kèm token hợp lệ từ `/api/security/csrf` thành công `200`. | Không có CSRF / sai CSRF trả `400 Bad Request`; có CSRF hợp lệ tạo đơn `200 OK` (`52 ms`). | **PASS** |
| 15 | `HttpSecurityAndApiIntegrationTests.AnonymousState_StripsAllPii_FiltersSlotPlaceholder_And_SupportsETag304` | **A & E. Privacy / Realtime** (`P0-06`, `P2-04`) | Khách ẩn danh gọi `GET /api/db/state` không thấy tên/SĐT/email/mã vé/tiền thu, ẩn sản phẩm nội bộ `"Slot"`, và gửi lại `If-None-Match` nhận `304 Not Modified`. | Payload ẩn danh sạch `100%` PII & `"Slot"`; lần gọi thứ 2 kèm `ETag` trả `304 Not Modified` (`212 ms`). | **PASS** |
| 16 | `HttpSecurityAndApiIntegrationTests.Rbac_RegisterForcesCustomer_StaffBlockedFromAdminEndpoints_AdminUpdatesCmsPricing_And_LiveLockRevokesCookie` | **A & B. RBAC / CMS** (`P0-02`, `P0-03`, `P0-04`, `P2-05`) | Đăng ký công khai gửi kèm `roleId=1` vẫn chỉ tạo `Customer`; `Staff` bị chặn `403` ở `/api/admin/cms` & `/Admin/Accounts`; `Admin` đổi giá CMS cập nhật ngay `/api/bookings/quote`; khóa tài khoản thu hồi ngay phiên cookie qua `SecurityStamp`. | Escalation bị chặn; `Staff` nhận `403`; CMS đổi giá có hiệu lực tức thì trên quote; tài khoản bị khóa mất quyền ngay ở request kế tiếp (`606 ms`). | **PASS** |
| 17 | `HttpSecurityAndApiIntegrationTests.Idor_UserBCannotLookupQrCancelOrAttachOrderToUserABooking_GuestSecretTokenWorks_PhoneAloneRejected` | **A & C. IDOR** (`P0-07`, `P1-12`, `P1-14`) | User B (dù nhập đúng SĐT của User A) bị chặn `403` khi tra cứu, lấy QR, hủy vé hoặc gắn đơn Pro Shop vào vé User A; khách vãng lai chỉ dùng SĐT bị chặn `403`, phải có `bk_...` `accessToken` mới hợp lệ. | Mọi hành vi mạo danh bằng SĐT đều bị chặn `403 Forbidden`; chủ tài khoản thật hoặc người giữ `bk_...` hợp lệ thao tác thành công (`839 ms`). | **PASS** |
| 18 | `HttpSecurityAndApiIntegrationTests.EndToEndQrImageUploadCheckIn_PendingRejected409_ApprovedDecodedByZXing200_ReplayRejected409` | **D. E2E QR Upload** (`P1-01`, `P1-07`) | Tải ảnh QR PNG thật từ `GET /api/bookings/{code}/qr.png`, upload multipart lên `POST /api/checkin/decode-qr`: vé `pending` bị chặn `409`, sau khi duyệt `approved` giải mã `ZXing.Net` check-in `200`, quét lại lần 2 bị chặn replay `409`. | Luồng ảnh QR thực tế từ server -> upload -> `ZXing.Net` -> check-in chạy chuẩn xác: `409` (pending) -> `200` (approved) -> `409` (replay) (`379 ms`). | **PASS** |
| 19 | `HttpSecurityAndApiIntegrationTests.ForgotPasswordReturnsHonest503_And_ReportsSummaryFiltersStrictlyByPaymentCreatedAt` | **C. Honest OTP & Ledger Report** (`P1-02`, `P1-06`) | `POST /api/auth/forgot-password` trả `503` minh bạch; `/api/admin/reports/summary` tổng hợp doanh thu theo đúng ngày thu tiền thực tế `TbPaymentTransaction.CreatedAt` (tách rời ngày đặt sân `BookingDate`). | `/api/auth/forgot-password` trả `503 OTP_PROVIDER_NOT_CONFIGURED`; báo cáo ngày thu tiền phản ánh đúng số thực thu sổ cái, ngày đặt sân chưa thu tiền có doanh thu thực thu `= 0` (`1 s`). | **PASS** |

---

## 3. Diễn Tập Sao Lưu, Khôi Phục, `DBCC CHECKDB` & `--migrate` Trên Bản Sao CSDL Thực (`P2-01`, `P2-02`, `P2-08`)

Thực hiện trực tiếp trên máy chủ `.\SQLEXPRESS` bằng chế độ `COPY_ONLY` sang DB cô lập `[PickleballClub_DryRunVerify]` (tuyệt đối không chạm vào DB `PickleballClub` đang chạy của người dùng):

1. **Sao lưu có kiểm tra `CHECKSUM` & xác minh file `.bak`**:
   - Lệnh: `BACKUP DATABASE [PickleballClub] TO DISK = N'...\PickleballClub_DryRun.bak' WITH COPY_ONLY, FORMAT, INIT, CHECKSUM` + `RESTORE VERIFYONLY`.
   - Kết quả: `Processed 682 pages in 0.063 seconds` — **`The backup set on file 1 is valid`**.
2. **Khôi phục sang `[PickleballClub_DryRunVerify]` & kiểm tra toàn vẹn vật lý**:
   - Lệnh: `RESTORE DATABASE [PickleballClub_DryRunVerify] ...` + `DBCC CHECKDB ([PickleballClub_DryRunVerify]) WITH NO_INFOMSGS`.
   - Kết quả: **`0 allocation errors and 0 consistency errors`**. Số dòng trước nâng cấp: `Accounts = 2, Courts = 4, Bookings = 3, Products = 10`.
3. **Chạy lệnh nâng cấp Production CLI (`--migrate`)**:
   - Lệnh:
     ```powershell
     $env:ASPNETCORE_ENVIRONMENT="Production"
     $env:SPIKE_QR_SIGNING_KEY="ProdDryRunTestSigningKey_AtLeast32BytesLong_2026!"
     $env:ConnectionStrings__DefaultConnection="Server=.\SQLEXPRESS;Database=PickleballClub_DryRunVerify;Trusted_Connection=True;TrustServerCertificate=True;"
     dotnet "e:\pickleball-ui\bin\Release\net10.0\PickleballClub.dll" --migrate
     ```
   - Kết quả: **`EF Core migrations applied successfully`** (`20261109010000_V2_BaselineAndSafeExpand` & `20261109010500_V2_LedgerTypedColumnsAndRowVersion`), bảo toàn nguyên vẹn `Accounts = 2, Courts = 4, Bookings = 3, Products = 10`, không lỗi, không mất dữ liệu.
4. **Dọn dẹp sau diễn tập**: Đã xóa `[PickleballClub_DryRunVerify]` và file `.bak` tạm; xác nhận `SELECT name FROM sys.databases WHERE name LIKE N'PickleballClub%'` chỉ còn duy nhất `PickleballClub`.

---

## 4. Kiểm Thử Trình Duyệt Thật Ở 4 Độ Rộng Màn Hình (`375, 768, 1024, 1440 px`) (`P2-09`)

- **Công cụ**: Google Chrome Headless kết nối qua CDP chạy trên instance thực thi thật của ứng dụng, đăng nhập phiên thực tế cho các trang khách hàng và quản trị.
- **Phạm vi**: **12 trang cốt lõi × 4 kích thước khung nhìn (`375px`, `768px`, `1024px`, `1440px`) = 48 bài kiểm tra bố cục & DOM**.
- **Các lỗi giao diện phát hiện trong lượt quét đầu và đã sửa triệt để**:
  1. Thanh điều hướng khách (`.header-inner`) bị tràn ngang `1128px` ở màn hình `1024px` -> Đã thu gọn khoảng cách menu và chuyển sang menu ngăn kéo từ `<= 1100px` trong `wwwroot/css/styles.css`.
  2. Khung chọn ngày trang `/Booking` dùng class `.date-chip` chưa đồng bộ với `.date-pill` -> Đã đồng bộ trong `wwwroot/js/booking.js` và `wwwroot/css/styles.css`.
  3. Topbar trang Quản trị (`.adm-header`) bị tràn ngang `502px` ở mobile `375px` và ô chọn ngày trang `/Reports` chữ trắng trên nền trắng -> Đã sửa responsive header trong `Areas/Admin/Views/Shared/_Layout.cshtml` và độ tương phản input ngày trong `Areas/Admin/Views/Home/Reports.cshtml`.
  4. Lưới biểu mẫu trang `/Account/Profile` chưa tự co xuống 1 cột ở `375px` -> Đã bổ sung class `.profile-two-col-grid` co giãn chuẩn tại `<= 640px`.

### Kết Quả Quét Lần Cuối (`48/48 PASS`)

| Tuyến Đường (Route) | `375px` (`scrollWidth`) | `768px` (`scrollWidth`) | `1024px` (`scrollWidth`) | `1440px` (`scrollWidth`) | Tràn Ngang (`Overflow`) | Lỗi JS Runtime | Trạng Thái |
|---|---|---|---|---|---|---|---|
| `/` (Trang chủ) | `375 / 375` | `753 / 768` | `1009 / 1024` | `1425 / 1440` | `0 px` | `0` | **PASS** |
| `/Booking` (Đặt sân) | `375 / 375` | `753 / 768` | `1009 / 1024` | `1425 / 1440` | `0 px` | `0` | **PASS** |
| `/Product` (Pro Shop & F&B) | `375 / 375` | `753 / 768` | `1009 / 1024` | `1425 / 1440` | `0 px` | `0` | **PASS** |
| `/Lookup` (Tra cứu vé & QR) | `375 / 375` | `753 / 768` | `1009 / 1024` | `1440 / 1440` | `0 px` | `0` | **PASS** |
| `/Account/Login` (Đăng nhập/Đăng ký) | `375 / 375` | `768 / 768` | `1024 / 1024` | `1440 / 1440` | `0 px` | `0` | **PASS** |
| `/Account/Profile` (Hồ sơ hội viên) | `375 / 375` | `753 / 768` | `1009 / 1024` | `1425 / 1440` | `0 px` | `0` | **PASS** |
| `/Admin` (Tổng quan quản trị) | `375 / 375` | `753 / 768` | `1009 / 1024` | `1425 / 1440` | `0 px` | `0` | **PASS** |
| `/Management` (POS & Sổ cái sân) | `375 / 375` | `753 / 768` | `1009 / 1024` | `1425 / 1440` | `0 px` | `0` | **PASS** |
| `/Checkin` (Soát vé QR) | `375 / 375` | `753 / 768` | `1009 / 1024` | `1425 / 1440` | `0 px` | `0` | **PASS** |
| `/Reports` (Báo cáo sổ cái) | `375 / 375` | `753 / 768` | `1009 / 1024` | `1425 / 1440` | `0 px` | `0` | **PASS** |
| `/Cms` (Cấu hình giá & giờ mở cửa) | `375 / 375` | `753 / 768` | `1009 / 1024` | `1425 / 1440` | `0 px` | `0` | **PASS** |
| `/Admin/Accounts` (Hội viên & Nhân sự) | `375 / 375` | `753 / 768` | `1009 / 1024` | `1425 / 1440` | `0 px` | `0` | **PASS** |

- **Ảnh chụp màn hình thực tế**: Lưu trữ đầy đủ tại thư mục phiên kiểm định (`v2_screenshots\home_375px.png` .. `admin_accounts_1440px.png`).

---

## 5. Ghi Nhận Trung Thực Các Giới Hạn Môi Trường / Bên Thứ Ba

1. **`P1-02` (Quên mật khẩu OTP tự động qua SMS/Email)**: Giữ trạng thái **`BLOCKED_EXTERNAL`** vì chưa có thông tin kết nối nhà cung cấp SMS Brandname / Zalo ZNS / Transactional SMTP từ chủ CLB. Đã gỡ bỏ hoàn toàn thông báo gửi OTP giả và trả về mã chuẩn `HTTP 503 OTP_PROVIDER_NOT_CONFIGURED`.
2. **Ống kính Camera vật lý trên điện thoại iOS/Android (`P1-01`)**: Máy chủ chạy kiểm thử tự động không gắn thiết bị điện thoại vật lý có camera quang học; tuy nhiên luồng tạo ảnh QR chuẩn ISO/IEC 18004 (`QRCoder`), tải ảnh QR và upload giải mã bằng `ZXing.Net` tại `POST /api/checkin/decode-qr`, cũng như mã khởi tạo `navigator.mediaDevices.getUserMedia` + `BarcodeDetector` trên trình duyệt Chrome đã được kiểm thử đầu-cuối thành công.
