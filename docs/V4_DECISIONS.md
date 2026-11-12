# NHẬT KÝ QUYẾT ĐỊNH KIẾN TRÚC & KỸ THUẬT V4 (`V4_DECISIONS.md`)

Tài liệu này ghi lại các quyết định thiết kế trọng yếu trong đợt phát hành **Master Prompt V4** nhằm giải quyết triệt để 4 Release Blockers (`V4-A1..A4`), 5 hạng mục bảo mật/vận hành (`V4-B1..B5`) và hệ thống **Premium Digital Ticket (`V4-QR`)**.

---

## 1. Quyết định `V4-A1`: Giải phóng tức thời Slot `pending` hết hạn ngay trong câu truy vấn xung đột

- **Vấn đề**: Nếu chỉ dựa vào tiến trình nền (`BookingLifecycleHostedService` chạy định kỳ mỗi 60 giây) để đổi trạng thái `pending -> expired`, thì trong khoảng trễ giữa hai lần quét, khách hàng mới vẫn bị báo trùng lịch giả hoặc nhân viên vẫn có thể bấm duyệt nhầm đơn đã hết hạn giữ chỗ.
- **Quyết định**:
  1. Lưu cột `HoldExpiresAtUtc` (`DATETIME2 NULL`, múi giờ UTC) trực tiếp trên `tb_Booking` với công thức:
     $$\text{HoldExpiresAtUtc} = \min(\text{utcNow} + \text{PendingHoldMinutes}, \text{slotStartUtc})$$
  2. Trong `CheckSlotConflictAsync` (chạy bên trong giao dịch `IsolationLevel.Serializable` với khóa `UPDLOCK, HOLDLOCK`), nếu phát hiện bản ghi `pending` có `HoldExpiresAtUtc <= utcNow`, hệ thống **chuyển trạng thái bản ghi đó sang `expired` ngay trong giao dịch hiện hành** và không tính là xung đột.
  3. Trong `ApproveBookingAsync`, kiểm tra `HoldExpiresAtUtc <= utcNow` ngay trong giao dịch `Serializable`; nếu đã quá hạn thì cập nhật `Status = "expired"`, lưu DB và trả về `409 Conflict` (`code = "PENDING_HOLD_EXPIRED"`).

---

## 2. Quyết định `V4-A2`: Tách biệt trạng thái nghiệp vụ (`status`) và pha hiển thị thời gian thực (`derivedPhase`)

- **Vấn đề**: Ở phiên bản trước, hàm `reconcileBookingsByRealTime()` trong `wwwroot/js/shared.js` tự động gán `b.status = 'completed'` khi đồng hồ máy khách vượt quá `endTime`. Khi server trả về `304 Not Modified`, trạng thái giả `'completed'` này tồn tại trên UI khiến nhân viên tưởng ca đã đóng và bỏ sót thu tiền/đóng ca trên SQL Server.
- **Quyết định**:
  - Tuyệt đối không bao giờ sửa `b.status` ở phía client.
  - Tính toán trường phụ `b.derivedPhase` (`'overdue'`, `'live'`, `'starting_soon'`, `'expired_hold'`).
  - Khi `b.status === 'playing'` và `b.derivedPhase === 'overdue'`, hàm `statusLabel(b.status, b)` hiển thị `"Quá giờ — chờ kết ca"` với badge màu cảnh báo (`badge-warning`), giữ nguyên các nút thao tác **Thu tiền** và **Đóng ca** cho nhân viên.

---

## 3. Quyết định `V4-A3`: Hợp đồng Idempotency định danh theo `(Scope, ActorKey, TargetEntity, RequestHash, IdempotencyKey)`

- **Vấn đề**: Nếu chỉ tra cứu `IdempotencyKey` đơn thuần, một client gửi lại cùng key cho một booking khác hoặc số tiền khác sẽ nhận lại kết quả của giao dịch cũ (silent mis-replay).
- **Quyết định**:
  - Thiết kế bảng `tb_IdempotencyRecord` lưu `Scope`, `ActorKey`, `TargetEntity` và `RequestHash` (SHA-256 của chuỗi canonical request).
  - Khi gặp lại `IdempotencyKey` đã tồn tại:
    - Nếu khớp toàn bộ `(Scope, ActorKey, TargetEntity, RequestHash)` -> trả về kết quả gốc (`200 OK`, `deduplicated = true`).
    - Nếu lệch bất kỳ thành phần nào -> trả về **`409 Conflict` (`code = "IDEMPOTENCY_KEY_CONFLICT"`)**.

---

## 4. Quyết định `V4-A4`: Ngăn chặn hoàn kho kép (Double-Restock) khi hủy Booking kèm đơn Pro Shop

- **Vấn đề**: Khi khách đặt Pro Shop gắn vào mã đặt sân (`CreateOrderAsync`), hệ thống vừa lưu chi tiết trong `TbOrderDetails` vừa gộp món vào `linkedBooking.PosItemsJson` để hiển thị tổng bill tại sân. Nếu khi hủy booking vừa hoàn kho theo `TbOrderDetails` vừa hoàn kho toàn bộ `PosItemsJson`, tồn kho sẽ bị cộng trả 2 lần.
- **Quyết định**:
  1. Gắn thẻ `OrderId` và `OrderCode` lên từng phần tử `PosCartItemDto` được sinh từ `CreateOrderAsync`.
  2. Khi hủy booking trong `CancelBookingAsync` (`IsolationLevel.Serializable`):
     - Hoàn kho từ `TbOrderDetails` của các đơn `TbOrder` liên kết chưa hủy.
     - Đối với `PosItemsJson`, chỉ hoàn kho các món POS trực tiếp tại quầy (`OrderId == null && string.IsNullOrEmpty(OrderCode)`), đồng thời trừ đi mọi số lượng trùng lắp của các đơn cũ chưa gắn thẻ.
     - Nhờ đó mỗi đơn vị hàng hóa chỉ được hoàn kho **đúng 1 lần duy nhất**, kèm bản ghi `TbInventoryMovement` (`Reason = "BOOKING_ORDER_CANCEL_RESTOCK"` hoặc `"BOOKING_POS_CANCEL_RESTOCK"`).

---

## 5. Quyết định `V4-QR`: Tự kiểm chứng giải mã ZXing trước khi xuất QR có Logo

- **Vấn đề**: Đặt logo ở tâm mã QR nếu vượt diện tích an toàn hoặc thiếu vùng đệm trắng sẽ làm hỏng các module dữ liệu trung tâm, gây lỗi quét trên điện thoại tầm trung.
- **Quyết định**:
  - Chế độ mặc định (`logo = false`) dùng `ECCLevel.M` không logo để có mật độ module thoáng nhất.
  - Khi người dùng bật `logo = true`, `QrTokenService` tự động nâng mức sửa lỗi lên `ECCLevel.H` (khả năng khôi phục 30%), giới hạn biểu tượng trung tâm ở mức `18%–20%` chiều rộng (`3.24%–4.0%` diện tích toàn mã QR, thấp hơn nhiều so với ngưỡng tối đa `10%`), bao quanh bằng viền trắng tinh `#FFFFFF`.
  - Trước khi trả ảnh/SVG có logo về cho client, `QrTokenService` gọi trực tiếp `TryDecodeQrImage` (`ZXing.Net`) để tự kiểm tra. Nếu vì bất kỳ lý do nào `ZXing.Net` không giải mã khớp chuỗi token gốc, dịch vụ tự động hạ cấp an toàn về bản QR tiêu chuẩn không logo.
