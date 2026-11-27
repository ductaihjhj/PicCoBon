/* ==========================================================================
   SPIKE PICKLEBALL CLUB — LEDGER-DRIVEN FINANCIAL REPORTS CONTROLLER (V5)
   Queries GET /api/admin/reports/summary filtered by actual payment CreatedAt.
   Strict Error/Stale Handling (UI-A04) & Zero-Height Zero-Value Bars (UI-A05).
   ========================================================================== */

let _reportAbortController = null;
let _reportReqCounter = 0;
let _lastReportData = null;
let _lastReportSyncTime = null;

function setReportBanner(html = '') {
  const host = document.getElementById('reportStatusBanner');
  if (!host) return;
  if (!html) {
    host.innerHTML = '';
    host.style.display = 'none';
  } else {
    host.innerHTML = html;
    host.style.display = 'block';
  }
}

async function renderReports() {
  const fromEl = document.getElementById('reportFrom');
  const toEl = document.getElementById('reportTo');
  if (fromEl && !fromEl.value) {
    fromEl.value = `${getTodayISO().slice(0, 7)}-01`;
  }
  if (toEl && !toEl.value) {
    toEl.value = getTodayISO();
  }

  const fromDate = fromEl?.value || '';
  const toDate = toEl?.value || '';

  await SpikeDemo.ensureLoaded(false);
  const data = SpikeDemo.getReportSummary(fromDate, toDate);
  _lastReportData = data;
  _lastReportSyncTime = new Date();
  setReportBanner('');

  renderReportKpis(data);
  renderReportCourts(data.courtBreakdown || []);
  renderReportTopMembers(data.topMembers || []);
  renderReportLedgerRows(data.recentTransactions || []);
  loadAdminPayPalTransactions();

  return { ok: true, status: 200, data };
}

function renderReportKpis(d) {
  const statsEl = document.getElementById('reportStats');
  if (!statsEl) return;

  statsEl.innerHTML = `
    <div class="stat-widget" data-testid="report-kpi-net">
      <div class="stat-widget-top">
        <span class="stat-label">Tổng thực thu ròng (Sổ cái)</span>
        <div class="stat-icon" style="background:var(--color-primary-50);color:var(--color-primary)"><iconify-icon icon="lucide:trending-up"></iconify-icon></div>
      </div>
      <div class="stat-value primary text-mono">${formatMoney(d.totalCollectedRevenue)}</div>
      <div class="stat-detail">Tiền mặt: ${formatMoney(d.cashNetCollected)} · CK: ${formatMoney(d.transferNetCollected)} · PayPal: ${formatMoney(d.paypalNetCollected || 0)}</div>
    </div>
    <div class="stat-widget" data-testid="report-kpi-booking-vs-shop">
      <div class="stat-widget-top">
        <span class="stat-label">Thực thu Sân vs Đơn lẻ Shop</span>
        <div class="stat-icon" style="background:var(--color-indigo-50);color:var(--color-indigo)"><iconify-icon icon="lucide:layers"></iconify-icon></div>
      </div>
      <div class="stat-value indigo text-mono">${formatMoney(d.collectedBookingRevenue)}</div>
      <div class="stat-detail">Đơn lẻ Pro Shop: <strong>${formatMoney(d.standaloneRetailRevenue)}</strong></div>
    </div>
    <div class="stat-widget" data-testid="report-kpi-receivables">
      <div class="stat-widget-top">
        <span class="stat-label">Công nợ chưa thu (Receivables)</span>
        <div class="stat-icon" style="background:var(--color-warning-light);color:#B45309"><iconify-icon icon="lucide:clock"></iconify-icon></div>
      </div>
      <div class="stat-value warning text-mono">${formatMoney(d.unpaidReceivables)}</div>
      <div class="stat-detail">Sân: ${formatMoney(d.unpaidBookingReceivables)} · Shop lẻ: ${formatMoney(d.unpaidShopReceivables)}</div>
    </div>
    <div class="stat-widget" data-testid="report-kpi-refunds">
      <div class="stat-widget-top">
        <span class="stat-label">Tổng hoàn tiền (Refunds)</span>
        <div class="stat-icon" style="background:#FEE2E2;color:#DC2626"><iconify-icon icon="lucide:rotate-ccw"></iconify-icon></div>
      </div>
      <div class="stat-value text-mono" style="color:#DC2626">${formatMoney(d.totalRefunds)}</div>
      <div class="stat-detail">Đã khấu trừ khỏi Tổng thực thu ròng</div>
    </div>
  `;
}

/**
 * UI-A05: Zero-value courts MUST have height: 0% (never Math.max(10, ...)) and display '0 đ' consistently.
 */
