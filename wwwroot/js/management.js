/* ==========================================================================
   SPIKE PICKLEBALL CLUB — MANAGEMENT, DISPATCH & POS CONTROLLER
   Server-Authoritative Approval, Check-In, Conflict-Checked Extension, Stock POS & Locks
   ========================================================================== */

let lockSelectedCourt = 1;
let mgmtFilter = 'active';
let posBooking = null;
let posCart = [];
let posCourtDuration = 60;

function initManagement() {
  if (!requireRole('admin')) return;

  const lockDateEl = document.getElementById('lockDate');
  if (lockDateEl && !lockDateEl.value) lockDateEl.value = getTodayStr();

  const lockSel = document.getElementById('lockStart');
  if (lockSel && lockSel.options.length === 0) {
    for (let h = 6; h <= 21; h++) {
      const hh = String(h).padStart(2, '0');
      lockSel.innerHTML += `<option value="${hh}:00">${hh}:00</option><option value="${hh}:30">${hh}:30</option>`;
    }
    lockSel.value = '12:00';
  }

  renderManagement();
}

function setMgmtFilter(filter) {
  mgmtFilter = filter;
  document.querySelectorAll('#mgmtFilterTabs .filter-tab').forEach(btn => {
    const isMatch = (filter === 'active' && btn.textContent.includes('Đang chơi')) ||
                    (filter === 'completed' && btn.textContent.includes('Đã thanh toán')) ||
                    (filter === 'all' && btn.textContent.includes('Tất cả'));
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
      <div class="empty-state" style="padding:1.75rem 1rem">
        <div class="empty-state-icon"><iconify-icon icon="lucide:check-circle-2" width="28" style="color:var(--color-success)"></iconify-icon></div>
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
              <strong>${escapeHtml(court.name)}</strong> · <span class="text-mono font-bold">${escapeHtml(b.time)} → ${escapeHtml(endTime)}</span> (${Number(b.duration)}p) · SĐT: <span class="text-mono">${escapeHtml(b.phone)}</span> · Tạm tính: <strong class="text-mono" style="color:var(--color-primary)">${formatMoney(expectedFee)}</strong>
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

  // 2. Render Active / Filtered Schedule (P1-07: Strict status, no wall-clock auto-playing)
  let listData = BOOKINGS.filter(b => b.status !== 'pending');
  if (mgmtFilter === 'active') {
    listData = BOOKINGS.filter(b => ['playing', 'approved'].includes(b.status));
  } else if (mgmtFilter === 'completed') {
    listData = BOOKINGS.filter(b => b.status === 'completed');
  }

  if (listData.length === 0) {
    todayList.innerHTML = `
      <div class="empty-state">
        <div class="empty-state-icon"><iconify-icon icon="lucide:calendar-x" width="28"></iconify-icon></div>
        <div class="empty-state-title">Chưa có ca thi đấu nào trong mục này</div>
      </div>`;
  } else {
    todayList.innerHTML = listData.map(b => {
      const court = getCourtById(b.courtId);
      const endTime = getEndTime(b.time, b.duration);
      const isPlaying = b.status === 'playing';
      const isApproved = b.status === 'approved';
      const posCount = (b.posItems || []).reduce((s, i) => s + Number(i.qty || 0), 0);
      const posExtra = (b.posItems || []).reduce((s, i) => s + Number(i.price || 0) * Number(i.qty || 0), 0);
      const amountDue = Number(b.amountDue) || (Number(b.courtFee || 0) + posExtra);
      const paidAmount = Number(b.paidAmount) || 0;

      let actionsHtml = '';
      if (isApproved) {
        actionsHtml = `
          <button class="btn btn-primary btn-sm" onclick="startMatchNow('${escapeAttr(b.id)}')">
            <iconify-icon icon="lucide:play" width="14"></iconify-icon> Nhận sân (Check-in)
          </button>
          <button class="btn btn-outline btn-sm" onclick="openPos('${escapeAttr(b.id)}')">
            <iconify-icon icon="lucide:coffee" width="14"></iconify-icon> POS (${posCount})
          </button>
        `;
      } else if (isPlaying) {
        actionsHtml = `
          <button class="btn btn-accent btn-sm" onclick="openPos('${escapeAttr(b.id)}')">
            <iconify-icon icon="lucide:shopping-cart" width="14"></iconify-icon> Gọi món &amp; Thu ngân ${posCount > 0 ? `(${posCount})` : ''}
          </button>
          <button class="btn btn-outline btn-sm" onclick="finishMatchNow('${escapeAttr(b.id)}')" title="Thu tiền & hoàn tất ca">
            <iconify-icon icon="lucide:check-square" width="13"></iconify-icon> Thu tiền &amp; Đóng ca
          </button>
        `;
      } else if (b.status === 'completed') {
        actionsHtml = `<span class="badge badge-success"><iconify-icon icon="lucide:check-circle-2" width="13"></iconify-icon> Đã thu: ${formatMoney(paidAmount || amountDue)}</span>`;
      } else {
        actionsHtml = `<span class="badge badge-danger">${escapeHtml(statusLabel(b.status, b))}</span>`;
      }

      const paymentTag = paidAmount >= amountDue && amountDue > 0
        ? `<span class="badge badge-success" style="font-size:0.68rem;">Đã thu đủ</span>`
        : `<span class="badge badge-warning" style="font-size:0.68rem;">Chưa thu (${formatMoney(amountDue - paidAmount)})</span>`;

      return `
        <div class="item-row" ${isPlaying ? 'style="background:var(--color-primary-50)"' : ''}>
          <div class="item-avatar" style="background:${isPlaying ? 'var(--color-primary)' : 'var(--bg-surface-alt)'};color:${isPlaying ? '#FFFFFF' : 'var(--text-primary)'}">S${Number(court.id)}</div>
          <div class="item-info">
            <div class="item-name">
              <span>${escapeHtml(b.name)}</span>
              <span class="badge ${statusBadgeClass(b.status)}">${escapeHtml(statusLabel(b.status, b))}</span>
              ${paymentTag}
              <span class="text-xs text-mono text-muted">${escapeHtml(b.id)}</span>
            </div>
            <div class="item-detail">
              <strong>${escapeHtml(court.name)}</strong> · <span class="text-mono font-semibold">${escapeHtml(b.time)} → ${escapeHtml(endTime)}</span> (${Number(b.duration)}p) · ${escapeHtml(formatDateVN(b.date))} · Tổng phải thu: <strong class="text-mono" style="color:var(--color-indigo)">${formatMoney(amountDue)}</strong>
            </div>
          </div>
          <div class="item-actions">
            ${actionsHtml}
          </div>
        </div>
      `;
    }).join('');
  }

  // 3. Render Court Lock Buttons & Active Locks List
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

async function approveBooking(id) {
  try {
    const res = await fetch(`/api/admin/bookings/${encodeURIComponent(id)}/approve`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' }
    });
    const data = await res.json();
    if (!res.ok || !data.ok) {
      showToast('Không thể duyệt vé', data.error || 'Khung giờ bị trùng.', 'error');
      return;
    }

    const b = data.booking;
    const court = getCourtById(b.courtId);
    const qrPlaceholder = document.querySelector('#modalQR .qr-placeholder');
    if (qrPlaceholder) {
      qrPlaceholder.innerHTML = `
        ${renderQrCodeSvg(b.checkInToken || b.id, 148)}
        <div class="text-xs font-bold text-mono mt-sm" id="qrRef">${escapeHtml(b.id)}</div>
      `;
    }
    document.getElementById('qrName').textContent = `${b.name} (${b.phone})`;
    document.getElementById('qrCourt').textContent = court.name;
    document.getElementById('qrTime').textContent = `${b.time} (${b.duration} phút) · ${formatDateVN(b.date)}`;
    openModal('modalQR');
    showToast('Đã duyệt đơn', `Đơn ${b.id} của ${b.name} đã chuyển sang Đã duyệt.`, 'success');
    await syncDatabaseFromServer();
    renderManagement();
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể duyệt đơn lúc này.', 'error');
  }
}

async function approveAllPending() {
  const pendings = BOOKINGS.filter(b => b.status === 'pending');
  let successCount = 0;
  for (const b of pendings) {
    const res = await fetch(`/api/admin/bookings/${encodeURIComponent(b.id)}/approve`, { method: 'POST' });
    if (res.ok) successCount++;
  }
  await syncDatabaseFromServer();
  showToast('Đã duyệt hàng loạt', `Đã duyệt thành công ${successCount}/${pendings.length} yêu cầu đặt sân.`, 'success');
  renderManagement();
}

async function rejectBooking(id) {
  if (!confirm(`Xác nhận từ chối và hủy yêu cầu đặt sân ${id}?`)) return;
  try {
    const res = await fetch(`/api/admin/bookings/${encodeURIComponent(id)}/reject`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ reason: 'REJECT' })
    });
    const data = await res.json();
    if (!res.ok || !data.ok) {
      showToast('Không thể từ chối', data.error || 'Đã xảy ra lỗi.', 'error');
      return;
    }
    await syncDatabaseFromServer();
    showToast('Đã từ chối đơn', `Đơn ${id} đã được hủy và giải phóng khung giờ sân.`, 'success');
    renderManagement();
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể hủy đơn lúc này.', 'error');
  }
}

