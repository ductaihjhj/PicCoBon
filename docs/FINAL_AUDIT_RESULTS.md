# BÁO CÁO NGHIỆM THU TỔNG HỢP V2 (40 HẠNG MỤC KHÓA GỐC) — SPIKE PICKLEBALL CLUB

- **Ngày nghiệm thu**: `09/11/2026`
- **Nhánh Git**: `fix/remediation-v2`
- **Tiêu chuẩn đối chiếu**: `Antigravity_Remediation_Prompt_Spike_Pickleball_v2.md` (giữ nguyên tuyệt đối **40 ID và tên gốc** của Master Prompt ban đầu, không gộp hay đổi tên ID).
- **Tổng kết trạng thái thực tế**:
  - **Hoàn thành & Đã qua kiểm thử thực tế (`DONE`)**: **`39 / 40` hạng mục**
  - **Chờ thông tin nhà cung cấp bên ngoài (`BLOCKED_EXTERNAL`)**: **`1 / 40` hạng mục** (`P1-02` — Quên mật khẩu OTP tự động qua SMS/SMTP; đã gỡ bỏ hoàn toàn thông báo giả và trả về mã chuẩn `HTTP 503 OTP_PROVIDER_NOT_CONFIGURED`)
- **Kết quả cổng chất lượng (Quality Gates)**:
  - `dotnet build PickleballClub.csproj -c Release`: **`0 Warning(s), 0 Error(s)`**
  - `node --check wwwroot/js/*.js`: **`8/8 PASS`**
  - `dotnet test PickleballClub.Tests/PickleballClub.Tests.csproj -c Release`: **`19/19 PASS`** (chạy trực tiếp trên SQL Server `.\SQLEXPRESS` thật và HTTP pipeline đầy đủ)
  - Diễn tập Backup + `RESTORE VERIFYONLY` + Restore + `DBCC CHECKDB` + `--migrate`: **`PASS` (`682 pages`, `0` lỗi, bảo toàn `100%` dữ liệu)**
  - Kiểm thử trình duyệt Google Chrome Headless (`375, 768, 1024, 1440 px` × `12` trang): **`48/48 PASS` (`0` tràn ngang, `0` lỗi JS runtime)**

---

## 1. Bảng Nghiệm Thu Chi Tiết 40 ID Gốc (`P0`, `P1`, `P2`)

### 1.1 Nhóm P0 — Bảo Mật, Quyền Sở Hữu, Cạnh Tranh SQL & Toàn Vẹn Dữ Liệu (`12/12 DONE`)

