# UI COMPONENT INVENTORY — SPIKE × NOTION DESIGN SYSTEM

Every component maps cleanly to Razor partials, CSS classes (`.sn-*` unified component classes layered harmoniously alongside backward-compatible hooks), and Vanilla JS utilities while preserving zero-dependency simplicity and full DOM compatibility.

---

## 1. Navigation & App Shells

| # | Component | File / Mount Target | Key Classes & Hooks | Purpose |
| :--- | :--- | :--- | :--- | :--- |
| 1 | **TopNavigation** | `Views/Shared/_Layout.cshtml` (`header.sn-topbar`) | `#siteMainNav`, `#headerAuthActionsMount`, `#siteThemeToggleBtn` | Clean 64px sticky top bar, hairline bottom border, minimal nav + member quick actions + single `#FF5520` primary CTA + responsive mobile nav drawer |
| 2 | **AppShell** | `Areas/Admin/Views/Shared/_Layout.cshtml` | `#shAdminShell`, `#shAdminSidebar`, `#shAdminTopbar` | Notion-workspace sidebar + breadcrumb topbar shell |
| 3 | **Sidebar** | `Areas/Admin/Views/Shared/_Layout.cshtml` | `#shSidebarToggleBtn`, `[data-testid="sh-nav-group-admin-only"]` | Grouped Operations vs. Management nav with Staff (`RoleId=3`) vs Admin (`RoleId=1`) separation, collapsible `240px <-> 64px` desktop / drawer mobile |
| 4 | **Command Palette** | `Areas/Admin/Views/Shared/_Layout.cshtml` | `#shCmdTriggerBtn`, `#shCommandModal` | Keyboard `Ctrl/Cmd+K` instant route launcher filtered by role |
| 5 | **Breadcrumbs** | `Areas/Admin/Views/Shared/_AdminPageHeader.cshtml` | `.sn-breadcrumbs`, `#adminPageTitle` | Clean path trail (`Quản trị / Quản lý vận hành`) inside Admin Topbar & subpages |

---

## 2. Structural Layout & Feedback Components

