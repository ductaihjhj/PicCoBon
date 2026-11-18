# MASTER PROMPT V7 — MIGRATE SPIKE PICKLEBALL UI TO SHADCN-ADMIN VISUAL SYSTEM WITHOUT BUSINESS-LOGIC REGRESSION

## 0. VAI TRÒ, NGUỒN THAM CHIẾU, PHẠM VI

Bạn là Principal Product Designer, Senior ASP.NET Core MVC Engineer, Frontend Architect, SQL Server Engineer, Accessibility Specialist và QA Automation Engineer chịu trách nhiệm nâng cấp giao diện **Spike Pickleball Club**. Hãy **TRỰC TIẾP SỬA CODE**, không chỉ tạo mockup hay viết kế hoạch. Bắt đầu trên **workspace mới nhất** do chủ dự án mở trong Antigravity; không giả định ZIP trước đây là bản mới nhất.

**Reference design chính:** https://github.com/satnaing/shadcn-admin

1. Truy cập repository thật trong môi trường Antigravity; kiểm tra README, license, cấu trúc, dependencies, demo/screenshots và các màn hình/components hiện có. Lưu URL, commit SHA hoặc tag thực sự đã xem vào `docs/ui-v7/REFERENCE_REVIEW.md`. Bỏ tham số tracking `utm_source`.
2. Đọc `AGENTS.md`, `.agent/skills/frontend-design/SKILL.md`, các skill thiết kế/kiểm thử có thật trong workspace. Nếu không truy cập được repository hoặc không có skill phù hợp, ghi `BLOCKED_REFERENCE` và chỉ sử dụng những thuộc tính tham chiếu có thể xác minh; **không tự nhận đã xem repo hoặc dùng skill**.
3. Quét dự án hiện hành: controllers/routes, Razor views/partials, JavaScript DOM selectors, CSS, API contracts, EF Core models, migrations, role authorization, authentication, payment/ledger, inventory, booking, QR/check-in, Pro Shop. Tạo snapshot/commit checkpoint trước mọi thay đổi.
4. Dùng **phong cách thiết kế** của shadcn-admin (shell quản trị gọn, sidebar, topbar, data tables, form controls, dialogs, popovers, tabs, typography, surfaces, density, states), không giả định toàn bộ source React/TypeScript của repo có thể chạy trực tiếp trong Razor. Kiểm tra license trước khi tái sử dụng code/assets; ghi attribution khi cần.

## 1. MỤC TIÊU CUỐI CÙNG

**Thiết kế lại 100% các màn hình thực đang có** theo một design system thống nhất lấy shadcn-admin làm cảm hứng/chuẩn thị giác, nhưng giữ nhận diện Spike Pickleball. Không biến site khách hàng thành bản sao dashboard quản trị: **Admin** dùng sidebar + topbar hiện đại; **website khách** dùng navigation web thương mại, nhưng chia sẻ cùng hệ tokens, component, typography, radius, border, shadow, input, button, toast, modal, dropdown, badges và motion.

**BẤT BIẾN NGHIỆP VỤ:** Không thay đổi business logic, luật tính giá, thanh toán/hoàn tiền, booking lifecycle, pending hold, trạng thái vé, check-in, anti-CSRF, idempotency, phân quyền, inventory, SQL data, API endpoint, request/response semantics, QR token hoặc khóa bảo mật chỉ vì làm mới giao diện. Không xóa dữ liệu, đổi cấu trúc DB hoặc migrate sang SPA/React ngoài phạm vi mà chưa được phê duyệt. Nếu thật sự cần API/backend change nhỏ để UI dùng đúng dữ liệu: giải thích tác động, chỉ sửa tương thích ngược, thêm test và migration an toàn khi bắt buộc.

**Lệnh đặc biệt:** Không được nói `DONE` dựa trên screenshot đẹp hoặc trang render được. Phải có bằng chứng UI → API → SQL Server và hành vi tương tác đúng.

## 2. KIẾN TRÚC HIỆN CÓ CẦN TÔN TRỌNG

Vị trí đã thấy ở bản dự án gần nhất (kiểm tra lại workspace thực tế):

