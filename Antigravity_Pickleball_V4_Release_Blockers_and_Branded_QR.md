# ANTIGRAVITY MASTER PROMPT V4 — RELEASE BLOCKERS, 9 FINDINGS & PREMIUM QR

> **Áp dụng cho bản nguồn hiện tại của Spike Pickleball Club (`done.zip`).** Đây là lệnh THỰC THI và NGHIỆM THU chứ không phải yêu cầu viết báo cáo hoặc thiết kế mockup. Đọc toàn bộ file trước khi sửa.
>
> **Nguồn đối chiếu bắt buộc:** `Spike_Pickleball_Audit_V3_done.md`, `Antigravity_Remediation_Prompt_Spike_Pickleball_v2.md` (nếu có), `docs/REMEDIATION_MATRIX.md`, `AGENTS.md`, toàn bộ code và test đang có. Nếu không thấy tài liệu, vẫn thực hiện theo đặc tả chi tiết trong prompt này; không tuyên bố đã đọc file không tồn tại.

## I. Vai trò và phạm vi

Bạn đóng vai **Principal ASP.NET Core Engineer + Application Security Engineer + SQL Server/EF Core Engineer + QA Automation Engineer + Senior Product Designer**. Làm việc trực tiếp trong workspace hiện hành, giữ nguyên stack **ASP.NET Core MVC / .NET 10, EF Core, SQL Server, JavaScript**. Không viết lại từ đầu. Không phá các API, UX, dữ liệu hoặc nghiệp vụ đã hoạt động đúng. Không đổi các ID của checklist cũ; lập thêm checklist riêng `V4-A1`…`V4-A4`, `V4-B1`…`V4-B5`, `V4-QR` và liên kết về các mục P0–P2 gốc, không thay nội dung checklist gốc.

Các thay đổi API/schema cần thiết để khắc phục lỗi **được chủ dự án cho phép trong phạm vi V4** (đây là ngoại lệ có giới hạn so với hướng dẫn thận trọng trong `AGENTS.md`), nhưng phải tạo EF migration an toàn, có kế hoạch rollback, không DROP/RESET dữ liệu thật, không làm thay đổi hành vi kinh doanh chưa được chỉ định. Nếu phải chọn chính sách nghiệp vụ chưa rõ, dùng các giá trị mặc định dưới đây, ghi rõ vào `docs/V4_DECISIONS.md` và để cấu hình được; chỉ chặn khi có rủi ro không thể đảo ngược.

**Định nghĩa trạng thái:** `DONE_VERIFIED` = có build/tests thực chạy, chứng cứ HTTP/DB/UI phù hợp; `PARTIAL` = code đã có nhưng chưa đủ test hoặc chưa hoàn thiện flow; `BLOCKED_ENV` = thiếu môi trường; `BLOCKED_EXTERNAL` = thiếu dịch vụ/credentials ngoài; `NOT_STARTED` = chưa sửa. Không được đánh dấu DONE nếu chỉ nhìn thấy method, có screenshot giao diện, hay test dùng mock/InMemory.

**Mục tiêu phát hành:** Trước khi công khai đặt sân, **4 release blockers V4-A1…A4 phải đạt nghiệm thu**. Nếu không, phát hành phải giữ trạng thái `NO_GO` dù tất cả phần còn lại đã chạy.

## II. Release blockers — PHẢI sửa trước tất cả việc khác

### V4-A1 — Pending booking không được khóa sân vô hạn; TTL + anti-abuse + concurrency

Vị trí cần dò: `Services/BookingService.cs` (overlap, create, approve, background expiration); `Services/ClubSettingsService.cs` (`PendingHoldMinutes`); `Controllers/ApiDbController.cs` (public CreateBooking); EF models, database context, booking UI, worker.

