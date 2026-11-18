# Báo cáo Kết quả Kiểm thử Tự động Pro Shop V6 (`TEST_RESULTS.md`)

**Ngày kiểm thử:** 2026-11-10  
**Môi trường:** `.NET 10.0.13` (`Release`), SQL Server Express (`.\SQLEXPRESS`, DB `PickleballClub`), Playwright Chromium (`Google Chrome Headless`)

---

## 1. Kết quả Kiểm thử Backend & Bảo mật Upload (`xUnit` — `PickleballClub.Tests`)

Tổng số bài kiểm thử thực thi: **31 / 31 PASS (100%)**  
Riêng bộ kiểm thử chuyên biệt cho Pro Shop V6 (`PickleballClub.Tests/ProShopV6ImageAndApiTests.cs`): **5 / 5 PASS**

| Mã kiểm thử | Nội dung kiểm chứng | Kết quả |
|---|---|---|
| `UploadValidPngJpgWebp_ResizesAndPreservesAlpha` | Upload PNG có nền trong suốt (`alpha = 0`), kích thước `1500×1000px` → tự động thu nhỏ về `1200×800px`, lưu `.webp` tại `/uploads/products/<guid>.webp`, giữ nguyên vùng trong suốt (`alpha == 0`). Kiểm tra thêm JPG (`400×400`) và WebP (`500×500`). | **PASS** |
| `RejectInvalidExtensionMimeFakeBytesAndOversizedFiles` | Chặn file `.svg` (`image/svg+xml`), chặn file giả mạo (`.html`/script đổi tên thành `.png` với `Content-Type: image/png`), chặn path traversal (`../../appsettings.png`), và chặn file vượt quá `5 MB`. | **PASS** |
| `EditLifecycle_ReplaceKeepAndRemoveImage_CleansUpUnreferencedFilesOnly` | Kiểm tra trọn vòng đời: (1) Tạo sản phẩm kèm ảnh → (2) Sửa tên/giá không chọn ảnh mới (`ImageFile == null`, `RemoveImage == false`) → giữ nguyên ảnh cũ & file vật lý → (3) Thay ảnh mới → xóa file cũ, lưu file mới → (4) Bật `RemoveImage = true` → xóa đường dẫn DB về `NULL` và dọn file vật lý. | **PASS** |
| `ApiDbState_ProjectsMenuImageUrl_AndInvalidatesETagWhenProductImageChanges` | Kiểm tra `/api/db/state` trả về `imageUrl` hợp lệ cho toàn bộ mặt hàng trong `menu`; kiểm tra `If-None-Match` trả về `304 Not Modified` khi chưa đổi ảnh và lập tức trả về `200 OK` kèm ETag mới khi `TbProduct.Image` thay đổi trong SQL Server. | **PASS** |
| `AdminProductEndpoints_EnforceAuthorizationAndAntiForgeryToken` | Kiểm tra truy cập ẩn danh tới `/Admin/Products` bị chuyển hướng xác thực (`302`) và `POST /Admin/Products/Edit/1` không có Anti-Forgery Token / quyền Admin bị chặn. | **PASS** |

---

## 2. Kết quả Kiểm thử Giao diện, Keyed DOM & Animation bằng Playwright (`verify-proshop-v6.js`)

