/* ==========================================================================
   SPIKE PICKLEBALL CLUB — MANAGEMENT, DISPATCH, POS & LEDGER CONTROLLER (V2)
   Separates Session Finish vs Payment/Refund Ledger + Standalone Shop Orders
   ========================================================================== */

let lockSelectedCourt = null;
let mgmtFilter = 'active';
let posBooking = null;
let posCart = [];
let posCourtDuration = 60;
let activeLedgerContext = null; // { kind: 'booking' | 'order', code: string }
let ADMIN_SHOP_ORDERS = null;
let ADMIN_ORDERS_LOADED = false;
let shopOrderPageSize = 25;
const _fulfillingOrderCodes = new Set();
let _approvingBatch = false;
let _ledgerPaySubmitting = false;

function getEffectiveAdminShopOrders() {
  if (ADMIN_ORDERS_LOADED && Array.isArray(ADMIN_SHOP_ORDERS)) {
    return ADMIN_SHOP_ORDERS;
  }
  return Array.isArray(STANDALONE_ORDERS) ? STANDALONE_ORDERS : [];
}

async function loadAdminShopOrders() {
  try {
    const res = await fetch('/api/admin/orders?standaloneOnly=false', {
      headers: { 'Accept': 'application/json' },
      credentials: 'same-origin',
      cache: 'no-store'
    });
    if (!res.ok) return false;
    const data = await res.json().catch(() => ({}));
    if (data && data.ok && Array.isArray(data.orders)) {
      ADMIN_SHOP_ORDERS = data.orders;
      ADMIN_ORDERS_LOADED = true;
      renderStandaloneOrdersSection();
      return true;
    }
  } catch (_) {}
  return false;
}

function initManagement() {
  if (!requireRole('admin')) return;

  const lockDateEl = document.getElementById('lockDate');
  if (lockDateEl) {
    const todayIso = getTodayStr();
    lockDateEl.min = todayIso;
    if (!lockDateEl.value || lockDateEl.value < todayIso) lockDateEl.value = todayIso;
  }

  populateLockStartOptions();
  renderManagement();
  loadAdminShopOrders();
}

function getClubOperatingWindowMinutes() {
  const openStr = CMS_CONFIG.openTime || '06:00';
  const closeStr = CMS_CONFIG.closeTime || '22:00';
  const openMins = timeToMinutes(openStr) || 360;
  const closeMins = timeToMinutes(closeStr) || 1320;
  return {
    openStr,
    closeStr,
    openMins,
    closeMins: Math.max(openMins + 60, closeMins)
  };
}

function populateLockStartOptions() {
  const lockSel = document.getElementById('lockStart');
  const { openStr, closeStr, openMins, closeMins } = getClubOperatingWindowMinutes();
  const fullDayOpt = document.getElementById('lockDurationFullDayOption');
  if (fullDayOpt) {
    const fullMins = closeMins - openMins;
    const fullHrs = Number((fullMins / 60).toFixed(1));
    fullDayOpt.textContent = `Cả ngày (${openStr} → ${closeStr} · ${fullHrs}h)`;
  }

  if (!lockSel) return;
  const curVal = lockSel.value || (openMins <= 720 && closeMins > 720 ? '12:00' : openStr);
  let html = '';
  for (let m = openMins; m <= closeMins - 30; m += 30) {
    const t = minutesToTime(m);
    html += `<option value="${t}">${t}</option>`;
  }
  lockSel.innerHTML = html;
  if ([...lockSel.options].some(o => o.value === curVal)) {
    lockSel.value = curVal;
  }
  onLockWindowChange();
}

function onLockDurationChange() {
  const durVal = document.getElementById('lockDuration')?.value || '60';
  const lockSel = document.getElementById('lockStart');
  const { openStr } = getClubOperatingWindowMinutes();
  if (durVal === 'full_day' && lockSel) {
    lockSel.value = openStr;
  }
  onLockWindowChange();
}

function onLockWindowChange() {
  const preview = document.getElementById('lockWindowPreview');
  const { openMins, closeMins, closeStr } = getClubOperatingWindowMinutes();
  const startStr = document.getElementById('lockStart')?.value || minutesToTime(openMins);
  const durRaw = document.getElementById('lockDuration')?.value || '60';
  const durMins = durRaw === 'full_day' ? (closeMins - openMins) : (Number(durRaw) || 60);
  const startMins = timeToMinutes(startStr);
  const endMins = startMins + durMins;

  if (preview) {
    const exceedsClose = endMins > closeMins || endMins > 24 * 60;
    preview.innerHTML = exceedsClose
      ? `<span style="color:var(--color-danger);font-weight:600;">Khung khóa (${startStr} → ${minutesToTime(Math.min(endMins, 24 * 60))}${endMins > 24 * 60 ? '+' : ''}) vượt quá giờ đóng cửa (${closeStr}).</span>`
      : `Khung giờ áp dụng: <strong class="text-mono">${escapeHtml(startStr)} → ${escapeHtml(minutesToTime(endMins))}</strong> (${durMins} phút)`;
  }
}

function setMgmtFilter(filter) {
  mgmtFilter = filter;
  document.querySelectorAll('#mgmtFilterTabs .filter-tab').forEach(btn => {
    const txt = btn.textContent || '';
    const isMatch =
      (filter === 'active' && txt.includes('Đang chơi')) ||
      (filter === 'unpaid' && txt.includes('Còn công nợ')) ||
      (filter === 'completed' && txt.includes('Đã đóng ca')) ||
      (filter === 'all' && txt.includes('Tất cả'));
    btn.classList.toggle('active', isMatch);
  });
  renderManagement();
}