- Mặc định **hold 30 phút** (cấu hình được, có giới hạn hợp lý), lưu thời điểm hết hạn **ở DB theo UTC** `HoldExpiresAtUtc` hoặc tương đương. Quy tắc chính xác: pending chỉ chiếm lịch khi `HoldExpiresAtUtc > nowUtc`; approved/playing chiếm theo nghiệp vụ; expired/cancelled không chiếm. Nếu slot sắp diễn ra, điều chỉnh hold để không vượt thời điểm bắt đầu ca; ghi rõ chính sách.
- Tính toán availability, tạo booking, staff approval, lock/extend và worker expiration phải dùng **cùng quy tắc** và **cùng nguồn thời gian server**. Không chỉ chờ cron mới giải phóng slot: truy vấn còn trống phải loại pending quá hạn ngay; background worker cập nhật trạng thái để lịch sử đúng. Dùng transaction/lock nhất quán để hai thao tác cạnh tranh không cùng giành slot.
- Approve sau `HoldExpiresAtUtc` phải từ chối rõ ràng; approve sát hạn hoặc tạo booking cùng slot phải không thể cả hai thành công. `pending -> approved/expired` là thay đổi atomic, audit user/time/reason.
- Bảo vệ abuse: chính sách rate limit **partition riêng cho CreateBooking**, theo IP đáng tin cậy + user identity nếu có; không tin IP từ `X-Forwarded-For` khi không cấu hình trusted proxies. Hạn mức pending/người dùng/tài khoản/số liên hệ đã xác minh, không tin số điện thoại tự khai làm danh tính duy nhất. Nếu chưa có kênh xác minh liên hệ thật, áp dụng hạn chế phù hợp kết hợp authenticated account/IP/session, audit và logging; không giả đã OTP.
- UI hiển thị “giữ chỗ đến…”/đếm ngược từ server, xử lý hết hạn và cập nhật availability, không hiển thị “đặt thành công vĩnh viễn” cho pending. Tạo booking công khai không tự động chiếm vô hạn từ một guest vô danh.
- Test: 100 requests tạo pending cạnh tranh cùng slot; pending 30 phút trước/sau hạn; worker chưa chạy nhưng availability vẫn giải phóng; approve đúng sát hạn, approve quá hạn; create vs approve race; nhiều IP độc lập, một IP spam bị giới hạn; timezone Asia/Ho_Chi_Minh/DST không bị sai UTC. Chứng minh chỉ đúng số slot khả dụng được giữ.

### V4-A2 — Server authoritative booking lifecycle; client không tự biến playing thành completed

Vị trí: `wwwroot/js/shared.js` (`reconcileBookingsByRealTime`); mọi frontend dùng booking `status`, API response, worker/`FinishBookingSessionAsync`.

- **Không sửa field `booking.status` tại frontend** dựa vào đồng hồ; xóa mọi nhánh ngầm `playing -> completed` khi quá giờ. Client chỉ tính **derived UI state** như `overdue`, `starting_soon`, `waiting_for_staff_checkout` mà không ghi DB và không dùng derived state cho quyền/đơn tiền.
- Chỉ service backend qua API đã xác thực/ủy quyền (hoặc worker theo policy có transaction + audit) được chuyển lifecycle. Giữ trạng thái công nợ/đã trả tiền độc lập với giờ kết thúc.
- Bổ sung workflow giao diện quá giờ: badge “Quá giờ — chờ kết ca”, cập nhật có kiểm soát; refresh và 304 không thay status trong localStorage. Nếu frontend dùng cache state, phân biệt server state và display flags.
- Test Node/integration: booking playing đã quá giờ + HTTP 304/refresh/2 tabs vẫn playing trên client và SQL cho tới khi finish hợp lệ; staff finish cập nhật cả client và DB; unauthorized/guest không tự finish.

### V4-A3 — Idempotency đúng actor/action/entity/payload/amount; replay an toàn

Vị trí: `Services/OrderService.cs` (create/collect/refund); `Services/BookingService.cs` (payment/refund/extend nếu có); controllers, DB schema.

- Tạo **một contract idempotency chuẩn** cho mọi mutation nguy hiểm: `(scope/operation, actor/tenant identity, target entity, canonical request hash, idempotency key)` và kết quả/ký hiệu giao dịch đã commit. Khóa unique phù hợp trên DB; không được chỉ tra theo key rồi trả lại giao dịch của đối tượng khác. Cân nhắc canonical JSON cho amount/currency/line items và xử lý normalization trước hash.
- Lần đầu: ghi intent, thực hiện transaction, lưu kết quả atomic nếu hệ thống hỗ trợ; same key + **cùng** semantic request -> replay **cùng** business result, không phát sinh payment/stock movement lần hai. Same key nhưng **khác actor, action, entity, amount hoặc payload** -> trả **HTTP 409 `IDEMPOTENCY_KEY_CONFLICT`** (không 200/success giả). Cùng một request song song không double-write; lock/unique constraint giải quyết race.
- Tuyệt đối không log raw access token, full payment secret, credential hoặc dữ liệu nhạy cảm. Có thời hạn lưu idempotency record phù hợp nghiệp vụ tài chính; chính sách retry khi transaction thất bại và lỗi timeout rõ ràng.
- Test bắt buộc: checkout/refund order A/B dùng trùng key; hai amount khác nhau trùng key; hai actor; request hợp lệ retry sau server commit nhưng client timeout; 20 requests cùng key đồng thời chỉ 1 transaction; hoàn tiền không vượt số đã thu. Check số `PaymentLedger`/`InventoryMovements` ở SQL Server.

