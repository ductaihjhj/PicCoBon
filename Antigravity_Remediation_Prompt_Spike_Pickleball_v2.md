# ANTIGRAVITY — REMEDIATION MASTER PROMPT V2
## Spike Pickleball Club | Hoàn thiện đúng các yêu cầu còn thiếu, KHÔNG đổi ID để báo DONE

> **Dành cho AI Agent được phép đọc và sửa trực tiếp workspace dự án.** Đây là nhiệm vụ **triển khai mã nguồn và nghiệm thu**, không phải nhiệm vụ viết kế hoạch rồi dừng. Bản đang mở cần là mã nguồn mới nhất của `hoanthanhtheoprompt.zip`, KHÔNG phải `pickleball-ui (3).zip` ban đầu.
>
> Tài liệu tham chiếu (nếu đặt trong workspace): `Antigravity_Master_Prompt_Spike_Pickleball.md` (bản yêu cầu gốc), `Spike_Pickleball_Audit.md` (audit đầu), `Spike_Pickleball_Independent_Progress_Audit.md` (audit độc lập 08/11/2026). Nếu thiếu một số tệp, **prompt này vẫn tự chứa danh mục 40 ID và điều kiện nghiệm thu**. KHÔNG giả định mọi kết luận audit cũ vẫn đúng: trước khi sửa phải đọc file hiện tại và xác minh.

---

# 1. Vai trò, mục tiêu và quy tắc tối quan trọng

Bạn là **Lead .NET Engineer / Security Engineer / Database Engineer / QA / UI Engineer**. Sửa **trực tiếp** ASP.NET Core MVC .NET 10, EF Core 10, SQL Server, Razor, CSS, vanilla JS hiện có. Không chuyển stack, không đại tu giao diện hoặc viết lại ứng dụng từ đầu. Giữ tính năng đã chạy đúng và nâng cấp theo chiều ngang có kiểm soát.

**Bài toán:** Lần triển khai trước tự báo cáo `40/40 DONE`, nhưng audit độc lập phân loại: **5 mục đạt chủ yếu ở code, 26 mục một phần, 8 mục chưa đạt, 1 mục phụ thuộc bên ngoài**. Có việc **đổi tên/đổi nghĩa ID gốc** trong tài liệu lần trước. Lần này bắt buộc **khắc phục các thiếu sót thực sự**, chứ không đổi checklist hay tạo wrapper/UI giả để “đạt”.

**Nguyên tắc không thương lượng:**
1. Đọc `AGENTS.md` và skill frontend của repo nếu có. Với nhiệm vụ này chủ dự án cho phép thay API/service/model/DB migration cần thiết để hoàn thành **đúng yêu cầu gốc**, dù `AGENTS.md` có giới hạn chung về việc sửa API/schema. **Không được xóa data, chạy script phá dữ liệu, triển khai production, thay secret production hay tự kích hoạt dịch vụ trả tiền.** Nếu chính sách hoặc lệnh đặc biệt thật sự xung đột thì ghi rõ và chọn phương án an toàn.
2. **Khóa nguyên 40 ID gốc** P0-01..P0-12, P1-01..P1-18, P2-01..P2-10. Mỗi ID phải có *đúng tên gốc, yêu cầu, lỗi tái hiện, thay đổi code, tests, kết quả*. Không tạo mục mới để thế chỗ ID khó, không gộp các ID làm mất nội dung, không lấy việc viết test thay cho migration, không lấy “ẩn nút OTP” thay cho “reset password thật”.
3. Trước khi sửa: tạo nhánh Git hoặc bản snapshot an toàn (nếu Git có), xem diff, tìm các caller/frontend phụ thuộc API, chạy baseline và lưu kết quả vào `docs/REMEDIATION_BASELINE.md`. Bỏ qua `bin/`, `obj/`, `.git`, vendor khi phân tích. **Không viết lại code đúng chỉ để tăng số file sửa.**
4. Làm theo nhóm có thể build/test, **không dừng ở lập kế hoạch**. Mỗi nhóm cập nhật `docs/REMEDIATION_WORKLOG.md` trước khi chuyển nhóm; ghi rõ commit/diff, test thật, lỗi mới còn tồn. Nếu phiên làm việc bị giới hạn, ghi checkpoint lệnh và bước tiếp theo; tuyệt đối không tự báo xong.
5. Backend/SQL là nguồn sự thật duy nhất cho quyền sở hữu, giá, giờ sân, tồn kho, trạng thái vé, payment, ưu đãi. Client chỉ gửi ý định và hiển thị phản hồi server; không tin phone, role, amount, PaidAmount, product price, booking status từ JSON request.
6. Chỉ dùng **DONE** khi điều kiện nghiệm thu của đúng ID đã qua kiểm thử thực tế được liệt kê; **PARTIAL** khi có code nhưng thiếu test/hành vi; **BLOCKED_EXTERNAL** khi thiếu credential/provider/hardware; **NOT_DONE** khi chưa triển khai. Nếu môi trường không có SQL/browser/dotnet thì không nói đã chạy test, tạo test script/hướng dẫn và giữ trạng thái PARTIAL/BLOCKED phù hợp. Phân biệt “code implemented” với “verified running”.
7. **Không thêm tính năng mới không thuộc phạm vi (giải đấu, đa chi nhánh, ví điện tử...)** khi còn P0/P1. Nếu muốn, chỉ đưa vào backlog sau nghiệm thu.

