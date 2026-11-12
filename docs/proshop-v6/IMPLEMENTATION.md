# Báo cáo Triển khai Pro Shop V6: Hình Ảnh Sản Phẩm Thật, Admin Upload & Tactile Microinteractions

**Ngày thực hiện:** 2026-11-10  
**Dự án:** Spike Pickleball Club (`ASP.NET Core MVC / .NET 10 / EF Core / SQL Server / Vanilla JS`)  
**Phạm vi:** Triển khai toàn diện `Antigravity_V6_Product_Images_Animated_ProShop.md` (`P0` → `P2`)

---

## 1. Kiến trúc Dữ liệu & Tương thích SQL Server (`TbProduct.Image`)

- **Tái sử dụng cột chuẩn hiện có:** Hệ thống sử dụng trực tiếp cột `TbProduct.Image` (`nvarchar(500)`) trong bảng `tb_Product` của SQL Server (`Models/TbProduct.cs`). Không tạo thêm cột trùng lặp (`ImageUrl`) hay migration phá vỡ schema.
- **Giữ nguyên `TbProduct.Icon` cho vai trò phụ trợ:** Trường `Icon` vẫn được bảo toàn cho các nhãn phụ hoặc dữ liệu lịch sử, nhưng trên giao diện Pro Shop (`/Product`), Quản trị sản phẩm (`/Admin/Products`) và Quầy POS (`/Management`), hình đại diện chính sử dụng hoàn toàn thẻ `<img>` với tỷ lệ khung студио `1:1` (`aspect-ratio: 1 / 1`, `object-fit: contain`).
- **Chuẩn hóa URL hình ảnh (`ProductImageService.NormalizeProductImageUrl`):**
  - Khi `TbProduct.Image` chứa đường dẫn hợp lệ thuộc `/uploads/products/...` hoặc `/assets/products/...` (không chứa `..` và không phải các ảnh sân/banner cũ như `/assets/court-vip-arena.png`), API trả về đúng đường dẫn ảnh sản phẩm đó.
  - Khi `TbProduct.Image` rỗng, `NULL`, hoặc đã được Admin gỡ ảnh, hệ thống trả về ảnh minh họa chuẩn theo danh mục:
    - `drink` → `/assets/products/placeholder-drink.svg`
    - `food` → `/assets/products/placeholder-food.svg`
    - `other` → `/assets/products/placeholder-other.svg`
    - Mặc định / fallback `onerror` → `/assets/products/placeholder-default.svg`

---

## 2. Hệ thống Upload Ảnh Sản Phẩm An toàn trong Admin (`/Admin/Products`)

### 2.1. ViewModel & Controller (`Models/ProductFormViewModel.cs`, `Areas/Admin/Controllers/ProductsController.cs`)
- Tách biệt `ProductFormViewModel` khỏi Entity EF Core `TbProduct`, bổ sung:
  - `IFormFile? ImageFile`: Nhận file ảnh upload từ `multipart/form-data`.
  - `bool RemoveImage`: Cho phép Admin chủ động gỡ ảnh hiện tại về ảnh mặc định theo danh mục.
  - `string? ExistingImage`: Hiển thị ảnh hiện hành trong form cập nhật.
- Không cho phép client tự truyền chuỗi `Image` tùy ý để ghi đè đường dẫn trong cơ sở dữ liệu.
- Bảo vệ toàn bộ các action `Create`, `Edit`, `Delete` bằng `[Area("Admin")]`, `[Authorize(Roles = "Admin")]`, `[ValidateAntiForgeryToken]` và `[RequestSizeLimit(6 * 1024 * 1024)]`.

### 2.2. Kiểm tra Bảo mật & Chuẩn hóa Ảnh bằng SixLabors.ImageSharp (`Services/ProductImageService.cs`)
1. **Kiểm tra định dạng & MIME:** Chỉ chấp nhận phần mở rộng `.png`, `.jpg`, `.jpeg`, `.webp` và `Content-Type` thuộc `image/png`, `image/jpeg`, `image/jpg`, `image/webp`.
2. **Giới hạn dung lượng:** Tối đa `5 MB` (`5 * 1024 * 1024` bytes).
3. **Kiểm tra Magic Bytes (File Signature):** Kiểm tra trực tiếp byte đầu của file (`PNG: 89 50 4E 47...`, `JPEG: FF D8 FF`, `WebP: RIFF....WEBP`) trước khi giải mã; chặn triệt để các file HTML, SVG, script hoặc executable đổi đuôi thành `.png`.
4. **Giải mã thật & Kiểm tra kích thước 픽셀 (`Image.IdentifyAsync` & `Image.LoadAsync<Rgba32>`):**
   - Từ chối ảnh có chiều rộng/cao `< 16px` hoặc `> 6000px` (chống ảnh dị dạng / decompression bomb).
   - Loại bỏ toàn bộ metadata (`ExifProfile`, `IccProfile`, `XmpProfile`).
   - Tự động thu nhỏ (`ResizeMode.Max`, `Lanczos3`) nếu cạnh lớn nhất vượt quá `1200px`.
   - Bảo toàn kênh trong suốt (`alpha channel`) bằng `WebpEncoder { Quality = 86, TransparentColorMode = WebpTransparentColorMode.Preserve }` và lưu dưới tên ngẫu nhiên `/uploads/products/<guid>.webp`.
