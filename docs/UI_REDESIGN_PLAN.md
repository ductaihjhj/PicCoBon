# SPIKE PICKLEBALL CLUB — UI/UX REDESIGN PLAN
## Architecture & Execution Roadmap (`SPIKE × NOTION PRODUCTIVITY`)

> **Branch:** `feat/notion-redesign` (branched from verified Master Prompt 01 baseline on `fix/safe-logic-remediation`)
> **Philosophy:** *"Sports energy outside, operational clarity inside."*
> **Reference Design System:** `www.notion.com-DESIGN.md` fused with Spike Orange (`#FF5520`) brand primary accent.

---

## 1. Release Gate Precondition Verification

Before initiating any UI modifications, Master Prompt 01 verification passed cleanly with zero regressions:
- Build: `dotnet build PickleballClub.csproj -c Release` — 0 warnings, 0 errors.
- Automated Tests: `dotnet test` — 38/38 passed (including `ShadcnAdminV7ParityTests`, `V4ReleaseBlockersAndQrTests` 105-QR matrix, `ProShopV6ImageAndApiTests`, `SafeRemediationRegressionTests`).
- JS Unit Tests: `node js_unit_tests.mjs` — passed across 4 timezones.
- Chrome E2E: 16 core operational & customer flows passed on isolated SQL Express instance; production database `PickleballClub` left completely untouched.
- Baseline Snapshots: Committed onto `fix/safe-logic-remediation`, patch exported to scratch, and true `Before` screenshots captured into `docs/ui-notion/screenshots/before/`.

---

## 2. Baseline Visual & UX Audit Findings

Inspecting the existing views (`Views/*`, `Areas/Admin/Views/*`) and `wwwroot/css/styles.css` (~4,841 lines) surfaced six structural UX inconsistencies:

1. **Fragmented Design Language (Customer vs. Admin):** Customer pages use a dark slate sticky top header (`#0F172A`) and heavy gradient `.web-hero` banners, while Admin mixes dark marketing `.page-hero` banners on `/Admin`, `/Management`, `/Checkin`, `/Reports`, and `/Cms` with legacy AdminLTE markup.
2. **Legacy AdminLTE / Bootstrap Class Leftovers:** `Areas/Admin/Views/Courts/{Create,Edit}.cshtml`, `Menus/{Index,Create,Edit,Delete,Details}.cshtml`, `Products/Delete.cshtml`, and `Register/Index.cshtml` still wrap views in `.content-wrapper`, `.content-header`, `.container-fluid`, `.card-primary`, `.btn-xs`, and `.btn-info` with uneven padding and mixed visual hierarchy.
3. **Dense Operations Without Timeline Visualization (`/Management`):** Staff must scan flat card lists (`#todayList`) to infer court utilization across hours instead of seeing a structured **Court × Time** visual timeline paired with a non-destructive slide-over detail drawer.
4. **Booking Matrix Clarity (`/Booking`):** While slot availability logic works cleanly (`court + time + server quote + hold TTL`), desktop layout lacks an instant side-by-side **Court × Time Matrix** paired with semantic non-color status markers and a tight Notion-style sticky fee card.
5. **Pro Shop Cart Ergonomics on Mobile (`/Product`):** Desktop cart and grid work well (`V6` studio packshots), but mobile cart bar + drawer flow, active category filtering, and restrained micro-interactions (`150–210ms`) can be unified under Notion Productivity spacing, crisp hairline borders, and tactile feedback without heavy motion.
6. **AI Cliché Typography & Contrast Friction:** Some headings rely on uppercase tracked micro-eyebrows, multi-bullet divider strings, or muted grays under WCAG AA contrast thresholds (`4.5:1`). Replacing decorative clutter with direct hierarchy and clean `#111827` / `#615D59` ink improves legibility dramatically.

---

## 3. Scope Guardrails & Non-Negotiable Constraints

