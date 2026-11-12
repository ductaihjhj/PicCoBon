# Hướng Dẫn Triển Khai, Sao Lưu & Rollback An Toàn (V2)

Tài liệu này quy định quy trình vận hành chuẩn (SOP) để triển khai, nâng cấp schema cơ sở dữ liệu, sao lưu (Backup), kiểm tra khôi phục (Restore Verification) và quay lui (Rollback) cho hệ thống **Spike Pickleball Club** (`.NET 10` + `EF Core 10` + `SQL Server`).

---

## 1. Điều Kiện Tiên Quyết & Cấu Hình Bảo Mật Bắt Buộc

Trước khi khởi chạy ở môi trường `Staging` hoặc `Production`, **bắt buộc** thiết lập đầy đủ các biến môi trường sau (không lưu khóa bí mật thật vào `appsettings.json` trên Git):

| Biến môi trường / Khóa cấu hình | Yêu cầu bắt buộc | Ghi chú |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` (hoặc `Staging`) | Kích hoạt `CookieSecurePolicy.Always`, `HSTS`, ẩn stack trace và tắt auto-migrate khi boot. |
| `ConnectionStrings__DefaultConnection` | Chuỗi kết nối SQL Server tối thiểu quyền (`db_datareader`, `db_datawriter`, quyền chạy `--migrate` riêng khi nâng cấp) | Không công khai qua bất kỳ API nào. |
| `SPIKE_QR_SIGNING_KEY` *(hoặc `Security__QrSigningKey`)* | Chuỗi ngẫu nhiên **tối thiểu 32 ký tự**, độ hỗn loạn cao (ví dụ 64 ký tự hex/base64) | Hệ thống **Fail-Fast** từ chối khởi động ở `Production` nếu thiếu, ngắn hơn 32 ký tự hoặc chứa giá trị mẫu (`CHANGE_ME`, `DevOnly`). |
| Reverse Proxy TLS (`HTTPS`) | IIS / Nginx / Caddy / Cloudflare Tunnel cấu hình chứng chỉ TLS hợp lệ | Cookie `spike_auth`, `spike_csrf`, `pickleball_session` bật cờ `Secure; HttpOnly; SameSite=Lax`. |

> **Cảnh báo an toàn dữ liệu (`P2-02`, `P2-10`)**:
> Tuyệt đối **KHÔNG** chạy tệp `scripts/demo-only/Database_PickleballClub_DEMO_DESTRUCTIVE.sql` trên máy chủ có dữ liệu thật (tệp này chỉ phục vụ dựng lab demo rỗng ban đầu).

---

## 2. Quy Trình Sao Lưu Trước Khi Nâng Cấp (Pre-Deploy SQL Backup)

Luôn thực hiện sao lưu có kiểm tra `CHECKSUM` và xác minh tệp `.bak` bằng `RESTORE VERIFYONLY` trước mọi đợt cập nhật mã nguồn hoặc chạy migration:

```sql
DECLARE @BackupFile NVARCHAR(500) =
    N'C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup\PickleballClub_PreDeploy_'
    + CONVERT(NVARCHAR(20), GETDATE(), 112) + N'_'
    + REPLACE(CONVERT(NVARCHAR(8), GETDATE(), 108), ':', '') + N'.bak';

BACKUP DATABASE [PickleballClub]
TO DISK = @BackupFile
WITH COPY_ONLY, FORMAT, INIT, CHECKSUM, STATS = 25;

RESTORE VERIFYONLY
FROM DISK = @BackupFile
WITH CHECKSUM;
```

Chỉ tiếp tục bước Migration khi SQL Server trả về thông báo:
`The backup set on file 1 is valid.`

---

## 3. Quy Trình Chạy EF Core Migrations & Phát Hành Ứng Dụng

Ứng dụng ở chế độ `Production` **không tự động sửa schema khi khởi động web** (`P2-01`). Việc nâng cấp CSDL được tách thành bước tường minh, có khóa ứng dụng (`sp_getapplock`) của EF Core:

### Bước 3.1 — Build gói phát hành (`Release`)
```powershell
dotnet publish PickleballClub.csproj -c Release -o C:\inetpub\spike-pickleball-next
```

### Bước 3.2 — Chạy Migration tường minh qua CLI `--migrate`
```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ConnectionStrings__DefaultConnection = "Server=.\SQLEXPRESS;Database=PickleballClub;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
$env:SPIKE_QR_SIGNING_KEY = "<KHOA_BI_MAT_TOI_THIEU_32_KY_TU>"

