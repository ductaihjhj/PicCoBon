# BÁO CÁO NGHIỆM THU CỔNG PHÁT HÀNH V4 (`V4_RELEASE_GATE.md`)

- **Dự án**: Spike Pickleball Club (`e:\pickleball-ui`)
- **Phiên bản**: Master Prompt V4 — Release Blockers Remediation & Branded Digital Ticket
- **Trạng thái Cổng Phát hành (Release Gate)**: **ĐẠT (READY FOR RELEASE)**

---

## 1. Bảng Kiểm tra Tiêu chí Chặn Phát hành (Release Gate Checklist)

| Nhóm | Mã | Hạng mục nghiệm thu | Trạng thái | Bằng chứng xác thực |
| :--- | :--- | :--- | :--- | :--- |
| **Release Blockers** | **`V4-A1`** | Đơn `pending` có `HoldExpiresAtUtc` (TTL 30p), tự giải phóng slot ngay khi hết hạn, chặn duyệt đơn quá hạn (`409 PENDING_HOLD_EXPIRED`) và giới hạn tối đa 3 đơn `pending` đồng thời mỗi actor (`429 PENDING_HOLD_LIMIT_EXCEEDED`). | **DONE (PASS)** | [`BookingService.cs`](file:///e:/pickleball-ui/Services/BookingService.cs), [`V4ReleaseBlockersAndQrTests.cs`](file:///e:/pickleball-ui/PickleballClub.Tests/V4ReleaseBlockersAndQrTests.cs) |
| **Release Blockers** | **`V4-A2`** | Trạng thái booking do server quyết định; xóa hoàn toàn lệnh tự gán `b.status = 'completed'` ở client; hiển thị badge `"Quá giờ — chờ kết ca"` cho đến khi nhân viên bấm **Đóng ca**. | **DONE (PASS)** | [`shared.js`](file:///e:/pickleball-ui/wwwroot/js/shared.js), [`V4ReleaseBlockersAndQrTests.cs`](file:///e:/pickleball-ui/PickleballClub.Tests/V4ReleaseBlockersAndQrTests.cs) |
| **Release Blockers** | **`V4-A3`** | Idempotency định danh chặt theo `(Scope, ActorKey, TargetEntity, RequestHash, IdempotencyKey)`; phát lại đúng yêu cầu trả `200 OK (deduplicated=true)`, lệch đối tượng/số tiền/payload trả `409 IDEMPOTENCY_KEY_CONFLICT`. | **DONE (PASS)** | [`IdempotencyService.cs`](file:///e:/pickleball-ui/Services/IdempotencyService.cs), [`V4ReleaseBlockersAndQrTests.cs`](file:///e:/pickleball-ui/PickleballClub.Tests/V4ReleaseBlockersAndQrTests.cs) |
| **Release Blockers** | **`V4-A4`** | Hủy booking gắn đơn Pro Shop trong 1 giao dịch `Serializable`: chặn hủy khi đã giao hàng (`LINKED_ORDER_DELIVERED`) hoặc đã thu tiền; khi chưa giao & chưa thu tiền thì hủy đơn liên kết và hoàn kho mỗi sản phẩm **đúng 1 lần**. | **DONE (PASS)** | [`BookingService.cs`](file:///e:/pickleball-ui/Services/BookingService.cs), [`OrderService.cs`](file:///e:/pickleball-ui/Services/OrderService.cs), [`V4ReleaseBlockersAndQrTests.cs`](file:///e:/pickleball-ui/PickleballClub.Tests/V4ReleaseBlockersAndQrTests.cs) |
| **Security & Ops** | **`V4-B1`** | `/api/security/csrf` trả về cả `token` và `csrfToken`; lỗi CSRF trả `400` kèm `code = "CSRF_TOKEN_INVALID"` và header `X-Csrf-Error`; `apiFetch()` chỉ refresh & retry khi đúng lỗi CSRF. | **DONE (PASS)** | [`ApiDbController.cs`](file:///e:/pickleball-ui/Controllers/ApiDbController.cs), [`Program.cs`](file:///e:/pickleball-ui/Program.cs), [`shared.js`](file:///e:/pickleball-ui/wwwroot/js/shared.js) |
| **Security & Ops** | **`V4-B2`** | Khôi phục `guestAccessToken` khi khách vãng lai retry cùng `IdempotencyKey` trong cùng phiên trình duyệt qua cookie `HttpOnly` mã hóa DataProtection `spike_guest_bk`; không lộ token sang phiên khác. | **DONE (PASS)** | [`ApiDbController.cs`](file:///e:/pickleball-ui/Controllers/ApiDbController.cs), [`V4ReleaseBlockersAndQrTests.cs`](file:///e:/pickleball-ui/PickleballClub.Tests/V4ReleaseBlockersAndQrTests.cs) |
| **Security & Ops** | **`V4-B3`** | Middleware cưỡng bức `MustChangePassword` chặn toàn bộ trang và API ngoài allowlist cho đến khi người dùng đổi mật khẩu tạm thời thành công. | **DONE (PASS)** | [`Program.cs`](file:///e:/pickleball-ui/Program.cs), [`AccountController.cs`](file:///e:/pickleball-ui/Controllers/AccountController.cs), [`V4ReleaseBlockersAndQrTests.cs`](file:///e:/pickleball-ui/PickleballClub.Tests/V4ReleaseBlockersAndQrTests.cs) |
| **Security & Ops** | **`V4-B4`** | ETag của `GET /api/db/state` băm đầy đủ mọi trường hiển thị của sân, đơn đặt sân, lệnh khóa sân, sản phẩm, đơn lẻ Pro Shop, thông tin hội viên và cấu hình CMS. | **DONE (PASS)** | [`ApiDbController.cs`](file:///e:/pickleball-ui/Controllers/ApiDbController.cs), [`V4ReleaseBlockersAndQrTests.cs`](file:///e:/pickleball-ui/PickleballClub.Tests/V4ReleaseBlockersAndQrTests.cs) |
| **Security & Ops** | **`V4-B5`** | Rate limiting phân vùng (`RateLimitPartition.GetFixedWindowLimiter`) theo `category|trustedClientIp|userId` kèm `Retry-After: 60`. | **DONE (PASS)** | [`Program.cs`](file:///e:/pickleball-ui/Program.cs) |
| **Branded QR** | **`V4-QR`** | Nâng cấp **Premium Digital Ticket** (`Light Premium` & `Dark Sport`, khung QR `#FFFFFF`, tùy chọn Logo tâm QR `ECC-H` có tự kiểm chứng `ZXing.Net`, tải Vé PNG `1080×1520`, tải QR PNG/SVG, phóng to, in vé) + Benchmark **105/105 (100%)** mẫu QR. | **DONE (PASS)** | [`QrTokenService.cs`](file:///e:/pickleball-ui/Services/QrTokenService.cs), [`shared.js`](file:///e:/pickleball-ui/wwwroot/js/shared.js), [`qr-benchmark-report.json`](file:///e:/pickleball-ui/docs/qa/qr/qr-benchmark-report.json) |

---

## 2. Các hạng mục Phụ thuộc Dịch vụ Bên ngoài (`BLOCKED_EXTERNAL`)

Theo đúng nguyên tắc trung thực kỹ thuật, 3 hạng mục tích hợp nhà cung cấp bên ngoài dưới đây giữ trạng thái `BLOCKED_EXTERNAL` (đã có adapter/endpoint trả lỗi `503` rõ ràng, tuyệt đối không giả lập thành công):
- **`P1-02`**: Cổng gửi SMS/Email OTP tự động khi quên mật khẩu (`/api/auth/forgot-password` trả `503 OTP_PROVIDER_NOT_CONFIGURED`).
- **`P2-06`**: Cổng đăng nhập OAuth Google / Facebook / Apple (cần `ClientId` / `ClientSecret` thật).
- **`P2-07`**: Cổng thanh toán trực tuyến VietQR / Webhook ngân hàng tự động (hệ thống hiện vận hành thu tiền mặt & chuyển khoản xác nhận mã tham chiếu tại quầy).
