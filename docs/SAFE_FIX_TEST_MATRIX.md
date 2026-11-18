# SAFE FIX TEST MATRIX — SPIKE PICKLEBALL CLUB

Tài liệu đối chiếu toàn bộ các lớp kiểm thử tự động đã chạy **sau lần chỉnh sửa code cuối cùng**, trên bản build `Release` (`net10.0`).

---

## 1. Tổng hợp thực thi kiểm thử

| Lớp kiểm thử | Lệnh thực thi / Môi trường | Số lượng & Phạm vi | Kết quả quan sát thực tế |
|---|---|---|---|
| **1. Kiểm tra cú pháp JS** | `node --check wwwroot/js/*.js` | 8 tệp: `booking.js`, `checkin.js`, `dashboard.js`, `lookup.js`, `management.js`, `menu.js`, `reports.js`, `shared.js` | **8/8 PASS** (exit code `0`) |
| **2. Build Release** | `dotnet build PickleballClub.slnx -c Release` | Toàn bộ ứng dụng ASP.NET Core MVC | **PASS** — `0 Warning(s)`, `0 Error(s)` |
| **3. Unit Tests đa múi giờ JS** | `js_unit_tests.mjs` với `$env:TZ` lần lượt là `UTC`, `America/New_York`, `Asia/Tokyo`, `Asia/Ho_Chi_Minh` | Kiểm tra `DEFAULT_COURTS`, `resolveNumericConfig`, `isValidVietnamPhone`, `calculateSegmentedPricing` (`peakSurcharge=0`/`undefined`), đồng hồ `UTC+7` + `SERVER_TIME_OFFSET_MS`, `renderReportCourts` giá trị âm `-50000`, `populateActiveBookingOptions` (lọc hôm nay + chưa hết giờ), `makeQuoteKey` đổi theo `PRICING_VERSION`, `normalizeServerQuotePayload` | **4/4 Timezone PASS** (0 assertion failed) |
| **4. xUnit Suite (`PickleballClub.Tests`)** | `dotnet test PickleballClub.Tests\PickleballClub.Tests.csproj -c Release --no-build --verbosity minimal` (chạy trên SQL Express cô lập mỗi fixture) | 35 test có sẵn (Unit/Security/QR/Concurrency/ProShop V6/V7 Admin Parity) + 3 integration test mới trong `SafeRemediationRegressionTests.cs` | **38/38 PASS** (`Failed: 0, Passed: 38, Skipped: 0`, 36s) |
| **5. QR Benchmark & Decode** | Thực thi trong `V4ReleaseBlockersAndQrTests` + `docs/qa/qr/qr-benchmark-report.json` | 105 mẫu (`standard_512_png`: 35, `logo_512_png`: 30, `mobile_220_downscaled`: 15, `jpeg_compressed_q80`: 15, `highres_1024_png`: 10) giải mã bằng ZXing & kiểm chữ ký HMAC `SPK1.*` | **105/105 PASS** (`passRatePercent: 100`) |
| **6. Playwright Chrome E2E** | `run_e2e_isolated.ps1` khởi chạy `PickleballClub.dll` (`Testing`, `http://127.0.0.1:5199`) trên DB SQL Express cô lập `PickleballClub_E2E_*`, chạy trên Chrome thật | 16 luồng nghiệp vụ + kiểm tra 4 viewport (`375x812`, `768x1024`, `1024x768`, `1440x900`) + browser timezone `America/New_York` + kiểm tra lỗi/bất thường | **PASS toàn phần** (`ALL E2E CHROME WORKFLOWS PASSED`, 0 console/page error ngoài ý muốn) |

---

## 2. Ma trận kịch bản kiểm thử chi tiết

### 2.1. 16 Luồng nghiệp vụ cốt lõi & Kiểm tra đa viewport (`375 / 768 / 1024 / 1440`)

