# UI ROUTE & CONTRACT COMPATIBILITY MATRIX

This matrix locks every URL, Razor View, Form POST field, JSON API endpoint, Role Guard, and protected DOM ID across the redesign. No contract listed here may be broken during any milestone.

---

## 1. Public & Customer Routes

| Route | Controller / View | JS Module | Auth / Role | Protected DOM IDs / Selectors | Connected APIs |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `/` | `HomeController.Index` -> `Views/Home/Index.cshtml` | `dashboard.js` | Anonymous / Any | `#heroTagText`, `#heroMainTitle`, `#heroSubText`, `#heroLiveCountTag`, `#heroOpenHours`, `#heroMiniCourts`, `#qbDate`, `#qbCourt`, `#qbTime`, `#dashStats`, `#dashMatrixTable`, `#courtsGrid`, `#pricingStdTag`, `#priceStandardCard`, `#pricingPeakTag`, `#pricePeakCard` | `GET /api/db/state`, `POST /api/bookings/quote` |
| `/Booking` | `BookingController.Index` -> `Views/Booking/Index.cshtml` | `booking.js` | Anonymous / Member | `#selectedDateLabel`, `#bookingDatePicker`, `#bookingDateNotice`, `#quickDateStrip`, `#courtOptions`, `#bookingCourtPriceBadge`, `#durationPills`, `#bookingTimeline`, `#bookingTicks`, `#bookingStart`, `#timeSlotGrid`, `#bookingFormTitle`, `#bookingQuoteSourceBadge`, `#bookingName`, `#bookingPhone`, `#bookingPlayers`, `#bookingSkill`, `#summaryDateTime`, `#summaryHourlyRate`, `#summaryCourtFee`, `#summaryTotal`, `#summarySplitPerPerson`, `#conflictBanner`, `#btnSubmitBooking`, `#modalQR`, `#bookingDigitalTicketHost`, `#qrCodeBadge`, `#qrGuestTokenValue`, `#qrDownloadPngBtn` | `GET /api/db/state`, `GET/POST /api/bookings/quote`, `POST /api/bookings`, `GET /api/bookings/{code}/qr.svg`, `GET /api/bookings/{code}/qr.png` |
| `/Product` | `ProductController.Index` -> `Views/Product/Index.cshtml` | `menu.js` | Anonymous / Member | `#menuSearchInput`, `#menuCategoryTabs`, `[data-cat]`, `#menuCount`, `#menuGrid`, `.menu-item[data-product-id]`, `[data-role="media\|img\|cat-pill\|stock-badge\|selected-badge\|selected-qty\|stepper\|stepper-qty\|cta\|cta-icon\|cta-label"]`, `[data-action="dec\|inc\|buy"]`, `#shopCartPanel`, `#shopCartBadge`, `#shopCartItems`, `#shopOrderBooking`, `#guestBookingAttachFields`, `#shopGuestBookingCode`, `#shopGuestAccessToken`, `#shopOrderName`, `#shopOrderPhone`, `#shopOrderNote`, `#shopCartTotal`, `#btnSubmitShopOrder`, `#mobileShopCartBar`, `#mobileCartActionBtn`, `#modalShopOrder` | `GET /api/db/state`, `POST /api/orders` |
| `/Lookup` | `LookupController.Index` -> `Views/Lookup/Index.cshtml` | `lookup.js` | Anonymous / Member | `#lookupCode`, `#lookupAccessToken`, `#lookupShowAllBtn`, `#lookupResultCard`, `#lookupResults`, `#lookupEmpty`, `#modalTicketQR`, `#lookupDigitalTicketHost`, `#ticketQrCode` | `GET /api/bookings/lookup`, `GET /api/bookings/me`, `POST /api/bookings/{code}/cancel` |
| `/Account/Login` | `AccountController.Login` -> `Views/Account/Login.cshtml` | `shared.js` | Anonymous | `#loginEmail`, `#loginPassword`, `#btnSubmitLogin`, `#forgotPasswordModal`, `@Html.AntiForgeryToken()`, `Account/Register` form fields (`Username`, `FullName`, `Phone`, `Email`, `Password`, `ConfirmPassword`) | `POST /Account/Login`, `POST /Account/Register`, `POST /api/auth/forgot-password` (`BLOCKED_EXTERNAL`) |
| `/Account/Profile` | `AccountController.Profile` -> `Views/Account/Profile.cshtml` | `shared.js` | Authenticated (Any Role) | Tab panels, profile update POST inputs (`FullName`, `Phone`, `Email`, `Address`, `Gender`, `BirthDate`, `SkillLevel`, `DominantHand`, `PaddleBrand`, `PlayStyle`, `EmergencyContact`, `Bio`), password change inputs (`currentPassword`, `newPassword`, `confirmPassword`) | `POST /Account/UpdateProfile`, `POST /Account/ChangePassword`, `GET /api/bookings/me` |

---

## 2. Admin Shell & Auth Contract (`ShadcnAdminV7ParityTests.cs`)

| Route / Shell | Role Allowed | Protected DOM IDs / Attributes | Required Semantics |
| :--- | :--- | :--- | :--- |
| `Areas/Admin/Views/Shared/_Layout.cshtml` | Admin (`1`), Staff (`3`) | `#shAdminShell`, `#shAdminSidebar`, `#shAdminTopbar`, `#shSidebarToggleBtn`, `#shCmdTriggerBtn`, `#shThemeToggleBtn`, `#shCommandModal`, `[data-testid="sh-nav-group-admin-only"]`, `meta[name="csrf-token"]` | `[data-testid="sh-nav-group-admin-only"]` hidden for Staff (`RoleId=3`); `"Staff Operator"` footer role text for Staff |
| `/Admin/Login` | Anonymous | `.sh-auth-card`, `@Html.AntiForgeryToken()`, `Username`, `Password` inputs | Rejects Role 2 (Customer) or inactive account |
| `/Admin/Register` | Anonymous | `.sh-auth-card`, `@Html.AntiForgeryToken()`, registration inputs | Clean card layout matching Admin auth parity |

