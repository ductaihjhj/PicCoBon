# Nghiên Cứu Nguồn Tham Chiếu Thiết Kế `satnaing/shadcn-admin` (`REFERENCE_REVIEW.md`)

**Ngày khảo sát:** 2026-11-16  
**Repository tham chiếu chính:** `https://github.com/satnaing/shadcn-admin`  
**Commit SHA đã truy cập trực tiếp:** `e16c87f213a5ba5e45964e9b67c792105ec74d26` (`2026-06-11T10:08:35Z`)  
**Giấy phép (License):** MIT License (`SPDX-ID: MIT`) — Cho phép tham khảo, chuyển dịch thiết kế và tái cấu trúc sang ASP.NET Core MVC Razor/CSS kèm ghi nhận nguồn tác giả `@satnaing`.

---

## 1. Các File Nguồn Đã Truy Cập & Phân Tích Trực Tiếp

Từ nhánh chính tại commit `e16c87f213a5ba5e45964e9b67c792105ec74d26`, các tệp cấu trúc và thiết kế sau đã được đọc và phân tích trực tiếp:
- `README.md` — Tổng quan kiến trúc, danh sách component tùy chỉnh, hệ thống icon (`Lucide`), hỗ trợ Light/Dark mode và Command Search.
- `src/styles/theme.css` — Hệ thống biến màu chuẩn `shadcn/ui` (`--background`, `--foreground`, `--card`, `--popover`, `--primary`, `--secondary`, `--muted`, `--accent`, `--destructive`, `--border`, `--input`, `--ring`, `--radius`, `--sidebar-*`).
- `src/styles/index.css` — Quy tắc cuộn mỏng (`scrollbar-width: thin`), ngăn zoom input trên mobile (`font-size: 16px`), utility container và hiệu ứng chuyển động.
- `src/components/layout/authenticated-layout.tsx` — Cấu trúc khung quản trị gồm `SkipToMain`, `AppSidebar` và `SidebarInset` (`@container/content`, chiều cao linh hoạt `min-h-svh`).
- `src/components/layout/app-sidebar.tsx` & `src/components/layout/data/sidebar-data.ts` — Cấu trúc Sidebar chia làm 3 vùng:
  1. `SidebarHeader`: Brand / Team Switcher (`Spike Pickleball Admin · Enterprise Operations`).
  2. `SidebarContent`: Các nhóm điều hướng (`NavGroup`) có nhãn phân nhóm (`Vận hành trực tiếp`, `Quản lý danh mục`, `Tài chính & Hệ thống`), mỗi mục có icon `Lucide`, tên mục, trạng thái active nổi bật và hỗ trợ thu gọn (`collapsible="icon"`).
  3. `SidebarFooter`: Khối thông tin người dùng đang đăng nhập (`NavUser`) kèm vai trò và nút đăng xuất nhanh.
- `src/components/layout/header.tsx` — Thanh `Header` cao `h-16` (`64px`), cố định đầu trang (`sticky top-0 z-50`), bên trái có nút đóng/mở Sidebar (`SidebarTrigger` kiểu `outline`) + vạch ngăn dọc (`Separator orientation="vertical" h-6`), bên phải có ô tìm kiếm nhanh (`Search` kèm phím tắt `⌘K` / `Ctrl+K`), nút chuyển giao diện Sáng/Tối (`ThemeSwitch`) và menu tài khoản (`ProfileDropdown`).
- `src/components/layout/main.tsx` — Vùng nội dung chính (`px-4 py-6`, giới hạn `max-w-7xl` căn giữa trên màn hình siêu rộng).
- `src/features/dashboard/index.tsx` — Bố cục trang Dashboard chuẩn `shadcn-admin`:
  - Tiêu đề trang (`text-2xl font-bold tracking-tight`) + cụm nút hành động bên phải.
  - Dải `TabsList` chuyển đổi chế độ xem.
  - Lưới 4 thẻ KPI (`grid gap-4 sm:grid-cols-2 lg:grid-cols-4`): mỗi `Card` có `CardHeader` (`flex flex-row items-center justify-between space-y-0 pb-2`), `CardTitle` (`text-sm font-medium`), icon `h-4 w-4 text-muted-foreground`, `CardContent` chứa giá trị lớn (`text-2xl font-bold tabular-nums`) và dòng mô tả phụ (`text-xs text-muted-foreground`).
  - Lưới chính `grid-cols-1 gap-4 lg:grid-cols-7` (cột biểu đồ/lịch `col-span-4` và cột danh sách/giao dịch gần đây `col-span-3`).
- `src/components/ui/{card,table,badge,button}.tsx` — Các nguyên mẫu UI với đường viền mảnh `1px solid var(--border)`, bo góc `rounded-xl` (`10px–12px`) cho Card, `rounded-md` (`6px–8px`) cho Button/Input/Badge, và bóng đổ tinh gọn (`shadow-xs`).

---

## 2. Chiến Lược Chuyển Dịch Sang ASP.NET Core MVC Razor (Không Chuyển Sang React SPA)

1. **Giữ nguyên 100% kiến trúc ASP.NET Core MVC (.NET 10), Razor Views, EF Core và Vanilla JS:**
   - Không cài đặt React, Vite hay TanStack Router để tránh phá vỡ kiến trúc MVC hiện hữu.
   - Chuyển dịch chính xác cấu trúc DOM, hệ thống CSS Custom Properties (`--sh-*` / `--sidebar-*`) và tương tác đóng/mở Sidebar, Command Search (`Ctrl+K`), Theme Switch (`Light`/`Dark`) sang `Areas/Admin/Views/Shared/_Layout.cshtml`, `Views/Shared/_Layout.cshtml`, `wwwroot/css/styles.css` và `wwwroot/js/shared.js`.
2. **Cá nhân hóa bản sắc Spike Pickleball trên nền `shadcn-admin`:**
   - Kết hợp sự tối giản, sắc sảo của `shadcn-admin` (`Slate/Zinc` surfaces, `1px` crisp borders, `tabular-nums` metrics, compact `h-9` controls) với điểm nhấn thể thao đặc trưng của **Spike Pickleball Club** (`#FF5520` Kinetic Orange cho primary CTA, `#D8F826` Volt Lime cho trạng thái sân/vé nổi bật, `#090D16` Deep Court Navy cho dark surfaces).
3. **Phân tách rõ Admin Shell và Customer Web Shell:**
   - **Admin (`/Admin`, `/Management`, `/Checkin`, `/Reports`, `/Cms`, `/Admin/*`):** Sử dụng `shadcn-admin` App Shell gồm Sidebar trái thu gọn được (`260px` ↔ `68px` trên desktop, Sheet trượt có backdrop & focus trap trên mobile `< 1024px`) + Sticky Topbar `64px` (Sidebar Trigger, Breadcrumb động, Command Search `Ctrl+K`, Theme Toggle, User Profile Pill) + vùng `Main` chuẩn `shadcn-admin`.
   - **Customer Website (`/`, `/Booking`, `/Product`, `/Lookup`, `/Account/*`):** Giữ bố cục Website Đặt sân & Thương mại điện tử (Top Navigation Bar + Hero + Quick Booking + Pro Shop Grid), nhưng đồng bộ toàn bộ token màu sắc, chữ, độ cong viền, bảng biểu, badge, form control, modal và toast theo chuẩn `shadcn-admin` V7.
