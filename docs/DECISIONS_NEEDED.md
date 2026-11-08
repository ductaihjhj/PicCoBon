# Các Quyết Định Vận Hành & Tích Hợp Bên Ngoài Cần Chủ Đầu Tư Phê Duyệt (`DECISIONS_NEEDED.md`)

Tài liệu này liệt kê minh bạch các hạng mục phụ thuộc vào tài khoản nhà cung cấp bên thứ ba hoặc chính sách vận hành thực tế của Câu lạc bộ mà hệ thống **tuyệt đối không giả lập (no fake / no mock)** theo chuẩn nghiệm thu V2.

---

## 1. Hạng Mục Đang Chờ Tài Khoản Bên Ngoài (`BLOCKED_EXTERNAL`)

### `P1-02` — Cổng Gửi OTP Tự Động (SMS Brandname / Zalo ZNS / Transactional SMTP) Cho Luồng Quên Mật Khẩu
- **Trạng thái hiện tại**: `BLOCKED_EXTERNAL` (đã khóa an toàn luồng giả lập).
- **Hành vi hiện tại trên hệ thống**:
  - Endpoint `POST /api/auth/forgot-password` trả về mã chuẩn `HTTP 503 Service Unavailable` (`code: "OTP_PROVIDER_NOT_CONFIGURED"`), tuyệt đối không tự đặt lại mật khẩu hay báo "đã gửi OTP thành công" giả.
  - Giao diện `/Account/Login` hiển thị thông báo minh bạch hướng dẫn hội viên liên hệ Quầy lễ tân / Hotline CLB để xác minh chính chủ và nhận mật khẩu tạm thời dùng 1 lần (`MustChangePassword = true`) do Admin cấp.
- **Quyết định cần Chủ CLB cung cấp khi muốn bật tự động**:
  1. Chọn nhà cung cấp: **Zalo ZNS**, **SMS Brandname** (Viettel/VNPT/eSMS), hoặc **Transactional Email** (Amazon SES / SendGrid / Postmark).
  2. Cung cấp API Key / Sender ID / Template ID thực tế qua biến môi trường máy chủ.

---

## 2. Tùy Chọn Mở Rộng Cổng Thanh Toán Trực Tuyến Tự Động (Nếu Có Nhu Cầu)

- **Trạng thái hiện tại**: Sổ cái thu ngân (`TbPaymentTransaction`) cho **Tiền mặt (`cash`)** và **Chuyển khoản ngân hàng có đối soát mã tham chiếu (`transfer` + `ReferenceCode`)** đã hoàn thiện `100%` (`P1-04`, `P1-05`, `P1-06`). Hệ thống không tự động cộng doanh thu khi chưa có xác nhận thu tiền thật.
- **Nếu CLB muốn tự động gạch nợ tức thì không cần thu ngân đối soát tay**:
  1. Đăng ký tài khoản Merchant **VNPAY** (`VnPay:TmnCode`, `VnPay:HashSecret` + URL Return/IPN công khai HTTPS) hoặc **Casso / SePay VietQR Webhook**.
  2. Cấu hình khóa bí mật qua biến môi trường để đối chiếu chữ ký HMAC-SHA512 và ghi tự động vào `TbPaymentTransaction` với `IdempotencyKey = BankTransactionId`.

---

## 3. Chính Sách Nghiệp Vụ Đã Áp Dụng Mặc Định (Có Thể Tinh Chỉnh Qua CMS `/Cms`)

Các thông số dưới đây đang áp dụng giá trị mặc định hợp lý và **Admin có thể chỉnh trực tiếp trên trang `/Cms` mà không cần sửa code**:

| Tham số CMS | Giá trị mặc định | Ý nghĩa nghiệp vụ |
|---|---|---|
| `openTime` / `closeTime` | `06:00` – `22:00` | Khung giờ mở/đóng cửa sân áp dụng đồng bộ trên UI và API. |
| `peakStart` | `17:00` | Mốc bắt đầu tính phụ thu khung giờ vàng (tính tách đoạn chính xác từng phút). |
| `courtPrice` | `120.000đ / giờ` | Giá cơ sở giờ thường (cộng thêm phụ thu sân VIP nếu có). |
| `peakSurcharge` | `30.000đ / giờ` | Phụ thu cộng thêm cho phần thời lượng rơi vào khung giờ vàng. |
| `checkInEarlyMinutes` | `30 phút` | Thời gian tối đa cho phép quét QR Check-in sớm trước giờ bắt đầu ca. |
| `cancelCutoffMinutes` | `120 phút` | Thời hạn tối thiểu trước giờ thi đấu để khách tự hủy vé đã duyệt trên `/Lookup`. |

---

## 4. Checklist Hạ Tầng Trước Khi Go-Live Môi Trường Thật

1. Sinh khóa `SPIKE_QR_SIGNING_KEY` ngẫu nhiên tối thiểu 32–64 ký tự trên máy chủ Production.
2. Đổi mật khẩu toàn bộ tài khoản quản trị (`Admin`) và nhân viên thu ngân (`Staff`) ngay lần đăng nhập đầu tiên.
3. Cấu hình chứng chỉ HTTPS hợp lệ trên Reverse Proxy (IIS / Nginx / Cloudflare).
4. Thiết lập lịch Windows Task Scheduler hoặc SQL Agent chạy sao lưu `.bak` hàng đêm sang ổ đĩa độc lập / Cloud Storage theo mẫu lệnh trong `docs/DEPLOY_AND_ROLLBACK.md`.
