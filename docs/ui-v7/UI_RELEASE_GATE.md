# Cổng Quyết Định Phát Hành Giao Diện V7 (`UI_RELEASE_GATE.md`)

**Quyết định phát hành:** **`GO`** *(Đạt tiêu chuẩn phát hành lên môi trường vận hành)*
**Ngày chốt:** `2026-11-17`

---

## 1. Bảng Đối Soát Tiêu Chí Nghiệm Thu (`Master Prompt V7`)

| Tiêu Chí Bắt Buộc | Trạng Thái | Bằng Chứng Xác Thực Trực Tiếp |
|---|---|---|
| Tham chiếu thật từ `satnaing/shadcn-admin` (Sidebar thu gọn, Sticky Topbar, Breadcrumb, Command Menu `Ctrl+K`, Theme Switch) | **PASS** | [REFERENCE_REVIEW.md](file:///e:/pickleball/docs/ui-v7/REFERENCE_REVIEW.md), [DESIGN_TOKENS.md](file:///e:/pickleball/docs/ui-v7/DESIGN_TOKENS.md), [06-admin-overview-1440-after.png](file:///e:/pickleball/docs/ui-v7/screenshots/after/06-admin-overview-1440-after.png) |
| Giữ nguyên 100% ASP.NET Core MVC, EF Core, SQL Server schema, CSRF, RBAC, Sổ cái, Nghiệp vụ V4–V6 | **PASS** | `dotnet build -c Release`: **0 Warning, 0 Error**; `dotnet test -c Release`: **35/35 PASSED** |
| Bảo toàn 100% Vé & Mã QR (`SPK1...`, nền `#FFFFFF` cứng ở mọi theme, tải PNG/SVG, Zoom Fullscreen, Giải mã ZXing máy chủ) | **PASS** | [17-booking-qr-ticket-light-after.png](file:///e:/pickleball/docs/ui-v7/screenshots/after/17-booking-qr-ticket-light-after.png), [18-booking-qr-ticket-dark-site-after.png](file:///e:/pickleball/docs/ui-v7/screenshots/after/18-booking-qr-ticket-dark-site-after.png), `V7-05` & `V7-07` **PASS** |
| Đồng bộ Website Khách hàng + Pro Shop V6 (ảnh `1:1`, keyed DOM giữ focus, animation) trong cả Light & Dark Mode | **PASS** | [01-home-1440-after.png](file:///e:/pickleball/docs/ui-v7/screenshots/after/01-home-1440-after.png), [03-proshop-1440-after.png](file:///e:/pickleball/docs/ui-v7/screenshots/after/03-proshop-1440-after.png), [25-proshop-dark-1440-after.png](file:///e:/pickleball/docs/ui-v7/screenshots/after/25-proshop-dark-1440-after.png), `V7-06` **PASS** |
| Responsive quét từ `320px` tới `1920px`, không overflow, không vỡ ảnh, không lỗi Console / JS / HTTP 4xx–5xx | **PASS** | `playwright-v7-results.json`: **`99 / 99` viewport PASS**, `overflowFailures: 0`, `brokenImages: 0`, `consoleErrors: 0`, `pageErrors: 0` |
| Không dữ liệu giả mạo (KPI, Báo cáo Sổ cái, Tồn kho POS lấy từ SQL Server & API thực) | **PASS** | `V7-11` (`Reports`), `V7-08` (`POS`), [10-reports-ledger-1440-after.png](file:///e:/pickleball/docs/ui-v7/screenshots/after/10-reports-ledger-1440-after.png) |
| Kiểm tra mắt thường ảnh chụp Before/After toàn bộ màn hình chính | **PASS** | `docs/ui-v7/screenshots/before/` và `docs/ui-v7/screenshots/after/` (đã rà soát và sửa triệt để 7 lỗi UI thực tế phát hiện qua ảnh) |

---

## 2. Tài Liệu Bàn Giao Đã Hoàn Thiện (`e:\pickleball\docs\ui-v7\`)

- [REFERENCE_REVIEW.md](file:///e:/pickleball/docs/ui-v7/REFERENCE_REVIEW.md)
- [DESIGN_TOKENS.md](file:///e:/pickleball/docs/ui-v7/DESIGN_TOKENS.md)
- [IMPLEMENTATION_PLAN.md](file:///e:/pickleball/docs/ui-v7/IMPLEMENTATION_PLAN.md)
- [ROUTE_PARITY_MATRIX.md](file:///e:/pickleball/docs/ui-v7/ROUTE_PARITY_MATRIX.md)
- [DOM_CONTRACT_MATRIX.md](file:///e:/pickleball/docs/ui-v7/DOM_CONTRACT_MATRIX.md)
- [UI_API_SQL_MATRIX.md](file:///e:/pickleball/docs/ui-v7/UI_API_SQL_MATRIX.md)
- [TEST_MATRIX.md](file:///e:/pickleball/docs/ui-v7/TEST_MATRIX.md)
- [TEST_RESULTS.md](file:///e:/pickleball/docs/ui-v7/TEST_RESULTS.md)
- [CHANGELOG.md](file:///e:/pickleball/docs/ui-v7/CHANGELOG.md)
- [KNOWN_LIMITATIONS.md](file:///e:/pickleball/docs/ui-v7/KNOWN_LIMITATIONS.md)
- [UI_RELEASE_GATE.md](file:///e:/pickleball/docs/ui-v7/UI_RELEASE_GATE.md)
- [playwright-v7-results.json](file:///e:/pickleball/docs/ui-v7/playwright-v7-results.json)

---

## 3. Hướng Dẫn Khôi Phục (Rollback Plan)

- Toàn bộ thay đổi V7 nằm hoàn toàn ở tầng giao diện (Razor Views `.cshtml`, CSS `wwwroot/css/styles.css`, JS `wwwroot/js/*.js`) và 1 file test `PickleballClub.Tests/ShadcnAdminV7ParityTests.cs`; **không có bất kỳ thay đổi migration SQL Server hoặc hợp đồng JSON API nào**.
- Khi cần quay về giao diện trước V7, chỉ cần khôi phục các tệp giao diện qua Git (`git checkout -- Areas/ Views/ wwwroot/css/styles.css wwwroot/js/`) và build lại (`dotnet build -c Release`).