5. **An toàn Transaction & Dọn dẹp File cũ:**
   - Nếu `SaveChangesAsync()` gặp lỗi sau khi đã ghi file mới, hệ thống lập tức xóa file vừa tạo (`DeletePhysicalUploadedFile`) để không sinh file mồ côi.
   - Khi thay ảnh mới hoặc chọn `RemoveImage = true` trong `Edit`, file cũ trong `/uploads/products/` chỉ bị xóa sau khi DB commit thành công và không còn sản phẩm nào khác tham chiếu (`DeleteOldUploadIfUnreferencedAsync`).
   - Khi sửa thông tin sản phẩm (tên, giá, tồn kho) mà không chọn file ảnh mới và không bật `RemoveImage`, ảnh hiện tại được giữ nguyên tuyệt đối.

---

## 3. Đồng bộ API `/api/db/state` & Cache ETag (`Controllers/ApiDbController.cs`)

- **Cập nhật ETag Seed:** Bổ sung `:p.Image:p.Description:p.ModifiedDate:O` vào chuỗi băm ETag của `rawProducts` trong `GetState()`. Mọi thao tác thêm, thay thế hoặc gỡ ảnh sản phẩm trong Admin đều làm thay đổi ETag ngay lập tức, buộc trình duyệt nhận `200 OK` thay vì `304 Not Modified`.
- **Mở rộng DTO `menu`:** Mỗi phần tử trong mảng `menu` trả về từ `/api/db/state` bao gồm:
  - `id`, `name`, `price`, `stock`, `type`, `description`
  - `imageUrl`: Đường dẫn ảnh sản phẩm đã chuẩn hóa
  - `rawImage` & `hasCustomImage`: Trạng thái ảnh riêng của sản phẩm
  - `icon`: Giữ tương thích ngược cho các thành phần phụ trợ

---

## 4. Thiết kế Card Sản Phẩm Pro Shop, Keyed DOM Reconciliation & Tactile Animations

### 4.1. Card Sản Phẩm Studio Stage (`wwwroot/css/styles.css`, `wwwroot/js/menu.js`)
- Khung ảnh `.menu-item-media` tỷ lệ `1:1` (`aspect-ratio: 1 / 1`) với nền studio sáng dịu (`radial-gradient`), `padding: 0.85rem`, `object-fit: contain` giúp chai nước, lon nước tăng lực, vợt T700 và bóng Franklin X-40 hiển thị trọn vẹn, không bị cắt mép.
- Hệ thống badge trực quan trên khung ảnh:
  - Góc trái trên (`.menu-item-cat-pill`): Nhãn danh mục (`Đồ uống`, `Đồ ăn`, `Phụ kiện`).
  - Góc phải trên (`.menu-item-stock-badge`): Trạng thái tồn kho (`Còn N`, `Sắp hết · N`, `Hết hàng`).
  - Góc phải dưới (`.menu-item-selected-badge`): Số lượng đã chọn trong giỏ (`is-visible` khi `inCartQty > 0`).

### 4.2. Keyed DOM Reconciliation (`renderMenu()` trong `wwwroot/js/menu.js`)
- Khắc phục triệt để vấn đề `grid.innerHTML = ...` dựng lại toàn bộ DOM sau mỗi lần bấm mua:
  - Khi danh sách ID sản phẩm hiển thị trong `#menuGrid` không thay đổi cấu trúc, `renderMenu()` duyệt qua từng `.menu-item[data-product-id]` hiện hữu và gọi `updateMenuCardInPlace(cardEl, item)`.
  - Chỉ cập nhật class trạng thái (`is-selected`, `is-out-of-stock`), badge số lượng, bộ tăng giảm `− / +` (`.menu-item-stepper`) và nhãn nút CTA (`Chọn mua` → `Đã chọn (N)`).
  - Giữ nguyên 100% tham chiếu DOM node (`cardEl` và `ctaBtn`), bảo toàn `document.activeElement` (keyboard focus), vị trí cuộn trang và chuỗi animation đang chạy.
  - Sử dụng Event Delegation một lần duy nhất trên `#menuGrid` (`ensureMenuGridDelegation()`), chống gắn trùng sự kiện khi bấm liên tiếp tốc độ cao.

### 4.3. Tactile Microinteractions & Hỗ trợ `prefers-reduced-motion`
- **Card Select Pulse (`animateCardSelect`):** Phản hồi xúc giác `scale(1) → scale(1.022) → scale(1)` (`220ms`, `cubic-bezier(0.22, 1, 0.36, 1)`) kèm hiệu ứng nảy số lượng (`scale(1.28)`).
- **Fly-to-Cart Clone (`animateFlyToCart`):** Trên màn hình `>= 768px`, nhân bản ảnh sản phẩm kích thước `56×56px` bay theo quỹ đạo cong về `#shopCartBadge` trong `400ms` và tự hủy hoàn toàn trong `onfinish`/`oncancel` kèm `setTimeout` bảo hiểm.
- **Cart Badge & Total Bump (`animateCartBump`):** Nhấn mạnh sự thay đổi tổng số món và tổng tiền trên cả panel desktop (`#shopCartBadge`, `#shopCartTotal`) và thanh giỏ hàng cố định trên mobile (`#mobileCartCount`, `#mobileCartTotal`) với `aria-live="polite"`.
- **Tôn trọng `prefers-reduced-motion: reduce`:** Tự động tắt hiệu ứng bay và chuyển động scale khi người dùng bật chế độ giảm chuyển động của hệ điều hành.
