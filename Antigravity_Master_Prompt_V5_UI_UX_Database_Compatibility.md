# ANTIGRAVITY MASTER PROMPT V5 — SPIKE PICKLEBALL
## UI/UX QUALITY, VISUAL REGRESSION, END-TO-END DATA INTEGRITY & AUTOMATED REPAIR

> **ĐÂY LÀ LỆNH THỰC THI, KHÔNG PHẢI YÊU CẦU CHỈ VIẾT KẾ HOẠCH.** Chạy trên workspace chứa mã nguồn mới nhất của `pickleball.zip` (hoặc phiên bản mới hơn nếu đang được mở). Mục tiêu: sửa toàn bộ 19 finding UI/UX đã phát hiện, kiểm tra và sửa thêm các lỗi UI mới có bằng chứng, bảo đảm giao diện hiển thị nhất quán với API và SQL Server. Không được tuyên bố dự án "không còn bất kỳ lỗi nào" nếu thiếu khả năng kiểm chứng.

## 0. NGỮ CẢNH, TÀI LIỆU VÀ THỨ TỰ ƯU TIÊN

Bạn đóng vai **Principal Product Designer, Frontend Architect, Senior ASP.NET Core MVC Engineer, SQL Server/EF Core Engineer, Accessibility Specialist và QA Automation Lead**. Hãy sửa trực tiếp trong repository, không chỉ viết mockup hay tạo báo cáo.

Trước khi bắt đầu:

1. Đọc **toàn bộ** `AGENTS.md` và `.agent/skills/frontend-design/SKILL.md` thực có trong dự án. Bắt buộc áp dụng nguyên tắc thiết kế có chủ đích, tránh giao diện SaaS rập khuôn, bố cục rõ thứ bậc, typography có hệ thống, hiệu ứng tiết chế, phù hợp câu lạc bộ thể thao Pickleball.
2. Kiểm tra danh mục skill khác đang **thực sự cài đặt**, ví dụ browser testing, Playwright, accessibility, design audit, debugging, responsive design, screenshots. Chỉ gọi/áp dụng skill có thật, ghi rõ đã đọc skill nào và dùng vào việc gì. **Không bịa tên skill, không tuyên bố sử dụng nếu không tồn tại.** Nếu chưa có browser skill, dùng Playwright/Chromium hay công cụ browser sẵn có, không cần thêm dependency vô lý.
3. Đọc `Spike_Pickleball_UI_UX_Audit_2026-11-10.md` nếu có. Tự đối chiếu lại mã nguồn, không mặc định mọi file/line number trong audit còn nguyên sau khi dự án đã được cập nhật.
4. Đọc các báo cáo V4, `docs/` hiện có và mã nguồn `Controllers`, `Areas`, `Services`, `Views`, `wwwroot/css`, `wwwroot/js`, EF entities/migrations, các test và cấu hình để hiểu hợp đồng dữ liệu thật.
5. Chạy baseline `git status`, `dotnet --info`, `dotnet restore`, `dotnet build`, `dotnet test`, `node --check` cho JS, xem các test/browser scripts hiện có. Ghi kết quả thật, phân biệt `PASS`, `FAIL`, `BLOCKED_ENV`.
6. Bắt đầu bằng **ma trận trang/chức năng và sơ đồ luồng UI → API → Service → SQL**, sau đó sửa theo thứ tự ở phần VII. Không dừng sau khi lập ma trận.

**Xung đột với AGENTS.md:** Giữ nguyên stack **ASP.NET Core MVC / .NET 10 / Entity Framework Core / SQL Server / JavaScript hiện tại** và các quy tắc an toàn. Chủ dự án cho phép sửa Views, CSS, JS, mapping DTO, validation và endpoint khi thực sự cần để đồng bộ UI–backend. **Không được tự thay đổi schema database, xóa/reset dữ liệu hoặc thay đổi chính sách giá, thu tiền, phân quyền nếu chưa có phê duyệt cụ thể.** Nếu phát hiện cần migration/schema mới, soạn đề xuất + ảnh hưởng + migration dự kiến + kế hoạch rollback và đánh dấu `NEEDS_APPROVAL`; vẫn xử lý được những phần không phụ thuộc. Không âm thầm xóa dữ liệu thật, không tự deploy production.

**Nguyên tắc không được vi phạm:**

