# DECISIONS & POLICIES APPLIED — SPIKE PICKLEBALL CLUB

This document records the default operational policies and external integration boundaries applied during the implementation of the 40-item Master Prompt (`Antigravity_Master_Prompt_Spike_Pickleball.md`).

---

## 1. Business Logic & Operational Policies

1. **Cross-Boundary Pricing Policy (`P1-08`)**:
   - **Rule**: Peak hours start at `17:00` (`17:00`–`22:30`). Standard hours are `06:00`–`17:00`.
   - **Calculation**: Every booking is divided into 30-minute slices (`[t, t + 30m)`). Slices before `17:00` are billed at the court's standard rate (`PricePerHour`, default `170,000đ/h`), and slices from `17:00` onward are billed at the peak rate (`PeakPricePerHour`, default `250,000đ/h`).
   - **Example**: A 2-hour booking from `16:30` to `18:30` consists of `0.5h` standard (`85,000đ`) + `1.5h` peak (`375,000đ`) = **`460,000đ`**.

2. **Member Tier & VIP Discount Policy (`P1-09`, `P1-16`)**:
   - **Qualification**: Member tiers are calculated strictly from **actual collected payments** (`PaidAmount` on non-cancelled bookings + paid retail orders), never from unpaid/pending bookings:
     - **Hạng Đồng (Bronze)**: `< 2,000,000đ` paid spend (`0%` court fee discount).
     - **Hạng Bạc (Silver)**: `2,000,000đ – 4,999,999đ` paid spend (`5%` court fee discount).
     - **Hạng Vàng VIP (Gold VIP)**: `>= 5,000,000đ` paid spend (`15%` court fee discount).

3. **Booking Lifecycle & State Machine (`P1-07`)**:
   - `pending` (Chờ duyệt) → `approved` (Đã duyệt / Chờ check-in) → `playing` (Đang thi đấu) → `completed` (Hoàn thành).
   - Terminal/exception states: `cancelled` (Đã hủy), `rejected` (Từ chối), `expired` / `no_show` (Quá hạn không đến).
   - **Crucial Rule**: `approved` bookings do **NOT** automatically transition to `playing` when the wall clock reaches `StartTime`. A booking only enters `playing` when checked in via QR/code at `/CheckIn` or started by Staff/Admin in `/Management`.
   - Active `playing` bookings automatically transition to `completed` once `now >= EndTime`.
   - `pending` or `approved` bookings whose `EndTime` has already passed without check-in are marked `expired`.

4. **Cancellation Cutoff Policy (`P1-12`)**:
   - **Customer / Guest**:
     - Can cancel `pending` bookings at any time prior to `StartTime`.
     - Can cancel `approved` bookings up to **120 minutes (2 hours)** prior to `StartTime`.
     - Within 2 hours of `StartTime`, or once in `playing`/`completed` state, customers must contact the club hotline/counter; only `Admin`/`Staff` can force-cancel.

5. **Payment Separation (`P1-04`, `P1-05`, `P1-06`)**:
   - `AmountDue = CourtFee - DiscountAmount + PosTotal`.
   - `PaidAmount` starts at `0` (`PaymentStatus = "unpaid"`) upon booking creation unless paid via a verified online gateway callback.
   - Revenue reports (`/Reports`, `/Admin`) report **Doanh thu thực thu (Collected Revenue)** from actual `PaidAmount` / `TbPaymentTransaction`, alongside **Công nợ / Tạm tính (Receivables)** so unpaid bookings never inflate collected revenue.

---

## 2. External Provider Credentials & Safe Fallbacks

Per Rule 7 of the Master Prompt (*"Do not invent fake credentials or pretend external services work"*):

1. **Social OAuth (`Google`, `Zalo`, `Apple`) (`P1-02`)**:
   - Configuration keys are structured under `Authentication:Google:ClientId`, `Authentication:Zalo:AppId`, `Authentication:Apple:ClientId`.
   - Because no live OAuth credentials are provided in the environment, the fake `demoToast` buttons on `/Account/Login` are replaced with a clean disabled/unconfigured notice when keys are absent.

2. **Password Reset (`SMS / Email OTP`) (`P1-03`)**:
   - Because no live SMS/SMTP gateway credentials are configured, the insecure client-side fake OTP reset (which allowed resetting any password in JS without verification) has been removed. Instead, users can submit a verified password-reset support request or change their password securely when logged in (`/Account/ChangePassword`).

3. **Online Payment Gateway (`VNPAY`) (`P1-04`)**:
   - Hardcoded test secrets were removed from `appsettings.json`.
   - Counter payment methods (**Tiền mặt tại quầy**, **Chuyển khoản VietQR tại quầy — chờ thu ngân xác nhận**) work end-to-end with `PaymentStatus = "unpaid"` until confirmed by staff in `/Management`.
   - VNPAY online redirect/IPN is wired cleanly via configuration (`VnPay:TmnCode`, `VnPay:HashSecret`) and only activates when live/sandbox merchant credentials are provided in environment variables.
