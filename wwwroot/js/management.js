/* ==========================================================================
   PICKLEBALL PRO ARENA — MANAGEMENT, DISPATCH & POS CONTROLLER
   Pending Approval, Live Court Activation, Persistent POS Cart & Court Locks
   ========================================================================== */

let lockSelectedCourt = 1;
let mgmtFilter = 'active';
let posBooking = null;
let posCart = [];
let posCourtDuration = 60;

function initManagement() {
  if (!requireRole('admin')) return;

  const lockDateEl = document.getElementById('lockDate');
  if (lockDateEl && !lockDateEl.value) lockDateEl.value = getTodayISO();

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
  document.getElementById('pendingCount').textContent = pendings.length;
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
      const endTime = minutesToTime(timeToMinutes(b.time) + Number(b.duration));
      const calc = calculateBookingTotal(b.courtId, b.time, b.duration);
      return `
        <div class="item-row">
          <div class="item-avatar" style="background:var(--color-warning-light);color:#B45309">S${court.id}</div>
          <div class="item-info">
            <div class="item-name">
              <span>${b.name}</span>
              <span class="badge badge-neutral text-mono">${b.id}</span>
              <span class="badge badge-warning">${formatDateVN(b.date)}</span>
            </div>
            <div class="item-detail">
              <strong>${court.name}</strong> · <span class="text-mono font-bold">${b.time} → ${endTime}</span> (${b.duration}p) · SĐT: <span class="text-mono">${b.phone}</span> · Dự kiến: <strong class="text-mono" style="color:var(--color-primary)">${formatMoney(b.totalPaid || calc.grandTotal)}</strong>
            </div>
          </div>
          <div class="item-actions">
            <button class="btn btn-success btn-sm" onclick="approveBooking('${b.id}')">
              <iconify-icon icon="lucide:check" width="14"></iconify-icon> Duyệt vé
            </button>
            <button class="btn btn-danger-outline btn-sm" onclick="rejectBooking('${b.id}')">
              <iconify-icon icon="lucide:x" width="14"></iconify-icon> Từ chối
            </button>
          </div>
        </div>
      `;
    }).join('');
  }

  // 2. Render Active / Filtered Schedule
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
      const endTime = minutesToTime(timeToMinutes(b.time) + Number(b.duration));
      const timing = getBookingTimingState(b);
      const isPlaying = b.status === 'playing' && timing.phase === 'live';
      const isApproved = b.status === 'approved' || (b.status === 'playing' && timing.phase !== 'live');
      const posCount = (b.posItems || []).reduce((s, i) => s + Number(i.qty), 0);
      const posExtra = (b.posItems || []).reduce((s, i) => s + Number(i.price) * Number(i.qty), 0);
      const calc = calculateBookingTotal(b.courtId, b.time, b.duration, posExtra);

      let actionsHtml = '';
      if (isApproved) {
        const checkinBtnHtml = timing.canCheckIn
          ? `<button class="btn btn-primary btn-sm" onclick="startMatchNow('${b.id}')">
               <iconify-icon icon="lucide:play" width="14"></iconify-icon> Nhận sân (Check-in)
             </button>`
          : `<button class="btn btn-outline btn-sm" onclick="startMatchNow('${b.id}')" title="Ca chơi bắt đầu lúc ${b.time} (${formatDateVN(b.date)})">
               <iconify-icon icon="lucide:clock" width="14"></iconify-icon> Chờ tới ${b.time}
             </button>`;
        actionsHtml = `
          ${checkinBtnHtml}
          <button class="btn btn-outline btn-sm" onclick="openPos('${b.id}')">
            <iconify-icon icon="lucide:coffee" width="14"></iconify-icon> POS (${posCount})
          </button>
        `;
      } else if (isPlaying) {
        actionsHtml = `
          <button class="btn btn-accent btn-sm" onclick="openPos('${b.id}')">
            <iconify-icon icon="lucide:shopping-cart" width="14"></iconify-icon> Gọi món & Thanh toán ${posCount > 0 ? `(${posCount})` : ''}
          </button>
          <button class="btn btn-outline btn-sm" onclick="finishMatchNow('${b.id}')" title="Dừng ca và tất toán ngay">
            <iconify-icon icon="lucide:square" width="13"></iconify-icon> Kết thúc ca
          </button>
        `;
      } else if (b.status === 'completed') {
        actionsHtml = `<span class="badge badge-neutral"><iconify-icon icon="lucide:check-circle-2" width="13"></iconify-icon> Đã thu: ${formatMoney(b.totalPaid || calc.grandTotal)}</span>`;
      } else {
        actionsHtml = `<span class="badge badge-danger">Đã hủy</span>`;
      }

      const displayStatus = isPlaying ? 'playing' : (isApproved ? 'approved' : b.status);

      return `
        <div class="item-row" ${isPlaying ? 'style="background:var(--color-primary-50)"' : ''}>
          <div class="item-avatar" style="background:${isPlaying ? 'var(--color-primary)' : 'var(--bg-surface-alt)'};color:${isPlaying ? '#FFFFFF' : 'var(--text-primary)'}">S${court.id}</div>
          <div class="item-info">
            <div class="item-name">
              <span>${b.name}</span>
              <span class="badge ${statusBadgeClass(displayStatus)}">${statusLabel(displayStatus, b)}</span>
              <span class="text-xs text-mono text-muted">${b.id}</span>
            </div>
            <div class="item-detail">
              <strong>${court.name}</strong> · <span class="text-mono font-semibold">${b.time} → ${endTime}</span> · ${formatDateVN(b.date)} · Tạm tính: <strong class="text-mono" style="color:var(--color-indigo)">${formatMoney(calc.grandTotal)}</strong>
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
      <button type="button" class="btn ${c.id === lockSelectedCourt ? 'btn-danger' : 'btn-outline'} btn-sm" onclick="selectLockCourt(${c.id})">
        ${c.name}
      </button>
    `).join('');
  }

  const locksCountEl = document.getElementById('activeLocksCount');
  const locksListEl = document.getElementById('activeLocksList');
  if (locksCountEl) locksCountEl.textContent = COURT_LOCKS.length;
  if (locksListEl) {
    if (COURT_LOCKS.length === 0) {
      locksListEl.innerHTML = `<div class="empty-state" style="padding:1.25rem"><div class="text-xs text-muted">Tất cả 4 sân đang hoạt động bình thường.</div></div>`;
    } else {
      locksListEl.innerHTML = COURT_LOCKS.map(l => {
        const c = getCourtById(l.courtId);
        const end = minutesToTime(timeToMinutes(l.startTime) + Number(l.duration));
        return `
          <div class="item-row" style="padding:0.65rem 1rem">
            <div class="item-info">
              <div class="item-name" style="font-size:0.825rem">${c.name} <span class="badge badge-danger">${l.startTime}→${end}</span></div>
              <div class="item-detail">${formatDateVN(l.date)} · ${l.reason}</div>
            </div>
            <button class="btn btn-outline btn-sm" onclick="unlockCourt('${l.id}')">Mở khóa</button>
          </div>
        `;
      }).join('');
    }
  }
}

