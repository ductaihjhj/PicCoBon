# Hệ Thống Design Tokens & Component Primitives V7 (`DESIGN_TOKENS.md`)

**Phiên bản:** Spike Pickleball V7 — Unified `shadcn-admin` Design System  
**Áp dụng cho:** Toàn bộ Admin Shell (`Areas/Admin/Views/**`) và Customer Web Shell (`Views/**`)

---

## 1. Hệ Thống Token Màu Sắc & Bề Mặt (`CSS Custom Properties`)

Lấy cảm hứng trực tiếp từ `src/styles/theme.css` của `satnaing/shadcn-admin` và hiệu chỉnh cho thương hiệu **Spike Pickleball Club**:

```css
:root {
  /* Shadcn-Admin Core Tokens (Light Mode Default) */
  --radius: 0.625rem;               /* 10px chuẩn shadcn-admin */
  --radius-xs: 0.375rem;            /* 6px cho badge, nút nhỏ */
  --radius-sm: 0.5rem;              /* 8px cho input, select, button */
  --radius-md: 0.75rem;             /* 12px cho card, panel */
  --radius-lg: 1rem;                /* 16px cho modal, hero card */

  --sh-bg: #F8FAFC;                 /* Slate-50 nền tổng thể dịu mắt */
  --sh-fg: #090D16;                 /* Deep Slate/Navy chữ chính (tương phản > 15:1) */
  --sh-card: #FFFFFF;               /* Bề mặt Card thuần khiết */
  --sh-card-fg: #090D16;
  --sh-popover: #FFFFFF;
  --sh-popover-fg: #090D16;

  /* Brand & Semantic Accents */
  --sh-primary: #0F172A;            /* Shadcn Slate-900 cho control quản trị chuẩn */
  --sh-primary-fg: #F8FAFC;
  --sh-brand: #FF5520;              /* Spike Kinetic Orange cho CTA hành động chính */
  --sh-brand-hover: #E04412;
  --sh-brand-fg: #FFFFFF;
  --sh-volt: #D8F826;               /* Spike Volt Lime cho điểm nhấn thể thao trên nền tối */

  --sh-secondary: #F1F5F9;          /* Slate-100 */
  --sh-secondary-fg: #0F172A;
  --sh-muted: #F1F5F9;
  --sh-muted-fg: #64748B;           /* Slate-500 (tương phản chuẩn AA >= 4.6:1 trên nền trắng) */
  --sh-accent: #F1F5F9;
  --sh-accent-fg: #0F172A;
  --sh-destructive: #DC2626;
  --sh-destructive-fg: #FFFFFF;

  --sh-border: #E2E8F0;             /* Viền 1px mảnh chuẩn shadcn */
  --sh-input: #CBD5E1;
  --sh-ring: rgba(255, 85, 32, 0.28);

  /* Sidebar Tokens (Shadcn-Admin AppSidebar) */
  --sidebar-width: 260px;
  --sidebar-width-collapsed: 68px;
  --sidebar-bg: #090D16;            /* Deep Court Slate Sidebar */
  --sidebar-fg: #E2E8F0;
  --sidebar-muted: #94A3B8;
  --sidebar-accent: rgba(255, 255, 255, 0.07);
  --sidebar-accent-fg: #FFFFFF;
  --sidebar-active-bg: #FF5520;
  --sidebar-active-fg: #FFFFFF;
  --sidebar-border: rgba(255, 255, 255, 0.08);
}

html[data-theme="dark"] {
  --sh-bg: #090D16;
  --sh-fg: #F8FAFC;
  --sh-card: #111827;
  --sh-card-fg: #F8FAFC;
  --sh-popover: #1E293B;
  --sh-popover-fg: #F8FAFC;
  --sh-primary: #F8FAFC;
  --sh-primary-fg: #0F172A;
  --sh-secondary: #1E293B;
  --sh-secondary-fg: #F8FAFC;
  --sh-muted: #1E293B;
  --sh-muted-fg: #94A3B8;
  --sh-accent: #1E293B;
  --sh-accent-fg: #F8FAFC;
  --sh-border: rgba(255, 255, 255, 0.11);
  --sh-input: rgba(255, 255, 255, 0.16);
}
```

