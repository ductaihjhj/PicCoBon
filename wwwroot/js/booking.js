/* ==========================================================================
   PICKLEBALL PRO ARENA — INTERACTIVE BOOKING ENGINE
   Date-Aware Slot Grid, Real-Time Conflict & Lock Detection, Split-Bill Calculator
   ========================================================================== */

let selectedDateISO = getTodayISO();
let selectedCourt = 1;
let bookingDuration = 60;
let selectedStartTime = '17:00';

const DAY_NAMES_VN = ['CN', 'Thứ 2', 'Thứ 3', 'Thứ 4', 'Thứ 5', 'Thứ 6', 'Thứ 7'];

function autoFillBookingUser() {
  if (CURRENT_USER) {
    const nameInput = document.getElementById('bookingName');
    const phoneInput = document.getElementById('bookingPhone');
    const skillSelect = document.getElementById('bookingSkill');
    if (nameInput && !nameInput.value) nameInput.value = CURRENT_USER.name || '';
    if (phoneInput && !phoneInput.value) phoneInput.value = CURRENT_USER.phone || '';
    if (skillSelect && CURRENT_USER.skillLevel) {
      const hasOption = Array.from(skillSelect.options).some(o => o.value === CURRENT_USER.skillLevel);
      if (hasOption) skillSelect.value = CURRENT_USER.skillLevel;
    }
  }
}

function initBooking() {
  // Restore draft if user just logged in and returned to /Booking
  try {
    const rawDraft = sessionStorage.getItem('pb_pending_booking_draft');
    if (rawDraft) {
      const draft = JSON.parse(rawDraft);
      if (draft.date) selectedDateISO = draft.date;
      if (draft.court) selectedCourt = Number(draft.court);
      if (draft.duration) bookingDuration = Number(draft.duration);
      if (draft.time) selectedStartTime = draft.time;
      if (draft.players && document.getElementById('bookingPlayers')) {
        document.getElementById('bookingPlayers').value = draft.players;
      }
      if (draft.skillLevel && document.getElementById('bookingSkill')) {
        document.getElementById('bookingSkill').value = draft.skillLevel;
      }
      if (draft.extraPaddles && document.getElementById('extraPaddles')) {
        document.getElementById('extraPaddles').checked = true;
      }
      if (draft.extraBalls && document.getElementById('extraBalls')) {
        document.getElementById('extraBalls').checked = true;
      }
      if (draft.name && document.getElementById('bookingName')) {
        document.getElementById('bookingName').value = draft.name;
      }
      if (draft.phone && document.getElementById('bookingPhone')) {
        document.getElementById('bookingPhone').value = draft.phone;
      }
      sessionStorage.removeItem('pb_pending_booking_draft');
    }
  } catch (_) {}

  // Read URL params if navigated from Dashboard Quick Matrix or Court Card
  const params = new URLSearchParams(window.location.search);
  if (params.get('court')) {
    const cId = Number(params.get('court'));
    if (cId > 0) selectedCourt = cId;
  }
  if (params.get('time')) {
    selectedStartTime = params.get('time');
  }
  if (params.get('date')) {
    selectedDateISO = params.get('date');
  }

  // Pre-fill user info if logged in
  autoFillBookingUser();

  renderQuickDateStrip();
  renderCourtOptions();
  populateStartSelect();
  setDuration(bookingDuration);
}

function renderQuickDateStrip() {
  const stripEl = document.getElementById('quickDateStrip');
  if (!stripEl) return;

  let html = '';
  for (let i = 0; i < 7; i++) {
    const dt = new Date();
    dt.setDate(dt.getDate() + i);
    const iso = getOffsetDateISO(i);
    const dayName = i === 0 ? 'Hôm nay' : i === 1 ? 'Ngày mai' : DAY_NAMES_VN[dt.getDay()];
    const dayNum = String(dt.getDate()).padStart(2, '0');
    const monthNum = `Tháng ${dt.getMonth() + 1}`;
    const isActive = iso === selectedDateISO;

    html += `
      <div class="date-pill ${isActive ? 'active' : ''}" onclick="selectDateISO('${iso}')">
        <span class="date-pill-day">${dayName}</span>
        <span class="date-pill-num">${dayNum}</span>
        <span class="date-pill-month">${monthNum}</span>
      </div>
    `;
  }
  stripEl.innerHTML = html;

  const labelEl = document.getElementById('selectedDateLabel');
  if (labelEl) labelEl.textContent = formatDateVN(selectedDateISO);
}

