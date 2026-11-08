/* ==========================================================================
   PICKLEBALL PRO ARENA — DYNAMIC REVENUE & ANALYTICS CONTROLLER
   Separates Collected Revenue (Thực thu) vs Receivables (Công nợ) — P1-06 & P0-12
   ========================================================================== */

function renderReports() {
  if (!requireRole('admin')) return;

  const fromEl = document.getElementById('reportFrom');
  const toEl = document.getElementById('reportTo');
  if (fromEl && !fromEl.value) {
    const now = new Date();
    fromEl.value = new Date(now.getFullYear(), now.getMonth(), 1).toISOString().slice(0, 10);
  }
  if (toEl && !toEl.value) {
    toEl.value = getOffsetDateISO(7);
  }

  const fromDate = fromEl?.value || '2000-01-01';
  const toDate = toEl?.value || '2099-12-31';

  const validBookings = BOOKINGS.filter(b => {
    if (b.status === 'rejected' || b.status === 'cancelled') return false;
    const d = b.date || getTodayISO();
    return d >= fromDate && d <= toDate;
  });

  let totalCourtRevenue = 0;
  let totalPosRevenue = 0;
  let totalCollectedRevenue = 0;
  let totalReceivables = 0;
  let totalMinutesBooked = 0;

  validBookings.forEach(b => {
    const posSum = Number(b.posTotal) || (b.posItems || []).reduce((s, i) => s + Number(i.price) * Number(i.qty), 0);
    const calc = calculateBookingTotal(b.courtId, b.time, b.duration, posSum);
    const courtFee = Number(b.courtFee) || calc.courtFee;
    const amountDue = Number(b.amountDue) || (courtFee - (Number(b.discountAmount) || 0) + posSum);
    const paidAmount = Number(b.paidAmount) || 0;

    totalCourtRevenue += courtFee;
    totalPosRevenue += posSum;
    totalCollectedRevenue += paidAmount;
    totalReceivables += Math.max(0, amountDue - paidAmount);
    totalMinutesBooked += Number(b.duration) || 60;
  });

  const grandTotal = totalCourtRevenue + totalPosRevenue;

  // 1. Render Summary KPI Cards (P1-06: Thực thu vs Công nợ)
  const statsEl = document.getElementById('reportStats');
  if (statsEl) {
    statsEl.innerHTML = `
      <div class="stat-widget">
        <div class="stat-widget-top">
          <span class="stat-label">Doanh thu thực thu (Đã thanh toán)</span>
          <div class="stat-icon" style="background:var(--color-primary-50);color:var(--color-primary)"><iconify-icon icon="lucide:trending-up"></iconify-icon></div>
        </div>
        <div class="stat-value primary text-mono">${formatMoney(totalCollectedRevenue)}</div>
        <div class="stat-detail">Tổng phải thu: ${formatMoney(grandTotal)} · Công nợ: <strong style="color:#D97706;">${formatMoney(totalReceivables)}</strong></div>
      </div>
      <div class="stat-widget">
        <div class="stat-widget-top">
          <span class="stat-label">Doanh thu POS &amp; Phụ phí</span>
          <div class="stat-icon" style="background:var(--color-coral-50);color:var(--color-coral)"><iconify-icon icon="lucide:coffee"></iconify-icon></div>
        </div>
        <div class="stat-value accent text-mono">${formatMoney(totalPosRevenue)}</div>
        <div class="stat-detail">Tiền sân niêm yết: ${formatMoney(totalCourtRevenue)}</div>
      </div>
      <div class="stat-widget">
        <div class="stat-widget-top">
          <span class="stat-label">Tổng lượt đặt hợp lệ</span>
          <div class="stat-icon" style="background:var(--color-indigo-50);color:var(--color-indigo)"><iconify-icon icon="lucide:calendar-check"></iconify-icon></div>
        </div>
        <div class="stat-value indigo">${validBookings.length} đơn</div>
        <div class="stat-detail">Tổng thời lượng: ${(totalMinutesBooked / 60).toFixed(1)} giờ</div>
      </div>
      <div class="stat-widget">
        <div class="stat-widget-top">
          <span class="stat-label">Giá trị trung bình / đơn</span>
          <div class="stat-icon" style="background:var(--color-warning-light);color:#B45309"><iconify-icon icon="lucide:award"></iconify-icon></div>
        </div>
        <div class="stat-value warning text-mono">${formatMoney(validBookings.length ? grandTotal / validBookings.length : 0)}</div>
        <div class="stat-detail">Thực thu TB: ${formatMoney(validBookings.length ? totalCollectedRevenue / validBookings.length : 0)}</div>
      </div>
    `;
  }

  // 2. Render Revenue Chart by Court
  const chartData = COURTS.map(c => {
    const courtBookings = validBookings.filter(b => Number(b.courtId) === Number(c.id));
    const rev = courtBookings.reduce((sum, b) => {
      const posSum = Number(b.posTotal) || (b.posItems || []).reduce((s, i) => s + Number(i.price) * Number(i.qty), 0);
      const calc = calculateBookingTotal(b.courtId, b.time, b.duration, posSum);
      return sum + (Number(b.amountDue) || Number(b.totalPaid) || calc.grandTotal);
    }, 0);
    const collected = courtBookings.reduce((sum, b) => sum + (Number(b.paidAmount) || 0), 0);
    return { label: c.name, value: rev, collected, count: courtBookings.length };
  });

  const maxVal = Math.max(...chartData.map(d => d.value), 100000);
  const chartEl = document.getElementById('reportChart');
  if (chartEl) {
    chartEl.innerHTML = chartData.map(d => {
      const pct = Math.max(10, Math.round((d.value / maxVal) * 100));
      return `
        <div class="chart-col">
          <div class="chart-bar" style="height:${pct}%;max-width:56px" title="Thực thu: ${formatMoney(d.collected)} / Phải thu: ${formatMoney(d.value)}">
            <span class="chart-bar-value">${Math.round(d.value / 1000)}k</span>
          </div>
          <span class="chart-label">${escapeHtml(d.label)}</span>
          <span class="text-xs text-muted">${d.count} đơn · Thu: ${Math.round(d.collected / 1000)}k</span>
        </div>
      `;
    }).join('');
  }

  // 3. Render Top Customers by Actual Paid Spend (P1-16)
  const customerMap = {};
  validBookings.forEach(b => {
    const key = b.phone || b.name;
    const paid = Number(b.paidAmount) || 0;
    const due = Number(b.amountDue) || Number(b.totalPaid) || Number(b.courtFee) || 0;
    if (!customerMap[key]) {
      customerMap[key] = { name: b.name, phone: b.phone, count: 0, paid: 0, due: 0 };
    }
    customerMap[key].count += 1;
    customerMap[key].paid += paid;
    customerMap[key].due += due;
  });

  const vips = Object.values(customerMap).sort((a, b) => (b.paid - a.paid) || (b.due - a.due)).slice(0, 5);
  const ranks = ['gold', 'silver', 'bronze', 'normal', 'normal'];
  const vipEl = document.getElementById('vipList');
  if (vipEl) {
    vipEl.innerHTML = vips.map((v, i) => `
      <div class="vip-item">
        <div class="vip-rank ${ranks[i]}">${i + 1}</div>
        <div style="flex:1;min-width:0">
          <div class="font-bold text-sm truncate">${escapeHtml(v.name)}</div>
          <div class="text-xs text-muted text-mono">${escapeHtml(v.phone)} · ${v.count} lượt đặt</div>
        </div>
        <div style="text-align:right;">
          <div class="font-bold text-mono" style="color:var(--color-primary)">${formatMoney(v.paid)}</div>
          <div class="text-xs text-muted text-mono">Tổng đơn: ${formatMoney(v.due)}</div>
        </div>
      </div>
    `).join('');
  }
}

function filterReport() {
  renderReports();
  showToast('Đã cập nhật báo cáo', 'Số liệu doanh thu thực thu và công nợ đã được tính toán lại theo kỳ hạn chọn.', 'success');
}

document.addEventListener('DOMContentLoaded', renderReports);
