# KẾ HOẠCH TRIỂN KHAI MASTER PROMPT V4 (`V4_IMPLEMENTATION_PLAN.md`)

- **Dự án**: Spike Pickleball Club (`e:\pickleball-ui`)
- **Nhánh phát triển**: `fix/v4-release-blockers-and-qr`
- **Cơ sở dữ liệu**: SQL Server `.\SQLEXPRESS` (`PickleballClub` + các DB kiểm thử cô lập `PickleballClub_HttpTest_*` / `PickleballClub_SqlTest_*`)
- **Bản sao lưu an toàn trước khi nâng cấp**: `C:\Windows\Temp\PickleballClub_V4_Baseline.bak`

---

## 1. Thứ tự triển khai bắt buộc (Execution Order)

Toàn bộ hạng mục trong `Antigravity_Pickleball_V4_Release_Blockers_and_Branded_QR.md` được triển khai tuần tự theo đúng thứ tự phụ thuộc:

1. **Bước 0 — Baseline & Safety Backup**:
   - Chụp baseline `dotnet test` (`19/19 Passed`).
   - Sao lưu SQL Server database `PickleballClub` ra `C:\Windows\Temp\PickleballClub_V4_Baseline.bak`.
   - Khởi tạo nhánh `fix/v4-release-blockers-and-qr`.
2. **Bước 1 — `V4-A1`: Pending Booking Hold TTL + Anti-Abuse + Concurrency**:
   - Mở rộng `TbBooking` thêm `HoldExpiresAtUtc` (`DATETIME2 NULL`) và `ClientIpHash` (`NVARCHAR(64) NULL`).
   - Cấu hình `PendingHoldMinutes` (mặc định `30` phút) và `MaxActivePendingPerActor` (mặc định `3`) trong `ClubSettingsService` & `tb_CmsConfig`.
   - Tính `HoldExpiresAtUtc = min(utcNow + PendingHoldMinutes, slotStartUtc)` khi tạo đơn `pending`.
   - Thống nhất quy tắc chiếm chỗ giữa `CheckSlotConflictAsync`, `/api/db/state`, `ApproveBookingAsync`, `CreateCourtLockAsync`, `ExtendBookingDurationAsync` và `ReconcileBookingLifecycleAsync`: đơn `pending` chỉ giữ chỗ khi `HoldExpiresAtUtc > DateTime.UtcNow`. Khi quá hạn, slot được giải phóng ngay lập tức cho khách mới mà không cần chờ background worker.
3. **Bước 2 — `V4-A2`: Server-Authoritative Booking Lifecycle**:
   - Loại bỏ hoàn toàn lệnh tự gán `b.status = 'completed'` trên trình duyệt trong `reconcileBookingsByRealTime()` (`wwwroot/js/shared.js`).
   - Tách riêng thuộc tính hiển thị `b.derivedPhase = 'overdue' | 'live' | 'starting_soon' | 'expired_hold'` mà không ghi đè `b.status`.
   - Hiển thị nhãn `"Quá giờ — chờ kết ca"` cho các ca `playing` đã qua giờ kết thúc dự kiến cho đến khi nhân viên bấm **Đóng ca** (`FinishBookingSessionAsync`).
4. **Bước 3 — `V4-A3`: Strict Idempotency Contract**:
   - Tạo bảng `tb_IdempotencyRecord` (`Models/TbIdempotencyRecord.cs`) và `Services/IdempotencyService.cs`.
   - Ràng buộc khóa idempotency theo bộ `(Scope, ActorKey, TargetEntity, RequestHash, IdempotencyKey)` với băm chuẩn hóa SHA-256 (`RequestHash`).
   - Cùng `IdempotencyKey` + cùng `ActorKey` + cùng `TargetEntity` + cùng `RequestHash` -> trả lại kết quả gốc (`200 OK`, `deduplicated = true`), không ghi trùng DB.
   - Cùng `IdempotencyKey` nhưng khác người gọi, khác đối tượng đích, khác số tiền hoặc khác dữ liệu đầu vào -> trả về **`409 Conflict` (`code = "IDEMPOTENCY_KEY_CONFLICT"`)**.
