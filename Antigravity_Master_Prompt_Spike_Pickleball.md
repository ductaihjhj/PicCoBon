# MASTER PROMPT CHO ANTIGRAVITY — SỬA & NÂNG CẤP TOÀN DIỆN SPIKE PICKLEBALL CLUB

> **Cách dùng:** Mở thư mục gốc của dự án `pickleball-ui` trong Antigravity, chuyển agent sang chế độ có thể đọc/sửa code, rồi dán **toàn bộ nội dung dưới đây**. Nếu có thể, đặt thêm file `Spike_Pickleball_Audit.md` ở thư mục gốc để agent đối chiếu chi tiết. Prompt này vẫn tự chứa các hạng mục cần xử lý nếu không có file audit.

---

## VAI TRÒ VÀ MỤC TIÊU

Bạn là **Lead Full-stack Engineer + Solution Architect + Security Engineer + QA Engineer + UI/UX Reviewer**. Hãy **trực tiếp làm việc trên source code đang mở**, không dừng lại ở phân tích hoặc chỉ tạo checklist. Nhiệm vụ: **sửa toàn bộ các vấn đề P0/P1/P2 bên dưới, hoàn thiện nghiệp vụ đặt sân pickleball và bán hàng để chạy được với SQL Server thật, bảo mật, nhất quán nhiều người dùng, dễ vận hành và không phá giao diện hiện có**.

**Dự án hiện tại:** ASP.NET Core MVC **.NET 10**, Razor Views, Entity Framework Core 10, SQL Server, CSS, vanilla JavaScript, Admin Area, các trang khách hàng; solution `PickleballClub.slnx`, project `PickleballClub.csproj`, `Program.cs`, các Controllers, Models, `wwwroot/js`, `Areas/Admin`. **KHÔNG** chuyển sang React/Next.js, không đổi stack, không viết lại từ đầu trừ khi bắt buộc và có lý do. Ưu tiên sửa có kiểm soát, tái sử dụng các trang/component hiện tại.

**Quy tắc bắt buộc:**
1. Đọc `AGENTS.md`; khi sửa UI phải đọc `.agent/skills/frontend-design/SKILL.md` nếu có, tuân theo design system. Với yêu cầu này, chủ dự án **cho phép** sửa API, controllers, entities và database schema **trong phạm vi những mục P0/P1/P2**; sự cho phép này không bao gồm xóa dữ liệu thật, chạy destructive SQL hoặc tự triển khai production. Hạng mục nằm ngoài phạm vi phải nêu và xin phép.
2. Đọc toàn bộ source *ứng dụng* và các quan hệ giữa frontend–backend–DB; bỏ qua `bin/`, `obj/`, `.git/`, vendor và thư mục temp khi rà soát. Đối chiếu với `Spike_Pickleball_Audit.md` nếu file tồn tại. **Không sửa máy móc theo tên/line cũ**: xác minh vấn đề còn tồn tại trong source hiện tại trước khi chỉnh.
3. **Thực hiện công việc thật**, chỉnh source và chạy kiểm thử nếu môi trường cho phép. Không chỉ viết báo cáo, không tạo mock để che phần chưa làm, không đổi text “thành công” thay cho xử lý backend thật.
4. Trước sửa: tạo branch làm việc (nếu có Git), ghi snapshot/danh sách file sửa; bảo toàn database và dữ liệu. Mọi thay đổi schema phải có migration tiến/lùi hoặc kế hoạch rollback, không `DROP DATABASE`, không `EnsureDeleted`, không auto-seed tài khoản mặc định lên production.
5. Làm theo thứ tự P0 → P1 → P2. Có thể chia thành các commit/batch nhỏ, mỗi batch phải build/test, cập nhật nhật ký. **Đừng dừng sau pha lập kế hoạch** và đừng hỏi lại cho từng việc đã có chỉ dẫn; tiếp tục triển khai cho đến khi hết các mục khả thi trong workspace. Nếu vướng secret, OAuth account, VNPAY merchant hoặc SQL Server không có sẵn, làm đầy đủ phần độc lập, cung cấp cấu hình + hướng dẫn tích hợp; **ẩn/disable UI chưa hoạt động** và ghi rõ là chưa kiểm thử E2E.
6. Giữ UI/UX tiếng Việt nhất quán, responsive desktop/tablet/mobile, không phá CSS và bố cục hiện tại. Không thêm dependency thừa. Không làm mất tính năng đang chạy; thay API cũ bằng luồng tương ứng trước khi loại bỏ.
7. Không ghi dữ liệu thật/credential vào log, không hardcode bí mật, không làm giả kết quả test.