function renderManagement() {
  const pendingList = document.getElementById('pendingList');
  const todayList = document.getElementById('todayList');
  const approveAllBtn = document.getElementById('approveAllBtn');
  if (!pendingList || !todayList) return;

  if (!HAS_SERVER_STATE) {
    const syncState = (typeof SPIKE_SYNC_STATE !== 'undefined' && (SPIKE_SYNC_STATE === 'offline' || SPIKE_SYNC_STATE === 'error'))
      ? SPIKE_SYNC_STATE
      : 'loading';
    const bannerHtml = renderUiStateBanner(syncState, {
      title: syncState === 'loading' ? 'Đang tải danh sách điều phối & POS...' : undefined,
      subtitle: syncState === 'loading' ? 'Đang đồng bộ trạng thái sân, yêu cầu đặt lịch và sổ cái thu ngân.' : (typeof LAST_SYNC_ERROR !== 'undefined' ? LAST_SYNC_ERROR : undefined),
      onRetry: 'retryServerSync()'
    });
    pendingList.innerHTML = bannerHtml;
    todayList.innerHTML = bannerHtml;
    const pendingCountEl = document.getElementById('pendingCount');
    if (pendingCountEl) pendingCountEl.textContent = '…';
    if (approveAllBtn) approveAllBtn.style.display = 'none';
    return;
  }

  const pendings = BOOKINGS.filter(b => b.status === 'pending' && isPendingBookingHoldActive(b));
  const pendingCountEl = document.getElementById('pendingCount');
  if (pendingCountEl) pendingCountEl.textContent = pendings.length;
  if (approveAllBtn) approveAllBtn.style.display = pendings.length > 1 ? 'inline-flex' : 'none';

  // 1. Render Pending Requests
  if (pendings.length === 0) {
    pendingList.innerHTML = `
      <div class="empty-state" style="padding:1.5rem 1rem">
        <div class="empty-state-icon"><iconify-icon icon="lucide:check-circle-2" width="26" style="color:var(--color-success)"></iconify-icon></div>
        <div class="empty-state-title">Không có đơn chờ duyệt</div>
        <div class="empty-state-text">Mọi yêu cầu đặt sân của khách hàng đã được xử lý.</div>
      </div>`;
  } else {
    pendingList.innerHTML = pendings.map(b => {
      const courtName = getCourtDisplayName(b.courtId, b.courtName);
      const courtNum = Number(b.courtId) || 1;
      const endTime = getEndTime(b.time, b.duration);
      const expectedFee = Number(b.amountDue) || Number(b.courtFee) || calcBookingPrice(b.courtId, b.time, b.duration);
      const holdLabel = getPendingHoldRemainingLabel(b);
      return `
        <div class="item-row">
          <div class="item-avatar" style="background:var(--color-warning-light);color:#B45309">S${courtNum}</div>
          <div class="item-info">
            <div class="item-name" style="flex-wrap:wrap;gap:0.35rem;">
              <span>${escapeHtml(b.name)}</span>
              <span class="badge badge-neutral text-mono">${escapeHtml(b.id)}</span>
              <span class="badge badge-warning">${escapeHtml(formatDateVN(b.date))}</span>
              ${holdLabel ? `<span class="badge badge-primary" style="font-size:0.7rem;">⏳ ${escapeHtml(holdLabel)}</span>` : ''}
            </div>
            <div class="item-detail">
              <strong>${escapeHtml(courtName)}</strong> · <span class="text-mono font-bold">${escapeHtml(b.time)} → ${escapeHtml(endTime)}</span> (${Number(b.duration)}p) · SĐT: <span class="text-mono">${escapeHtml(b.phone)}</span> · Phải thu: <strong class="text-mono" style="color:var(--color-primary)">${formatMoney(expectedFee)}</strong>
            </div>
          </div>
          <div class="item-actions">
            <button type="button" class="btn btn-success btn-sm" onclick="approveBooking('${escapeAttr(b.id)}')">
              <iconify-icon icon="lucide:check" width="14"></iconify-icon> Duyệt vé
            </button>
            <button type="button" class="btn btn-danger-outline btn-sm" onclick="rejectBooking('${escapeAttr(b.id)}')">
              <iconify-icon icon="lucide:x" width="14"></iconify-icon> Từ chối
            </button>
          </div>
        </div>
      `;
    }).join('');
  }

  // 2. Render Active / Unpaid / Completed Bookings (P1-04, P1-05, P1-07)
  let listData = BOOKINGS.filter(b => b.status !== 'pending');
  if (mgmtFilter === 'active') {
    listData = BOOKINGS.filter(b => ['playing', 'approved'].includes(b.status));
  } else if (mgmtFilter === 'unpaid') {
    listData = BOOKINGS.filter(b => {
      if (['rejected', 'cancelled', 'expired', 'no_show', 'pending'].includes(b.status)) return false;
      const due = Number(b.amountDue) || Number(b.courtFee) || 0;
      const paid = Number(b.paidAmount) || 0;
      return due > paid;
    });
  } else if (mgmtFilter === 'completed') {
    listData = BOOKINGS.filter(b => b.status === 'completed');
  }

  if (listData.length === 0) {
    todayList.innerHTML = `
      <div class="empty-state">
        <div class="empty-state-icon"><iconify-icon icon="lucide:calendar-x" width="28"></iconify-icon></div>
        <div class="empty-state-title">Chưa có ca sân nào trong bộ lọc này</div>
      </div>`;
  } else {
    todayList.innerHTML = listData.map(b => {
      const courtName = getCourtDisplayName(b.courtId, b.courtName);
      const courtNum = Number(b.courtId) || 1;
      const endTime = getEndTime(b.time, b.duration);
      const isPlaying = b.status === 'playing';
      const isApproved = b.status === 'approved';
      const isCompleted = b.status === 'completed';
      const posCount = (b.posItems || []).reduce((s, i) => s + Number(i.qty || 0), 0);
      const posExtra = (b.posItems || []).reduce((s, i) => s + Number(i.price || 0) * Number(i.qty || 0), 0);
      const amountDue = Number(b.amountDue) || (Number(b.courtFee || 0) + posExtra);
      const paidAmount = Number(b.paidAmount) || 0;
      const remaining = Math.max(0, amountDue - paidAmount);

      let actionsHtml = '';
      if (isApproved) {
        actionsHtml = `
          <button type="button" class="btn btn-primary btn-sm" onclick="startMatchNow('${escapeAttr(b.id)}')">
            <iconify-icon icon="lucide:play" width="14"></iconify-icon> Nhận sân
          </button>
          <button type="button" class="btn btn-outline btn-sm" onclick="openPos('${escapeAttr(b.id)}')">
            <iconify-icon icon="lucide:coffee" width="14"></iconify-icon> POS (${posCount})
          </button>
          <button type="button" class="btn btn-outline btn-sm" onclick="openLedgerModal('booking', '${escapeAttr(b.id)}')">
            <iconify-icon icon="lucide:wallet" width="14"></iconify-icon> Sổ thu
          </button>
        `;
      } else if (isPlaying) {
        actionsHtml = `
          <button type="button" class="btn btn-accent btn-sm" onclick="openPos('${escapeAttr(b.id)}')">
            <iconify-icon icon="lucide:shopping-cart" width="14"></iconify-icon> POS ${posCount > 0 ? `(${posCount})` : ''}
          </button>
          <button type="button" class="btn btn-primary btn-sm" onclick="openLedgerModal('booking', '${escapeAttr(b.id)}')">
            <iconify-icon icon="lucide:wallet" width="14"></iconify-icon> ${remaining > 0 ? 'Thu tiền' : 'Sổ cái'}
          </button>
          <button type="button" class="btn btn-outline btn-sm" onclick="finishSessionOnly('${escapeAttr(b.id)}')" title="Chỉ đóng ca chơi, KHÔNG tự ghi thu tiền khống">
            <iconify-icon icon="lucide:flag" width="13"></iconify-icon> Đóng ca
          </button>
        `;
      } else if (isCompleted) {
        actionsHtml = `
          <button type="button" class="btn ${remaining > 0 ? 'btn-primary' : 'btn-outline'} btn-sm" onclick="openLedgerModal('booking', '${escapeAttr(b.id)}')">
            <iconify-icon icon="lucide:wallet" width="14"></iconify-icon> ${remaining > 0 ? `Thu nợ (${formatMoney(remaining)})` : 'Sổ cái / Hoàn'}
          </button>
        `;
      } else {
        actionsHtml = `
          <span class="badge badge-danger">${escapeHtml(statusLabel(b.status, b))}</span>
          ${paidAmount > 0 ? `<button type="button" class="btn btn-outline btn-xs" onclick="openLedgerModal('booking', '${escapeAttr(b.id)}')">Hoàn tiền</button>` : ''}
        `;
      }

      return `
        <div class="item-row" ${isPlaying ? 'style="background:var(--color-primary-50)"' : ''}>
          <div class="item-avatar" style="background:${isPlaying ? 'var(--color-primary)' : 'var(--bg-surface-alt)'};color:${isPlaying ? '#FFFFFF' : 'var(--text-primary)'}">S${courtNum}</div>
          <div class="item-info">
            <div class="item-name" style="flex-wrap:wrap;gap:0.35rem;">
              <span>${escapeHtml(b.name)}</span>
              <span class="badge ${statusBadgeClass(b.status, b)}">${escapeHtml(statusLabel(b.status, b))}</span>
              ${paymentBadgeHtml(paidAmount, amountDue, b.paymentStatus)}
              <span class="text-xs text-mono text-muted">${escapeHtml(b.id)}</span>
            </div>
            <div class="item-detail">
              <strong>${escapeHtml(courtName)}</strong> · <span class="text-mono font-semibold">${escapeHtml(b.time)} → ${escapeHtml(endTime)}</span> (${Number(b.duration)}p) · ${escapeHtml(formatDateVN(b.date))} · Phải thu: <strong class="text-mono" style="color:var(--color-indigo)">${formatMoney(amountDue)}</strong> · Đã thu: <strong class="text-mono" style="color:var(--color-success)">${formatMoney(paidAmount)}</strong>
            </div>
          </div>
          <div class="item-actions" style="flex-wrap:wrap;">
            ${actionsHtml}
          </div>
        </div>
      `;
    }).join('');
  }

  // 3. Render Standalone Pro Shop Orders (P1-15)
  renderStandaloneOrdersSection();

  // 4. Render Court Lock Buttons & Active Locks List (FIX-02: never assume COURT id=1 if COURTS empty)
  if (HAS_SERVER_STATE) {
    if (COURTS.length > 0 && !getCourtById(lockSelectedCourt)) {
      lockSelectedCourt = Number(COURTS[0].id);
    } else if (COURTS.length === 0) {
      lockSelectedCourt = null;
    }
  }
  const lockBtnsEl = document.getElementById('lockCourtBtns');
  const lockSubmitBtn = document.getElementById('btnCreateCourtLock');
  if (lockBtnsEl) {
    if (COURTS.length === 0) {
      lockBtnsEl.innerHTML = `<div class="text-xs text-muted" style="grid-column:1/-1;">Chưa có sân đang hoạt động để khóa.</div>`;
    } else {
      lockBtnsEl.innerHTML = COURTS.map(c => `
        <button type="button" class="btn ${Number(c.id) === Number(lockSelectedCourt) ? 'btn-danger' : 'btn-outline'} btn-sm" onclick="selectLockCourt(${Number(c.id)})">
          ${escapeHtml(c.name)}
        </button>
      `).join('');
    }
  }
  if (lockSubmitBtn) {
    lockSubmitBtn.disabled = !HAS_SERVER_STATE || COURTS.length === 0 || !getCourtById(lockSelectedCourt);
  }

  const locksCountEl = document.getElementById('activeLocksCount');
  const locksListEl = document.getElementById('activeLocksList');
  if (locksCountEl) locksCountEl.textContent = COURT_LOCKS.length;
  if (locksListEl) {
    if (COURT_LOCKS.length === 0) {
      locksListEl.innerHTML = `<div class="empty-state" style="padding:1.25rem"><div class="text-xs text-muted">Tất cả ${COURTS.length} sân đang hoạt động bình thường.</div></div>`;
    } else {
      locksListEl.innerHTML = COURT_LOCKS.map(l => {
        const courtName = getCourtDisplayName(l.courtId);
        const end = getEndTime(l.startTime, l.duration);
        return `
          <div class="item-row" style="padding:0.65rem 1rem">
            <div class="item-info">
              <div class="item-name" style="font-size:0.825rem">${escapeHtml(courtName)} <span class="badge badge-danger">${escapeHtml(l.startTime)}→${escapeHtml(end)}</span></div>
              <div class="item-detail">${escapeHtml(formatDateVN(l.date))} · ${escapeHtml(l.reason)}</div>
            </div>
            <button type="button" class="btn btn-outline btn-sm" onclick="unlockCourt('${escapeAttr(l.id)}')">Mở khóa</button>
          </div>
        `;
      }).join('');
    }
  }
  renderManagementTimeline();
}

