# Nhật Ký Thay Đổi Giao Diện & Tích Hợp V7 (`CHANGELOG.md`)

**Phiên bản:** `V7 — Shadcn-Admin Unified UI System`
**Thiết kế tham chiếu:** [satnaing/shadcn-admin](https://github.com/satnaing/shadcn-admin) (`e16c87f213a5ba5e45964e9b67c792105ec74d26`, MIT License)

---

## 1. Hệ Thống Thiết Kế & Token Thống Nhất

- **`e:\pickleball\wwwroot\css\styles.css`**:
  - Chuẩn hóa biến thiết kế dùng chung giữa Admin Portal và Website Khách hàng: `--bg-page`, `--bg-surface`, `--bg-subtle`, `--text-primary`, `--text-secondary`, `--text-muted`, `--border-color`, `--sidebar-*`, `--sh-radius`.
  - Bổ sung bộ theme tối đầy đủ qua `html[data-theme="dark"]` nhưng **cách ly tuyệt đối vùng trắng `#FFFFFF` của mã QR** (`.spk-qr-well`, `.server-qr-frame`, `.spk-ticket-qr-well`, `.spk-ticket-light`) để đảm bảo máy quét QR ISO/IEC 18004 luôn nhận diện 100%.
  - Xây dựng hoàn chỉnh hệ thống layout `shadcn-admin`: `.sh-admin-shell`, `.sh-sidebar` (Desktop `256px` / Icon-collapsed `68px` / Mobile Sheet Drawer `< 1024px`), `.sh-topbar` (`sticky`), `.sh-cmd-dialog` (Command Palette `Ctrl+K`), `.sh-module-card`, `.sh-upload-dropzone`, `.sh-alert-*`, `.sh-subtle-panel`.
  - Chuẩn hóa `z-index` phân tầng: Sidebar (`900`), Mobile Drawer (`1050`), Modals (`.modal-overlay` = `1100`), Toast (`1200`), Fullscreen QR Zoom (`9999`).
  - Khắc phục `.sr-only`, `.text-nowrap`, `.court-option` (xếp dọc + active border), `.item-row` mobile stacking, nút `.pos-qty`, và responsive header `@media (max-width: 400px)` đạt `0px` tràn ngang tại `320px`.

## 2. Shell Quản Trị & Trang Quản Trị (`Areas/Admin/Views/...`)

- **`Areas/Admin/Views/Shared/_Layout.cshtml`**: Chuyển từ thanh subbar AdminLTE cũ sang `shadcn-admin` Sidebar + Sticky Topbar + Breadcrumb động + Nút chuyển Light/Dark + Hộp thoại Command Search (`Ctrl+K` / `Ctrl+B` / `Escape`) + Gating nhóm menu theo quyền (`Function.IsAdmin()`).
- **`Areas/Admin/Views/Home/{Index,Management,Checkin,Reports,Cms}.cshtml`**: Áp dụng `.sh-module-card`, `.sh-subtle-panel`, `.sh-card-header-pending`, `.sh-refund-panel`; loại bỏ màu nền cứng `#FFFFFF` xung đột Dark Mode trên bộ lọc ngày Báo cáo; ẩn đúng nhãn `.sr-only`.
- **`Areas/Admin/Views/Bookings/Index.cshtml`, `Accounts/Index.cshtml`, `Courts/Index.cshtml`**: Thay alert cứng bằng `.sh-alert-*`; sửa `table-hover` trên `/Admin/Courts` để hiển thị trọn vẹn cột `Thao tác` ở `1440px`; giữ đơn giá/phụ thu/trạng thái không ngắt dòng vụn.
- **`Areas/Admin/Views/Products/{Index,Create,Edit}.cshtml`**: Thay toàn bộ icon FontAwesome chết (`<i class="fas ...">`) bằng Iconify SVG (`<iconify-icon>`), chuẩn hóa nút `.btn-danger-outline` / `.btn-outline`, vùng kéo-thả `.sh-upload-dropzone` và đồng bộ Dark Mode.
- **`Areas/Admin/Views/Login/Index.cshtml`, `Register/Index.cshtml`**: Thiết kế lại màn hình xác thực Admin theo phong cách card tối giản của `shadcn-admin` + nhúng favicon SVG.

## 3. Website Khách Hàng & Script Tương Tác (`Views/...`, `wwwroot/js/...`)

- **`Views/Shared/_Layout.cshtml`**: Đồng bộ design token, thêm nút chuyển đổi chế độ Sáng/Tối `#siteThemeToggleBtn` (lưu `localStorage.spike_theme`), nhúng link điều hướng nhanh `Quản trị` cho phiên Staff/Admin, nhúng favicon SVG.
- **`Views/Account/Profile.cshtml` & `Login.cshtml`**: Đồng bộ thẻ `.paddle-chip`, thông báo `TempData` bằng semantic variables cho Dark Mode; tối ưu responsive header/card dưới `480px`.
- **`Views/Booking/Index.cshtml`, `Views/Product/Index.cshtml`, `Views/Home/Index.cshtml`, `Views/Shared/Components/Court/Default.cshtml`**: Gỡ các nền cứng xung đột Dark Mode, bảo toàn hoàn toàn khu vực QR và giỏ hàng Pro Shop V6.
- **`wwwroot/js/shared.js`**: Bổ sung điều khiển Sidebar (`toggleAdminSidebar`, `closeMobileAdminSidebar`, trap focus mobile drawer), Command Palette (`openCommandPalette`, `filterCommandPalette`), `toggleSpikeTheme`, và tối ưu `SpikeTicket.openFullscreenZoom` tái sử dụng trực tiếp SVG QR từ backend khi không bật logo giữa.
- **`wwwroot/js/management.js`, `dashboard.js`, `lookup.js`**: Sửa hiển thị `(Kho: X)` trong Modal POS và chống xuống dòng mã đặt sân `PB-XXXXXX` trên bảng tra cứu.

## 4. Kiểm Thử Tích Hợp Mới (`PickleballClub.Tests`)

- **`PickleballClub.Tests/ShadcnAdminV7ParityTests.cs`**: Bổ sung 4 test tích hợp HTTP/DOM kiểm chứng cấu trúc Shell `shadcn-admin`, phân quyền RBAC giữa Admin (`RoleId=1`) và Staff (`RoleId=3`), và tính nguyên vẹn của trang khách.
