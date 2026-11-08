/* ================================================================
   SPIKE PICKLEBALL CLUB — BOOKING PAGE ENGINE (Server-Authoritative)
   Supports segmented normal/peak pricing, VIP discounts & signed QR
   ================================================================ */

let selectedCourtId = 1;
let selectedDate = getTodayStr();
let selectedDuration = 60;
let selectedStartTime = '17:00';
let isSubmittingBooking = false;

const BOOKING_TIME_SLOTS = [
  '06:00', '06:30', '07:00', '07:30', '08:00', '08:30', '09:00', '09:30',
  '10:00', '10:30', '11:00', '11:30', '14:00', '14:30', '15:00', '15:30',
  '16:00', '16:30', '17:00', '17:30', '18:00', '18:30', '19:00', '19:30',
  '20:00', '20:30', '21:00'
];

function initBookingPage() {
  const params = new URLSearchParams(window.location.search);
  const courtParam = parseInt(params.get('court'));
  const dateParam = params.get('date');
  const timeParam = params.get('time');

  if (courtParam && COURTS.some(c => Number(c.id) === courtParam)) {
    selectedCourtId = courtParam;
  } else if (COURTS.length > 0) {
    selectedCourtId = Number(COURTS[0].id);
  }
  if (dateParam) selectedDate = dateParam;
  if (timeParam && BOOKING_TIME_SLOTS.includes(timeParam)) selectedStartTime = timeParam;

  const user = getCurrentUser();
  if (user) {
    const nameEl = document.getElementById('bookingName');
    const phoneEl = document.getElementById('bookingPhone');
    if (nameEl && !nameEl.value && user.name) nameEl.value = user.name;
    if (phoneEl && !phoneEl.value && user.phone) phoneEl.value = user.phone;
  }

  renderDateStrip();
  renderCourtOptions();
  renderDurationPills();
  renderTimeline();
  renderTimeSlotGrid();
  updatePriceBreakdown();
}

function renderDateStrip() {
  const strip = document.getElementById('quickDateStrip');
  const label = document.getElementById('selectedDateLabel');
  if (!strip) return;

  const dayNames = ['Chủ nhật', 'Thứ 2', 'Thứ 3', 'Thứ 4', 'Thứ 5', 'Thứ 6', 'Thứ 7'];
  const days = [];

  for (let i = 0; i < 7; i++) {
    const dateStr = getOffsetDateStr(i);
    const d = new Date(dateStr + 'T00:00:00');
    const dow = i === 0 ? 'Hôm nay' : i === 1 ? 'Ngày mai' : dayNames[d.getDay()];
    const dayNum = `${String(d.getDate()).padStart(2, '0')}/${String(d.getMonth() + 1).padStart(2, '0')}`;
    const active = dateStr === selectedDate ? 'active' : '';

    days.push(`
      <button type="button" class="date-chip ${active}" onclick="selectBookingDate('${escapeAttr(dateStr)}')">
        <span class="date-chip-dow">${escapeHtml(dow)}</span>
        <span class="date-chip-day">${escapeHtml(dayNum)}</span>
      </button>
    `);
  }

  strip.innerHTML = days.join('');
  if (label) label.textContent = formatDateVN(selectedDate);
}

function selectBookingDate(dateStr) {
  selectedDate = dateStr;
  renderDateStrip();
  renderCourtOptions();
  renderTimeline();
  renderTimeSlotGrid();
  updatePriceBreakdown();
}

function renderCourtOptions() {
  const container = document.getElementById('courtOptions');
  if (!container) return;

  container.innerHTML = COURTS.map(court => {
    const active = Number(court.id) === Number(selectedCourtId) ? 'active' : '';
    const bookingsCount = BOOKINGS.filter(b =>
      Number(b.courtId) === Number(court.id) &&
      b.date === selectedDate &&
      b.status !== 'rejected' &&
      b.status !== 'cancelled' &&
      b.status !== 'expired'
    ).length;

    const stdRate = (Number(court.basePrice) || 120000) + (Number(court.surcharge) || 0);
    const peakRate = stdRate + (Number(CMS_CONFIG.peakSurcharge) || 30000);

    return `
      <div class="court-option ${active}" onclick="selectCourt(${Number(court.id)})">
        <div class="flex-between mb-sm">
          <span class="badge ${court.type === 'Indoor' ? 'badge-primary' : 'badge-neutral'}">${escapeHtml(court.type)}</span>
          <span class="text-xs text-muted">${bookingsCount} ca đã đặt</span>
        </div>
        <div class="court-option-name">${escapeHtml(court.name)}</div>
        <div class="court-option-meta">${escapeHtml(court.surface || 'Tiêu chuẩn USAPA')}</div>
        <div class="court-option-price">${formatMoney(stdRate)}<span style="font-size:0.72rem;font-weight:500;color:var(--text-secondary)">/h · Giờ vàng ${formatMoney(peakRate)}/h</span></div>
      </div>
    `;
  }).join('');
}