function renderManagementTimeline() {
  const table = document.getElementById('mgmtTimelineGrid');
  if (!table) return;

  const dateInput = document.getElementById('mgmtTimelineDate');
  const todayStr = getTodayStr();
  if (dateInput && !dateInput.value) {
    dateInput.value = todayStr;
  }
  const targetDate = (dateInput && dateInput.value) || todayStr;

  if (!HAS_SERVER_STATE) {
    table.innerHTML = `<tbody><tr><td style="padding:18px;">${renderUiStateBanner('loading', { compact: true })}</td></tr></tbody>`;
    return;
  }
  if (COURTS.length === 0) {
    table.innerHTML = `<tbody><tr><td style="padding:18px;">${renderUiStateBanner('empty', { compact: true, title: 'Chưa có sân đang hoạt động' })}</td></tr></tbody>`;
    return;
  }

  const { openMins, closeMins } = getClubOperatingWindowMinutes();
  const openHour = Math.max(5, Math.floor(openMins / 60));
  const closeHour = Math.min(23, Math.ceil(closeMins / 60));
  const dayBookings = BOOKINGS.filter(b =>
    (b.date || todayStr) === targetDate &&
    !['rejected', 'cancelled', 'expired', 'no_show'].includes(b.status) &&
    (b.status !== 'pending' || isPendingBookingHoldActive(b))
  );

  let html = '<thead><tr><th style="width:70px;text-align:center;">Giờ</th>';
  COURTS.forEach(c => {
    const shortName = escapeHtml(String(c.name || '').replace(' (Arena A)', '').replace(' (Arena B)', ''));
    html += `<th>${shortName}<div style="font-size:0.7rem;font-weight:500;color:var(--text-muted);">${c.type === 'Indoor' ? 'Trong nhà VIP' : 'Ngoài trời'}</div></th>`;
  });
  html += '</tr></thead><tbody>';

  for (let h = openHour; h < closeHour; h++) {
    const hourStr = `${String(h).padStart(2, '0')}:00`;
    const slotStart = h * 60;
    const slotEnd = (h + 1) * 60;
    html += `<tr><td class="sn-ops-timeline-hour">${hourStr}</td>`;

    COURTS.forEach(c => {
      const lockHit = COURT_LOCKS.find(l => {
        if (Number(l.courtId) !== Number(c.id) || l.date !== targetDate) return false;
        const ls = timeToMinutes(l.startTime);
        const le = ls + (Number(l.duration) || 60);
        return slotStart < le && slotEnd > ls;
      });
      if (lockHit) {
        html += `<td><div class="sn-ops-slot-pill state-locked" title="${escapeAttr(lockHit.reason || 'Khóa sân')}"><span>✕ ${escapeHtml(lockHit.reason || 'Khóa sân')}</span></div></td>`;
        return;
      }

      const b = dayBookings.find(item => {
        if (Number(item.courtId) !== Number(c.id)) return false;
        const bs = timeToMinutes(item.time);
        const be = bs + (Number(item.duration) || 60);
        return slotStart < be && slotEnd > bs;
      });

      if (!b) {
        html += `<td><div class="sn-ops-slot-empty">— Trống</div></td>`;
      } else {
        const stateCls = b.status === 'playing' ? 'state-playing' : (b.status === 'pending' ? 'state-pending' : 'state-approved');
        const stateIcon = b.status === 'playing' ? '■' : (b.status === 'pending' ? '◷' : '●');
        html += `<td>
          <button type="button"
                  class="sn-ops-slot-pill ${stateCls}"
                  onclick="openManagementDetailDrawer('${escapeAttr(b.id)}', this)">
            <span style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">${stateIcon} ${escapeHtml(b.name || b.id)}</span>
            <span class="font-mono" style="font-size:0.7rem;flex-shrink:0;">${escapeHtml(b.time)}</span>
          </button>
        </td>`;
      }
    });

    html += '</tr>';
  }
  html += '</tbody>';
  table.innerHTML = html;
}

function openManagementDetailDrawer(bookingCode, triggerEl = null) {
  const b = BOOKINGS.find(item => item.id === bookingCode);
  if (!b) return;
  const titleEl = document.getElementById('mgmtDrawerTitle');
  const subEl = document.getElementById('mgmtDrawerSubtitle');
  const bodyEl = document.getElementById('mgmtDrawerBody');
  const footEl = document.getElementById('mgmtDrawerFooter');

  const courtName = getCourtDisplayName(b.courtId, b.courtName);
  const grandTotal = getBookingGrandTotal(b);
  const paidAmount = Number(b.paidAmount || 0);
  const unpaid = Math.max(0, grandTotal - paidAmount);
  const endTime = getEndTime(b.time, b.duration);

  if (subEl) subEl.textContent = `${b.id} · ${formatDateVN(b.date)}`;
  if (titleEl) titleEl.textContent = `${b.name || 'Khách hàng'} — ${courtName}`;
  if (bodyEl) {
    bodyEl.innerHTML = `
      <div style="display:flex;gap:8px;flex-wrap:wrap;">
        <span class="badge ${statusBadgeClass(b.status, b)}">${escapeHtml(statusLabel(b.status, b))}</span>
        ${paymentBadgeHtml(paidAmount, grandTotal, b.paymentStatus)}
      </div>
      <div class="payment-summary" style="border-radius:var(--radius-md);">
        <div class="payment-line"><span class="text-muted">Sân thi đấu</span><strong>${escapeHtml(courtName)}</strong></div>
        <div class="payment-line"><span class="text-muted">Khung giờ</span><strong class="text-mono">${escapeHtml(b.time)} → ${escapeHtml(endTime)} (${b.duration}p)</strong></div>
        <div class="payment-line"><span class="text-muted">Số điện thoại</span><strong class="text-mono">${escapeHtml(b.phone || '---')}</strong></div>
        <div class="payment-line"><span class="text-muted">Dịch vụ POS thêm</span><strong class="text-mono">${formatMoney(getBookingPosTotal(b))}</strong></div>
        <div class="payment-line"><span class="text-muted">Đã thu thực tế</span><strong class="text-mono" style="color:var(--status-success-text)">${formatMoney(paidAmount)}</strong></div>
        <div class="payment-line total"><span>Còn phải thu</span><span class="text-mono" style="color:var(--brand-primary)">${formatMoney(unpaid)}</span></div>
      </div>
    `;
  }
  if (footEl) {
    const actions = [];
    if (b.status === 'pending') {
      actions.push(`<button type="button" class="btn btn-primary btn-sm" onclick="SpikeUI.closeDrawer('mgmtDetailDrawer');approveBooking('${escapeAttr(b.id)}')">Duyệt vé</button>`);
      actions.push(`<button type="button" class="btn btn-danger btn-sm" onclick="SpikeUI.closeDrawer('mgmtDetailDrawer');rejectBooking('${escapeAttr(b.id)}')">Từ chối</button>`);
    } else {
      if (b.status === 'approved') {
        actions.push(`<button type="button" class="btn btn-secondary btn-sm" onclick="SpikeUI.closeDrawer('mgmtDetailDrawer');startMatchNow('${escapeAttr(b.id)}')">Nhận sân vào chơi</button>`);
      }
      if (b.status === 'playing' || b.status === 'approved') {
        actions.push(`<button type="button" class="btn btn-secondary btn-sm" onclick="SpikeUI.closeDrawer('mgmtDetailDrawer');openPos('${escapeAttr(b.id)}')">Mở POS / Đổi sân</button>`);
      }
      actions.push(`<button type="button" class="btn btn-primary btn-sm" onclick="SpikeUI.closeDrawer('mgmtDetailDrawer');openLedgerModal('booking','${escapeAttr(b.id)}')">Sổ cái / Thu tiền</button>`);
      if (b.status === 'playing') {
        actions.push(`<button type="button" class="btn btn-outline btn-sm" onclick="SpikeUI.closeDrawer('mgmtDetailDrawer');finishSessionOnly('${escapeAttr(b.id)}')">Đóng ca</button>`);
      }
    }
    actions.push(`<button type="button" class="btn btn-ghost btn-sm" onclick="SpikeUI.closeDrawer('mgmtDetailDrawer')">Đóng</button>`);
    footEl.innerHTML = actions.join('');
  }

  SpikeUI.openDrawer('mgmtDetailDrawer', triggerEl);
}