# 2. Quyết định nghiệp vụ an toàn đang áp dụng

Các quyết định này là **giả định triển khai có thể cấu hình**, không được ngầm coi là quyết định của chủ dự án khi chưa xác nhận. Ghi chỗ cần quyết định vào `docs/DECISIONS_NEEDED.md` và tiếp tục phần không phụ thuộc.

- Một CLB/cơ sở trước; giữ giao diện và luồng khách hiện tại. Khách đặt sân → `pending` → nhân viên duyệt thành `approved` → chỉ `approved` hợp lệ mới `checked_in`/`playing` → kết thúc ca `completed`. `cancelled`, `expired`, `no_show` là trạng thái riêng. Duyệt, check-in, hoàn tất và thanh toán **là các hành động khác nhau**.
- **Kết thúc ca không đồng nghĩa thu tiền**. Mặc định thu tiền mặt/chuyển khoản tại quầy sau khi nhân viên xác nhận *thực tế*; cho phép partial payment, hoàn tiền, công nợ trong ledger. VNPAY chỉ bật sau khi có cấu hình và kiểm thử callback hợp lệ.
- Giá được server phân đoạn qua các khung giờ thường/giờ vàng, dựa cùng cấu hình mở cửa và bảng giá; giá cuối cùng và breakdown lưu cùng booking. Không lấy một giá cho toàn ca nếu qua ranh giới; không dùng giá tự tính ở browser làm giá chốt.
- Không tự gán quyền VIP hoặc quyền gắn đơn vào sân bằng số điện thoại chưa được xác minh. Với khách chưa đăng nhập, dùng kênh chứng minh quyền sở hữu thích hợp (OTP thật hoặc token truy cập vé opaque, có hạn, sinh từ server, kiểm soát/rate-limit).
- Nếu provider OTP/OAuth/online payment chưa có: tắt/ẩn/đánh nhãn **chưa hỗ trợ**, không hiện toast “đã gửi”, “thanh toán thành công”; vẫn hoàn thiện luồng phục vụ tại quầy.
- Mọi đổi chính sách như thời gian giữ slot pending, giờ check-in sớm/trễ, no-show penalty, hủy/hoàn tiền, giờ mở cửa, giá, VIP %, và split bill thật phải lưu cấu hình server hoặc ghi quyết định chờ chủ dự án. Không tự tạo phí/phạt/công nợ không được giao.

# 3. Ưu tiên 0 — BLOCK RELEASE: lỗi có thể làm mất tiền / hở quyền / sai dữ liệu

## 3.1 [P0-08, P1-04, P1-05, P1-06, P1-07, P1-17] Payment và state machine (SỬA TRƯỚC)

**Bằng chứng cần xem:** `Services/BookingService.cs` vùng checkout/finish ~773–795; `Areas/Admin/Controllers/BookingsController.cs`; `wwwroot/js/management.js`; `Controllers/ApiDbController.cs`; entities/ledger/report.

