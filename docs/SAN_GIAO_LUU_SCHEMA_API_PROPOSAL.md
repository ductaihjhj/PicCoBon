# Đề xuất thay đổi Schema & API — Tính năng Sân giao lưu (Spike OS)

> **Trạng thái:** Chờ chủ dự án duyệt theo quy tắc `AGENTS.md` (*"Do not modify APIs or database schemas without approval"*).  
> Trong thời gian chờ duyệt, toàn bộ 8 sprite nhân vật, engine chuyển động tự động (`wwwroot/js/san-giao-luu-engine.js`) và giao diện prototype đầy đủ (`prototypes/san-giao-luu/San_Giao_Luu_8_Nhan_Vat.html`) đã được hoàn thiện bằng dữ liệu mẫu tách biệt.

---

## 1. Thay đổi Database Schema đề xuất

Thay vì thêm nhiều cột trực tiếp vào `tb_Account`, đề xuất tạo **2 bảng mới độc lập** có khóa ngoại tới `tb_Account(AccountId)`:

### 1.1. Bảng `tb_SocialProfile` (Quan hệ 1–1 với `tb_Account`)
| Cột | Kiểu SQL | Mặc định | Mục đích |
| :--- | :--- | :--- | :--- |
| `AccountId` | `int` (PK, FK → `tb_Account`) | — | Liên kết chính xác từng hội viên |
| `IsVisibleOnCourt` | `bit` | `0` (`false`) | Quyền tham gia / ẩn khỏi Sân giao lưu (opt-in) |
| `IsReadyToPlay` | `bit` | `0` (`false`) | Trạng thái **Sẵn sàng giao lưu** hiển thị chấm xanh trên sân |
| `AllowMatchSuggestion` | `bit` | `0` (`false`) | Cho phép xuất hiện trong kết quả **Tìm người hợp trình** |
| `SpriteId` | `nvarchar(24)` | `NULL` | Mã sprite pixel đã chọn (`sunny`, `ace`, `mochi`, `coco`, `maya`, `leo`, `kiko`, `kai`) — tách biệt hoàn toàn với `tb_Account.Avatar` |
| `SkillConfirmed` | `bit` | `0` (`false`) | Phân biệt trình độ tự khai đã xác nhận vs. giá trị mặc định `Giao lưu (DUPR 3.0)` |
| `ConfirmedSkillLevel` | `nvarchar(40)` | `NULL` | Nhãn trình độ tự khai khi `SkillConfirmed = 1` |
| `PreferredFreeSlotsJson` | `nvarchar(400)` | `N'[]'` | Danh sách khung giờ thường rảnh hội viên chọn chia sẻ |
| `UpdatedAtUtc` | `datetime2` | `SYSUTCDATETIME()` | Thời điểm cập nhật gần nhất |

### 1.2. Bảng `tb_SocialInvite` (Lời mời giao lưu)
| Cột | Kiểu SQL | Mặc định | Mục đích |
| :--- | :--- | :--- | :--- |
| `InviteId` | `int` (PK, Identity) | — | Khóa chính lời mời |
| `SenderAccountId` | `int` (FK → `tb_Account`) | — | Hội viên gửi lời mời |
| `ReceiverAccountId` | `int` (FK → `tb_Account`) | — | Hội viên nhận lời mời |
| `ProposedDate` | `date` | — | Ngày giao lưu đề xuất (chặn ngày đã qua theo giờ Việt Nam) |
| `ProposedStartTime` | `nvarchar(5)` | — | Giờ bắt đầu (`HH:mm`) |
| `DurationMinutes` | `int` | `60` | Thời lượng dự kiến (`60`–`180`) |
| `Note` | `nvarchar(200)` | `NULL` | Lời nhắn ngắn (strip HTML) |
| `Status` | `nvarchar(20)` | `N'pending'` | `pending` / `accepted` / `declined` / `countered` / `cancelled` |
| `CounterDate` | `date` | `NULL` | Ngày đề xuất lại (nếu người nhận chọn giờ khác) |
| `CounterStartTime` | `nvarchar(5)` | `NULL` | Giờ đề xuất lại |
| `CreatedAtUtc` | `datetime2` | `SYSUTCDATETIME()` | Thời điểm tạo |
| `RespondedAtUtc` | `datetime2` | `NULL` | Thời điểm phản hồi |

---

## 2. Tác động lên dữ liệu cũ & Phạm vi bảo toàn

