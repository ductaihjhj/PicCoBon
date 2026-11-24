# 🏗️ Kiến Trúc Hệ Thống & Bản Đồ Mã Nguồn — Spike Pickleball Club

Tài liệu này cung cấp cái nhìn tổng thể và ghi chú chi tiết từng thư mục, tệp mã nguồn, bảng dữ liệu và luồng nghiệp vụ để lập trình viên có thể nắm bắt dự án nhanh chóng mà không cần dò tìm thủ công.

---

## 1. Cấu Trúc Thư Mục Toàn Dự Án

```text
e:\pickleball\
├── Areas/
│   └── Admin/                      # Phân hệ Quản trị & Vận hành nội bộ (Admin & Staff)
│       ├── Controllers/            # Quản lý Sân, Sản phẩm POS, Đơn đặt sân, Tài khoản, Menu
│       └── Views/                  # Giao diện quản trị theo chuẩn Admin Shell (Shadcn V7)
├── Configuration/                  # Lớp cấu hình mạnh kiểu (Strongly-typed Options)
│   ├── GeminiOptions.cs            # Cấu hình trợ lý AI Gemini (Model, ApiKey, Timeout, RateLimit)
│   └── PayPalOptions.cs            # Cấu hình cổng thanh toán PayPal (ClientId, Secret, Tỷ giá)
├── Controllers/                    # MVC Controllers & REST JSON Endpoints phía Khách hàng / Chung
├── Logging/                        # Bộ định dạng log console gọn sạch & Correlation ID theo request
├── Migrations/                     # Lịch sử nâng cấp CSDL EF Core (V2 -> V4 -> V5 -> V6)
├── Models/                         # Thực thể EF Core (Tb*) và DbContext (PickleballContext)
├── PickleballClub.Tests/           # Bộ kiểm thử tự động xUnit (Unit, Concurrency, QR, AI, PayPal, Social)
├── prototypes/
│   └── san-giao-luu/               # Bản mẫu HTML tĩnh dùng đối chiếu thiết kế nhân vật pixel
├── scripts/
│   └── demo-only/                  # Script SQL tạo dữ liệu mẫu (chỉ dùng cho môi trường lab trống)
├── Services/                       # Tầng xử lý nghiệp vụ cốt lõi (Domain & Application Services)
├── Utilities/                      # Tiện ích xác thực Cookie Claims, CSRF, Session & mã băm cũ
├── ViewComponents/                 # Thành phần giao diện tái sử dụng (Menu động, Danh sách sân/sản phẩm)
├── Views/                          # Giao diện Razor phía hội viên, khách hàng & màn hình vận hành
├── wwwroot/                        # Tài nguyên tĩnh (CSS phân tầng, JS module, ảnh sân/sản phẩm/sprite)
├── docs/                           # Kho tài liệu kỹ thuật được phân nhóm gọn gàng
├── AGENTS.md                       # Quy tắc an toàn bắt buộc khi chỉnh sửa dự án
├── Program.cs                      # Điểm khởi chạy ứng dụng, đăng ký DI, Auth, Middleware & CLI --migrate
└── README.md                       # Tài liệu giới thiệu & hướng dẫn khởi chạy nhanh
```

---

## 2. Ghi Chú Chi Tiết Tầng Backend (`Controllers` & `Services`)

### 2.1. Các Controller Chính (`Controllers/`)