- **Tách tuyệt đối** `FinishSession` (đóng ca/ghi thời gian, trạng thái) khỏi `RecordPayment` (giao dịch thu tiền có actor đủ quyền). Xóa ngữ nghĩa cũ kiểu `amount=0` => tự thu toàn bộ số còn thiếu. Không tạo payment “completed” chỉ vì nhấn Hoàn tất ca. Không cho client tùy ý gửi `PaidAmount` hoặc gán `PaymentStatus`.
- `RecordPayment`: amount > 0, currency/precision chuẩn, payment method (cash/bank transfer), timestamp thực thu, staff ID, booking/order ID, unique idempotency key, reference nếu chuyển khoản, audit log. Tổng PaidAmount được tính từ **ledger giao dịch thành công trừ refunds**, không sửa trực tiếp bằng UI. Chặn overpayment hoặc giải thích chính sách nhận thừa/thối tiền.
- Refund/reversal phải là giao dịch đối ứng, không xóa lịch sử. Báo cáo theo **ngày giao dịch thanh toán** (Asia/Ho_Chi_Minh), phân biệt doanh thu thực thu, doanh thu đặt trước, chưa thu, hoàn tiền, doanh thu Shop độc lập; không group sai theo booking date.
- API xác thực, phân quyền, CSRF, request validation, retries idempotent. Chỉ cho trạng thái hợp lệ; `pending` không được check-in, vé đã hủy/hết hạn không được check-in, vé chưa thu vẫn có thể kết thúc ca mà không tự nhận tiền.
- Viết test: finish unpaid → PaidAmount=0 và revenue=0; manual payment 100k → ledger +100k; retry same idempotency → 1 giao dịch; partial payments, refund, lặp request, report day=ngày tiền vào; unauthorized payment 403; transition bất hợp lệ 409/422.

## 3.2 [P0-08, P0-10, P0-11, P1-10, P1-11, P1-15] SQL concurrency / shop / court locks

**Bằng chứng:** `Controllers/ApiDbController.cs` vùng create order ~893–1120, tạo khóa sân ~683–730; `Services/BookingService.cs`; EF context.

- Bao **cùng một chiến lược atomic SQL Server** cho: customer booking, walk-in, approve (nếu chiếm slot), maintenance court lock, extend/move court, order/stock decrement, cancel/refund/restock; không được `check then insert` ngoài transaction. Booking vs booking, booking vs lock, extend vs booking phải chỉ có 1 thắng tại slot trùng; thất bại trả 409, UI refresh và không tuyên bố thành công.
- Với Shop: transaction xuyên suốt validate → atomic stock decrement (`UPDATE ... WHERE Quantity >= @qty` hoặc concurrency token tương đương) → order + items → commit. Số lượng 0/âm, giá giả trên client, sản phẩm thiếu/hết hàng bị từ chối. Request retry phải idempotent; rollback stock/order khi bất kỳ bước fail; tránh order một nửa, tồn âm. Hoàn/hủy phải theo chính sách và ghi inventory movements.
- **Không coi EF InMemory test là bằng chứng chống race SQL Server.** Thêm integration test thực trên SQL Server test database với Task.WhenAll/parallel HTTP/EF contexts riêng. Test cuối kho 1 sản phẩm, 2 người mua → tối đa 1 đơn; 2 booking đè giờ → tối đa 1; booking vs lock, extend vs booking → tối đa 1 thành công. Nếu chưa có SQL, mark BLOCKED test, giữ implementation status PARTIAL.

## 3.3 [P0-02, P0-04, P0-05, P0-06, P0-07, P0-09, P0-12, P1-12, P1-14, P1-17] Security / owner / CSRF / XSS

- Áp anti-forgery **nhất quán** với cookie-auth cho mọi API ghi dữ liệu (đặc biệt JSON POST/PUT/PATCH/DELETE) và form; inject token vào trang và fetch header, test cross-site POST trả 400/403 và request hợp lệ thành công. SameSite không thay thế CSRF.
- `GET /api/db/state` không rò PII/mã vé/trạng thái chi tiết không cần công khai; endpoint public chỉ trả slot busy ẩn danh theo time window, sân và sản phẩm public. `POST /api/db/sync` không chấp nhận snapshot/tuỳ ý CMS state; chuyển thành các endpoint resource/DTO riêng có quyền, audit, concurrency. Cập nhật toàn bộ frontend callers trước khi ngừng endpoint cũ.
- Tra cứu/hủy/vào đơn Shop theo booking phải kiểm tra chủ sở hữu bằng claims/customer ID hoặc token/OTP có xác thực thật. **SĐT tự nhập trùng SĐT booking KHÔNG đủ chứng minh quyền.** Gắn Shop vào booking chỉ khi owner hợp lệ và trong thời gian nghiệp vụ cho phép; không để attach nhầm từ booking khác/ngày khác.
- Membership discount không dựa trên phone tự khai; mã/điều kiện ưu đãi được server xác minh qua principal/verified guest flow. Không cho khách gán tier, role, total, checkout status.
- `QrTokenService` không fallback sang hardcoded signing key ở Production: fail-fast khi không cấu hình secret; có quy trình tạo/rotate key, không commit key thật. Cookie auth Secure HTTPS (`SecurePolicy=Always` ở production), HttpOnly, SameSite phù hợp, session invalidate/revoke khi cần. Xóa password mặc định `123456` ở `AccountsController.cs`, dùng invite/reset hợp lệ hoặc mật khẩu mạnh bắt buộc kèm đổi lần đầu.
- Thay `@Html.Raw(p.Icon)`/HTML từ CMS hoặc sản phẩm bằng whitelist đã kiểm soát và HTML encoding. Audit `innerHTML` và API response, thêm test payload XSS (HTML/script, attribute injection). Không sửa bằng cách loại bỏ toàn bộ icon/giao diện đẹp nếu có whitelist an toàn.
- Đảm bảo GET nhạy cảm và mutating actions có [Authorize] + đúng role + owner checks; phản hồi 401/403/404 hợp lý, không lộ chi tiết ngoài quyền. Thêm rate limit login, lookup, checkin, reset, optional audit anomalies.

