/* ==========================================================================
   SPIKE PICKLEBALL CLUB — LEDGER-DRIVEN FINANCIAL REPORTS CONTROLLER (P1-06)
   Queries GET /api/admin/reports/summary filtered by actual payment CreatedAt
   ========================================================================== */

async function renderReports() {
  if (!requireRole('admin')) return;

  const fromEl = document.getElementById('reportFrom');
  const toEl = document.getElementById('reportTo');
  if (fromEl && !fromEl.value) {
    const now = new Date();
    const first = new Date(now.getFullYear(), now.getMonth(), 1);
    const y = first.getFullYear();
    const m = String(first.getMonth() + 1).padStart(2, '0');
    fromEl.value = `${y}-${m}-01`;
  }
  if (toEl && !toEl.value) {
    toEl.value = getTodayISO();
  }

  const fromDate = fromEl?.value || '';
  const toDate = toEl?.value || '';

  try {
    const qs = new URLSearchParams();
    if (fromDate) qs.set('dateFrom', fromDate);
    if (toDate) qs.set('dateTo', toDate);

    const res = await fetch(`/api/admin/reports/summary?${qs.toString()}`, {
      headers: { 'Accept': 'application/json' },
      credentials: 'same-origin',
      cache: 'no-store'
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      showToast('Không thể tải báo cáo sổ cái', data.error || 'Vui lòng thử lại.', 'error');
      return;
    }

    renderReportKpis(data);
    renderReportCourts(data.courtBreakdown || []);
    renderReportTopMembers(data.topMembers || []);
    renderReportLedgerRows(data.recentTransactions || []);
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể tải dữ liệu báo cáo doanh thu từ máy chủ.', 'error');
  }
}

function renderReportKpis(d) {
  const statsEl = document.getElementById('reportStats');
  if (!statsEl) return;

  statsEl.innerHTML = `
    <div class="stat-widget">
      <div class="stat-widget-top">
        <span class="stat-label">Tổng thực thu ròng (Sổ cái)</span>
        <div class="stat-icon" style="background:var(--color-primary-50);color:var(--color-primary)"><iconify-icon icon="lucide:trending-up"></iconify-icon></div>
      </div>
      <div class="stat-value primary text-mono">${formatMoney(d.totalCollectedRevenue)}</div>
      <div class="stat-detail">Tiền mặt: ${formatMoney(d.cashNetCollected)} · CK: ${formatMoney(d.transferNetCollected)}</div>
    </div>
    <div class="stat-widget">
      <div class="stat-widget-top">
        <span class="stat-label">Thực thu Sân vs Đơn lẻ Shop</span>
        <div class="stat-icon" style="background:var(--color-indigo-50);color:var(--color-indigo)"><iconify-icon icon="lucide:layers"></iconify-icon></div>
      </div>
      <div class="stat-value indigo text-mono">${formatMoney(d.collectedBookingRevenue)}</div>
      <div class="stat-detail">Đơn lẻ Pro Shop: <strong>${formatMoney(d.standaloneRetailRevenue)}</strong></div>
    </div>
    <div class="stat-widget">
      <div class="stat-widget-top">
        <span class="stat-label">Công nợ chưa thu (Receivables)</span>
        <div class="stat-icon" style="background:var(--color-warning-light);color:#B45309"><iconify-icon icon="lucide:clock"></iconify-icon></div>
      </div>
      <div class="stat-value warning text-mono">${formatMoney(d.unpaidReceivables)}</div>
      <div class="stat-detail">Sân: ${formatMoney(d.unpaidBookingReceivables)} · Shop lẻ: ${formatMoney(d.unpaidShopReceivables)}</div>
    </div>
    <div class="stat-widget">
      <div class="stat-widget-top">
        <span class="stat-label">Tổng hoàn tiền (Refunds)</span>
        <div class="stat-icon" style="background:#FEE2E2;color:#DC2626"><iconify-icon icon="lucide:rotate-ccw"></iconify-icon></div>
      </div>
      <div class="stat-value text-mono" style="color:#DC2626">${formatMoney(d.totalRefunds)}</div>
      <div class="stat-detail">Đã khấu trừ khỏi Tổng thực thu ròng</div>
    </div>
  `;
}

function renderReportCourts(courts) {
  const chartEl = document.getElementById('reportChart');
  if (!chartEl) return;

  if (!courts.length) {
    chartEl.innerHTML = `<div class="text-xs text-muted">Chưa có dữ liệu sân thi đấu.</div>`;
    return;
  }

  const maxVal = Math.max(...courts.map(c => Math.max(Number(c.collectedRevenue) || 0, Number(c.unpaidReceivable) || 0)), 100000);
  chartEl.innerHTML = courts.map(c => {
    const collected = Math.max(0, Number(c.collectedRevenue) || 0);
    const pct = Math.max(10, Math.round((collected / maxVal) * 100));
    return `
      <div class="chart-col">
        <div class="chart-bar" style="height:${pct}%;max-width:56px" title="Thực thu: ${formatMoney(collected)} · Công nợ: ${formatMoney(c.unpaidReceivable)}">
          <span class="chart-bar-value">${Math.round(collected / 1000)}k</span>
        </div>
        <span class="chart-label">${escapeHtml(c.courtName)}</span>
        <span class="text-xs text-muted">${Number(c.sessionsCount)} ca (${Number(c.hoursBooked)}h) · Nợ: ${Math.round((Number(c.unpaidReceivable) || 0) / 1000)}k</span>
      </div>
    `;
  }).join('');
}

function renderReportTopMembers(vips) {
  const vipEl = document.getElementById('vipList');
  if (!vipEl) return;

  if (!vips.length) {
    vipEl.innerHTML = `<div class="text-xs text-muted" style="padding:1rem 0;">Chưa có hội viên xác thực nào phát sinh thực thu trong kỳ lọc này.</div>`;
    return;
  }

  const ranks = ['gold', 'silver', 'bronze', 'normal', 'normal', 'normal', 'normal', 'normal'];
  vipEl.innerHTML = vips.map((v, i) => `
    <div class="vip-item">
      <div class="vip-rank ${ranks[i] || 'normal'}">${i + 1}</div>
      <div style="flex:1;min-width:0">
        <div class="font-bold text-sm truncate">${escapeHtml(v.name)}</div>
        <div class="text-xs text-muted text-mono">${escapeHtml(v.phone)} · ${escapeHtml(v.memberTier)}</div>
      </div>
      <div style="text-align:right;">
        <div class="font-bold text-mono" style="color:var(--color-primary)">${formatMoney(v.netPaidInRange)}</div>
        <div class="text-xs text-muted">Thực thu ròng</div>
      </div>
    </div>
  `).join('');
}

function renderReportLedgerRows(rows) {
  const badge = document.getElementById('reportTxCountBadge');
  const tbody = document.getElementById('reportLedgerTableBody');
  if (badge) badge.textContent = `${rows.length} bút toán`;
  if (!tbody) return;

  if (!rows.length) {
    tbody.innerHTML = `<tr><td colspan="7" style="text-align:center;padding:1.75rem;color:var(--text-muted);">Chưa có bút toán thu / hoàn tiền nào trong khoảng ngày đã chọn.</td></tr>`;
    return;
  }

  tbody.innerHTML = rows.map(r => {
    const isRefund = String(r.type).toLowerCase() === 'refund' || Number(r.amount) < 0;
    const badgeCls = isRefund ? 'badge-danger' : 'badge-success';
    const typeLbl = isRefund ? 'Hoàn tiền' : 'Thu tiền';
    const methodLbl = r.method === 'transfer' ? 'Chuyển khoản' : 'Tiền mặt';
    return `
      <tr>
        <td class="text-mono text-xs">${escapeHtml(r.createdAt)}</td>
        <td><span class="badge ${badgeCls}">${typeLbl}</span></td>
        <td class="text-mono font-bold" style="color:var(--color-indigo)">${escapeHtml(r.targetCode)}</td>
        <td>${escapeHtml(r.customerName)}</td>
        <td class="text-xs">
          <strong>${methodLbl}</strong>
          ${r.referenceCode ? `<span class="text-mono text-muted"> · Ref: ${escapeHtml(r.referenceCode)}</span>` : ''}
          ${r.note ? `<div class="text-muted">${escapeHtml(r.note)}</div>` : ''}
        </td>
        <td class="text-mono font-bold" style="color:${isRefund ? 'var(--color-danger)' : 'var(--color-success)'}">
          ${isRefund ? '−' : '+'}${formatMoney(Math.abs(Number(r.amount) || 0))}
        </td>
        <td class="text-xs">${escapeHtml(r.recordedBy)}</td>
      </tr>
    `;
  }).join('');
}

async function filterReport() {
  await renderReports();
  showToast('Đã cập nhật sổ cái', 'Số liệu doanh thu thực thu, hoàn tiền và công nợ đã được tải từ máy chủ.', 'success');
}

document.addEventListener('DOMContentLoaded', renderReports);
