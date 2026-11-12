# REMEDIATION BASELINE — Spike Pickleball Club (Master Prompt V2)

- **Timestamp**: `2026-11-09T00:26:25+07:00`
- **Workspace**: `e:\pickleball-ui` (Post-v1 remediation baseline on `fix/audit-40-items`)
- **Environment Verified**:
  - **OS**: Windows (PowerShell)
  - **.NET SDK**: `10.0.402` (`net10.0`)
  - **SQL Server**: Microsoft SQL Server 2022 Express (`.\SQLEXPRESS`, Windows Authentication verified online)
  - **Node.js**: Installed (`node --check` available)
  - **Browsers**: Google Chrome & Microsoft Edge installed locally (headless screenshot capable)

## 1. Baseline Build & Test Snapshot (Before V2 Code Changes)

- `dotnet build PickleballClub.Tests\PickleballClub.Tests.csproj`: **0 Warning(s), 0 Error(s)**
- `dotnet test PickleballClub.Tests\PickleballClub.Tests.csproj`: **9/9 Passed** (EF InMemory unit tests only; no real SQL Server concurrency tests, no HTTP CSRF/IDOR tests, no independent QR decoder tests yet)
- `node --check wwwroot/js/*.js`: **8/8 JS files valid syntax**

## 2. Confirmed Baseline Gaps Verified Directly in Source Code

| Area | File & Line Reference | Confirmed Issue |
|---|---|---|
| **Payment vs Finish Conflation (`P1-04`, `P1-05`)** | `Services/BookingService.cs:774-796`, `Areas/Admin/Controllers/BookingsController.cs:109`, `wwwroot/js/management.js:338,448` | `RecordBookingPaymentAsync` sets `collectAmount = amount > 0 ? amount : remaining`, so finishing an unpaid session (`amount=0, markCompleted=true`) automatically records a full cash payment and marks `paid`. |
| **Report Grouping (`P1-06`)** | `Controllers/ApiDbController.cs:1126-1197`, `wwwroot/js/reports.js:22-46` | Revenue is computed from `BOOKINGS` grouped by `BookingDate` instead of querying `TbPaymentTransaction` by actual payment timestamp (`CreatedAt` in `Asia/Ho_Chi_Minh`), omitting standalone Pro Shop orders and refunds. |
| **Check-in Pending & GET Side-Effects (`P1-07`)** | `Services/BookingService.cs:500`, `Controllers/ApiDbController.cs:157` | `CheckInBookingAsync` allows `pending` bookings to check in directly instead of requiring `approved` + valid check-in window; `GET /api/db/state` mutates expired bookings on read instead of a background worker. |
| **Standalone Shop Settlement (`P1-04`, `P1-14`, `P1-15`)** | `Controllers/ApiDbController.cs:894-1118` | Standalone `TbOrder` (`BookingId == null`) has no staff payment confirmation, refund, or cancel-with-restock workflow; `CreateOrderApi` and `CreateCourtLockApi` (`684-732`) perform check-then-insert outside SQL transactions. |
| **CSRF Missing on JSON APIs (`P1-17`)** | `Controllers/ApiDbController.cs`, `wwwroot/js/shared.js` | Cookie-authenticated `[ApiController]` mutating POST/PUT/DELETE endpoints lack anti-forgery validation (`RequestVerificationToken`). |
| **Phone Self-Declaration IDOR & VIP (`P0-07`, `P1-12`, `P1-14`, `P1-16`, `P1-18`)** | `Controllers/ApiDbController.cs:362,514,564,1021`, `Services/BookingService.cs:338` | Typing a phone number grants VIP 15% quote/booking discount, booking lookup/cancel, and order-to-booking attachment without cryptographic ownership proof (`AccountId` or opaque `BookingAccessToken`). |
| **Public State Exposure & `/api/db/sync` (`P0-05`, `P0-06`, `P2-06`)** | `Controllers/ApiDbController.cs:148-347`, `Views/Shared/_Layout.cshtml:140`, `Areas/Admin/Views/Shared/_Layout.cshtml:304` | `GET /api/db/state` returns unbounded slot records with `players`/`skillLevel`/`paymentStatus`; `POST /api/db/sync` still accepts arbitrary CMS key/value dictionary; layouts write `pb_auth_user` PII into `localStorage`. |
| **Hardcoded Fallback Secret & Default Password (`P0-02`, `P0-09`)** | `Services/QrTokenService.cs:21`, `Program.cs:35`, `Areas/Admin/Controllers/AccountsController.cs:78`, `Areas/Admin/Views/Accounts/Index.cshtml:87` | `QrTokenService` falls back to hardcoded dev HMAC key even in Production; cookie `SameAsRequest` instead of `Always` in Production; walk-in member creation defaults password to `"123456"`. |
| **Raw HTML XSS (`P0-12`)** | `Views/Shared/Components/Product/Default.cshtml:9` | `@Html.Raw(p.Icon ?? ...)` renders unvalidated product icon HTML directly. |
| **Fake Visual QR (`P1-01`)** | `wwwroot/js/shared.js:549-615`, `Areas/Admin/Views/Home/Checkin.cshtml:48` | `renderQrCodeSvg` draws a non-standard FNV-1a hash grid that real QR decoders cannot scan; `Checkin.cshtml` upload calls `mockCheckIn()` without real QR image/camera decoding. |
| **Fake OTP Toast & Unverified Claims (`P1-02`, `P1-03`, `P2-07`)** | `Views/Account/Login.cshtml:1664,1731` | `handleResetPassword()` shows fake `"Đã gửi mã OTP"` toast without sending anything; `handleSocialLogin()` still exists in script; unverified `"1,250+ tay vợt"` / USAPA certification claims appear as factual statements. |
| **Split Config / Past Hours / Fake Extras (`P1-08`, `P1-09`, `P2-05`)** | `Services/BookingService.cs`, `Services/PricingService.cs`, `wwwroot/js/booking.js:234`, `wwwroot/js/shared.js:399` | Operating hours/peak surcharge split across files; `booking.js` adds client-only `extraPaddles`/`extraBalls` fees not persisted on server; `data-cms="openHours"` vs `[data-cms="hours"]` selector mismatch. |
| **Ad-hoc Startup SQL & Demo Script (`P2-01`, `P2-02`, `P2-10`)** | `Services/DatabaseSchemaUpdater.cs`, `Program.cs:106`, `Database_PickleballClub.sql` | Startup runs raw `ALTER TABLE` swallowing errors instead of versioned EF Core Migrations + `rowversion`; `Database_PickleballClub.sql` with `DROP DATABASE` sits in project root. |
