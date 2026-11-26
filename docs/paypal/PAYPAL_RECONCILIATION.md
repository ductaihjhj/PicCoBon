# PayPal Sandbox Reconciliation & Compensation Runbook — Spike OS

## 1. Three-Layer Reconciliation Architecture

Spike OS guarantees financial consistency across SQL Server and PayPal without holding SQL locks over external network calls:

1. **Immediate Buyer Return / Self-Service Reconcile**
   - `GET /Payment/PayPalReturn` performs zero DB mutations and invokes CSRF-protected `POST /api/payments/paypal/capture` (or manual `"Kiểm tra & Đối soát lại ngay"` via `POST /api/payments/paypal/reconcile`).
   - Always queries `GET /v2/checkout/orders/{id}` BEFORE calling `/capture`:
     - Calls `/capture` ONLY when `Order.Status == "APPROVED"`.
     - Rejects `PAYER_ACTION_REQUIRED` / `CREATED` with `409 PAYPAL_APPROVAL_REQUIRED`.
     - Recovers `COMPLETED` without calling `/capture` a second time.
2. **Background Worker Sweep (`PayPalReconciliationWorker`)**
   - Runs periodically every `60s` (`ReconcileStaleAttemptsAsync`) scanning a bounded batch (`20` rows) in `AmbiguousTimeout`, `CapturedCompensationPending`, `CompensationRefundRequired`, stale `Capturing` (`> 2m`), and aged `AwaitingApproval` (`> 3m` or expired).
3. **Cryptographically Verified PayPal Webhooks**
   - `POST /api/webhooks/paypal` verifies signatures via `POST /v1/notifications/verify-webhook-signature` (rejects unverified payloads with `401`).
   - Processes `CHECKOUT.ORDER.APPROVED`, `PAYMENT.CAPTURE.COMPLETED`, and compensation events idempotently.

## 2. State Machine & Manual Operations

| `LocalStatus` | Meaning | Resolution |
|---|---|---|
| `Creating` | Short Phase A lease while calling PayPal CreateOrder | Auto-resolves or transitions to `CreateFailed` |
| `AwaitingApproval` | Buyer redirected to PayPal Sandbox; no funds captured | Buyer finishes/cancels, or worker marks `ExpiredBeforeCapture` |
| `Capturing` | Exclusive capture lease held during external `/capture` call | Concurrent captures blocked (`409`); worker cleans stuck leases `> 2m` |
| `Completed` | PayPal `COMPLETED` verified + `TbPaymentTransaction` receipt posted | Terminal paid state; refundable by Admin |
| `AmbiguousTimeout` | Network timeout or transient error during capture | Worker / buyer / Admin triggers `Reconcile` (`GetOrderAsync`) |
| `CapturedCompensationPending` | PayPal captured AFTER hold expired or target settled concurrently; receipt + Capture ID saved | Executes compensation refund outside SQL TX |
| `CompensationRefundRequired` | Compensation refund failed transiently; **capture evidence retained** | Retried automatically by `PayPalReconciliationWorker` & Admin |
| `RefundedAfterExpiry` | Compensation refund completed + offsetting `refund` ledger entry posted (`NetPaid = 0`) | Terminal compensated state |
| `Refunded` | Admin issued refund + offsetting `refund` ledger entry posted | Terminal refunded state |

## 3. Admin Dashboard Procedures (`/Admin/Home/Reports`)

- Open **`/Admin/Home/Reports`** → scroll to **PayPal Sandbox — Theo Dõi & Đối Soát Giao Dịch**.
- Any unsettled/ambiguous transactions trigger a warning banner showing **`Cần đối soát: N`**.
- **Đối soát**: Queries PayPal `/v2/checkout/orders/{id}` and safely posts any missing receipt or completes pending compensation refunds.
- **Hoàn tiền PayPal**: Executes `POST /v2/payments/captures/{captureId}/refund` and posts an immutable `TransactionType = "refund"`, `PaymentMethod = "paypal"` counter-entry in `tb_PaymentTransaction`. Never delete ledger rows.
