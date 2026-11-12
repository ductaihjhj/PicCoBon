# MASTER PROMPT V6 — SPIKE PICKLEBALL: PRODUCT IMAGE UPLOAD & ANIMATED PRO SHOP

## Vai trò và mục tiêu

Bạn là Senior ASP.NET Core MVC Engineer, Product UI/UX Designer, Frontend Animation Engineer, Security Engineer và QA Automation Engineer. Hãy **sửa trực tiếp mã nguồn dự án hiện tại**, không dừng ở kế hoạch. Nâng cấp Pro Shop từ card dùng icon sang hình sản phẩm thực, có hệ thống quản lý ảnh, animation chọn mua cao cấp, đồng bộ với API và SQL Server.

**Giữ nguyên** .NET 10 ASP.NET Core MVC, EF Core, SQL Server, Razor, JavaScript hiện có, toàn bộ booking, POS, checkout, phân quyền, tồn kho và payment đã hoạt động. Không viết lại dự án. Đọc `AGENTS.md`, `.agent/skills/frontend-design/SKILL.md` và các skill kiểm thử thực sự có trong workspace; tôn trọng design system, hạn chế dependency.

## Hiện trạng nguồn cần kiểm tra trước khi sửa

- `Models/TbProduct.cs` **đã có** `string? Image` và `string? Icon`; `Models/PickleballContext.cs` cấu hình `Image` dài tối đa 500 ký tự. **Tái sử dụng Image, không tạo thêm ImageUrl hoặc migration trùng lặp.** Chỉ migration nếu sau rà soát phát hiện nhu cầu thực sự, không phá dữ liệu.
- `Areas/Admin/Controllers/ProductsController.cs`: POST Create/Edit currently bind `Image` là chuỗi; `Areas/Admin/Views/Products/Create.cshtml`, `Edit.cshtml` mới cho nhập Icon, chưa có `multipart/form-data` và `IFormFile` upload.
- `Controllers/ApiDbController.cs`: projection `menu` hiện trả `id/name/price/stock/type/icon`, **thiếu** ảnh.
- `wwwroot/js/menu.js`: `renderMenu()` dùng `<div class="menu-item-icon">${sanitizeIconHtml(item.icon)}</div>`, các thao tác `renderMenu()` sau khi add/remove liên tục thay `grid.innerHTML`, có thể làm animation mất hoặc giật.
- `wwwroot/css/styles.css`: có `.menu-item-*`, `.shop-cart-*`, `.pos-menu-item-icon`.
- `wwwroot/js/management.js`: POS cũng hiển thị icon. Rà soát và đồng bộ ảnh tại các nơi cần thiết nhưng không phá nghiệp vụ.
- Trên frontend, `MENU` / `MENU_ITEMS` và các API là nguồn hiển thị; kiểm tra loading/error/empty; không dùng mock làm dữ liệu thực khi API lỗi.

## P0 — Quản lý ảnh sản phẩm thật và bảo mật

1. Ở Admin Products Create/Edit, bổ sung khu vực **Ảnh sản phẩm** chuyên nghiệp: drag & drop, nhấn chọn file, preview trước upload, mô tả định dạng và giới hạn, thay ảnh, gỡ ảnh, khôi phục khi chưa lưu, xem ảnh đã lưu khi Edit. Chỉ Admin mới có quyền upload/sửa/xóa. Sử dụng `enctype="multipart/form-data"`, `IFormFile` thông qua một ViewModel riêng và anti-forgery token; không tin `Image` path do client tự nhập. Hiển thị lỗi validation cạnh input, không làm mất nội dung form khi upload lỗi.
2. Chấp nhận PNG, JPG/JPEG, WebP (bao gồm PNG có alpha); giới hạn dung lượng mặc định 5 MB và giới hạn kích thước pixel hợp lý. Kiểm tra content-type **và magic bytes/giải mã ảnh thật**; không cho HTML, SVG không kiểm soát, script, file giả đuôi ảnh, đường dẫn traversal hay tên file do người dùng đặt làm tên lưu. Giải pháp xử lý ảnh phải phù hợp với dependencies hiện có; nếu thêm thư viện thì giải thích và kiểm tra license.
3. Lưu vào `wwwroot/uploads/products/` theo tên GUID không đoán trước, không đặt file ngoài thư mục cho phép. Lưu **đường dẫn tương đối đã chuẩn hóa** vào `TbProduct.Image` (ví dụ `/uploads/products/<guid>.webp`), không lưu blob vào SQL. Khi upload thành công nhưng DB save thất bại, dọn file mới; khi Edit thành công mới dọn file cũ nếu file đó không còn bản ghi tham chiếu. Không xóa nhầm file dùng chung. Giữ nguyên ảnh cũ khi form Edit không chọn file mới. Xóa ảnh là hành động tường minh, không phải tự động khi `IFormFile` rỗng.
4. Nếu có image processing an toàn, tạo kích thước hợp lý (gợi ý <= 1200px) và WebP hoặc giữ PNG alpha. Ưu tiên chất lượng nền trong suốt, không ép sang JPEG làm mất transparency. Không yêu cầu xử lý ảnh bằng AI trong request upload.
5. Đảm bảo tương thích sản phẩm hiện có, không sửa giá/tồn kho/chính sách bán. Nếu Admin chuyển sản phẩm `IsActive=false`, ảnh không bị xóa theo cách làm hỏng dữ liệu lịch sử. Thêm cơ chế upload/thay/xóa ảnh cả trên trang Admin listing nếu thiết kế phù hợp; tối thiểu có thumbnail chính xác trong listing/Create/Edit.

