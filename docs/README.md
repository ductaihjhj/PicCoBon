# 📚 Mục Lục Tài Liệu Kỹ Thuật — Spike Pickleball Club

Thư mục `docs/` chỉ giữ lại các tài liệu kiến trúc, cấu hình và vận hành đang sử dụng thực tế (toàn bộ đề bài cũ, kế hoạch triển khai, nhật ký sửa lỗi và báo cáo QA đã hoàn thành đã được dọn sạch).

---

## 1. Tài Liệu Cốt Lõi

| Tài liệu | Nội dung |
| --- | --- |
| [`../README.md`](../README.md) | Tổng quan hệ thống, hướng dẫn cài đặt nhanh, chạy migration `--migrate` và kiểm thử. |
| [`ARCHITECTURE_AND_MODULES.md`](ARCHITECTURE_AND_MODULES.md) | **Sổ tay Kiến trúc & Bản đồ Mã nguồn**: giải thích vai trò của từng thư mục, `Controller`, `Service`, bảng SQL Server (`tb_*`), CSS/JS frontend và bộ kiểm thử. |
| [`DEPLOY_AND_ROLLBACK.md`](DEPLOY_AND_ROLLBACK.md) | Quy trình sao lưu SQL Server, triển khai Production, kiểm tra `/health` và kịch bản khôi phục (Rollback). |

---

## 2. Hướng Dẫn Cấu Hình & Quy Chuẩn Theo Phân Hệ

- **Sân Giao Lưu & Nhân Vật Pixel (`V6`)**:
  - [`san-giao-luu/SPRITE_AND_ANIMATION_GUIDE.md`](san-giao-luu/SPRITE_AND_ANIMATION_GUIDE.md) — Quy chuẩn 8 nhân vật pixel, lưới sprite `8×4` (`96×104`), điểm neo bàn chân và đồng bộ avatar từ `/Account/Profile`.
- **Trợ Lý Ảo Spike AI Mascot (`Google Gemini`)**:
  - [`ai-assistant/SPIKE_AI_CONFIGURATION.md`](ai-assistant/SPIKE_AI_CONFIGURATION.md) — Cấu hình khóa API `GeminiOptions`, giới hạn tốc độ và chế độ dự phòng.
  - [`ai-assistant/SPIKE_AI_ARCHITECTURE.md`](ai-assistant/SPIKE_AI_ARCHITECTURE.md) — Luồng xử lý hội thoại và công cụ đọc dữ liệu nội bộ (`SpikeAssistantToolService`).
- **Cổng Thanh Toán Quốc Tế PayPal (`V5`)**:
  - [`paypal/PAYPAL_CONFIGURATION_GUIDE.md`](paypal/PAYPAL_CONFIGURATION_GUIDE.md) — Cấu hình `ClientId`, `ClientSecret`, tỷ giá quy đổi và môi trường Sandbox/Live.
  - [`paypal/PAYPAL_RECONCILIATION.md`](paypal/PAYPAL_RECONCILIATION.md) — Cơ chế đối soát giao dịch nền (`PayPalReconciliationWorker`) với sổ cái `tb_PaymentTransaction`.
- **Hệ Thống Giao Diện & Nhật Ký Hệ Thống**:
  - [`design-system/DESIGN_SYSTEM.md`](design-system/DESIGN_SYSTEM.md) — Hệ thống biến màu, font chữ và quy chuẩn bố cục.
  - [`logging/LOGGING_CONFIGURATION.md`](logging/LOGGING_CONFIGURATION.md) — Cấu hình định dạng log console gọn sạch và chế độ chẩn đoán `SqlDebug`.
- **Đầu Ra Kiểm Thử Tự Động**:
  - `qa/` — Thư mục xuất kết quả kiểm thử mã vé QR tự động của `V4ReleaseBlockersAndQrTests.cs` (giữ nguyên đường dẫn).