| # | Component | Implementation | Classes / Attributes | Usage |
| :--- | :--- | :--- | :--- | :--- |
| 6 | **PageHeader** | `Areas/Admin/Views/Shared/_AdminPageHeader.cshtml` & `.sn-page-header` | `.sn-page-title`, `.sn-page-subtitle`, `.sn-page-actions` | Replaces dark `.page-hero` marketing banners with scannable editorial headers |
| 7 | **StatCard / KPICard** | `.sn-stat-card` (`Dashboard`, `Reports`, `Profile`) | `.sn-stat-label`, `.sn-stat-value`, `.sn-stat-meta` | Tabular figures, concise metadata, zero noise |
| 8 | **DataTable** | `.sn-table-wrap` + `table.sn-table` | `.sn-table-scroll`, sortable header alignment, hairline borders | Clean row height, crisp right-aligned tabular currency columns, responsive horizontal overflow containment |
| 9 | **FilterBar** | `.sn-filter-bar` | `.sn-pill-group`, `[data-cat]`, `[data-filter]` | Pill filter tabs + inline controls for Pro Shop, Management, Reports, Admin CRUD |
| 10 | **SearchInput** | `.sn-search-field` | `#menuSearchInput`, `#shopOrderSearch` | Hairline input with crisp left search SVG & clear affordance |
| 11 | **DatePicker** | `.sn-date-field` + `#quickDateStrip` | `#bookingDatePicker`, `#qbDate`, `#lockDate`, `#reportFrom`, `#reportTo` | Clean native date picker enhanced with quick-date pill buttons (`Hôm nay`, `Ngày mai`, `+2`, ...) |
| 12 | **TimeSlotSelector & BookingMatrix** | `#bookingCourtMatrix`, `#timeSlotGrid`, `#bookingTimeline` | `.sn-matrix-table`, `.slot-btn`, `.sn-matrix-cell` | Synchronized **Court × Time** visual availability grid + quick duration pills (`#durationPills`) + hour timeline (`#bookingTimeline`) |
| 13 | **PriceSummaryCard** | `.sn-summary-card` (`Views/Booking/Index.cshtml`) | `#summaryDateTime`, `#summaryHourlyRate`, `#summaryCourtFee`, `#summaryTotal`, `#summarySplitPerPerson` | Sticky desktop summary card & collapsible mobile bottom bar |
| 14 | **CourtCard** | `Views/Shared/Components/Court/Default.cshtml` & `#courtsGrid` | `.sn-court-card` | Photo-first card with surface spec, live status badge, base price/hr, direct booking action |
| 15 | **ProductCard** | `Views/Shared/Components/Product/Default.cshtml` & `menu.js` | `.menu-item[data-product-id]`, `[data-role="media|img|stock-badge|stepper|cta"]` | Square product packshot stage, category tag, clear price + stock badge, tactile selected border & qty stepper |
| 16 | **CartPanel & BottomSheet** | `Views/Product/Index.cshtml` | `#shopCartPanel`, `#shopCartItems`, `#mobileShopCartBar`, `#mobileCartActionBtn` | Desktop sticky right Cart Panel + Mobile sticky summary bar toggling compact Bottom Sheet |
| 17 | **StatusBadge** | `.sn-badge` + `_StatusBadge.cshtml` | `.sn-badge-success`, `.sn-badge-info`, `.sn-badge-warning`, `.sn-badge-danger`, `.sn-badge-neutral` | Dual-coded status tags with semantic symbol/icon + accessible contrast |
| 18 | **Dialog / ConfirmDialog** | `.sn-modal` + `shared.js` (`window.SpikeUI.confirm`) | `.modal`, `.modal-box`, `#spkConfirmModal` | Keyboard-accessible modal dialog (`Escape` close, focus return, destructive confirmation without blocking `window.confirm`) |
| 19 | **Drawer (Slide-Over)** | `.sn-drawer-backdrop` + `.sn-drawer` (`Views/Admin/Home/Management.cshtml`) | `#mgmtDetailDrawer`, `#mgmtDrawerBody` | Non-destructive slide-over operational panel in Management (booking inspect, check-in, extend, move court, POS quick-add, payment ledger settle, finish session) |
| 20 | **DropdownMenu / Select / Checkbox** | `.sn-select`, `.sn-checkbox`, `.sn-dropdown` | Native styled form controls + menu popovers | Hairline borders, `18px` minimum hit targets, accessible labels |
| 21 | **FormField / Input / Dropzone** | `.sn-field`, `.sn-input`, `.sn-dropzone` | `#productImageDropzone`, `#productPreviewStage` | Unified form controls, validation feedback, drag-and-drop image staging |
| 22 | **Alert / Inline Banner** | `.sn-alert` | `#conflictBanner`, `#bookingDateNotice`, `#reportStatusBanner`, `#ledgerPartialFailureBanner` | High-contrast warning/error/info notices with icon prefix |
| 23 | **EmptyState / ErrorState / Skeleton** | `.sn-empty-state`, `.sn-error-state`, `.sn-skeleton` | `.sn-empty-icon`, `.sn-empty-title`, `.sn-retry-btn` | Honest empty/network failure UIs with manual retry CTAs (zero silent fallback or fake mock data) |
| 24 | **Toast Notification** | `#toast`, `#toastContainer` (`shared.js`) | `.sn-toast` | Non-intrusive top/bottom-right status feedback |
| 25 | **Pagination** | `.sn-pagination` | `#shopOrdersPagination` | Clean tabular page controls matching Notion list pagination |
| 26 | **Digital QR Ticket (`SpikeTicket`)** | `shared.js` (`renderDigitalTicket`) | `#spkDigitalTicketCard`, `.spk-ticket-qr-well`, `#qrDownloadPngBtn`, `#qrDownloadSvgBtn` | Protected high-contrast `#FFFFFF !important` quiet-zone QR module + ticket details + SVG/PNG download + printable card |
| 27 | **BookingTimeline (Admin Ops)** | `#mgmtTimelineGrid` (`Management.cshtml` + `management.js`) | `.sn-ops-timeline`, `.sn-ops-slot-block` | Columns = active courts, Rows = operating hours (`06:00–23:00`), clicking any occupied block opens `#mgmtDetailDrawer` |
