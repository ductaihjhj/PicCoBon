# BẰNG CHỨNG KIỂM THỬ TOÀN DIỆN V4 (`V4_TEST_EVIDENCE.md`)

Tài liệu này ghi nhận kết quả thực thi kiểm thử tự động trên **SQL Server `.\SQLEXPRESS` thật**, kiểm thử tích hợp HTTP (`WebApplicationFactory<Program>`) và bài benchmark **105 mẫu mã QR (`ZXing.Net` + `SixLabors.ImageSharp`)** cho phiên bản **Master Prompt V4**.

---

## 1. Tổng quan Kết quả Test Suite (`PickleballClub.Tests`)

- **Lệnh thực thi**:
  ```powershell
  dotnet test PickleballClub.Tests\PickleballClub.Tests.csproj -c Release --logger "console;verbosity=normal"
  ```
- **Tổng số bài kiểm thử**: **`25 / 25 PASSED (0 Failed, 0 Skipped)`**
- **Phân bổ bộ kiểm thử**:
  1. [`V4ReleaseBlockersAndQrTests.cs`](file:///e:/pickleball-ui/PickleballClub.Tests/V4ReleaseBlockersAndQrTests.cs): **5 bài kiểm thử tích hợp chuyên sâu V4** (`V4-A1`, `V4-A2`, `V4-A3`, `V4-A4`, `V4-B1..B4` và **Benchmark 105 mẫu QR `V4-QR`**).
  2. [`SqlServerConcurrencyAndMigrationTests.cs`](file:///e:/pickleball-ui/PickleballClub.Tests/SqlServerConcurrencyAndMigrationTests.cs): **5 bài kiểm thử tương tranh SQL Server `IsolationLevel.Serializable` & Migration**.
  3. [`HttpSecurityAndApiIntegrationTests.cs`](file:///e:/pickleball-ui/PickleballClub.Tests/HttpSecurityAndApiIntegrationTests.cs): **8 bài kiểm thử bảo mật HTTP, RBAC, IDOR, CSRF, Rate Limit, Sổ cái Thu/Chi & Báo cáo**.
  4. [`UnitAndQrVerificationTests.cs`](file:///e:/pickleball-ui/PickleballClub.Tests/UnitAndQrVerificationTests.cs): **7 bài kiểm thử đơn vị về Định giá phân đoạn, PBKDF2, QR HMAC-SHA256 & XSS Sanitizer**.

---

## 2. Chi tiết Nghiệm thu từng Hạng mục V4

| Mã hạng mục | Bài kiểm thử xác minh | Kịch bản kiểm thử thực tế | Kết quả |
| :--- | :--- | :--- | :--- |
| **`V4-A1`** | `V4_A1_PendingHoldTtl_ReleasesExpiredSlotImmediately_RejectsExpiredApproval_And_EnforcesPerActorCap` | 1. Tạo đơn `pending`, xác minh `HoldExpiresAtUtc` được lưu UTC.<br>2. Cho `HoldExpiresAtUtc` quá hạn 2 phút -> Nhân viên bấm duyệt nhận `409 Conflict` (`PENDING_HOLD_EXPIRED`).<br>3. Khách thứ 2 đặt đúng khung giờ đó ngay lập tức (không chờ cron) -> Thành công `200 OK`.<br>4. Cùng 1 hội viên tạo quá `MaxActivePendingPerActor = 3` đơn `pending` -> Đơn thứ 4 bị chặn `429 Too Many Requests` (`PENDING_HOLD_LIMIT_EXCEEDED`). | **PASS** |
| **`V4-A2`** | `V4_A2_BookingLifecycle_RemainsPlayingWhenOverdue_UntilStaffExplicitlyFinishesSession` | 1. Ca `playing` đã qua giờ kết thúc dự kiến -> Chạy `ReconcileBookingLifecycleAsync` và gọi `GET /api/db/state` -> Trạng thái vẫn là `playing` (không tự động chuyển sang `completed`).<br>2. Kiểm tra mã nguồn `wwwroot/js/shared.js` không còn `b.status = 'completed'`, hiển thị `"Quá giờ — chờ kết ca"`.<br>3. Nhân viên gọi `POST /api/admin/bookings/{code}/finish` -> Chuyển sang `completed`, `PaidAmount` giữ nguyên `0`. | **PASS** |
| **`V4-A3`** | `V4_A3_StrictIdempotency_ReplaysIdenticalRequest_And_RejectsPayloadOrTargetMismatchWith409` | 1. Gửi lại cùng `IdempotencyKey` + cùng người gọi + cùng payload -> `200 OK` (`deduplicated = true`), không tạo bản ghi trùng.<br>2. Gửi lại cùng `IdempotencyKey` nhưng đổi khung giờ đặt sân -> `409 Conflict` (`IDEMPOTENCY_KEY_CONFLICT`).<br>3. Gửi lại cùng `IdempotencyKey` thanh toán nhưng đổi số tiền (`50.000đ -> 80.000đ`) hoặc đổi sang mã đặt sân khác -> `409 Conflict` (`IDEMPOTENCY_KEY_CONFLICT`). | **PASS** |
| **`V4-A4`** | `V4_A4_CancelBookingLinkedWithProShop_AtomicallyCancelsOrders_RestocksExactlyOnce_And_BlocksDeliveredOrPaid` | 1. Tồn kho ban đầu `25`. Tạo booking kèm đơn Pro Shop (`3` bóng) + thêm trực tiếp tại POS (`2` bóng) -> Tồn kho còn `20`.<br>2. Hủy booking -> Đơn Pro Shop chuyển sang `cancelled`, `PosTotal = 0`, `AmountDue = 0`, tồn kho hoàn trả chính xác về **`25`** (không bị hoàn kho kép lên `28`).<br>3. Đánh dấu đơn Pro Shop đã giao (`POST /api/admin/orders/{code}/fulfill`) -> Thử hủy booking bị chặn `409 Conflict` (`LINKED_ORDER_DELIVERED`). | **PASS** |
| **`V4-B1`** | `V4_B1_B2_B3_B4_CsrfRefresh_GuestRecovery_MustChangePassword_And_ETagConsistency` | 1. `GET /api/security/csrf` trả về cả `token` và `csrfToken` khớp nhau.<br>2. Gửi `POST /api/bookings` với CSRF sai -> Nhận `400 Bad Request` kèm JSON `code = "CSRF_TOKEN_INVALID"` và header `X-Csrf-Error: CSRF_TOKEN_INVALID`. | **PASS** |
| **`V4-B2`** | `V4_B1_B2_B3_B4_CsrfRefresh_GuestRecovery_MustChangePassword_And_ETagConsistency` | 1. Khách vãng lai tạo booking nhận `guestAccessToken` (`bk_...`) và cookie bảo mật `spike_guest_bk`.<br>2. Retry cùng `IdempotencyKey` trong cùng phiên -> Khôi phục đúng `guestAccessToken`.<br>3. Phiên trình duyệt khác gửi cùng `IdempotencyKey` -> Không nhận được `guestAccessToken`. | **PASS** |
| **`V4-B3`** | `V4_B1_B2_B3_B4_CsrfRefresh_GuestRecovery_MustChangePassword_And_ETagConsistency` | 1. Tài khoản có `MustChangePassword = true` truy cập `GET /Booking` -> Redirect `302` về `/Account/Profile?forceChange=1`.<br>2. Gọi `POST /api/bookings` -> Bị chặn `403 Forbidden` (`MUST_CHANGE_PASSWORD`).<br>3. Đổi mật khẩu qua `POST /api/auth/change-password` -> `MustChangePassword = false`, truy cập lại `GET /Booking` nhận `200 OK`. | **PASS** |
| **`V4-B4`** | `V4_B1_B2_B3_B4_CsrfRefresh_GuestRecovery_MustChangePassword_And_ETagConsistency` | 1. Gọi `GET /api/db/state` với `If-None-Match` khi dữ liệu không đổi -> Nhận `304 Not Modified`.<br>2. Cập nhật thông tin mặt sân (`TbCourt.Surface`) -> Gọi lại với ETag cũ nhận `200 OK` và ETag mới. | **PASS** |
| **`V4-B5`** | `HttpSecurityAndApiIntegrationTests.RateLimiting_CheckInEndpoint_Returns429WhenThresholdExceeded` | Partitioned `FixedWindowLimiter` theo `category|trustedClientIp|userId` trả về `429 Too Many Requests` kèm header `Retry-After: 60`. | **PASS** |
| **`V4-QR`** | `V4_QR_100PlusBenchmark_AllGeneratedQrVariantsDecode100PercentWithZXing_And_SavesQaArtifacts` | Sinh và giải mã độc lập bằng `ZXing.Net` trên **105 mẫu QR** (35 mẫu chuẩn 512px, 30 mẫu có logo tâm ECC-H 512px, 15 mẫu thu nhỏ mobile 220px, 15 mẫu nén JPEG Q80, 10 mẫu độ phân giải cao 1024px) -> **105/105 (100.0%) giải mã thành công và khớp chữ ký HMAC-SHA256**. | **PASS** |

---

## 3. Bằng chứng Benchmark 105 mẫu QR (`docs/qa/qr/`)

Các tệp mẫu và báo cáo JSON đã được lưu trữ trực tiếp tại [`docs/qa/qr/`](file:///e:/pickleball-ui/docs/qa/qr/):
- Báo cáo JSON: [`docs/qa/qr/qr-benchmark-report.json`](file:///e:/pickleball-ui/docs/qa/qr/qr-benchmark-report.json) (`105/105` mẫu đạt `100%`).
- Mẫu QR tiêu chuẩn (`ECC-M`, 512px): [`docs/qa/qr/sample-standard-512.png`](file:///e:/pickleball-ui/docs/qa/qr/sample-standard-512.png) & [`docs/qa/qr/sample-standard.svg`](file:///e:/pickleball-ui/docs/qa/qr/sample-standard.svg).
- Mẫu QR gắn Logo CLB ở tâm (`ECC-H`, 512px & 1024px): [`docs/qa/qr/sample-logo-512.png`](file:///e:/pickleball-ui/docs/qa/qr/sample-logo-512.png), [`docs/qa/qr/sample-logo-1024.png`](file:///e:/pickleball-ui/docs/qa/qr/sample-logo-1024.png) & [`docs/qa/qr/sample-logo.svg`](file:///e:/pickleball-ui/docs/qa/qr/sample-logo.svg).