### V4-A4 — Hủy booking gộp Pro Shop phải nhất quán order / tồn / payment

Vị trí: `Services/OrderService.cs` (merge + cancel); `Services/BookingService.cs` (cancel and stock restoration); `TbOrder`, POS items, inventory ledger, payment ledger.

- **Chọn chính sách mặc định:** Nếu linked Shop order chưa giao và chưa thanh toán: hủy booking phải **hủy linked order**, rollback reservation/stock chính xác một lần, cập nhật `TbOrder.Status`, POS totals, audit trong **một DB transaction**. Nếu món đã giao, đã thu tiền hoặc refund có tác động: **KHÔNG âm thầm restock hoặc xóa giao dịch**; yêu cầu staff xử lý với explicit return/refund/reversal workflow theo quyền, policy, chứng từ; nếu policy không cho hủy, trả lỗi có thể hành động, không cho trạng thái dở dang.
- Quy định source of truth duy nhất cho item qty/stock movement, tránh cả `PosItemsJson` và `TbOrder` cùng dẫn đến restock hai lần. Ràng buộc integrity giữa booking, linked order, ledger và tồn kho; chống double cancel và retry bằng idempotency.
- Nếu hủy booking có nhiều linked orders, transaction phải xử lý **tất cả hoặc không cái nào**; không partial update. Nếu có external payment, sử dụng saga/compensation hợp lý và trạng thái `refund_pending`, không tự ghi đã hoàn tiền khi gateway chưa xác nhận.
- Tests: order chưa giao/chưa thanh toán; đã giao; đã thu một phần/đủ; có refund; 2 linked orders; double cancel và concurrent cancel+checkout/ship; rollback khi giữa chừng lỗi; kiểm kê và báo cáo không ghi nhận đơn cancelled như bán thành công.

## III. Năm vấn đề phải sửa ngay sau release blockers

### V4-B1 — CSRF refresh contract

- Controller API refresh đang trả `{ok:true, token:...}` nhưng `wwwroot/js/shared.js` đọc `data.csrfToken`; chuẩn hóa contract (ưu tiên `token`, sửa JS + test API contract). Cập nhật token header chính xác, retry **một lần** khi 400 có nguyên nhân token hết hạn được xác định, không retry mutation không an toàn vô hạn; các response 400 validation khác phải hiện đúng lỗi, không bị nhầm CSRF.
- Test login/logout ở tab khác, token hết hạn, refresh hợp lệ, request retry và lỗi validation.

### V4-B2 — Guest retry mất access token sau khi commit

- `BookingService` idempotent replay trả existing booking nhưng `GuestAccessToken` rỗng vì DB chỉ lưu hash. **Không trả raw guest token từ hash (không thể), không lưu plain token mới ở DB và không phát token mới cho người lạ chỉ vì biết idempotency key.**
- Thiết kế recovery chắc chắn: gắn retry với guest session đã chứng thực bằng HttpOnly Secure SameSite cookie hoặc session-bound proof; bảo vệ token lúc cấp, để retry cùng session có thể lấy lại quyền vé (ví dụ rotate access token bằng flow atomically authenticated), không khiến người giữ mã booking/idempotency key chiếm vé. Nếu dùng encrypted short-lived response cache, yêu cầu threat model, encryption key trong secret manager và TTL. Với khách đã rời session: recovery thông qua xác minh kênh sở hữu hợp lệ, hoặc yêu cầu staff hỗ trợ an toàn nếu OTP chưa kết nối.
- Test commit rồi timeout rồi retry trong cùng authenticated guest session, session khác, replay từ attacker, revoke/rotate old token và tra cứu/hủy sau retry. Không bỏ sót hiển thị token cần lưu cho khách.

### V4-B3 — Enforce `MustChangePassword` server-side

- Account được cấp mật khẩu tạm **phải** qua trang đổi mật khẩu bắt buộc trước khi truy cập bất kỳ Admin/Staff API hoặc View nào ngoài allowlist tối thiểu (change password, logout, supporting assets). Dùng authorization policy/filter/middleware và cookie/session validation, không chỉ redirect một trang.
- Test dùng mật khẩu tạm gọi thẳng endpoint admin (GET/POST), cookie trước/sau đổi, API AJAX, nhiều tab; đổi xong mới mở quyền. Không xuất plaintext password qua log/response.

