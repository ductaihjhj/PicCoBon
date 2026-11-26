# Sân Giao Lưu — Hướng Dẫn Nhân Vật Pixel, Sprite Atlas & Hoạt Ảnh (V6)

Tài liệu này ghi chú cấu trúc tài nguyên hình ảnh, quy ước lưới hoạt ảnh (`8×4` multi-row atlas), cơ chế đồng bộ ảnh đại diện từ `/Account/Profile` và bộ máy dựng cảnh trên trang `/SanGiaoLuu`.

---

## 1. Kiến Trúc Kết Nối Tệp Mã Nguồn

| Thành phần | Đường dẫn | Vai trò |
| --- | --- | --- |
| **Controller & REST APIs** | `Controllers/SanGiaoLuuController.cs` | Phục vụ trang `GET /SanGiaoLuu` và nhóm API `/api/social/*` (chỉ dành cho `Customer`, chặn `Admin`/`Staff`, bảo vệ `ValidateAntiForgeryToken`). |
| **Domain Service** | `Services/SocialCourtService.cs` | Đồng bộ `AvatarUrl` ↔ `SpriteId`, lọc bỏ PII (`Email`, `Phone`, `PasswordHash`), chấm điểm gợi ý bạn tập và quản lý lời mời (`TbSocialInvite`). |
| **Database Entities** | `Models/TbSocialProfile.cs`, `Models/TbSocialInvite.cs` | Lưu hồ sơ giao lưu của hội viên và trạng thái lời mời (`pending`, `accepted`, `declined`, `cancelled`). |
| **Razor View** | `Views/SanGiaoLuu/Index.cshtml` | Giao diện chính của Sân giao lưu, danh sách hội viên, hộp thoại Thẻ hồ sơ, Ghép cặp và Cài đặt giao lưu. |
| **Page Controller JS** | `wwwroot/js/san-giao-luu-page.js` | Nối API `/api/social/*` với canvas sân, quản lý sự kiện tương tác hội viên, lọc danh sách và modal. |
| **Pixel Court Engine JS** | `wwwroot/js/san-giao-luu-engine.js` | Vẽ sân Pickleball 2D (`courtMapCanvas`), điều hướng nhân vật tự động giữa 3 khu vực (`court-left`, `bench-lounge`, `court-right`), tránh vùng lưới và phát hoạt ảnh tự nhiên. |
| **Stylesheet** | `wwwroot/css/pages/san-giao-luu.css` | Bố cục sân tương tác, thẻ nhân vật trên sân (`court-actor`), Thẻ hồ sơ hội viên và giao diện đáp ứng đa màn hình. |
| **Sprite Assets (Runtime)** | `wwwroot/assets/san-giao-luu/` | Chứa `manifest.json` và bộ 4 tệp cho mỗi nhân vật: `<id>-atlas.png`, `<id>-animation.json`, `<id>-master.png`, `<id>-profile.webp`. |
| **Preset Avatars** | `wwwroot/images/avatars/` | Ảnh đại diện chọn sẵn trong `/Account/Profile` (`sunny.png` .. `kai.png` và các bản kế thừa `avatar-01.svg` .. `avatar-08.svg`). |
| **Bản mẫu độc lập** | `prototypes/san-giao-luu/` | Bản HTML thử nghiệm độc lập ban đầu để đối chiếu thiết kế và kiểm tra nhanh sprite ngoài ứng dụng. |

---

## 2. Danh Sách 8 Nhân Vật Pixel & Quy Tắc Đồng Bộ Avatar

Hệ thống hỗ trợ 8 nhân vật gốc (mỗi hội viên chọn cùng kiểu nhân vật vẫn có `Actor` và `AccountId` độc lập trên sân):