function selectCourt(courtId) {
  selectedCourtId = Number(courtId);
  renderCourtOptions();
  renderTimeline();
  renderTimeSlotGrid();
  updatePriceBreakdown();
}

function renderDurationPills() {
  document.querySelectorAll('#durationPills .duration-pill').forEach(btn => {
    const dur = parseInt(btn.getAttribute('data-dur'));
    btn.classList.toggle('active', dur === selectedDuration);
  });
}

function setDuration(dur) {
  selectedDuration = dur;
  renderDurationPills();
  renderTimeline();
  renderTimeSlotGrid();
  updatePriceBreakdown();
}

function renderTimeline() {
  const bar = document.getElementById('bookingTimeline');
  const ticks = document.getElementById('bookingTicks');
  if (!bar) return;

  const slots = [];
  for (let m = 6 * 60; m < 22 * 60; m += 30) {
    const t = minutesToTime(m);
    const check = checkCourtConflict(selectedCourtId, selectedDate, t, 30);
    let cls = '';
    let title = `${t} - Trống`;

    if (check.conflict) {
      if (check.type === 'lock') {
        cls = 'locked';
        title = `${t} - Bảo trì`;
      } else if (check.booking.status === 'pending') {
        cls = 'pending';
        title = `${t} - Chờ duyệt`;
      } else {
        cls = 'booked';
        title = `${t} - Đã đặt`;
      }
    }

    slots.push(`<div class="timeline-seg ${cls}" title="${escapeAttr(title)}" onclick="selectStartTime('${t}')"></div>`);
  }

  bar.innerHTML = slots.join('');

  if (ticks) {
    ticks.innerHTML = ['06:00', '09:00', '12:00', '15:00', '18:00', '21:00']
      .map(t => `<span class="timeline-tick">${t}</span>`)
      .join('');
  }
}

function renderTimeSlotGrid() {
  const grid = document.getElementById('timeSlotGrid');
  const select = document.getElementById('bookingStart');
  if (!grid) return;

  const todayStr = getTodayStr();
  const now = new Date();
  const nowMins = now.getHours() * 60 + now.getMinutes();

  grid.innerHTML = BOOKING_TIME_SLOTS.map(time => {
    const check = checkCourtConflict(selectedCourtId, selectedDate, time, selectedDuration);
    const isPastToday = selectedDate === todayStr && timeToMinutes(time) < (nowMins - 10);
    const isConflict = check.conflict || isPastToday;
    const isSelected = time === selectedStartTime && !isConflict;
    const rate = getCourtHourlyRate(selectedCourtId, time);
    const peakTag = isPeakHour(time) ? '⚡ Giờ vàng' : 'Giờ thường';

    let subText = `${formatMoney(rate)}/h · ${peakTag}`;
    if (isPastToday) {
      subText = 'Đã qua giờ';
    } else if (check.conflict) {
      subText = check.type === 'lock' ? 'Bảo trì' : 'Đã kín lịch';
    }

    return `
      <button type="button"
        class="slot-btn ${isSelected ? 'selected' : ''} ${isConflict ? 'occupied' : ''}"
        ${isConflict ? 'disabled' : ''}
        onclick="selectStartTime('${time}')">
        <span class="slot-btn-time">${time} – ${getEndTime(time, selectedDuration)}</span>
        <span class="slot-btn-price">${escapeHtml(subText)}</span>
      </button>
    `;
  }).join('');

  if (select) {
    select.innerHTML = BOOKING_TIME_SLOTS.map(t => {
      const c = checkCourtConflict(selectedCourtId, selectedDate, t, selectedDuration);
      return `<option value="${t}" ${t === selectedStartTime ? 'selected' : ''}>${t} (${c.conflict ? 'Đã kín' : 'Còn trống'})</option>`;
    }).join('');
  }
}

