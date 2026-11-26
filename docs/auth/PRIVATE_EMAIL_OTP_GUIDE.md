# Hướng Dẫn Vận Hành OTP Đăng Ký Qua Namecheap Private Email — Spike OS

Tài liệu này hướng dẫn cấu hình hộp thư **Namecheap Private Email** (`admin@spikepickleball.club`), thiết lập biến môi trường bảo mật, chạy migration cơ sở dữ liệu và kiểm tra giao thư thực tế trên VPS cho luồng **Xác minh OTP trước khi tạo tài khoản hội viên**.

---

## 1. Bật SMTP & Tạo Application Password Trên Private Email

1. Đăng nhập hộp thư quản trị `admin@spikepickleball.club` trên giao diện web Namecheap Private Email (`https://privateemail.com`).
2. Vào **Settings → Connect third-party apps** và bật quyền kết nối ứng dụng ngoài qua giao thức **SMTP**.
3. Mở **Security Center → App passwords**:
   - Tạo mật khẩu ứng dụng riêng đặt tên `Spike OS Registration OTP`.
   - Sao chép mật khẩu vừa sinh (chỉ hiển thị một lần) để nạp vào User Secrets (máy phát triển) hoặc file biến môi trường bảo vệ ngoài repo trên VPS.
   - **Không** dùng mật khẩu đăng nhập webmail chính và **không** đưa mật khẩu vào `appsettings*.json` hay Git.

---

## 2. Tham Số Cấu Hình & Khóa Bảo Mật

| Biến môi trường | Giá trị chuẩn cho `admin@spikepickleball.club` | Ghi chú |
| --- | --- | --- |
| `EmailSmtp__Host` | `mail.privateemail.com` | Máy chủ SMTP Namecheap Private Email |
| `EmailSmtp__Port` | `587` *(hoặc `465`)* | Mặc định `587` dùng `StartTls`; nếu hạ tầng mạng chặn cổng `587`, đổi sang `465` kèm `SslOnConnect` |
| `EmailSmtp__Security` | `StartTls` *(hoặc `SslOnConnect`)* | Bắt buộc mã hóa TLS và giữ nguyên kiểm tra chứng chỉ số |
| `EmailSmtp__Username` | `admin@spikepickleball.club` | Địa chỉ đầy đủ của hộp thư xác thực |
| `EmailSmtp__Password` | `<PRIVATE_EMAIL_APP_PASSWORD>` | Application Password tạo riêng cho Spike OS |
| `EmailSmtp__FromAddress` | `admin@spikepickleball.club` | Bắt buộc trùng với `EmailSmtp__Username` |
| `EmailSmtp__FromName` | `Spike Pickleball Club` | Tên người gửi hiển thị trong hộp thư khách |
| `EmailSmtp__TimeoutSeconds` | `15` | Giới hạn thời gian chờ mỗi lần gửi (3–60 giây) |
| `RegistrationOtp__HmacKey` | `<RANDOM_BASE64_MIN_32_BYTES>` | Khóa HMAC riêng cho OTP đăng ký, tối thiểu 32 byte, tách biệt với `SPIKE_QR_SIGNING_KEY` |

### 2.1 Thiết lập khi phát triển nội bộ (.NET User Secrets)

Sinh khóa `RegistrationOtp:HmacKey` ngẫu nhiên 32 byte và nạp vào User Secrets tại thư mục gốc repo:

```powershell
$otpKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
dotnet user-secrets set "EmailSmtp:Username" "admin@spikepickleball.club"
dotnet user-secrets set "EmailSmtp:FromAddress" "admin@spikepickleball.club"
dotnet user-secrets set "EmailSmtp:Password" "<APP_PASSWORD_TU_PRIVATE_EMAIL>"
dotnet user-secrets set "RegistrationOtp:HmacKey" $otpKey
```

### 2.2 Thiết lập trên VPS Linux (`systemd` EnvironmentFile ngoài repo)

Lưu biến bí mật tại file ngoài thư mục mã nguồn (ví dụ `/etc/spike-os/spike.env`, quyền `0600`, chủ sở hữu `root:root`):