---

## 3. Admin Operations & CRUD Routes

| Route | Area View | JS / Form Method | Role Allowed | Protected DOM IDs / Bindings | APIs / Actions |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `/Admin` (`/Dashboard`) | `Admin/Home/Index.cshtml` | Razor SSR + links | Admin, Staff | Operational KPI counters, pending badge, active court tiles | `Areas/Admin/Controllers/HomeController.Index` |
| `/Management` | `Admin/Home/Management.cshtml` | `management.js` | Admin, Staff | `#pendingCount`, `#approveAllBtn`, `#pendingList`, `#mgmtFilterTabs`, `#todayList`, `#standaloneOrdersCount`, `#shopOrderSearch`, `#shopOrderFilter`, `#standaloneOrdersList`, `#shopOrdersPagination`, `#lockCourtBtns`, `#lockDate`, `#lockStart`, `#lockDuration`, `#lockWindowPreview`, `#lockReason`, `#btnCreateCourtLock`, `#activeLocksCount`, `#activeLocksList`, `#modalPOS`, `#modalLedgerSettlement`, `#ledgerPartialFailureBanner`, `#btnRetryLedgerFinishOnly` (+ new `#mgmtTimelineGrid`, `#mgmtDetailDrawer`) | `/api/admin/bookings/{code}/approve\|reject\|extend\|move-court\|pos-item\|payments\|refunds\|transactions\|finish`, `/api/admin/orders`, `/api/admin/orders/{code}/fulfill\|payment`, `/api/admin/court-locks` |
| `/Checkin` | `Admin/Home/Checkin.cshtml` | `checkin.js` | Admin, Staff | Camera start/stop/switch buttons, QR photo upload input (`#qrImageUpload` / existing selectors), manual booking/QR token input, live scan result card | `POST /api/checkin/verify`, `POST /api/checkin/decode-qr`, `POST /api/admin/bookings/checkin` |
| `/Reports` | `Admin/Home/Reports.cshtml` | `reports.js` | **Admin only** | `#reportFrom`, `#reportTo`, `#btnFilterReport`, `#reportStatusBanner`, `#reportStats`, `#reportChart`, `#vipList`, `#reportTxCountBadge`, `#reportLedgerTableBody` | `GET /api/admin/reports/summary` (assumed by `reports.js`) |
| `/Cms` | `Admin/Home/Cms.cshtml` | Inline CMS JS | **Admin only** | `#cmsClubName`, `#cmsHeroTag`, `#cmsHeroTitle`, `#cmsHeroSub`, `#cmsOpenHours`, `#cmsPhone`, `#cmsAddress`, `#cmsBankId`, `#cmsBankAcc`, `#cmsBankOwner`, `#cmsPriceStd`, `#cmsPricePeak`, etc. | `PUT/POST /api/admin/cms` |
| `/Admin/Bookings` | `Admin/Bookings/Index.cshtml` | Razor Form POST | Admin, Staff | Walk-in creation (`CreateWalkIn`), status actions (`UpdateStatus`), payment/refund (`RecordPayment`, `RefundPayment`), Admin delete (`Delete`) | `BookingsController` + `@Html.AntiForgeryToken()` |
| `/Admin/Courts` | `Admin/Courts/{Index,Create,Edit}.cshtml` | Razor Form POST | **Admin only** | `CourtId`, `Alias`, `CourtName`, `CourtType`, `TypeLabel`, `Surface`, `BasePrice`, `Surcharge`, `Image`, `Description`, `IsActive` | `CourtsController` + `ToggleActive` |
| `/Admin/Products` | `Admin/Products/{Index,Create,Edit,Delete}.cshtml` | Razor Form POST + `admin-product-image.js` | **Admin only** | `[data-testid="admin-products-table"]`, `#productPreviewStage`, `#productImagePreviewImg`, `#productImageDropzone`, `#productImageInput`, `[data-testid="admin-product-submit"]` | `ProductsController` |
| `/Admin/Accounts` | `Admin/Accounts/Index.cshtml` | Razor Form POST | **Admin only** | `CreateMember`, `UpdateMemberTier`, `ToggleActive` POST forms with `@Html.AntiForgeryToken()` | `AccountsController` |
| `/Admin/Menus` | `Admin/Menus/{Index,Create,Edit,Delete,Details}.cshtml` | Razor Form POST | **Admin only** | `MenuId`, `Title`, `Alias`, `Description`, `Levels`, `Position`, `IsActive` | `MenusController` |

---

## 4. QR Presentation Invariants (`SpikeTicket`)

1. QR code token payload MUST remain verbatim (`SPK1.*` HMAC-SHA256 signed string from backend or `qrPayload.svgUrl` / `qrPayload.pngUrl`).
2. `.spk-ticket-qr-well` MUST enforce `#FFFFFF !important` background, minimum `12–16px` quiet zone padding, crisp border contrast, ZERO CSS `transform`/`filter`/`opacity` overlays, and explicit integer width/height for clean PNG/SVG downloads.
3. All ticket summary fields (Code, Customer, Court, Date, Time, Total/Payment status) must print cleanly under `@media print`.