### V4-B4 — ETag / multi-device state consistency

- Xem lại `Controllers/ApiDbController.cs` ETag hiện tính theo field aggregate thiếu nhiều trường. Ưu tiên version tăng đơn điệu và cập nhật atomically cho mọi mutation có ảnh hưởng snapshot; nếu không thể bảo đảm mọi write path, dùng deterministic hash có canonical ordering của **toàn bộ** fields API trả. ETag phải thay khi sửa tên sân, ảnh sân, thời gian booking, chi tiết lock, sản phẩm, giờ mở cửa, giá, v.v.
- Test `If-None-Match`: không đổi -> 304, sửa bất kỳ field trả trong snapshot -> 200 + ETag mới, hai thiết bị nhận dữ liệu mới, xử lý race + cache headers đúng. Tránh N+1 và full-table rehash trên mỗi lượt truy cập nếu có thể.

### V4-B5 — Partitioned rate limit; chống abuse không khóa người dùng hợp lệ

- `Program.cs` đang dùng fixed-window limiter chung `AuthAndLookup`, thay bằng partition theo route/category + actor/IP đáng tin cậy (và account khi phù hợp). Tách login, password reset, ticket lookup, booking create, QR check-in, admin APIs; có ngưỡng/burst hợp lý và `Retry-After` với 429. Quan sát logging không lộ PII.
- Test: hai IP độc lập không chia quota; một IP spam bị chặn; authenticated user và guest phân biệt; trusted proxy behavior; không khóa toàn bộ CLB khi nhiều nhân viên chung NAT. Có endpoint-specific limits và policy cho thất bại đăng nhập.

## IV. QR V4-QR — nâng cấp vé QR đẹp, sắc nét, đáng tin cậy (không làm giả QR)

**Đã có code QR chuẩn** qua `Services/QrTokenService.cs` sử dụng QRCoder cho SVG/PNG và ZXing.Net/ImageSharp giải mã. **Giữ và tận dụng encoder/decoder hiện có**, không thay QR bằng CSS hình vuông ngẫu nhiên, AI-generated QR hay dịch vụ QR bên thứ ba làm rò token. Ưu tiên thiết kế **thẻ vé quanh QR**, không tùy biến khiến các module mất khả năng đọc.

### 1) Visual ticket (UI/UX)

- Tạo **Premium Digital Ticket** đồng bộ design system hiện hữu (`.agent/skills/frontend-design/SKILL.md`, typography, spacing, color, border radius). Bố cục mobile-first với header thương hiệu Spike Pickleball, trạng thái vé, tên sân, ngày giờ, thời lượng, mã vé dễ đọc, QR chính giữa, hướng dẫn “Đưa mã này cho nhân viên quét tại quầy”.
- QR trên nền trắng hoặc rất sáng; foreground đậm đồng nhất; **quiet zone tối thiểu 4 modules** mọi phía. Không gradient, ảnh nền, shadow, border hoặc pattern nằm TRONG modules/quiet zone. Border radius, nền gradient, icon thể thao và hình trang trí chỉ được đặt ở **khung thẻ bên ngoài QR**.
- Nếu có logo CLB hợp lệ từ asset dự án, cho phép biến thể logo nhỏ ở tâm QR **chỉ khi** đã tăng ECC thích hợp (thử Q/H tùy chiều dài token), chừa vùng nền trắng quanh logo, không che finder/timing/alignment patterns, diện tích che nhỏ (khuyến nghị tối đa khoảng 10–12% diện tích mã khi đã test) và có auto-fallback bản không logo. Mặc định chọn QR chuẩn không logo khi logo làm giảm chất lượng quét. Không dùng vòng tròn/tô tròn cả ba finder corners nếu chưa test scanner đủ loại.
- Hỗ trợ màn hình 320–430px, desktop, in A4/A6 và screenshot; mã QR không nhỏ hơn ~220 CSS px trên mobile khi có không gian, export ảnh PNG đủ nét 512–1024px và SVG nếu phù hợp. Giữ kích thước vùng QR theo module thay vì ép CSS resample gây mờ; xác định kích thước dựa mật độ payload.
- Thao tác thật: **Tải vé PNG**, **Tải QR PNG/SVG** nếu hiện trạng cho phép, **Sao chép mã vé**, **In vé**, mở fullscreen QR khi quầy cần; có trạng thái loading/error, không lộ secret trong log, không ghi mã truy cập nhạy cảm vào public analytics. Văn bản tiếng Việt tự nhiên, accessible keyboard/focus, aria-label, fallback manual code có rate limit/authz.
- Đề xuất 2 theme ở ngoài QR: `Light Premium` (nền trắng/kem, điểm nhấn brand) và `Dark Sport` (nền tối của card nhưng QR vẫn nằm trên ô trắng). Không thay design system toàn site; ưu tiên theme hiện tại.