| ID | Yêu Cầu Gốc (Khóa Cứng) | Kết Quả Khắc Phục & Kiểm Chứng Thực Tế | Trạng Thái |
|---|---|---|---|
| **P0-01** | Không dùng static account global | Xác thực hoàn toàn theo từng HTTP Request qua `IHttpContextAccessor` + `ClaimsPrincipal` ([`Utilities/Function.cs`](file:///e:/pickleball-ui/Utilities/Function.cs)). Đã kiểm chứng cô lập đa phiên song song. | **DONE** |
| **P0-02** | Chống cookie giả, cookie an toàn | Cookie mã hóa `spike_auth` (`HttpOnly`, `SecurePolicy=Always` ở Production) kèm kiểm tra `SecurityStamp` & `IsActive` trực tiếp trên mỗi request (`OnValidatePrincipal`) trong [`Program.cs`](file:///e:/pickleball-ui/Program.cs), thu hồi phiên ngay lập tức khi khóa tài khoản. | **DONE** |
| **P0-03** | Không đăng ký Admin công khai | `/api/auth/register` ép cứng `RoleId = 2` (`Customer`); `/Admin/Register` khóa chặt `[Authorize(Roles = "Admin")]`. | **DONE** |
| **P0-04** | Phân quyền Admin controllers | Phân tách rõ quyền `Admin` và `Staff` trên toàn bộ Controllers và JSON API; `Staff` bị chặn `403 Forbidden` khi truy cập `/Admin/Accounts` hoặc `/api/admin/cms`. | **DONE** |
| **P0-05** | Loại bỏ API sync snapshot, thay API theo action | Xóa hoàn toàn endpoint `POST /api/db/sync` (trả `404`); mọi thao tác ghi chuyển sang các endpoint nghiệp vụ riêng có RBAC, CSRF và Audit Log. | **DONE** |
| **P0-06** | Không lộ PII trong public API | `GET /api/db/state` cho khách ẩn danh chỉ trả khung giờ bận ẩn danh trong cửa sổ `-1..+30` ngày, loại bỏ sạch tên, SĐT, email, mã vé, số tiền và mặt hàng nội bộ `"Slot"`. | **DONE** |
| **P0-07** | Chống IDOR xem/hủy đơn | Bỏ cơ chế dùng SĐT tự khai để xem/hủy vé; bắt buộc khớp `AccountId` phiên đăng nhập hoặc mã bí mật `bk_...` (`AccessTokenHash` SHA-256) trong [`Controllers/ApiDbController.cs`](file:///e:/pickleball-ui/Controllers/ApiDbController.cs). | **DONE** |
| **P0-08** | Booking/extend/lock concurrency | Áp dụng khóa `SELECT 1 FROM tb_Court WITH (UPDLOCK, HOLDLOCK)` trong Transaction `Serializable` tại [`Services/BookingService.cs`](file:///e:/pickleball-ui/Services/BookingService.cs). Đã vượt qua bài test `10` luồng đặt đè giờ song song trên SQL Server thật (`1` thắng, `9` nhận `409`). | **DONE** |
| **P0-09** | Password hash, secrets, demo credentials | Băm PBKDF2 + nâng cấp MD5 legacy; [`Services/QrTokenService.cs`](file:///e:/pickleball-ui/Services/QrTokenService.cs) fail-fast ở Production nếu thiếu hoặc dùng khóa yếu; bỏ mật khẩu mặc định `"123456"` thay bằng mật khẩu ngẫu nhiên mạnh dùng 1 lần (`MustChangePassword = true`). | **DONE** |
| **P0-10** | Giá, stock, order server-authoritative | Server tính `100%` giá sân và giá sản phẩm từ CSDL; trừ kho nguyên tử bằng `UPDLOCK, ROWLOCK, HOLDLOCK` theo thứ tự `ProductId` tăng dần trong [`Services/OrderService.cs`](file:///e:/pickleball-ui/Services/OrderService.cs). | **DONE** |
| **P0-11** | Không overwrite đa thiết bị, 409 concurrency | Bổ sung `RowVersion` (`[Timestamp]`) trên `TbBooking`, `TbCourt`, `TbProduct`, `TbOrder` và xử lý `DbUpdateConcurrencyException` trả `409 Conflict`. | **DONE** |
| **P0-12** | Chống XSS toàn ứng dụng | Thay thế `@Html.Raw(p.Icon)` bằng [`Services/IconSanitizer.cs`](file:///e:/pickleball-ui/Services/IconSanitizer.cs) kết hợp `escapeHtml`/`escapeAttr` trên toàn bộ JS; vượt qua bài kiểm thử payload XSS. | **DONE** |

---

### 1.2 Nhóm P1 — Nghiệp Vụ Cốt Lõi: QR, Sổ Cái Thu Ngân, Giá Tách Đoạn & Kho Hàng (`17/18 DONE`, `1/18 BLOCKED_EXTERNAL`)

| ID | Yêu Cầu Gốc (Khóa Cứng) | Kết Quả Khắc Phục & Kiểm Chứng Thực Tế | Trạng Thái |
|---|---|---|---|
| **P1-01** | QR thật và check-in server | Sinh ảnh QR chuẩn ISO/IEC 18004 bằng `QRCoder` (`/qr.svg`, `/qr.png`) chứa token ký HMAC `SPK1...` (không chứa PII) và giải mã ngược độc lập bằng `ZXing.Net` tại `POST /api/checkin/decode-qr`, chặn quét lặp (replay `409`). *(Lưu ý: Đã test đầu-cuối bằng ảnh QR thật + trình duyệt Chrome; ống kính điện thoại vật lý chưa gắn vào máy CI).* | **DONE** |
| **P1-02** | Quên mật khẩu thật | Đã gỡ bỏ hoàn toàn toast `"Đã gửi mã OTP"` giả trên [`Views/Account/Login.cshtml`](file:///e:/pickleball-ui/Views/Account/Login.cshtml); `/api/auth/forgot-password` trả `HTTP 503 OTP_PROVIDER_NOT_CONFIGURED` minh bạch. Chờ chủ CLB cấp tài khoản cổng SMS/ZNS/SMTP thật (xem [`docs/DECISIONS_NEEDED.md`](file:///e:/pickleball-ui/docs/DECISIONS_NEEDED.md)). | **BLOCKED_EXTERNAL** |
| **P1-03** | Google/Zalo/Apple OAuth thật hoặc ẩn | Đã ẩn hoàn toàn nút đăng nhập MXH chưa cấu hình và xóa sạch hàm JS `handleSocialLogin` khỏi `Views/Account/Login.cshtml`. | **DONE** |
| **P1-04** | Payment thật/VNPAY callback hoặc tại quầy có xác nhận | Hoàn thiện luồng thu ngân xác nhận thu tiền mặt (`cash`) và chuyển khoản bắt buộc mã tham chiếu (`transfer` + `ReferenceCode`) có `IdempotencyKey` cho cả Sân và Đơn lẻ Pro Shop; tắt thanh toán online giả. | **DONE** |
| **P1-05** | PaidAmount thực thu | Tách rời tuyệt đối `FinishBookingSessionAsync` (đóng ca chơi giữ nguyên công nợ) khỏi `RecordBookingPaymentAsync`; `PaidAmount` luôn bằng `SUM(payment) - SUM(refund)` trong sổ cái `TbPaymentTransaction`. | **DONE** |
| **P1-06** | Báo cáo từ payment ledger | `/api/admin/reports/summary` tổng hợp doanh thu theo đúng ngày phát sinh giao dịch `TbPaymentTransaction.CreatedAt` (múi giờ Việt Nam), tách rõ thực thu sân, thực thu Pro Shop, hoàn tiền và công nợ chưa thu. | **DONE** |
| **P1-07** | State machine check-in/no-show | Chỉ vé `approved` trong khung giờ hợp lệ mới được Check-in (`pending` bị từ chối `409`); tác vụ nền [`Services/BookingLifecycleWorker.cs`](file:///e:/pickleball-ui/Services/BookingLifecycleWorker.cs) tự động chuyển `expired`/`no_show` thay vì ghi lén trong lệnh `GET`. | **DONE** |
| **P1-08** | Giá segment đúng, server quote | Hợp nhất cấu hình giá/giờ vàng tại [`Services/ClubSettingsService.cs`](file:///e:/pickleball-ui/Services/ClubSettingsService.cs); tính giá tách đoạn chính xác từng lát 30 phút và lưu snapshot vào vé. | **DONE** |
| **P1-09** | Chặn giờ quá khứ/ngoài cửa | Server và UI đồng bộ khung giờ mở/đóng cửa từ CMS và từ chối mọi yêu cầu đặt giờ trong quá khứ hoặc vượt giờ đóng cửa. | **DONE** |
| **P1-10** | POS extend không trùng lịch | Gia hạn `+30p` trên POS khóa nguyên tử trên sân trong SQL Server, chống đè cả vé kế tiếp lẫn lịch bảo trì. | **DONE** |
| **P1-11** | Court maintenance conflict & time-bound display | Tạo lịch bảo trì qua `BookingService.CreateCourtLockAsync` có khóa SQL chống đè lịch đặt sân; bảng điều khiển hiển thị đúng khoảng giờ bảo trì. | **DONE** |
| **P1-12** | Khách chỉ thấy/hủy vé mình | Kiểm soát chặt quyền sở hữu qua `AccountId` hoặc `bk_...` `accessToken` và kiểm tra thời hạn hủy trước giờ chơi (`cancelCutoffMinutes`). | **DONE** |
| **P1-13** | Chỉ báo thành công sau server commit | Giao diện chỉ hiển thị xác nhận sau khi nhận HTTP `200` từ Server; tự động báo lỗi và làm mới dữ liệu khi gặp `409 Conflict` hoặc lỗi mạng. | **DONE** |
| **P1-14** | Order ownership/FK/không gộp nhầm | Kiểm tra quyền sở hữu vé **trước** trạng thái/ngày chơi khi gắn đơn Pro Shop vào sân; hỗ trợ đầy đủ đơn mua lẻ độc lập (`BookingId == null`). | **DONE** |
| **P1-15** | Inventory / reserve / refund / oversell | Vượt qua bài kiểm thử `10` người mua tranh `3` sản phẩm cuối cùng trên SQL Server (`3` thành công, `7` nhận `409`, tồn kho `== 0`, không âm, không deadlock), hỗ trợ hủy đơn hoàn kho và hoàn tiền sổ cái. | **DONE** |
| **P1-16** | VIP và split bill thực | Chặn mạo danh SĐT nhận giảm giá VIP; tính năng chia tiền nhóm ghi rõ nhãn **"Ước tính chia đều tham khảo (chưa thanh toán)"**. | **DONE** |
| **P1-17** | CSRF + validation + status transitions | Áp dụng `[ValidateAntiForgeryToken]` trên toàn bộ endpoint ghi dữ liệu cookie-auth (đã test chặn `400` khi thiếu/sai token) và kiểm soát chặt trạng thái đơn. | **DONE** |
| **P1-18** | Member tier theo payment thật | Hạng thẻ hội viên (`Silver` / `VIP`) tính từ tổng tiền thực thu ròng (`payment - refund`) gắn với `AccountId` trong `TbPaymentTransaction`. | **DONE** |

---

### 1.3 Nhóm P2 — Database Migrations, Vận Hành, Giao Diện Responsive & Tài Liệu (`10/10 DONE`)

| ID | Yêu Cầu Gốc (Khóa Cứng) | Kết Quả Khắc Phục & Kiểm Chứng Thực Tế | Trạng Thái |
|---|---|---|---|
| **P2-01** | Cột date/time/decimal/rowversion + migration | Bổ sung 2 bản EF Core Migrations chuẩn hóa `DECIMAL(18,2)`, `DateOnly`/`TimeOnly`, `RowVersion`, `IdempotencyKey` unique filtered index và trigger SQL bảo vệ sổ cái bất biến `TR_tb_PaymentTransaction_Immutable`. | **DONE** |
| **P2-02** | SQL demo safe, prod migration | Xóa `DatabaseSchemaUpdater.cs`; cách ly script SQL có `DROP DATABASE` sang [`scripts/demo-only/Database_PickleballClub_DEMO_DESTRUCTIVE.sql`](file:///e:/pickleball-ui/scripts/demo-only/Database_PickleballClub_DEMO_DESTRUCTIVE.sql); cung cấp lệnh CLI `--migrate` an toàn cho Production. | **DONE** |
| **P2-03** | README chính xác | Cập nhật [`README.md`](file:///e:/pickleball-ui/README.md) đúng thực tế `.NET 10`, biến môi trường, `--migrate`, `/health`, kiểm thử và không chứa mật khẩu demo. | **DONE** |
| **P2-04** | Near-realtime đa thiết bị | Hỗ trợ `ETag` / `If-None-Match` trả `304 Not Modified` trên `/api/db/state`, đồng bộ nhẹ nhàng đa thiết bị và xử lý `409 Conflict`. | **DONE** |
| **P2-05** | CMS data chính xác, bao gồm pricing/hours | Đồng bộ selector `[data-cms="openHours"]` & `[data-cms="hours"]`; cập nhật giá và giờ hoạt động qua `/Cms` có hiệu lực tức thì trên cả Server quote và UI. | **DONE** |
| **P2-06** | Không localStorage lưu DB/PII | Không lưu bất kỳ PII, phiên đăng nhập hay token nào vào `localStorage`, đồng thời tự động xóa khóa cũ `pb_auth_user` nếu tồn tại. | **DONE** |
| **P2-07** | Không marketing claim giả | Đã gỡ bỏ hoàn toàn các tuyên bố chưa kiểm chứng (`"1,250+ tay vợt"`, `"đạt chuẩn quốc tế USAPA"`) khỏi giao diện và lọc sạch dữ liệu hiển thị. | **DONE** |
| **P2-08** | Tests, logs, health, backup | Bổ sung `GET /health`, 19 bài kiểm thử tự động SQL/HTTP/Unit, và hoàn thành diễn tập Backup + Restore + `DBCC CHECKDB` trên bản sao CSDL thật. | **DONE** |
| **P2-09** | Responsive/accessibility/DOM safety | Đạt `48/48` bài kiểm tra bằng Google Chrome Headless ở `375, 768, 1024, 1440 px` trên 12 trang với `0` tràn ngang và `0` lỗi JS. | **DONE** |
| **P2-10** | Clean source/build artifacts | Chuẩn hóa [`.gitignore`](file:///e:/pickleball-ui/.gitignore) và hướng dẫn đóng gói bản phát hành sạch không chứa `bin/`, `obj/`, `.bak` hay khóa bí mật. | **DONE** |

---

## 2. Tài Liệu Bàn Giao Đính Kèm Trong `docs/`

1. [`docs/REMEDIATION_BASELINE.md`](file:///e:/pickleball-ui/docs/REMEDIATION_BASELINE.md) — Snapshot hiện trạng trước khi sửa V2.
2. [`docs/REMEDIATION_MATRIX.md`](file:///e:/pickleball-ui/docs/REMEDIATION_MATRIX.md) — Ma trận đối chiếu chi tiết toàn bộ 40 ID gốc.
3. [`docs/REMEDIATION_WORKLOG.md`](file:///e:/pickleball-ui/docs/REMEDIATION_WORKLOG.md) — Nhật ký triển khai theo từng giai đoạn.
4. [`docs/TEST_EVIDENCE.md`](file:///e:/pickleball-ui/docs/TEST_EVIDENCE.md) — Bằng chứng thực thi lệnh build, 19 bài test SQL/HTTP, diễn tập Backup/Restore và 48 bài kiểm tra trình duyệt.
5. [`docs/DECISIONS_NEEDED.md`](file:///e:/pickleball-ui/docs/DECISIONS_NEEDED.md) — Hạng mục chờ cổng OTP bên thứ ba (`P1-02`) và cấu hình mặc định của CLB.
6. [`docs/DEPLOY_AND_ROLLBACK.md`](file:///e:/pickleball-ui/docs/DEPLOY_AND_ROLLBACK.md) — Quy trình sao lưu, nâng cấp `--migrate`, kiểm tra `/health` và phục hồi khẩn cấp.