File dữ liệu thô: [`docs/proshop-v6/playwright-v6-results.json`](file:///e:/pickleball/docs/proshop-v6/playwright-v6-results.json)

| Mã kiểm thử E2E | Hạng mục kiểm chứng | Kết quả | Chi tiết đo đạc |
|---|---|---|---|
| `V6-01_StudioPackshotImagesOnProShop` | Thay thế hoàn toàn ô icon cũ bằng ảnh sản phẩm thật `1:1` trên `/Product` | **PASS** | `10/10` card hiển thị `<img class="menu-item-img">` (`naturalWidth > 0`), `legacyIconsCount = 0`, khung `.menu-item-media` vuông `1:1` (`ratioDiff <= 2px`). |
| `V6-02_KeyedDomPreservationAndSelectState` | Keyed DOM Reconciliation (`renderMenu()`) & trạng thái chọn mua | **PASS** | `sameCardNode = true`, `sameButtonNode = true`, `focusPreserved = true` (`document.activeElement === ctaBtn`), `.is-selected = true`, badge số lượng & stepper `− / +` hiển thị, `#shopCartBadge` cập nhật `1 món` (`aria-live="polite"`), ảnh thu nhỏ trong giỏ hàng `.shop-cart-thumb` tải thành công. |
| `V6-03_RapidMultiClickAndFlyCloneCleanup` | Bấm liên tiếp tốc độ cao (`+5` lần) & tự dọn dẹp clone bay vào giỏ | **PASS** | Số lượng tăng chính xác từ `1` lên `6` (`6 món`), `orphanClones = 0` sau khi kết thúc animation, không nhân đôi event listener. |
| `V6-04_BrokenImageOnErrorFallback` | Tự động chuyển sang ảnh dự phòng khi đường dẫn ảnh bị lỗi (`onerror`) | **PASS** | Khi gán URL lỗi `non-existent-test-404.webp`, sự kiện `onerror` tự động chuyển `src` về `/assets/products/placeholder-default.svg` (`naturalWidth > 0`). |
| `V6-05_PrefersReducedMotionSupport` | Tương thích `prefers-reduced-motion: reduce` | **PASS** | Khi bật `reducedMotion: 'reduce'`, `prefersReducedMotion() === true`, không sinh `.proshop-fly-clone` (`clonesDuringClick = 0`) nhưng vẫn cập nhật đầy đủ trạng thái `.is-selected` và giỏ hàng. |
| `V6-06_ResponsiveBreakpoints320To1920` | Kiểm tra responsive trên 7 độ rộng màn hình (`320, 375, 390, 768, 1024, 1440, 1920px`) | **PASS** | `7/7` breakpoint đạt `overflowPx = 0` và `brokenCount = 0`. |
| `V6-07_AdminProductsIndexThumbnails` | Hiển thị ảnh thumbnail sản phẩm trong bảng `/Admin/Products` | **PASS** | `10/10` dòng sản phẩm hiển thị ảnh thumbnail sắc nét (`naturalWidth > 0`). |
| `V6-08_AdminImageUploadE2eAndApiSync` | Upload ảnh mới trong `/Admin/Products/Edit/1`, xem trước (`blob:`), lưu DB & đồng bộ `/api/db/state` | **PASS** | Xem trước ảnh hoạt động ngay khi chọn file (`previewUpdatedBeforeSubmit = true`), sau khi bấm Lưu hiển thị thông báo thành công và `/api/db/state` trả về đường dẫn `/uploads/products/<guid>.webp`. |
| `V6-09_PosModalProductThumbnails` | Hiển thị ảnh thu nhỏ sản phẩm trong modal POS (`/Management`) | **PASS** | `10/10` nút `.pos-menu-item` trong `#posMenuGrid` hiển thị `img.pos-menu-item-thumb` (`naturalWidth > 0`). |

---

## 3. Danh mục Ảnh Chụp Màn Hình Đối Chứng (Before / After)

### Trước khi nâng cấp (`docs/proshop-v6/screenshots/before/`)
- [`01-proshop-desktop-1440-before.png`](file:///e:/pickleball/docs/proshop-v6/screenshots/before/01-proshop-desktop-1440-before.png) — Giao diện Pro Shop `1440px` cũ dùng ô icon 44×44.
- [`02-proshop-mobile-375-before.png`](file:///e:/pickleball/docs/proshop-v6/screenshots/before/02-proshop-mobile-375-before.png) — Giao diện Pro Shop `375px` mobile cũ.
- [`03-admin-products-index-before.png`](file:///e:/pickleball/docs/proshop-v6/screenshots/before/03-admin-products-index-before.png) — Trang `/Admin/Products` cũ chỉ hiển thị chuỗi tên icon.
- [`04-admin-products-edit-before.png`](file:///e:/pickleball/docs/proshop-v6/screenshots/before/04-admin-products-edit-before.png) — Trang `/Admin/Products/Edit/1` cũ không có tính năng upload ảnh.
- [`05-pos-modal-before.png`](file:///e:/pickleball/docs/proshop-v6/screenshots/before/05-pos-modal-before.png) — Modal POS trên `/Management` cũ dùng icon.

### Sau khi nâng cấp (`docs/proshop-v6/screenshots/after/`)
- [`01-proshop-desktop-1440-after.png`](file:///e:/pickleball/docs/proshop-v6/screenshots/after/01-proshop-desktop-1440-after.png) — Giao diện Pro Shop `1440px` mới với khung ảnh studio `1:1`.
- [`06-proshop-selected-state-1440-after.png`](file:///e:/pickleball/docs/proshop-v6/screenshots/after/06-proshop-selected-state-1440-after.png) — Trạng thái card đã chọn mua (`is-selected`), badge số lượng, bộ tăng giảm `− / +` và giỏ hàng có ảnh thu nhỏ.
- [`02-proshop-mobile-375-after.png`](file:///e:/pickleball/docs/proshop-v6/screenshots/after/02-proshop-mobile-375-after.png) — Giao diện Pro Shop `375px` mobile kèm thanh giỏ hàng cố định.
- [`07-proshop-mobile-320-after.png`](file:///e:/pickleball/docs/proshop-v6/screenshots/after/07-proshop-mobile-320-after.png) — Giao diện Pro Shop ở độ rộng nhỏ nhất `320px` không tràn ngang.
- [`03-admin-products-index-after.png`](file:///e:/pickleball/docs/proshop-v6/screenshots/after/03-admin-products-index-after.png) — Bảng `/Admin/Products` mới hiển thị cột ảnh thumbnail thực tế.
- [`04-admin-products-edit-after.png`](file:///e:/pickleball/docs/proshop-v6/screenshots/after/04-admin-products-edit-after.png) — Giao diện `/Admin/Products/Edit/1` với khung xem trước `1:1` và vùng kéo-thả upload ảnh.
- [`08-admin-products-upload-preview-after.png`](file:///e:/pickleball/docs/proshop-v6/screenshots/after/08-admin-products-upload-preview-after.png) — Trạng thái xem trước ảnh mới ngay trên trình duyệt trước khi bấm Lưu.
- [`05-pos-modal-after.png`](file:///e:/pickleball/docs/proshop-v6/screenshots/after/05-pos-modal-after.png) — Modal POS trên `/Management` hiển thị ảnh thu nhỏ cho từng mặt hàng.