# 4. Ưu tiên 1 — Hoàn tất MVP thực sự cho 1 CLB

## 4.1 [P1-01, P1-07] QR chuẩn, check-in bằng camera hoặc ảnh

- Trong `wwwroot/js/shared.js`, loại bỏ `renderQrCodeSvg()` tự vẽ ma trận giả; tạo QR Code **theo chuẩn** bằng thư viện phổ biến, hoặc generate PNG/SVG server-side bằng QRCoder/equivalent, chứa **token đã ký/opaque** (không chứa PII). Bắt buộc test giải mã ngược ra chính xác token nguồn bằng **decoder độc lập**, không chỉ kiểm tra `SVG` có ô vuông.
- `checkin.js`: hỗ trợ camera khi trình duyệt cho phép, giải mã ảnh upload bằng decoder thực, fallback nhập code với xác minh server; permission denied, invalid QR, expired, tampered, replay, pending booking, ngoài window đều có thông báo đúng. Server check-in atomic/idempotent; không check-in vé pending, không tự check-in do timer.
- Token ký: TTL/booking binding, secret quản lý ngoài source, chống replay; quy định cửa sổ check-in cấu hình. Test iOS/Android hoặc browser tương đương **nếu có thiết bị/browser thật**, nếu không ghi rõ chưa nghiệm thu camera.

## 4.2 [P1-08, P1-09, P1-16, P1-18, P2-05] Pricing + CMS + member consistency

- Có **một nguồn cấu hình server** cho giờ mở/đóng, ngày hoạt động, peak bands, phụ thu peak, giá theo sân, chính sách khuyến mãi/VIP và timezone `Asia/Ho_Chi_Minh`. Xóa hardcode lệch giữa `BookingService.cs` (05:00–23:30), CMS `shared.js` (06:00–22:00), `PricingService.cs` (phụ thu 30.000) và cấu hình `peakSurcharge`. Nếu chưa có quyết định giờ chính thức, giữ giá trị hiện hành có nguồn gốc và ghi `DECISIONS_NEEDED`, không tự chốt một số giờ bằng đoán.
- `GET quote`/equivalent trả breakdown theo đoạn, tổng, phí, ưu đãi đã xác thực, thời điểm hiệu lực và phiên bản giá. Frontend đặt sân và POS lấy quote của server, create re-validate và lưu snapshot giá chốt; quote stale trả 409/refresh. Không cho đặt giờ trong quá khứ, ngoài giờ hoạt động, xung đột, thời lượng không hợp lệ.
- Sửa selector `data-cms="openHours"` vs `[data-cms="hours"]`, đảm bảo UI thể hiện chính xác giờ server. Với VIP/split bill: ưu đãi từ verified member; nếu chưa xử lý split payments thật, nhãn rõ “ước tính chia đều”, không coi là đã thanh toán.
- Test 16:30–18:30 cắt 17:00, open/close, quá khứ, weekend, ngày đặc biệt nếu có config, thay config CMS, 2 users same price; UI quote = persisted total = báo cáo hóa đơn.

## 4.3 [P1-02, P1-03, P1-04, P1-14, P1-15] Account recovery / Shop / payment providers