## P1 — API, SQL và hiển thị ảnh thay icon

6. Sửa `ApiDbController` mapping trạng thái/menu từ `TbProduct.Image` ra `imageUrl` (hoặc một tên JSON thống nhất), đồng thời giữ `icon` để tương thích nếu nơi khác còn dùng. Trả URL an toàn và thống nhất ở mọi endpoint liên quan Pro Shop/POS. Nếu ETag/state version dùng cache, thay ảnh sản phẩm phải làm cache hết hạn để nhiều thiết bị thấy ảnh mới.
7. Sửa `wwwroot/js/menu.js` và giao diện Pro Shop `/Product` để các card sử dụng `<img>` với `src` ảnh từ server, `alt` lấy từ tên sản phẩm (escape/encode đúng), `loading="lazy"`, `decoding="async"`, tỷ lệ 1:1, `object-fit: contain`, nền sáng nhẹ tôn vật thể, padding đồng đều. Không crop mất cạnh vợt/chai nước/túi bóng. Với sản phẩm không có ảnh hoặc URL ảnh hỏng, hiển thị fallback image/placeholder đẹp **không dùng icon lớn như hình sản phẩm chính**. Icon nhỏ chỉ giữ cho hành động hoặc nhãn.
8. Hiển thị ảnh thu nhỏ trong giỏ hàng và POS khi có không gian; nếu giao diện POS quá chật, ưu tiên trải nghiệm và giữ thao tác nhanh, không bắt buộc ảnh lớn. Đồng bộ tên/giá/tồn kho/trạng thái với API. Giảm số lượng ảnh tải bằng responsive sizes/lazy load; tránh layout shift bằng aspect-ratio/width/height.
9. Dùng bộ ảnh packshot PNG trong suốt đã cung cấp trong workspace làm ảnh mẫu **chỉ khi đối chiếu đúng loại mặt hàng**. Không tự gán ảnh vợt cho SKU đồ uống hoặc ảnh generative vào sản phẩm thương hiệu thật khi không trùng sản phẩm. Không tự thay thông tin sản phẩm/giá/stock. Không tự sửa dữ liệu production để có demo. Có thể tạo seed dữ liệu thử nghiệm trong môi trường dev riêng, có thể tắt đi.

## P1 — Thiết kế card Pro Shop và microinteractions

10. Dùng design system Spike Pickleball hiện tại (màu nền/navy, accent neon/cam theo token thực, typography hiện có). Thiết kế card mới với **image stage rõ, tên, nhóm, giá, trạng thái tồn kho, CTA**. Chiều cao card cân đối, không cắt chữ tùy tiện, không đè badge lên ảnh, không tràn trên mobile. Thống nhất các card có ảnh ngang/dọc khác nhau.
11. Khi hover (chỉ thiết bị có hover): ảnh scale nhẹ ~1.03, shadow/tint tinh tế, chuyển tiếp 180–250ms; nút có trạng thái hover/active/focus. Không dùng hiệu ứng mạnh gây cảm giác rẻ, không làm layout shift.
12. Khi nhấn "Chọn mua": tạo **tactile feedback** tức thì như scale card rất nhẹ rồi trở về, outline/halo màu thương hiệu, icon dấu tick/badge số lượng, CTA chuyển thành `Đã chọn (n)`; cập nhật giỏ và tổng tiền đúng. Có thể thêm hiệu ứng thumbnail “fly to cart” ngắn 350–550ms ở desktop nếu không ảnh hưởng hiệu suất; ở mobile ưu tiên badge giỏ hàng cập nhật nổi bật hơn hiệu ứng bay xa. Có thể animating number count / cart bump 150–250ms. Tránh confetti/pháo giấy và animation vô hạn.
13. Khi tăng/giảm số lượng: animation số thay đổi nhẹ, không rung cả lưới, không reset scroll/focus trên card. Khi đưa số lượng về 0, card trở về trạng thái bình thường. Khi hết hàng, disabled rõ ràng, không hiệu ứng như chọn được. Khi nút được nhấn liên tiếp, mỗi lần chỉ tăng đúng một sản phẩm và không double-bind event.
14. **Cực kỳ quan trọng:** `renderMenu()` hiện dùng `innerHTML = filtered.map(...).join('')` sau mỗi lần click. Hãy refactor tối thiểu để animation tồn tại qua render: DOM keyed update, event delegation, Web Animations API hoặc biện pháp tương đương. Không làm mất input focus, vị trí scroll, trạng thái lọc và tìm kiếm. Không dùng `setTimeout` để giả cập nhật dữ liệu/giả thành công.
15. Nếu dữ liệu đang đồng bộ: skeleton card, spinner nhỏ / disable chính xác. Khi API lỗi: hiển thị lỗi tải sản phẩm và nút thử lại, không mặc định hiển thị catalog giả như dữ liệu SQL. Khi submit checkout: khóa CTA đúng lúc, xử lý timeout/error/success theo phản hồi API, **không làm thay đổi cơ chế idempotency, pricing, inventory, payment**. Mọi animation chỉ mô tả hành động UI thực, không được ám chỉ đơn đã đặt khi server chưa xác nhận.
16. Hỗ trợ `prefers-reduced-motion: reduce`, focus-visible, ARIA live cho badge cart/tổng số lượng khi phù hợp, bàn phím và touch. Không dùng animation chỉ có hover làm chức năng bị mất trên mobile. Chú ý performance trên điện thoại yếu; animation ưu tiên CSS transform/opacity, không forced reflow.