- Layout Admin: `Areas/Admin/Views/Shared/_Layout.cshtml`.
- Layout trang khách: `Views/Shared/_Layout.cshtml`.
- Trang Admin trong `Areas/Admin/Views/Home/` gồm `Index`, `Management`, `Checkin`, `Reports`, `Cms`; các trang `Bookings`, `Courts`, `Products`, `Menus`, `Accounts`, `Login` và trang khác nếu tồn tại.
- Trang khách: `Views/Home/Index.cshtml`, `Views/Booking/Index.cshtml`, `Views/Product/Index.cshtml`, `Views/Lookup/Index.cshtml`, `Views/Account/Login.cshtml`, `Views/Account/Profile.cshtml` và view phát sinh.
- CSS gốc: `wwwroot/css/styles.css`; JavaScript: `wwwroot/js/shared.js`, `booking.js`, `lookup.js`, `checkin.js`, `dashboard.js`, `management.js`, `reports.js`, `menu.js`. Có thể bản mới nhất đã khác; khảo sát trước khi thao tác.
- QR thực: hàm `SpikeTicket.mountTicket(...)`; modal `#modalTicketQR`, host `#lookupDigitalTicketHost`; API QR `/api/bookings/{id}/qr.png` và `.svg` (xác minh route trên bản hiện tại); quét camera/ảnh trong `Areas/Admin/Views/Home/Checkin.cshtml`.
- Sản phẩm: model `TbProduct.Image` tồn tại; nếu V6 đã triển khai, bảo toàn upload ảnh, gallery, preview, sản phẩm PNG/WebP và animation; nếu V6 chưa làm xong, **đừng coi là đã có**.
- Các đoạn JS có thể phụ thuộc vào ID/class, `data-*`, các modal và delegated events. Trước khi đổi markup, lập inventory selectors + form fields + hidden antiforgery inputs + scripts.

## 3. DESIGN SYSTEM THỐNG NHẤT

### 3.1. Token-first, không CSS vá chồng

Tạo một lớp token dùng chung hoặc tổ chức CSS rõ ràng: colors (background, foreground, card, popover, primary, secondary, muted, accent, destructive, border, input, ring), typography, spacing, radius, shadow, focus outline, breakpoints, z-index, motion durations; bảo đảm tương thích với hệ CSS/Razor hiện hữu. Học cách đặt token và độ tương phản của shadcn-admin **sau khi xem nguồn thật**, nhưng cá nhân hóa màu thương hiệu Spike (navy/charcoal, off-white, điểm nhấn lime/volt, orange khi hợp lý), không bê nguyên palette tùy tiện.

- Bật light/dark mode nếu dự án có hoặc phù hợp, lưu preference an toàn; đồng bộ mọi modal, popover, dropdown, table, ticket; không tạo chữ tối trên nền tối.
- Dùng một bộ component tái sử dụng (Razor partial/tag helper hoặc HTML/CSS component phù hợp) cho Button, Card, Input, Select, Checkbox, Radio, Switch, Table, Pagination, StatusBadge, Toast, Dialog, Drawer/Sheet, Tabs, Tooltip, EmptyState, Skeleton, ErrorState, ConfirmDialog, ChartFrame, PageHeader, Breadcrumb, ProductCard.
- Không bắt buộc cài React, Vite, Next.js, TanStack hoặc Tailwind runtime nếu app hiện là ASP.NET Core MVC. Nếu chọn dùng Tailwind build ở mức CSS, phải tích hợp build pipeline ổn định với Razor và không phá CSS. **Ưu tiên chuyển dịch style bằng Razor/HTML/CSS/JS hiện có**.
- Tránh inline styles kéo dài, `!important` diện rộng, CSS selector global có khả năng phá QR/print. Đặt scope như `.admin-shell`, `.site-shell`, `.ticket-scope` khi cần.
- Icon chức năng theo một nguồn nhất quán (thư viện đã có hoặc Lucide/Iconify đúng gói), không tự chế icon; **ảnh sản phẩm là `<img>` từ database, không thay lại thành icon**.

### 3.2. Admin shell theo tham chiếu shadcn-admin