| Mã `SpriteId` | Tên hiển thị | Phong cách nhận diện | 4 động tác cá tính riêng (Signature Actions) |
| --- | --- | --- | --- |
| `sunny` | **Sunny** | Tóc đuôi ngựa năng động, băng đô thể thao | `sunny_fix_band`, `sunny_ball_toss`, `sunny_peace_sign`, `sunny_pony_adjust` |
| `ace` | **Ace** | Đội mũ lưỡi trai, phong thái điềm tĩnh tự tin | `ace_cap_tip`, `ace_serve`, `ace_hold_pose`, `ace_regrip` |
| `mochi` | **Mochi** | Mũ gấu trúc dễ thương, nhịp đi thong thả | `mochi_peekaboo`, `mochi_water_break`, `mochi_yawn`, `mochi_shy_nod` |
| `coco` | **Coco** | Tai thỏ nhanh nhẹn, phản xạ trên lưới | `coco_ear_perk`, `coco_dink`, `coco_look_back`, `coco_quick_salute` |
| `maya` | **Maya** | VĐV hai búi tóc cân bằng, kỹ thuật chuẩn | `maya_paddle_twirl`, `maya_fist_pump`, `maya_backhand_ready`, `maya_shoulder_stretch` |
| `leo` | **Leo** | Đeo kính chiến thuật, điều phối lối chơi | `leo_fix_glasses`, `leo_call_play`, `leo_approve`, `leo_clipboard` |
| `kiko` | **Kiko** | Mũ/tai mèo tinh nghịch, bước chân linh hoạt | `kiko_paw_tap`, `kiko_cat_stretch`, `kiko_underhand_lob`, `kiko_tail_swish` |
| `kai` | **Kai** | Băng trán thể lực mạnh mẽ, chuyên đánh cuối sân | `kai_headband_tighten`, `kai_power_smash`, `kai_warm_knees`, `kai_ready_split` |

### Quy tắc ánh xạ từ `/Account/Profile`:
1. Khi hội viên chọn 1 trong 8 avatar có sẵn (`/images/avatars/<id>.png`), `SocialCourtService.ResolveSpriteSelection` tự động gán đúng `SpriteId` tương ứng và `AvatarMode = "preset-sprite"`.
2. Khi hội viên tải ảnh cá nhân lên (`/uploads/avatars/...`), hệ thống giữ ảnh cá nhân cho Thẻ hồ sơ & huy hiệu đầu nhân vật (`AvatarMode = "custom-upload"`), đồng thời gán một thân sprite pixel ổn định để nhân vật vẫn di chuyển mượt trên sân.

---

## 3. Quy Ước Kỹ Thuật Của Sprite Atlas (`v4`)

Để tránh lỗi giật hình, trượt chân hoặc nhấp nháy do lệch bộ nhớ đệm, mọi chỉnh sửa sprite trong `wwwroot/assets/san-giao-luu/` cần tuân thủ đúng thông số sau:

- **Kích thước ô khung (`frameSize`)**: `96 × 104 px`.
- **Điểm neo bàn chân (`anchor`)**: `[48, 100]` (căn giữa theo trục ngang thân trên/đầu, gót chân chạm đúng `y = 100`).
- ** Bố cục lưới Atlas (`grid`)**: `8 cột × 4 hàng` = **32 ô** (`768 × 416 px`, nền trong suốt RGBA cứng):
  - **Hàng 0 (`0..7`)**: Đứng nghỉ (`idle`), chớp 2 mắt (`blink`), nháy 1 mắt (`wink`) và các khung chuyển tiếp ổn định.
  - **Hàng 1 (`8..15`)**: Chu kỳ bước đi chân luân phiên thật (`walk`, sử dụng 4 tư thế tách chân rõ rệt `[8, 10, 11, 13]` tính theo quãng đường di chuyển thực tế trên sân).
  - **Hàng 2 (`16..23`)**: Vẫy tay chào (`wave`) và vung vợt (`swing`).
  - **Hàng 3 (`24..31`)**: Các khung tư thế riêng (`signature actions`) của từng nhân vật.
- **Quy tắc ghép mắt (`blink` / `wink`)**: Chỉ thay vùng cửa sổ mắt đã căn chỉnh đầu lên khung gốc `idle_0`, tuyệt đối không thay cả thân từ khung khác để tránh dịch chuyển kính, mũ hoặc bàn chân.
- **Cache-busting**: Khi cập nhật tệp `*-atlas.png`, `*-animation.json` hoặc `manifest.json`, luôn tăng hậu tố phiên bản `assetVersion` (hiện tại là `?v=sgl-v4`) trong `manifest.json` và `wwwroot/js/san-giao-luu-engine.js`.