## P2 — Responsive, QA và tiêu chí nghiệm thu

17. Kiểm tra ở ít nhất 320, 375, 390, 768, 1024, 1440, 1920px; 1 cột mobile, 2+ cột khi đủ rộng, giỏ hàng mobile tiện truy cập (sticky mini cart/bottom sheet nếu cần), tránh che nút mua và che footer. Kiểm tra nội dung tên dài, giá lớn, 0 tồn, ảnh dọc/ảnh vuông/ảnh thiếu, mạng chậm và cache ảnh cũ. Kiểm tra trên Chrome/Chromium và mobile touch.
18. Viết unit/integration tests cho upload hợp lệ, sai loại, file giả, quá dung lượng, thiếu quyền, CSRF, thay ảnh, không chọn file vẫn giữ ảnh, rollback DB, và API trả imageUrl chính xác. Kiểm thử truy xuất SQL Server thật nếu môi trường có sẵn; xác nhận `TbProduct.Image` được lưu và xuất hiện trên UI qua `/api/...` và `/Product`, cùng ảnh thumbnail trong Admin.
19. Dùng browser automation nếu sẵn có: click chọn mua, tăng/giảm quantity, lọc/tìm kiếm, cart summary, checkout thất bại/thành công, screenshot before/after, console errors, broken images, CLS/overflow. Kiểm tra `prefers-reduced-motion`, focus sau render, đa click nhanh. Không tuyên bố PASS nếu chỉ nhìn code hoặc không chạy test.
20. Đảm bảo những khu vực khác của dự án không bị regression: đặt sân, check-in QR, thanh toán, POS, phân quyền, báo cáo. Không sửa chính sách nghiệp vụ hoặc database ngoài phạm vi; nếu thay đổi schema/API không tương thích thì yêu cầu phê duyệt trước. Ưu tiên không migration vì `TbProduct.Image` đã tồn tại.

## Bàn giao bắt buộc

- Mã nguồn **đã sửa trực tiếp**: Admin upload và preview; API/DTO; Pro Shop image card; cart/POS thumbnail tùy phù hợp; animations + responsive + accessiblity.
- Tài liệu `docs/proshop-v6/IMPLEMENTATION.md`: file đã sửa, ảnh nằm ở đâu, quy tắc upload/thay/xóa, thiết lập production permissions, xử lý backup ảnh (file static không nằm trong SQL backup), rollback.
- `docs/proshop-v6/TEST_RESULTS.md`: các lệnh build/test thật sự đã chạy, output chính, kết quả PASS/FAIL/BLOCKED_ENV, screenshot paths và các tình huống chưa kiểm tra được.
- `docs/proshop-v6/RELEASE_GATE.md`: checklist UI → API → SQL, chỉ công nhận DONE nếu có bằng chứng, kết luận GO/NO-GO dựa trên phạm vi sản phẩm.

## Quy trình làm việc

1. Đọc mã nguồn và skills → lập danh sách điểm cần sửa thật sự.
2. Triển khai backend upload an toàn dùng `TbProduct.Image` hiện có → kiểm thử.
3. Map imageUrl qua API → kiểm thử SQL/API và cache invalidation.
4. Thiết kế UI image cards + trạng thái skeleton/fallback → screenshot.
5. Triển khai animation tinh tế và animation giỏ hàng + reduced motion → kiểm thử tương tác.
6. Regression và browser visual review → sửa lỗi có thể tái hiện → kiểm thử lại.
7. Xuất báo cáo có bằng chứng, không tự nhận zero bugs nếu chưa kiểm thử thực.

**HÃY TRIỂN KHAI NGAY TRÊN WORKSPACE HIỆN TẠI. Không chỉ phân tích, không dùng mock để thay dữ liệu SQL, không xóa dữ liệu, không triển khai production khi chưa được phê duyệt.**