- Sidebar co/giãn desktop, active item rõ, phân nhóm menu hợp lý; item hiển thị theo role thực, link không hoạt động phải được xử lý hoặc ẩn với ghi nhận, không giả route.
- Topbar: menu điều hướng, breadcrumb, search nếu thực sự có dữ liệu tìm, dropdown tài khoản/logout, điều khiển theme; không tạo notification count giả.
- Dashboard KPI cards, charts, data table, filters, pagination, dialogs, drawers thống nhất; loading/empty/error states thật; số liệu do API/SQL xác thực.
- Mobile: sidebar mở dạng sheet có overlay, trap focus, close on navigation/Escape; các nút quan trọng luôn thao tác được. Tables dùng responsive strategy (scroll cue, stacked/card view hoặc sticky important columns); không che action cuối.
- Giữ nguyên permission Admin/Staff/Customer. Sidebar chỉ là UI, bảo mật vẫn phải được enforce tại server.

### 3.3. Website khách cùng ngôn ngữ thiết kế, không copy sidebar Admin

- Header/menu/footer nhất quán với Admin về màu chữ, font, border, radius, button, form, toast; vẫn phục vụ đặt sân và thương mại: home, booking, lookup, Pro Shop, account.
- Đặt sân: chọn ngày/sân/giờ, panel báo giá, trạng thái khả dụng, CTA, pending expiry, API errors; luôn server-authoritative. Không cho dữ liệu fallback giả khiến khách hiểu là sân đang trống/giá đúng.
- Pro Shop: grid ảnh sản phẩm từ `TbProduct.Image`, product details, trạng thái stock, CTA & hover/tap motion nhẹ, responsive cart sheet; giữ nguyên request/response, pricing, cart math, order creation, payment state.
- Account/lookup: form validation & focus, trạng thái trống, lỗi, vé và badge nhất quán; không rò rỉ token bảo mật vào UI/log.

## 4. QUY TẮC BẢO VỆ QR — ƯU TIÊN CAO NHẤT

**Đừng chỉnh QR theo cách làm hỏng khả năng quét, vị trí vé hoặc in ấn.** QR là **mã thật từ server**, không được thay bằng icon, hình minh họa, CSS pseudo-elements hoặc ma trận tự vẽ.

- Giữ nguyên QRCoder/ZXing hoặc các thư viện đang vận hành; giữ token signed, auth/owner access, API QR PNG/SVG, scope thời gian và trạng thái check-in.
- Giữ nguyên/vận chuyển an toàn các selector `#modalTicketQR`, `#lookupDigitalTicketHost`, thành phần do `SpikeTicket` mount, các nút tải PNG/SVG, print, fullscreen, check-in camera/file input/manual code. Nếu đổi ID là bắt buộc, cập nhật toàn bộ selector, test, event, form binding và ghi migration table.
- **Layout vé:** nội dung QR có vị trí rõ ở giữa khối vé, tỷ lệ 1:1; màu module đậm trên nền trắng, quiet zone tối thiểu theo thư viện; không clipping, không transform/rotate hoặc animation trên chính mã QR; kích thước đủ quét trên mobile; không overlay badge/logo/decoration lên ô dữ liệu QR trừ khi có giải mã/scan test nghiêm ngặt.
- Modal vé: responsive (320–1920px), không cắt QR/mã vé, modal không phủ nút tải hoặc info sân/ngày/giờ; giữ header/footer và vị trí các nút hành động hợp lý; mobile có thể scroll nội dung trong modal, không làm mất focus.
- `@media print`: in vé đúng kích thước, không bị dark background làm QR mất tương phản; các control/no-print ẩn đúng; thông tin mã đặt sân không xuống dòng giữa mã.
- Camera scanner: video viewport/overlay không bị che bởi topbar/sidebar/dialog; permission denied and no-camera states; file upload và manual check-in cùng dùng dịch vụ xác minh hiện có.
- Bắt buộc test kỹ thuật: tạo vé từ SQL test -> render SVG/PNG -> decode bằng thư viện đọc QR -> so khớp payload -> check-in hợp lệ; test vé hết hạn/đã check-in/chưa duyệt/quyền sai, camera trên thiết bị có thể truy cập. Nếu thiếu thiết bị, ghi `BLOCKED_DEVICE`.

## 5. HỢP ĐỒNG UI ↔ API ↔ SQL KHÔNG ĐƯỢC ĐỔI

Với mỗi trang, lập bảng `route -> view -> JS -> API -> service -> model/table -> auth -> states -> tests` trước và sau redesign. Không thay payload, các flag business, CSRF tokens, session/claims, role, idempotency keys, guest access token, pricing version, stock locking, transaction boundaries, policy date/time, payment ledger hoặc status transitions.