function approveBooking(id) {
  const b = BOOKINGS.find(x => x.id === id);
  if (!b) return;
  b.status = 'approved';
  saveData();
  document.getElementById('qrRef').textContent = b.id;
  document.getElementById('qrName').textContent = `${b.name} (${b.phone})`;
  document.getElementById('qrCourt').textContent = getCourtById(b.courtId).name;
  document.getElementById('qrTime').textContent = `${b.time} (${b.duration} phút) · ${formatDateVN(b.date)}`;
  openModal('modalQR');
  showToast('Đã duyệt đơn', `Đơn ${b.id} của ${b.name} đã chuyển sang Đã duyệt.`, 'success');
  renderManagement();
}

function approveAllPending() {
  const pendings = BOOKINGS.filter(b => b.status === 'pending');
  pendings.forEach(b => { b.status = 'approved'; });
  saveData();
  showToast('Đã duyệt tất cả', `Đã duyệt nhanh ${pendings.length} yêu cầu đặt sân.`, 'success');
  renderManagement();
}

function rejectBooking(id) {
  const b = BOOKINGS.find(x => x.id === id);
  if (!b) return;
  b.status = 'rejected';
  saveData();
  showToast('Đã từ chối đơn', `Đơn ${b.id} đã được hủy và giải phóng khung giờ sân.`, 'info');
  renderManagement();
}

