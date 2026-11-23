# Sân giao lưu · Bản thử Maya

Mở `San_Giao_Luu_Maya.html` bằng trình duyệt. File tự chứa ảnh và chạy offline. Maya tự diễn hoạt hoặc có thể thử lần lượt bốn nút dưới sân. Chạm vào nhân vật hoặc chọn Maya trong danh sách để mở hồ sơ mẫu.

## Asset bàn giao

| Tệp | Nội dung |
| --- | --- |
| `assets/maya-master.png` | Sprite gốc trong suốt, 51 × 89 px |
| `assets/maya-atlas.png` | 8 khung, 83 × 93 px mỗi khung, xếp ngang |
| `assets/maya-animation.json` | Khung và thời lượng, điểm neo chân tại (41, 91) |
| `assets/maya-profile.webp` | Ảnh chân dung lấy từ ZIP người dùng cung cấp |

Nguồn thiết kế là `maya.png` trong `8_avatar_comic_1x1(1).zip`. Mẫu toàn thân giữ hai búi tóc, kẹp và hoa tai xanh, áo xanh viền chanh. Phần bổ sung gồm váy quần kem, giày sân trắng và vợt xanh kem.

Tạo mẫu toàn thân và sáu tư thế trung gian bằng sinh ảnh từ tham chiếu. Dùng [Agent Sprite Forge](https://github.com/0x0funky/agent-sprite-forge/tree/25203d03900b46b444bdd72a75768c33d1895ad6) tại commit `25203d0` để đọc lưới, tách nền, rút bảng màu và tạo hai khung đứng bằng `pixel_motion.py breath`. Căn các tư thế đi, vẫy tay và vung vợt theo mốc chân trước khi ghép atlas. Vẫy tay và vung vợt là các động tác minh họa ngắn từ tư thế tĩnh. Bản đi sang hướng đối diện dùng cùng các khung được lật ngang.

## Kiểm tra trước khi mở rộng

- Duyệt Maya ở kích thước hiển thị trong sân. Xem búi tóc, mặt, vợt và chân có đủ dễ nhận ra khi xem trên điện thoại.
- Kiểm tra chạm trực tiếp vào nhân vật, mở thẻ hồ sơ từ danh sách và đóng bằng nút hoặc phím Escape.
- Khi chốt tạo hình, dùng Maya làm chuẩn tỉ lệ cho Sunny, Ace, Mochi, Coco, Leo, Kiko và Kai. Tạo từng sprite cạnh mẫu Maya và so chúng trên cùng mặt đất.
- Giữ nguyên phạm vi bảng mẫu trong bản thử. Phân quyền hội viên và nội dung hồ sơ chia sẻ sẽ dùng dữ liệu thật khi tích hợp.
