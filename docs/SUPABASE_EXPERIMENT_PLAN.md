# Spike OS — Supabase PostgreSQL experiment

> **Trạng thái: giai đoạn chuẩn bị, CHƯA phải bản Spike OS chạy trên PostgreSQL.**

## 1. Cô lập an toàn

- GitHub repository: `ductai206user/PicCoBon`
- Nhánh gốc: `main` (được xác nhận mới hơn các nhánh `feat/paypal-sandbox`, `feat/notion-redesign`)
- Nhánh thử nghiệm: `experiment/supabase-postgres`
- **Không merge, không sửa nhánh gốc, không chạm SQL Server hiện tại.**
- Tài khoản Supabase kết nối tại thời điểm tạo nhánh **chưa có project**, nên chưa thể kết nối hay chạy migrations từ xa.
- Repository là **public**. KHÔNG commit database password, Gemini API key, PayPal secret, connection string, file backup hoặc dữ liệu khách.

## 2. Kiến trúc thử nghiệm mục tiêu

`Browser -> ASP.NET Core 10 MVC -> EF Core 10 + Npgsql -> Supabase PostgreSQL`

Giữ nguyên: frontend Razor, tài khoản ASP.NET Core, QR, Gemini thông qua backend, Booking, Pro Shop, PayPal Sandbox, Admin/Staff.

**Không chuyển sang Supabase Auth hoặc client SDK** trong giai đoạn này. Website .NET vẫn cần hosting riêng; Supabase chỉ cung cấp PostgreSQL.

## 3. Audit thực tế trên nhánh main

Đã kiểm tra:

- `PickleballClub.csproj`: `net10.0`, `Microsoft.EntityFrameworkCore.SqlServer 10.0.13`.
- `Program.cs`: `AddDbContext<PickleballContext>(...UseSqlServer(DefaultConnection))`.
- `Models/PickleballContext.cs`: schema `tb_*`, các typed-column và SQL Server-specific mappings cần kiểm tra đầy đủ.
- `Services/BookingService.cs`: xử lý hold, concurrency và kiểm tra lịch.
- `Services/OrderService.cs`: xử lý tồn kho, idempotency và thanh toán.
- `README.md`: xác nhận sử dụng `UPDLOCK, HOLDLOCK`, `sp_getapplock`, `rowversion`, trigger chống sửa ledger.

### Những chỗ không thể chỉ đổi connection string

| Vấn đề SQL Server | Cách triển khai PostgreSQL đề xuất | Bắt buộc kiểm thử |
|---|---|---|
| `sp_getapplock` | PostgreSQL transaction-scoped advisory lock với key ổn định | Cùng sân/cùng giờ không double-book |
| `UPDLOCK, HOLDLOCK` | `SELECT ... FOR UPDATE` trong transaction | Booking, sản phẩm, concurrent updates |
| SQL Server `rowversion` | Concurrency token riêng (ví dụ version bigint do app/trigger tăng); không dùng `xmin` tùy tiện | Stale-write/retry |
| SQL Server migrations | Tạo migrations PostgreSQL riêng; **KHÔNG chạy SQL Server migration trên Supabase** | Fresh install, constraints, indexes |
| T-SQL và column types | Đổi cú pháp/kiểu theo PostgreSQL, review mỗi raw SQL | Reports, services, transactions |
| Trigger ledger SQL Server | Viết trigger PostgreSQL tương đương, kiểm thử bất biến | Không được sửa/xóa ledger đã hoàn tất |
| Case-sensitive identifiers | Rà soát tên cột/table quoted identifiers | EF mapping, raw SQL |
| Date/Time `DateTimeKind` | Chuẩn hóa UTC, giữ booking theo Asia/Ho_Chi_Minh | Hold, giá giờ vàng, báo cáo |
| Filtered indexes | PostgreSQL partial unique indexes | Idempotency, capture ID, active booking |

Không loại bỏ khóa, constraints hoặc trigger chỉ để ứng dụng chạy được.

## 4. Bảo mật Supabase

