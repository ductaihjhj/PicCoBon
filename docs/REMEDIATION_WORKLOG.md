# REMEDIATION WORKLOG — Spike Pickleball Club (Master Prompt V2)

## Checkpoint 0 — Baseline, Environment Verification & Snapshot (`2026-11-09T00:26+07:00`)
- **Completed**:
  - Đọc toàn bộ `Antigravity_Remediation_Prompt_Spike_Pickleball_v2.md`, `Antigravity_Master_Prompt_Spike_Pickleball.md`, `AGENTS.md`, `.agent/skills/frontend-design/SKILL.md` và đối chiếu trực tiếp toàn bộ Controllers, Services, Models, Razor Views, ViewComponents và JavaScript.
  - Xác minh môi trường `.NET 10.0.402`, SQL Server 2022 Express (`.\SQLEXPRESS`), Node.js và Google Chrome Headless.
  - Tạo nhánh làm việc độc lập `fix/remediation-v2` từ commit baseline `c996337`.
  - Khởi tạo `docs/REMEDIATION_BASELINE.md` và `docs/REMEDIATION_MATRIX.md` khóa cứng 40 ID gốc (`P0-01`..`P0-12`, `P1-01`..`P1-18`, `P2-01`..`P2-10`).

---

## Batch 1 — Release Blockers: Sổ Cái Thu Ngân, SQL Concurrency, CSRF, IDOR & Bảo Mật Cốt Lõi
- **Thay đổi thực hiện**:
  - **Tách bạch Kết thúc ca & Thu tiền thật (`P1-04`, `P1-05`, `P1-06`)**: Tách `FinishBookingSessionAsync` (đóng ca chơi, không ghi doanh thu ảo) khỏi `RecordBookingPaymentAsync` và `RefundBookingPaymentAsync` trong `Services/BookingService.cs` và `Areas/Admin/Controllers/BookingsController.cs`. Bắt buộc nhập `ReferenceCode` khi thanh toán `transfer`.
  - **Đơn lẻ Pro Shop & Chống bán âm kho / chống Deadlock (`P0-10`, `P1-14`, `P1-15`)**: Xây dựng `Services/OrderService.cs` thực thi khóa hàng `SELECT 1 FROM tb_Product WITH (UPDLOCK, ROWLOCK, HOLDLOCK)` theo thứ tự `ProductId` tăng dần trong Transaction SQL, hỗ trợ `IdempotencyKey`, thanh toán đơn lẻ, hoàn tiền đối ứng và hủy đơn hoàn kho (`+Qty` vào `TbInventoryMovement`).
  - **Khóa cạnh tranh Đặt sân / Gia hạn POS / Bảo trì sân (`P0-08`, `P0-11`, `P1-10`, `P1-11`)**: Bao bọc `CreateBookingAsync`, `ApproveBookingAsync`, `ExtendBookingAsync`, `CreateCourtLockAsync` bằng `IsolationLevel.Serializable` kèm `SELECT 1 FROM tb_Court WITH (UPDLOCK, HOLDLOCK)`.
  - **CSRF, Quyền sở hữu & Chống dò vé (`P0-05`, `P0-06`, `P0-07`, `P1-12`, `P1-17`)**: Xóa `POST /api/db/sync`; bổ sung `/api/security/csrf` và `[ValidateAntiForgeryToken]` trên toàn bộ endpoint ghi dữ liệu; thay kiểm tra SĐT tự nhập bằng `AccountId` hoặc mã bí mật `bk_...` (`AccessTokenHash` SHA-256), đồng thời đặt kiểm tra quyền sở hữu **trước** kiểm tra ngày/trạng thái trong `OrderService` để chống dò mã vé.
  - **Bảo mật Khóa ký, Cookie, Mật khẩu tạm & XSS (`P0-02`, `P0-09`, `P0-12`, `P2-06`)**: `QrTokenService` fail-fast ở Production nếu thiếu key hoặc key yếu; `OnValidatePrincipal` kiểm tra `SecurityStamp` thu hồi phiên tức thì khi khóa tài khoản; thay mật khẩu mặc định `"123456"` bằng mật khẩu ngẫu nhiên mạnh dùng 1 lần (`MustChangePassword = true`); thay `@Html.Raw(p.Icon)` bằng `IconSanitizer.RenderSafeIconHtml()`; xóa sạch `pb_auth_user` khỏi `localStorage`.

---

