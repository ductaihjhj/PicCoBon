/* ==========================================================================
   SPIKE PICKLEBALL CLUB — MANAGEMENT, DISPATCH, POS & LEDGER CONTROLLER (V2)
   Separates Session Finish vs Payment/Refund Ledger + Standalone Shop Orders
   ========================================================================== */

let lockSelectedCourt = 1;
let mgmtFilter = 'active';
let posBooking = null;
let posCart = [];
let posCourtDuration = 60;
let activeLedgerContext = null; // { kind: 'booking' | 'order', code: string }

function initManagement() {
  if (!requireRole('admin')) return;

  const lockDateEl = document.getElementById('lockDate');
  if (lockDateEl && !lockDateEl.value) lockDateEl.value = getTodayStr();

  populateLockStartOptions();
  renderManagement();
}

function populateLockStartOptions() {
  const lockSel = document.getElementById('lockStart');
  if (!lockSel) return;
  const openMins = timeToMinutes(CMS_CONFIG.openTime || '06:00') || 360;
  const closeMins = timeToMinutes(CMS_CONFIG.closeTime || '22:00') || 1320;
  const curVal = lockSel.value || '12:00';
  let html = '';
  for (let m = openMins; m <= closeMins - 30; m += 30) {
    const t = minutesToTime(m);
    html += `<option value="${t}">${t}</option>`;
  }
  lockSel.innerHTML = html;
  if ([...lockSel.options].some(o => o.value === curVal)) {
    lockSel.value = curVal;
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

  const pendings = BOOKINGS.filter(b => b.status === 'pending');
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
      const court = getCourtById(b.courtId);
      const endTime = getEndTime(b.time, b.duration);
      const expectedFee = Number(b.amountDue) || Number(b.courtFee) || calcBookingPrice(b.courtId, b.time, b.duration);
      return `
        <div class="item-row">
          <div class="item-avatar" style="background:var(--color-warning-light);color:#B45309">S${Number(court.id)}</div>
          <div class="item-info">
            <div class="item-name">
              <span>${escapeHtml(b.name)}</span>
              <span class="badge badge-neutral text-mono">${escapeHtml(b.id)}</span>
              <span class="badge badge-warning">${escapeHtml(formatDateVN(b.date))}</span>
            </div>
            <div class="item-detail">
              <strong>${escapeHtml(court.name)}</strong> · <span class="text-mono font-bold">${escapeHtml(b.time)} → ${escapeHtml(endTime)}</span> (${Number(b.duration)}p) · SĐT: <span class="text-mono">${escapeHtml(b.phone)}</span> · Phải thu: <strong class="text-mono" style="color:var(--color-primary)">${formatMoney(expectedFee)}</strong>
            </div>
          </div>
          <div class="item-actions">
            <button class="btn btn-success btn-sm" onclick="approveBooking('${escapeAttr(b.id)}')">
              <iconify-icon icon="lucide:check" width="14"></iconify-icon> Duyệt vé
            </button>
            <button class="btn btn-danger-outline btn-sm" onclick="rejectBooking('${escapeAttr(b.id)}')">
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
      const court = getCourtById(b.courtId);
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
          <button class="btn btn-primary btn-sm" onclick="startMatchNow('${escapeAttr(b.id)}')">
            <iconify-icon icon="lucide:play" width="14"></iconify-icon> Nhận sân
          </button>
          <button class="btn btn-outline btn-sm" onclick="openPos('${escapeAttr(b.id)}')">
            <iconify-icon icon="lucide:coffee" width="14"></iconify-icon> POS (${posCount})
          </button>
          <button class="btn btn-outline btn-sm" onclick="openLedgerModal('booking', '${escapeAttr(b.id)}')">
            <iconify-icon icon="lucide:wallet" width="14"></iconify-icon> Sổ thu
          </button>
        `;
      } else if (isPlaying) {
        actionsHtml = `
          <button class="btn btn-accent btn-sm" onclick="openPos('${escapeAttr(b.id)}')">
            <iconify-icon icon="lucide:shopping-cart" width="14"></iconify-icon> POS ${posCount > 0 ? `(${posCount})` : ''}
          </button>
          <button class="btn btn-primary btn-sm" onclick="openLedgerModal('booking', '${escapeAttr(b.id)}')">
            <iconify-icon icon="lucide:wallet" width="14"></iconify-icon> ${remaining > 0 ? 'Thu tiền' : 'Sổ cái'}
          </button>
          <button class="btn btn-outline btn-sm" onclick="finishSessionOnly('${escapeAttr(b.id)}')" title="Chỉ đóng ca chơi, KHÔNG tự ghi thu tiền khống">
            <iconify-icon icon="lucide:flag" width="13"></iconify-icon> Đóng ca
          </button>
        `;
      } else if (isCompleted) {
        actionsHtml = `
          <button class="btn ${remaining > 0 ? 'btn-primary' : 'btn-outline'} btn-sm" onclick="openLedgerModal('booking', '${escapeAttr(b.id)}')">
            <iconify-icon icon="lucide:wallet" width="14"></iconify-icon> ${remaining > 0 ? `Thu nợ (${formatMoney(remaining)})` : 'Sổ cái / Hoàn'}
          </button>
        `;
      } else {
        actionsHtml = `
          <span class="badge badge-danger">${escapeHtml(statusLabel(b.status, b))}</span>
          ${paidAmount > 0 ? `<button class="btn btn-outline btn-xs" onclick="openLedgerModal('booking', '${escapeAttr(b.id)}')">Hoàn tiền</button>` : ''}
        `;
      }

      return `
        <div class="item-row" ${isPlaying ? 'style="background:var(--color-primary-50)"' : ''}>
          <div class="item-avatar" style="background:${isPlaying ? 'var(--color-primary)' : 'var(--bg-surface-alt)'};color:${isPlaying ? '#FFFFFF' : 'var(--text-primary)'}">S${Number(court.id)}</div>
          <div class="item-info">
            <div class="item-name" style="flex-wrap:wrap;gap:0.35rem;">
              <span>${escapeHtml(b.name)}</span>
              <span class="badge ${statusBadgeClass(b.status)}">${escapeHtml(statusLabel(b.status, b))}</span>
              ${paymentBadgeHtml(paidAmount, amountDue, b.paymentStatus)}
              <span class="text-xs text-mono text-muted">${escapeHtml(b.id)}</span>
            </div>
            <div class="item-detail">
              <strong>${escapeHtml(court.name)}</strong> · <span class="text-mono font-semibold">${escapeHtml(b.time)} → ${escapeHtml(endTime)}</span> (${Number(b.duration)}p) · ${escapeHtml(formatDateVN(b.date))} · Phải thu: <strong class="text-mono" style="color:var(--color-indigo)">${formatMoney(amountDue)}</strong> · Đã thu: <strong class="text-mono" style="color:var(--color-success)">${formatMoney(paidAmount)}</strong>
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

  // 4. Render Court Lock Buttons & Active Locks List
  const lockBtnsEl = document.getElementById('lockCourtBtns');
  if (lockBtnsEl) {
    lockBtnsEl.innerHTML = COURTS.map(c => `
      <button type="button" class="btn ${Number(c.id) === Number(lockSelectedCourt) ? 'btn-danger' : 'btn-outline'} btn-sm" onclick="selectLockCourt(${Number(c.id)})">
        ${escapeHtml(c.name)}
      </button>
    `).join('');
  }

  const locksCountEl = document.getElementById('activeLocksCount');
  const locksListEl = document.getElementById('activeLocksList');
  if (locksCountEl) locksCountEl.textContent = COURT_LOCKS.length;
  if (locksListEl) {
    if (COURT_LOCKS.length === 0) {
      locksListEl.innerHTML = `<div class="empty-state" style="padding:1.25rem"><div class="text-xs text-muted">Tất cả ${COURTS.length} sân đang hoạt động bình thường.</div></div>`;
    } else {
      locksListEl.innerHTML = COURT_LOCKS.map(l => {
        const c = getCourtById(l.courtId);
        const end = getEndTime(l.startTime, l.duration);
        return `
          <div class="item-row" style="padding:0.65rem 1rem">
            <div class="item-info">
              <div class="item-name" style="font-size:0.825rem">${escapeHtml(c.name)} <span class="badge badge-danger">${escapeHtml(l.startTime)}→${escapeHtml(end)}</span></div>
              <div class="item-detail">${escapeHtml(formatDateVN(l.date))} · ${escapeHtml(l.reason)}</div>
            </div>
            <button class="btn btn-outline btn-sm" onclick="unlockCourt('${escapeAttr(l.id)}')">Mở khóa</button>
          </div>
        `;
      }).join('');
    }
  }
}

function renderStandaloneOrdersSection() {
  const countEl = document.getElementById('standaloneOrdersCount');
  const listEl = document.getElementById('standaloneOrdersList');
  if (!listEl) return;

  const orders = Array.isArray(STANDALONE_ORDERS) ? STANDALONE_ORDERS : [];
  if (countEl) countEl.textContent = orders.length;

  if (orders.length === 0) {
    listEl.innerHTML = `
      <div class="empty-state" style="padding:1.5rem 1rem">
        <div class="text-xs text-muted">Chưa có đơn hàng mua lẻ Pro Shop nào.</div>
      </div>`;
    return;
  }

  listEl.innerHTML = orders.slice(0, 25).map(o => {
    const total = Number(o.totalAmount) || 0;
    const paid = Number(o.paidAmount) || 0;
    const rem = Math.max(0, total - paid);
    const isCancelled = o.status === 'cancelled';

    return `
      <div class="item-row" ${isCancelled ? 'style="opacity:0.65"' : ''}>
        <div class="item-avatar" style="background:var(--color-indigo-50);color:var(--color-indigo);">PS</div>
        <div class="item-info">
          <div class="item-name" style="flex-wrap:wrap;gap:0.35rem;">
            <span class="text-mono font-bold" style="color:var(--color-indigo)">${escapeHtml(o.code)}</span>
            <span>${escapeHtml(o.customerName)}</span>
            ${isCancelled ? '<span class="badge badge-danger">Đã hủy · Đã hoàn kho</span>' : paymentBadgeHtml(paid, total, o.paymentStatus)}
          </div>
          <div class="item-detail">
            SĐT: <span class="text-mono">${escapeHtml(o.phone)}</span> · ${escapeHtml(o.address || 'Nhận tại quầy')} · Phải thu: <strong class="text-mono">${formatMoney(total)}</strong> · Đã thu: <strong class="text-mono" style="color:var(--color-success)">${formatMoney(paid)}</strong>
          </div>
        </div>
        <div class="item-actions" style="flex-wrap:wrap;">
          ${!isCancelled ? `
            <button class="btn ${rem > 0 ? 'btn-primary' : 'btn-outline'} btn-sm" onclick="openLedgerModal('order', '${escapeAttr(o.code)}')">
              <iconify-icon icon="lucide:wallet" width="14"></iconify-icon> ${rem > 0 ? 'Thu tiền' : 'Sổ cái / Hoàn'}
            </button>
            ${paid === 0 ? `<button class="btn btn-danger-outline btn-sm" onclick="cancelStandaloneOrder('${escapeAttr(o.code)}')">Hủy &amp; Hoàn kho</button>` : ''}
          ` : ''}
        </div>
      </div>
    `;
  }).join('');
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
    const court = getCourtById(b.courtId);
    const qrPlaceholder = document.querySelector('#modalQR .qr-placeholder');
    if (qrPlaceholder) {
      qrPlaceholder.innerHTML = `
        ${renderBookingQrHtml(b, 156)}
        <div class="text-xs font-bold text-mono mt-sm" id="qrRef">${escapeHtml(b.id)}</div>
      `;
    }
    document.getElementById('qrName').textContent = `${b.name} (${b.phone})`;
    document.getElementById('qrCourt').textContent = court.name;
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
  const pendings = BOOKINGS.filter(b => b.status === 'pending');
  let successCount = 0;
  for (const b of pendings) {
    const res = await apiFetch(`/api/admin/bookings/${encodeURIComponent(b.id)}/approve`, { method: 'POST' });
    if (res.ok) successCount++;
  }
  await syncDatabaseFromServer(true);
  showToast('Đã duyệt hàng loạt', `Đã duyệt thành công ${successCount}/${pendings.length} yêu cầu đặt sân.`, 'success');
  renderManagement();
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

async function openLedgerModal(kind, code) {
  activeLedgerContext = { kind, code };
  toggleTransferRefRequirement();

  let title = '';
  let subtitle = '';
  let due = 0;
  let paid = 0;
  let summaryHtml = '';
  let isPlayingBooking = false;

  if (kind === 'booking') {
    const b = BOOKINGS.find(x => x.id === code);
    if (!b) return;
    isPlayingBooking = b.status === 'playing';
    const court = getCourtById(b.courtId);
    const posExtra = Number(b.posTotal) || (b.posItems || []).reduce((s, i) => s + Number(i.price || 0) * Number(i.qty || 0), 0);
    const courtFee = Number(b.courtFee) || 0;
    const disc = Number(b.discountAmount) || 0;
    due = Number(b.amountDue) || Math.max(0, courtFee - disc + posExtra);
    paid = Number(b.paidAmount) || 0;

    title = `Sổ Cái Ca Sân — ${b.id}`;
    subtitle = `${b.name} (${b.phone}) · ${court.name} · ${b.time}`;
    summaryHtml = `
      <div class="payment-line"><span class="text-muted">Tiền thuê sân (${Number(b.duration)}p)</span><span class="font-semibold text-mono">${formatMoney(courtFee)}</span></div>
      ${disc > 0 ? `<div class="payment-line"><span class="text-muted">Chiết khấu hội viên</span><span class="font-semibold text-mono" style="color:var(--color-success)">-${formatMoney(disc)}</span></div>` : ''}
      <div class="payment-line"><span class="text-muted">Dịch vụ POS / Pro Shop gộp sân</span><span class="font-semibold text-mono">${formatMoney(posExtra)}</span></div>
      <div class="payment-line"><span class="text-muted">Tổng phải thu (AmountDue)</span><span class="font-bold text-mono">${formatMoney(due)}</span></div>
      <div class="payment-line"><span class="text-muted">Đã thu ròng trên Sổ cái</span><span class="font-bold text-mono" style="color:var(--color-success)">${formatMoney(paid)}</span></div>
      <div class="payment-line total"><span>Còn phải thu</span><span class="text-mono" style="color:${due - paid > 0 ? 'var(--color-primary)' : 'var(--color-success)'}">${formatMoney(Math.max(0, due - paid))}</span></div>
    `;
  } else {
    const o = STANDALONE_ORDERS.find(x => x.code === code);
    if (!o) return;
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
  document.getElementById('ledgerPayRef').value = '';
  document.getElementById('ledgerPayNote').value = '';

  const finishWrap = document.getElementById('ledgerAlsoFinishWrap');
  const finishChk = document.getElementById('ledgerAlsoFinishSession');
  if (finishWrap && finishChk) {
    finishWrap.style.display = kind === 'booking' && isPlayingBooking ? 'flex' : 'none';
    finishChk.checked = kind === 'booking' && isPlayingBooking;
  }

  const refundDetails = document.getElementById('ledgerRefundDetails');
  if (refundDetails) {
    refundDetails.style.display = paid > 0 ? 'block' : 'none';
    refundDetails.open = false;
    const refAmt = document.getElementById('ledgerRefundAmount');
    if (refAmt) refAmt.value = paid;
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
        <div style="display:flex;justify-content:space-between;align-items:center;padding:0.45rem 0.65rem;border-bottom:1px solid #F1F5F9;font-size:0.76rem;">
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
  if (!activeLedgerContext) return;
  const amount = parseInt(document.getElementById('ledgerPayAmount')?.value || '0');
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
      await apiFetch(`/api/admin/bookings/${encodeURIComponent(activeLedgerContext.code)}/finish`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ note: 'Đóng ca sau khi thu ngân xác nhận thanh toán' })
      });
    }

    await syncDatabaseFromServer(true);
    renderManagement();
    closeModal('modalLedgerSettlement');
    showToast('Đã ghi sổ thu tiền!', `Đã ghi nhận thực thu ${formatMoney(amount)} (${paymentMethod === 'transfer' ? 'Chuyển khoản' : 'Tiền mặt'}).`, 'success');
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể lưu bút toán thu tiền.', 'error');
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

    await syncDatabaseFromServer(true);
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
    await syncDatabaseFromServer(true);
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
  const court = getCourtById(lockSelectedCourt);
  const date = document.getElementById('lockDate')?.value || getTodayStr();
  const startTime = document.getElementById('lockStart')?.value || '12:00';
  const duration = Number(document.getElementById('lockDuration')?.value) || 60;
  const reason = (document.getElementById('lockReason')?.value || 'Bảo dưỡng định kỳ').trim();

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
    showToast('Đã khóa sân', `${court.name} đã tạm khóa (${startTime}, ${duration}p) để: ${reason}`, 'warning');
    renderManagement();
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể áp dụng lệnh khóa sân.', 'error');
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

  const court = getCourtById(posBooking.courtId);
  document.getElementById('posCustomer').textContent = `${posBooking.name} (${posBooking.phone})`;
  document.getElementById('posCourtInfo').textContent = `${court.name} · Bắt đầu: ${posBooking.time} · Mã: ${posBooking.id}`;

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
    const court = getCourtById(posBooking.courtId);
    document.getElementById('posCourtInfo').textContent = `${court.name} · Bắt đầu: ${posBooking.time} · Mã: ${posBooking.id}`;
    updatePosCourtFee();
    updatePosTotal();
    renderManagement();
    showToast('Đã chuyển sân!', `Ca ${posBooking.id} đã chuyển sang ${court.name}.`, 'success');
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
  const menuData = Array.isArray(MENU) && MENU.length > 0 ? MENU : MENU_ITEMS;
  grid.innerHTML = menuData.map(item => {
    const stock = typeof item.stock === 'number' ? item.stock : 0;
    const outOfStock = stock <= 0;
    return `
      <div class="pos-menu-item" ${outOfStock ? 'style="opacity:0.5;pointer-events:none"' : ''} onclick="addToCart(${Number(item.id)})">
        <div class="pos-menu-item-icon">${sanitizeIconHtml(item.icon)}</div>
        <div class="pos-menu-item-name">${escapeHtml(item.name)}</div>
        <div class="pos-menu-item-price text-mono">${formatMoney(item.price)} <span style="font-size:0.65rem;color:var(--text-muted)">(${outOfStock ? 'Hết' : `Kho: ${stock}`})</span></div>
      </div>
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
});