- Database/backend là **source of truth** cho availability, booking status, quote, giá thực, ưu đãi, sân, sản phẩm, stock, payment ledger, quyền truy cập, ngày giờ và nội dung CMS. Frontend chỉ trình bày và nhận hành động, không tự đặt trạng thái nghiệp vụ hoặc suy đoán từ fallback.
- Mọi nút phải thực hiện đúng hành động hoặc có trạng thái disabled với lý do; không toast thành công giả, không hard-code dữ liệu thật, không tự chuyển `playing → completed`, không đổi court ID không tồn tại thành sân khác.
- Giữ nguyên và phát triển **design identity Spike Pickleball** đang có: thể thao, năng lượng, sắc cam/than/xanh đã được dùng nhất quán. Không thay toàn site thành template Tailwind/SaaS phổ thông. Không copy giao diện từ nguồn bên ngoài; tận dụng asset và component hiện có.
- Không thay thư viện QR chuẩn bằng ảnh QR dựng bằng AI/CSS. QR phải được giải mã đúng; không làm rò raw token vào analytics, logs, ảnh công khai.
- Dùng **dữ liệu seed chỉ cho môi trường test**; nghiệm thu end-to-end phải nối app .NET thực với SQL Server test thật. Mock API có thể dùng cho unit/edge-case nhưng không được thay bằng chứng tích hợp thực.
- Không coi 1 ảnh screenshot hoặc `node --check` là bằng chứng UI, logic và database đã đồng bộ. Không tự ghi PASS khi chưa thực hiện.

## I. KẾT QUẢ CẦN BÀN GIAO

1. **Mã nguồn đã được chỉnh sửa trực tiếp**, không chỉ mô tả cách sửa; giữ nguyên nghiệp vụ đúng.
2. **19/19 finding gốc có trạng thái rõ ràng** theo nguyên ID `UI-A01…UI-A08`, `UI-B01…UI-B08`, `UI-C01…UI-C03`; không đổi tên/đánh tráo yêu cầu. Nếu phát hiện lỗi mới, ghi ID `UI-NEW-XX` riêng.
3. **Thiết kế UI đồng bộ**, thông tin thật, responsive, trình tự thao tác rõ, loading/empty/stale/error/success có ngữ nghĩa, dark/light QR dễ đọc.
4. Bộ kiểm thử: frontend unit/contract, .NET integration, SQL Server integration (nếu môi trường có), browser E2E, visual screenshot regression, accessibility, responsive, network errors/timeout/slow/offline, multiple users/tabs.
5. Báo cáo và bằng chứng gồm ảnh before/after, ảnh tại các breakpoint, network/API traces đã che secrets, console errors, SQL assertions, kết quả build/tests, các lỗi chưa xử lý và lý do.
6. Kết luận `UI_RELEASE_GATE = PASS | FAIL | BLOCKED_ENV | NEEDS_APPROVAL`, giải thích cụ thể. Không đánh dấu PASS chỉ vì đã sửa CSS.

## II. SỬA 8 LỖI LOGIC UI — KHÔNG ĐƯỢC BỎ SÓT

### UI-A01 — Offline/API lỗi không được giả hiển thị sân trống

Điểm cần rà: `wwwroot/js/shared.js` (`DEFAULT_COURTS`, `BOOKINGS`, state sync), `dashboard.js`, `booking.js`, các trang sân và dashboard.

- Thiết kế state machine nguồn dữ liệu: `initial/loading/ready/refreshing/stale/offline/error`; không dùng dữ liệu mặc định cho kết luận “có sân trống”/“giá hợp lệ” khi API chưa thành công.
- Loading: skeleton rõ nhưng không nháy fake empty; ready: dữ liệu có timestamp/version; stale: banner dữ liệu cũ và hạn chế mutation không an toàn; error/offline: banner rõ lý do + nút thử lại; không hiển thị “4/4 sân trống” khi chưa có xác nhận từ server.
- Chặn CTA đặt sân nếu chưa có availability mới và server quote hợp lệ; luôn tái xác minh slot khi tạo booking, xử lý 409 concurrency/trùng sân.
- Kịch bản test: API `/api/db/state` chậm, HTTP 500, timeout, offline, đổi tab, reconnect; **không** hiện dữ liệu giả như thật hoặc thao tác đặt sân khi invalid.

### UI-A02 — Server quote không được dùng lại sau khi đổi lựa chọn

Điểm cần rà: `Views/Booking/Index.cshtml`, `wwwroot/js/booking.js`.

- Một quote gắn chặt với `{courtId, dateISO, startTime, duration, membership identity/eligibility, pricingVersion hoặc server quote id}`. Mỗi lần input thay đổi, **invalid ngay** quote cũ, xóa badge “Báo giá máy chủ”, disable CTA trong khi lấy báo giá mới.
- Chặn response cũ về muộn bằng `AbortController` + request sequence/request key; server quote phải khớp state hiện tại trước khi render; spinner/error rõ ràng, timeout retry có kiểm soát.
- Chỉ hiển thị tiền đã được server chốt làm “Báo giá máy chủ”; tiền ước tính (nếu có) phải ghi rõ “tạm tính” và tuyệt đối không dùng nó làm giá xác nhận.
- Khi submit, backend vẫn tính lại và xác thực giá, trả 409/422 nếu giá/availability thay đổi; UI cho người dùng xem và xác nhận lại giá mới.
- Test đổi sân/ngày/giờ/thời lượng liên tục, response ngoài thứ tự, báo giá HTTP 500/timeout, thay giá tại CMS, thay tier hội viên, 2 tabs và retry.