function selectStartTime(time) {
  selectedStartTime = time;
  renderTimeSlotGrid();
  updatePriceBreakdown();
}

function updatePriceBreakdown() {
  const court = getCourtById(selectedCourtId);
  const endTime = getEndTime(selectedStartTime, selectedDuration);

  // Segmented calculation across normal (< 17:00) and peak (>= 17:00) slices (P1-08, P1-09)
  const quote = calculateSegmentedPricing(selectedCourtId, selectedStartTime, selectedDuration);

  const paddlesChecked = document.getElementById('extraPaddles')?.checked;
  const ballsChecked = document.getElementById('extraBalls')?.checked;
  const extrasFee = (paddlesChecked ? 80000 : 0) + (ballsChecked ? 50000 : 0);
  const grandTotal = quote.netCourtFee + extrasFee;
  const players = parseInt(document.getElementById('bookingPlayers')?.value || '4');
  const perPerson = Math.ceil(grandTotal / players);

  const titleEl = document.getElementById('bookingFormTitle');
  if (titleEl && court) titleEl.textContent = `Phiếu đặt — ${court.name}`;

  const priceBadge = document.getElementById('bookingCourtPriceBadge');
  if (priceBadge) {
    if (quote.normalMinutes > 0 && quote.peakMinutes > 0) {
      priceBadge.textContent = `Kết hợp Giờ thường & Giờ vàng ⚡`;
    } else {
      const curRate = quote.peakMinutes > 0 ? quote.peakRate : quote.standardRate;
      priceBadge.textContent = `${formatMoney(curRate)} / giờ ${quote.peakMinutes > 0 ? '(Giờ vàng)' : ''}`;
    }
  }

  const dtEl = document.getElementById('summaryDateTime');
  if (dtEl) dtEl.textContent = `${selectedStartTime} → ${endTime} · ${formatDateVN(selectedDate)}`;

  const rateEl = document.getElementById('summaryHourlyRate');
  if (rateEl) {
    if (quote.normalMinutes > 0 && quote.peakMinutes > 0) {
      rateEl.textContent = `${quote.normalMinutes}p thường (${formatMoney(quote.normalSubtotal)}) + ${quote.peakMinutes}p vàng (${formatMoney(quote.peakSubtotal)})`;
    } else {
      const r = quote.peakMinutes > 0 ? quote.peakRate : quote.standardRate;
      rateEl.textContent = `${formatMoney(r)}/h × ${selectedDuration} phút`;
    }
  }

  const feeEl = document.getElementById('summaryCourtFee');
  if (feeEl) {
    if (quote.discountAmount > 0) {
      feeEl.innerHTML = `<span style="text-decoration:line-through;color:var(--text-muted);margin-right:6px;">${formatMoney(quote.grossCourtFee)}</span><strong style="color:var(--color-success)">${formatMoney(quote.netCourtFee)} (-${quote.discountPercent}% ${escapeHtml(quote.memberTier)})</strong>`;
    } else {
      feeEl.textContent = formatMoney(quote.netCourtFee);
    }
  }

  const extrasRow = document.getElementById('summaryExtrasRow');
  const extrasVal = document.getElementById('summaryExtrasFee');
  if (extrasRow && extrasVal) {
    extrasRow.style.display = extrasFee > 0 ? 'flex' : 'none';
    extrasVal.textContent = formatMoney(extrasFee);
  }

  const totalEl = document.getElementById('summaryTotal');
  if (totalEl) totalEl.textContent = formatMoney(grandTotal);

  const splitEl = document.getElementById('summarySplitPerPerson');
  if (splitEl) splitEl.textContent = `${formatMoney(perPerson)} / người (${players} VĐV)`;

  const conflictBanner = document.getElementById('conflictBanner');
  const conflictText = document.getElementById('conflictBannerText');
  const check = checkCourtConflict(selectedCourtId, selectedDate, selectedStartTime, selectedDuration);
  if (conflictBanner) {
    if (check.conflict) {
      conflictBanner.classList.add('show');
      if (conflictText) conflictText.textContent = check.reason;
    } else {
      conflictBanner.classList.remove('show');
    }
  }
}

