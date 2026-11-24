# SPIKE × NOTION PRODUCTIVITY — DESIGN SYSTEM SPECIFICATION
> **Reference Spec:** `www.notion.com-DESIGN.md` adapted for **Spike Pickleball Club**
> **Design Philosophy:** *"Sports energy outside, operational clarity inside."*

---

## 1. Visual Identity & Color Tokens

Notion's light editorial canvas (`#FFFFFF`) and warm stone surface (`#F6F5F4`) provide calm, operational clarity, while **Spike Orange (`#FF5520`)** serves as the single authoritative brand & primary action accent. Notion's reference blue (`#0075DE`) is strictly scoped to informational states and links so there are never competing primary buttons.

### 1.1 Core Surface, Text & Brand Tokens (`:root` Light Default)

| Category | CSS Token | Value | Usage Rule |
| :--- | :--- | :--- | :--- |
| **Brand Primary** | `--brand-primary` | `#FF5520` | Primary CTAs (`Đặt sân ngay`, `Xác nhận đặt`, `Thêm món`), active tab bar, focus emphasis |
| **Brand Hover** | `--brand-primary-hover` | `#E94A16` | Hover/pressed state for primary CTA |
| **Brand Tint** | `--brand-primary-tint` | `#FFF1EB` | Active selection surface, soft badge background, subtle callout fill |
| **Brand Ink onTint** | `--brand-primary-ink` | `#B8320A` | Accessible dark-orange text on `#FFF1EB` (`> 5.5:1` contrast) |
| **Canvas** | `--background` | `#FFFFFF` | Primary page canvas (Public Home, Booking, Pro Shop, Admin workspace) |
| **Surface** | `--surface` | `#FFFFFF` | Cards, tables, forms, modals, slide-over drawers |
| **Surface Secondary** | `--surface-secondary` | `#F6F5F4` | Admin sidebar rail, warm section bands, table header fill, hover rows, input rest fill |
| **Surface Muted** | `--surface-subtle` | `#FAFAFA` | Zebra rows, read-only panels, square product stage |
| **Text Primary** | `--text-primary` | `#111827` | Headings, table figures, court names, SKU titles (`17.7:1` on `#FFFFFF`) |
| **Text Secondary** | `--text-secondary` | `#615D59` | Body copy, subtitles, descriptions (`6.5:1` on `#FFFFFF`) |
| **Text Muted** | `--text-muted` | `#716D69` | Captions, table column labels, placeholders (`> 4.8:1` on `#FFFFFF` for WCAG AA) |
| **Border Default** | `--border-default` | `#E5E7EB` | Hairline `1px` card borders, table gridlines, structural dividers |
| **Border Strong** | `--border-strong` | `#D1D5DB` | Form control borders, hover borders, secondary button borders |
| **Border Focus** | `--border-focus` | `#FF5520` | Active input border paired with outer focus ring |

### 1.2 Semantic Status Tokens (Accessibility Dual-Coded)

Every status indicator pairs **background tint + high-contrast text/border + semantic icon or concise label**:

| State | Icon | Light Bg | Light Text / Ink | Border | Semantics |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Available / Open** | `○` / Check | `#F0FDF4` | `#166534` | `#BBF7D0` | Slot available to book |
| **Selected / Active** | `●` / Checkmark | `#FF5520` | `#FFFFFF` | `#E94A16` | User's chosen slot or active tab |
| **Booked / Playing** | `■` / Court | `#EFF6FF` | `#1D4ED8` | `#BFDBFE` | Approved, checked-in, or paid session (`Notion Info Blue #0075DE` family) |
| **Pending / Unpaid Hold** | `◷` / Clock | `#FFFBEB` | `#92400E` | `#FDE68A` | Pending approval with TTL hold timer |
| **Maintenance / Cancelled / Overdue** | `✕` / Wrench | `#FEF2F2` | `#991B1B` | `#FECACA` | Locked court, error, refunded, cancelled |
| **Past / Inactive / Neutral** | `—` | `#F3F4F6` | `#4B5563` | `#E5E7EB` | Expired time window or inactive SKU |

### 1.3 Dark Mode Tokens (`[data-theme="dark"]`)

Adapted from Notion's dark workspace surface architecture (`#090F16` / `#181D24` / `#30353B`):
- `--background`: `#0B1117`
- `--surface`: `#131A22`
- `--surface-secondary`: `#181F28`
- `--surface-subtle`: `#1D2530`
- `--text-primary`: `#F3F4F6`
- `--text-secondary`: `#B0B7C3`
- `--text-muted`: `#8B949E`
- `--border-default`: `#26303D`
- `--border-strong`: `#364354`
- **QR Digital Ticket Invariant:** `#spkDigitalTicketCard` and `.spk-ticket-qr-well` ALWAYS enforce `#FFFFFF !important` background and `#000000` QR modules in both light and dark modes so ZXing optical decoding never fails.

---

## 2. Typography Architecture

- **UI / Display Stack:** `'NotionInter'`, `'Inter'`, `'Plus Jakarta Sans'`, `-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif` (Google Fonts `Plus Jakarta Sans` + `Inter` + `JetBrains Mono` loaded in `_Layout` with `display=swap` and graceful offline system fallbacks).
- **Monospace Stack:** `'JetBrains Mono', ui-monospace, SFMono-Regular, Menlo, monospace` (`font-variant-numeric: tabular-nums` enforced for all `VNĐ` currency values, booking codes `PB-*`, `ORD-*`, durations, and timestamps).