function handleShopOrderFilterChange() {
  shopOrderPageSize = 25;
  renderStandaloneOrdersSection();
}

function loadMoreShopOrders() {
  shopOrderPageSize += 25;
  renderStandaloneOrdersSection();
}

function renderStandaloneOrdersSection() {
  const countEl = document.getElementById('standaloneOrdersCount');
  const listEl = document.getElementById('standaloneOrdersList');
  const pageEl = document.getElementById('shopOrdersPagination');
  if (!listEl) return;

  const allOrders = getEffectiveAdminShopOrders();
  if (countEl) countEl.textContent = allOrders.length;

  if (allOrders.length === 0) {
    if (pageEl) pageEl.style.display = 'none';
    listEl.innerHTML = `
      <div class="empty-state" style="padding:1.5rem 1rem">
        <div class="text-xs text-muted">Chưa có đơn hàng Pro Shop nào.</div>
      </div>`;
    return;
  }

  const q = (document.getElementById('shopOrderSearch')?.value || '').trim().toLowerCase();
  const mode = document.getElementById('shopOrderFilter')?.value || 'all';

  const filtered = allOrders.filter(o => {
    const isCancelled = o.status === 'cancelled';
    const fulfill = String(o.fulfillmentStatus || 'pending').toLowerCase();
    const total = Number(o.totalAmount) || 0;
    const paid = Number(o.paidAmount) || 0;

    if (mode === 'pending_fulfill' && (isCancelled || fulfill === 'delivered')) return false;
    if (mode === 'delivered' && (isCancelled || fulfill !== 'delivered')) return false;
    if (mode === 'unpaid' && (isCancelled || Boolean(o.bookingCode) || paid >= total)) return false;
    if (mode === 'cancelled' && !isCancelled) return false;

    if (q) {
      const hay = `${o.code || ''} ${o.customerName || ''} ${o.phone || ''} ${o.address || ''} ${o.bookingCode || ''}`.toLowerCase();
      if (!hay.includes(q)) return false;
    }
    return true;
  });

  if (filtered.length === 0) {
    if (pageEl) pageEl.style.display = 'none';
    listEl.innerHTML = `
      <div class="empty-state" style="padding:1.5rem 1rem">
        <div class="text-xs text-muted">Không có đơn hàng nào khớp bộ lọc hiện tại (${allOrders.length} đơn đang quản lý).</div>
      </div>`;
    return;
  }

  const visible = filtered.slice(0, shopOrderPageSize);

  listEl.innerHTML = visible.map(o => {
    const total = Number(o.totalAmount) || 0;
    const paid = Number(o.paidAmount) || 0;
    const rem = Math.max(0, total - paid);
    const isCancelled = o.status === 'cancelled';
    const fulfillStatus = String(o.fulfillmentStatus || 'pending').toLowerCase();
    const isDelivered = fulfillStatus === 'delivered';
    const isLinked = Boolean(o.bookingCode);
    const isBusyFulfill = _fulfillingOrderCodes.has(o.code);

    const fulfillBadgeHtml = isCancelled
      ? '<span class="badge badge-danger">Đã hủy · Đã hoàn kho</span>'
      : (isDelivered
        ? '<span class="badge badge-success">Đã giao đồ</span>'
        : '<span class="badge badge-warning">Chờ giao đồ</span>');

    const channelBadgeHtml = isLinked
      ? `<span class="badge badge-primary">Gộp sân ${escapeHtml(o.bookingCode)}</span>`
      : '<span class="badge badge-neutral">Đơn lẻ quầy</span>';

    const paymentSummaryHtml = isCancelled
      ? ''
      : (isLinked
        ? `Thanh toán theo hóa đơn sân <strong>${escapeHtml(o.bookingCode)}</strong> · Giá trị: <strong class="text-mono">${formatMoney(total)}</strong>`
        : `Phải thu: <strong class="text-mono">${formatMoney(total)}</strong> · Đã thu: <strong class="text-mono" style="color:var(--color-success)">${formatMoney(paid)}</strong>`);

    return `
      <div class="item-row" data-order-code="${escapeAttr(o.code)}" ${isCancelled ? 'style="opacity:0.65"' : ''}>
        <div class="item-avatar" style="background:var(--color-indigo-50);color:var(--color-indigo);">PS</div>
        <div class="item-info">
          <div class="item-name" style="flex-wrap:wrap;gap:0.35rem;">
            <span class="text-mono font-bold" style="color:var(--color-indigo)">${escapeHtml(o.code)}</span>
            <span>${escapeHtml(o.customerName)}</span>
            ${channelBadgeHtml}
            ${fulfillBadgeHtml}
            ${(!isCancelled && !isLinked) ? paymentBadgeHtml(paid, total, o.paymentStatus) : ''}
          </div>
          <div class="item-detail">
            SĐT: <span class="text-mono">${escapeHtml(o.phone)}</span> · ${escapeHtml(o.address || 'Nhận tại quầy')} · ${paymentSummaryHtml}
          </div>
        </div>
        <div class="item-actions" style="flex-wrap:wrap;">
          ${!isCancelled ? `
            ${!isDelivered ? `
              <button type="button" class="btn btn-success btn-sm" data-fulfill-code="${escapeAttr(o.code)}" ${isBusyFulfill ? 'disabled' : ''} onclick="fulfillAdminOrder('${escapeAttr(o.code)}')">
                <iconify-icon icon="lucide:package-check" width="14"></iconify-icon> ${isBusyFulfill ? 'Đang xử lý...' : 'Đã giao đồ'}
              </button>
            ` : ''}
            ${isLinked ? `
              <button type="button" class="btn btn-outline btn-sm" onclick="openLedgerModal('booking', '${escapeAttr(o.bookingCode)}')">
                <iconify-icon icon="lucide:wallet" width="14"></iconify-icon> Sổ cái sân
              </button>
            ` : `
              <button type="button" class="btn ${rem > 0 ? 'btn-primary' : 'btn-outline'} btn-sm" onclick="openLedgerModal('order', '${escapeAttr(o.code)}')">
                <iconify-icon icon="lucide:wallet" width="14"></iconify-icon> ${rem > 0 ? 'Thu tiền' : 'Sổ cái / Hoàn'}
              </button>
              ${paid === 0 ? `<button type="button" class="btn btn-danger-outline btn-sm" onclick="cancelStandaloneOrder('${escapeAttr(o.code)}')">Hủy &amp; Hoàn kho</button>` : ''}
            `}
          ` : ''}
        </div>
      </div>
    `;
  }).join('');

  if (pageEl) {
    pageEl.style.display = 'flex';
    pageEl.style.alignItems = 'center';
    pageEl.style.justifyContent = 'space-between';
    pageEl.style.gap = '0.5rem';
    pageEl.style.flexWrap = 'wrap';
    const remaining = filtered.length - visible.length;
    pageEl.innerHTML = `
      <span class="text-xs text-muted">Đang hiển thị <strong>${visible.length}</strong> / <strong>${filtered.length}</strong> đơn hàng${filtered.length !== allOrders.length ? ` (lọc từ ${allOrders.length} đơn)` : ''}.</span>
      ${remaining > 0 ? `<button type="button" class="btn btn-outline btn-sm" id="btnLoadMoreShopOrders" onclick="loadMoreShopOrders()">Xem thêm (+${Math.min(25, remaining)} đơn)</button>` : ''}
    `;
  }
}