### UI-A03 — KPI “Thực thu ca hiện tại” phản ánh sổ giao dịch thật

Điểm cần rà: `wwwroot/js/dashboard.js`, ledger endpoints, báo cáo admin.

- Chỉ hiển thị “Thực thu ca hiện tại” nếu có **ca thu ngân thực sự**, mốc bắt đầu/kết thúc, danh sách transaction và công thức được backend xác nhận. Nếu hệ thống chưa có khái niệm ca thu ngân, đổi nhãn và mô tả sang chính xác phạm vi dữ liệu hiện có như “Đã thu trong ngày” kèm ngày/filter; không ngụy trang KPI.
- Lọc payment/refund theo ngày giao dịch, timezone `Asia/Ho_Chi_Minh`, kỳ/ca và phương thức thực tế; không lấy toàn bộ booking đang active để đại diện doanh thu.
- Hiển thị tổng phải thu, đã thu, còn nợ, hoàn tiền thành các metric **khác nhau**, có tooltip về cách tính; không làm thay đổi ledger hoặc business logic.
- Test hai đơn khác ngày, thanh toán nhiều lần/hoàn một phần, ca khác nhau, user không đủ quyền.

### UI-A04 — Báo cáo lỗi API không được toast “thành công”

Điểm cần rà: `wwwroot/js/reports.js` (`renderReports`, `filterReport`).

- `renderReports()` trả result có thể quan sát `{ok, data, error, isStale}` hoặc throw có phân loại. Chỉ success toast khi tải xong và dữ liệu hợp lệ.
- Loading đúng; lỗi 401/403/500/timeout/offline có thông điệp tương ứng. Nếu giữ số liệu lần trước, gắn nhãn “Dữ liệu cũ — cập nhật lần cuối …”; không chuyển nó thành dữ liệu realtime.
- Test lỗi reports endpoint, filter liên tục, response ngoài thứ tự và retry.

### UI-A05 — Cột giá trị 0 không được vẽ thành số dương

Điểm cần rà: `wwwroot/js/reports.js`.

- Value bằng 0 => bar height 0, hiển thị `0 đ`; chỉ áp min-height cho giá trị dương rất nhỏ theo quy tắc có giải thích. Chart dùng cùng data và currency format với summary, có accessible text.
- Test toàn 0, hỗn hợp 0/dương, refund âm nếu backend có, giá trị rất lớn, mốc ngày không có dữ liệu.

### UI-A06 — Sân không tồn tại không được biến thành sân đầu tiên

Điểm cần rà: `wwwroot/js/shared.js` (`getCourtById`) và mọi nơi gọi; Lookup, Booking, POS, Ticket, Reports.

- Helper trả `null`/typed missing khi không tìm thấy; UI nêu “Sân không còn tồn tại (mã …)” hoặc “Đã ngừng hoạt động”. Không tự ánh xạ sang `COURTS[0]`, không tạo hình/giá sai.
- Đối với booking lịch sử, vẫn hiển thị snapshot tên sân nếu backend có; không mất lịch sử. CTA đổi sân/đặt sân phải an toàn và có xác nhận.
- Test court đã xóa/ngưng hoạt động, unknown ID, court list rỗng.

### UI-A07 — Deep link chọn ngày có active state chính xác

Điểm cần rà: `wwwroot/js/booking.js` và date strip.

- Date strip và picker hỗ trợ điều hướng tới ngày hợp lệ trong horizon do server cấu hình; ngày trong URL phải hiện active trong viewport (tự scroll hợp lý) hoặc bị từ chối có thông báo nếu ngoài horizon.
- Dùng ISO date rõ ràng; không chuyển sai ngày do parse UTC/local của JS. Chặn quá khứ, ngày đóng cửa, ngày vượt giới hạn.
- Test các trường hợp ngày cuối tháng, cuối năm, tháng 2, booking URL trực tiếp, reload, timezone Việt Nam.

### UI-A08 — Không còn slot thì reset sạch lựa chọn cũ, disable thao tác

- Khi không có slot hoặc slot đổi thành bận, xóa select option cũ, cập nhật trạng thái phù hợp, CTA disabled kèm lý do ngắn gọn; gợi ý sân hoặc giờ khác khi có dữ liệu thật.
- Không cho submit từ keyboard, Enter hoặc double-click khi invalid. Khi slot mới khả dụng và quote hợp lệ, CTA mới bật lại.
- Test full slots, court lock, booking cạnh tranh hai client, slot chuyển trạng thái khi đang chọn.

## III. SỬA 8 VẤN ĐỀ BỐ CỤC/THIẾT KẾ

### UI-B01 — Dark Sport QR ticket: contrast và trạng thái nút

