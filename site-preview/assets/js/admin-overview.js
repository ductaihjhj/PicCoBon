/* ==========================================================================
   SPIKE OS — ADMIN OVERVIEW & REVENUE CHARTS CONTROLLER (UI DEMO)
   ========================================================================== */

(function () {
  function renderAdminOverview() {
    if (!HAS_SERVER_STATE) return;
    const raw = SpikeDemo.getRawState();
    const courts = raw?.courts || [];
    const bookings = raw?.bookings || [];
    const products = raw?.products || [];
    const accounts = raw?.accounts || [];

    const today = SpikeDemo.isoDateOffset(0);
    const report = SpikeDemo.getReportSummary(`${today.slice(0, 7)}-01`, today);
    const pendingCount = bookings.filter(b => b.status === 'pending').length;

    const setText = (id, v) => {
      const el = document.getElementById(id);
      if (el) el.textContent = v;
    };

    setText('admTotalCourts', `${courts.filter(c => c.isActive !== false).length} sân`);
    setText('admTotalBookings', `${bookings.length} đơn`);
    setText('admPendingBookings', String(pendingCount));
    setText('admTotalProducts', `${products.length} món`);
    setText('admTotalAccounts', `${accounts.length} tài khoản`);
    setText('admNetRevenue', formatMoney(report.totalCollectedRevenue));
    setText('admReceivables', formatMoney(report.unpaidReceivables));

    renderWeeklyTrendChart(raw, report);
    renderCourtBreakdownChart(report.courtBreakdown || []);
    renderRecentBookingsTable(bookings.slice(0, 6));
  }

  function renderWeeklyTrendChart(raw, report) {
    const host = document.getElementById('admWeeklyTrendChart');
    if (!host) return;

    const base = raw?.weeklyTrendBase || [];
    const todayLedger = SpikeDemo.getTodayLedgerSummary();
    const todayBookingRev = (raw?.transactions || [])
      .filter(t => t.date === SpikeDemo.isoDateOffset(0) && t.type !== 'refund' && t.targetType !== 'order')
      .reduce((s, t) => s + (Number(t.amount) || 0), 0);
    const todayShopRev = (raw?.transactions || [])
      .filter(t => t.date === SpikeDemo.isoDateOffset(0) && t.type !== 'refund' && t.targetType === 'order')
      .reduce((s, t) => s + (Number(t.amount) || 0), 0);

    const days = [
      ...base.map(d => ({
        label: d.label,
        bookingRev: Number(d.bookingRev) || 0,
        shopRev: Number(d.shopRev) || 0
      })),
      {
        label: 'Hôm nay',
        bookingRev: Math.max(todayBookingRev, todayLedger.netCollectedToday - todayShopRev),
        shopRev: todayShopRev
      }
    ];

    const maxTotal = Math.max(...days.map(d => d.bookingRev + d.shopRev), 500000);
    host.innerHTML = days.map(d => {
      const total = d.bookingRev + d.shopRev;
      const bPct = Math.round((d.bookingRev / maxTotal) * 100);
      const sPct = Math.round((d.shopRev / maxTotal) * 100);
      const shortVal = total >= 1000000
        ? `${(total / 1000000).toFixed(1)}tr`
        : `${Math.round(total / 1000)}k`;
      return `
        <div class="spk-trend-col" title="${escapeAttr(d.label)}: Sân ${formatMoney(d.bookingRev)} + Shop ${formatMoney(d.shopRev)}">
          <span class="spk-trend-val">${escapeHtml(shortVal)}</span>
          <div class="spk-trend-track">
            <div class="spk-trend-fill-shop" style="height:${sPct}%;"></div>
            <div class="spk-trend-fill-booking" style="height:${bPct}%;"></div>
          </div>
          <span class="spk-trend-lbl">${escapeHtml(d.label)}</span>
        </div>
      `;
    }).join('');
  }

  function renderCourtBreakdownChart(courtBreakdown) {
    const host = document.getElementById('admCourtRevenueChart');
    if (!host) return;
    if (!courtBreakdown.length) {
      host.innerHTML = `<div class="text-sm text-muted">Chưa có dữ liệu sân.</div>`;
      return;
    }

    const maxVal = Math.max(...courtBreakdown.map(c => Number(c.collectedRevenue) || 0), 100000);
    host.innerHTML = courtBreakdown.map(c => {
      const val = Number(c.collectedRevenue) || 0;
      const pct = val <= 0 ? 0 : Math.max(10, Math.round((val / maxVal) * 100));
      const shortName = String(c.courtName || '').replace(' (Arena A)', '').replace(' (Arena B)', '');
      return `
        <div style="display:flex;flex-direction:column;gap:5px;">
          <div style="display:flex;justify-content:space-between;align-items:center;font-size:0.82rem;">
            <span style="font-weight:650;color:var(--text-primary);">${escapeHtml(shortName)}</span>
            <span class="text-mono font-bold" style="color:var(--brand-primary);">${formatMoney(val)} <span class="text-xs text-muted">(${c.sessionsCount} ca)</span></span>
          </div>
          <div style="height:10px;border-radius:999px;background:var(--surface-secondary);border:1px solid var(--border-default);overflow:hidden;">
            <div style="height:100%;width:${pct}%;background:var(--brand-cta-gradient);border-radius:999px;"></div>
          </div>
        </div>
      `;
    }).join('');
  }

  function renderRecentBookingsTable(list) {
    const tbody = document.getElementById('admRecentBookingsBody');
    if (!tbody) return;
    if (!list.length) {
      tbody.innerHTML = `<tr><td colspan="6" style="text-align:center;padding:1.5rem;color:var(--text-muted);">Chưa có đơn đặt sân mẫu.</td></tr>`;
      return;
    }

    tbody.innerHTML = list.map(b => {
      const due = Number(b.amountDue) || Number(b.courtFee) || 0;
      const paid = Number(b.paidAmount) || 0;
      return `
        <tr>
          <td><code class="text-mono font-bold" style="color:var(--notion-blue);">${escapeHtml(b.id)}</code></td>
          <td>
            <div style="font-weight:650;">${escapeHtml(b.name)}</div>
            <div class="text-xs text-mono text-muted">${escapeHtml(b.phone)}</div>
          </td>
          <td>${escapeHtml(b.courtName || ('Sân #' + b.courtId))}</td>
          <td class="text-mono text-xs">${escapeHtml(formatDateVN(b.date))} · ${escapeHtml(b.time)} (${b.duration}p)</td>
          <td class="text-mono font-bold" style="color:var(--brand-primary);">${formatMoney(due)} ${paymentBadgeHtml(paid, due, b.paymentStatus)}</td>
          <td><span class="badge ${statusBadgeClass(b.status, b)}">${escapeHtml(statusLabel(b.status, b))}</span></td>
        </tr>
      `;
    }).join('');
  }

  document.addEventListener('DOMContentLoaded', renderAdminOverview);
  window.addEventListener('pickleball:db-synced', renderAdminOverview);
})();