async function startMatchNow(id) {
  try {
    const res = await fetch('/api/checkin/verify', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ tokenOrCode: id, requireSignedQr: false })
    });
    const data = await res.json();
    if (!res.ok || !data.ok) {
      showToast('Không thể Check-in nhận sân', data.error || 'Vui lòng kiểm tra lại ngày/giờ đặt sân.', 'warning');
      return;
    }
    await syncDatabaseFromServer();
    showToast('Đã kích hoạt sân!', `Khách ${data.booking.name} đã nhận sân và bắt đầu thi đấu.`, 'success');
    renderManagement();
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể kích hoạt ca sân.', 'error');
  }
}

async function finishMatchNow(id) {
  try {
    const res = await fetch(`/api/admin/bookings/${encodeURIComponent(id)}/checkout`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ paymentMethod: 'cash', markCompleted: true })
    });
    const data = await res.json();
    if (!res.ok || !data.ok) {
      showToast('Không thể tất toán', data.error || 'Đã xảy ra lỗi.', 'error');
      return;
    }
    await syncDatabaseFromServer();
    showToast('Đã thu tiền & kết thúc ca', `Ca sân ${id} đã hoàn tất. Đã thu: ${formatMoney(data.booking.paidAmount)}.`, 'success');
    renderManagement();
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể tất toán ca sân.', 'error');
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
    const res = await fetch('/api/admin/court-locks', {
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
    const data = await res.json();
    if (!res.ok || !data.ok) {
      showToast('Không thể khóa sân', data.error || 'Khung giờ đang có khách đặt.', 'error');
      return;
    }
    await syncDatabaseFromServer();
    showToast('Đã khóa sân', `${court.name} đã tạm khóa (${startTime}, ${duration}p) để: ${reason}`, 'warning');
    renderManagement();
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể áp dụng lệnh khóa sân.', 'error');
  }
}