function selectDateISO(iso) {
  selectedDateISO = iso;
  document.getElementById('conflictBanner')?.classList.remove('show');
  renderQuickDateStrip();
  renderTimelineAndSlots();
  updatePriceBreakdown();
}

function renderCourtOptions() {
  const container = document.getElementById('courtOptions');
  if (!container) return;

  container.innerHTML = COURTS.map(c => {
    const isSelected = Number(c.id) === Number(selectedCourt);
    const baseRate = getCourtHourlyRate(c.id, '08:00');
    return `
      <div class="court-option ${isSelected ? 'selected' : ''}" onclick="selectCourt(${c.id})">
        <div>
          <div class="court-option-name">${c.name}</div>
          <div class="court-option-type">${c.typeLabel}</div>
          <div class="text-xs font-bold text-mono" style="color:var(--color-primary);margin-top:4px">Từ ${formatMoney(baseRate)}/h</div>
        </div>
        <div class="court-option-dot"></div>
      </div>
    `;
  }).join('');
}

function selectCourt(id) {
  selectedCourt = Number(id);
  document.getElementById('conflictBanner')?.classList.remove('show');
  renderCourtOptions();
  renderTimelineAndSlots();
  updatePriceBreakdown();
}

function setDuration(mins) {
  bookingDuration = Number(mins);
  document.querySelectorAll('#durationPills .duration-pill').forEach(btn => {
    btn.classList.toggle('active', Number(btn.getAttribute('data-dur')) === bookingDuration);
  });
  document.getElementById('conflictBanner')?.classList.remove('show');
  renderTimelineAndSlots();
  updatePriceBreakdown();
}

function populateStartSelect() {
  const sel = document.getElementById('bookingStart');
  if (!sel) return;
  sel.innerHTML = '';
  for (let h = 6; h <= 21; h++) {
    const hh = String(h).padStart(2, '0');
    for (const m of ['00', '30']) {
      if (h === 21 && m === '30') continue;
      const val = `${hh}:${m}`;
      sel.innerHTML += `<option value="${val}">${val}</option>`;
    }
  }
  sel.value = selectedStartTime;
}

function selectStartTime(timeStr) {
  selectedStartTime = timeStr;
  const sel = document.getElementById('bookingStart');
  if (sel && sel.value !== timeStr) sel.value = timeStr;
  document.getElementById('conflictBanner')?.classList.remove('show');
  renderTimelineAndSlots();
  updatePriceBreakdown();
}

function renderTimelineAndSlots() {
  const bar = document.getElementById('bookingTimeline');
  const ticks = document.getElementById('bookingTicks');
  const slotGrid = document.getElementById('timeSlotGrid');
  if (!bar || !slotGrid) return;

  // 1. Render visual timeline bar (06:00 to 22:00 = 960 mins)
  bar.innerHTML = '';
  const totalMins = 16 * 60;

  const courtBookings = BOOKINGS.filter(b =>
    Number(b.courtId) === Number(selectedCourt) &&
    (b.date || getTodayISO()) === selectedDateISO &&
    ['playing', 'pending', 'approved', 'completed'].includes(b.status)
  );

  courtBookings.forEach(b => {
    const startOffset = timeToMinutes(b.time) - 360;
    const left = Math.max(0, (startOffset / totalMins) * 100);
    const width = Math.min(100 - left, (Number(b.duration) / totalMins) * 100);
    const cls = b.status === 'pending' ? 'pending' : 'booked';
    bar.innerHTML += `<div class="timeline-block ${cls}" style="left:${left}%;width:${width}%" title="${b.name}: ${b.time} (${b.duration}p)"></div>`;
  });

  const courtLocks = COURT_LOCKS.filter(l =>
    Number(l.courtId) === Number(selectedCourt) &&
    l.date === selectedDateISO
  );

  courtLocks.forEach(l => {
    const startOffset = timeToMinutes(l.startTime) - 360;
    const left = Math.max(0, (startOffset / totalMins) * 100);
    const width = Math.min(100 - left, (Number(l.duration) / totalMins) * 100);
    bar.innerHTML += `<div class="timeline-block locked" style="left:${left}%;width:${width}%" title="Bảo trì: ${l.reason}"></div>`;
  });

  if (ticks) {
    ticks.innerHTML = '';
    for (let h = 6; h <= 22; h += 2) {
      ticks.innerHTML += `<span class="timeline-tick">${String(h).padStart(2, '0')}:00</span>`;
    }
  }

  // 2. Render Interactive Time Slot Buttons (30-min intervals from 06:00 to 21:00)
  const slots = [];
  for (let h = 6; h <= 21; h++) {
    const hh = String(h).padStart(2, '0');
    slots.push(`${hh}:00`);
    if (h < 21) slots.push(`${hh}:30`);
  }

  slotGrid.innerHTML = slots.map(t => {
    const startMins = timeToMinutes(t);
    const endMins = startMins + bookingDuration;
    const conflict = checkSlotConflict(selectedCourt, selectedDateISO, startMins, endMins);
    const exceedsClosing = endMins > 22 * 60 + 30; // Allow finishing up to 22:30
    const isDisabled = conflict.hasConflict || exceedsClosing;
    const isSelected = t === selectedStartTime && !isDisabled;
    const peak = isPeakHour(t);
    const hourly = getCourtHourlyRate(selectedCourt, t);

    let stateClass = '';
    let subLabel = `${Math.round(hourly / 1000)}k/h`;
    if (conflict.hasConflict) {
      if (conflict.type === 'lock') {
        stateClass = 'locked-slot';
        subLabel = 'Bảo trì';
      } else {
        stateClass = 'booked';
        subLabel = conflict.record.status === 'pending' ? 'Chờ duyệt' : 'Đã kín';
      }
    } else if (exceedsClosing) {
      stateClass = 'booked';
      subLabel = 'Quá giờ đóng';
    } else if (isSelected) {
      stateClass = 'selected';
    } else if (peak) {
      stateClass = 'peak';
    }

    return `
      <button type="button" class="slot-btn ${stateClass}" ${isDisabled ? 'disabled' : ''} onclick="selectStartTime('${t}')">
        ${peak && !isDisabled ? '<span class="slot-peak-tag">HOT</span>' : ''}
        <span class="slot-time">${t}</span>
        <span class="slot-price">${subLabel}</span>
      </button>
    `;
  }).join('');
}

