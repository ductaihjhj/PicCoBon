# UI REDESIGN CHANGELOG — SPIKE × NOTION PRODUCTIVITY
> **Branch:** `feat/notion-redesign` (branched from `fix/safe-logic-remediation`)
> **Scope:** Complete UI/UX modernization across Customer & Admin surfaces with zero schema or business logic mutation.

---

## 1. Design System & Modular CSS Layer (`wwwroot/css/`)
- **`wwwroot/css/tokens.css` (New):** Standardized Notion-inspired light surfaces (`#FFFFFF`, `#F6F5F4`, `#FAFAFA`), dark inks (`#111827`, `#615D59`), hairline borders (`#E5E7EB`, `#D1D5DB`), Spike Orange primary CTA (`#FF5520` / `#E94A16`), dark mode adaptation (`#0B1117` / `#131A22` / `#181F28`), radii (`4/8/12/16px`), micro-shadows, tabular monospace utilities, and `@media (prefers-reduced-motion: reduce)`.
- **`wwwroot/css/components.css` (New):** Unified buttons, cards, dual-coded status badges, data tables, filter pills, search inputs, slide-over drawers (`.sn-drawer-backdrop`, `.sn-drawer`), alerts, empty/skeleton states, modals, pagination, focus rings, and AdminLTE bridge rules inside `.sh-admin-shell`.
- **`wwwroot/css/layouts.css` (New):** 64px sticky Notion Customer TopNavigation + mobile nav drawer (`.site-nav.is-open`), warm stone Footer, and Notion-workspace Admin AppShell (`.sh-sidebar` `#F6F5F4` / `#141B23`, crisp active indicator bar, `.sh-topbar`, mobile responsive drawer with `0px` horizontal overflow).
- **`wwwroot/css/pages.css` (New):** Bright Notion editorial Home hero (`sn-home-hero`), Customer Booking `Court × Time Slot Matrix` (`sn-matrix-table`, dual-coded cell states, mobile sticky bottom bar), Pro Shop minimalist hover cards & frosted category chips, Management `Court × Time` Scheduling Timeline (`sn-ops-timeline-table`), and unconditional `#FFFFFF !important` QR well protection.
- **`wwwroot/css/styles.css`:** Switched `--font-display` source font import from `Oswald` to `'Inter', 'Plus Jakarta Sans'` so headings eliminate condensed uppercase distortion.

## 2. Shared Razor Partials & Shell Layouts
- **`Views/Shared/_DesignSystemCss.cshtml` (New):** Central CSS sheet partial loaded in all Layouts and `Layout = null` Auth pages.
- **`Areas/Admin/Views/Shared/_AdminPageHeader.cshtml` (New):** Structured Admin PageHeader replacing AdminLTE headers and dark `.page-hero` banners.
- **`Views/Shared/_Layout.cshtml`:** Clean 64px topbar + accessible `#mainContent` skip target, fixed mobile hamburger button (`#siteMobileNavBtn`), and removed dark inline color overrides (`#FFF`, `--color-volt`) inside light/dark header and footer.
- **`Areas/Admin/Views/Shared/_Layout.cshtml`:** Retained all protected selectors (`#shAdminShell`, `#shAdminSidebar`, `#shAdminTopbar`, `#shSidebarToggleBtn`, `#shCmdTriggerBtn`, `#shThemeToggleBtn`, `#shCommandModal`, `data-testid="sh-nav-group-admin-only"`, `"Staff Operator"`), applied Notion workspace styling and `.sh-admin-main`.
- **`Views/Shared/Components/MenuTop/Default.cshtml`:** Preserved `tb_Menu` SSR navigation with clean default route fallback when table has no level-1 items.
- **`wwwroot/js/shared.js`:** Added working `toggleMobileNav()` for mobile customer menu toggle and `window.SpikeUI` (`openDrawer`, `closeDrawer`, `confirm` dialog with `Escape` support & focus restoration).

## 3. Customer Experience Views & Scripts
- **`Views/Home/Index.cshtml`, `Views/Shared/Components/Court/Default.cshtml`, `wwwroot/js/dashboard.js`:** Converted hero from dark overlay banner into split bright Notion editorial hero + `/assets/court-vip-arena.png` live monitor (`#heroMiniCourts` contrast tokens fixed), added Quick 3-Step Booking Guide, upgraded pricing card contrast (`.pricing-card.featured`), and fixed past-slot background token (`var(--surface-secondary)`).
- **`Views/Booking/Index.cshtml`, `wwwroot/js/booking.js`:** Added interactive **Court × Time Slot Matrix (`#bookingCourtMatrix`)** synchronized bidirectionally with `selectCourt`, `selectStartTime`, `renderTimeline`, and server quote pricing via `selectMatrixSlot()`, plus dual-coded semantic legend and mobile sticky summary bar (`#mobileBookingBar`).
- **`Views/Product/Index.cshtml`:** Updated Pro Shop page header and `#shopCartPanel` header to Notion surface tokens while preserving every `[data-product-id]` / `[data-role]` / `#mobileShopCartBar` / `#modalShopOrder` hook.
- **`Views/Lookup/Index.cshtml`:** Upgraded search bar, results `.sn-table`, empty state, and QR modal (`#modalTicketQR`) while keeping `SpikeTicket` and compat IDs intact.
- **`Views/Account/Profile.cshtml`:** Converted Member Passport Card from dark `#0A0E17` neon box into crisp light/dark token-aware card (`var(--surface)` / `var(--border-default)`).
- **`Views/Account/Login.cshtml`:** Replaced glowing dark ambient backdrop/orbs with clean `#F6F5F4` / `#FFFFFF` Notion split auth stage while retaining the interactive Arena right showcase, mascot eye tracking, strength meter, CSRF, and WCAG AA contrast links (`#B8320A`).

## 4. Admin Operations, CRUD, Reports, CMS & Auth Views
- **`Areas/Admin/Views/Home/Index.cshtml`:** Removed `.page-hero` dark marketing banner; implemented structured operational KPI strip and launchpad grid.
- **`Areas/Admin/Views/Home/Management.cshtml` & `wwwroot/js/management.js`:** Added visual **Court × Time Scheduling Timeline (`#mgmtTimelineGrid`)** + **Slide-Over Detail Drawer (`#mgmtDetailDrawer`)** calling existing approve/reject/check-in/POS/ledger/finish actions; fixed POS & Ledger panel tokens.
- **`Areas/Admin/Views/Home/Checkin.cshtml`:** Replaced `.page-hero` with `.sn-page-header` and removed inline `style="display:none"` from `#checkinResult` so `.checkin-result.show` toggles visibility properly.
- **`Areas/Admin/Views/Home/Reports.cshtml` & `Areas/Admin/Views/Home/Cms.cshtml`:** Replaced `.page-hero` dark banners with structured `.sn-page-header` and updated CMS Live Preview box to Notion light surfaces.
- **`Areas/Admin/Views/{Bookings,Accounts,Courts,Menus,Products}/*.cshtml`:** Removed leftover `.content-wrapper`, `.content-header`, `.container-fluid`, `.card-primary`, and `color:var(--color-volt)` hacks; normalized CRUD forms, tables, and product deletion confirmation while preserving all `asp-for`, `asp-action`, `@Html.AntiForgeryToken()`, image upload dropzone IDs, and `data-testid` attributes.
- **`Areas/Admin/Views/Login/Index.cshtml` & `Areas/Admin/Views/Register/Index.cshtml`:** Converted from dark radial backdrop to crisp Notion `.sh-auth-card` on `#F6F5F4` loading `_DesignSystemCss.cshtml`.