| Tệp | Đường dẫn chính | Trách nhiệm |
| --- | --- | --- |
| `HomeController.cs` | `/`, `/Checkin`, `/Management`, `/Reports`, `/Cms`, `/health` | Trang chủ công khai; các màn hình vận hành nghiệp vụ cho `Staff`/`Admin` (Soát vé QR, Điều phối sân & POS, Báo cáo sổ cái, Cấu hình CMS) và endpoint kiểm tra sức khỏe hệ thống. |
| `BookingController.cs` | `/Booking` | Giao diện đặt sân trực tuyến theo ngày, sân, khung giờ và thời lượng. |
| `ProductController.cs` | `/Product` | Cửa hàng dụng cụ & giải khát Pro Shop (mua lẻ hoặc gộp vào ca sân đang chơi). |
| `LookupController.cs` | `/Lookup` | Tra cứu lịch đặt sân của hội viên hoặc tra cứu/hủy vé khách vãng lai bằng `Mã đặt sân + Mã bảo mật vé`. |
| `AccountController.cs` | `/Account/Login`, `/Account/Register`, `/Account/Profile` | Đăng nhập, đăng ký hội viên, đổi mật khẩu, cập nhật hồ sơ VĐV (DUPR, lối đánh) và chọn/tải ảnh đại diện đồng bộ sang Sân giao lưu. |
| `PaymentController.cs` | `/Payment/*`, `/api/paypal/*` | Khởi tạo giao dịch thanh toán trực tuyến qua PayPal Sandbox, xác nhận Capture và hiển thị biên lai điện tử. |
| `AssistantController.cs` | `/api/assistant/*` | API trò chuyện với trợ lý ảo **Spike AI Mascot** (hỗ trợ hỏi giá sân, kiểm tra sân trống, tra cứu sản phẩm Pro Shop và quy định CLB). |
| `SanGiaoLuuController.cs` | `/SanGiaoLuu`, `/api/social/*` | Sân giao lưu trực quan 2D Pixel dành riêng cho Hội viên (`Customer`): xem hội viên trên sân, tìm bạn tập cùng trình độ và gửi/nhận lời mời giao lưu. |
| `ApiDbController.cs` | `/api/db/state`, `/api/bookings/*`, `/api/orders/*`, `/api/checkin/*` | Cụm REST JSON API trung tâm phục vụ báo giá tách đoạn, giữ chỗ/đặt sân chống trùng, quét vé QR, POS thu ngân, tồn kho và báo cáo sổ cái. |

### 2.2. Các Controller Quản Trị (`Areas/Admin/Controllers/`)
- `CourtsController.cs` (`/Admin/Courts`): Thêm/sửa sân thi đấu, trạng thái bảo trì và bảng giá cơ sở.
- `ProductsController.cs` (`/Admin/Products`): Quản lý danh mục hàng hóa Pro Shop, tồn kho và ảnh sản phẩm chuẩn hóa.
- `BookingsController.cs` (`/Admin/Bookings`): Danh sách tra cứu & quản trị toàn bộ lịch đặt sân.
- `AccountsController.cs` (`/Admin/Accounts`): Quản lý tài khoản hội viên & nhân sự, khóa/mở khóa phiên tức thì qua `SecurityStamp`, cấp mật khẩu tạm một lần.
- `MenusController.cs` (`/Admin/Menus`): Cấu hình thanh điều hướng động trên giao diện khách hàng.
- `HomeController.cs`, `LoginController.cs`, `RegisterController.cs`: Cổng điều hướng khu vực quản trị.

### 2.3. Tầng Nghiệp Vụ (`Services/`)