## CHÍNH SÁCH NGHIỆP VỤ MẶC ĐỊNH ĐỂ TRIỂN KHAI

Cho phép cấu hình chính sách qua server/database thay vì hardcode. Nếu source hiện có quy định chính thức mâu thuẫn, ưu tiên bảo toàn quy định có bằng chứng, ghi rõ xung đột vào `docs/DECISIONS_NEEDED.md` và triển khai phần không phụ thuộc.

- **Đơn đặt sân:** khách tạo yêu cầu `pending` → nhân viên đủ quyền `approved` → khách check-in bằng QR/code hợp lệ `checked_in` → `in_progress` → `completed`; có `cancelled`, `expired`, `no_show` riêng. `pending` có thể giữ slot tạm với thời hạn cấu hình (mặc định **15 phút**, không dùng nếu không phù hợp workflow chờ duyệt dài); không được overbook. Booking chỉ xác nhận sau khi server commit thành công.
- **Giá:** mặc định **tính theo thời lượng thực ở từng khung giờ thường/giờ vàng**, hỗ trợ cắt qua ranh giới (ví dụ Outdoor 16:30–18:30 = 285.000đ với bảng giá trong audit, nếu bảng giá không thay đổi). Backend là nguồn tính giá duy nhất; UI chỉ hiển thị quote server. Giới hạn thời gian theo open/close, không chấp nhận đặt giờ quá khứ. Lưu giá chốt và breakdown lúc đặt để đối soát.
- **Thanh toán:** tách `AmountDue`, `PaidAmount`, `PaymentStatus`, payment transactions/refunds; **mặc định ưu tiên tiền mặt/chuyển khoản tại quầy được nhân viên xác nhận**, không tự đánh dấu đã thu khi tạo vé hoặc kết thúc ca. VNPAY chỉ bật khi đủ merchant secrets + callback xác minh signature và đã test.
- **Check-in:** QR ký/an toàn, chỉ vé đã duyệt và còn hiệu lực, chống dùng lại; cho phép nhập mã vé thay thế có xác thực; giới hạn thời điểm check-in có thể cấu hình. Không tự chuyển thành `playing` chỉ vì đến giờ.
- **Quyền:** `Admin`, `Staff` (nếu cần cho POS/check-in), `Customer`; áp dụng least privilege. Khách chỉ xem/sửa đơn của mình. Quản trị nhân sự chỉ Admin có quyền; không có public admin registration.
- **Đơn hàng:** sản phẩm/giá/tồn kho do server xác định; đơn shop độc lập hoặc gắn booking đúng FK; không trừ tiền hai lần; giao dịch idempotent.
- **Hội viên:** ưu đãi (ví dụ VIP 15%) chỉ hiển thị/áp dụng nếu có rule thật ở server; không dùng số tiền dự kiến làm “tổng đã chi tiêu”.
- **Ngoại lệ:** các nhà cung cấp bên ngoài chưa có credential thì xây adapter/interface, config qua environment/secrets, UI ở trạng thái rõ ràng; không tự chế OAuth/OTP/thanh toán giả.

## P0 — PHẢI SỬA ĐẦU TIÊN: 12 VẤN ĐỀ NGHIÊM TRỌNG