- Điểm cần rà: `wwwroot/css/styles.css` `.btn-outline`, `.spk-ticket-dark .spk-ticket-actions`.
- Mọi nút “Tải QR PNG”, “Tải SVG”, “Phóng to”, “In vé” phải đọc được trên nền tối, bảo đảm WCAG AA thông thường: chữ ≥4.5:1, large text ≥3:1 khi có thể; focus/hover/active/disabled phân biệt rõ.
- QR vẫn nằm trên nền sáng, quiet zone đủ, không chỉnh module encoder để lấy thẩm mỹ. Test cả Light Premium/Dark Sport và trạng thái disabled/loading.

### UI-B02 — Mobile ticket: không cắt/gãy mã vé và không đè badge

- Ở 320–430px, thay layout hero thành xếp dọc hoặc wrap có chủ đích; mã vé nguyên vẹn, có copy button accessible, không phá layout khi mã dài bất thường; tên sân và badge không chồng.
- Không chỉ dùng `font-size:10px` để che lỗi; áp dụng `min-width:0`, flex/grid hợp lý, wrapping có kiểm soát, nội dung fallback cho chuỗi cực dài, tránh overflow toàn trang.
- Test mã ngắn/dài, tên sân dài, chưa thu/đã thu một phần, Dark/Light, zoom 200%.

### UI-B03 — Nội dung marketing/Home/CMS lấy từ sự thật, không hardcode sai

- Điểm cần rà: `Views/Home/Index.cshtml`, `Views/Shared/_Layout.cshtml`, `dashboard.js`, API settings/venues.
- Số sân, loại sân, giờ mở cửa, địa chỉ, hotline, hình ảnh, giá công bố, ưu đãi, tier hội viên phải lấy từ server/CMS **nếu đã có nguồn hợp lệ**. Không mặc định “4 sân” nếu DB có 3 hoặc 6 sân. Giá phải chính xác theo loại sân/khung giờ, không cố định phụ thu vô căn cứ.
- Headline tier phù hợp: “Ưu đãi đến 15% (tùy hạng)” chỉ khi DB xác nhận chính sách thực; nếu thiếu cấu hình thì hiển thị copy trung tính, không bịa tiện ích.
- Dữ liệu CMS thiếu: placeholder có chủ đích, không giá giả. Escape/sanitize HTML output đúng Razor; không đưa user-provided HTML vào DOM không kiểm soát.

### UI-B04 — Vé QR phân biệt tổng phải thanh toán và thực thu

- Thẻ vé thể hiện **“Tổng phải thanh toán”, “Đã thanh toán”, “Còn lại”** theo ledger authoritative và badge `Chưa thanh toán / Đã thanh toán một phần / Đã thanh toán đủ` nếu data/roles phù hợp. Không ghi “Tổng thanh toán” khiến khách hiểu là đã thu.
- Số tiền định dạng VND chính xác, không cộng double Shop merged, không dùng `paidAmount` client giả; refresh sau payment/refund có trạng thái syncing.
- Cẩn thận quyền xem số tiền của khách vãng lai và vé đã hủy, không vô tình hiển thị thông tin người khác.

### UI-B05 — Pro Shop mobile: giỏ hàng và checkout luôn tìm được

- Mobile (đặc biệt khi 20–30 sản phẩm) có sticky bottom cart summary hoặc bottom sheet để mở giỏ; hiển thị item count và tổng tiền chính xác; không che bottom navigation, cookie/keyboard, vùng an toàn `env(safe-area-inset-bottom)`.
- Không trùng hai nguồn trạng thái giỏ; cart state cập nhật sau add/remove/stock change; CTA checkout disable nếu hàng hết/tồn kho không đủ/API lỗi. Không cho ấn checkout hai lần tạo hai đơn.
- Test keyboard mở, modal sheet, scroll 30 sản phẩm, giỏ rỗng, mất mạng, stock changed by another customer.

### UI-B06 — Tab lọc sản phẩm có affordance cuộn/được focus

- Dải category tab trên mobile phải có fade edge, hint/chevron hoặc wrap ở breakpoint phù hợp; nút có hit target >=44×44 CSS px khi có thể. Tab đang active hiển thị rõ và scroll into view; keyboard arrow/tab hoạt động theo pattern thích hợp.

### UI-B07 — Sửa fallback asset không tồn tại

- `wwwroot/js/shared.js` tham chiếu `/assets/hero-pickleball-club.png` trong khi asset thực có `/assets/hero-spike-pickleball.png`. Đối chiếu mọi ảnh/icon/fonts/network 404; dùng manifest/path hiện hữu và có fallback hợp lệ. Không tự thêm ảnh stock lạc phong cách.

### UI-B08 — Design system nhất quán, không đại refactor phá layout