### 2) Tối ưu kỹ thuật quét & an toàn

- QRCoder tạo canonical QR đúng ISO; ZXing.Net giải mã độc lập. Khuyến nghị dùng nội dung rút gọn có cấu trúc, **token mờ/opaque hoặc signed reference** không chứa PII. Tránh URL quá dài. Nếu dùng URL để mở vé, chỉ dùng HTTPS trên domain chính chủ và validate/check-in tại server; không công khai guest access token hoặc dùng QR làm lối bỏ qua authz. Có cách offline ở quầy: manual code / upload; quét không bắt buộc online từ điện thoại khách nếu nhân viên có mạng.
- Duy trì HMAC/expiry/revocation hiện có, check status approved, window, cancel/expired, phòng replay check-in. Không chỉ xác thực chữ ký rồi check-in bất kỳ vé nào; check quyền của nhân viên. Check-in cùng mã 2 lần phản hồi trạng thái idempotent chính xác, không tạo hai lần.
- QR code phải là dữ liệu chuẩn; SVG vector được tạo từ encoder, không viết custom giả ma trận; kiểm tra MIME, filename, encoding, XML/SVG injection, XSS, CSP và không chèn SVG không tin cậy bằng `innerHTML` nếu chưa sanitize.
- Cân bằng ECC: chuẩn M khi không logo có thể hợp lý; thử Q/H nếu có logo nhưng **H tăng độ dày/mật độ**, không khẳng định H lúc nào cũng dễ quét hơn. Chỉ chọn qua benchmark giải mã thực tế.
- Nếu thiết kế token ngắn hơn, đảm bảo entropy/chữ ký đủ mạnh, có versioning, rotation secret, khả năng vô hiệu hóa vé, backward compatibility và migration khi cần; không tùy ý đổi định dạng làm vô hiệu QR vé cũ.

### 3) Kiểm thử QR bắt buộc

- Tạo ít nhất 100 QR với token độ dài khác nhau; xuất **SVG và PNG**, đọc ngược bằng ZXing.Net, mọi trường hợp trả **đúng 100% nội dung ban đầu** trong bộ test; thử 3 kích thước ảnh, theme Light/Dark, biến thể logo bật/tắt.
- Test ảnh chụp màn hình độ sáng thấp, screenshot bị scale/resize, print monochrome, xoay 90/180°, ảnh hơi mờ hoặc bị nén; báo tỷ lệ scan riêng từng biến thể; thử quét camera điện thoại Android & iOS nếu có thiết bị. **Không claim thiết bị thật PASS nếu chỉ chạy decoder trong unit tests**.
- Camera/UI: hành trình customer mở vé → staff scan thành công → xác thực server → báo nhận sân; sai chữ ký/hết hạn/chưa duyệt/đã hủy/scan trùng phải trả thông báo cụ thể và không mutate DB sai.
- Lưu screenshots và sample QR ở `docs/qa/qr/`, không upload ảnh có token production thật; có option disable logo nếu tỷ lệ quét không đạt ngưỡng.

## V. Các mục ngoài 9 finding: giữ đúng trạng thái, không ngụy trang hoàn tất

- **OTP quên mật khẩu**: hiện phụ thuộc nhà cung cấp (SMTP/SMS/Zalo ZNS), giữ `BLOCKED_EXTERNAL` nếu chưa có credentials, không báo “đã gửi” khi chưa gửi, không tạo OTP cố định; nếu bổ sung thực, lưu hash OTP + TTL + chống brute force + không lộ sự tồn tại tài khoản.
- **VNPAY/đối soát chuyển khoản**: chỉ triển khai khi có sandbox, callback validation, signatures, reconciliation và refund policy; phân biệt thu tiền tại quầy với thanh toán online đã đối soát.
- **Packaging**: cung cấp ZIP source-only không `bin/`, `obj/`, `.git/`, `node_modules/`, production secrets; có `.gitignore`, `.env.example`, hướng dẫn backup/restore.
- Không thay đổi một nghiệp vụ khác để “sửa” các finding; nếu phát hiện bug mới, thêm vào `docs/V4_FINDINGS.md` với severity và test.