5. **Bước 4 — `V4-A4`: Đồng bộ hủy Booking có gắn đơn Pro Shop**:
   - Mở rộng `TbOrder` thêm `FulfillmentStatus` (`pending`, `fulfilled`, `delivered`, `cancelled`).
   - Khi hủy booking có đơn `TbOrder` liên kết hoặc món POS:
     - Chặn hủy (`409 Conflict`) nếu đơn hàng liên kết đã giao (`LINKED_ORDER_DELIVERED`) hoặc đã có phát sinh thu tiền (`LINKED_ORDER_PAID` / `BOOKING_HAS_PAYMENT`).
     - Nếu chưa thu tiền và chưa giao hàng: trong một giao dịch `IsolationLevel.Serializable` duy nhất, hủy toàn bộ `TbOrder` liên kết, hoàn kho mỗi sản phẩm **đúng 1 lần** (tách biệt lượng hàng từ `TbOrderDetails` và lượng hàng POS trực tiếp trong `PosItemsJson`), ghi `TbInventoryMovement`, cập nhật `PosTotal = 0`, `AmountDue = 0` và ghi `TbAuditLog`.
6. **Bước 5 — `V4-B1..B5`: Khắc phục 5 lỗi Bảo mật & Vận hành**:
   - **`V4-B1`**: Đồng bộ `/api/security/csrf` trả về cả `token` và `csrfToken`; thêm `CsrfFailureResultFilter` trả về `400` kèm `code = "CSRF_TOKEN_INVALID"` và header `X-Csrf-Error`; cập nhật `apiFetch()` trong `shared.js` chỉ refresh CSRF và thử lại 1 lần khi đúng lỗi CSRF.
   - **`V4-B2`**: Lưu bằng chứng khôi phục `guestAccessToken` mã hóa bằng ASP.NET Core `IDataProtectionProvider` trong cookie `HttpOnly` `spike_guest_bk` (TTL 30 phút) để khách vãng lai retry cùng `IdempotencyKey` trong cùng phiên trình duyệt lấy lại được `guestAccessToken`, đồng thời chặn lộ token sang phiên khác.
   - **`V4-B3`**: Thêm middleware cưỡng bức `MustChangePassword` trên toàn bộ route ngoài allowlist (`403 MUST_CHANGE_PASSWORD` cho `/api/*`, redirect `302` về `/Account/Profile?forceChange=1` cho trang web) và thêm endpoint `/api/auth/change-password`.
   - **`V4-B4`**: Mở rộng hàm băm ETag của `GET /api/db/state` bao phủ toàn bộ trường dữ liệu trả về của `courts`, `scopedBookings`, `rawLocks`, `rawProducts`, `standaloneOrders`, `currentUserDto`, `settings` và `cmsConfig`.
   - **`V4-B5`**: Nâng cấp Rate Limiter sang `RateLimitPartition.GetFixedWindowLimiter` phân vùng theo `category|trustedClientIp|userId` kèm header `Retry-After: 60`.
7. **Bước 6 — `V4-QR`: Nâng cấp Premium Digital Ticket & Benchmark 105 mẫu QR**:
   - Nâng cấp `QrTokenService` hỗ trợ xuất SVG/PNG độ phân giải cao (`512px–1024px`), chế độ Logo tâm QR (`ECCLevel.H`, diện tích logo `<= 4%` vùng QR kèm đệm trắng quiet pad và cơ chế tự kiểm tra giải mã `ZXing.Net` — tự động fallback về QR không logo nếu giải mã lỗi).
   - Thiết kế lại giao diện vé điện tử `SpikeTicket` (`Light Premium` & `Dark Sport`, khung QR trắng tinh `#FFFFFF`, tải Vé PNG `1080×1520`, tải QR PNG/SVG, sao chép mã, phóng to độ tương phản tối đa, in vé A4/A6).
8. **Bước 7 — Kiểm thử Tích hợp, Benchmark QR & Nghiệm thu Tài liệu V4**:
   - Thực thi migration lên SQL Server `.\SQLEXPRESS`.
   - Chạy toàn bộ test suite (`25/25 Passed`) và benchmark `105/105` mẫu QR (`100%` decode pass rate).
   - Cập nhật đầy đủ 6 tài liệu V4 và `docs/REMEDIATION_MATRIX.md`.