- **P0-01 — Lẫn đăng nhập giữa người dùng:** bỏ trạng thái tài khoản `static` trong `Utilities/Function.cs`; chuyển sang ASP.NET Core Cookie Authentication + claims/principal theo request, `HttpContext.User`, `[Authorize]`; không để hai browser dùng chung phiên.
- **P0-02 — Cookie giả mạo:** bỏ cookie `pb_admin_uid` kiểu ID thuần và middleware tự khôi phục trong `Program.cs`; cấu hình cookie auth được ký mã hóa, `HttpOnly`, `Secure` trên HTTPS, `SameSite`, expiry, logout/revocation phù hợp; không tin ID lấy từ client.
- **P0-03 — Public admin registration:** chặn `Areas/Admin/Controllers/RegisterController.cs` cho người không đủ quyền; nhân viên chỉ được Admin có thẩm quyền tạo/mời; không cho client tự gán role cao.
- **P0-04 — Phân quyền toàn bộ Admin Area:** bảo vệ tất cả trang và mutating actions trong `Areas/Admin/Controllers/*` (đặc biệt Accounts, Bookings, Courts, Menus, Products, Home/POS/Checkin); dùng role/policy rõ ràng và owner checks; kiểm tra GET nhạy cảm + POST/PUT/DELETE, tránh chỉ dựa `IsLogin()`.
- **P0-05 — API sync không bảo vệ:** loại bỏ/khóa `POST /api/db/sync` ghi snapshot toàn cục trong `Controllers/ApiDbController.cs`; thay bằng các API nghiệp vụ chuyên biệt (`bookings`, `cancel`, `approve`, `checkin`, `extend`, `court-locks`, `orders`, `products`, `cms`), DTO/validation/authz/antiforgery hoặc cơ chế CSRF phù hợp. Không tạo endpoint ghi tùy ý state.
- **P0-06 — API rò rỉ dữ liệu cá nhân:** giới hạn `GET /api/db/state` hoặc thay hoàn toàn; public chỉ được dữ liệu lịch bận ẩn danh/sân/sản phẩm công khai; không lộ phone, email, member, full bookings, booking code, financial data. Admin cần role, khách chỉ bookings của chính mình.
- **P0-07 — IDOR tra cứu/hủy vé:** sửa `wwwroot/js/lookup.js` và API để không xem/hủy đơn người khác. `GET /api/bookings/me`, `POST /api/bookings/{id}/cancel` với kiểm tra chủ sở hữu, tình trạng đơn, thời hạn hủy, hoàn tiền; nếu tra cứu mã vé cho guest thì yêu cầu kênh xác minh bổ sung.
- **P0-08 — Trùng lịch khi nhiều người dùng:** tạo một `BookingService` trung tâm (bao gồm walk-in, admin tạo/duyệt, gia hạn, đổi sân, maintenance); check overlap tại DB trong **SQL transaction có chiến lược khóa/isolation phù hợp**, không chỉ frontend; xử lý 409 Conflict; test 2 request đồng thời cùng sân/giờ chỉ 1 booking thắng.
- **P0-09 — Mật khẩu yếu và secret trong source:** ngừng MD5 và so sánh plaintext, migrate bằng ASP.NET Identity PasswordHasher/PBKDF2 hoặc Argon2id, chiến lược chuyển tài khoản cũ an toàn (rehash khi xác thực hợp lệ, buộc reset khi không thể); không lộ hash; xóa demo/default password khi deploy; thay secret VNPAY/cookie/connection string bằng env/user-secrets/secret store. Không commit secret mới. Ghi rõ secret cũ cần rotate ngoài hệ thống.
- **P0-10 — Client điều khiển giá/stock/order:** tạo đơn chỉ chấp nhận product ID, quantity và tùy chọn hợp lệ; lấy giá từ DB, validate stock, ownership booking, coupon/tier và tổng tiền ở server; transaction, idempotency, không cho quantity âm/0, giá giả, duplicate pay.
- **P0-11 — Ghi đè dữ liệu đa thiết bị:** dừng mô hình `localStorage` snapshot → SQL trong `wwwroot/js/shared.js`; SQL là nguồn dữ liệu duy nhất, dùng API theo tài nguyên, `rowversion`/optimistic concurrency hoặc locking, conflict HTTP 409 + refresh UI; không xóa tất cả court locks/menu/products chỉ vì client thiếu item.
- **P0-12 — XSS / HTML injection:** rà soát `innerHTML`, raw icon HTML, dữ liệu CMS/products/booking injected vào DOM (`shared.js`, `menu.js`, `dashboard.js`, Razor Components). Sử dụng encoding/textContent hoặc template escape, whitelist icon, validation/sanitization theo ngữ cảnh; thêm CSP khả thi và bảo vệ header. Có test payload XSS.

