# Sân giao lưu · Đủ bộ 8 nhân vật Pixel

## 1. Cách chạy bản thử
- **Bản mẫu gốc Maya (offline 1 file):** Mở `prototypes/san-giao-luu/San_Giao_Luu_Maya.html`.
- **Bản đầy đủ 8 nhân vật (9 hội viên mẫu):** Chạy máy chủ web cục bộ (hoặc từ ứng dụng Spike) và mở `prototypes/san-giao-luu/San_Giao_Luu_8_Nhan_Vat.html`.

## 2. Bộ 8 nhân vật & Asset bàn giao

Tất cả tài sản đã được xuất đồng bộ vào `wwwroot/assets/san-giao-luu/` và `prototypes/san-giao-luu/assets/`:

| Nhân vật | Dấu hiệu nhận dạng | Kích thước khung | Mốc chân (`anchor`) |
| --- | --- | --- | --- |
| **Maya** | Hai búi tóc, kẹp xanh, áo thể thao viền chanh | `83 × 93 px` | `(41, 91)` |
| **Sunny** | Băng đô xanh có nét vàng, tóc đuôi ngựa | `96 × 104 px` | `(48, 101)` |
| **Ace** | Mũ lưỡi trai xanh có họa tiết hoa kem | `96 × 104 px` | `(48, 101)` |
| **Mochi** | Mũ gấu trúc và áo khoác xanh | `96 × 104 px` | `(48, 101)` |
| **Coco** | Tai cáo nhọn và mái tóc cam | `96 × 104 px` | `(48, 101)` |
| **Leo** | Kính tròn gọng đen và tóc ngắn | `96 × 104 px` | `(48, 101)` |
| **Kiko** | Mặt mèo tam thể, tai nhọn và mảng lông cam | `96 × 104 px` | `(48, 101)` |
| **Kai** | Tóc dựng và băng đô răng cưa màu chanh | `96 × 104 px` | `(48, 101)` |

- Quy trình dựng master: sử dụng **Agent Sprite Forge** (`25203d0`) tuyến **Pixel Looks** (`pixel_look.py ask-cast` + `cast` đứng cạnh `maya-master.png` trên nền `#FF00FF`, kiểm tra độ cao & đường mặt đất bằng `pixel_look.py lineup`).
- Quy trình động tác:
  - `idle`: sinh trực tiếp 2 khung thở bằng `pixel_motion.py breath`.
  - `walk`, `wave`, `swing`: do môi trường chưa kết nối nguồn image-to-video clip (và image API giới hạn quota 429), 7 nhân vật mới sử dụng bộ 4 khung bước đi + tư thế vẫy tay + tư thế vung vợt dựng từ các lớp pixel thật của từng master sprite (nearest-neighbor, giữ nguyên 100% đầu, phụ kiện, màu áo và đường viền 1px). Có thể nâng cấp sang clip I2V sau mà không phải đổi cấu trúc `manifest.json` hay engine.

## 3. Tích hợp hồ sơ thật & Đề xuất Schema/API
Xem chi tiết tại `docs/SAN_GIAO_LUU_SCHEMA_API_PROPOSAL.md` để chủ dự án duyệt trước khi chỉnh sửa cơ sở dữ liệu và API theo `AGENTS.md`.