- **Không sửa đổi bất kỳ cột hay ràng buộc nào** của `tb_Account`, `tb_Booking`, `tb_Court`, `tb_Product`, `tb_PaymentTransaction`.
- **An toàn quyền riêng tư mặc định:** Mọi tài khoản hiện có chưa có bản ghi `tb_SocialProfile` sẽ mặc định **ẩn khỏi Sân giao lưu** (`IsVisibleOnCourt = false`) và không xuất hiện trong danh sách hay gợi ý cho tới khi hội viên tự bật.
- **Xử lý trình độ mặc định:** Trường `tb_Account.SkillLevel` hiện có giá trị mặc định `Giao lưu (DUPR 3.0)` sẽ hiển thị là *"Chưa xác nhận trình độ"* trong ngữ cảnh Sân giao lưu nếu `SkillConfirmed = false`.
- **Không lộ dữ liệu riêng tư:** Projection DTO chỉ trả về: `accountId`, `displayName` (chỉ lấy `FullName`, fallback `"Hội viên Spike"`, tuyệt đối không fallback `Username`), `avatar`, `avatarMode`, `spriteId` (ưu tiên ánh xạ từ avatar `/Account/Profile` trước `tb_SocialProfile.SpriteId`), `skillConfirmed`, `skillLabel`, `dominantHand`, `paddleBrand`, `playStyle`, `freeSlots`, `readyToPlay`, `isSelf`. Tuyệt đối loại trừ `Phone`, `Email`, `Address`, `BirthDate`, `EmergencyContact`, `Username`, `Bio`, hạng tích lũy, điểm/tiền tích lũy, lịch đặt sân và giao dịch.
- **Đề xuất mở rộng hợp đồng DTO (additive, không đổi SQL/migration):** Bổ sung `dominantHand`, `paddleBrand` (lấy từ cột sẵn có của `tb_Account`, lọc thông tin liên hệ và fallback `"Chưa cập nhật"`) và `avatarMode` (`preset` | `custom-upload` | `fallback`) cùng tham số tùy chọn `page`, `pageSize`, `search` trên `GET /api/social/court`. Giữ nguyên toàn bộ route, HTTP method và cấu trúc request hiện có.

---

## 3. Danh sách Route & API đề xuất

Tất cả endpoint đều áp dụng `[Authorize]`, kiểm tra `tb_Account.IsActive == true` và quyền hội viên hợp lệ tại server, cùng `[ValidateAntiForgeryToken]` cho mọi thao tác ghi:

| Method & Route | Chức năng |
| :--- | :--- |
| `GET /SanGiaoLuu` | Trang MVC Razor tích hợp vào `_Layout.cshtml` sử dụng `wwwroot/js/san-giao-luu-engine.js` và bộ 8 sprite `wwwroot/assets/san-giao-luu/` |
| `GET /api/social/court` | Trả danh sách hội viên đủ điều kiện & đang bật `IsVisibleOnCourt = true` (kèm trạng thái hồ sơ giao lưu của chính người xem) |
| `PUT /api/social/profile` | Cập nhật opt-in `IsVisibleOnCourt`, `IsReadyToPlay`, `AllowMatchSuggestion`, `SpriteId`, `SkillConfirmed`, `ConfirmedSkillLevel`, `PreferredFreeSlots` |
| `GET /api/social/matches` | Backend lọc hội viên bật `IsVisibleOnCourt && AllowMatchSuggestion && IsActive`, xếp hạng theo trình độ tự khai, lối chơi đơn/đôi và giờ rảnh giao nhau -> trả 2–3 gợi ý kèm lý do từ dữ liệu thật (có thể nhờ Spike Assistant diễn đạt lại câu lý do và tự fallback khi AI không khả dụng) |
| `GET /api/social/invites` | Danh sách lời mời đã gửi và đã nhận của tài khoản đang đăng nhập |
| `POST /api/social/invites` | Gửi lời mời giao lưu (chặn tự mời mình, chặn trùng lời mời `pending`, chặn người nhận bị khóa/ẩn, chặn thời gian quá khứ) |
| `PATCH /api/social/invites/{id}` | Người nhận đồng ý (`accepted`), từ chối (`declined`), hoặc đề xuất giờ khác (`countered`). Sau khi `accepted`, giao diện hiển thị nút **Xem sân trống** mở `/Booking?date=...&time=...` |

---

## 4. Kế hoạch khôi phục (Rollback)

1. Khôi phục DB về migration trước đó:
   ```powershell
   dotnet ef database update <PreviousMigrationName>
   ```
   (Hàm `Down()` của migration chỉ `DROP TABLE tb_SocialInvite` và `DROP TABLE tb_SocialProfile`, hoàn toàn không ảnh hưởng dữ liệu bảng cũ).
2. Revert commit hoặc không merge nhánh `feat/san-giao-luu`.