- Rà `wwwroot/css/styles.css`, CSS trong Login, Admin layout, style inline; định nghĩa token dùng chung màu sắc, spacing, typography, radius, shadows, focus, z-index, modal, toast, field, button, card, table và breakpoint.
- Giữ cá tính Spike Pickleball: trang đặt sân ưu tiên sân/giờ/giá, trang shop ưu tiên sản phẩm, admin ưu tiên mật độ thông tin và hiệu suất thao tác. Không bắt mọi trang dùng cùng một card/hero.
- Giảm selectors ghi đè chéo, dùng CSS riêng theo component/module khi lợi ích rõ. Refactor **theo trang**, chạy visual regression trước/sau. Không loại bỏ hiệu ứng trang Login nếu chưa xác minh không làm giảm trải nghiệm.

## IV. SỬA 3 LỖI COMPONENT/ACCESSIBILITY/STATE

### UI-C01 — Xóa dần ticket/QR DOM ẩn trùng lặp sau khi đổi test

- Điểm cần rà: `Views/Booking/Index.cshtml`, `Views/Lookup/Index.cshtml`, `booking.js`, `lookup.js`.
- Tìm các khối QR ẩn giữ vì test cũ; chuyển tests sang ticket hiện hành và `data-testid` rõ ràng trước khi bỏ legacy DOM/JS; đảm bảo không phát sinh hai request ảnh, hai state, hai ticket trên accessibility tree.
- Không xóa phần tử nếu luồng thực tế còn phụ thuộc mà chưa thay thế được; ghi rõ mục nào còn legacy.

### UI-C02 — Modal, court selector, QR zoom/copy dùng được bằng bàn phím

- Dùng `<button type="button">` thay `div onclick` khi khả thi; label accessible, focus-visible, tab order logic. Modal/Bottom sheet: `role="dialog"`, `aria-modal`, `aria-labelledby`, trap focus, Escape đóng nếu phù hợp, restore focus, khóa cuộn nền hợp lý, focus tới thông báo lỗi và keyboard-only thao tác được.
- Không nested interactive controls; đảm bảo touchscreen, keyboard, screen reader; reduced motion theo hệ điều hành. Tooltips không là nguồn thông tin duy nhất.

### UI-C03 — Loading, empty, filtered empty, offline và error phải khác nhau

- Tạo các component/state patterns dùng chung cho Home/Courts, Booking, Shop, Dashboard, Reports, Lookup, CMS, Admin: skeleton, empty database, no matching filter, request error, offline/retry, stale timestamp, success.
- Không viết “Không tìm thấy sản phẩm” khi API đang loading/500, không vẽ bảng trống khi chưa có quyền hoặc chưa tải xong. 401/403 phân biệt 404, 409, 422, 429 và 500 với thông báo phù hợp.

## V. KIỂM TOÁN MỌI TRANG VÀ LOGIC UI, KHÔNG GIỚI HẠN 19 FINDING

Sau khi sửa 19 mục, quét lại **toàn bộ tuyến trang và các role thực có**. Tự thu thập danh sách route từ MVC routing, Views và sidebar/nav; không giới hạn ở một danh sách giả định. Bao phủ tối thiểu:

- Public: Home, Courts/Booking, Pro Shop, Lookup, guest ticket, Login/Register/Forgot Password (phần phụ thuộc OTP vẫn ghi `BLOCKED_EXTERNAL`, không giả đã gửi email), Profile nếu có.
- Staff/Admin: Dashboard, Bookings/Management, Court schedule/locks, Check-in QR, POS/Orders, Pro Shop stock/products, Members, Reports, Settings/CMS, Accounts, Password change và các màn hình thực có.
- Thành phần liên tuyến: header, nav, sidebar collapse, footer, notification/toast, dropdown, modal, tabs, date-picker, time-picker, form validation, confirmation, pagination, tables, charts, ticket QR, print layout.

**Kiểm tra cụ thể trên từng trang:** visual hierarchy, pixel clipping, overlap, scroll lock, body horizontal overflow, layout shift, buttons offscreen, text truncation, icons missing, spacing, grid, focus, color contrast, Vietnamese diacritics, date/time VNT, VND currency, trạng thái hover/focus/disabled, empty/long text/large numbers, loading/error/retry. Phân biệt horizontal scroll chủ đích của bảng hoặc date strip với lỗi tràn toàn trang. Không xóa affordance cuộn chỉ để đạt phép đo overflow.

Tạo `docs/ui-v5/ROUTE_MATRIX.md`: mỗi route/role × state × breakpoint × actions × API, có liên kết screenshot và test. Đừng đánh giá “đẹp” mà không chạy interaction thực tế.

## VI. HỢP ĐỒNG UI ↔ API ↔ SQL SERVER — TIÊU CHÍ BẮT BUỘC

### 1) Lập data contract map