- `Views/Account/Login.cshtml` hiện có chức năng quên mật khẩu **hiển thị toast “Đã gửi OTP” nhưng không gửi**. Phải triển khai flow reset token/OTP thật với provider hợp lệ, TTL, single use, rate-limit, anti-enumeration, audit, hoặc khi chưa được cấp provider phải **gỡ hoàn toàn tuyên bố đã gửi** và chỉ hướng dẫn liên hệ quản trị. **P1-02 vẫn BLOCKED_EXTERNAL/PARTIAL, không DONE nếu không có flow thực kiểm thử.**
- Google/Zalo/Apple OAuth: chỉ bật nút nào có provider+credentials hoạt động; nếu không thì ẩn hoặc ghi “chưa hỗ trợ” rõ ràng. Không giả lập đăng nhập; P1-03 theo tiêu chí gốc có thể **DONE khi ẩn nút chưa triển khai**.
- VNPAY: chỉ triển khai/bật khi có merchant key/test credential; signature/IPN verification, exact amount/currency, duplicate callbacks/idempotency, refund policy. Nếu thiếu, **tắt online payment**, hoàn thiện tiền mặt/chuyển khoản tại quầy có xác nhận người thu; không tự đánh dấu paid.
- Shop standalone phải có đường **thu tiền + xác nhận + đối soát** độc lập với booking, cùng invoice/ledger. Order gắn booking chỉ khi owner và window hợp lệ; stock decrement/return/cancel/refund theo transaction; không cần ép khách mua đồ phải có booking.

## 4.4 [P1-07, P1-11, P1-12, P2-04] Status reconciliation / realtime

- Xây state machine tập trung, `expired/no_show` rõ với thời gian nghiệp vụ và logging; background worker/job idempotent thay vì GET `/state` có side effect. Không tự chuyển approved thành playing, không cộng tiền khi hết giờ.
- Cập nhật client nhiều máy bằng SignalR hoặc polling **versioned/delta** có ETag/version, chỉ phạm vi cần, không tải toàn bộ DB mỗi 15s; không ghi đè thao tác đang xử lý, UI xử lý 409 và refresh.
- Khóa sân chỉ hiện đúng interval, xem trạng thái chính xác theo ngày/giờ, atomic concurrency khi bảo trì.

# 5. Ưu tiên 2 — Database, vận hành, giao diện và tài liệu

## 5.1 [P2-01, P2-02] Migration dữ liệu an toàn

- Chấm dứt dựa vào `Services/DatabaseSchemaUpdater.cs` chạy các `ALTER TABLE` tùy tiện lúc startup và nuốt lỗi. Dùng **EF Core migrations versioned** + quy trình triển khai schema riêng (không tự migrate Production khi boot). Thêm chuyển dữ liệu cũ có kiểm tra, chỉ sau khi backup. Rà các cột string date/time và money int: chuyển sang SQL `date/time/datetime2/datetimeoffset` và `decimal(18,2)` ở chỗ phù hợp, concurrency token rowversion, indexes/unique keys, FK/constraints, IdempotencyKey.
- Nếu đổi schema lớn làm hỏng dữ liệu cũ, làm các bước expand → backfill/verify → switch → contract, không `DROP`/truncate. Tạo migration tests/dry-run trên DB test và rollback/recovery plan. Tách `Database_PickleballClub.sql` có `DROP DATABASE` ra thư mục demo có cảnh báo mạnh, tuyệt đối không đóng gói cho production hoặc gọi từ script chạy mặc định.
- Nếu thiếu SQL Server trong môi trường agent: tạo migration và script kiểm tra hợp lệ nhưng **không tự đánh dấu verified**, ghi kết quả test còn BLOCKED.

## 5.2 [P2-03, P2-07, P2-10] README, nguồn dữ liệu hiển thị, package

- Viết lại README thực trạng .NET 10, setup SQL/migration, dev-only seed, cấu hình user-secrets/env, build/test/run/health/backup, workflow Admin/Staff/Customer, payment/QR, giới hạn credentials. Xóa demo password và mô tả hash sai. Các claim USAPA/"1.250+ người chơi"/đánh giá giả nếu không có chứng cứ thì bỏ hoặc ghi rõ dữ liệu minh họa.
- Clean repo ZIP/source: không đưa `bin/`, `obj/`, logs/temp, secrets/caches vào git/ZIP, cập nhật `.gitignore`, chỉ xóa file sinh ra có thể tái tạo và rõ nguồn; **không xóa tài sản vendor thư viện còn chạy**. Có hướng dẫn tạo ZIP release sạch.

## 5.3 [P2-04, P2-06, P2-08, P2-09] Quality gate + UI thực