async function submitBooking() {
  if (isSubmittingBooking) return;

  const name = document.getElementById('bookingName')?.value.trim();
  const phone = document.getElementById('bookingPhone')?.value.trim();
  const players = parseInt(document.getElementById('bookingPlayers')?.value || '4');
  const skillLevel = document.getElementById('bookingSkill')?.value || 'Giao lưu (DUPR 3.0)';

  if (!name || name.length < 2) {
    showToast('Thiếu thông tin!', 'Vui lòng nhập Họ và tên (tối thiểu 2 ký tự).', 'error');
    return;
  }

  if (!phone || !/^(0|\+84)[0-9]{8,10}$/.test(phone.replace(/[\s.-]/g, ''))) {
    showToast('Số điện thoại chưa hợp lệ!', 'Vui lòng nhập đúng số điện thoại 10 chữ số để nhận mã QR.', 'error');
    return;
  }

  const check = checkCourtConflict(selectedCourtId, selectedDate, selectedStartTime, selectedDuration);
  if (check.conflict) {
    showToast('Trùng khung giờ!', check.reason, 'error');
    return;
  }

  isSubmittingBooking = true;
  try {
    const res = await fetch('/api/bookings', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        courtId: selectedCourtId,
        customerName: name,
        customerPhone: phone,
        date: selectedDate,
        time: selectedStartTime,
        duration: selectedDuration,
        players,
        skillLevel,
        paymentMethod: 'cash'
      })
    });

    const data = await res.json();
    if (!res.ok || !data.ok) {
      showToast('Không thể đặt sân!', data.error || 'Khung giờ đã có người đặt, vui lòng chọn giờ khác.', 'error');
      await syncDatabaseFromServer();
      return;
    }

    const createdBooking = data.booking;

    // Refresh global state from server
    await syncDatabaseFromServer();

    // Render real signed QR code in modal (P1-01)
    const court = getCourtById(createdBooking.courtId);
    const qrToken = createdBooking.checkInToken || createdBooking.id;
    const qrPlaceholder = document.querySelector('#modalQR .qr-placeholder');
    if (qrPlaceholder) {
      qrPlaceholder.innerHTML = `
        ${renderQrCodeSvg(qrToken, 152)}
        <div class="text-xs font-bold text-mono mt-sm" id="qrCodeBadge" style="color:var(--color-indigo)">${escapeHtml(createdBooking.id)}</div>
        <div style="font-size:0.68rem;color:var(--text-muted);font-family:var(--font-mono);word-break:break-all;max-width:220px;margin:2px auto 0;">Token: ${escapeHtml(qrToken.slice(0, 26))}...</div>
      `;
    }

    const setEl = (id, val) => {
      const el = document.getElementById(id);
      if (el) el.textContent = val;
    };

    setEl('qrRef', createdBooking.id);
    setEl('qrName', `${createdBooking.name} (${createdBooking.phone})`);
    setEl('qrDate', formatDateVN(createdBooking.date));
    setEl('qrCourt', court ? court.name : `Sân #${createdBooking.courtId}`);
    setEl('qrTime', `${createdBooking.time} → ${getEndTime(createdBooking.time, createdBooking.duration)} (${createdBooking.duration} phút)`);
    setEl('qrTotal', `${formatMoney(createdBooking.amountDue || createdBooking.courtFee)} (Chờ thanh toán tại quầy)`);

    openModal('modalQR');
    showToast('Đặt sân thành công!', `Mã vé điện tử của bạn là ${createdBooking.id}`);
  } catch (err) {
    showToast('Lỗi kết nối!', 'Không thể gửi yêu cầu đặt sân tới máy chủ.', 'error');
  } finally {
    isSubmittingBooking = false;
  }
}

document.addEventListener('DOMContentLoaded', initBookingPage);
window.addEventListener('pickleball:db-synced', () => {
  renderCourtOptions();
  renderTimeline();
  renderTimeSlotGrid();
  updatePriceBreakdown();
});