Tạo `docs/ui-v5/DATA_CONTRACT_MATRIX.md` ghi `UI field → API endpoint/DTO/property → Service/query → DB entity/column → formatter → action/permissions`. Bắt đầu với: số sân đang hoạt động, availability theo ngày/giờ, booking IDs/status/holdUntil, quote & pricing version, paid/refund/debt, QR token/status, stock & product image, member tier/benefit, staff permissions, CMS contact/hours/images.

### 2) Server authority, cache, stale, races

- State payload phải được validate với schema/contract phù hợp; không tự tạo `success:true` khi thiếu trường. Cần có version/timestamp nếu backend đã hỗ trợ. Với ETag/304, không kết luận data mới khi chỉ có snapshot cũ; hiển thị stale theo chính sách; khi mutation thành công, refetch hoặc cập nhật có căn cứ từ response thật.
- Kiểm tra request races (đổi ngày/giờ nhanh), optimistic UI có rollback khi server từ chối, retry idempotency cho mutation, chống double-submit, xử lý 409/422/429/401/403 đúng.
- Không thay booking.status tại frontend; computed badge có thể `quá giờ` nhưng trạng thái kinh doanh vẫn là server status. Không suy đoán payment success từ frontend, không dùng cache làm bằng chứng thu tiền.
- Audit tất cả nút thao tác: quyền actor, trạng thái hiện tại, thứ tự state transitions, thông báo sau mutation, value display sau reload và trên thiết bị thứ hai.

### 3) SQL Server test phải độc lập với mock

- Sử dụng SQL Server **test** database (container/local/test instance) nếu có. Không chạm production. Tạo fixture/seed có đủ: 0 sân, 1 sân, nhiều sân, disabled court, pending/approved/playing/completed/cancelled, booking tranh lịch, hết hạn hold, nhiều tier, pending payment, thu một phần, refund, shop stock 0/1/n, linked order, dữ liệu CMS thiếu, tên dài, tiếng Việt có dấu, ngày cuối tháng.
- Với từng tác vụ E2E quan trọng, assert **UI displayed value ↔ API response ↔ DB persisted state** sau mutation và sau reload/đăng nhập role khác. Báo cáo chênh lệch kể cả 1 đồng hoặc 1 trạng thái.
- Đối chiếu theo đơn vị tiền `decimal`/VND và quy tắc làm tròn từ server; tuyệt đối không dùng JS float để chốt giá tiền. UTC trong DB, timezone địa phương đúng khi hiển thị; locale `vi-VN`.
- Kiểm tra DB schema bằng EF migrations/read-only inspection; chỉ tạo migration khi chủ dự án phê duyệt. Nếu thiếu SQL Server, tạo test fixture phù hợp, chạy unit/browser mock cho phần có thể, báo `BLOCKED_ENV` với lệnh chuẩn để chủ dự án chạy, không tự tuyên bố tương thích DB tuyệt đối.

### 4) Quy tắc quyền và dữ liệu

- Guest không đọc được booking người khác; Customer chỉ thấy dữ liệu mình được phép; Staff chỉ thấy hành động đã được cấp; Admin hợp lệ; nút ẩn **không thay thế kiểm tra quyền backend**.
- QR/ticket chỉ hiển thị trạng thái và tiền theo quyền; không đưa secret vào URL công khai, local logging, screenshot bằng dữ liệu production. 404/403 phải có UX thống nhất.

## VII. DESIGN PROCESS & VÒNG LẶP TỰ KIỂM TRA/SỬA LỖI

### Phase 0 — Baseline + Design critique (không bỏ qua)

1. Chụp ảnh **trước khi sửa** tất cả route chính có thể truy cập ở kích thước 320, 375, 390, 414, 768, 1024, 1440, 1920 px; tối thiểu test mobile/desktop trong từng nhóm route. Kiểm tra dark/light, 200% zoom, text dài, danh sách 0/1/30+ dòng, modal mở, keyboard, lỗi API.
2. Tạo `docs/ui-v5/DESIGN_SYSTEM.md`: hệ màu thực tế 4–6 hex, font/type scale, spacing 4/8px, component tokens, breakpoint, card/button/form/table/dialog, loading/error state, đặc tính riêng của brand. Không làm lại nhận diện nếu không có lý do UX.
3. Tạo screenshot catalog `docs/ui-v5/screenshots/before/`. Phân loại issue do code hiện tại, issue chỉ xảy ra với mock/preview, issue không tái hiện. Baseline console/network/JS errors, `document.documentElement.scrollWidth > innerWidth`, bounding rect overlap/crop, missing assets, contrast.

### Phase 1 — Fix source-of-truth UI bugs

Sửa **UI-A01 → UI-A08**, đồng thời `UI-C03`. Mỗi lỗi có regression test gây lỗi ban đầu (nếu có thể), sửa, build/test, screenshot/DB assertion. Phân biệt lỗi API với lỗi empty data. Không dùng biến `DEFAULT_...` để thay cho record thật trong production.

### Phase 2 — Fix visual/UI system