**Gate P0 bắt buộc:** chưa xong P0 không coi hệ thống an toàn; test user A/B đồng thời, guest vs Admin, ownership, forged cookie, unauthorized API, duplicate booking, snapshot lost update, giá sửa ở client, XSS.

## P1 — HOÀN THIỆN NGHIỆP VỤ THẬT: 18 HẠNG MỤC

- **P1-01 QR thật:** render QR chứa token ký/opaque của vé (không chứa PII); scan camera và decode ảnh upload ở `checkin.js` nếu browser/hardware hỗ trợ, nhập mã thay thế; verify trên server, một lần, đúng status, khung giờ, có thông báo lỗi rõ.
- **P1-02 Quên mật khẩu:** flow reset password bằng token/OTP qua email/SMS provider thực, TTL/rate limiting/anti-enumeration và audit; khi chưa có provider, ẩn/tắt giao diện phát OTP, không hiển thị “đã gửi” giả.
- **P1-03 Google/Zalo/Apple login:** tích hợp OAuth/OIDC chính thức theo provider nào có credentials; callback/state/PKCE phù hợp, liên kết tài khoản an toàn; provider chưa cấu hình thì ẩn nút, không giả vờ đăng nhập bằng cách chuyển tab.
- **P1-04 VNPAY + payment ledger:** thiết kế giao dịch thanh toán với amount/currency/status/provider reference và callback/idempotency; verify checksum/signature theo tài liệu provider, tránh tin return URL frontend; hỗ trợ thu tại quầy ngay cả khi VNPAY chưa cấu hình.
- **P1-05 Tổng phải thu khác tổng đã thu:** bỏ ghi `TotalPaid` trước khi thu; trạng thái chơi và thanh toán độc lập; không tự đánh dấu `paid` khi hoàn tất; hỗ trợ partial payment, refund, cancellation theo policy.
- **P1-06 Báo cáo doanh thu thật:** tổng hợp từ successful payments (booking + Pro Shop/F&B), tách cọc/thu còn lại/hoàn tiền, không tính pending/unpaid, có lọc ngày/loại giao dịch/trạng thái; đối soát với ledger.
- **P1-07 State machine chuẩn:** chuyển `pending`, `approved`, `checked_in`, `in_progress`, `completed`, `cancelled`, `expired`, `no_show` theo server policy; không auto `approved → playing` chỉ vì đến giờ; không check-in pending; cấm nhảy status trái phép, log nhân viên.
- **P1-08 Tính giá qua ranh giới giờ:** `PricingService` server phân đoạn theo khung giờ ngày/giờ thực tế, weekend/tier/discount nếu có; trả quote breakdown có thể giải thích; một bộ test giờ thường, giờ vàng, qua biên, đóng cửa, nhiều ngày (nếu cho phép).
- **P1-09 Validate lịch đặt:** giờ mở/đóng lấy từ config server, slot granularity, min/max duration, advance booking, ngày quá khứ, DST/timezone, resource maintenance, disabled grid và input manual đều phải tuân server.
- **P1-10 Gia hạn/đổi thời lượng:** POS gửi lệnh `extend/reschedule` lên server, kiểm tra conflict/lock, báo phí tăng, chỉ commit khi hợp lệ; không sửa object JS và đánh dấu thành công ngay.
- **P1-11 Lịch bảo trì:** lock theo khoảng thời gian, server kiểm tra booking đang tồn tại và quy trình xử lý xung đột; dashboard phản ánh trạng thái sân tại **thời điểm tương ứng**, không khóa cả ngày nếu chỉ khóa 1 giờ.
- **P1-12 Riêng tư khi tra cứu:** bỏ “xem tất cả” cho khách; chỉ xem booking của mình hoặc xác minh mã + OTP; bảo vệ dữ liệu nhạy cảm và chống brute-force booking code.
- **P1-13 Confirm server trước UI:** booking frontend `await` API tạo đơn, chỉ hiển thị vé/QR sau 201 + persisted ID; xử lý 400/401/403/409/422/500, retry không tạo booking trùng, loading/rollback đúng; không catch rỗng.
- **P1-14 Order ↔ Booking thật:** không cho gộp sản phẩm vào vé của người khác, không map product không tồn tại thành ID đầu tiên; lưu `Order.BookingId` là FK thay vì nhét mã vào Address; hỗ trợ đơn mua lẻ.
- **P1-15 Kho hàng:** decrement/reserve khi chốt đơn theo chính sách, chống oversell khi 2 máy thu ngân bán đồng thời, rollback/hủy/hoàn hàng/điều chỉnh tồn kho có lịch sử, hiển thị hết hàng/cảnh báo tồn thấp.
- **P1-16 VIP & chia hóa đơn:** discount VIP (ví dụ 15%) phải chạy từ server và đúng điều kiện; nếu split bill thực thì lưu nhiều payment allocations, tránh chỉ lấy tổng tiền chia số người; nếu không làm split bill thực, đổi nhãn thành “ước tính chia đều” và không coi đã thu.
- **P1-17 Anti-CSRF/validation/transition:** cho tất cả form mutating Admin và customer, CSRF protection phù hợp MVC/cookie; DTO validate, audit status transitions, không cho client tùy ý POST `status`, chuẩn hóa API errors.
- **P1-18 Hội viên:** định nghĩa policy tier rõ ràng, tính từ **payment thực**, không tự suy từ amount dự kiến; persist/recalculate an toàn; hiển thị quyền lợi đúng giá thực được áp dụng.