| Scale Token | Desktop Size | Mobile Size (`<=640px`) | Weight | Line Height | Letter Spacing | Usage |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Hero Display** | `48px` (`3rem`) | `32px` (`2rem`) | `750` | `1.08` | `-0.035em` | Customer landing hero headline |
| **Page Title (H1)** | `28px` (`1.75rem`) | `22px` (`1.375rem`) | `700` | `1.2` | `-0.025em` | Public subpage & Admin workspace PageHeader |
| **Section Heading (H2)** | `20px` (`1.25rem`) | `18px` (`1.125rem`) | `650` | `1.28` | `-0.018em` | Section headers, Dashboard modules |
| **Card Heading (H3)** | `16px` (`1rem`) | `15px` | `600` | `1.35` | `-0.01em` | Card titles, Drawer headers, Modals |
| **Body Default** | `15px` (`0.9375rem`) | `15px` | `400–500` | `1.5` | `0` | Prose, instructions, empty states |
| **Compact UI / Table** | `14px` (`0.875rem`) | `14px` | `400–600` | `1.4` | `0` | Table cells, inputs, buttons, nav links |
| **Caption / Meta** | `12.5px` | `12.5px` | `500` | `1.35` | `0` | Helper text, badges, timestamps, subtle tags |

**Editorial Copy Restraint Rules (`.agent/skills/frontend-design/SKILL.md`):**
- Zero gratuitous tracked uppercase micro-eyebrows stacked above every card/section.
- Zero decorative filler bullets (`Feature · Tag · Subtag`) or hollow arrow chains in headings.
- Concise, clear Vietnamese operational copy free of marketing hype inside Admin modules.

---

## 3. Shape, Elevation, Spacing & Layout Grid

### 3.1 Border Radius
- `--radius-sm`: `4px` — checkboxes, small inline badges, compact slot cells, kbd chips
- `--radius-md`: `8px` — buttons, form inputs, table action pills, dropdown items, alerts
- `--radius-lg`: `12px` — cards, drawers, dialogs, QR ticket card, product media stage, KPI panels

### 3.2 Elevation (Hairline First, Micro-Shadow Second)
Inspired directly by `www.notion.com-DESIGN.md`:
- **Resting Cards & Panels:** `border: 1px solid var(--border-default); box-shadow: none;` — clean architectural separation without murky grey blur shadows.
- **Interactive Hover Card:** `border-color: var(--border-strong); box-shadow: 0 2px 8px rgba(17, 24, 39, 0.04);`
- **Floating Popovers / Sticky Summary / Dropdowns:** `--shadow-float: 0 4px 18px rgba(0, 0, 0, 0.06), 0 1px 3px rgba(0, 0, 0, 0.04);`
- **Modals & Slide-Over Drawers:** `--shadow-overlay: 0 16px 40px -8px rgba(17, 24, 39, 0.16), 0 4px 12px rgba(17, 24, 39, 0.06);`

### 3.3 Spacing Scale & Containers
- **Base Grid:** `4, 8, 12, 16, 20, 24, 32, 40, 48, 64px` (`--space-1` through `--space-16`).
- **Public Max Container:** `1200px` centered (`padding-inline: 24px` desktop, `16px` mobile).
- **Public Topbar Height:** `64px` fixed/sticky white bar with `1px solid var(--border-default)` bottom hairline (`50%` opacity feel in scrolled transparency transition, crisp solid `#FFFFFF` default).
- **Admin Sidebar Width:** `240px` expanded (`64px` collapsed desktop `--collapsed-width`, full height drawer on `< 992px`).
- **Admin Content Padding:** `24px–32px` desktop, `16px` mobile.

### 3.4 Responsive Breakpoints
- `<= 640px` (`mobile`): Single-column grids, sticky mobile bottom cart/booking summary bars, `44×44px` hit areas, collapsible filters.
- `641px – 1023px` (`tablet`): Compact grids, off-canvas Admin sidebar drawer, responsive table horizontal wrappers (`overflow-x: auto` with clean scroll containment).
- `>= 1024px` (`desktop`): Two-column Booking Matrix (`1fr 360px`), Pro Shop grid + sticky right Cart (`1fr 340px`), persistent collapsible Admin Sidebar + Breadcrumb Topbar (`1440px` max inner workspace fluency).

---

## 4. Motion & Accessibility Specification

- **Duration Scale:** `--ease-out: cubic-bezier(0.16, 1, 0.3, 1);`, fast hover `120ms`, button/card state `180ms`, drawer/modal slide `220ms`, image hover scale max `1.03`.
- **Forbidden Animations:** Infinite pulse loops on resting UI, bouncy overshoot scaling on buttons, continuous shimmer when idle, transform over QR canvas.
- **Reduced Motion:**
  ```css
  @media (prefers-reduced-motion: reduce) {
    *, *::before, *::after {
      animation-duration: 0.01ms !important;
      animation-iteration-count: 1 !important;
      transition-duration: 0.01ms !important;
      scroll-behavior: auto !important;
    }
  }
  ```
- **Keyboard & Focus Accessibility:** Visible `:focus-visible` ring (`0 0 0 2px var(--surface), 0 0 0 4px rgba(255, 85, 32, 0.45)`); `Escape` closes drawers/modals/command palette; semantic `<button>` and `<label for>` bindings across all forms and booking cells.