- Khuyến nghị tạo **schema backend riêng `spike_os`** và không expose schema này qua Supabase Data API; xác nhận cấu hình Exposed schemas trong Supabase Dashboard.
- Với bảng ở schema `public` hoặc schema được Data API expose, phải bật RLS và chính sách chính xác trước khi cấp GRANT.
- Chỉ backend ASP.NET Core dùng connection string PostgreSQL. Không gửi credentials/database password xuống browser.
- Dùng tài khoản/role có quyền tối thiểu cho runtime; tài khoản migration quyền cao phải tách biệt.
- Dữ liệu test phải là dữ liệu giả, không copy thông tin người dùng hoặc secrets từ SQL Server thật.
- Supabase direct PostgreSQL hiện dùng IPv6 trên nhiều gói; nếu máy/hosting chỉ có IPv4, dùng **Session pooler (port 5432)** theo kết nối hiển thị trong Supabase Dashboard. Transaction pooler (port 6543) có giới hạn prepared statements; chỉ dùng khi cấu hình đúng.

Tài liệu kết nối: https://supabase.com/docs/guides/database/connecting-to-postgres

## 5. Kiểm tra kết nối không thay đổi database

Project `tools/SupabaseProbe/` là ứng dụng console độc lập:

- Dùng Npgsql, chỉ chạy truy vấn `SELECT`.
- Đọc kết nối từ `SPIKE_SUPABASE_CONNECTION`, không lưu vào source.
- Kiểm tra phiên bản Postgres và schema `spike_os` có tồn tại chưa.
- **Không** sửa database, **không** chạy migration, **không** kiểm thử được nghiệp vụ Booking.

Chạy trên máy có .NET SDK 10:

~~~powershell
$env:SPIKE_SUPABASE_CONNECTION = "Host=YOUR_HOST;Port=5432;Database=postgres;Username=YOUR_USER;Password=YOUR_PASSWORD;SSL Mode=VerifyFull"
dotnet run --project tools/SupabaseProbe/SupabaseProbe.csproj
Remove-Item Env:\SPIKE_SUPABASE_CONNECTION
~~~

Hoặc đặt biến môi trường bằng phương thức lưu secrets thích hợp. Không dán connection string vào chat, commit, hoặc log.

## 6. Các mốc tiếp theo

- [x] Tạo nhánh Git độc lập từ `main`.
- [x] Đọc source chính và xác định những điểm SQL Server-specific.
- [x] Thêm probe kết nối PostgreSQL đọc-only.
- [ ] Tạo **Supabase test project** sau khi xác nhận chi phí/gói miễn phí.
- [ ] Chạy probe bằng connection string của test project.
- [ ] Tạo provider abstraction và Npgsql DbContext configuration chỉ trên nhánh thử nghiệm.
- [ ] Thiết kế schema/migrations PostgreSQL tương thích **từng bảng**, đặt trong schema không exposed.
- [ ] Chuyển các raw SQL, lock, rowversion, trigger ledger.
- [ ] Seed dữ liệu test; xác minh xác thực tài khoản và mật khẩu test.
- [ ] Test đồng thời Booking/Pro Shop, idempotency, PayPal Sandbox, QR và reports.
- [ ] Host .NET application độc lập để có URL thử nghiệm.

**Release gate:** Không bật Supabase làm database của Spike OS public cho đến khi các kiểm thử về booking concurrency, inventory, ledger/refunds, auth và QR hoàn tất.

## 7. Chính sách rollback

Chỉ chuyển về nhánh `main` hoặc thư mục code gốc. Database SQL Server gốc giữ nguyên. Không merge thử nghiệm vào `main` cho đến khi người dùng duyệt và regression suite pass.

## 8. Lưu ý về tính trung thực kiểm chứng

Nhánh này chỉ chứa công cụ kiểm tra kết nối và hướng dẫn; **chưa có Supabase project, chưa migrate schema, chưa chạy website trên PostgreSQL và chưa chứng minh tính tương thích nghiệp vụ**.