| Nhóm nghiệp vụ | Service | Ghi chú kỹ thuật quan trọng |
| --- | --- | --- |
| **Đặt sân & Định giá** | `BookingService.cs`, `PricingService.cs`, `BookingLifecycleWorker.cs` | Sử dụng giao dịch `Serializable` kèm khóa `UPDLOCK, HOLDLOCK` chống đặt trùng tuyệt đối. `PricingService` tách phút chính xác giữa giờ thường và giờ vàng (`Peak-hour`). Worker nền tự động giải phóng đơn quá hạn giữ chỗ (`expired` / `no_show`). |
| **Pro Shop, POS & Sổ cái** | `OrderService.cs`, `ProductImageService.cs`, `IdempotencyService.cs` | Trừ/hoàn tồn kho nguyên tử có ghi lịch sử `tb_InventoryMovement`. Sổ cái `tb_PaymentTransaction` chỉ thêm mới (append-only), trạng thái thanh toán dẫn xuất từ tổng thu trừ tổng hoàn. `IdempotencyService` chống gửi trùng thao tác mạng chập chờn. |
| **Mã vé QR & Bảo mật** | `QrTokenService.cs`, `PasswordService.cs`, `GuestRecoveryCookieHelper.cs`, `IconSanitizer.cs` | Sinh vé QR chuẩn ISO/IEC 18004 (`QRCoder`) chứa chữ ký `HMAC-SHA256` (`SPK1...`) và giải mã ảnh upload bằng `ZXing.Net`. Băm mật khẩu chuẩn `PBKDF2`. Lọc whitelist tên icon chống XSS. |
| **Thanh toán PayPal (V5)** | `PayPalClient.cs` (`IPayPalClient.cs`), `PayPalPaymentService.cs`, `PayPalReconciliationWorker.cs` | Giao tiếp REST v2 với PayPal, quy đổi tỷ giá minh bạch, đối soát tự động các giao dịch treo và tự động tạo bút toán sổ cái khi Capture thành công. |
| **Trợ lý AI Mascot** | `GeminiAssistantService.cs` (`IAiAssistantService.cs`), `SpikeAssistantToolService.cs` | Tích hợp Google Gemini với bộ công cụ nội bộ chỉ đọc (tra sân trống, tính giá dự kiến, tìm vợt/bóng còn hàng) mà không bao giờ truy cập thông tin cá nhân của khách khác. |
| **Sân giao lưu (V6)** | `SocialCourtService.cs` | Đồng bộ ảnh đại diện `/Account/Profile` sang 8 nhân vật pixel, lọc sạch PII, tính điểm tương thích tìm bạn tập và quản lý lời mời giao lưu. |
| **Cấu hình CLB** | `ClubSettingsService.cs` | Đọc/ghi cấu hình động từ bảng `tb_CmsConfig` có bộ nhớ đệm an toàn (giờ mở cửa, giờ vàng, chính sách hủy vé). |

---

## 3. Sơ Đồ Bảng Cơ Sở Dữ Liệu Chính (`Models/`)

| Nhóm | Bảng SQL (`Entity`) | Mục đích lưu trữ |
| --- | --- | --- |
| **Tài khoản & Phân quyền** | `tb_Account` (`TbAccount`), `tb_Role` (`TbRole`) | Hồ sơ người dùng, vai trò (`1: Admin`, `2: Customer`, `3: Staff`), mật khẩu băm `PBKDF2`, `SecurityStamp`, `AvatarUrl` và thông số VĐV. |
| **Sân & Lịch đặt sân** | `tb_Court` (`TbCourt`), `tb_CourtLock` (`TbCourtLock`), `tb_Booking` (`TbBooking`) | Danh sách sân, lịch khóa sân bảo trì và đơn đặt sân (lưu thời lượng, báo giá tách đoạn, mã bảo mật vé khách lẻ băm `SHA-256`, `RowVersion`). |
| **Pro Shop & Tồn kho** | `tb_ProductCategory`, `tb_Product`, `tb_Order`, `tb_OrderDetail`, `tb_OrderStatus`, `tb_InventoryMovement` | Danh mục mặt hàng, đơn hàng lẻ hoặc gắn kèm ca sân, chi tiết món và nhật ký xuất/nhập tồn kho nguyên tử. |
| **Sổ cái & Thanh toán** | `tb_PaymentTransaction`, `tb_PayPalTransaction`, `tb_IdempotencyRecord` | Sổ cái thu tiền/hoàn tiền bất biến (`cash`, `transfer`, `paypal`, `refund`), đối chiếu giao dịch PayPal quốc tế và khóa chống trùng request. |
| **Sân giao lưu (V6)** | `tb_SocialProfile` (`TbSocialProfile`), `tb_SocialInvite` (`TbSocialInvite`) | Thiết lập hiện diện trên Sân giao lưu (nhân vật `SpriteId`, trình độ, khu vực sân, câu chào) và lời mời rủ đánh cùng giữa các hội viên. |
| **Quản trị & Kiểm toán** | `tb_CmsConfig`, `tb_Menu`, `tb_AuditLog` | Tham số vận hành CLB, menu điều hướng động và nhật ký kiểm toán thao tác nhạy cảm của nhân sự. |

---

## 4. Tổ Chức Tài Nguyên Tĩnh (`wwwroot/`)