function startMatchNow(id) {
  const b = BOOKINGS.find(x => x.id === id);
  if (!b) return;

  const timing = getBookingTimingState(b);
  if (!timing.canCheckIn) {
    if (timing.phase === 'future_day') {
      showToast('Chưa tới ngày thi đấu', `Vé ${b.id} được đặt cho ${formatDateVN(b.date)} lúc ${b.time}. Hệ thống sẽ tự động kích hoạt khi tới giờ.`, 'warning');
      return;
    }
    if (timing.phase === 'upcoming_today') {
      showToast('Chưa tới giờ nhận sân', `Ca chơi của ${b.name} bắt đầu lúc ${b.time} (chỉ mở check-in trước 15 phút). Hệ thống sẽ tự động chuyển sang Đang thi đấu đúng ${b.time}.`, 'warning');
      return;
    }
    if (timing.phase === 'ended') {
      showToast('Ca chơi đã qua giờ', `Khung giờ ${b.time} của vé ${b.id} đã kết thúc.`, 'warning');
      return;
    }
  }

  b.status = 'playing';
  saveData();
  showToast('Đã kích hoạt sân!', `Khách ${b.name} đã nhận ${getCourtById(b.courtId).name}. Hệ thống sẽ tự động dừng khi hết giờ.`, 'success');
  renderManagement();
}

function finishMatchNow(id) {
  const b = BOOKINGS.find(x => x.id === id);
  if (!b) return;
  const posExtra = (b.posItems || []).reduce((s, i) => s + Number(i.price) * Number(i.qty), 0);
  const calc = calculateBookingTotal(b.courtId, b.time, b.duration, posExtra);
  b.courtFee = calc.courtFee;
  b.totalPaid = calc.grandTotal;
  b.status = 'completed';
  saveData();
  showToast('Đã kết thúc ca chơi', `Ca sân của ${b.name} (${b.id}) đã hoàn tất. Tổng thu: ${formatMoney(calc.grandTotal)}.`, 'success');
  renderManagement();
}

function selectLockCourt(id) {
  lockSelectedCourt = Number(id);
  renderManagement();
}

function lockCourt() {
  const court = getCourtById(lockSelectedCourt);
  const date = document.getElementById('lockDate')?.value || getTodayISO();
  const startTime = document.getElementById('lockStart')?.value || '12:00';
  const duration = Number(document.getElementById('lockDuration')?.value) || 60;
  const reason = (document.getElementById('lockReason')?.value || 'Bảo dưỡng định kỳ').trim();

  const newLock = {
    id: 'LK-' + Date.now(),
    courtId: court.id,
    date,
    startTime,
    duration,
    reason
  };

  COURT_LOCKS.push(newLock);
  saveData();
  showToast('Đã khóa sân', `${court.name} đã tạm khóa (${startTime}, ${duration}p) để: ${reason}`, 'warning');
  renderManagement();
}

function unlockCourt(lockId) {
  COURT_LOCKS = COURT_LOCKS.filter(l => l.id !== lockId);
  saveData();
  showToast('Đã mở khóa sân', 'Sân đã sẵn sàng nhận khách đặt lịch trở lại.', 'success');
  renderManagement();
}

/* ---------- POS SYSTEM ---------- */
function openPos(bookingId) {
  posBooking = BOOKINGS.find(b => b.id === bookingId);
  if (!posBooking) return;
  // Clone existing posItems so edits can be saved or checked out
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
  const calc = calculateBookingTotal(posBooking.courtId, posBooking.time, posCourtDuration, 0);
  document.getElementById('posCourtDuration').textContent = `${posCourtDuration} phút × ${formatMoney(calc.hourlyRate)}/h`;
  document.getElementById('posCourtFee').textContent = formatMoney(calc.courtFee);
}

function adjustCourtTime(delta) {
  posCourtDuration = Math.max(30, Math.min(480, posCourtDuration + delta));
  updatePosCourtFee();
  updatePosTotal();
}