Sửa **UI-B01 → UI-B08**, `UI-C02`. Theo đúng skill frontend-design: design review có chủ đích trước khi code, kiểm lại sau mỗi nhóm. Giữ website đặc trưng cho Pickleball; chọn 1–2 điểm nhấn thị giác (ví dụ hero & vé QR), không rải gradient, border, badge và animation vô nghĩa toàn trang.

### Phase 3 — Remove legacy/duplicate safely

Sửa `UI-C01`; xác định references bằng search/static + runtime trước khi xóa. Dọn CSS selectors trùng theo component, asset 404, buttons không hoạt động, chỗ hiển thị thừa. Không đại refactor làm phá regression.

### Phase 4 — Automated QA / screenshot repair loop

1. Dùng **Playwright** hoặc browser automation có thể chạy với bản ASP.NET Core thực; nếu thiếu, cài dependencies **phù hợp repo** với lý do rõ, không cài tràn lan. Khởi động test DB/app, đăng nhập từng role bằng test credentials an toàn, truy cập các route thực.
2. Chạy test layout tại `320,360,375,390,414,430,768,1024,1280,1440,1920`; desktop + mobile emulation, Chrome/Chromium. Nếu có Firefox/WebKit thì chạy thêm; không claim kiểm Safari/iPhone thật nếu chỉ dùng Chromium emulation.
3. Screenshot **full-page + component-level** cho từng page/state, so với baseline đã review. Pixel-diff có ngưỡng và ignore vùng động (đồng hồ, QR token test, timestamps) nhưng **mọi khác biệt đáng kể phải được review bằng mắt/công cụ thị giác**, không pass hàng loạt bằng update snapshots.
4. Instrument `console.error`, `pageerror`, failed requests, network status, 404 asset, JS exceptions, overflow measurements, overlap geometry, focus keyboard, basic axe/WCAG contrast. Một số trường hợp cần kiểm thủ công để tránh false positive.
5. Khi có lỗi: (a) ghi ảnh/trace/URL/role/breakpoint, (b) xác định root cause, (c) sửa đúng component/contract, (d) chạy unit + integration liên quan, (e) chụp lại để xác nhận, (f) chạy regression toàn site. **Lặp lại vòng kiểm tra–sửa–kiểm tra** cho tới khi không còn issue tái hiện trong **ma trận nghiệm thu đã định nghĩa**, hoặc ghi rõ giới hạn/chướng ngại vật. Không bỏ qua lỗi bằng cách ẩn element/disable test/cập nhật baseline vô điều kiện.
6. Khóa vòng lặp bằng bằng chứng: sau mỗi pass cập nhật `docs/ui-v5/FIX_LOG.md` và `docs/ui-v5/TEST_EVIDENCE.md`, gồm issue, source commit/file, test command/result, screenshot before/after. Nếu phát hiện lỗi mới, thêm `UI-NEW-XX` và lặp tiếp. Có thể dừng khi bị chặn môi trường/credential nhưng phải ghi trạng thái `BLOCKED_*`, không hứa chạy nền hoặc báo hết lỗi.

### Phạm vi test hành trình thực tế

- Khách: mở trang chủ → xem sân đúng CMS → chọn ngày/giờ → lấy quote mới → đặt sân → xem vé/QR → tra cứu/hủy theo quyền → theo dõi trạng thái.
- Staff: đăng nhập → xem sân đã đặt → approve pending hợp lệ → check-in QR → kết thúc ca → thu tiền đúng → ghi nhận đơn shop/hoàn stock.
- Admin: thay giá/giờ hoạt động hoặc ẩn sân → khách thấy dữ liệu mới sau refresh; sửa/tắt sản phẩm → cart/stock cập nhật; xem report và đối chiếu ledger.
- Hai người dùng song song: một người đặt slot hoặc mua stock=1 → người còn lại nhận conflict và UI cập nhật đúng; timeout/retry/double-click không tạo giao dịch trùng.
- Error cases: 401,403,404,409,422,429,500, network disconnected, slow response, stale ETag, token CSRF hết hạn và login/logout trong 2 tab.

### Performance & accessibility

- Không kéo theo thư viện UI nặng không cần thiết. Không render/lấy lại toàn bộ data vô tội vạ chỉ vì chuyển tab; cancel stale requests, debounce phù hợp.
- Không gây layout shift rõ rệt, có `loading` skeleton giữ kích thước; ảnh lazy-load phù hợp, font fallbacks, tránh console warnings.
- WCAG AA thực tiễn: focus visible, keyboard usable, sufficient contrast, dialogs focus management, aria names, images alt, errors announced, prefers-reduced-motion. 200% zoom và mobile landscape không làm mất nội dung quan trọng.

## VIII. ACCEPTANCE GATES — KHÔNG ĐƯỢC TỰ BÁO '0 BUG' BẰNG ĐOÁN

