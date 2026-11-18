# Kế Hoạch Triển Khai & Quản Trị Rủi Ro V7 (`IMPLEMENTATION_PLAN.md`)

**Mục tiêu:** Chuyển đổi toàn bộ giao diện Quản trị (`/Admin/**`, `/Management`, `/Checkin`, `/Reports`, `/Cms`) sang hệ thống thiết kế `shadcn-admin` (`satnaing/shadcn-admin`) và đồng bộ hóa ngôn ngữ thiết kế cho Website Khách hàng (`/`, `/Booking`, `/Product`, `/Lookup`, `/Account/**`) trên nền tảng ASP.NET Core MVC (.NET 10) + SQL Server mà **không gây bất kỳ hồi quy nghiệp vụ nào**.

---

## 1. Các Giai Đoạn Thực Thi (Phased Execution)

### Phase 0 — Khảo sát, Baseline & Lập Ma Trận Hợp Đồng
- Truy cập trực tiếp repo `satnaing/shadcn-admin` (SHA `e16c87f213a5ba5e45964e9b67c792105ec74d26`), phân tích cấu trúc `AppSidebar`, `SidebarInset`, `Header`, `Main`, `Card`, `DataTable`, `CommandMenu`, `ThemeSwitch`.
- Chụp bộ ảnh màn hình Baseline (`docs/ui-v7/screenshots/before/`) trên cả Desktop (`1440px`) và Mobile (`375px`) cho toàn bộ 16 màn hình chính.
- Lập đầy đủ các ma trận bảo vệ hợp đồng: `ROUTE_PARITY_MATRIX.md`, `DOM_CONTRACT_MATRIX.md`, `UI_API_SQL_MATRIX.md`, `TEST_MATRIX.md`.

### Phase 1 — Xây Dựng Nền Tảng Design Tokens & `shadcn-admin` Shell
- **Nâng cấp `wwwroot/css/styles.css`:**
  - Bổ sung hệ thống biến màu, bóng đổ, viền `1px`, typography `tabular-nums`, chế độ `Light` / `Dark` (`html[data-theme="dark"]`) với bảo vệ tuyệt đối cho vùng QR `.spk-ticket-qr-well` (`#FFFFFF`).
  - Xây dựng bộ CSS cho `.sh-admin-shell`, `.sh-sidebar` (hỗ trợ thu gọn `data-sidebar-collapsed="true"` trên desktop và ngăn kéo trượt `data-sidebar-mobile-open="true"` kèm backdrop trên mobile), `.sh-topbar`, `.sh-breadcrumb`, `.sh-cmd-modal` (`Ctrl+K`), `.sh-page-header`.
- **Nâng cấp `Areas/Admin/Views/Shared/_Layout.cshtml`:**
  - Thay thế thanh ngang `.site-header` + `.admin-subbar` cũ trong khu vực Admin bằng bố cục chuẩn `shadcn-admin`:
    - **Left Collapsible Sidebar (`#shAdminSidebar`):**
      - `SidebarHeader`: Logo Spike Pickleball + Tên tổ chức (`Spike Pickleball · Hệ thống Quản trị CLB`) + nút thu gọn.
      - `SidebarContent`: Phân thành 3 nhóm rõ ràng theo đúng chuẩn `shadcn-admin` và kiểm soát hiển thị theo quyền thực (`Admin` vs `Staff`):
        1. **Vận hành & Điều phối (Admin + Staff):** Tổng quan (`/Admin`), Điều phối & POS (`/Management`), Đơn đặt sân (`/Admin/Bookings`), Soát vé QR (`/Checkin`), Sổ cái & Báo cáo (`/Reports`).
        2. **Danh mục & Cấu hình (Chỉ Admin):** Quản lý cụm sân (`/Admin/Courts`), Sản phẩm Pro Shop (`/Admin/Products`), Hội viên & RBAC (`/Admin/Accounts`), Thanh điều hướng (`/Admin/Menus`), Cấu hình CMS (`/Cms`).
        3. **Lối tắt Cổng Khách hàng:** Xem Trang chủ (`/`), Đặt sân trực tuyến (`/Booking`), Cửa hàng Pro Shop (`/Product`), Tra cứu vé QR (`/Lookup`).
      - `SidebarFooter`: Thẻ tài khoản đang đăng nhập (`@Function._FullName`, vai trò `Admin`/`Staff`, link Hồ sơ `/Account/Profile` và nút Thoát `/Admin/Home/Logout`).
    - **Sticky Topbar (`#shAdminTopbar`):**
      - Nút `SidebarTrigger` (`#shSidebarToggleBtn`, hỗ trợ cả desktop collapse và mobile drawer).
      - Vạch phân cách dọc + `Breadcrumb` tự động nhận diện trang hiện tại.
      - Thanh tìm kiếm nhanh `Command Search` (`#shCmdTriggerBtn`, phím tắt `Ctrl+K` / `⌘K`) mở hộp thoại nhảy nhanh đến mọi trang & công cụ vận hành.
      - Nút chuyển đổi giao diện Sáng / Tối (`#shThemeToggleBtn`).
      - Nút xem nhanh Cửa hàng / Trang chủ và menu tài khoản.