dotnet C:\inetpub\spike-pickleball-next\PickleballClub.dll --migrate
```
- Lệnh `--migrate` áp dụng tuần tự các bản migration phiên bản hóa:
  1. `20261109010000_V2_BaselineAndSafeExpand` (mở rộng không phá hủy các bảng hiện hữu, tạo bảng sổ cái/kiểm toán/khóa sân/biến động tồn kho, backfill `SecurityStamp` và `AmountDue`).
  2. `20261109010500_V2_LedgerTypedColumnsAndRowVersion` (bổ sung cột kiểu mạnh `DECIMAL(18,2)`, `DATE`, `TIME`, `ROWVERSION`, trigger chống sửa/xóa sổ cái thanh toán `TR_tb_PaymentTransaction_Immutable`, và các `UNIQUE FILTERED INDEX` cho `IdempotencyKey`).
- Sau khi hoàn tất, tiến trình tự thoát với mã `0` (`EF Core migrations applied successfully.`).

### Bước 3.3 — Chuyển lưu lượng & Kiểm tra `/health`
1. Trỏ dịch vụ Windows Service / IIS AppPool sang thư mục bản phát hành mới và khởi động.
2. Kiểm tra đầu cuối sức khỏe vận hành:
```powershell
Invoke-RestMethod -Uri "https://<ten-mien-clb>/health"
```
Kết quả mong đợi (`HTTP 200 OK`, không lộ chuỗi kết nối hoặc thông tin nhạy cảm):
```json
{
  "status": "Healthy",
  "database": "Online",
  "environment": "Production",
  "timestampUtc": "..."
}
```

---

## 4. Quy Trình Diễn Tập Khôi Phục Định Kỳ (Restore Verification Drill)

Một bản sao lưu chưa từng khôi phục thử thì chưa được coi là an toàn. Thực hiện định kỳ hàng tuần trên một tên DB kiểm tra độc lập (`PickleballClub_RestoreVerify`):

```sql
RESTORE DATABASE [PickleballClub_RestoreVerify]
FROM DISK = N'C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup\<TenFileBackup>.bak'
WITH MOVE N'PickleballClub' TO N'C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\DATA\PickleballClub_RestoreVerify.mdf',
     MOVE N'PickleballClub_log' TO N'C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\DATA\PickleballClub_RestoreVerify_log.ldf',
     REPLACE, RECOVERY;

DBCC CHECKDB ([PickleballClub_RestoreVerify]) WITH NO_INFOMSGS;

-- Đối chiếu số lượng bản ghi cốt lõi
SELECT
    (SELECT COUNT(*) FROM [PickleballClub_RestoreVerify].dbo.tb_Account) AS Accounts,
    (SELECT COUNT(*) FROM [PickleballClub_RestoreVerify].dbo.tb_Booking) AS Bookings,
    (SELECT COUNT(*) FROM [PickleballClub_RestoreVerify].dbo.tb_PaymentTransaction) AS LedgerRows;

-- Dọn dẹp DB diễn tập sau khi xác minh thành công
ALTER DATABASE [PickleballClub_RestoreVerify] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
DROP DATABASE [PickleballClub_RestoreVerify];
```

*(Quy trình trên đã được thực thi và xác minh thực tế trên `.\SQLEXPRESS` trong đợt nghiệm thu V2 — xem chi tiết log tại `docs/TEST_EVIDENCE.md`).*

---

## 5. Quy Trình Quay Lui Khẩn Cấp (Rollback Plan)

### Trường hợp A — Lỗi tầng giao diện / ứng dụng (chưa phát sinh thay đổi dữ liệu không tương thích)
1. Vì các bản migration V2 được thiết kế theo mô hình **Expand-Only (chỉ mở rộng cột có `DEFAULT`, giữ nguyên cột cũ)**, có thể trỏ ngay IIS / Windows Service về thư mục `Release` ổn định liền trước mà không làm mất dữ liệu đặt sân hay giao dịch thu ngân vừa phát sinh.
2. Kiểm tra lại `GET /health`.

### Trường hợp B — Sự cố nghiêm trọng trong lúc chạy Migration trước khi mở lưu lượng
1. Giữ ứng dụng ở trạng thái bảo trì (Maintenance / Offline).
2. Khôi phục cơ sở dữ liệu từ bản sao lưu `PreDeploy` vừa xác minh ở Mục 2:
```sql
USE [master];
ALTER DATABASE [PickleballClub] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;

RESTORE DATABASE [PickleballClub]
FROM DISK = N'C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup\<File_PreDeploy>.bak'
WITH REPLACE, RECOVERY;

ALTER DATABASE [PickleballClub] SET MULTI_USER;
DBCC CHECKDB ([PickleballClub]) WITH NO_INFOMSGS;
```
3. Khởi động lại phiên bản ứng dụng ổn định liền trước.