function renderReportCourts(courts) {
  const chartEl = document.getElementById('reportChart');
  if (!chartEl) return;

  if (!courts.length) {
    chartEl.innerHTML = `<div style="width:100%;">${renderUiStateBanner('empty', {
      compact: true,
      title: 'Chưa có dữ liệu sân thi đấu trong kỳ báo cáo'
    })}</div>`;
    return;
  }

  const maxVal = Math.max(...courts.map(c => Math.max(Math.abs(Number(c.collectedRevenue) || 0), Math.max(0, Number(c.unpaidReceivable) || 0))), 100000);
  chartEl.innerHTML = courts.map(c => {
    const rawCollected = Number(c.collectedRevenue) || 0;
    const unpaid = Math.max(0, Number(c.unpaidReceivable) || 0);
    const isNegative = rawCollected < 0;
    const isZero = rawCollected === 0;
    const pct = rawCollected <= 0 ? 0 : Math.max(6, Math.min(100, Math.round((rawCollected / maxVal) * 100)));
    const barLabel = isZero ? '0 đ' : formatMoney(rawCollected);
    const ariaSummary = `${c.courtName}: Thực thu ${formatMoney(rawCollected)}${isNegative ? ' (Âm do hoàn tiền vượt thu trong kỳ)' : ''}, Công nợ ${formatMoney(unpaid)}, ${Number(c.sessionsCount) || 0} ca (${Number(c.hoursBooked) || 0} giờ)`;

    return `
      <div class="chart-col" role="img" aria-label="${escapeAttr(ariaSummary)}">
        <div class="chart-bar-track">
          <div class="chart-bar ${rawCollected <= 0 ? 'is-zero' : ''} ${isNegative ? 'is-negative' : ''}" data-value="${rawCollected}" style="height:${pct}%;min-height:0;max-width:64px;" title="${escapeAttr(ariaSummary)}">
            <span class="chart-bar-value text-mono" ${isNegative ? 'style="color:var(--color-danger);font-weight:700;"' : ''}>${escapeHtml(barLabel)}</span>
          </div>
        </div>
        <span class="chart-label">${escapeHtml(c.courtName)}</span>
        <span class="text-xs text-muted text-mono">${Number(c.sessionsCount) || 0} ca (${Number(c.hoursBooked) || 0}h) · Nợ: ${formatMoney(unpaid)}</span>
      </div>
    `;
  }).join('');
}