## VI. Trình tự thực hiện & artifacts bắt buộc

1. **Baseline:** đọc docs và source, chạy `dotnet --info`, `dotnet restore`, `dotnet build`, `dotnet test`, `node --check` cho các JS trước khi sửa. Ghi rõ lệnh nào chạy được, fail do đâu. Kiểm tra `git status`, sao lưu schema/data theo chính sách an toàn. Không viết “PASS” khi chưa thực thi.
2. **Reproduce:** với 9 lỗi, viết regression test **đỏ trước khi sửa** nếu có thể; nếu không thể chạy vì môi trường, vẫn thêm test đúng và ghi `BLOCKED_ENV`.
3. **Fix 4 release blockers** A1→A4 theo atomic commits/checkpoints; mỗi mục kiểm code review, DB migration/transactions và SQL Server test trước khi chuyển mục tiếp theo.
4. **Fix B1→B5**, hồi quy toàn bộ flows liên quan: guest booking, login, admin, POS, payment, shop, cancel, expiry, check-in, dashboard.
5. **QR premium**: sửa UI đúng design system, xuất file chất lượng cao; test decoder và kiểm thử camera thiết bị khi có.
6. **Verify release:** chạy test suite + SQL Server integration + HTTP tests + browser E2E (khách, staff, admin; hai browser/tài khoản; concurrency) + responsive 320/375/430/768/1024/1440px; không sử dụng EF InMemory để tuyên bố bảo đảm transaction/row locking SQL Server.
7. **Tài liệu đầu ra bắt buộc**, tạo/cập nhật đúng đường dẫn:
   - `docs/V4_IMPLEMENTATION_PLAN.md`: checklist `V4-A1`…`A4`, `V4-B1`…`B5`, `V4-QR` với status, owner, file/function, migration, links tests.
   - `docs/V4_TEST_EVIDENCE.md`: lệnh chính xác, kết quả có timestamp, test file, ảnh màn hình, request/response (đã ẩn secrets), dữ liệu SQL trước/sau, vấn đề môi trường.
   - `docs/V4_DECISIONS.md`: pending TTL 30 phút, linked Shop cancellation policy, cách guest recovery, QR ECC/logo, rate-limits; thay đổi so với mặc định phải giải thích.
   - `docs/V4_SECURITY_AND_ROLLBACK.md`: CSRF, permission matrix, abuse model, EF migration rollback, backup/restore.
   - `docs/V4_RELEASE_GATE.md`: `GO` hoặc `NO_GO` kèm lý do và từng blocker A1–A4; không tự nói production-ready nếu chưa có SQL/HTTP/real-device evidence.
   - `docs/V4_QR_DESIGN.md`: screenshots / specification Light/Dark, QR scan matrix, lý do chọn ECC, logo/no-logo fallback.
8. **Final report tiếng Việt:** bảng 10 mục `V4-A1…A4`, `V4-B1…B5`, `V4-QR`, trạng thái thật, đường dẫn thay đổi, test đã chạy/chưa chạy, rủi ro còn lại, migrations, các bước chủ dự án cần thực hiện. Giữ nguyên nội dung/checklist P0–P2 ban đầu; không tự ý sửa status thành DONE nếu không có evidence.

## VII. Tiêu chí GO/NO-GO tối thiểu

Chỉ đề xuất **GO for controlled pilot/public booking** khi A1–A4 có test phù hợp chạy trên SQL Server thật và API HTTP, không còn lỗi high severity liên quan auth/finance/inventory/booking; B1–B5 không còn lỗi làm hỏng luồng khách; QR decode và check-in đúng trong các tình huống chính; có backup/restore được thử, cấu hình secrets/HTTPS và giám sát. Nếu bất kỳ điều kiện nào thiếu, ghi **NO_GO** và nêu đúng blocker.

**BẮT ĐẦU NGAY TRONG WORKSPACE:** kiểm tra thực trạng file và tái hiện 9 lỗi, sửa theo thứ tự trên, tự chạy kiểm thử trong giới hạn môi trường, hoàn thiện báo cáo nghiệm thu. Không hỏi lại những thông tin đã nêu. Không sửa/trình bày mock UI cho xong việc. Khi phụ thuộc tài khoản ngoài, triển khai đến mức có thể và đánh dấu BLOCKED_EXTERNAL; tuyệt đối không giả vờ hoàn thành.