function getSelectedExtrasTotal() {
  let extras = 0;
  const paddles = document.getElementById('extraPaddles');
  const balls = document.getElementById('extraBalls');
  if (paddles && paddles.checked) extras += 80000;
  if (balls && balls.checked) extras += 50000;
  return extras;
}

function updatePriceBreakdown() {
  const court = getCourtById(selectedCourt);
  const extrasTotal = getSelectedExtrasTotal();
  const calc = calculateBookingTotal(selectedCourt, selectedStartTime, bookingDuration, extrasTotal);
  const endTime = minutesToTime(timeToMinutes(selectedStartTime) + bookingDuration);
  const players = Number(document.getElementById('bookingPlayers')?.value) || 4;
  const splitAmount = Math.round(calc.grandTotal / players);

  const formTitle = document.getElementById('bookingFormTitle');
  if (formTitle) formTitle.textContent = `Phiếu đặt: ${court.name}`;

  const badge = document.getElementById('bookingCourtPriceBadge');
  if (badge) {
    badge.textContent = `${formatMoney(calc.hourlyRate)} / giờ ${isPeakHour(selectedStartTime) ? '(Giờ vàng)' : ''}`;
  }

  const dtEl = document.getElementById('summaryDateTime');
  if (dtEl) dtEl.textContent = `${selectedStartTime}→${endTime} · ${formatDateVN(selectedDateISO)}`;

  const rateEl = document.getElementById('summaryHourlyRate');
  if (rateEl) rateEl.textContent = `${formatMoney(calc.hourlyRate)}/h × ${bookingDuration}p`;

  const courtFeeEl = document.getElementById('summaryCourtFee');
  if (courtFeeEl) courtFeeEl.textContent = formatMoney(calc.courtFee);

  const extrasRow = document.getElementById('summaryExtrasRow');
  const extrasFeeEl = document.getElementById('summaryExtrasFee');
  if (extrasRow && extrasFeeEl) {
    extrasRow.style.display = extrasTotal > 0 ? 'flex' : 'none';
    extrasFeeEl.textContent = formatMoney(extrasTotal);
  }

  const totalEl = document.getElementById('summaryTotal');
  if (totalEl) totalEl.textContent = formatMoney(calc.grandTotal);

  const splitEl = document.getElementById('summarySplitPerPerson');
  if (splitEl) splitEl.textContent = `${formatMoney(splitAmount)} / người (${players} người)`;
}