async function unlockCourt(lockId) {
  try {
    const res = await fetch(`/api/admin/court-locks/${encodeURIComponent(lockId)}`, {
      method: 'DELETE'
    });
    if (!res.ok) {
      showToast('Không thể mở khóa', 'Vui lòng thử lại.', 'error');
      return;
    }
    await syncDatabaseFromServer();
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

  updatePosCourtFee();
  renderPosMenu();
  renderPosCart();
  openModal('modalPOS');
}

function updatePosCourtFee() {
  if (!posBooking) return;
  const seg = calculateSegmentedPricing(posBooking.courtId, posBooking.time, posCourtDuration);
  document.getElementById('posCourtDuration').textContent = `${posCourtDuration} phút (${posBooking.time} → ${getEndTime(posBooking.time, posCourtDuration)})`;
  document.getElementById('posCourtFee').textContent = formatMoney(seg.netCourtFee);
}

async function adjustCourtTime(delta) {
  if (!posBooking) return;
  const step = delta > 0 ? 30 : -30;
  if (step < 0) {
    showToast('Lưu ý gia hạn', 'Chỉ hỗ trợ gia hạn thêm thời lượng (+30 phút / lần) trực tiếp trên ca đang chạy.', 'warning');
    return;
  }

  try {
    const res = await fetch(`/api/admin/bookings/${encodeURIComponent(posBooking.id)}/extend`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ addMinutes: 30 })
    });
    const data = await res.json();
    if (!res.ok || !data.ok) {
      showToast('Không thể gia hạn sân!', data.error || 'Khung giờ kế tiếp đã có người đặt hoặc đang khóa bảo trì.', 'error');
      return;
    }

    posBooking = data.booking;
    posCourtDuration = Number(posBooking.duration);
    await syncDatabaseFromServer();
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
    const stock = typeof item.stock === 'number' ? item.stock : 100;
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
    const res = await fetch(`/api/admin/bookings/${encodeURIComponent(posBooking.id)}/pos-item`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ productId: Number(itemId), deltaQty: Number(delta) })
    });
    const data = await res.json();
    if (!res.ok || !data.ok) {
      showToast('Không thể cập nhật món', data.error || 'Vượt quá tồn kho.', 'warning');
      return;
    }

    posBooking = data.booking;
    posCart = JSON.parse(JSON.stringify(posBooking.posItems || []));
    await syncDatabaseFromServer();
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
            <button onclick="updateCartQty(${Number(item.id)}, -1)">−</button>
            <span class="text-mono font-bold" style="min-width:18px;text-align:center">${Number(item.qty)}</span>
            <button onclick="updateCartQty(${Number(item.id)}, 1)">+</button>
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
  const drinkTotal = posCart.reduce((sum, item) => sum + Number(item.price) * Number(item.qty), 0);
  const seg = calculateSegmentedPricing(posBooking.courtId, posBooking.time, posCourtDuration);
  const grandTotal = seg.netCourtFee + drinkTotal;
  document.getElementById('posTotal').textContent = formatMoney(grandTotal);
}

