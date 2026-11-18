# THIẾT KẾ VÉ ĐIỆN TỬ & MÃ QR ĐỊNH DANH THƯƠNG HIỆU V4 (`V4_QR_DESIGN.md`)

Tài liệu này mô tả kiến trúc kỹ thuật và quy chuẩn thị giác của hệ thống **Premium Digital Court Ticket (`V4-QR`)** tại Spike Pickleball Club.

---

## 1. Nguyên tắc cốt lõi: Không đánh đổi khả năng quét lấy thẩm mỹ

Mọi mã QR xuất ra từ hệ thống tuân thủ nghiêm ngặt tiêu chuẩn **ISO/IEC 18004**:

1. **Khung QR (`QR Well`) luôn trắng tinh `#FFFFFF`**:
   - Cho dù người dùng chọn giao diện thẻ ngoài là **Light Premium** (`#FFFFFF`) hay **Dark Sport** (`#0F172A`), vùng chứa mã QR bên trong luôn cố định nền `#FFFFFF` và module tối màu `#0F172A` (tỷ lệ tương phản `> 17:1`, vượt xa ngưỡng tối thiểu `4.5:1`).
   - Tuyệt đối không dùng QR đảo màu (module sáng trên nền tối) hoặc đổ gradient làm giảm biên độ nhận diện của cảm biến camera.
2. **Vùng yên tĩnh (`Quiet Zone`) tối thiểu 4 modules**:
   - Cả SVG và PNG đều bật `drawQuietZones: true` trong `QRCoder`, cộng thêm lớp đệm `10px–16px` của `.spk-qr-well`.
3. **Chế độ Logo CLB ở tâm QR (`includeLogo = true`) an toàn tuyệt đối**:
   - Khi tắt logo (mặc định): sử dụng mức sửa lỗi `ECCLevel.M` (15% redundancy) giúp ma trận thoáng, dễ quét ở kích thước nhỏ.
   - Khi bật logo: tự động nâng lên mức sửa lỗi cao nhất **`ECCLevel.H` (30% Reed-Solomon redundancy)**.
   - Kích thước biểu tượng trung tâm được khóa ở mức `18%` chiều rộng với PNG (`3.24%` diện tích mã QR) và `20%` chiều rộng với SVG (`4.0%` diện tích mã QR) — thấp hơn nhiều so với giới hạn an toàn `<= 10%` diện tích.
   - **Cơ chế tự kiểm chứng (`Self-Verification Fallback`)**: Ngay sau khi vẽ biểu tượng vào tâm ảnh QR, `QrTokenService` gọi trực tiếp bộ giải mã độc lập `ZXing.Net` (`TryDecodeQrImage`). Nếu giải mã không khớp 100% với chuỗi token gốc, hệ thống tự động trả về bản QR tiêu chuẩn không logo.

---

## 2. Cấu trúc Payload Bảo mật (Zero-PII Signed Token)

Nội dung bên trong mã QR không chứa họ tên hay số điện thoại khách hàng để bảo vệ quyền riêng tư khi người chơi chia sẻ ảnh vé lên mạng xã hội:

```text
SPK1.{Base64Url(BookingCode|BookingDate|ExpiresUnixUtc|RandomNonce)}.{Base64Url(HMAC_SHA256_Signature)}
```

- **Chống giả mạo**: Chữ ký HMAC-SHA256 (khóa tối thiểu 32 ký tự từ `Security:QrSigningKey` / `SPIKE_QR_SIGNING_KEY`) được kiểm tra bằng `CryptographicOperations.FixedTimeEquals`.
- **Kiểm soát quyền truy cập**: Các endpoint `/api/bookings/{code}/qr.svg` và `/api/bookings/{code}/qr.png` yêu cầu người gọi phải là chủ vé (đã đăng nhập đúng `AccountId` hoặc cung cấp đúng `accessToken` / `X-Booking-Access-Token`) hoặc nhân viên (`Staff`/`Admin`).

---

## 3. Giao diện Thẻ Vé Điện Tử (`window.SpikeTicket`)

Thẻ vé điện tử được tích hợp đồng bộ tại Modal Đặt sân thành công (`/Booking`) và Modal Xem vé (`/Lookup`):

- **Header Thương hiệu**: Biểu tượng 🏓, tên câu lạc bộ `SPIKE PICKLEBALL CLUB`, dòng phụ đề `OFFICIAL DIGITAL COURT PASS` và huy hiệu trạng thái thời gian thực (`Chờ duyệt`, `Chờ check-in`, `Đang thi đấu`, `Quá giờ — chờ kết ca`, `Hoàn thành`).
- **Thông tin Đặt sân 2 cột**:
  - Sân thi đấu & loại sân (`Indoor` / `Outdoor`, số lượng VĐV).
  - Nút pill **Mã đặt sân** (`PB-XXXXXX`) cho phép sao chép 1 chạm.
  - Ngày thi đấu, Khung giờ (`HH:mm – HH:mm`), Người đặt sân, Tổng thanh toán.
  - Hộp hiển thị **Mã bảo mật vé vãng lai (`bk_...`)** kèm nút sao chép nhanh (khi đặt sân không đăng nhập).
- **Đường cắt vé (`Perforation Line`)**: Đường kẻ đứt nét kèm hai khuyết tròn mô phỏng vé thể thao chuyên nghiệp.
- **Thanh công cụ tương tác (`Action Toolbar`)**:
  1. **Đổi giao diện**: Chuyển đổi tức thì giữa `☀️ Light Premium` và `🌙 Dark Sport`.
  2. **Bật/Tắt Logo tâm QR**: Chuyển đổi giữa QR tiêu chuẩn (`ECC-M`) và QR gắn biểu tượng `SPK` (`ECC-H`).
  3. **Tải Vé PNG (`1080×1520`)**: Xuất toàn bộ thẻ vé độ phân giải cao bằng HTML5 Canvas, nhúng trực tiếp ảnh QR sắc nét (`imageSmoothingEnabled = false`) để khách lưu vào thư viện ảnh điện thoại và xuất trình tại cổng.
  4. **Tải QR PNG (`1024×1024`) & SVG Vector**: Phục vụ in ấn hoặc chia sẻ nhanh.
  5. **Phóng to độ tương phản tối đa**: Mở hộp thoại toàn màn hình nền trắng tinh `#FFFFFF` viền `#0F172A` giúp máy quét tại quầy đọc mã ngay lập tức.
  6. **In vé (`@media print`)**: Định dạng in gọn gàng trên khổ A4/A6, tự động ẩn các nút điều khiển.
