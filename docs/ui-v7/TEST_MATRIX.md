# Ma Trận Kiểm Thử Tự Động & Nghiệm Thu V7 (`TEST_MATRIX.md`)

---

## 1. Kiểm Thử Đơn Vị & Tích Hợp Backend (`xUnit` — `PickleballClub.Tests`)

| Nhóm kiểm thử | File Test | Số lượng Test | Mục tiêu xác minh |
|---|---|---|---|
| **Concurrency, Migration & Booking Integrity** | `SqlServerConcurrencyAndMigrationTests.cs` | 6 | Chống đặt trùng lịch song song (`409`), khóa sân toàn ngày, trừ tồn kho nguyên tử Pro Shop, sổ cái thu/hoàn tiền tách biệt đóng ca, nâng cấp schema an toàn. |
| **HTTP Security, CSRF, ETag & ZXing QR Check-in** | `HttpSecurityAndApiIntegrationTests.cs` | 4 | Bảo mật header, ẩn PII với khách ẩn danh, hỗ trợ `ETag 304`, bắt buộc Anti-Forgery Token, giải mã ảnh QR thật bằng `ZXing` khi check-in. |
| **Unit Pricing, Password & QR Token** | `UnitAndQrVerificationTests.cs` | 10 | Tính giá phân đoạn 30 phút, chiết khấu hội viên, băm mật khẩu PBKDF2, ký và xác thực token QR `SPK1...`. |
| **V4 Release Blockers & Branded QR** | `V4ReleaseBlockersAndQrTests.cs` | 6 | Luồng đặt sân → sinh QR SVG/PNG → giải mã ZXing → check-in cổng. |
| **V6 Product Image Upload & API Sync** | `ProShopV6ImageAndApiTests.cs` | 5 | Upload PNG/JPG/WebP, giữ kênh alpha, chặn file giả mạo/quá 5MB, thay/gỡ/giữ ảnh khi Edit, đồng bộ `imageUrl` và đổi `ETag` trên `/api/db/state`. |
| **V7 Shadcn-Admin Shell, RBAC & QR Integrity** | `ShadcnAdminV7ParityTests.cs` *(Mới)* | 4 | Xác minh cấu trúc `shadcn-admin` Shell trên các trang Admin, kiểm soát hiển thị menu theo vai trò `Admin` vs `Staff`, chặn truy cập trái quyền và bảo toàn tuyệt đối API/QR/Pro Shop. |

---

## 2. Kiểm Thử Trình Duyệt Thực Tế Playwright (`verify-ui-v7.js`)

| Mã Check | Tên Bài Kiểm Thử E2E | Điều Kiện Đạt (Pass Criteria) |
|---|---|---|
| `V7-01` | **Shadcn-Admin App Shell & Collapsible Sidebar (Desktop & Mobile)** | Sidebar `#shAdminSidebar` hiển thị đúng các nhóm điều hướng; bấm `#shSidebarToggleBtn` trên desktop thu gọn Sidebar (`data-sidebar-collapsed="true"`) không làm vỡ bảng hay mất nút; trên mobile (`375px`) mở dạng Sheet có backdrop và đóng bằng phím `Escape`. |
| `V7-02` | **Topbar Sticky, Breadcrumb & Command Search (`Ctrl+K`)** | Topbar `#shAdminTopbar` cố định đầu trang; bấm `#shCmdTriggerBtn` hoặc nhấn `Ctrl+K` mở hộp thoại Command Search (`#shCommandModal`), lọc nhanh các mục điều hướng và đóng bằng `Escape`. |
| `V7-03` | **Theme Switch (`Light` / `Dark`) & Bảo Toàn Tuyệt Đối Vùng Trắng QR** | Chuyển đổi qua lại giữa `Light` và `Dark` mode cập nhật `html[data-theme]`, lưu trạng thái an toàn, đảm bảo độ tương phản chữ và **bắt buộc `.spk-ticket-qr-well` giữ nguyên màu nền `rgb(255, 255, 255)`** trong cả hai chế độ. |
| `V7-04` | **Phân Quyền Hiển Thị Sidebar Theo Vai Trò (`Admin` vs `Staff`)** | Tài khoản `Admin` thấy đầy đủ các mục cấu hình (`/Admin/Courts`, `/Admin/Products`, `/Admin/Accounts`, `/Cms`); tài khoản `Staff` chỉ thấy nhóm vận hành nhưng vẫn truy cập mượt `/Management`, `/Checkin`, `/Reports`. |
| `V7-05` | **Bảo Toàn Nghiệp Vụ Đặt Sân & Vé Điện Tử QR (`/Booking`, `/Lookup`)** | Báo giá máy chủ hoạt động chính xác; mở `#modalTicketQR` trên `/Lookup` hiển thị đúng thẻ vé `#spkDigitalTicketCard`, ảnh QR tải thành công (`naturalWidth > 0`), không bị tràn hay bị che bởi Sidebar/Topbar, các nút tải PNG/SVG và In vé hiển thị đầy đủ. |
| `V7-06` | **Bảo Toàn Pro Shop V6 (Ảnh 1:1, Keyed DOM & Tactile Animation)** | Toàn bộ 10 sản phẩm hiển thị ảnh thật `1:1`, bấm chọn mua giữ nguyên tham chiếu DOM (`sameCardNode === true`, `focusPreserved === true`), cập nhật giỏ hàng và thumbnail `.shop-cart-thumb`. |
| `V7-07` | **Bảo Toàn Admin Product Image Upload (`/Admin/Products/Edit/1`)** | Khung preview `1:1` và vùng kéo-thả ảnh hoạt động chính xác trong `shadcn-admin` Shell. |
| `V7-08` | **Bảo Toàn Điều Phối Sân, POS Modal & Sổ Cái (`/Management`, `/Reports`)** | Mở `#modalPOS` hiển thị đầy đủ ảnh thumbnail sản phẩm; mở `#modalLedgerSettlement` hiển thị đúng form thu/hoàn tiền; trang `/Reports` hiển thị đúng KPI và bảng bút toán sổ cái. |
| `V7-09` | **Responsive Sweep (320px → 1920px) & Zero Console/Page/Image Errors** | Quét qua 7 độ rộng (`320, 375, 390, 768, 1024, 1440, 1920px`) trên cả trang Khách và trang Admin: `overflowPx === 0`, `brokenImages === 0`, `consoleErrors === 0`, `pageErrors === 0`. |
