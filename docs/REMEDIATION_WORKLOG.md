# REMEDIATION WORKLOG — Spike Pickleball Club (Master Prompt V2)

## Checkpoint 0 — Baseline, Environment Verification & Snapshot (`2026-11-09T00:26+07:00`)
- **Completed**:
  - Read `Antigravity_Remediation_Prompt_Spike_Pickleball_v2.md`, `Antigravity_Master_Prompt_Spike_Pickleball.md`, `AGENTS.md`, `.agent/skills/frontend-design/SKILL.md`, and audited all Controllers, Services, Models, Razor Views, ViewComponents, and JS files.
  - Verified `.NET 10.0.402`, SQL Server Express (`.\SQLEXPRESS`), Node.js, and Chrome/Edge headless availability.
  - Created `docs/REMEDIATION_BASELINE.md` and `docs/REMEDIATION_MATRIX.md` with all **40 locked original IDs** (`P0-01`..`P0-12`, `P1-01`..`P1-18`, `P2-01`..`P2-10`).
- **Next Action**:
  - Commit pre-V2 baseline snapshot on `fix/audit-40-items` and create working branch `fix/remediation-v2`.
  - Execute **Batch 1 (Release Blockers)**: Schema/Entities + EF Core Migration (`RowVersion`, `AccessTokenHash`, `TransactionType`, `IdempotencyKey`, `MustChangePassword`), Payment Ledger vs Session Finish separation, Standalone Shop Order settlement/refund/restock, SQL Server atomic transactions (`UPDLOCK, HOLDLOCK` + atomic stock `UPDATE`), Global CSRF protection, Ownership (`BookingAccessToken` / `AccountId`), Fail-fast secrets, Secure cookies, Remove default `"123456"` password, Remove `localStorage` PII, Fix `@Html.Raw(p.Icon)` XSS.