async function fulfillAdminOrder(orderCode) {
  if (!orderCode || _fulfillingOrderCodes.has(orderCode)) return;
  _fulfillingOrderCodes.add(orderCode);
  renderStandaloneOrdersSection();

  try {
    const res = await apiFetch(`/api/admin/orders/${encodeURIComponent(orderCode)}/fulfill`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' }
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      showToast('Không thể cập nhật giao hàng', data.error || 'Đơn hàng đã bị hủy hoặc không hợp lệ.', 'error');
      await Promise.all([syncDatabaseFromServer(true), loadAdminShopOrders()]);
      return;
    }

    await Promise.all([syncDatabaseFromServer(true), loadAdminShopOrders()]);
    renderManagement();
    showToast('Đã giao hàng Pro Shop!', `Đơn ${orderCode} đã được xác nhận Hoàn tất / Đã giao đồ.`, 'success');
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể cập nhật trạng thái giao hàng lúc này.', 'error');
  } finally {
    _fulfillingOrderCodes.delete(orderCode);
    renderStandaloneOrdersSection();
  }
}

async function approveBooking(id) {
  try {
    const res = await apiFetch(`/api/admin/bookings/${encodeURIComponent(id)}/approve`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' }
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      showToast('Không thể duyệt vé', data.error || 'Khung giờ bị trùng.', 'error');
      return;
    }

    const b = data.booking;
    const courtName = getCourtDisplayName(b.courtId, b.courtName);
    const qrPlaceholder = document.querySelector('#modalQR .qr-placeholder');
    if (qrPlaceholder) {
      qrPlaceholder.innerHTML = `
        ${renderBookingQrHtml(b, 156)}
        <div class="text-xs font-bold text-mono mt-sm" id="qrRef">${escapeHtml(b.id)}</div>
      `;
    }
    document.getElementById('qrName').textContent = `${b.name} (${b.phone})`;
    document.getElementById('qrCourt').textContent = courtName;
    document.getElementById('qrTime').textContent = `${b.time} (${b.duration} phút) · ${formatDateVN(b.date)}`;
    openModal('modalQR');
    showToast('Đã duyệt đơn', `Đơn ${b.id} của ${b.name} đã chuyển sang Đã duyệt.`, 'success');
    await syncDatabaseFromServer(true);
    renderManagement();
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể duyệt đơn lúc này.', 'error');
  }
}

async function approveAllPending() {
  if (_approvingBatch) return;
  const pendings = BOOKINGS.filter(b => b.status === 'pending' && isPendingBookingHoldActive(b));
  if (pendings.length === 0) {
    await syncDatabaseFromServer(true);
    renderManagement();
    showToast('Không có đơn hợp lệ', 'Không còn đơn chờ duyệt nào đang trong thời hạn giữ chỗ.', 'info');
    return;
  }

  if (!confirm(`Xác nhận duyệt hàng loạt ${pendings.length} yêu cầu đặt sân đang giữ chỗ hợp lệ?`)) {
    return;
  }

  const btn = document.getElementById('approveAllBtn');
  _approvingBatch = true;
  if (btn) btn.disabled = true;

  let successCount = 0;
  let conflictOrExpiredCount = 0;
  let failedCount = 0;

  try {
    for (const b of pendings) {
      try {
        const res = await apiFetch(`/api/admin/bookings/${encodeURIComponent(b.id)}/approve`, { method: 'POST' });
        if (res.ok) {
          successCount++;
        } else if (res.status === 409) {
          conflictOrExpiredCount++;
        } else {
          failedCount++;
        }
      } catch (_) {
        failedCount++;
      }
    }

    await syncDatabaseFromServer(true);
    renderManagement();

    const parts = [`Đã duyệt thành công ${successCount}/${pendings.length} đơn`];
    if (conflictOrExpiredCount > 0) parts.push(`${conflictOrExpiredCount} đơn trùng lịch hoặc hết giữ chỗ`);
    if (failedCount > 0) parts.push(`${failedCount} đơn gặp lỗi máy chủ`);
    const level = (failedCount > 0 || conflictOrExpiredCount > 0) ? (successCount > 0 ? 'warning' : 'error') : 'success';
    showToast(successCount > 0 ? 'Kết quả duyệt hàng loạt' : 'Chưa duyệt được đơn nào', `${parts.join(' · ')}.`, level);
  } finally {
    _approvingBatch = false;
    if (btn) btn.disabled = false;
  }
}

async function rejectBooking(id) {
  if (!confirm(`Xác nhận từ chối và hủy yêu cầu đặt sân ${id}?`)) return;
  try {
    const res = await apiFetch(`/api/admin/bookings/${encodeURIComponent(id)}/reject`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ reason: 'Lễ tân từ chối yêu cầu đặt sân' })
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      showToast('Không thể từ chối', data.error || 'Đã xảy ra lỗi.', 'error');
      return;
    }
    await syncDatabaseFromServer(true);
    showToast('Đã từ chối đơn', `Đơn ${id} đã được hủy và giải phóng khung giờ sân.`, 'success');
    renderManagement();
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể hủy đơn lúc này.', 'error');
  }
}

async function startMatchNow(id) {
  try {
    const res = await apiFetch('/api/checkin/verify', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ tokenOrCode: id, requireSignedQr: false })
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      showToast('Không thể Check-in nhận sân', data.error || 'Chỉ vé Đã duyệt và trong khung giờ hợp lệ mới được nhận sân.', 'warning');
      return;
    }
    await syncDatabaseFromServer(true);
    showToast('Đã kích hoạt sân!', `Khách ${data.booking.name} đã nhận sân và bắt đầu thi đấu.`, 'success');
    renderManagement();
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể kích hoạt ca sân.', 'error');
  }
}

/**
 * P1-04 & P1-05: Closes court session WITHOUT creating fake payment records!
 */
async function finishSessionOnly(id) {
  try {
    const res = await apiFetch(`/api/admin/bookings/${encodeURIComponent(id)}/finish`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ note: 'Đóng ca chơi trên sân' })
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      showToast('Không thể đóng ca', data.error || 'Đã xảy ra lỗi.', 'error');
      return;
    }
    await syncDatabaseFromServer(true);
    const b = data.booking;
    const due = Number(b.amountDue) || 0;
    const paid = Number(b.paidAmount) || 0;
    showToast(
      'Đã kết thúc ca sân',
      paid >= due
        ? `Ca ${id} đã đóng (Đã thu đủ ${formatMoney(paid)}).`
        : `Ca ${id} đã đóng. Công nợ còn lại ${formatMoney(due - paid)} — bấm "Thu nợ" khi khách thanh toán.`,
      'success'
    );
    renderManagement();
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể kết thúc ca sân.', 'error');
  }
}

/* ---------- LEDGER SETTLEMENT & REFUND MODAL (P1-04, P1-05, P1-15) ---------- */
function toggleTransferRefRequirement() {
  const method = document.getElementById('ledgerPayMethod')?.value || 'cash';
  const lbl = document.getElementById('ledgerPayRefLabel');
  if (lbl) {
    lbl.textContent = method === 'transfer' ? 'Mã tham chiếu / UNC ngân hàng *' : 'Mã tham chiếu (Tùy chọn)';
  }
}