**Gate P1 bắt buộc:** booking–QR–checkin–POS–payment–report hoạt động liên thông với DB; hàng hết không bán; chưa thu không thành doanh thu; gia hạn không đè booking; UI quote = total ở DB = hóa đơn; demo OTP/OAuth/VNPAY không xuất hiện như tính năng đã chạy.

## P2 — KIẾN TRÚC, UI/UX, DEVOPS, CHẤT LƯỢNG: 10 HẠNG MỤC

- **P2-01 Database:** sửa ngày/giờ `NVARCHAR` sang kiểu `date/time/datetimeoffset` phù hợp, tiền sang `decimal(18,2)` nếu cần, kiểm tra range/unique/index theo court/date/status; rowversion và lịch sử booking/payment/order; migration có bước chuyển dữ liệu cũ và rollback, không làm mất dữ liệu.
- **P2-02 SQL an toàn:** tách script `Database_PickleballClub.sql` có `DROP DATABASE` thành demo-only với cảnh báo, loại khỏi deploy; dùng EF migrations cho production, backup trước upgrade.
- **P2-03 README đúng thực tế:** .NET 10, SQL Server, hướng dẫn restore/build/run/migration/seed dev-only, role test, secrets, test accounts do người vận hành tạo, cách test QR/payment; bỏ mật khẩu mẫu sai hoặc nguy hiểm, bỏ mô tả “MD5 có salt”.
- **P2-04 Dữ liệu đa thiết bị:** SignalR hoặc polling versioned API để cập nhật lịch sân, tồn kho, thay đổi POS gần realtime; phân biệt server truth và cached UI, không phụ thuộc local timer tự reconcile.
- **P2-05 CMS/config:** đưa hotline/địa chỉ/giờ hoạt động/bảng giá và dữ liệu có thể quản trị về một nguồn rõ ràng; bảo vệ write endpoints; rà soát hardcode trong `_Layout.cshtml`, trang Home, dashboard và chính sách.
- **P2-06 localStorage:** chỉ dùng cho draft/filter/cart không nhạy cảm theo thiết kế, có TTL/schema version; không lưu JWT/PII/giá chốt hay toàn DB; xử lý offline/API errors rõ ràng.
- **P2-07 Dữ liệu marketing:** bỏ số hội viên giả/claims chưa xác minh (ví dụ USAPA) hoặc gắn nhãn demo rõ; dùng số thật nếu truy vấn được; không đưa thông tin quảng cáo không có bằng chứng.
- **P2-08 Automated tests + vận hành:** test project xUnit/NUnit, integration tests với SQL Server test nếu có, E2E Playwright nếu browser hỗ trợ; logging/audit, health checks, backups/restore drill, error handling và quan sát lỗi. Không giả claim chạy test nếu không chạy được.
- **P2-09 DOM/Razor safety + UI:** chuẩn hóa render escaped content, form validation, loading/success/error/empty state, responsive cho 375/768/1024/1440px, keyboard accessibility/focus, mobile navigation, bảng/tràn chữ, modal, z-index, scroll, contrast; giữ nhận diện/thẩm mỹ hiện tại.
- **P2-10 Repo/build:** dọn build artifacts `bin/`, `obj/`, thư mục temp khỏi track/ZIP và `.gitignore`; không xóa thư viện cần thiết; đánh giá vendor và assets thừa. Không cam kết giảm dung lượng nếu không đo thực tế.