- Tăng unit test cùng integration HTTP tests (auth/CSRF/IDOR/DTO errors), SQL Server race/inventory test, QR encode/decode bằng decoder độc lập, payment ledger/report, browser E2E journey. EF InMemory KHÔNG đủ làm SQL race test. Viết health endpoint và logging/audit không tiết lộ PII; backup + restore drill hướng dẫn và bằng chứng nếu có DB test.
- Mở trình duyệt thật kiểm các độ rộng **375, 768, 1024, 1440 px**: Home, login/register, booking, lookup, Shop, dashboard, courts, bookings, checkin, management/POS, products, reports, CMS. Chụp màn hình mỗi nhóm trang + kiểm text overlap, horizontal overflow, fixed header, modal/z-index, keyboard tab, accessible labels, focus, loading/empty/error, touch target. **Nếu không có browser, đừng tự nhận “responsive pass”**; ghi TODO test.
- `localStorage` chỉ chứa draft/filter/cart không nhạy cảm; không chứa PII, token hay giá chốt, xử lý offline không tự nhận đặt sân/thanh toán thành công.

# 6. CHECKLIST 40 ID — KHÓA NGUYÊN NHÃN GỐC VÀ TRẠNG THÁI BASELINE

> Những mục đánh giá đã ĐẠT chủ yếu ở source (P0-01, P0-03, P0-04, P1-13, P2-06) **chỉ cần xác nhận bằng test, sửa tồn dư nếu có**, không phá và làm lại vô ích. Các trạng thái sau là **baseline từ audit độc lập, không phải chứng nhận mới**.

| ID | Yêu cầu gốc, không được đổi | Baseline | Điều kiện để DONE |
|---|---|---|---|
| P0-01 | Không dùng static account global | SOURCE_OK | Không cross-session; không fallback account static, multi-browser test |
| P0-02 | Chống cookie giả, cookie an toàn | PARTIAL | Signed cookie, HTTPS secure, revoke, forged cookie test |
| P0-03 | Không đăng ký Admin công khai | SOURCE_OK | Guest không tạo/cấp Admin, Admin invite đúng quyền |
| P0-04 | Phân quyền Admin controllers | SOURCE_OK | 401/403 cho mọi route/JSON action nhạy cảm, role test |
| P0-05 | Loại bỏ API sync snapshot, thay API theo action | PARTIAL | Không còn arbitrary state write/sync; mọi caller chuyển endpoint có quyền |
| P0-06 | Không lộ PII trong public API | PARTIAL | Không có PII/booking code/public finance; limited scope/window |
| P0-07 | Chống IDOR xem/hủy đơn | PARTIAL | A không tra cứu/hủy vé B bằng mã/phone tự nhập |
| P0-08 | Booking/extend/lock concurrency | PARTIAL | SQL parallel overlapping slots chỉ một commit và 409 đúng |
| P0-09 | Password hash, secrets, demo credentials | PARTIAL | Hash/rehash/reset safe; no default password/key; prod secret fail-fast |
| P0-10 | Giá, stock, order server-authoritative | PARTIAL | Giá server, stock atomic, retry safe, invalid client inputs rejected |
| P0-11 | Không overwrite đa thiết bị, 409 concurrency | PARTIAL | No client snapshot overwrite; rowversion/409; two-client tests |
| P0-12 | Chống XSS toàn ứng dụng | PARTIAL | Không raw HTML chưa kiểm soát; XSS tests, no executed payload |
| P1-01 | QR thật và check-in server | NOT_DONE | QR decoder độc lập đọc đúng token, scanner/checkin thật, replay denied |
| P1-02 | Quên mật khẩu thật | NOT_DONE | OTP/token được gửi/thử thật; thiếu provider = disabled + BLOCKED |
| P1-03 | Google/Zalo/Apple OAuth thật hoặc ẩn | EXTERNAL | OAuth thật có test HOẶC ẩn hoàn toàn các nút chưa chạy |
| P1-04 | Payment thật/VNPAY callback hoặc tại quầy có xác nhận | PARTIAL | Nhân viên xác nhận thu tiền, ledger idempotent; online disabled nếu thiếu |
| P1-05 | PaidAmount thực thu | PARTIAL | Finish không tự mark paid; sum thành công - refund |
| P1-06 | Báo cáo từ payment ledger | PARTIAL | Sum theo transaction date, partial/refund/shop reconciled |
| P1-07 | State machine check-in/no-show | PARTIAL | Approved-only, valid window, expired/no-show, transitions test |
| P1-08 | Giá segment đúng, server quote | PARTIAL | Khung giờ chia đoạn, CMS config server, quote = persisted |
| P1-09 | Chặn giờ quá khứ/ngoài cửa | NOT_DONE | Past/outside-hours denied by server và UI |
| P1-10 | POS extend không trùng lịch | PARTIAL | Extend/move atomic SQL + concurrency tests |
| P1-11 | Court maintenance conflict & time-bound display | PARTIAL | Lock atomic vs booking + interval display correct |
| P1-12 | Khách chỉ thấy/hủy vé mình | PARTIAL | Auth owner/verified guest proof; IDOR tested |
| P1-13 | Chỉ báo thành công sau server commit | SOURCE_OK | Network failure/409 no false success; E2E evidence |
| P1-14 | Order ownership/FK/không gộp nhầm | PARTIAL | FK + owner proof + correct session/date; independent shop works |
| P1-15 | Inventory / reserve / refund / oversell | PARTIAL | Atomic stock/reserve, cancellation/restock, ledger, SQL race pass |
| P1-16 | VIP và split bill thực | PARTIAL | Verified VIP; split allocation real OR label estimate only |
| P1-17 | CSRF + validation + status transitions | PARTIAL | Cookie API CSRF negative test, DTO validation, valid state machine |
| P1-18 | Member tier theo payment thật | PARTIAL | Verified identity, tier based on net paid ledger, no client override |
| P2-01 | Cột date/time/decimal/rowversion + migration | NOT_DONE | Versioned EF migrations/backfill/rowversion, test DB upgrade |
| P2-02 | SQL demo safe, prod migration | NOT_DONE | No destructive scripts in deploy, no silent startup ALTER |
| P2-03 | README chính xác | NOT_DONE | Actual run instructions, no demo credentials, no false claims |
| P2-04 | Near-realtime đa thiết bị | PARTIAL | SignalR/versioned polling, scoped updates, multi-client test |
| P2-05 | CMS data chính xác, bao gồm pricing/hours | PARTIAL | Server/UI agree after CMS change, openHours selector corrected |
| P2-06 | Không localStorage lưu DB/PII | SOURCE_OK | No DB/PII/tokens persisted in browser storage, test reload |
| P2-07 | Không marketing claim giả | NOT_DONE | Unverified USAPA/number claims removed or marked demo |
| P2-08 | Tests, logs, health, backup | PARTIAL | SQL/HTTP/E2E + health + backup/restore evidence or BLOCKED |
| P2-09 | Responsive/accessibility/DOM safety | PARTIAL | Browser screenshots at 4 widths, keyboard + XSS/layout checks |
| P2-10 | Clean source/build artifacts | NOT_DONE | No bin/obj/temp/secrets in release ZIP, .gitignore verified |

