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
  if (!requireRole('admin')) {
    return { ok: false, status: 403, error: 'Truy cập bị từ chối.' };
  }

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

  if (_reportAbortController) {
    try { _reportAbortController.abort(); } catch (_) {}
  }
  const controller = new AbortController();
  _reportAbortController = controller;
  const reqId = ++_reportReqCounter;

  const filterBtn = document.getElementById('btnFilterReport');
  if (filterBtn) filterBtn.disabled = true;

  if (!_lastReportData) {
    setReportBanner(renderUiStateBanner('loading', {
      compact: true,
      title: 'Đang tổng hợp dữ liệu sổ cái tài chính...',
      message: 'Đang truy vấn các bút toán thu tiền, hoàn tiền và công nợ từ SQL Server.'
    }));
  }

  try {
    const qs = new URLSearchParams();
    if (fromDate) qs.set('dateFrom', fromDate);
    if (toDate) qs.set('dateTo', toDate);

    const res = await fetch(`/api/admin/reports/summary?${qs.toString()}`, {
      headers: { 'Accept': 'application/json' },
      credentials: 'same-origin',
      cache: 'no-store',
      signal: controller.signal
    });

    if (reqId !== _reportReqCounter) {
      return { ok: false, aborted: true };
    }

    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data || !data.ok) {
      const errMsg = (data && data.error) || `Máy chủ trả về lỗi HTTP ${res.status} khi tải báo cáo sổ cái.`;
      showToast('Không thể tải báo cáo sổ cái', errMsg, 'error');

      if (_lastReportData) {
        const lastTimeStr = formatSyncTimeHHMMSS(_lastReportSyncTime) || 'trước đó';
        setReportBanner(renderUiStateBanner('stale', {
          compact: true,
          title: `Dữ liệu cũ — Cập nhật thành công lần cuối lúc ${lastTimeStr}`,
          message: `${errMsg} Các chỉ số bên dưới là dữ liệu của lần tải thành công trước đó.`,
          actionHtml: `<button type="button" class="btn btn-primary btn-sm" onclick="filterReport()">
                         <iconify-icon icon="lucide:refresh-cw" width="15"></iconify-icon>
                         <span>Thử tải lại báo cáo</span>
                       </button>`
        }));
      } else {
        setReportBanner(renderUiStateBanner('error', {
          title: 'Không thể tải báo cáo doanh thu & sổ cái',
          message: errMsg,
          actionHtml: `<button type="button" class="btn btn-primary btn-sm" onclick="filterReport()">
                         <iconify-icon icon="lucide:refresh-cw" width="15"></iconify-icon>
                         <span>Thử lại ngay</span>
                       </button>`
        }));
      }

      return { ok: false, status: res.status, error: errMsg, isStale: Boolean(_lastReportData) };
    }

    _lastReportData = data;
    _lastReportSyncTime = new Date();
    setReportBanner('');

    renderReportKpis(data);
    renderReportCourts(data.courtBreakdown || []);
    renderReportTopMembers(data.topMembers || []);
    renderReportLedgerRows(data.recentTransactions || []);

    return { ok: true, status: 200, data };
  } catch (err) {
    if (err && err.name === 'AbortError') {
      return { ok: false, aborted: true };
    }
    const isOffline = typeof navigator !== 'undefined' && navigator.onLine === false;
    const errMsg = isOffline
      ? 'Thiết bị đang mất kết nối mạng. Không thể tải dữ liệu báo cáo doanh thu.'
      : 'Không thể kết nối tới máy chủ để tải dữ liệu báo cáo doanh thu.';

    showToast('Lỗi kết nối', errMsg, 'error');

    if (_lastReportData) {
      const lastTimeStr = formatSyncTimeHHMMSS(_lastReportSyncTime) || 'trước đó';
      setReportBanner(renderUiStateBanner('stale', {
        compact: true,
        title: `Dữ liệu cũ — Cập nhật thành công lần cuối lúc ${lastTimeStr}`,
        message: `${errMsg} Số liệu trên màn hình đang giữ từ lần tải trước.`,
        actionHtml: `<button type="button" class="btn btn-primary btn-sm" onclick="filterReport()">
                       <iconify-icon icon="lucide:refresh-cw" width="15"></iconify-icon>
                       <span>Thử kết nối lại</span>
                     </button>`
      }));
    } else {
      setReportBanner(renderUiStateBanner(isOffline ? 'offline' : 'error', {
        title: 'Mất kết nối khi tải báo cáo sổ cái',
        message: errMsg,
        actionHtml: `<button type="button" class="btn btn-primary btn-sm" onclick="filterReport()">
                       <iconify-icon icon="lucide:refresh-cw" width="15"></iconify-icon>
                       <span>Thử lại ngay</span>
                     </button>`
      }));
    }

    return { ok: false, status: 0, error: errMsg, isStale: Boolean(_lastReportData) };
  } finally {
    if (filterBtn && reqId === _reportReqCounter) {
      filterBtn.disabled = false;
    }
  }
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
      <div class="stat-detail">Tiền mặt: ${formatMoney(d.cashNetCollected)} · CK: ${formatMoney(d.transferNetCollected)}</div>
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

  const maxVal = Math.max(...courts.map(c => Math.max(Number(c.collectedRevenue) || 0, Number(c.unpaidReceivable) || 0)), 100000);
  chartEl.innerHTML = courts.map(c => {
    const collected = Math.max(0, Number(c.collectedRevenue) || 0);
    const unpaid = Math.max(0, Number(c.unpaidReceivable) || 0);
    const isZero = collected <= 0;
    const pct = isZero ? 0 : Math.max(6, Math.min(100, Math.round((collected / maxVal) * 100)));
    const barLabel = isZero ? '0 đ' : formatMoney(collected);
    const ariaSummary = `${c.courtName}: Thực thu ${formatMoney(collected)}, Công nợ ${formatMoney(unpaid)}, ${Number(c.sessionsCount) || 0} ca (${Number(c.hoursBooked) || 0} giờ)`;

    return `
      <div class="chart-col" role="img" aria-label="${escapeAttr(ariaSummary)}">
        <div class="chart-bar-track">
          <div class="chart-bar ${isZero ? 'is-zero' : ''}" data-value="${collected}" style="height:${pct}%;min-height:0;max-width:64px;" title="${escapeAttr(ariaSummary)}">
            <span class="chart-bar-value text-mono">${escapeHtml(barLabel)}</span>
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

/**
 * UI-A04: Only show success toast when renderReports() actually succeeded (res.ok === true).
 */
async function filterReport() {
  const res = await renderReports();
  if (res && res.ok) {
    showToast('Đã cập nhật sổ cái', 'Số liệu doanh thu thực thu, hoàn tiền và công nợ đã được tải từ máy chủ.', 'success');
  }
}

document.addEventListener('DOMContentLoaded', renderReports);