> **Quy tắc bất biến cho Vé QR (`#spkDigitalTicketCard .spk-ticket-qr-well`):**
> Bất kể giao diện đang ở chế độ `Light` hay `Dark`, vùng chứa mã QR (`.spk-ticket-qr-well`) luôn bắt buộc giữ nền trắng tuyệt đối `#FFFFFF` (`background: #FFFFFF !important; padding: 14px;`) và module QR màu đen đậm để đảm bảo độ tương phản quét quang học ISO/IEC 18004 đạt 100%.

---

## 2. Typography & Thứ Bậc Chữ

- **Font Sans chính (`--font-sans`):** `'Plus Jakarta Sans', 'Inter', -apple-system, BlinkMacSystemFont, sans-serif` — Rõ nét cho bảng dữ liệu, nhãn form và điều hướng.
- **Font Display (`--font-display`):** `'Oswald', sans-serif` — Dùng có chọn lọc cho tiêu đề Hero trang khách và mã vé thể thao. Trong Admin Shell, tiêu đề trang (`PageHeader`) dùng `--font-sans` đậm (`font-weight: 700; letter-spacing: -0.025em; font-size: 1.5rem`) đúng chuẩn `shadcn-admin`.
- **Font Monospace (`--font-mono`):** `'JetBrains Mono', monospace` kèm `font-variant-numeric: tabular-nums` — Bắt buộc cho mọi số tiền VNĐ, mã đặt sân (`PB-...`), khung giờ (`17:00`), tồn kho và chỉ số KPI.

---

## 3. Danh Mục Component Dùng Chung V7

| Component | Lớp CSS chuẩn | Đặc tả theo `shadcn-admin` |
|---|---|---|
| **Admin App Shell** | `.sh-admin-shell`, `.sh-sidebar`, `.sh-sidebar-inset`, `.sh-topbar` | Sidebar trái đóng/mở (`260px` / `68px` desktop, Drawer mobile) + Topbar sticky `64px` + Main container. |
| **Command Search Dialog** | `#shCommandModal`, `.sh-cmd-dialog`, `.sh-cmd-list` | Hộp thoại tìm kiếm nhanh (`Ctrl+K` / `/`) cho phép nhảy nhanh tới mọi phân hệ Admin & trang khách. |
| **Page Header** | `.sh-page-header`, `.sh-page-title`, `.sh-page-desc`, `.sh-page-actions` | Thay thế khối banner tối cồng kềnh trong Admin bằng thanh tiêu đề gọn sắc nét chuẩn `shadcn-admin` (tiêu đề trái, cụm nút công cụ phải). |
| **KPI Metric Card** | `.stat-card`, `.stat-widget`, `.sh-kpi-card` | Khung `1px solid var(--sh-border)`, `border-radius: 12px`, `shadow-xs`, nhãn `0.8rem font-medium text-muted`, icon góc phải, số liệu `1.65rem font-bold tabular-nums`. |
| **Data Table & Toolbar** | `.table-wrap`, `.table`, `.sh-table-toolbar` | Header bảng nền `#F8FAFC`, chữ `0.75rem font-semibold text-slate-600`, dòng hover `#F8FAFC/80`, ô dữ liệu căn giữa dọc, thanh cuộn ngang mượt không che cột thao tác. |
| **Form Controls** | `.form-input`, `.form-select`, `.form-control` | Chiều cao chuẩn `38px`, viền `1px solid var(--sh-input)`, `border-radius: 8px`, focus ring `0 0 0 3px var(--sh-ring)`. |
| **Buttons** | `.btn`, `.btn-primary`, `.btn-secondary`, `.btn-outline`, `.btn-ghost`, `.btn-danger` | Chiều cao chuẩn `36px` (`btn-sm`: `32px`, `btn-xs`: `28px`, `btn-lg`: `42px`), bo góc `8px`, `font-weight: 600`. |
| **Status Badges** | `.badge`, `.badge-success`, `.badge-warning`, `.badge-danger`, `.badge-primary`, `.badge-neutral` | Dạng pill/rounded-md gọn (`padding: 0.2rem 0.55rem`, `font-size: 0.72rem`, `font-weight: 600`) với viền mảnh đồng màu. |
| **Dialogs / Modals** | `.modal-overlay`, `.modal`, `.modal-header`, `.modal-body`, `.modal-footer` | Nền mờ `backdrop-filter: blur(4px)`, hộp thoại viền `1px solid var(--sh-border)`, đóng bằng `Escape`, bẫy focus (`Focus Trap`) và không bị Sidebar che khuất (`z-index: 1100`). |
