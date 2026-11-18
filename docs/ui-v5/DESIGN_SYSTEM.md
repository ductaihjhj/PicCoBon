# Hệ Thống Thiết Kế Giao Diện — Spike Pickleball Design System (V5)

**Tài liệu**: `docs/ui-v5/DESIGN_SYSTEM.md`  
**Áp dụng cho**: Toàn bộ Customer Portal (`/`, `/Booking`, `/Product`, `/Lookup`, `/Account/*`) và Staff/Admin Portal (`/Admin`, `/Admin/Home/Management`, `/Admin/Home/Checkin`, `/Admin/Home/Reports`, `/Admin/Home/Cms`)  
**Tập tin CSS trung tâm**: [`wwwroot/css/styles.css`](file:///e:/pickleball/wwwroot/css/styles.css)

---

## 1. Định hướng Thẩm mỹ (Aesthetic Direction)

Spike Pickleball mang phong cách **High-Voltage Athletic Club & Tournament Precision**:
- **Bề mặt (Surfaces)**: Nền sáng thể thao sạch sẽ (`#F8FAFC` / `#FFFFFF`) cho khu vực thao tác ban ngày, kết hợp các khối Hero, Topbar Admin và Vé điện tử Dark Sport (`#0B111E` / `#1E293B`) tạo độ tương phản thị giác mạnh mẽ.
- **Điểm nhấn năng lượng (High-Voltage Accents)**: Cam Thể Thao (`#FF5520`) cho các CTA chính và Vàng Neon Trái Bóng Pickleball (`#D8F826`) cho huy hiệu nổi bật trên nền tối.
- **Độ rõ nét số liệu (Tabular Precision)**: Mọi mã vé (`PB-XXXXXX`), khung giờ (`17:00 - 18:00`), số tiền (`120.000 đ`) và bộ đếm giữ chỗ (`29:45`) đều sử dụng `font-variant-numeric: tabular-nums` và font monospace `JetBrains Mono` để dễ quét mắt ở quầy lễ tân.

---

## 2. Bảng Màu Chuẩn Hóa (Color Tokens & WCAG AA Contrast)

| Token CSS | Giá trị Hex / RGBA | Mục đích sử dụng | Tương phản chuẩn (WCAG AA) |
|---|---|---|---|
| `--brand-primary` | `#FF5520` | Nút hành động chính (Primary CTA), điểm nhấn thương hiệu | Chữ trắng `#FFFFFF` trên nền `#E04412` / `#FF5520` cho nút lớn |
| `--brand-primary-hover` | `#E04412` | Trạng thái `:hover` / `:active` của nút chính | `>= 4.6:1` với `#FFFFFF` |
| `--brand-volt` | `#D8F826` | Nhấn sáng trên nền tối (`--bg-dark`), huy hiệu VIP | Chữ `#0B111E` trên nền `#D8F826` (`13.8:1`) |
| `--bg-dark` | `#0B111E` | Hero banner, Admin header, nền vé QR Dark Sport | Chữ `#F8FAFC` trên nền `#0B111E` (`17.4:1`) |
| `--bg-dark-elevated` | `#1E293B` | Bề mặt thẻ tối, thanh hành động vé Dark Sport | Chữ `#F8FAFC` trên nền `#1E293B` (`13.5:1`) |
| `--bg-body` | `#F8FAFC` | Nền tổng thể các trang vận hành & đặt sân | Chữ chính `#0F172A` (`16.8:1`) |
| `--bg-surface` | `#FFFFFF` | Thẻ (card), bảng (table), modal, giếng QR (`.spk-qr-well`) | Chữ chính `#0F172A` (`18.1:1`) |
| `--text-primary` | `#0F172A` | Tiêu đề, nội dung chính trên nền sáng | `16.8:1` trên `--bg-body` |
| `--text-secondary` | `#334155` | Nhãn biểu mẫu, mô tả phụ trên nền sáng | `10.3:1` trên `--bg-surface` |
| `--text-muted` | `#475569` | Ghi chú phụ, метаdata (đã nâng cấp độ đậm đạt chuẩn AA) | `7.5:1` trên `--bg-surface` |
| `--color-emerald` | `#059669` | Trạng thái Sân trống / Đã duyệt / Đã thanh toán đủ | Nhãn `#065F46` trên nền `#D1FAE5` (`7.8:1`) |
| `--color-amber` | `#D97706` | Trạng thái Chờ duyệt / Giờ vàng / Thanh toán một phần | Nhãn `#92400E` trên nền `#FEF3C7` (`7.2:1`) |
| `--color-danger` | `#DC2626` | Trạng thái Đang thi đấu / Lỗi / Công nợ / Từ chối | Nhãn `#991B1B` trên nền `#FEE2E2` (`7.1:1`) |
| `--color-info` | `#2563EB` | Trạng thái Thông tin / Đang đồng bộ / Báo giá máy chủ | Nhãn `#1E40AF` trên nền `#DBEAFE` (`8.2:1`) |

### Quy tắc Tương phản Đặc biệt cho Vé QR (`SpikeTicket` — `UI-B01`)
- **Giếng QR (`.spk-qr-well`)**: Bắt buộc giữ nền trắng tinh khiết `#FFFFFF` với quiet zone tối thiểu `4 modules` dù vé đang ở chế độ **Dark Sport** hay **Light Print**, đảm bảo 100% máy quét quang học và ZXing giải mã tức thì.
- **Nút phụ trong Dark Sport (`.spk-ticket-dark .btn-outline`)**: Sử dụng `background: rgba(255, 255, 255, 0.08)`, `color: #F8FAFC`, `border: 1.5px solid #64748B`, khi `:hover` chuyển sang `background: rgba(255, 255, 255, 0.16); border-color: #CBD5E1; color: #FFFFFF`.

---

## 3. Hệ Thống Chữ (Typography Scale)

- **`--font-display`**: `'Oswald', sans-serif` — Dùng cho tiêu đề trang, tên sân, số liệu KPI lớn, tổng tiền thanh toán (`font-weight: 700`, `letter-spacing: 0.02em`).
- **`--font-sans`**: `'Plus Jakarta Sans', -apple-system, BlinkMacSystemFont, sans-serif` — Dùng cho toàn bộ văn bản giao diện, biểu mẫu, nút bấm, bảng dữ liệu (`line-height: 1.5`).
- **`--font-mono`**: `'JetBrains Mono', monospace` — Dùng cho mã đặt sân (`PB-XXXXXX`), token bảo mật (`bk_...`), khung giờ, số tiền sổ cái (`font-variant-numeric: tabular-nums`).

| Cấp độ | Kích thước (Fluid / Responsive) | Line Height | Ứng dụng |
|---|---|---|---|
| Display Hero | `clamp(1.85rem, 4vw, 3.25rem)` | `1.12` | Tiêu đề Hero trang chủ |
| Page Title (`h1`) | `clamp(1.5rem, 2.8vw, 2.25rem)` | `1.2` | Tiêu đề chính các trang Đặt sân, Cửa hàng, Tra cứu |
| Section Title (`h2`) | `clamp(1.2rem, 2vw, 1.5rem)` | `1.28` | Tiêu đề khối chức năng, thẻ thống kê |
| Card Title (`h3`) | `1.05rem` – `1.15rem` | `1.35` | Tên sân, tên sản phẩm Pro Shop, tiêu đề Modal |
| Body Default | `0.9375rem` (`15px`) | `1.5` | Nội dung chuẩn, ô nhập liệu, dòng bảng |
| Caption / Badge | `0.75rem` – `0.8125rem` (`12px–13px`) | `1.35` | Huy hiệu trạng thái, chú thích nhỏ, nhãn cột |

---

## 4. Khoảng Cách, Bo Góc, Đổ Bóng & Lớp Hiển Thị (Spacing, Radius, Shadows & Z-Index)

- **Spacing Scale (`4px` base)**:
  - `--space-1: 0.25rem (4px)`
  - `--space-2: 0.5rem (8px)`
  - `--space-3: 0.75rem (12px)`
  - `--space-4: 1rem (16px)`
  - `--space-5: 1.25rem (20px)`
  - `--space-6: 1.5rem (24px)`
  - `--space-8: 2rem (32px)`
  - `--space-10: 2.5rem (40px)`
- **Border Radius**:
  - `--radius-xs: 6px` (Huy hiệu nhỏ, ô giờ trong ma trận)
  - `--radius-sm: 10px` (Ô nhập liệu, nút bấm tiêu chuẩn)
  - `--radius-md: 14px` (Thẻ sân, thẻ sản phẩm, khối thông báo)
  - `--radius-lg: 20px` (Khung vé QR `SpikeTicket`, thẻ lớn)
  - `--radius-xl: 24px` (Modal dialog, Hero container)
  - `--radius-full: 9999px` (Nút pill, bộ lọc danh mục)
- **Z-Index Scale**:
  - `--z-sticky: 90` (Thanh điều hướng, cột tóm tắt đặt sân)
  - `--z-mobile-bar: 95` (Thanh giỏ hàng cố định trên mobile `#mobileShopCartBar`)
  - `--z-header: 100` (Main Header & Admin Topbar)
  - `--z-modal: 1000` (Modal backdrop & dialog)
  - `--z-lightbox: 1150` (QR Zoom Lightbox)
  - `--z-toast: 1200` (Toast notifications)

---

## 5. Chuẩn Tương Tác, Accessibility & Responsive Breakpoints

1. **Kích thước vùng chạm (Touch Targets)**:
   - Mọi nút bấm, tab lọc danh mục, pill chọn ngày, nút tăng/giảm số lượng trên mobile đảm bảo diện tích tương tác tối thiểu `44px × 44px` (hoặc chiều cao tối thiểu `40px–44px` với khoảng cách an toàn).
2. **Trạng thái Focus (`:focus-visible`)**:
   - Hiển thị vòng focus rõ nét: `outline: 3px solid rgba(255, 85, 32, 0.45); outline-offset: 2px;` trên nền sáng và `outline: 3px solid #D8F826; outline-offset: 2px;` trên nền tối.
3. **Giảm chuyển động (`prefers-reduced-motion: reduce`)**:
   - Tắt hoặc rút ngắn toàn bộ animation/transition xuống `< 0.01ms` cho người dùng nhạy cảm với chuyển động.
4. **Dải Breakpoint Kiểm thử Bắt buộc**:
   - **Mobile nhỏ**: `320px`, `360px`, `375px`
   - **Mobile phổ thông**: `390px`, `414px`, `430px`
   - **Tablet**: `768px`, `1024px`
   - **Desktop & Widescreen**: `1280px`, `1440px`, `1920px`