- Tuyệt đối không có fake success toast khi API lỗi; render `loading`, `empty`, `error`, `stale`, `unauthorized`, `success` theo API response thật.
- Chống stale/race trên UI: không dùng báo giá cũ sau khi đổi lựa chọn, không tự gán trạng thái booking `completed`, không biến `pending` thành `approved` chỉ vì thời gian.
- Nếu chỉ sửa giao diện, backend không đổi. Nếu có lỗi dữ liệu/phân quyền phát hiện khi test: ghi issue, chỉ sửa khi thực sự trong phạm vi và có test.
- Không cập nhật data SQL production để demo UI; seed test database độc lập bằng dữ liệu giả **chỉ trong môi trường test** với cleanup rõ, không hiển thị mock data cho khách thực.
- Không xóa id, name, form method, antiforgery tag, validation span, ARIA link, data attributes, Ajax callback hay event handlers mà chưa map replacement. Với các hành vi `onclick` inline cũ có thể refactor sang listeners sau khi tests xác nhận không đổi hành vi.

## 6. DANH SÁCH MÀN HÌNH BẮT BUỘC ĐỐI CHIẾU

Lập `docs/ui-v7/ROUTE_PARITY_MATRIX.md` liệt kê toàn bộ route thực, không chỉ các route sau. Phân nhóm:

**ADMIN:** login, dashboard/index, management/POS, bookings, courts CRUD, products CRUD + upload ảnh, menus/categories, accounts/roles, check-in QR, reports/ledger, CMS/config, all modals/drawers/confirmations. 
**CUSTOMER:** home, booking/availability, product/Pro Shop, lookup/ticket QR, account login/register/profile, cart/checkout, order confirmation, error/403/404 và bất kỳ view hiện có.

Với từng route phải giữ: vị trí nút quan trọng, breadcrumb/navigation, form labels, validation messages, sorting/filtering, table columns, pagination, CTA visibility, permission-gating, data bindings, modal open/close, keyboard actions. Không tự thêm chức năng giả chỉ để giống design tham chiếu.

## 7. QUY TRÌNH TRIỂN KHAI BẮT BUỘC

### Phase 0 — Baseline & plan

- Snapshot/backup; đọc reference và skills; chạy build + tests hiện có, ghi rõ kết quả baseline và lỗi có sẵn; screenshot baseline desktop/mobile các trang chính (bằng đăng nhập theo role nếu môi trường có dữ liệu test).
- Tạo `docs/ui-v7/IMPLEMENTATION_PLAN.md`, `ROUTE_PARITY_MATRIX.md`, `DOM_CONTRACT_MATRIX.md`, `DESIGN_TOKENS.md`, `TEST_MATRIX.md`; ưu tiên không phá tác vụ nghiệp vụ.

### Phase 1 — Foundation / isolated shell

- Tokens + components; xây shell Admin (sidebar/topbar) mới có responsive, light/dark, accessibility. Không chỉnh tất cả trang cùng lúc; giữ khả năng rollback. Commit/checkpoint.
- Đồng bộ public shell theo cùng system (header/footer/controls), không ép admin navigation vào public.

### Phase 2 — Chuyển từng màn hình theo vertical slice

- Admin Dashboard -> Bookings -> Check-in QR -> Management/POS -> Products/Inventory -> Courts -> Reports -> CMS -> Accounts -> các trang còn lại. Sau mỗi màn: test screenshot, form, API, role, database fixture.
- Public Home -> Booking -> Lookup & Digital Ticket QR -> Pro Shop -> Account -> các trang còn lại; với booking/QR/payments/stock luôn kiểm thử regression sau thay đổi.
- Không xóa CSS cũ cho tới khi route mới vượt qua parity test; khi xóa phải chắc chắn không còn phụ thuộc.

### Phase 3 — Automated visual & functional regression