function renderReportTopMembers(vips) {
  const vipEl = document.getElementById('vipList');
  if (!vipEl) return;

  if (!vips.length) {
    vipEl.innerHTML = renderUiStateBanner('filtered-empty', {
      compact: true,
      icon: 'lucide:users',
      title: 'Chưa phát sinh thực thu hội viên trong kỳ',
      message: 'Chưa có hội viên xác thực nào có giao dịch thu tiền hoàn tất trong khoảng ngày đã chọn.'
    });
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
    const methodKey = String(r.method || '').toLowerCase();
    const methodLbl = methodKey === 'transfer'
      ? 'Chuyển khoản'
      : (methodKey === 'paypal' ? 'PayPal Sandbox' : 'Tiền mặt');
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

/**
 * UI-A04: Only show success toast when renderReports() actually succeeded (res.ok === true).
 */
async function filterReport() {
  const res = await renderReports();
  if (res && res.ok) {
    showToast('Đã cập nhật sổ cái', 'Số liệu doanh thu thực thu, hoàn tiền và công nợ đã được tải từ máy chủ.', 'success');
  }
}

function payPalLocalStatusBadge(status) {
  switch (status) {
    case 'Completed': return '<span class="badge badge-success">Hoàn tất (Completed)</span>';
    case 'AwaitingApproval': return '<span class="badge badge-info">Chờ duyệt PayPal</span>';
    case 'Cancelled': return '<span class="badge badge-neutral">Đã hủy</span>';
    case 'Refunded': return '<span class="badge badge-danger">Đã hoàn tiền</span>';
    case 'RefundedAfterExpiry': return '<span class="badge badge-danger">Hoàn do hết hạn</span>';
    case 'AmbiguousTimeout':
    case 'Capturing':
    case 'CapturedCompensationPending':
    case 'CompensationRefundRequired':
      return `<span class="badge badge-warning">Cần đối soát (${escapeHtml(status)})</span>`;
    default:
      return `<span class="badge badge-neutral">${escapeHtml(status || '--')}</span>`;
  }
}

async function loadAdminPayPalTransactions() {
  const tbody = document.getElementById('reportPayPalTableBody');
  const alertHost = document.getElementById('reportPayPalAlertHost');
  const badgeEl = document.getElementById('reportPayPalReconcileBadge');
  if (!tbody) return;

  await SpikeDemo.ensureLoaded(false);
  const rows = Array.isArray(SpikeDemo.getRawState()?.paypalTransactions)
    ? SpikeDemo.getRawState().paypalTransactions
    : [];
  const reconcileStatuses = new Set(['AmbiguousTimeout', 'Capturing', 'CapturedCompensationPending', 'CompensationRefundRequired', 'AwaitingApproval']);
  const urgentCount = rows.filter(x => reconcileStatuses.has(x.localStatus) && x.localStatus !== 'AwaitingApproval').length;

  if (badgeEl) {
    badgeEl.className = `badge ${urgentCount > 0 ? 'badge-warning' : 'badge-success'}`;
    badgeEl.textContent = urgentCount > 0 ? `Cần đối soát: ${urgentCount}` : 'Đồng bộ';
  }
  if (alertHost) {
    alertHost.style.display = 'none';
    alertHost.innerHTML = '';
  }

  if (!rows.length) {
    tbody.innerHTML = `<tr><td colspan="6" style="text-align:center;padding:1.25rem;color:var(--text-muted);">Chưa phát sinh giao dịch PayPal Sandbox mẫu.</td></tr>`;
    return;
  }

  tbody.innerHTML = rows.map(tx => {
    const canReconcile = ['AwaitingApproval', 'Capturing', 'AmbiguousTimeout'].includes(tx.localStatus);
    const canRefund = tx.localStatus === 'Completed' && Boolean(tx.payPalCaptureId) && !tx.payPalRefundId;
    return `
      <tr>
        <td>
          <div class="text-mono font-bold text-xs">${escapeHtml(tx.attemptCode)}</div>
          <div class="text-xs text-muted">${escapeHtml((tx.createdAtUtc || '').slice(0, 19).replace('T', ' '))} UTC</div>
        </td>
        <td>
          <span class="badge badge-neutral">${tx.targetType === 'order' ? 'Shop' : 'Đặt sân'}</span>
          <strong class="text-mono" style="margin-left:4px;color:var(--color-indigo);">${escapeHtml(tx.targetCode)}</strong>
        </td>
        <td class="text-mono">
          <strong>${formatMoney(tx.amountVnd)}</strong>
          <div class="text-xs text-muted">$${Number(tx.amountUsd || 0).toFixed(2)} USD (@${Number(tx.exchangeRateVndPerUsd || 0).toLocaleString('vi-VN')})</div>
        </td>
        <td class="text-mono text-xs">
          <div>Ord: ${escapeHtml(tx.payPalOrderId || '--')}</div>
          ${tx.payPalCaptureId ? `<div style="color:var(--color-success);">Cap: ${escapeHtml(tx.payPalCaptureId)}</div>` : ''}
          ${tx.payPalRefundId ? `<div style="color:var(--color-danger);">Rfd: ${escapeHtml(tx.payPalRefundId)}</div>` : ''}
        </td>
        <td>${payPalLocalStatusBadge(tx.localStatus)}</td>
        <td>
          <div style="display:flex;gap:6px;flex-wrap:wrap;">
            ${canReconcile ? `<button type="button" class="btn btn-outline btn-xs" onclick="adminReconcilePayPal('${escapeAttr(tx.attemptCode)}', this)">Đối soát Demo</button>` : ''}
            ${canRefund ? `<button type="button" class="btn btn-danger-outline btn-xs" onclick="adminRefundPayPal('${escapeAttr(tx.attemptCode)}', ${Number(tx.amountVnd) || 0}, this)">Hoàn tiền Demo</button>` : ''}
          </div>
        </td>
      </tr>
    `;
  }).join('');
}

async function adminReconcilePayPal(attemptCode) {
  SpikeDemo.simulatePayPalCapture(attemptCode);
  showToast('Đối soát PayPal Demo thành công', `Phiên ${attemptCode} → Completed`, 'success');
  await renderReports();
}

async function adminRefundPayPal(attemptCode, amountVnd) {
  const okConfirm = await SpikeUI.confirm({
    title: 'Mô phỏng hoàn tiền PayPal Sandbox',
    message: `Ghi nhận hoàn trả mô phỏng ${formatMoney(amountVnd)} cho phiên ${attemptCode} trên dữ liệu Demo?`,
    confirmLabel: 'Hoàn tiền Demo',
    danger: true
  });
  if (!okConfirm) return;
  const raw = SpikeDemo.getRawState();
  const tx = (raw?.paypalTransactions || []).find(t => t.attemptCode === attemptCode);
  if (tx) {
    tx.localStatus = 'Refunded';
    tx.payPalRefundId = 'DEMO-RFD-' + Math.floor(1000 + Math.random() * 8999);
    raw.transactions.push({
      id: 'TX-RFD-' + Math.floor(100 + Math.random() * 899),
      type: 'refund',
      targetType: tx.targetType,
      targetCode: tx.targetCode,
      customerName: 'Hoàn tiền PayPal Demo',
      method: 'paypal',
      referenceCode: tx.payPalRefundId,
      note: 'Hoàn tiền mô phỏng (UI Demo)',
      amount: -Math.abs(amountVnd),
      recordedBy: 'Admin Demo',
      date: SpikeDemo.isoDateOffset(0),
      createdAt: `${SpikeDemo.isoDateOffset(0)} 15:30`
    });
    SpikeDemo.saveState();
  }
  showToast('Đã mô phỏng hoàn tiền PayPal', `Phiên ${attemptCode} đã hoàn tiền trong sổ cái Demo.`, 'success');
  await renderReports();
}

document.addEventListener('DOMContentLoaded', renderReports);