## KIẾN TRÚC TRIỂN KHAI MONG MUỐN

- Tách **Controllers (HTTP + auth)** → **Services (business rules)** → **EF Core repositories/context** rõ ràng, nhưng không over-engineer; DI và async hợp lý.
- Thiết kế DTO/request/response và API lỗi nhất quán (`ProblemDetails`, status codes 400/401/403/404/409/422/500). Validate với DataAnnotations hoặc phương án tương đương; không bind EF entity từ client cho mutating endpoints.
- Server là nguồn đáng tin cậy duy nhất cho **availability, price, status transition, orders, inventory, payments, owner permissions**. Frontend không được phép ghi trực tiếp state toàn cục.
- SQL Server xử lý atomic commit, concurrency, unique/idempotency key nơi cần; ghi audit ai làm gì, lúc nào, trước/sau đối với admin/payment/booking. Không dùng memory/static mutable state cho phiên hoặc booking.
- Cookie auth an toàn + role/policy + anti-CSRF; tài khoản cũ có quy trình migration mật khẩu thận trọng; rate limit login/reset/lookup/check-in.
- Tính giờ theo timezone nghiệp vụ `Asia/Ho_Chi_Minh`, lưu có quy ước nhất quán. Không dùng string so sánh thời gian để quyết định nghiệp vụ.
- Với đổi schema: tạo migration, script update/sample seed dev riêng, chỉ chạy trên test DB trước. **KHÔNG tự chạy trên production DB**.
- API cũ nếu thay đổi phải cập nhật mọi frontend callers; không để nút/action trỏ endpoint cũ hoặc mock. Xóa dead code và demo fallback gây hiểu lầm.

## TRÌNH TỰ HÀNH ĐỘNG CỤ THỂ

**Bước 0 — Khảo sát và baseline (thực hiện nhanh, không dừng ở đây):**
- Xem `AGENTS.md`, skill frontend, source tree và mọi API caller; lập sơ đồ thực tế account/booking/lock/order/payment/report.
- Chạy `dotnet --info`, `dotnet restore`, `dotnet build`; `node --check` cho JS, `dotnet test` nếu đã có. Ghi baseline và lỗi chưa có từ trước.
- Tạo `docs/IMPLEMENTATION_PLAN.md` mapping **40 ID P0/P1/P2 → file → thay đổi → test → trạng thái** và `docs/WORKLOG.md`, dùng làm checkpoint liên tục. Nếu audit Markdown có trong repo thì so sánh với phát hiện thực tế.

**Bước 1 — P0 Security & server authority:**
- Auth/cookie/role/password/secrets; thay API state/sync; nghiệp vụ booking server transaction/ownership; orders server truth; XSS/CSRF. Migrate client calls để UI vẫn hoạt động.
- Viết unit/integration tests những trường hợp rủi ro; chạy build/test; sửa bug regressions trước khi sang bước 2.

**Bước 2 — P1 Real-world workflows:**
- Booking quote → create → approve → QR → check-in → play → complete → collect payment → report; Admin walk-in, maintenance, extend/reschedule; Pro Shop checkout/stock; membership; reset password/OAuth/VNPAY theo credentials có thật.
- E2E trên các vai trò (guest/customer/staff/admin) nếu có browser và DB; không đánh dấu thành công chỉ vì JS hợp lệ.

**Bước 3 — P2 Consistency & UX:**
- Migrations, real-time update, CMS, refactor safe rendering, responsive audit, monitoring, docs, test suite, clean repo.
- Nếu có khả năng chạy trình duyệt, chụp/kiểm tra màn hình thực tế từng trang tại mobile/tablet/desktop, sửa **gốc nguyên nhân** các lỗi overflow, layout shift, modal, clickable area, thiếu loading/error.