- Dùng Playwright/Chromium hoặc bộ browser tool có thật. Chụp các kích thước 320, 375/390, 768, 1024, 1280, 1440, 1920; trang có sidebar mở/đóng, modal QR, form validation, cart, scroll tables, dropdowns, theme light/dark.
- Dùng screenshot compare/baseline + review trực quan để phát hiện clipping, overflow, overlap, text wrap sai, line-height, contrast, focus lost, z-index, broken images, touch target quá nhỏ, sidebar che modal, chart/table lỗi.
- Chạy UI interaction E2E: nav, filters, search, CRUD, upload product image, add/remove cart, booking quote and submit, lookup ticket, print/download QR, scan/upload QR, check-in, role denial, API fail/retry, payment/stock regression. Kiểm thử cả bằng bàn phím.
- Chạy `dotnet build`, `dotnet test`, kiểm thử SQL Server thực nếu có; Node syntax checks, console, server logs, failed HTTP/network, accessibility axe (nếu sẵn); kiểm tra cả test cũ và test mới. Không tự fake 100% PASS.

### Phase 4 — Fix loops and release gate

- Khi test fail: tạo issue có screen/viewport/steps/selector/root cause/severity; sửa source, re-run focused tests + full regression; lặp đến khi không còn lỗi **tái hiện được trong phạm vi test**.
- Nếu thiếu .NET/SQL/browser/credentials/phone camera: status `BLOCKED_ENV`/`BLOCKED_EXTERNAL`/`BLOCKED_DEVICE`, kèm steps để người dùng chạy; không nhận DONE.
- Chỉ kết luận `GO` khi không còn P0/P1 open và mọi đường đi chính có bằng chứng API+DB+UI; nếu không, kết luận `NO_GO` đúng thực tế.

## 8. YÊU CẦU VISUAL + INTERACTION CỤ THỂ

- Typography rõ cấp bậc; KPI/metrics thống nhất; label/tên dài không đè badge/nút; dữ liệu VND định dạng nhất quán, date/time theo logic app.
- StatusBadge mang ngữ nghĩa đúng: pending, approved, playing, completed, cancelled, expired, paid/unpaid/refunded; không suy diễn trạng thái từ local clock để thay đổi dữ liệu nguồn.
- Data table sort/filter/paginate cần có dữ liệu thật, không có control giả. Skeleton không chớp loạn và error state không nhầm empty state.
- Dialog/focus: đóng bằng ESC khi hợp lệ, restore focus, aria-labelledby, confirmation trước thao tác phá hủy, toast không che nội dung thiết yếu.
- Motion: 120–250ms, transform/opacity, tôn trọng `prefers-reduced-motion`; tránh hoạt ảnh gây chậm hoặc che QR, check-in/camera. Không animate mã QR.
- Responsive: không horizontal overflow toàn trang; với bảng quá rộng có indicator cuộn rõ ràng và không cắt action; mobile keyboard không che submit.
- Ảnh sản phẩm: dùng `TbProduct.Image`/URL đã được xác minh; lazy loading, `object-fit:contain`, fallback an toàn chỉ nếu thiếu ảnh, không hiển thị icon như ảnh hàng hóa; tránh URL không an toàn và broken images.
- Thống nhất bố cục bảng, biểu đồ, form, modal và trạng thái tương tác cho cả site và Admin, nhưng cho phép mật độ thông tin khác nhau phù hợp người dùng.

## 9. DANH MỤC BÀI TEST CHẶN RELEASE

1. Bật/tắt sidebar desktop/mobile không làm mất header, nội dung, toolbar hoặc action.
2. Role Staff chỉ thấy action phù hợp; gọi trực tiếp endpoint trái quyền vẫn bị từ chối bởi backend.
3. Quản lý sân/sản phẩm/đơn hàng: Create/Edit/Delete/Upload/Filter/Pagination không thay giá/tồn kho sai; dữ liệu SQL trước/sau đúng.
4. Hai khách đặt cùng sân cùng khung giờ không thể cùng được duyệt/giữ chỗ trái policy; UI không hiển thị success giả khi server từ chối.
5. Đổi sân/giờ/ngày làm báo giá cũ mất hiệu lực ngay; chỉ chốt quote mới từ server.
6. QR modal render đúng vị trí, đủ vùng trắng, code không ngắt dòng; SVG/PNG decode đúng, print rõ; QR scan/upload/manual vẫn check-in hợp lệ trên server.
7. Vé sai/quá hạn/chưa duyệt/check-in lặp phải báo lỗi chính xác; không tự đổi trạng thái trên UI.
8. POS: kết thúc ca ≠ tự thu tiền; thu/hoàn tiền theo ledger, UI chỉ hiển thị thành công khi server commit.
9. Shop: ảnh upload/preview/delete an toàn nếu tính năng đã có, cart animation không làm mất state, lỗi stock=0/409 phản ánh đúng, đặt hàng thật ghi DB.
10. Reports: doanh thu chỉ hiển thị từ ledger đúng; nếu API 500/offline thì không toast “đã cập nhật”.
11. CSRF/auth/idempotency không mất khi thay markup/form; login/logout/must-change-password theo rule hiện tại.
12. Test thay đổi dữ liệu ở 2 tab/2 role, ETag/state refresh, empty/error/unauthorized, keyboard and screen-reader semantics.
13. All main pages: screenshots 320..1920, light/dark if enabled, no console errors from own code, no broken internal URLs, no overlapping or inaccessible CTA.