function refreshLedgerModalFields(kind, code) {
  let title = '';
  let subtitle = '';
  let due = 0;
  let paid = 0;
  let summaryHtml = '';
  let isPlayingBooking = false;

  if (kind === 'booking') {
    const b = BOOKINGS.find(x => x.id === code);
    if (!b) return false;
    isPlayingBooking = b.status === 'playing';
    const courtName = getCourtDisplayName(b.courtId, b.courtName);
    const posExtra = Number(b.posTotal) || (b.posItems || []).reduce((s, i) => s + Number(i.price || 0) * Number(i.qty || 0), 0);
    const courtFee = Number(b.courtFee) || 0;
    const disc = Number(b.discountAmount) || 0;
    due = Number(b.amountDue) || Math.max(0, courtFee - disc + posExtra);
    paid = Number(b.paidAmount) || 0;

    title = `Sổ Cái Ca Sân — ${b.id}`;
    subtitle = `${b.name} (${b.phone}) · ${courtName} · ${b.time}`;
    summaryHtml = `
      <div class="payment-line"><span class="text-muted">Tiền thuê sân (${Number(b.duration)}p)</span><span class="font-semibold text-mono">${formatMoney(courtFee)}</span></div>
      ${disc > 0 ? `<div class="payment-line"><span class="text-muted">Chiết khấu hội viên</span><span class="font-semibold text-mono" style="color:var(--color-success)">-${formatMoney(disc)}</span></div>` : ''}
      <div class="payment-line"><span class="text-muted">Dịch vụ POS / Pro Shop gộp sân</span><span class="font-semibold text-mono">${formatMoney(posExtra)}</span></div>
      <div class="payment-line"><span class="text-muted">Tổng phải thu (AmountDue)</span><span class="font-bold text-mono">${formatMoney(due)}</span></div>
      <div class="payment-line"><span class="text-muted">Đã thu ròng trên Sổ cái</span><span class="font-bold text-mono" style="color:var(--color-success)">${formatMoney(paid)}</span></div>
      <div class="payment-line total"><span>Còn phải thu</span><span class="text-mono" style="color:${due - paid > 0 ? 'var(--color-primary)' : 'var(--color-success)'}">${formatMoney(Math.max(0, due - paid))}</span></div>
    `;
  } else {
    const orders = getEffectiveAdminShopOrders();
    const o = orders.find(x => x.code === code) || STANDALONE_ORDERS.find(x => x.code === code);
    if (!o) return false;
    due = Number(o.totalAmount) || 0;
    paid = Number(o.paidAmount) || 0;
    title = `Sổ Cái Đơn Lẻ Pro Shop — ${o.code}`;
    subtitle = `${o.customerName} (${o.phone}) · ${o.address || 'Nhận tại quầy'}`;
    summaryHtml = `
      <div class="payment-line"><span class="text-muted">Tổng giá trị đơn hàng</span><span class="font-bold text-mono">${formatMoney(due)}</span></div>
      <div class="payment-line"><span class="text-muted">Đã thu ròng trên Sổ cái</span><span class="font-bold text-mono" style="color:var(--color-success)">${formatMoney(paid)}</span></div>
      <div class="payment-line total"><span>Còn phải thu</span><span class="text-mono">${formatMoney(Math.max(0, due - paid))}</span></div>
    `;
  }

  document.getElementById('ledgerModalTitle').textContent = title;
  document.getElementById('ledgerModalSubtitle').textContent = subtitle;
  document.getElementById('ledgerSummaryBox').innerHTML = summaryHtml;

  const rem = Math.max(0, due - paid);
  const collectSec = document.getElementById('ledgerCollectSection');
  if (collectSec) collectSec.style.display = rem > 0 ? 'block' : 'none';

  const amtIn = document.getElementById('ledgerPayAmount');
  if (amtIn) amtIn.value = rem > 0 ? rem : '';

  const finishWrap = document.getElementById('ledgerAlsoFinishWrap');
  const finishChk = document.getElementById('ledgerAlsoFinishSession');
  if (finishWrap && finishChk) {
    finishWrap.style.display = kind === 'booking' && isPlayingBooking ? 'flex' : 'none';
    finishChk.checked = kind === 'booking' && isPlayingBooking;
  }

  const refundDetails = document.getElementById('ledgerRefundDetails');
  if (refundDetails) {
    refundDetails.style.display = paid > 0 ? 'block' : 'none';
    const refAmt = document.getElementById('ledgerRefundAmount');
    if (refAmt) refAmt.value = paid;
  }
  return true;
}

async function openLedgerModal(kind, code) {
  activeLedgerContext = { kind, code };
  toggleTransferRefRequirement();

  const partialBanner = document.getElementById('ledgerPartialFailureBanner');
  if (partialBanner) partialBanner.style.display = 'none';
  const payBtn = document.getElementById('btnSubmitLedgerPay');
  if (payBtn) payBtn.disabled = false;

  if (!refreshLedgerModalFields(kind, code)) return;

  document.getElementById('ledgerPayRef').value = '';
  document.getElementById('ledgerPayNote').value = '';
  const refundDetails = document.getElementById('ledgerRefundDetails');
  if (refundDetails) {
    refundDetails.open = false;
    document.getElementById('ledgerRefundRef').value = '';
    document.getElementById('ledgerRefundReason').value = '';
  }

  openModal('modalLedgerSettlement');
  await loadLedgerTransactions(kind, code);
}

async function loadLedgerTransactions(kind, code) {
  const box = document.getElementById('ledgerTxHistoryList');
  if (!box) return;
  box.innerHTML = `<div class="text-xs text-muted" style="padding:0.65rem;">Đang tải bút toán...</div>`;

  if (kind !== 'booking') {
    box.innerHTML = `<div class="text-xs text-muted" style="padding:0.65rem;">Các bút toán đơn lẻ được tổng hợp trực tiếp trong Báo cáo Sổ cái (/Reports).</div>`;
    return;
  }

  try {
    const res = await fetch(`/api/admin/bookings/${encodeURIComponent(code)}/transactions`, {
      headers: { 'Accept': 'application/json' },
      credentials: 'same-origin',
      cache: 'no-store'
    });
    const data = await res.json().catch(() => ({}));
    const list = data.transactions || [];
    if (list.length === 0) {
      box.innerHTML = `<div class="text-xs text-muted" style="padding:0.65rem;">Chưa có giao dịch thu/hoàn tiền nào được ghi nhận.</div>`;
      return;
    }

    box.innerHTML = list.map(t => {
      const isRefund = String(t.transactionType || t.type).toLowerCase() === 'refund' || Number(t.amount) < 0;
      const sign = isRefund ? '−' : '+';
      const color = isRefund ? 'var(--color-danger)' : 'var(--color-success)';
      return `
        <div style="display:flex;justify-content:space-between;align-items:center;padding:0.45rem 0.65rem;border-bottom:1px solid var(--border-light);font-size:0.76rem;">
          <div>
            <strong style="color:${color}">${isRefund ? 'Hoàn tiền' : 'Thu tiền'} (${escapeHtml(t.paymentMethod)})</strong>
            ${t.referenceCode ? ` · <span class="text-mono">Ref: ${escapeHtml(t.referenceCode)}</span>` : ''}
            <div class="text-muted">${escapeHtml(t.createdAt || '')} · bởi ${escapeHtml(t.recordedBy || 'Staff')}</div>
          </div>
          <strong class="text-mono" style="color:${color}">${sign}${formatMoney(Math.abs(Number(t.amount) || 0))}</strong>
        </div>
      `;
    }).join('');
  } catch (_) {
    box.innerHTML = `<div class="text-xs text-muted" style="padding:0.65rem;">Không thể tải lịch sử giao dịch.</div>`;
  }
}

async function submitLedgerPayment() {
  if (!activeLedgerContext || _ledgerPaySubmitting) return;
  const amount = parseInt(document.getElementById('ledgerPayAmount')?.value || '0', 10);
  const paymentMethod = document.getElementById('ledgerPayMethod')?.value || 'cash';
  const referenceCode = (document.getElementById('ledgerPayRef')?.value || '').trim();
  const note = (document.getElementById('ledgerPayNote')?.value || '').trim();
  const alsoFinish = Boolean(document.getElementById('ledgerAlsoFinishSession')?.checked);

  if (!amount || amount <= 0) {
    showToast('Số tiền không hợp lệ', 'Vui lòng nhập số tiền thực thu lớn hơn 0 đ.', 'warning');
    return;
  }

  if (paymentMethod === 'transfer' && !referenceCode) {
    showToast('Thiếu mã tham chiếu chuyển khoản', 'Giao dịch chuyển khoản bắt buộc nhập Mã tham chiếu / UNC ngân hàng để đối soát.', 'warning');
    return;
  }

  const endpoint = activeLedgerContext.kind === 'booking'
    ? `/api/admin/bookings/${encodeURIComponent(activeLedgerContext.code)}/payments`
    : `/api/admin/orders/${encodeURIComponent(activeLedgerContext.code)}/payments`;

  const btn = document.getElementById('btnSubmitLedgerPay');
  _ledgerPaySubmitting = true;
  if (btn) btn.disabled = true;

  try {
    const res = await apiFetch(endpoint, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        amount,
        paymentMethod,
        referenceCode,
        note,
        idempotencyKey: generateIdempotencyKey('PAY')
      })
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      showToast('Không thể ghi sổ thu tiền', data.error || 'Vui lòng kiểm tra lại số tiền hoặc mã tham chiếu.', 'error');
      return;
    }

    if (activeLedgerContext.kind === 'booking' && alsoFinish) {
      let finishOk = false;
      let finishError = 'Không thể tự động đóng ca sau khi thu tiền.';
      try {
        const finishRes = await apiFetch(`/api/admin/bookings/${encodeURIComponent(activeLedgerContext.code)}/finish`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ note: 'Đóng ca sau khi thu ngân xác nhận thanh toán' })
        });
        const finishData = await finishRes.json().catch(() => ({}));
        if (finishRes.ok && finishData.ok) {
          finishOk = true;
        } else {
          finishError = finishData.error || `Máy chủ từ chối đóng ca (HTTP ${finishRes.status}).`;
        }
      } catch (_) {
        finishError = 'Mất kết nối mạng khi đóng ca chơi.';
      }

      if (!finishOk) {
        await Promise.all([syncDatabaseFromServer(true), loadAdminShopOrders()]);
        renderManagement();
        refreshLedgerModalFields(activeLedgerContext.kind, activeLedgerContext.code);
        await loadLedgerTransactions(activeLedgerContext.kind, activeLedgerContext.code);
        const banner = document.getElementById('ledgerPartialFailureBanner');
        const detail = document.getElementById('ledgerPartialFailureDetail');
        if (detail) {
          detail.textContent = `Số tiền ${formatMoney(amount)} ĐÃ ĐƯỢC LƯU vào Sổ cái. Lỗi đóng ca: ${finishError} Vui lòng bấm nút bên dưới để chỉ đóng ca lại mà không tạo bút toán thu trùng.`;
        }
        if (banner) banner.style.display = 'block';
        showToast('Đã thu tiền — Chưa đóng ca', `Đã ghi sổ ${formatMoney(amount)} thành công, nhưng đóng ca thất bại: ${finishError}`, 'warning');
        return;
      }
    }

    await Promise.all([syncDatabaseFromServer(true), loadAdminShopOrders()]);
    renderManagement();
    closeModal('modalLedgerSettlement');
    showToast('Đã ghi sổ thu tiền!', `Đã ghi nhận thực thu ${formatMoney(amount)} (${paymentMethod === 'transfer' ? 'Chuyển khoản' : 'Tiền mặt'}).`, 'success');
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể lưu bút toán thu tiền.', 'error');
  } finally {
    _ledgerPaySubmitting = false;
    if (btn) btn.disabled = false;
  }
}