- **Đồng bộ `Views/Shared/_Layout.cshtml` (Customer Web Shell):**
  - Tinh chỉnh Header, Navigation pill, nút chuyển giao diện Sáng/Tối (`#siteThemeToggleBtn`), thanh lối tắt Admin (khi đăng nhập bằng tài khoản Admin/Staff) và Footer đồng bộ token V7.

### Phase 2 — Chuẩn Hóa Từng Màn Hình (Vertical Slices)
- **Các trang Admin (`Areas/Admin/Views/**`):**
  - Đồng bộ `PageHeader`, `Card`, `StatsRow`, `DataTable`, `FilterTabs` và `Forms` trên `/Admin` (`Home/Index.cshtml`), `/Management`, `/Checkin`, `/Reports`, `/Cms`, `/Admin/Bookings`, `/Admin/Courts` (`Index`, `Create`, `Edit`), `/Admin/Products` (`Index`, `Create`, `Edit`, `Delete`), `/Admin/Accounts`, `/Admin/Menus` (`Index`, `Create`, `Edit`, `Details`, `Delete`), `/Admin/Login` và `/Admin/Register`.
  - Giữ nguyên 100% `id`, `name`, `data-*`, `@Html.AntiForgeryToken()`, `enctype="multipart/form-data"` và các hàm sự kiện JS.
- **Các trang Khách hàng (`Views/**`):**
  - Tinh chỉnh tinh tế các bề mặt thẻ, bộ lọc, bảng khung giờ trống, bảng giá, khung tra cứu vé QR và trang đăng nhập/hồ sơ theo hệ token V7 mà không làm thay đổi luồng tương tác đã qua kiểm thử V5 & V6.

### Phase 3 & Phase 4 — Kiểm Thử Tự Động Toàn Diện, Tự Sửa Lỗi & Nghiệm Thu
- Chạy `node --check` trên toàn bộ tệp JS.
- Chạy `dotnet test -c Release` (toàn bộ 31 bài kiểm thử xUnit hiện có + bổ sung bài kiểm thử V7 xác minh Admin Shell, RBAC, QR PNG/SVG decode & API parity).
- Chạy kịch bản Playwright tự động (`verify-ui-v7.js`) trên 7 độ rộng màn hình (`320, 375, 390, 768, 1024, 1440, 1920px`), kiểm tra đóng/mở Sidebar desktop & mobile, Command Palette (`Ctrl+K`), chế độ Light/Dark, bảo toàn vùng trắng QR (`#FFFFFF`), upload ảnh Pro Shop V6, POS, Sổ cái và chụp đầy đủ bộ ảnh `docs/ui-v7/screenshots/after/`.

---

## 2. Chiến Lược Rollback An Toàn

- Workspace đang được quản lý bằng Git với trạng thái sạch tại điểm hoàn tất V6 (`HEAD`).
- Nếu cần quay lui bất kỳ tệp giao diện nào:
  ```powershell
  git checkout -- Areas/ Views/ wwwroot/
  ```
- Không có thay đổi phá vỡ cấu trúc bảng SQL Server hoặc xóa dữ liệu hiện hữu.