## Batch 2 — Chuẩn Hóa Nghiệp Vụ MVP: QR Chuẩn ISO/ZXing, Check-in, Tính Giá Tách Đoạn & CMS
- **Thay đổi thực hiện**:
  - **QR Code thật & Giải mã độc lập (`P1-01`, `P1-07`)**: Tích hợp `QRCoder` xuất `/api/bookings/{code}/qr.svg` & `/qr.png` chứa token ký HMAC-SHA256 `SPK1...` (không chứa PII); tích hợp `ZXing.Net` + `SixLabors.ImageSharp` giải mã ảnh upload tại `POST /api/checkin/decode-qr`; chỉ cho phép check-in vé `approved` trong khung giờ hợp lệ và chống quét lại (replay); chuyển tự động đối soát `expired`/`no_show` sang `BookingLifecycleWorker`.
  - **Một nguồn cấu hình giờ & giá duy nhất (`P1-08`, `P1-09`, `P1-16`, `P1-18`, `P2-05`)**: Xây dựng `Services/ClubSettingsService.cs` hợp nhất cấu hình giờ mở/đóng cửa, mốc giờ vàng, phụ thu giờ vàng và cập nhật qua `PUT /api/admin/cms`; xét hạng thẻ hội viên dựa trên doanh thu thực thu ròng (`TbPaymentTransaction`) gắn với `AccountId`; ghi nhãn rõ tính năng chia tiền nhóm là ước tính tham khảo.
  - **Minh bạch OAuth / OTP / Nội dung giới thiệu (`P1-02`, `P1-03`, `P2-07`)**: Gỡ bỏ toast gửi OTP giả (trả `HTTP 503 OTP_PROVIDER_NOT_CONFIGURED`, đánh dấu `P1-02` = `BLOCKED_EXTERNAL`), xóa hàm `handleSocialLogin` chết và gỡ bỏ hoàn toàn các tuyên bố `"1,250+"` / `"USAPA"` chưa kiểm chứng.

---

## Batch 3 — EF Core Migrations, Kiểm Thử SQL/HTTP Thật, Backup Drill & Giao Diện Responsive
- **Thay đổi thực hiện**:
  - **EF Core Migrations an toàn (`P2-01`, `P2-02`, `P2-10`)**: Xóa `Services/DatabaseSchemaUpdater.cs`; cách ly script SQL có `DROP DATABASE` vào `scripts/demo-only/Database_PickleballClub_DEMO_DESTRUCTIVE.sql`; tạo 2 bản migration `20261109010000_V2_BaselineAndSafeExpand` và `20261109010500_V2_LedgerTypedColumnsAndRowVersion` (kèm trigger `TR_tb_PaymentTransaction_Immutable`) và tham số CLI `--migrate`.
  - **Bộ kiểm thử tự động V2 (`PickleballClub.Tests`)**: Xây dựng 19 bài kiểm thử thực tế (`UnitAndQrVerificationTests`, `SqlServerConcurrencyAndMigrationTests`, `HttpSecurityAndApiIntegrationTests`) chạy trực tiếp trên SQL Server `.\SQLEXPRESS` và `WebApplicationFactory<Program>`. Phát hiện và xử lý triệt để lỗi SQL Deadlock `RangeS-S` khi 10 khách mua tranh 3 món cuối kho, lỗi thiếu cột legacy khi nâng cấp DB cũ và lọc ẩn mặt hàng nội bộ `"Slot"`. Kết quả: **`19/19 PASS`**.
  - **Diễn tập Sao lưu & Khôi phục (`P2-08`)**: Chạy `BACKUP DATABASE ... WITH COPY_ONLY, CHECKSUM` (`682 pages`), `RESTORE VERIFYONLY`, khôi phục sang `[PickleballClub_DryRunVerify]`, chạy `DBCC CHECKDB` (`0` lỗi) và chạy `--migrate` Production thành công `100%`, sau đó dọn sạch bản sao tạm.
  - **Kiểm thử trình duyệt thật ở 4 độ rộng (`P2-09`)**: Chạy Google Chrome Headless qua 12 trang × 4 kích thước (`375, 768, 1024, 1440 px`). Phát hiện và sửa tràn ngang thanh menu ở `1024px`, topbar Admin ở `375px`, tương phản ô ngày trang `/Reports` và lưới hồ sơ `/Account/Profile`. Chạy lại toàn bộ đạt **`48/48 PASS` (`0` tràn ngang, `0` lỗi JS)**.

---

## Batch 4 — Hoàn Thiện Tài Liệu Nghiệm Thu & Bàn Giao (`2026-11-09T01:41+07:00`)
- **Completed**:
  - Chạy lại `dotnet test -c Release` sau đợt tinh chỉnh CSS/View cuối cùng: **`19/19 PASS` (`14.10s`)**.
  - Hoàn thiện trọn bộ tài liệu chuẩn V2: `README.md`, `docs/REMEDIATION_BASELINE.md`, `docs/REMEDIATION_MATRIX.md`, `docs/REMEDIATION_WORKLOG.md`, `docs/TEST_EVIDENCE.md`, `docs/DECISIONS_NEEDED.md`, `docs/DEPLOY_AND_ROLLBACK.md`, và `docs/FINAL_AUDIT_RESULTS.md`.
  - Kiểm tra vệ sinh kho mã nguồn (`.gitignore`, không chứa `bin/`, `obj/`, `.bak`, khóa bí mật) và lưu commit sạch trên nhánh `fix/remediation-v2`.