```ini
EmailSmtp__Host=mail.privateemail.com
EmailSmtp__Port=587
EmailSmtp__Security=StartTls
EmailSmtp__Username=admin@spikepickleball.club
EmailSmtp__Password=<APP_PASSWORD_TU_PRIVATE_EMAIL>
EmailSmtp__FromAddress=admin@spikepickleball.club
EmailSmtp__FromName=Spike Pickleball Club
RegistrationOtp__HmacKey=<CHUOI_BASE64_32_BYTE_SINH_TU_OPENSSL_RAND>
```

Sinh khóa ngẫu nhiên trực tiếp trên VPS để điền vào `/etc/spike-os/spike.env`:

```bash
openssl rand -base64 32
```

Trong unit `systemd` (ví dụ `/etc/systemd/system/spike-os.service`), đảm bảo dòng nạp biến môi trường:

```ini
EnvironmentFile=/etc/spike-os/spike.env
```

---

## 3. Chạy Migration Bổ Sung Trên SQL Server

Bản migration bổ sung là **`20261126080000_V7_RegistrationEmailOtp`**:
- Mở rộng không phá hủy cột `EmailVerifiedAtUtc DATETIME2 NULL` trên `tb_Account` (toàn bộ tài khoản hiện có giữ nguyên dữ liệu và `EmailVerifiedAtUtc = NULL`).
- Tạo bảng `tb_RegistrationVerification` (lưu yêu cầu tạm, mật khẩu băm và HMAC của OTP, tự động xóa thông tin nhạy cảm sau khi xác minh hoặc hết hạn).
- Tạo bảng `tb_RegistrationEmailLog` (theo dõi hạn mức cộng dồn theo phiên, email, IP và tổng hạn mức gửi của hộp thư mà không lưu thông tin định danh thô).

Sau khi sao lưu DB theo quy trình `docs/DEPLOY_AND_ROLLBACK.md`, chạy migration tường minh:

```bash
dotnet /opt/spike-os/PickleballClub.dll --migrate
```

---

## 4. Kiểm Tra Kết Nối TLS Tới Private Email Từ VPS

Trước khi mở lưu lượng người dùng, kiểm tra kết nối mạng ra ngoài và chứng chỉ TLS từ VPS tới `mail.privateemail.com`:

### Cổng 587 (`STARTTLS` — khuyến nghị mặc định)
```bash
openssl s_client -starttls smtp -connect mail.privateemail.com:587 -servername mail.privateemail.com -crlf </dev/null
```

### Cổng 465 (`SslOnConnect` — phương án dự phòng khi nhà cung cấp VPS chặn cổng 587)
```bash
openssl s_client -connect mail.privateemail.com:465 -servername mail.privateemail.com -crlf </dev/null
```

Kết quả hợp lệ phải trả về `Verify return code: 0 (ok)`. Nếu VPS chỉ thông cổng `465`, cập nhật `EmailSmtp__Port=465` và `EmailSmtp__Security=SslOnConnect` trong `/etc/spike-os/spike.env` rồi `systemctl restart spike-os`.

---

## 5. Thử Nghiệm Đăng Ký Thật Sau Triển Khai

1. Mở `/Account/Login?mode=register` trên trình duyệt ẩn danh.
2. Điền thông tin đăng ký với một hộp thư cá nhân do chủ dự án kiểm soát và bấm **Gửi mã xác minh**.
3. Kiểm tra hộp thư đến:
   - Người gửi: `Spike Pickleball Club <admin@spikepickleball.club>`.
   - Tiêu đề: `Mã xác minh đăng ký Spike Pickleball Club` (không chứa mã OTP trên tiêu đề).
   - Nội dung chứa mã 6 chữ số có hiệu lực 10 phút.
4. Dán mã 6 chữ số vào ô xác minh:
   - Màn hình hiển thị **Đăng ký thành công** và nút **Đăng nhập**.
   - Bấm **Đăng nhập** và xác nhận đăng nhập thành công với vai trò Hội viên (`Customer`, `EmailVerifiedAtUtc` khác `NULL`).