### 4.1. Kiến trúc CSS phân tầng (`wwwroot/css/`)
- `core/reset.css` & `core/tokens.css`: Chuẩn hóa trình duyệt và định nghĩa toàn bộ biến màu sắc, font chữ, bo góc, đổ bóng dùng chung.
- `components/primitives.css`: Hệ thống nút bấm, ô nhập liệu, huy hiệu (badge), bảng biểu, thẻ (card) và hộp thoại (modal).
- `components/qr-ticket.css`: Giao diện thẻ vé QR thương hiệu Spike Pickleball.
- `components/mascot-assistant.css`: Nút nổi và khung chat trợ lý ảo Spike AI Mascot.
- `layouts/public-shell.css` & `layouts/admin-shell.css`: Khung bố cục đầu trang/chân trang khách hàng và thanh bên quản trị.
- `pages/public-pages.css`, `pages/admin-pages.css`, `pages/san-giao-luu.css`: CSS đặc thù theo từng nhóm màn hình (không ghi đè lẫn nhau).

### 4.2. Các Module JavaScript (`wwwroot/js/`)
- **Dùng chung**: `shared.js` (CSRF fetch helper, định dạng tiền tệ VND, thông báo toast, đồng bộ trạng thái `/api/db/state`), `menu.js`, `mascot-assistant.js`.
- **Nghiệp vụ khách hàng**: `booking.js` (đặt sân & báo giá trực tiếp), `lookup.js` (tra cứu/hủy vé), `pages/paypal-checkout.js`, `pages/profile.js`, `pages/auth.js`.
- **Sân giao lưu**: `san-giao-luu-engine.js` (engine 2D pixel court & hoạt ảnh nhân vật) và `san-giao-luu-page.js` (kết nối dữ liệu hội viên & lời mời).
- **Vận hành & Quản trị**: `management.js` (điều phối sân, POS & thu ngân), `checkin.js` (quét QR camera/tệp ảnh), `reports.js` (báo cáo doanh thu sổ cái), `dashboard.js`, `pages/cms.js`, `pages/admin-product-form.js`.
- **Xem thử độc lập**: `wwwroot/mascot-preview.html` (trang xem nhanh biểu cảm linh vật AI Mascot khi chạy dev server).

---

## 5. Bộ Kiểm Thử Tự Động (`PickleballClub.Tests/`)

Chạy toàn bộ bằng lệnh `dotnet test PickleballClub.Tests/PickleballClub.Tests.csproj`:

| Tệp kiểm thử | Nội dung xác minh |
| --- | --- |
| `SqlServerConcurrencyAndMigrationTests.cs` | Kiểm thử tranh chấp đặt sân song song trên SQL Server thật, kiểm tra migration và toàn vẹn tài nguyên tĩnh cốt lõi. |
| `UnitAndQrVerificationTests.cs` & `V4ReleaseBlockersAndQrTests.cs` | Kiểm thử định giá giờ vàng, chữ ký số vé QR `HMAC-SHA256`, giải mã ảnh `ZXing.Net`, Hold TTL và Idempotency. |
| `HttpSecurityAndApiIntegrationTests.cs` & `SafeRemediationRegressionTests.cs` | Kiểm tra phân quyền RBAC (`Admin`/`Staff`/`Customer`), chống CSRF, chống lộ PII trên API công khai và nghiệp vụ sổ cái. |
| `PayPalSandboxIntegrationTests.cs` | Kiểm thử luồng tạo đơn PayPal, Capture nguyên tử, quy đổi tiền tệ và chống ghi sổ trùng. |
| `GeminiAssistantTests.cs` | Kiểm thử trợ lý AI Mascot, gọi công cụ tra cứu dữ liệu thật và rào chắn bảo mật dữ liệu. |
| `ProShopV6ImageAndApiTests.cs` & `ShadcnAdminV7ParityTests.cs` | Kiểm thử chuẩn hóa ảnh sản phẩm Pro Shop và tính tương thích giao diện quản trị V7. |
| `ProfileAvatarTests.cs` & `SanGiaoLuuTests.cs` | Kiểm thử tải/chọn avatar hồ sơ, đồng bộ sang 8 nhân vật pixel, phân quyền Sân giao lưu, ẩn PII và vòng đời lời mời. |
| `LoggingAndDiagnosticsTests.cs` | Kiểm thử định dạng log console sạch và đảm bảo không ghi lộ thông tin nhạy cảm. |