# 7. Test matrix bắt buộc — bằng chứng không thể thay bằng câu “đã test”

Tạo `docs/TEST_EVIDENCE.md` với **lệnh đã chạy, thời gian, môi trường, test case, expected/actual, PASS/FAIL/NOT_RUN, ảnh/log path**; không sửa log cho đẹp. Test tốt nhất là tự động hóa và chạy lặp lại.

**A. Security / multi-user**
- Hai browser login 2 account khác nhau; guest không truy cập Admin; Staff không tạo Admin; user A không đọc/hủy booking hoặc gắn order vào booking B; giả cookie không cấp được quyền; CSRF POST không token bị chặn còn request hợp lệ được chấp nhận.
- Payload tên sản phẩm, CMS icon HTML `<img onerror=...>` và `<script>` hiển thị dạng text hoặc icon whitelist, không chạy JS; public API không trả phone/email/code/payment.

**B. Booking / SQL concurrency**
- Hai yêu cầu booking cùng sân cùng slot qua SQL Server: một thắng, một 409; booking vs maintenance, extend vs booking, change court; refresh 2 thiết bị không overwrite.
- Past time, outside configured hours, overlap, cancel/no-show, stale quote; 16:30–18:30 cắt khung peak; frontend quote equals persisted price.

**C. Shop / money**
- Hai order cùng mua món cuối kho: tổng bán <= tồn; toàn bộ transaction rollback khi lỗi; retry same IdempotencyKey không nhân đôi.
- Hoàn tất ca unpaid: không ledger credit; cash/transfer staff confirm: có transaction reference; retry không duplicate; partial payments/refunds và report theo ngày tiền vào; shop standalone thanh toán/hoàn tiền.