## 10. BÀN GIAO

Bàn giao code đã thay đổi **ngay trong workspace**, giữ cấu trúc ASP.NET MVC hiện có; tạo:

- `docs/ui-v7/REFERENCE_REVIEW.md` (repo URL, SHA/tag, license, component/style đã tham chiếu và phần không thể truy cập).
- `docs/ui-v7/IMPLEMENTATION_PLAN.md` (phases, checkpoints, rollback, file changes).
- `docs/ui-v7/DESIGN_TOKENS.md` + danh sách component dùng chung.
- `docs/ui-v7/ROUTE_PARITY_MATRIX.md` (mỗi route trước/sau, auth, actions, selectors).
- `docs/ui-v7/DOM_CONTRACT_MATRIX.md` (ID, class, data-* được giữ/đổi, cập nhật tests).
- `docs/ui-v7/UI_API_SQL_MATRIX.md` (giao diện -> API/service/DB, nguồn sự thật dữ liệu).
- `docs/ui-v7/TEST_MATRIX.md` + `TEST_RESULTS.md` (commands/results, SQL and visual evidence, no fake PASS).
- `docs/ui-v7/screenshots/` (before/after theo route + viewport + theme + trạng thái quan trọng).
- `docs/ui-v7/CHANGELOG.md`, `KNOWN_LIMITATIONS.md`, `UI_RELEASE_GATE.md` (`GO`/`NO_GO`, P0/P1 unresolved).

Khi hoàn thành, trả về: tổng số route, route đã redesign, số test chạy/thành công/thất bại/bị chặn, screenshot paths, thay đổi backend nếu có, kết quả so với baseline, những điều chưa thể xác minh và chỉ dẫn deploy/rollback. Không viết “không còn bất cứ lỗi nào” nếu chỉ mới test một số viewport/data states.

## 11. CẤM CÁC LỐI TẮT PHỔ BIẾN

- Cấm clone hoặc thay thế toàn bộ ASP.NET Core MVC thành React app chỉ để có giao diện shadcn.
- Cấm thay admin route/url hoặc phát minh menu không có API thực.
- Cấm hardcode demo KPI, booking, giá, stock, account, payment hay fake notification.
- Cấm chuyển QR vào nơi bị cắt, overlay animation/icon/logo không kiểm chứng hoặc tự vẽ QR giả.
- Cấm xóa hidden form fields, CSRF token, các selector JS cần thiết mà không sửa cả script/test.
- Cấm sửa/seed DB production, thay đổi payment/booking/authorization policy không được yêu cầu.
- Cấm chỉ chạy `npm build` hoặc `dotnet build` rồi coi UI logic đúng.
- Cấm tuyên bố đã clone/kiểm tra reference repo, chạy browser hoặc kiểm thử SQL nếu thực tế chưa làm.

## 12. LỆNH THỰC THI

**BẮT ĐẦU NGAY:** Quét source + reference thật + `AGENTS.md` + design skill; lập baseline, sau đó sửa code theo phase và chạy test sau từng nhóm. Không dừng ở lập plan. Mục tiêu: một **Spike Pickleball** có giao diện Admin theo shadcn-admin, giao diện khách cùng design language, vẫn hoạt động đúng trên database/API hiện tại, đặc biệt **QR/check-in, booking, POS, inventory và payment**. Hoàn thành tối đa trong phiên làm việc hiện tại, checkpoint minh bạch nếu vướng môi trường hoặc các dịch vụ ngoài.