async function savePosDraft() {
  if (!posBooking) return;
  await syncDatabaseFromServer();
  closeModal('modalPOS');
  showToast('Đã lưu giỏ hàng POS', `Hóa đơn của ${posBooking.name} đã được đồng bộ lên máy chủ.`, 'success');
  renderManagement();
}

async function checkoutPos() {
  if (!posBooking) return;
  try {
    const res = await fetch(`/api/admin/bookings/${encodeURIComponent(posBooking.id)}/checkout`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        paymentMethod: 'cash',
        markCompleted: true,
        note: 'Thu ngân tất toán POS & trả sân'
      })
    });
    const data = await res.json();
    if (!res.ok || !data.ok) {
      showToast('Không thể thanh toán', data.error || 'Đã xảy ra lỗi.', 'error');
      return;
    }

    const completed = data.booking;
    const drinkTotal = (completed.posItems || []).reduce((sum, item) => sum + Number(item.price) * Number(item.qty), 0);

    let lines = `
      <div class="payment-line"><span class="text-muted">Khách hàng</span><span class="font-bold">${escapeHtml(completed.name)}</span></div>
      <div class="payment-line"><span class="text-muted">Tiền sân (${Number(completed.duration)}p)</span><span class="font-bold text-mono">${formatMoney(completed.courtFee)}</span></div>
      <div class="payment-line"><span class="text-muted">Menu &amp; Phụ phí POS</span><span class="font-bold text-mono">${formatMoney(drinkTotal)}</span></div>
    `;
    (completed.posItems || []).forEach(item => {
      lines += `<div class="payment-line" style="padding-left:0.85rem"><span class="text-xs text-muted">• ${escapeHtml(item.name)} × ${Number(item.qty)}</span><span class="text-xs text-mono">${formatMoney(item.price * item.qty)}</span></div>`;
    });
    lines += `<div class="payment-line total"><span>Tổng thực thu</span><span class="text-mono">${formatMoney(completed.paidAmount)}</span></div>`;

    document.getElementById('paymentSummary').innerHTML = lines;
    closeModal('modalPOS');
    openModal('modalPayment');
    await syncDatabaseFromServer();
    showToast('Thanh toán thành công', `Đã thu ${formatMoney(completed.paidAmount)} và hoàn tất ca sân.`, 'success');
    renderManagement();
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể thực hiện tất toán lúc này.', 'error');
  }
}

document.addEventListener('DOMContentLoaded', initManagement);
window.addEventListener('pickleball:db-synced', renderManagement);