**D. QR / camera**
- Mã QR sinh ra decode bằng thư viện/thiết bị **khác implementation tạo**, ra chính xác token; ảnh upload QR và camera thành công khi hỗ trợ; sửa 1 bit token, hết hạn, replay, booking pending và wrong window bị từ chối.

**E. UX + DB release**
- `dotnet restore`, `dotnet build`, `dotnet test`; node syntax; SQL migrations apply lên DB copy có dữ liệu giả cũ; backup/restore drill; screenshot 375/768/1024/1440; offline/500/409 không hiện “thành công”.

# 8. Trình tự thực thi không được bỏ qua

**Bước 0 — Baseline + mapping**
1. Xác định đúng workspace là bản `hoanthanhtheoprompt`; đọc `AGENTS.md`, 3 báo cáo nếu có, controllers/services/views/js, migration/test hiện có. Tạo branch hoặc bảo toàn snapshot; ghi baseline build/test trước khi sửa.
2. Tạo `docs/REMEDIATION_MATRIX.md` 40 hàng **giữ ID và tên cố định**, thêm các cột `Baseline`, `Current evidence`, `Files changed`, `Test`, `Status`, `Risk`, `Blocker`.
3. Xác nhận các lỗi audit còn tồn tại trên source, điều chỉnh line number theo bản hiện tại. Bắt đầu sửa ngay, không chờ phê duyệt từng chi tiết kỹ thuật trong phạm vi này.

**Bước 1 — Release blockers:** Payment/ledger; SQL stock+booking+lock concurrency; CSRF/IDOR/authorization; secret/default passwords/XSS. Chạy unit/integration sau từng batch.

**Bước 2 — MVP:** QR chuẩn và scanner thật; approved-only state machine/no-show; server pricing+CMS; standalone shop settle; reset/OAuth/VNPAY xử lý trung thực theo credential.

**Bước 3 — DB + Quality:** Migration production-safe; near realtime; UI responsive/accessibility; README; tests HTTP/SQL/E2E; backup/health; clean artifacts.

**Bước 4 — Bàn giao:** `docs/REMEDIATION_MATRIX.md` 40 mục, `docs/REMEDIATION_WORKLOG.md`, `docs/TEST_EVIDENCE.md`, `docs/DECISIONS_NEEDED.md`, `docs/DEPLOY_AND_ROLLBACK.md`, sửa `docs/FINAL_AUDIT_RESULTS.md` phản ánh tình trạng thật; liệt kê toàn bộ file thay đổi, migrations, lệnh test, bộ dữ liệu thử, lỗi/giới hạn chưa giải quyết. Không còn tuyên bố “40/40 DONE” nếu chưa qua gates.

# 9. Định nghĩa DONE và cách phản hồi cho chủ dự án

- DONE = có code, được nối từ UI → API → service → DB, bảo mật đúng, không mock, test phù hợp thực sự PASS; với SQL concurrency/QR/camera/payment, unit tests một mình không đủ.
- PARTIAL = đã viết code hoặc test unit, nhưng chưa xác minh SQL/browser/provider hoặc còn edge case.
- BLOCKED_EXTERNAL = cần key/provider/SQL/hardware/permission ngoài quyền kiểm soát. Phải ghi chính xác cần gì, nơi cấu hình, bước kiểm tra khi nhận được. Thay UI thật bằng thông báo không hỗ trợ, không làm giả.
- NOT_DONE = chưa thực hiện hoặc không đạt hành vi.
- Sau mỗi batch, báo cáo ngắn gồm: **ID gốc**, file thay đổi thật, lệnh/test thật, PASS/FAIL/NOT_RUN, rủi ro còn lại, batch tiếp theo. Nếu lỗi nền không sửa được, không tự hợp thức hóa bằng cách thay test/đổi yêu cầu.

## LỆNH BẮT ĐẦU

**Hãy thực thi ngay trên mã nguồn phiên bản mới hiện mở. Đừng chỉ phân tích, đừng đổi nghĩa 40 ID và đừng tự nhận hoàn thành. Ưu tiên payment, SQL concurrency, CSRF/ownership và QR thật; sau đó hoàn tất tất cả phần còn sót theo 40 ID gốc. Chỉ bàn giao sau khi build/test tối đa có thể và cung cấp bằng chứng trung thực. Nếu bị giới hạn môi trường/phiên, cập nhật checkpoint rồi tiếp tục các phần khác còn thực hiện được.**
