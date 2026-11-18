# SAFE FIX RELEASE GATE — SPIKE PICKLEBALL CLUB

**Tài liệu:** Deliverable 6 — Quyết định Release Gate & Đánh giá Rủi ro Trước Giai đoạn Nâng cấp Giao diện Tiếp theo  
**Branch hiện tại:** `fix/safe-logic-remediation`

---

## 1. Kết quả Risk Audit Toàn Diện

| Hạng mục kiểm toán | Cơ chế bảo vệ đã xác minh | Bằng chứng & Trạng thái |
|---|---|---|
| **A. Database / Migration / Concurrency** | Không sửa schema hay tạo migration dư; ` PickleballClub` khớp 3/3 migration (`V2`, `V2_Ledger`, `V4_HoldTtl`). `BookingService` dùng transaction + `Serializable`/khóa dòng/AppLock; `OrderService` trừ kho atomic trong SQL transaction (`UPDLOCK`); `RowVersion` bảo vệ cập nhật đồng thời. | **PASS** — `SqlServerConcurrencyAndMigrationTests` + kiểm tra trực tiếp `__EFMigrationsHistory` |
| **B. Auth / RBAC / Session / CSRF / IDOR** | Identity claims per-request qua `IHttpContextAccessor` (`Function`); `OnValidatePrincipal` xác thực `SecurityStamp` & `IsActive` trong DB; phân tách nghiêm `[Authorize(Roles = "Admin")]` với `[Authorize(Roles = "Admin,Staff")]`; `ValidateAntiForgeryToken` bắt buộc trên mọi endpoint ghi; `/api/bookings/lookup` & `/api/bookings/{code}/qr.*` chặn hoàn toàn tra cứu ẩn danh bằng SĐT (`CallerCanAccessBookingTicket` đòi `AccountId` chính chủ hoặc `bk_*` accessToken / `SPK1.*` hợp lệ). | **PASS** — `HttpSecurityAndApiIntegrationTests`, `ShadcnAdminV7ParityTests`, Chrome E2E (Guest, Customer, Staff, Admin) |
| **C. Booking Lifecycle** | Pending hold TTL 15–30p & giới hạn đơn pending/actor qua DB (`IsPendingHoldActive`); `ApproveBookingAsync` từ chối đơn quá hạn giữ chỗ (409); `CheckSlotConflictAsync` chống đặt trùng sân cả khi gia hạn/chuyển sân; check-in kiểm khung giờ chuẩn VN `UTC+7` ở backend & frontend; `approveAllPending` không còn duyệt nhầm đơn hết hạn. | **PASS** — `V4ReleaseBlockersAndQrTests`, Playwright Chrome TZ `America/New_York` |
| **D. Finance & Pro Shop** | Mọi báo giá & tính tiền theo phân đoạn `CalculateQuote` trên máy chủ (`BookingQuoteBreakdown` + `PricingVersion`); `booking.js` ánh xạ đúng DTO máy chủ không còn hiển thị `0 đ`; đơn Pro Shop đi đúng route `/api/orders/create` với atomic stock decrement & restock khi hủy; thanh toán & hoàn tiền bất biến trên `TbPaymentTransaction`; thu tiền kèm lỗi đóng ca không bao giờ gửi trùng `/payments`; báo cáo giữ nguyên số thu ròng âm khi hoàn vượt thu. | **PASS** — `SafeRemediationRegressionTests`, `ProShopV6ImageAndApiTests`, Chrome E2E |
| **E. QR Protected Component** | Không chỉnh sửa `QrTokenService`, định dạng `SPK1.<payload>.<hmac>`, kích thước/ECC hay bố cục vùng QR trên `#bookingDigitalTicketHost` & `#lookupDigitalTicketHost`; tải SVG/PNG 512px/1024px hoạt động và giải mã lại thành công trên trạm `/Checkin` qua ZXing. | **PASS** — Benchmark **105/105** (`docs/qa/qr/qr-benchmark-report.json`) + E2E upload PNG ZXing |

---

## 2. Kiểm tra tiêu chí Release Gate

| Tiêu chí bắt buộc | Yêu cầu | Kết quả đo được | Đạt? |
|---|---|---|---|
| Tất cả lỗi **P0** đã sửa triệt để (`FIX-01`, `FIX-02`, `FIX-04`, `FIX-05`, `FIX-08`, `FIX-13`, `FIX-20` + `FIX-R1..R3`) | 100% PASS | Tất cả P0 + P1 + P2 đều đạt **PASS** | **YES** |
| Build Solution `.NET 10 Release` sạch | 0 Error | `0 Warning(s)`, `0 Error(s)` | **YES** |
| Bộ Test tự động xUnit + JS đa múi giờ | 100% PASS | xUnit **38/38 PASS**, Node JS **4/4 TZ PASS**, Node syntax **8/8 PASS** | **YES** |
| Kiểm thử trình duyệt thật 4 viewport + luồng lỗi | PASS, 0 console/page errors | Chrome E2E PASS 16 luồng (`375/768/1024/1440px`, `America/New_York` TZ, `0` lỗi ngoài ý muốn) | **YES** |
| Bảo toàn hoàn toàn QR, Pricing, Ledger, Schema & Dữ liệu thật | Không thay đổi phá vỡ | 0 migration mới, 0 bản ghi test còn sót trong DB `PickleballClub`, QR **105/105** | **YES** |

---

## 3. Quyết định Release Gate

**VERDICT: `CONDITIONAL GO`** *(An toàn để mở bước UI/UX tiếp theo trên mã nguồn hiện tại; với phát hành Production công khai rộng rãi, cần hoàn tất cấu hình hạ tầng ngoài mục 4)*.

### Lý do đạt chuẩn (Sẵn sàng cho bước tiếp theo):
- Mọi khiếm khuyết logic, đồng bộ múi giờ Việt Nam, lỗi phụ thu `peakSurcharge=0`, kẹp doanh thu âm, liệt nút giao hàng Pro Shop, rủi ro đóng ca thất bại sau thu tiền, cùng 3 lỗi hợp đồng API tiềm ẩn nghiêm trọng vừa được phát hiện (`booking.js` quote DTO mapping, `menu.js` `/api/orders/create` & `isValidVietnamPhone`, `shared.js` `saveCmsConfig` allowlist) đều đã được vá an toàn và chứng minh bằng kiểm thử tự động từ tầng unit tới trình duyệt thật.

### Các điều kiện ngoài mã nguồn trước khi Go-Live Production công khai:
1. Cấu hình chuỗi bí mật `Security:QrSigningKey` riêng của môi trường Production (>= 32 ký tự) qua Secret Manager / biến môi trường.
2. Nếu kích hoạt quên mật khẩu OTP tự động (`P1-02`), OAuth Google/Zalo (`P2-06`) hoặc đối soát VietQR (`P2-07`), phải cấu hình khóa API nhà cung cấp thật (hiện tại hệ thống từ chối trung thực với HTTP `503` không giả lập).
3. Giữ nguyên hai quy tắc trong mọi thay đổi giao diện sắp tới: **không sửa ID/data-attribute kiểm thử trên Modal QR / Vé điện tử** và **luôn chạy lại `dotnet test` + bộ E2E sau mỗi đợt chỉnh UI**.