Chỉ đề xuất `UI_RELEASE_GATE = PASS` khi **đồng thời**:

1. `UI-A01…08`, `UI-B01…08`, `UI-C01…03` **đều có `DONE_VERIFIED` với bằng chứng**; các finding mới mức high/critical đã xử lý, không còn failure trong test matrix.
2. `dotnet build`, các test phù hợp, `node --check`, Playwright E2E thực chạy và log kết quả; nếu môi trường không có .NET/SQL thì gate phải `BLOCKED_ENV` (không thay bằng mock để PASS).
3. Mọi trang/role/state bắt buộc không có lỗi `pageerror`, console error quan trọng, route 404 ngoài dự kiến, image 404, content overlap, CTA bị che, body-level overflow ngoài ý muốn; screenshot regression qua vòng review thực tế.
4. Server quote, availability, booking status, paid/refund, stock, CMS hiển thị **trùng API và DB** trong test SQL thật. Mọi mutation phải phản ánh đúng sau reload và mở ở thiết bị/tab khác; không hiển thị success khi server fail.
5. Các hành trình quan trọng Guest/Customer/Staff/Admin hoạt động hoàn chỉnh hoặc lỗi không thể kiểm do thiếu phụ thuộc được khai báo rõ và gate không PASS toàn phần.
6. Không có vi phạm phân quyền/PII/secret trong flow UI, không thao tác sai trạng thái, không hồi quy 4 V4 release blockers trước đó.
7. Các giả định về responsive Safari/iPhone thật, OTP provider, payment gateway nếu chưa kiểm được phải liệt kê ngoại lệ rõ, **không được mô tả như đã PASS**.

**Mục tiêu “sửa tới khi không còn lỗi” được hiểu là không còn lỗi TÁI HIỆN ĐƯỢC trong phạm vi các tuyến trang, vai trò, trạng thái, breakpoint và kịch bản đã kiểm; không ai có thể bảo đảm tuyệt đối mọi lỗi tương lai.** Nếu không đạt gate, ghi `FAIL`/`BLOCKED_ENV`/`NEEDS_APPROVAL` và nêu blockers, không che lỗi bằng thay đổi số liệu hoặc nới test.

## IX. CÁC FILE BẮT BUỘC TẠO/CẬP NHẬT

- `docs/ui-v5/UI_V5_IMPLEMENTATION_PLAN.md`: 19 finding gốc + finding mới, trạng thái `NOT_STARTED | IN_PROGRESS | DONE_VERIFIED | PARTIAL | BLOCKED_ENV | BLOCKED_EXTERNAL | NEEDS_APPROVAL`; file liên quan/test/evidence link.
- `docs/ui-v5/DESIGN_SYSTEM.md`: tokens, principles, components, states, responsive, QR styles, quy tắc consistency.
- `docs/ui-v5/ROUTE_MATRIX.md`: tất cả page/role/state/breakpoint/CTA.
- `docs/ui-v5/DATA_CONTRACT_MATRIX.md`: UI ↔ DTO ↔ API ↔ Service ↔ SQL, source of truth, formatting, quyền.
- `docs/ui-v5/TEST_EVIDENCE.md`: commands, timestamps, results, failures, screenshot paths, browser, DB assertions, api traces đã mask secrets.
- `docs/ui-v5/FIX_LOG.md`: vòng trước/sau, nguyên nhân, sửa đổi, regression results, những lỗi mới.
- `docs/ui-v5/UI_RELEASE_GATE.md`: PASS/FAIL/BLOCKED_ENV/NEEDS_APPROVAL kèm giải thích; tách phần visual đẹp và dữ liệu đúng.
- `docs/ui-v5/screenshots/{before,after}/`: bằng chứng ảnh trang/component và responsive, chỉ dùng dữ liệu test không nhạy cảm.

**Báo cáo cuối bằng tiếng Việt**, có bảng 19 ID đúng nghĩa, tổng số đã verified, các files thay đổi, test chạy thật và test chưa chạy, screenshots quan trọng, lỗi còn lại, rủi ro kiến trúc, các đề xuất nâng cấp ở phiên sau. Không chỉ báo “39/40 DONE” hay “100% hoàn thành” mà không có evidence.

## X. LỆNH BẮT ĐẦU

Hãy triển khai trực tiếp trên source hiện hành: **đọc skill → baseline và mapping → sửa lỗi UI trust → sửa visual responsive → sửa accessibility/legacy → browser E2E kết nối SQL test → screenshot audit tự lặp → chốt release gate**. Tận dụng code ổn định, không làm lại dự án. Sau mỗi nhóm sửa, chạy test liên quan và tiếp tục cho tới khi không còn lỗi tái hiện trong phạm vi nghiệm thu; nếu gặp blocker thực, ghi chi tiết, tiếp tục các phần không phụ thuộc, tuyệt đối không tự báo PASSED.
