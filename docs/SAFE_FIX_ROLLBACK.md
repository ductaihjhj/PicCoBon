# SAFE FIX ROLLBACK GUIDE — SPIKE PICKLEBALL CLUB

Tài liệu hướng dẫn khôi phục nhanh và an toàn toàn bộ thay đổi thuộc Master Prompt 01.

---

## 1. Điểm khôi phục & Tình trạng cơ sở dữ liệu

- **Branch trước khi chạy Master Prompt 01:** `fix/v4-release-blockers-and-qr` @ commit `49ceb36`
- **Branch hiện tại chứa toàn bộ các bản sửa:** `fix/safe-logic-remediation`
- **Snapshot diff các thay đổi V7/QA chưa commit trước lúc bắt đầu:** được sao lưu tại thư mục artifact scratch `pre_remediation_diff.patch` và danh sách `pre_remediation_status.txt`.
- **Trạng thái SQL Server:** **KHÔNG có bất kỳ thay đổi schema, migration, seed hay ghi dữ liệu thử nào lên database `PickleballClub`**. Chỉ tồn tại 3 migration chính thức từ trước (`V2_BaselineAndSafeExpand`, `V2_LedgerTypedColumnsAndRowVersion`, `V4_HoldTtlIdempotencyAndOrderFulfillment`). Do đó **không cần rollback database**.

---

## 2. Phương án Rollback nhanh

### Phương án A — Khôi phục riêng các tệp JS/Razor/Test đã chỉnh bởi Master Prompt 01 (giữ nguyên toàn bộ V7 UI)

Chỉ áp dụng `git checkout HEAD -- <file>` nếu chưa commit, HOẶC khôi phục từ bản sao lưu `pre_remediation_diff.patch` với đúng các tệp thuộc đợt sửa logic:

```powershell
# Danh sách tệp JS/Test hoàn toàn thuộc logic remediation (có thể hoàn nguyên an toàn từng tệp):
git checkout -- wwwroot/js/booking.js wwwroot/js/menu.js wwwroot/js/reports.js
Remove-Item PickleballClub.Tests\SafeRemediationRegressionTests.cs -ErrorAction SilentlyContinue
```

> Lưu ý: `wwwroot/js/shared.js`, `wwwroot/js/dashboard.js`, `wwwroot/js/management.js`, `Areas/Admin/Views/Home/{Management,Cms}.cshtml`, và `Views/**` trước đó đã mang các chỉnh sửa V7 UI chưa commit. Nếu cần đưa riêng chúng về trạng thái trước Master Prompt 01 mà vẫn giữ nguyên V7 UI, hãy checkout về `49ceb36` rồi áp lại `pre_remediation_diff.patch`.

### Phương án B — Hoàn nguyên toàn bộ về đúng snapshot trước Master Prompt 01 (giữ 100% V7 UI & QR)

```powershell
git checkout fix/v4-release-blockers-and-qr
# Các chỉnh sửa V7 UI đang ở working tree của nhánh ban đầu không bị ghi đè;
# Nếu vừa tạo thêm tệp untracked từ nhánh sửa lỗi (SafeRemediationRegressionTests.cs, docs/SAFE_*.md), xóa gọn chúng:
Remove-Item PickleballClub.Tests\SafeRemediationRegressionTests.cs -ErrorAction SilentlyContinue
Remove-Item docs\SAFE_*.md -ErrorAction SilentlyContinue
dotnet build PickleballClub.slnx -c Release
```

### Phương án C — Rollback từng Batch độc lập

- **Batch A (Time & Pricing Core):** hoàn tác `getVietnamNowDate` / `resolveNumericConfig` trong `wwwroot/js/shared.js`, `wwwroot/js/dashboard.js`, `wwwroot/js/reports.js`, `Areas/Admin/Views/Home/Cms.cshtml`.
- **Batch B (Management Ops):** hoàn tác phần `fulfillAdminOrder`, `#ledgerPartialFailureBanner`, `approveAllPending`, `full_day` lock và thanh tìm kiếm đơn hàng trong `Areas/Admin/Views/Home/Management.cshtml` & `wwwroot/js/management.js`.
- **Batch C (Booking & Pro Shop Contract):** hoàn tác `normalizeServerQuotePayload`, `syncBookingUrlState`, `PRICING_VERSION` key, `populateActiveBookingOptions`, `/api/orders/create`, `WRITABLE_CMS_KEYS` trong `wwwroot/js/booking.js`, `wwwroot/js/menu.js`, `wwwroot/js/shared.js`, `wwwroot/js/reports.js`, `Views/Product/Index.cshtml`, `Views/Booking/Index.cshtml`.
- **Batch D (Accessibility & SSR Truth):** gỡ bỏ block `@inject ClubSettingsService` / `PickleballContext` trong `Views/Shared/_Layout.cshtml`, `Views/Home/Index.cshtml`, `Views/Booking/Index.cshtml`, `Views/Account/Login.cshtml`, `Areas/Admin/Views/Home/Cms.cshtml` và trả `.paddle-chip` ở `Views/Account/Profile.cshtml`.

---

## 3. Kiểm chứng sau Rollback

Sau bất kỳ thao tác rollback nào, bắt buộc chạy lại:

```powershell
dotnet build PickleballClub.slnx -c Release
dotnet test PickleballClub.Tests\PickleballClub.Tests.csproj -c Release --verbosity minimal
```