async function retryLedgerFinishOnly() {
  if (!activeLedgerContext || activeLedgerContext.kind !== 'booking') return;
  const retryBtn = document.getElementById('btnRetryLedgerFinishOnly');
  if (retryBtn) retryBtn.disabled = true;
  try {
    const finishRes = await apiFetch(`/api/admin/bookings/${encodeURIComponent(activeLedgerContext.code)}/finish`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ note: 'Đóng ca sau khi thu ngân xác nhận thanh toán' })
    });
    const finishData = await finishRes.json().catch(() => ({}));
    if (!finishRes.ok || !finishData.ok) {
      showToast('Chưa thể đóng ca', finishData.error || 'Vui lòng kiểm tra lại trạng thái ca chơi.', 'error');
      return;
    }

    await Promise.all([syncDatabaseFromServer(true), loadAdminShopOrders()]);
    renderManagement();
    closeModal('modalLedgerSettlement');
    showToast('Đã đóng ca sân!', `Ca ${activeLedgerContext.code} đã đóng thành công (không phát sinh bút toán thu tiền mới).`, 'success');
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể đóng ca sân lúc này.', 'error');
  } finally {
    if (retryBtn) retryBtn.disabled = false;
  }
}

async function submitLedgerRefund() {
  if (!activeLedgerContext) return;
  const amount = parseInt(document.getElementById('ledgerRefundAmount')?.value || '0');
  const paymentMethod = document.getElementById('ledgerRefundMethod')?.value || 'cash';
  const referenceCode = (document.getElementById('ledgerRefundRef')?.value || '').trim();
  const reason = (document.getElementById('ledgerRefundReason')?.value || '').trim();

  if (!amount || amount <= 0) {
    showToast('Số tiền hoàn không hợp lệ', 'Vui lòng nhập số tiền cần hoàn > 0 đ.', 'warning');
    return;
  }
  if (!reason) {
    showToast('Nhập lý do hoàn tiền', 'Vui lòng ghi rõ lý do hoàn tiền để lưu kiểm toán.', 'warning');
    return;
  }

  const endpoint = activeLedgerContext.kind === 'booking'
    ? `/api/admin/bookings/${encodeURIComponent(activeLedgerContext.code)}/refunds`
    : `/api/admin/orders/${encodeURIComponent(activeLedgerContext.code)}/refunds`;

  try {
    const res = await apiFetch(endpoint, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        amount,
        paymentMethod,
        referenceCode,
        reason,
        idempotencyKey: generateIdempotencyKey('REF')
      })
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      showToast('Không thể hoàn tiền', data.error || 'Số tiền hoàn vượt quá số thực thu.', 'error');
      return;
    }

    await Promise.all([syncDatabaseFromServer(true), loadAdminShopOrders()]);
    renderManagement();
    closeModal('modalLedgerSettlement');
    showToast('Đã ghi sổ hoàn tiền', `Đã hoàn ${formatMoney(amount)} cho đơn ${activeLedgerContext.code}.`, 'success');
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể thực hiện bút toán hoàn tiền.', 'error');
  }
}

async function cancelStandaloneOrder(orderCode) {
  if (!confirm(`Xác nhận hủy đơn hàng lẻ ${orderCode} và hoàn trả tồn kho các mặt hàng?`)) return;
  try {
    const res = await apiFetch(`/api/admin/orders/${encodeURIComponent(orderCode)}/cancel`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ reason: 'Hủy đơn lẻ tại quầy và hoàn kho' })
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      showToast('Không thể hủy đơn', data.error || 'Đơn đã thu tiền cần hoàn tiền trước khi hủy.', 'error');
      return;
    }
    await Promise.all([syncDatabaseFromServer(true), loadAdminShopOrders()]);
    renderManagement();
    showToast('Đã hủy đơn & hoàn kho', `Đơn ${orderCode} đã hủy và cộng lại số lượng tồn kho.`, 'success');
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể hủy đơn hàng.', 'error');
  }
}

function selectLockCourt(id) {
  lockSelectedCourt = Number(id);
  renderManagement();
}

async function lockCourt() {
  if (!COURTS.length) {
    showToast('Chưa có sân khả dụng', 'Hệ thống chưa tải được danh sách sân đang hoạt động từ SQL Server.', 'error');
    return;
  }
  const court = getCourtById(lockSelectedCourt);
  if (!court) {
    showToast('Sân không hợp lệ', 'Vui lòng chọn lại sân cần khóa trước khi áp dụng.', 'error');
    return;
  }

  const { openStr, openMins, closeStr, closeMins } = getClubOperatingWindowMinutes();
  const date = document.getElementById('lockDate')?.value || getTodayStr();
  const durRaw = document.getElementById('lockDuration')?.value || '60';
  const isFullDay = durRaw === 'full_day';
  const startTime = isFullDay ? openStr : (document.getElementById('lockStart')?.value || openStr);
  const duration = isFullDay ? (closeMins - openMins) : (Number(durRaw) || 60);
  const startMins = timeToMinutes(startTime);
  const endMins = startMins + duration;

  if (date < getTodayStr()) {
    showToast('Ngày khóa không hợp lệ', 'Không thể tạo khóa bảo trì cho ngày trong quá khứ.', 'warning');
    return;
  }

  if (endMins > 24 * 60 || endMins > closeMins) {
    showToast(
      'Khung giờ khóa vượt giới hạn',
      `Khung khóa (${startTime} + ${duration}p) vượt quá giờ đóng cửa ${closeStr}. Vui lòng chọn giờ sớm hơn hoặc chọn "Cả ngày".`,
      'warning'
    );
    return;
  }

  const reason = (document.getElementById('lockReason')?.value || 'Bảo dưỡng định kỳ').trim();
  const btn = document.getElementById('btnCreateCourtLock');
  if (btn) btn.disabled = true;

  try {
    const res = await apiFetch('/api/admin/court-locks', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        courtId: court.id,
        date,
        startTime,
        duration,
        reason
      })
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      showToast('Không thể khóa sân', data.error || 'Khung giờ đang có khách đặt.', 'error');
      return;
    }
    await syncDatabaseFromServer(true);
    showToast('Đã khóa sân', `${court.name} đã tạm khóa (${startTime} → ${minutesToTime(endMins)}, ${duration}p) để: ${reason}`, 'warning');
    renderManagement();
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể áp dụng lệnh khóa sân.', 'error');
  } finally {
    if (btn) btn.disabled = !HAS_SERVER_STATE || !COURTS.length;
  }
}

async function unlockCourt(lockId) {
  try {
    const res = await apiFetch(`/api/admin/court-locks/${encodeURIComponent(lockId)}`, {
      method: 'DELETE'
    });
    if (!res.ok) {
      showToast('Không thể mở khóa', 'Vui lòng thử lại.', 'error');
      return;
    }
    await syncDatabaseFromServer(true);
    showToast('Đã mở khóa sân', 'Sân đã sẵn sàng nhận khách đặt lịch trở lại.', 'success');
    renderManagement();
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể mở khóa sân.', 'error');
  }
}