**Bước 4 — Nghiệm thu & bàn giao:**
- Chạy lại toàn bộ build + tests + flow E2E có điều kiện. Viết `docs/FINAL_AUDIT_RESULTS.md` gồm: ID từng mục, trạng thái (`DONE` / `PARTIAL` / `BLOCKED`), file thay đổi, cách test, bằng chứng build/test, tồn đọng thực tế, hướng dẫn cấu hình và thứ tự deploy/rollback. **Không ghi `DONE` cho tính năng chỉ là stub/mock hoặc chưa có backend hoạt động.**

## TEST MATRIX BẮT BUỘC (KỂ CẢ KHI PHẢI VIẾT MỚI)

1. Hai trình duyệt đăng nhập hai tài khoản khác nhau không lẫn session; forged cookie, guest/Admin/customer permission negative tests.
2. Customer A không xem, hủy, thêm sản phẩm vào vé của B; API public không lộ số điện thoại/email/booking code.
3. Hai concurrent booking cùng court + overlapping slot: chỉ một commit; test walk-in vs online, extend vs booking, maintenance vs booking.
4. Case giá 16:30–18:30 qua 17:00; ca đến giờ đóng cửa, quá khứ, ngày nghỉ/weekend, min/max duration, discount VIP; server quote khớp persisted total.
5. POST order sửa giá/âm số lượng/productId giả hoặc hết tồn kho đều bị chặn; hai order đồng thời không oversell; retry không nhân đôi đơn.
6. QR thật có thể quét; token sửa/expired/booking pending/đã check-in → từ chối; camera không cấp quyền có fallback.
7. Đơn unpaid hoàn thành ca vẫn không ghi doanh thu; partial/paid/refund và VNPAY callback lặp không ghi tiền hai lần.
8. Ngắt mạng/API 409/500 lúc đặt sân hoặc thanh toán: UI không tuyên bố thành công; xử lý retry và lỗi rõ ràng.
9. Payload XSS trên tên sản phẩm/CMS/icon, CSRF admin action, login/reset/lookup rate limit, status transition trái phép.
10. Refresh và cập nhật ở hai thiết bị: trạng thái slot/stock/payment không bị rollback do localStorage, dữ liệu sau reload đúng DB.
11. Dùng desktop/mobile, kiểm tra từng luồng thực tế: home, booking, lookup, account, menu/shop, dashboard, courts, bookings, check-in, management/POS, products, reports, CMS.

## QUY ĐỊNH BÁO CÁO KHI LÀM VIỆC

- Khi bắt đầu, trả lời ngắn gọn: **đã đọc những phần nào**, baseline build/tests, kế hoạch theo P0/P1/P2, và bắt đầu sửa ngay (không chờ phê duyệt thêm cho các ID đã được giao).
- Sau mỗi batch: liệt kê **file thật đã sửa**, mục ID đã xử lý, test **thật đã chạy** và kết quả; tiếp tục công việc batch sau.
- Khi gặp lỗi do environment/secret/provider: không nói “hoàn tất”. Ghi `BLOCKED`, hướng dẫn để chủ dự án cung cấp cần gì, làm fallback an toàn và tiếp tục mọi task khác.
- Tránh thay đổi hành vi nghiệp vụ âm thầm; chính sách chưa có bằng chứng phải cấu hình hoặc ghi `docs/DECISIONS_NEEDED.md` với lựa chọn mặc định đang áp dụng.
- Giữ thay đổi có kiểm soát, ưu tiên code clean và maintainable, không thêm ồ ạt package. Không xóa dữ liệu/logic lịch sử khi chưa có migration và test.
- **Kết quả cuối phải là source code đã chỉnh sửa**, có thể build/run và các tài liệu audit/test, không chỉ lời khuyên. Nếu giới hạn phiên làm việc, cập nhật `docs/WORKLOG.md` để phiên sau đọc và làm tiếp, không tự nhận toàn bộ hoàn tất.

**LỆNH TRIỂN KHAI:** BẮT ĐẦU NGAY TỪ BƯỚC 0. Hoàn thành P0 trước, lần lượt P1 rồi P2; sửa trực tiếp project đang mở, kiểm thử kỹ các luồng nghiệp vụ và bảo mật, cập nhật checklist 40 mục trung thực, và bàn giao các file/code đã thay đổi cùng hướng dẫn chạy/rollback. **Không chỉ đề xuất — hãy triển khai.**