- **Stack Preservation:** Keep ASP.NET Core 10 MVC Razor Views + Entity Framework Core 10 + SQL Server + Vanilla JS (`shared.js`, `dashboard.js`, `booking.js`, `menu.js`, `lookup.js`, `management.js`, `checkin.js`, `reports.js`). No React/Vue rewrite, no Tailwind runtime bloat, no GSAP/Three.js.
- **Zero Backend / Schema Mutation:** Zero EF migrations, zero SQL schema modifications, zero API endpoint/DTO breaking changes, zero pricing/ledger/inventory/hold-TTL algorithm changes.
- **Role & Auth Integrity:** Admin (`RoleId=1`), Staff (`RoleId=3`), Customer (`RoleId=2`) RBAC boundaries, CSRF headers, `SameSite` cookies, and `sh-nav-group-admin-only` markers remain intact.
- **`BLOCKED_EXTERNAL` Honesty:** SMS/Email OTP forgot-password reset, external Google/Facebook OAuth, and automated live VietQR bank webhooks remain disabled/honest placeholders — zero fake automation.
- **Protected QR Presentation (`SpikeTicket`):** `#spkDigitalTicketCard` and `.spk-ticket-qr-well` MUST keep `#FFFFFF !important` quiet-zone padding (`14px+`), `SPK1.*` token payload, SVG/PNG downloads, ZXing scan accuracy, printable layout, and zero CSS transforms/clipping over QR pixels.
- **Protected Selectors:** Preserve every selector locked in `docs/UI_ROUTE_COMPATIBILITY_MATRIX.md` and `PickleballClub.Tests/ShadcnAdminV7ParityTests.cs`.

---

## 4. Phased 6-Milestone Implementation Roadmap

| Milestone | Scope | Primary Deliverables | Exit Gate |
| :--- | :--- | :--- | :--- |
| **M0 — Freeze & Foundation Docs** | Git snapshot, Before screenshots (`1440px`, `375px`), unseen Admin views audit | `UI_REDESIGN_PLAN.md`, `DESIGN_SYSTEM.md`, `UI_COMPONENT_INVENTORY.md`, `UI_ROUTE_COMPATIBILITY_MATRIX.md`, `screenshots/before/*` | Clean baseline commit + docs reviewed |
| **M1 — Design System & App Shells** | Modular CSS tokens/components, Razor UI partials, Notion-grade public 64px TopNav & footer, crisp Admin AppShell (`#shAdminShell`, `#shAdminSidebar`, `#shAdminTopbar`, breadcrumbs, theme toggle) | Updated `styles.css` (+ clean token/component layers), `_Layout.cshtml` (public & admin), reusable partials | `dotnet build -c Release` + `38/38 dotnet test` + zero layout overflow |
| **M2 — Customer Experience** | Home (`/`), Booking + interactive Court × Time Matrix (`/Booking`), Digital QR Ticket polish, Lookup (`/Lookup`), Profile (`/Account/Profile`), Login (`/Account/Login`) | Updated Views + synchronized `dashboard.js`, `booking.js`, `lookup.js` | M2 Playwright visual + functional booking/QR/profile E2E |
| **M3 — Pro Shop E-Commerce** | Desktop filter/grid + sticky right cart panel, mobile sticky Bottom Sheet drawer, tactile card selection & add-to-cart feedback | Updated `Views/Product/Index.cshtml`, `Product/Default.cshtml`, `menu.js` | `ProShopV6ImageAndApiTests` + cart/order E2E + responsive check |
| **M4 — Admin Operational Suite** | Operational KPI Dashboard (`/Admin`), Management Court × Time Scheduling Timeline + Slide-over Detail Drawer + Cashier/POS modals + Pro Shop Dispatch (`/Management`), Check-in QR workspace (`/Checkin`) | Updated Admin operational views + timeline/drawer controller in `management.js` | M4 operational E2E (approve, check-in, extend, move court, POS, ledger settle/refund, lock) |
| **M5 — Admin CRUD, Reports, CMS & Auth** | Strip all AdminLTE leftovers across `Bookings`, `Courts`, `Products`, `Accounts`, `Menus`; polish `/Reports` chart & ledger; clean `/Cms`; polish `/Admin/Login` & `/Admin/Register` | Clean Razor Views + normalized forms/dropzones/pagination + Staff RBAC preservation | M5 CRUD + Staff RBAC E2E + `ShadcnAdminV7ParityTests` |
| **M6 — Final QA & Release Gate** | Cross-viewport checks (`375/768/1024/1440px`), WCAG 2.2 AA contrast & keyboard focus audit, `105/105` QR benchmark, full 16-flow E2E, After screenshots | `UI_REDESIGN_CHANGELOG.md`, `UI_VISUAL_QA_REPORT.md`, `UI_REGRESSION_REPORT.md`, `UI_REDESIGN_RELEASE_GATE.md`, `screenshots/after/*` | 100% pass across build, tests, QR, Playwright E2E; `PickleballClub` DB untouched |