/* ---------- POS SYSTEM (P1-10, P1-14) ---------- */
function openPos(bookingId) {
  posBooking = BOOKINGS.find(b => b.id === bookingId);
  if (!posBooking) return;
  posCart = JSON.parse(JSON.stringify(posBooking.posItems || []));
  posCourtDuration = Number(posBooking.duration) || 60;

  const courtName = getCourtDisplayName(posBooking.courtId, posBooking.courtName);
  document.getElementById('posCustomer').textContent = `${posBooking.name} (${posBooking.phone})`;
  document.getElementById('posCourtInfo').textContent = `${courtName} · Bắt đầu: ${posBooking.time} · Mã: ${posBooking.id}`;

  const moveSel = document.getElementById('posMoveCourtSelect');
  if (moveSel) {
    moveSel.innerHTML = COURTS.map(c =>
      `<option value="${Number(c.id)}" ${Number(c.id) === Number(posBooking.courtId) ? 'selected' : ''}>${escapeHtml(c.name)}</option>`
    ).join('');
  }

  updatePosCourtFee();
  renderPosMenu();
  renderPosCart();
  openModal('modalPOS');
}

async function movePosBookingCourt() {
  if (!posBooking) return;
  const targetCourtId = Number(document.getElementById('posMoveCourtSelect')?.value || 0);
  if (!targetCourtId || targetCourtId === Number(posBooking.courtId)) {
    showToast('Chọn sân khác', 'Vui lòng chọn sân đích khác với sân hiện tại.', 'warning');
    return;
  }

  try {
    const res = await apiFetch(`/api/admin/bookings/${encodeURIComponent(posBooking.id)}/move-court`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ targetCourtId })
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      showToast('Không thể chuyển sân', data.error || 'Sân đích đang bận hoặc khóa bảo trì.', 'error');
      return;
    }
    posBooking = data.booking;
    await syncDatabaseFromServer(true);
    const courtName = getCourtDisplayName(posBooking.courtId, posBooking.courtName);
    document.getElementById('posCourtInfo').textContent = `${courtName} · Bắt đầu: ${posBooking.time} · Mã: ${posBooking.id}`;
    updatePosCourtFee();
    updatePosTotal();
    renderManagement();
    showToast('Đã chuyển sân!', `Ca ${posBooking.id} đã chuyển sang ${courtName}.`, 'success');
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể chuyển sân lúc này.', 'error');
  }
}

function updatePosCourtFee() {
  if (!posBooking) return;
  const fee = Number(posBooking.courtFee) || calculateSegmentedPricing(posBooking.courtId, posBooking.time, posCourtDuration).netCourtFee;
  document.getElementById('posCourtDuration').textContent = `${posCourtDuration} phút (${posBooking.time} → ${getEndTime(posBooking.time, posCourtDuration)})`;
  document.getElementById('posCourtFee').textContent = formatMoney(fee);
}

async function adjustCourtTime(delta) {
  if (!posBooking || delta <= 0) return;
  try {
    const res = await apiFetch(`/api/admin/bookings/${encodeURIComponent(posBooking.id)}/extend`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ addMinutes: 30 })
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      showToast('Không thể gia hạn sân!', data.error || 'Khung giờ kế tiếp đã có người đặt hoặc vượt giờ đóng cửa.', 'error');
      return;
    }

    posBooking = data.booking;
    posCourtDuration = Number(posBooking.duration);
    await syncDatabaseFromServer(true);
    updatePosCourtFee();
    updatePosTotal();
    showToast('Đã gia hạn +30 phút', `Thời lượng mới: ${posCourtDuration} phút (${posBooking.time} → ${getEndTime(posBooking.time, posCourtDuration)})`, 'success');
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể gia hạn giờ sân.', 'error');
  }
}

function renderPosMenu() {
  const grid = document.getElementById('posMenuGrid');
  if (!grid) return;
  const menuData = Array.isArray(MENU) ? MENU : [];
  grid.innerHTML = menuData.map(item => {
    const stock = typeof item.stock === 'number' ? item.stock : 0;
    const outOfStock = stock <= 0;
    const rawImg = String(item.imageUrl || item.image || '').trim();
    const imgUrl = (rawImg.startsWith('/uploads/products/') || rawImg.startsWith('/assets/products/'))
      ? rawImg
      : '/assets/products/placeholder-default.svg';
    return `
      <button type="button" class="pos-menu-item" data-pos-product-id="${Number(item.id)}" ${outOfStock ? 'disabled style="opacity:0.5"' : ''} onclick="addToCart(${Number(item.id)})">
        <div class="pos-menu-item-media">
          <img class="pos-menu-item-thumb"
               src="${escapeHtml(imgUrl)}"
               alt="${escapeHtml(item.name)}"
               width="56"
               height="56"
               loading="lazy"
               decoding="async"
               onerror="this.onerror=null;this.src='/assets/products/placeholder-default.svg';" />
        </div>
        <div class="pos-menu-item-name">${escapeHtml(item.name)}</div>
        <div class="pos-menu-item-price text-mono" style="white-space:nowrap;">${formatMoney(item.price)}</div>
        <div style="font-size:0.68rem;color:var(--text-muted);white-space:nowrap;">(${outOfStock ? 'Hết hàng' : `Kho: ${stock}`})</div>
      </button>
    `;
  }).join('');
}

async function addToCart(itemId) {
  if (!posBooking) return;
  await updateCartQty(itemId, 1);
}

async function updateCartQty(itemId, delta) {
  if (!posBooking) return;
  try {
    const res = await apiFetch(`/api/admin/bookings/${encodeURIComponent(posBooking.id)}/pos-item`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ productId: Number(itemId), deltaQty: Number(delta) })
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      showToast('Không thể cập nhật món', data.error || 'Vượt quá tồn kho.', 'warning');
      return;
    }

    posBooking = data.booking;
    posCart = JSON.parse(JSON.stringify(posBooking.posItems || []));
    await syncDatabaseFromServer(true);
    renderPosMenu();
    renderPosCart();
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể cập nhật giỏ hàng POS.', 'error');
  }
}

function renderPosCart() {
  const container = document.getElementById('posCartDynamic');
  if (!container) return;

  if (posCart.length === 0) {
    container.innerHTML = `<div class="text-xs text-muted" style="text-align:center;padding:1rem">Chưa gọi thêm nước uống / dịch vụ phụ.</div>`;
  } else {
    container.innerHTML = posCart.map(item => {
      const total = Number(item.price) * Number(item.qty);
      return `
        <div class="pos-cart-item">
          <div class="pos-cart-item-info">
            <div class="pos-cart-item-name">${escapeHtml(item.name)}</div>
            <div class="pos-cart-item-price text-mono">${formatMoney(item.price)}</div>
          </div>
          <div class="pos-qty">
            <button type="button" onclick="updateCartQty(${Number(item.id)}, -1)">−</button>
            <span class="text-mono font-bold" style="min-width:18px;text-align:center">${Number(item.qty)}</span>
            <button type="button" onclick="updateCartQty(${Number(item.id)}, 1)">+</button>
          </div>
          <div class="pos-cart-item-total text-mono">${formatMoney(total)}</div>
        </div>
      `;
    }).join('');
  }
  updatePosTotal();
}

function updatePosTotal() {
  if (!posBooking) return;
  const due = Number(posBooking.amountDue) || (
    (Number(posBooking.courtFee) || 0) -
    (Number(posBooking.discountAmount) || 0) +
    posCart.reduce((sum, item) => sum + Number(item.price) * Number(item.qty), 0)
  );
  document.getElementById('posTotal').textContent = formatMoney(due);
}

async function savePosDraft() {
  if (!posBooking) return;
  await syncDatabaseFromServer(true);
  closeModal('modalPOS');
  showToast('Đã đồng bộ POS', `Giỏ hàng POS của ${posBooking.name} đã lưu trên máy chủ.`, 'success');
  renderManagement();
}

async function openBookingSettlementFromPos() {
  if (!posBooking) return;
  const code = posBooking.id;
  await syncDatabaseFromServer(true);
  closeModal('modalPOS');
  await openLedgerModal('booking', code);
}

document.addEventListener('DOMContentLoaded', initManagement);
window.addEventListener('pickleball:db-synced', () => {
  populateLockStartOptions();
  renderManagement();
  loadAdminShopOrders();
});
window.addEventListener('pickleball:sync-state', () => {
  renderManagement();
});
