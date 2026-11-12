# KẾ HOẠCH BẢO MẬT, PHÒNG NGỪA LẠM DỤNG & PHƯƠNG ÁN ROLLBACK V4 (`V4_SECURITY_AND_ROLLBACK.md`)

Tài liệu này mô tả chi tiết cấu hình bảo mật, cơ chế chống lạm dụng (anti-abuse) và phương án khôi phục (rollback) của phiên bản **Master Prompt V4**.

---

## 1. Các lớp bảo vệ & Kiểm soát truy cập mới trong V4

### 1.1. Giữ chỗ tạm thời có thời hạn & Chống chiếm dụng sân (`V4-A1`)
- **TTL tự động**: Mỗi đơn `pending` được gán `HoldExpiresAtUtc = min(utcNow + PendingHoldMinutes, slotStartUtc)` (mặc định `30` phút).
- **Giải phóng tức thời**: Mọi truy vấn khả dụng (`CheckSlotConflictAsync`, `/api/db/state`) và thao tác duyệt vé (`ApproveBookingAsync`) chỉ coi đơn `pending` còn hiệu lực khi `HoldExpiresAtUtc > DateTime.UtcNow`. Ngay khi quá hạn, slot được giải phóng cho người đặt mới mà không cần chờ tiến trình nền.
- **Giới hạn theo Actor (`MaxActivePendingPerActor`)**: Mỗi tài khoản hội viên, số điện thoại hoặc địa chỉ IP băm (`ClientIpHash`) chỉ được giữ tối đa `3` đơn `pending` đang hoạt động cùng lúc. Yêu cầu vượt ngưỡng bị từ chối với mã `429 Too Many Requests` (`code = "PENDING_HOLD_LIMIT_EXCEEDED"`).

### 1.2. Hợp đồng Idempotency chống phát lại chéo (`V4-A3`)
- Bảng `tb_IdempotencyRecord` lưu khóa định danh duy nhất `UX_tb_IdempotencyRecord_Key` kèm:
  - `Scope` (`BOOKING_CREATE`, `BOOKING_CANCEL`, `BOOKING_EXTEND`, `BOOKING_PAYMENT`, `BOOKING_REFUND`, `ORDER_CREATE`, `ORDER_CANCEL`, `ORDER_PAYMENT`, `ORDER_REFUND`)
  - `ActorKey` (`acc:{AccountId}` hoặc `guest:{Phone}` / `ip:{IpHash}`)
  - `TargetEntity` (`BookingCode` hoặc `OrderCode`)
  - `RequestHash` (SHA-256 của dữ liệu chuẩn hóa gồm cả số tiền, khung giờ, danh sách sản phẩm)
- Mọi hành vi tái sử dụng `IdempotencyKey` cho người dùng khác, đối tượng đích khác hoặc số tiền/payload khác đều bị chặn đứng bằng **`409 Conflict` (`code = "IDEMPOTENCY_KEY_CONFLICT"`)**.

### 1.3. Bảo vệ CSRF và Khôi phục Token Khách vãng lai (`V4-B1`, `V4-B2`)
- **CSRF Contract (`V4-B1`)**:
  - `GET /api/security/csrf` trả về `{ ok: true, token: "...", csrfToken: "..." }` (`Cache-Control: no-store`).
  - `CsrfFailureResultFilter` chuẩn hóa mọi lỗi antiforgery thành HTTP `400` kèm `code = "CSRF_TOKEN_INVALID"` và header `X-Csrf-Error: CSRF_TOKEN_INVALID`.
  - Hàm `apiFetch()` trong `wwwroot/js/shared.js` chỉ làm mới token và gửi lại 1 lần khi xác nhận đúng lỗi `CSRF_TOKEN_INVALID`.
- **Khôi phục `guestAccessToken` an toàn (`V4-B2`)**:
  - Khi khách vãng lai đặt sân thành công, server mã hóa `{ BookingCode, GuestAccessToken, IdempotencyKey, ExpiresUtc }` bằng `IDataProtectionProvider` (`Purpose = "Spike.GuestBookingRecovery.v4"`) và đặt vào cookie `HttpOnly`, `SameSite=Lax` tên `spike_guest_bk` (TTL 30 phút).
  - Chỉ phiên trình duyệt sở hữu cookie hợp lệ và gửi lại đúng `IdempotencyKey` mới nhận lại được `guestAccessToken` khi mạng chập chờn; mọi phiên khác hoàn toàn không thể đọc được token.

### 1.4. Cưỡng bức đổi mật khẩu tạm thời (`V4-B3`) & Rate Limiting phân vùng (`V4-B5`)
- **Middleware `MustChangePassword` (`V4-B3`)**:
  - Tài khoản có `MustChangePassword == true` bị chặn ở tầng middleware trước khi vào bất kỳ trang hoặc API nghiệp vụ nào ngoài danh sách cho phép (`/Account/Profile`, `/Account/ChangePassword`, `/Account/Logout`, `/api/auth/change-password`, `/api/auth/logout`, `/api/security/csrf`, `GET /api/db/state`).
- **Partitioned Rate Limiting (`V4-B5`)**:
  - Các chính sách `AuthAndLookup`, `CheckInLimiter`, `BookingCreateLimiter` sử dụng `RateLimitPartition.GetFixedWindowLimiter` phân tách theo `category|trustedClientIp|userId`.
  - Chỉ tin cậy `X-Forwarded-For` khi `RemoteIpAddress` là loopback proxy (`127.0.0.1` / `::1`), ngăn chặn giả mạo header để né giới hạn tần suất.

---

## 2. Phương án Rollback (Khôi phục an toàn)

### 2.1. Rollback mã nguồn (Application Rollback)
- Toàn bộ thay đổi V4 được cô lập trên nhánh `fix/v4-release-blockers-and-qr`.
- Các cột mới (`HoldExpiresAtUtc`, `ClientIpHash` trên `tb_Booking`; `FulfillmentStatus` trên `tb_Order`) và bảng mới (`tb_IdempotencyRecord`) đều tương thích ngược (additive schema) — không xóa hay đổi tên bất kỳ cột hiện có nào.

### 2.2. Rollback Migration EF Core
Nếu cần hoàn tác schema về trạng thái trước V4 (`20261108174500_V2_FullEnterpriseSchemaAndIndexes`):
```powershell
dotnet ef database update 20261108174500_V2_FullEnterpriseSchemaAndIndexes --project PickleballClub.csproj
```
Phương thức `Down()` trong `Migrations/20261109123000_V4_HoldTtlIdempotencyAndOrderFulfillment.cs` sẽ dọn dẹp an toàn bảng `tb_IdempotencyRecord`, index `IX_tb_Booking_PendingHold` và các cột mở rộng V4.

### 2.3. Khôi phục từ bản sao lưu SQL Server đầy đủ
Trước khi chạy migration V4, bản sao lưu toàn phần của database đã được lưu tại `C:\Windows\Temp\PickleballClub_V4_Baseline.bak`. Có thể khôi phục tức thì bằng lệnh:
```sql
USE master;
ALTER DATABASE [PickleballClub] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
RESTORE DATABASE [PickleballClub] FROM DISK = N'C:\Windows\Temp\PickleballClub_V4_Baseline.bak' WITH REPLACE;
ALTER DATABASE [PickleballClub] SET MULTI_USER;
```