function renderPosMenu() {
  const grid = document.getElementById('posMenuGrid');
  if (!grid) return;
  grid.innerHTML = MENU.map(item => `
    <div class="pos-menu-item" onclick="addToCart(${item.id})">
      <div class="pos-menu-item-icon">${item.icon}</div>
      <div class="pos-menu-item-name">${item.name}</div>
      <div class="pos-menu-item-price text-mono">${formatMoney(item.price)}</div>
    </div>
  `).join('');
}

function addToCart(itemId) {
  const existing = posCart.find(c => Number(c.id) === Number(itemId));
  if (existing) {
    existing.qty += 1;
  } else {
    const item = MENU.find(m => Number(m.id) === Number(itemId));
    if (item) posCart.push({ id: item.id, name: item.name, price: item.price, qty: 1 });
  }
  renderPosCart();
}

function updateCartQty(itemId, delta) {
  const item = posCart.find(c => Number(c.id) === Number(itemId));
  if (item) {
    item.qty += delta;
    if (item.qty <= 0) posCart = posCart.filter(c => Number(c.id) !== Number(itemId));
  }
  renderPosCart();
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
            <div class="pos-cart-item-name">${item.name}</div>
            <div class="pos-cart-item-price text-mono">${formatMoney(item.price)}</div>
          </div>
          <div class="pos-qty">
            <button onclick="updateCartQty(${item.id}, -1)">−</button>
            <span class="text-mono font-bold" style="min-width:18px;text-align:center">${item.qty}</span>
            <button onclick="updateCartQty(${item.id}, 1)">+</button>
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
  const calc = calculateBookingTotal(posBooking.courtId, posBooking.time, posCourtDuration, drinkTotal);
  document.getElementById('posTotal').textContent = formatMoney(calc.grandTotal);
}

function savePosDraft() {
  if (!posBooking) return;
  const drinkTotal = posCart.reduce((sum, item) => sum + Number(item.price) * Number(item.qty), 0);
  const calc = calculateBookingTotal(posBooking.courtId, posBooking.time, posCourtDuration, drinkTotal);

  posBooking.duration = posCourtDuration;
  posBooking.posItems = JSON.parse(JSON.stringify(posCart));
  posBooking.courtFee = calc.courtFee;
  posBooking.totalPaid = calc.grandTotal;
  saveData();

  closeModal('modalPOS');
  showToast('Đã lưu giỏ hàng POS', `Cập nhật hóa đơn tạm tính cho ${posBooking.name}: ${formatMoney(calc.grandTotal)}`, 'info');
  renderManagement();
}

function checkoutPos() {
  if (!posBooking) return;
  const drinkTotal = posCart.reduce((sum, item) => sum + Number(item.price) * Number(item.qty), 0);
  const calc = calculateBookingTotal(posBooking.courtId, posBooking.time, posCourtDuration, drinkTotal);

  posBooking.duration = posCourtDuration;
  posBooking.posItems = JSON.parse(JSON.stringify(posCart));
  posBooking.courtFee = calc.courtFee;
  posBooking.totalPaid = calc.grandTotal;
  posBooking.status = 'completed';
  saveData();

  let lines = `
    <div class="payment-line"><span class="text-muted">Khách hàng</span><span class="font-bold">${posBooking.name}</span></div>
    <div class="payment-line"><span class="text-muted">Tiền sân (${posCourtDuration}p)</span><span class="font-bold text-mono">${formatMoney(calc.courtFee)}</span></div>
    <div class="payment-line"><span class="text-muted">Menu & Phụ phí</span><span class="font-bold text-mono">${formatMoney(drinkTotal)}</span></div>
  `;
  posCart.forEach(item => {
    lines += `<div class="payment-line" style="padding-left:0.85rem"><span class="text-xs text-muted">• ${item.name} × ${item.qty}</span><span class="text-xs text-mono">${formatMoney(item.price * item.qty)}</span></div>`;
  });
  lines += `<div class="payment-line total"><span>Tổng thu thực tế</span><span class="text-mono">${formatMoney(calc.grandTotal)}</span></div>`;

  document.getElementById('paymentSummary').innerHTML = lines;
  closeModal('modalPOS');
  openModal('modalPayment');
  showToast('Thanh toán thành công', `Đã thu ${formatMoney(calc.grandTotal)} và hoàn tất ca sân.`, 'success');
  renderManagement();
}

document.addEventListener('DOMContentLoaded', initManagement);