| STT | Luồng nghiệp vụ | Hành động & Kiểm chứng | Kết quả E2E |
|---|---|---|---|
| 1 | Trang chủ (`/`) | Tải SSR & sync `/api/db/state`: đúng 2 sân thật từ DB (`#courtsGrid`), `[data-cms="courtCount"] = 2`, quick book, không tràn ngang ở `375/768/1024/1440px` | PASS |
| 2 | Đặt sân (`/Booking`) dưới TZ `America/New_York` | `#bookingDatePicker` khớp ngày hiện tại `Asia/Ho_Chi_Minh`, đổi duration=90 cập nhật URL `?duration=90`, quote hiển thị giá thật (không còn `0 đ`), gửi đơn khách lẻ sinh mã `PB-*` & `bk_*` | PASS |
| 3 | Vé Pending vs Approved (`FIX-17`) | Đơn mới ở trạng thái `pending` mở `#modalQR` với tiêu đề `"Vé Sân Điện Tử · Đã Tiếp Nhận — Chờ Duyệt"` và SVG QR | PASS |
| 4 | Đóng Modal bằng Backdrop (`FIX-15`) | Click vùng nền ngoài `#modalQR` gọi `closeModal`, xóa `.open` và đặt `aria-hidden="true"` | PASS |
| 5 | Tra cứu vé (`/Lookup`) | Anonymous query `?phone=...` bị từ chối HTTP 400; tra cứu bằng `PB-*` + `bk_*` hiển thị kết quả, tải `/api/bookings/{code}/qr.png?size=512` trả HTTP 200 | PASS |
| 6 | Bảo vệ Guest truy cập Admin | Khách chưa đăng nhập vào `/Management` bị chuyển hướng về màn hình Login | PASS |
| 7 | Đăng nhập Hội viên & Profile | Đăng nhập `e2e_member@spike.test`, `.paddle-chip` là thẻ `<button type="button">` thao tác bằng phím `Tab` + `Enter`, kiểm tra tràn ngang 4 viewport | PASS |
| 8 | Pro Shop ảnh & Fallback (`FIX-19`) | Kiểm tra mọi `<img class="menu-item-img">` có `naturalWidth > 0`; ảnh thiếu (`/uploads/products/nonexistent-test-fallback.png`) chuyển sang `placeholder-default.svg` sạch sẽ | PASS |
| 9 | Pro Shop lọc đơn gộp sân (`FIX-03`, `FIX-16`) | Nhãn trường đổi rõ nghĩa; `PB-E2EFUT` (ngày mai) & `PB-E2EEND` (đã hết giờ hôm nay) bị ẩn khỏi dropdown, chỉ giữ `PB-E2EPLAY` đang chơi hôm nay | PASS |
| 10 | Tạo đơn Pro Shop thật (`FIX-R2`) | Bấm "Chọn mua", gộp `PB-E2EPLAY`, gửi đơn qua `POST /api/orders/create` trả `200 OK` và mở `#modalShopOrder` kèm mã `ORD-*` | PASS |
| 11 | Báo cáo doanh thu âm & lỗi mạng (`FIX-06`, `FIX-07`) | Lọc `2026-09-02` khi hoàn 100.000đ trên sân không có thu mới: cột biểu đồ giữ `data-value="-100000"`, nhãn `-100.000 đ` và `.is-negative`. Khi `route.abort()` xảy ra, hiện `[data-testid="ui-state-stale"]` | PASS |
| 12 | Duyệt hàng loạt có TTL (`FIX-10`) | `PB-EXPHOLD` (quá hạn TTL giữ chỗ) bị loại; bấm "Duyệt tất cả" rồi Hủy confirm -> giữ nguyên số lượng; bấm OK confirm -> duyệt hết 2+ đơn hợp lệ | PASS |
| 13 | Pro Shop Admin Search, Pagination >25 & Fulfill (`FIX-04`, `FIX-14`) | Hiển thị tổng >= 28 đơn, nút `#btnLoadMoreShopOrders` tải đủ >25 đơn, bấm "Đã giao đồ" gọi `POST /api/admin/orders/{code}/fulfill` thành công | PASS |
| 14 | Sổ cái Thu tiền + Lỗi đóng ca (`FIX-05`) | Mô phỏng `POST /finish` lỗi `500` sau khi `POST /payments` thành công: hiện `#ledgerPartialFailureBanner`, bấm `#btnRetryLedgerFinishOnly` chỉ gọi `/finish` (tổng `/payments` vẫn là `1`) và đóng ca | PASS |
| 15 | Khóa sân cả ngày & CMS `peakSurcharge=0` (`FIX-11`, `FIX-20`, `FIX-R3`) | Chọn `full_day` tự đặt `lockStart=06:00` và khóa `06:00→22:00`; Admin đổi `peakSurcharge=0` trên `/Cms` lưu `200 OK`, `/` hiển thị giá giờ vàng `120.000 đ` | PASS |
| 16 | Check-in QR PNG ZXing & Staff RBAC | Tải ảnh 512px QR lên `#qrFileInput` tại `/Checkin` -> `POST /api/checkin/decode-qr` giải mã ra đúng token `SPK1.*`; Staff vào `/Management` (`200`), bị chặn chuyển hướng khi vào `/Admin/Courts` & `/Cms` | PASS |

### 2.2. Kiểm chứng toàn vẹn Database thực

- Kiểm tra lại `sqlcmd` trên `.\SQLEXPRESS`: chỉ tồn tại duy nhất DB thật `PickleballClub` (`ActiveCourts = 4`, `ActiveProducts = 11`, `Bookings = 9`, `Orders = 0`). Mọi DB test tạm đều đã được dọn dẹp hoàn toàn.