function submitBooking() {
  const name = document.getElementById('bookingName')?.value.trim() || '';
  const phone = document.getElementById('bookingPhone')?.value.trim() || '';
  const players = Number(document.getElementById('bookingPlayers')?.value) || 4;
  const skillLevel = document.getElementById('bookingSkill')?.value || 'Giao lưu';

  if (!CURRENT_USER) {
    sessionStorage.setItem('pb_pending_booking_draft', JSON.stringify({
      date: selectedDateISO,
      court: selectedCourt,
      duration: bookingDuration,
      time: selectedStartTime,
      players,
      skillLevel,
      extraPaddles: !!document.getElementById('extraPaddles')?.checked,
      extraBalls: !!document.getElementById('extraBalls')?.checked,
      name,
      phone
    }));
    const returnUrl = `/Booking?date=${encodeURIComponent(selectedDateISO)}&court=${encodeURIComponent(selectedCourt)}&time=${encodeURIComponent(selectedStartTime)}`;
    requireAuth(returnUrl);
    return;
  }

  const banner = document.getElementById('conflictBanner');
  const bannerText = document.getElementById('conflictBannerText');

  if (!name || !phone) {
    showToast('Thiếu thông tin', 'Vui lòng nhập đầy đủ họ tên và số điện thoại liên hệ.', 'warning');
    return;
  }

  const phoneClean = phone.replace(/\s+/g, '');
  if (!/^0\d{8,10}$/.test(phoneClean)) {
    showToast('Số điện thoại chưa hợp lệ', 'Vui lòng nhập số điện thoại Việt Nam hợp lệ (bắt đầu bằng số 0, từ 9-11 số).', 'warning');
    return;
  }

  const startMins = timeToMinutes(selectedStartTime);
  const endMins = startMins + bookingDuration;
  const conflict = checkSlotConflict(selectedCourt, selectedDateISO, startMins, endMins);

  if (conflict.hasConflict) {
    if (banner && bannerText) {
      bannerText.textContent = conflict.type === 'lock'
        ? `Sân đang tạm khóa bảo trì (${conflict.record.reason}). Vui lòng chọn giờ hoặc sân khác!`
        : `Khung giờ ${selectedStartTime} → ${minutesToTime(endMins)} đã có người đặt trên sân này. Vui lòng chọn khung giờ trống!`;
      banner.classList.add('show');
    }
    showToast('Trùng khung giờ', 'Khung giờ bạn chọn đã kín hoặc đang bảo trì.', 'error');
    return;
  }

  if (banner) banner.classList.remove('show');

  const extrasTotal = getSelectedExtrasTotal();
  const calc = calculateBookingTotal(selectedCourt, selectedStartTime, bookingDuration, extrasTotal);
  const posItems = [];
  if (document.getElementById('extraPaddles')?.checked) {
    posItems.push({ id: 9, name: 'Thuê Vợt Carbon Pro', price: 40000, qty: 2 });
  }
  if (document.getElementById('extraBalls')?.checked) {
    posItems.push({ id: 10, name: 'Bóng Thi Đấu Franklin X-40', price: 50000, qty: 1 });
  }

  const ref = generateRef();
  const court = getCourtById(selectedCourt);
  const newBooking = {
    id: ref,
    courtId: court.id,
    userId: CURRENT_USER ? CURRENT_USER.id : null,
    date: selectedDateISO,
    name,
    phone: phoneClean,
    time: selectedStartTime,
    duration: bookingDuration,
    status: 'pending',
    players,
    skillLevel,
    courtFee: calc.courtFee,
    posItems,
    totalPaid: calc.grandTotal
  };

  BOOKINGS.unshift(newBooking);
  saveData();

  // Populate QR Modal
  document.getElementById('qrCodeBadge').textContent = ref;
  document.getElementById('qrRef').textContent = ref;
  document.getElementById('qrName').textContent = `${name} (${phoneClean})`;
  document.getElementById('qrDate').textContent = formatDateVN(selectedDateISO);
  document.getElementById('qrCourt').textContent = `${court.name} (${court.type === 'Indoor' ? 'Trong nhà' : 'Ngoài trời'})`;
  document.getElementById('qrTime').textContent = `${selectedStartTime} → ${minutesToTime(endMins)} (${bookingDuration} phút)`;
  document.getElementById('qrTotal').textContent = formatMoney(calc.grandTotal);

  openModal('modalQR');
  showToast('Đặt sân thành công!', `Mã vé: ${ref} đã được ghi nhận.`, 'success');

  // Reset optional extras and re-render grid
  if (document.getElementById('extraPaddles')) document.getElementById('extraPaddles').checked = false;
  if (document.getElementById('extraBalls')) document.getElementById('extraBalls').checked = false;
  renderTimelineAndSlots();
  updatePriceBreakdown();
}

document.addEventListener('DOMContentLoaded', initBooking);
window.addEventListener('pickleball:db-synced', () => {
  autoFillBookingUser();
  renderCourtOptions();
  renderTimelineAndSlots();
  updatePriceBreakdown();
});
