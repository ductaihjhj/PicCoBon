/* ================================================================
   SPIKE PICKLEBALL CLUB — BOOKING PAGE ENGINE (Server-Authoritative V5)
   Strict Server Truth (UI-A01), AbortController Quote Binding (UI-A02),
   Safe Court Lookup (UI-A06), Horizon Date Strip + Picker (UI-A07),
   Zero-Slot Reset & CTA Guard (UI-A08), Single QR DOM (UI-C01), Keyboard A11y (UI-C02)
   ================================================================ */

let selectedCourtId = null;
let selectedDate = getTodayStr();
let selectedDuration = 60;
let selectedStartTime = '';
let isSubmittingBooking = false;
let latestServerQuote = null;
let latestQuoteKey = '';
let quoteStatus = 'idle'; // 'idle' | 'loading' | 'ready' | 'error'
let quoteAbortController = null;
let lastIssuedGuestToken = '';
let quoteRequestCounter = 0;
let initialUrlParamsApplied = false;
let requestedInvalidCourtId = null;
let pendingDeepLinkTime = '';

function getBookingMaxHorizonDays() {
  return IS_STAFF_OR_ADMIN ? 60 : 30;
}

function validateAndNormalizeBookingDate(rawDateStr) {
  const todayIso = getTodayStr();
  const maxDays = getBookingMaxHorizonDays();
  const maxDateIso = getOffsetDateStr(maxDays);
  const parsed = parseLocalDateIso(rawDateStr);

  if (!parsed) {
    return {
      valid: false,
      normalizedDate: todayIso,
      reason: `Định dạng ngày "${rawDateStr || ''}" không hợp lệ. Hệ thống đã chuyển về hôm nay (${formatDateVN(todayIso)}).`
    };
  }

  const cleanIso = rawDateStr.trim();
  if (cleanIso < todayIso) {
    return {
      valid: false,
      normalizedDate: todayIso,
      reason: `Ngày ${formatDateVN(cleanIso)} đã qua. Hệ thống đã chuyển về hôm nay (${formatDateVN(todayIso)}).`
    };
  }

  if (cleanIso > maxDateIso) {
    return {
      valid: false,
      normalizedDate: maxDateIso,
      reason: `Lịch đặt trực tuyến mở tối đa ${maxDays} ngày tới (đến ${formatDateVN(maxDateIso)}).`
    };
  }

  return {
    valid: true,
    normalizedDate: cleanIso,
    reason: ''
  };
}

function setDateNotice(message = '') {
  const banner = document.getElementById('bookingDateNotice');
  const textEl = document.getElementById('bookingDateNoticeText');
  if (!banner || !textEl) return;
  if (message) {
    textEl.textContent = message;
    banner.classList.add('show');
  } else {
    textEl.textContent = '';
    banner.classList.remove('show');
  }
}

function getDynamicTimeSlots(durationMins = selectedDuration) {
  const openMins = timeToMinutes(CMS_CONFIG.openTime || '06:00') || (6 * 60);
  const closeMins = timeToMinutes(CMS_CONFIG.closeTime || '22:00') || (22 * 60);
  const dur = Math.max(30, Number(durationMins) || 60);
  const slots = [];
  for (let m = openMins; m + dur <= closeMins; m += 30) {
    slots.push(minutesToTime(m));
  }
  return slots;
}

function isSlotAvailable(courtId, dateStr, timeStr, durationMins = selectedDuration) {
  if (!HAS_SERVER_STATE) return false;
  const court = getCourtById(courtId);
  if (!court) return false;
  const todayStr = getTodayStr();
  const nowMins = getNowMinutes();
  if (dateStr === todayStr && timeToMinutes(timeStr) < (nowMins - 5)) {
    return false;
  }
  const check = checkCourtConflict(court.id, dateStr, timeStr, durationMins);
  return !check.conflict;
}

function getAvailableTimeSlots(courtId = selectedCourtId, dateStr = selectedDate, durationMins = selectedDuration) {
  if (!HAS_SERVER_STATE || !getCourtById(courtId)) return [];
  const allSlots = getDynamicTimeSlots(durationMins);
  return allSlots.filter(t => isSlotAvailable(courtId, dateStr, t, durationMins));
}

function makeQuoteKey(courtId = selectedCourtId, dateStr = selectedDate, timeStr = selectedStartTime, durationMins = selectedDuration) {
  if (!courtId || !dateStr || !timeStr || !durationMins) return '';
  return `${Number(courtId)}|${dateStr}|${timeStr}|${Number(durationMins)}`;
}

function invalidateServerQuote() {
  latestServerQuote = null;
  latestQuoteKey = '';
  if (quoteAbortController) {
    try { quoteAbortController.abort(); } catch (_) {}
    quoteAbortController = null;
  }
}

/**
 * UI-A08: Ensures selectedStartTime only ever points to an AVAILABLE slot.
 * If all slots for the selected court & date are occupied/locked/past, resets selectedStartTime = ''!
 */
function ensureValidSelectedTime() {
  if (!HAS_SERVER_STATE || !getCourtById(selectedCourtId)) {
    selectedStartTime = '';
    return;
  }

  const available = getAvailableTimeSlots(selectedCourtId, selectedDate, selectedDuration);
  if (available.length === 0) {
    selectedStartTime = '';
    return;
  }

  if (pendingDeepLinkTime && available.includes(pendingDeepLinkTime)) {
    selectedStartTime = pendingDeepLinkTime;
    pendingDeepLinkTime = '';
    return;
  }
  pendingDeepLinkTime = '';

  if (!selectedStartTime || !available.includes(selectedStartTime)) {
    selectedStartTime = available.includes('17:00') ? '17:00' : available[0];
  }
}

function applyUrlParamsOnce() {
  const params = new URLSearchParams(window.location.search);
  const dateParam = params.get('date');
  const timeParam = params.get('time');
  const durParam = parseInt(params.get('duration') || '', 10);

  if (dateParam) {
    const checkDate = validateAndNormalizeBookingDate(dateParam);
    selectedDate = checkDate.normalizedDate;
    if (!checkDate.valid) {
      setDateNotice(checkDate.reason);
    }
  }

  if ([60, 90, 120, 180].includes(durParam)) {
    selectedDuration = durParam;
  }

  if (timeParam && /^\d{2}:\d{2}$/.test(timeParam.trim())) {
    pendingDeepLinkTime = timeParam.trim();
  }
}

function resolveCourtSelectionAfterSync() {
  if (!HAS_SERVER_STATE) return;

  const params = new URLSearchParams(window.location.search);
  const rawCourtParam = params.get('court');

  if (!initialUrlParamsApplied) {
    initialUrlParamsApplied = true;
    if (rawCourtParam !== null && rawCourtParam.trim() !== '') {
      const parsedCourtId = Number(rawCourtParam);
      const matchedCourt = getCourtById(parsedCourtId);
      if (matchedCourt) {
        selectedCourtId = Number(matchedCourt.id);
        requestedInvalidCourtId = null;
      } else {
        requestedInvalidCourtId = rawCourtParam.trim();
        selectedCourtId = COURTS.length > 0 ? Number(COURTS[0].id) : null;
        showToast(
          'Sân yêu cầu không khả dụng',
          `Sân #${requestedInvalidCourtId} không tồn tại hoặc đang ngừng hoạt động. Hệ thống đã chuyển sang sân khả dụng.`,
          'warning'
        );
      }
      return;
    }
  }

  const existing = getCourtById(selectedCourtId);
  if (existing) {
    selectedCourtId = Number(existing.id);
  } else if (COURTS.length > 0) {
    selectedCourtId = Number(COURTS[0].id);
  } else {
    selectedCourtId = null;
  }
}

function initBookingPage() {
  applyUrlParamsOnce();

  const user = getCurrentUser();
  if (user) {
    const nameEl = document.getElementById('bookingName');
    const phoneEl = document.getElementById('bookingPhone');
    if (nameEl && !nameEl.value && user.name) nameEl.value = user.name;
    if (phoneEl && !phoneEl.value && user.phone) phoneEl.value = user.phone;
  }

  if (HAS_SERVER_STATE) {
    resolveCourtSelectionAfterSync();
    ensureValidSelectedTime();
  }

  renderPeakBadge();
  renderDateStrip();
  renderCourtOptions();
  renderDurationPills();
  renderTimeline();
  renderTimeSlotGrid();
  updatePriceBreakdown(true);
}

function renderPeakBadge() {
  const badge = document.getElementById('heroPeakBadge');
  if (!badge) return;
  const peakStart = CMS_CONFIG.peakStart || '17:00';
  const closeTime = CMS_CONFIG.closeTime || '22:00';
  badge.textContent = `Giờ vàng: ${peakStart} – ${closeTime}`;
}

function renderDateStrip() {
  const strip = document.getElementById('quickDateStrip');
  const label = document.getElementById('selectedDateLabel');
  const picker = document.getElementById('bookingDatePicker');
  if (!strip) return;

  const todayIso = getTodayStr();
  const maxDateIso = getOffsetDateStr(getBookingMaxHorizonDays());

  if (picker) {
    picker.min = todayIso;
    picker.max = maxDateIso;
    picker.value = selectedDate;
  }

  const dayNames = ['Chủ nhật', 'Thứ 2', 'Thứ 3', 'Thứ 4', 'Thứ 5', 'Thứ 6', 'Thứ 7'];
  const stripDates = [];
  for (let i = 0; i < 7; i++) {
    stripDates.push(getOffsetDateStr(i));
  }

  // UI-A07: If selectedDate is valid and beyond the first 7 days, include it in the strip so it has an active pill
  if (selectedDate && !stripDates.includes(selectedDate)) {
    stripDates.push(selectedDate);
  }

  const pillsHtml = stripDates.map((dateStr, idx) => {
    const d = parseLocalDateIso(dateStr) || new Date();
    const isToday = dateStr === todayIso;
    const isTomorrow = dateStr === getOffsetDateStr(1);
    const dow = isToday ? 'Hôm nay' : isTomorrow ? 'Ngày mai' : dayNames[d.getDay()];
    const dd = String(d.getDate()).padStart(2, '0');
    const mm = `Thg ${String(d.getMonth() + 1).padStart(2, '0')}`;
    const isActive = dateStr === selectedDate;

    return `
      <button type="button"
        class="date-pill ${isActive ? 'active' : ''}"
        data-date="${escapeAttr(dateStr)}"
        aria-pressed="${isActive ? 'true' : 'false'}"
        aria-label="Chọn ngày ${escapeAttr(formatDateVN(dateStr))}"
        onclick="selectBookingDate('${escapeAttr(dateStr)}')">
        <span class="date-pill-day">${escapeHtml(dow)}</span>
        <span class="date-pill-num">${escapeHtml(dd)}</span>
        <span class="date-pill-month">${escapeHtml(mm)}${idx >= 7 ? ' ★' : ''}</span>
      </button>
    `;
  });

  strip.innerHTML = pillsHtml.join('');
  if (label) label.textContent = formatDateVN(selectedDate);

  const activePill = strip.querySelector('.date-pill.active');
  if (activePill && typeof activePill.scrollIntoView === 'function') {
    try {
      activePill.scrollIntoView({ behavior: 'smooth', inline: 'nearest', block: 'nearest' });
    } catch (_) {}
  }
}

function selectBookingDate(dateStr) {
  const check = validateAndNormalizeBookingDate(dateStr);
  selectedDate = check.normalizedDate;
  setDateNotice(check.valid ? '' : check.reason);

  invalidateServerQuote();
  ensureValidSelectedTime();
  renderDateStrip();
  renderCourtOptions();
  renderTimeline();
  renderTimeSlotGrid();
  updatePriceBreakdown(true);
}

function renderCourtOptions() {
  const container = document.getElementById('courtOptions');
  if (!container) return;

  // UI-A01 & UI-C03: Never render fake court availability when server state is not ready
  if (!HAS_SERVER_STATE) {
    const stateType = (SPIKE_SYNC_STATE === 'offline' || SPIKE_SYNC_STATE === 'error') ? SPIKE_SYNC_STATE : 'loading';
    container.innerHTML = `<div style="grid-column:1/-1;">${renderUiStateBanner(stateType)}</div>`;
    return;
  }

  if (COURTS.length === 0) {
    container.innerHTML = `<div style="grid-column:1/-1;">${renderUiStateBanner('empty', {
      title: 'Hiện chưa có sân thi đấu nào đang mở đặt trực tuyến',
      message: 'Câu lạc bộ đang cập nhật danh sách sân thi đấu. Vui lòng liên hệ Hotline lễ tân để được hỗ trợ.'
    })}</div>`;
    return;
  }

  const invalidCourtBanner = requestedInvalidCourtId
    ? `<div class="conflict-banner show" style="grid-column:1/-1;margin-bottom:0.5rem;" role="alert">
         <iconify-icon icon="lucide:alert-triangle" width="16"></iconify-icon>
         <span>Sân <strong>#${escapeHtml(requestedInvalidCourtId)}</strong> trong liên kết không tồn tại hoặc đã ngừng hoạt động. Hệ thống đã chọn <strong>${escapeHtml(getCourtDisplayName(selectedCourtId))}</strong>.</span>
       </div>`
    : '';

  const cardsHtml = COURTS.map(court => {
    const isActive = Number(court.id) === Number(selectedCourtId);
    const activeClass = isActive ? 'active' : '';
    const freeSlotsCount = getAvailableTimeSlots(court.id, selectedDate, selectedDuration).length;
    const bookingsCount = BOOKINGS.filter(b =>
      Number(b.courtId) === Number(court.id) &&
      b.date === selectedDate &&
      !['rejected', 'cancelled', 'expired', 'no_show'].includes(b.status) &&
      (b.status !== 'pending' || isPendingBookingHoldActive(b))
    ).length;

    const stdRate = (Number(court.basePrice) || Number(CMS_CONFIG.courtPrice) || 120000) + (Number(court.surcharge) || 0);
    const peakRate = stdRate + (Number(CMS_CONFIG.peakSurcharge) ?? 30000);
    const cleanSurface = String(court.surface || 'Acrylic Cushion 8 lớp').replace(/USAPA\s*/gi, '');
    const slotStatusHtml = freeSlotsCount > 0
      ? `<span class="text-xs font-semibold" style="color:var(--color-success)">Còn ${freeSlotsCount} khung giờ trống</span>`
      : `<span class="text-xs font-semibold" style="color:var(--color-danger)">Kín lịch hôm nay (${bookingsCount} ca)</span>`;

    return `
      <button type="button"
        class="court-option ${activeClass}"
        role="radio"
        aria-checked="${isActive ? 'true' : 'false'}"
        data-court-id="${Number(court.id)}"
        onclick="selectCourt(${Number(court.id)})">
        <div class="flex-between mb-sm" style="width:100%;">
          <span class="badge ${court.type === 'Indoor' ? 'badge-primary' : 'badge-neutral'}">${escapeHtml(court.type === 'Indoor' ? 'Trong nhà VIP' : 'Ngoài trời')}</span>
          ${slotStatusHtml}
        </div>
        <div class="court-option-name">${escapeHtml(court.name)}</div>
        <div class="court-option-meta">${escapeHtml(cleanSurface)}</div>
        <div class="court-option-price">${formatMoney(stdRate)}<span style="font-size:0.72rem;font-weight:500;color:var(--text-secondary)">/h · Giờ vàng ${formatMoney(peakRate)}/h</span></div>
      </button>
    `;
  }).join('');

  container.innerHTML = invalidCourtBanner + cardsHtml;
}

function selectCourt(courtId) {
  const target = getCourtById(courtId);
  if (!target) {
    showToast('Sân không tồn tại', `Sân #${courtId} không có trong danh sách hoạt động.`, 'error');
    return;
  }
  requestedInvalidCourtId = null;
  selectedCourtId = Number(target.id);
  invalidateServerQuote();
  ensureValidSelectedTime();
  renderCourtOptions();
  renderTimeline();
  renderTimeSlotGrid();
  updatePriceBreakdown(true);
}

function renderDurationPills() {
  document.querySelectorAll('#durationPills .duration-pill').forEach(btn => {
    const dur = parseInt(btn.getAttribute('data-dur'), 10);
    const isActive = dur === selectedDuration;
    btn.classList.toggle('active', isActive);
    btn.setAttribute('aria-pressed', isActive ? 'true' : 'false');
  });
}

function setDuration(dur) {
  selectedDuration = Number(dur) || 60;
  invalidateServerQuote();
  ensureValidSelectedTime();
  renderDurationPills();
  renderCourtOptions();
  renderTimeline();
  renderTimeSlotGrid();
  updatePriceBreakdown(true);
}

function renderTimeline() {
  const bar = document.getElementById('bookingTimeline');
  const ticks = document.getElementById('bookingTicks');
  if (!bar) return;

  if (!HAS_SERVER_STATE || !getCourtById(selectedCourtId)) {
    bar.innerHTML = `<div class="timeline-seg" style="flex:1;opacity:0.35;" title="Đang đồng bộ trạng thái sân..."></div>`;
    if (ticks) ticks.innerHTML = '';
    return;
  }

  const openMins = timeToMinutes(CMS_CONFIG.openTime || '06:00') || (6 * 60);
  const closeMins = timeToMinutes(CMS_CONFIG.closeTime || '22:00') || (22 * 60);
  const todayStr = getTodayStr();
  const nowMins = getNowMinutes();

  const slots = [];
  for (let m = openMins; m < closeMins; m += 30) {
    const t = minutesToTime(m);
    const check = checkCourtConflict(selectedCourtId, selectedDate, t, 30);
    const isPast = selectedDate === todayStr && m < (nowMins - 5);
    let cls = '';
    let title = `${t} - Trống`;

    if (check.conflict) {
      if (check.type === 'lock') {
        cls = 'locked';
        title = `${t} - Bảo trì`;
      } else if (check.booking && check.booking.status === 'pending') {
        cls = 'pending';
        title = `${t} - Chờ duyệt`;
      } else {
        cls = 'booked';
        title = `${t} - Đã đặt`;
      }
    } else if (isPast) {
      cls = 'booked';
      title = `${t} - Đã qua giờ`;
    }

    const canPick = (m + selectedDuration <= closeMins) && !check.conflict && !isPast;
    slots.push(`<div class="timeline-seg ${cls}" title="${escapeAttr(title)}" ${canPick ? `onclick="selectStartTime('${t}')"` : ''}></div>`);
  }

  bar.innerHTML = slots.join('');

  if (ticks) {
    const step = Math.max(60, Math.round((closeMins - openMins) / 5 / 60) * 60);
    const tickLabels = [];
    for (let m = openMins; m <= closeMins; m += step) {
      tickLabels.push(minutesToTime(m));
    }
    if (tickLabels[tickLabels.length - 1] !== minutesToTime(closeMins)) {
      tickLabels.push(minutesToTime(closeMins));
    }
    ticks.innerHTML = tickLabels.map(t => `<span class="timeline-tick">${t}</span>`).join('');
  }
}

function renderTimeSlotGrid() {
  const grid = document.getElementById('timeSlotGrid');
  const select = document.getElementById('bookingStart');
  if (!grid) return;

  if (!HAS_SERVER_STATE) {
    const stateType = (SPIKE_SYNC_STATE === 'offline' || SPIKE_SYNC_STATE === 'error') ? SPIKE_SYNC_STATE : 'loading';
    grid.innerHTML = `<div style="grid-column:1/-1;">${renderUiStateBanner(stateType, { compact: true })}</div>`;
    if (select) {
      select.innerHTML = '<option value="">Đang đồng bộ dữ liệu...</option>';
      select.disabled = true;
    }
    return;
  }

  if (!getCourtById(selectedCourtId)) {
    grid.innerHTML = `<div style="grid-column:1/-1;">${renderUiStateBanner('empty', {
      compact: true,
      title: 'Chưa có sân thi đấu khả dụng',
      message: 'Vui lòng chọn một sân thi đấu đang hoạt động để xem khung giờ.'
    })}</div>`;
    if (select) {
      select.innerHTML = '<option value="">Chưa có sân</option>';
      select.disabled = true;
    }
    return;
  }

  const slots = getDynamicTimeSlots(selectedDuration);
  const availableSlots = getAvailableTimeSlots(selectedCourtId, selectedDate, selectedDuration);
  const todayStr = getTodayStr();
  const nowMins = getNowMinutes();

  if (slots.length === 0) {
    grid.innerHTML = `<div style="grid-column:1/-1;">${renderUiStateBanner('filtered-empty', {
      compact: true,
      title: `Không có khung giờ phù hợp với thời lượng ${selectedDuration} phút`,
      message: 'Hãy chọn thời lượng ngắn hơn (60 hoặc 90 phút).'
    })}</div>`;
    if (select) {
      select.innerHTML = '<option value="">Không có khung giờ</option>';
      select.disabled = true;
    }
    return;
  }

  // UI-A08: Rich empty/full state when 0 slots are available on this court & date
  let fullNoticeHtml = '';
  if (availableSlots.length === 0) {
    const altCourt = COURTS.find(c =>
      Number(c.id) !== Number(selectedCourtId) &&
      getAvailableTimeSlots(c.id, selectedDate, selectedDuration).length > 0
    );
    const nextDayIso = getOffsetDateStr(1);
    const altCourtBtn = altCourt
      ? `<button type="button" class="btn btn-primary btn-sm" onclick="selectCourt(${Number(altCourt.id)})">
           <iconify-icon icon="lucide:arrow-right-circle" width="15"></iconify-icon>
           <span>Chuyển sang ${escapeHtml(altCourt.name)}</span>
         </button>`
      : '';
    const nextDayBtn = selectedDate !== nextDayIso
      ? `<button type="button" class="btn btn-outline btn-sm" onclick="selectBookingDate('${escapeAttr(nextDayIso)}')">
           <iconify-icon icon="lucide:calendar-plus" width="15"></iconify-icon>
           <span>Xem lịch Ngày mai (${escapeHtml(formatDateVN(nextDayIso))})</span>
         </button>`
      : '';

    fullNoticeHtml = `
      <div style="grid-column:1/-1;margin-bottom:0.5rem;">
        ${renderUiStateBanner('filtered-empty', {
          compact: true,
          icon: 'lucide:calendar-x-2',
          title: `${getCourtDisplayName(selectedCourtId)} đã hết khung giờ trống trong ngày ${formatDateVN(selectedDate)}`,
          message: 'Tất cả các khung giờ trong ngày này đã được đặt, đang khóa bảo trì hoặc đã qua giờ. Vui lòng chọn sân khác hoặc chuyển sang ngày tiếp theo.',
          actionHtml: `${altCourtBtn} ${nextDayBtn}`.trim()
        })}
      </div>
    `;
  }

  const slotButtonsHtml = slots.map(time => {
    const check = checkCourtConflict(selectedCourtId, selectedDate, time, selectedDuration);
    const isPastToday = selectedDate === todayStr && timeToMinutes(time) < (nowMins - 5);
    const isConflict = check.conflict || isPastToday;
    const isSelected = !isConflict && time === selectedStartTime;
    const quote = calculateSegmentedPricing(selectedCourtId, time, selectedDuration);
    const peakTag = quote.peakMinutes > 0 && quote.normalMinutes > 0
      ? 'Thường + Vàng'
      : (quote.peakMinutes > 0 ? '⚡ Giờ vàng' : 'Giờ thường');

    let subText = `${formatMoney(quote.netCourtFee)} · ${peakTag}`;
    if (isPastToday) {
      subText = 'Đã qua giờ';
    } else if (check.conflict) {
      subText = check.type === 'lock' ? 'Bảo trì' : 'Đã kín lịch';
    }

    return `
      <button type="button"
        class="slot-btn ${isSelected ? 'selected' : ''} ${isConflict ? 'occupied' : ''}"
        aria-pressed="${isSelected ? 'true' : 'false'}"
        ${isConflict ? 'disabled' : ''}
        onclick="selectStartTime('${time}')">
        <span class="slot-btn-time">${time} – ${getEndTime(time, selectedDuration)}</span>
        <span class="slot-btn-price">${escapeHtml(subText)}</span>
      </button>
    `;
  }).join('');

  grid.innerHTML = fullNoticeHtml + slotButtonsHtml;

  if (select) {
    if (availableSlots.length === 0) {
      select.innerHTML = '<option value="">Hết khung giờ trống — Chọn sân/ngày khác</option>';
      select.value = '';
      select.disabled = true;
    } else {
      select.disabled = false;
      select.innerHTML = slots.map(t => {
        const c = checkCourtConflict(selectedCourtId, selectedDate, t, selectedDuration);
        const isPast = selectedDate === todayStr && timeToMinutes(t) < (nowMins - 5);
        const st = isPast ? 'Qua giờ' : (c.conflict ? 'Đã kín' : 'Còn trống');
        return `<option value="${t}" ${t === selectedStartTime ? 'selected' : ''} ${isPast || c.conflict ? 'disabled' : ''}>${t} (${st})</option>`;
      }).join('');
      select.value = selectedStartTime;
    }
  }
}

function selectStartTime(time) {
  if (!isSlotAvailable(selectedCourtId, selectedDate, time, selectedDuration)) {
    return;
  }
  if (selectedStartTime === time && quoteStatus === 'ready' && latestQuoteKey === makeQuoteKey(selectedCourtId, selectedDate, time, selectedDuration)) {
    return;
  }
  selectedStartTime = time;
  invalidateServerQuote();
  renderTimeSlotGrid();
  updatePriceBreakdown(true);
}

function updateQuoteBadgeDom() {
  const badge = document.getElementById('bookingQuoteSourceBadge');
  if (!badge) return;

  if (!HAS_SERVER_STATE) {
    badge.textContent = '⏳ Đang kết nối máy chủ...';
    badge.style.background = 'rgba(148,163,184,0.2)';
    badge.style.color = '#E2E8F0';
    badge.style.borderColor = 'rgba(148,163,184,0.4)';
    return;
  }

  if (!selectedStartTime) {
    badge.textContent = 'Chưa chọn khung giờ';
    badge.style.background = 'rgba(245,158,11,0.2)';
    badge.style.color = '#FCD34D';
    badge.style.borderColor = 'rgba(245,158,11,0.45)';
    return;
  }

  if (quoteStatus === 'loading') {
    badge.textContent = '⏳ Đang lấy báo giá máy chủ...';
    badge.style.background = 'rgba(59,130,246,0.22)';
    badge.style.color = '#93C5FD';
    badge.style.borderColor = 'rgba(59,130,246,0.45)';
    return;
  }

  if (quoteStatus === 'error') {
    badge.textContent = '⚠️ Lỗi báo giá — Nhấn thử lại';
    badge.style.background = 'rgba(239,68,68,0.25)';
    badge.style.color = '#FCA5A5';
    badge.style.borderColor = 'rgba(239,68,68,0.5)';
    return;
  }

  if (quoteStatus === 'ready' && latestServerQuote) {
    badge.textContent = '✓ Báo giá máy chủ';
    badge.style.background = 'rgba(216,248,38,0.18)';
    badge.style.color = 'var(--color-volt)';
    badge.style.borderColor = 'rgba(216,248,38,0.35)';
    return;
  }

  badge.textContent = 'Tạm tính · Chờ xác nhận';
}

function syncSubmitButtonState() {
  const btn = document.getElementById('btnSubmitBooking');
  if (!btn) return;
  const labelSpan = btn.querySelector('span') || btn;

  if (isSubmittingBooking) {
    btn.disabled = true;
    labelSpan.textContent = 'Đang xử lý đặt sân...';
    return;
  }

  if (!HAS_SERVER_STATE) {
    btn.disabled = true;
    labelSpan.textContent = SPIKE_SYNC_STATE === 'loading'
      ? 'Đang đồng bộ dữ liệu máy chủ...'
      : 'Mất kết nối máy chủ — Chưa thể đặt sân';
    return;
  }

  if (!getCourtById(selectedCourtId)) {
    btn.disabled = true;
    labelSpan.textContent = 'Chưa có sân thi đấu khả dụng';
    return;
  }

  if (!selectedStartTime) {
    btn.disabled = true;
    labelSpan.textContent = 'Hết khung giờ trống — Chọn sân/ngày khác';
    return;
  }

  if (!isSlotAvailable(selectedCourtId, selectedDate, selectedStartTime, selectedDuration)) {
    btn.disabled = true;
    labelSpan.textContent = 'Khung giờ đã kín — Chọn giờ khác';
    return;
  }

  if (quoteStatus === 'loading') {
    btn.disabled = true;
    labelSpan.textContent = 'Đang xác thực báo giá máy chủ...';
    return;
  }

  if (quoteStatus === 'error' || !latestServerQuote || latestQuoteKey !== makeQuoteKey(selectedCourtId, selectedDate, selectedStartTime, selectedDuration)) {
    btn.disabled = true;
    labelSpan.textContent = 'Chưa có báo giá máy chủ — Bấm lấy lại giá';
    return;
  }

  btn.disabled = false;
  labelSpan.textContent = 'Xác nhận đặt sân & Nhận mã QR';
}

function retryServerQuote() {
  if (!HAS_SERVER_STATE) {
    retryServerSync();
    return;
  }
  updatePriceBreakdown(true);
}

async function fetchServerQuote(reqId) {
  if (!HAS_SERVER_STATE || !getCourtById(selectedCourtId) || !selectedStartTime) {
    quoteStatus = 'idle';
    updateQuoteBadgeDom();
    syncSubmitButtonState();
    return;
  }

  const targetCourtId = Number(selectedCourtId);
  const targetDate = selectedDate;
  const targetTime = selectedStartTime;
  const targetDuration = Number(selectedDuration);
  const expectedKey = makeQuoteKey(targetCourtId, targetDate, targetTime, targetDuration);

  if (quoteAbortController) {
    try { quoteAbortController.abort(); } catch (_) {}
  }
  const controller = new AbortController();
  quoteAbortController = controller;
  quoteStatus = 'loading';
  updateQuoteBadgeDom();
  syncSubmitButtonState();

  try {
    const qs = new URLSearchParams({
      courtId: String(targetCourtId),
      date: targetDate,
      time: targetTime,
      duration: String(targetDuration)
    });
    const res = await fetch(`/api/bookings/quote?${qs.toString()}`, {
      headers: { 'Accept': 'application/json' },
      credentials: 'same-origin',
      cache: 'no-store',
      signal: controller.signal
    });

    if (reqId !== quoteRequestCounter) return;

    if (!res.ok) {
      latestServerQuote = null;
      latestQuoteKey = '';
      quoteStatus = 'error';
      updateQuoteBadgeDom();
      syncSubmitButtonState();
      const conflictBanner = document.getElementById('conflictBanner');
      const conflictText = document.getElementById('conflictBannerText');
      if (conflictBanner && conflictText) {
        conflictText.textContent = 'Không thể lấy báo giá từ máy chủ. Vui lòng nhấn vào huy hiệu "Lỗi báo giá" để thử lại.';
        conflictBanner.classList.add('show');
      }
      return;
    }

    const data = await res.json();
    if (reqId !== quoteRequestCounter) return;
    if (!data || !data.ok || !data.quote) {
      latestServerQuote = null;
      latestQuoteKey = '';
      quoteStatus = 'error';
      updateQuoteBadgeDom();
      syncSubmitButtonState();
      return;
    }

    // Verify the current selection has not drifted while awaiting response (UI-A02)
    if (makeQuoteKey(selectedCourtId, selectedDate, selectedStartTime, selectedDuration) !== expectedKey) {
      return;
    }

    latestServerQuote = data.quote;
    latestQuoteKey = expectedKey;
    quoteStatus = 'ready';
    if (data.quote.pricingVersion) {
      PRICING_VERSION = data.quote.pricingVersion;
    }

    applyQuoteToDom({
      standardRate: data.quote.standardRate,
      peakRate: data.quote.peakRate,
      normalMinutes: data.quote.normalMinutes,
      peakMinutes: data.quote.peakMinutes,
      normalSubtotal: data.quote.normalAmount,
      peakSubtotal: data.quote.peakAmount,
      grossCourtFee: data.quote.courtFee,
      discountPercent: data.quote.discountPercent,
      discountAmount: data.quote.discountAmount,
      netCourtFee: data.quote.amountDue,
      memberTier: data.quote.memberTier || 'Khách tiêu chuẩn',
      isServerVerified: true
    });
    updateQuoteBadgeDom();
    syncSubmitButtonState();
  } catch (err) {
    if (err && err.name === 'AbortError') return;
    if (reqId !== quoteRequestCounter) return;
    latestServerQuote = null;
    latestQuoteKey = '';
    quoteStatus = 'error';
    updateQuoteBadgeDom();
    syncSubmitButtonState();
    const conflictBanner = document.getElementById('conflictBanner');
    const conflictText = document.getElementById('conflictBannerText');
    if (conflictBanner && conflictText) {
      conflictText.textContent = 'Mất kết nối khi xác thực báo giá máy chủ. Vui lòng kiểm tra mạng và thử lại.';
      conflictBanner.classList.add('show');
    }
  }
}

function applyQuoteToDom(quote) {
  const court = getCourtById(selectedCourtId);
  const titleEl = document.getElementById('bookingFormTitle');
  if (titleEl) {
    titleEl.textContent = court ? `Phiếu đặt — ${court.name}` : 'Phiếu đặt sân';
  }

  const dtEl = document.getElementById('summaryDateTime');
  const rateEl = document.getElementById('summaryHourlyRate');
  const feeEl = document.getElementById('summaryCourtFee');
  const totalEl = document.getElementById('summaryTotal');
  const splitEl = document.getElementById('summarySplitPerPerson');
  const priceBadge = document.getElementById('bookingCourtPriceBadge');
  const conflictBanner = document.getElementById('conflictBanner');
  const conflictText = document.getElementById('conflictBannerText');

  if (!HAS_SERVER_STATE || !court || !selectedStartTime || !quote) {
    if (dtEl) dtEl.textContent = selectedStartTime ? `${selectedStartTime} · ${formatDateVN(selectedDate)}` : `Chưa chọn giờ · ${formatDateVN(selectedDate)}`;
    if (rateEl) rateEl.textContent = '--';
    if (feeEl) feeEl.textContent = '--';
    if (totalEl) totalEl.textContent = '0 đ';
    if (splitEl) splitEl.textContent = '0 đ / người';
    if (conflictBanner && conflictText) {
      if (!HAS_SERVER_STATE) {
        conflictText.textContent = 'Đang đồng bộ trạng thái sân và bảng giá trực tiếp từ máy chủ...';
        conflictBanner.classList.add('show');
      } else if (!selectedStartTime) {
        conflictText.textContent = 'Sân đã kín toàn bộ khung giờ trong ngày này. Vui lòng chọn sân khác hoặc ngày khác.';
        conflictBanner.classList.add('show');
      } else {
        conflictBanner.classList.remove('show');
      }
    }
    updateQuoteBadgeDom();
    syncSubmitButtonState();
    return;
  }

  const endTime = getEndTime(selectedStartTime, selectedDuration);
  const grandTotal = quote.netCourtFee;
  const players = parseInt(document.getElementById('bookingPlayers')?.value || '4', 10);
  const perPerson = Math.ceil(grandTotal / Math.max(1, players));

  if (priceBadge) {
    if (quote.normalMinutes > 0 && quote.peakMinutes > 0) {
      priceBadge.textContent = 'Kết hợp Giờ thường & Giờ vàng ⚡';
    } else {
      const curRate = quote.peakMinutes > 0 ? quote.peakRate : quote.standardRate;
      priceBadge.textContent = `${formatMoney(curRate)} / giờ ${quote.peakMinutes > 0 ? '(Giờ vàng)' : ''}`;
    }
  }

  if (dtEl) dtEl.textContent = `${selectedStartTime} → ${endTime} · ${formatDateVN(selectedDate)}`;

  if (rateEl) {
    if (quote.normalMinutes > 0 && quote.peakMinutes > 0) {
      rateEl.textContent = `${quote.normalMinutes}p thường (${formatMoney(quote.normalSubtotal)}) + ${quote.peakMinutes}p vàng (${formatMoney(quote.peakSubtotal)})`;
    } else {
      const r = quote.peakMinutes > 0 ? quote.peakRate : quote.standardRate;
      rateEl.textContent = `${formatMoney(r)}/h × ${selectedDuration} phút`;
    }
  }

  if (feeEl) {
    if (quote.discountAmount > 0) {
      feeEl.innerHTML = `<span style="text-decoration:line-through;color:var(--text-muted);margin-right:6px;">${formatMoney(quote.grossCourtFee)}</span><strong style="color:var(--color-success)">${formatMoney(quote.netCourtFee)} (-${Number(quote.discountPercent)}% ${escapeHtml(quote.memberTier)})</strong>`;
    } else {
      feeEl.textContent = formatMoney(quote.netCourtFee);
    }
  }

  if (totalEl) {
    totalEl.textContent = quote.isServerVerified
      ? formatMoney(grandTotal)
      : `~${formatMoney(grandTotal)} (Đang xác nhận...)`;
  }

  if (splitEl) splitEl.textContent = `~${formatMoney(perPerson)} / người (${players} VĐV)`;

  const todayStr = getTodayStr();
  const nowMins = getNowMinutes();
  const isPastToday = selectedDate === todayStr && timeToMinutes(selectedStartTime) < (nowMins - 5);
  const check = checkCourtConflict(selectedCourtId, selectedDate, selectedStartTime, selectedDuration);

  if (conflictBanner) {
    if (isPastToday) {
      conflictBanner.classList.add('show');
      if (conflictText) conflictText.textContent = 'Khung giờ bắt đầu này đã trôi qua. Vui lòng chọn giờ khác!';
    } else if (check.conflict) {
      conflictBanner.classList.add('show');
      if (conflictText) conflictText.textContent = check.reason;
    } else if (quoteStatus !== 'error') {
      conflictBanner.classList.remove('show');
    }
  }

  updateQuoteBadgeDom();
  syncSubmitButtonState();
}

function updatePriceBreakdown(fetchAuthoritative = false) {
  if (!HAS_SERVER_STATE || !getCourtById(selectedCourtId) || !selectedStartTime) {
    invalidateServerQuote();
    quoteStatus = 'idle';
    applyQuoteToDom(null);
    return;
  }

  const currentKey = makeQuoteKey(selectedCourtId, selectedDate, selectedStartTime, selectedDuration);
  if (fetchAuthoritative || latestQuoteKey !== currentKey || !latestServerQuote) {
    invalidateServerQuote();
    quoteStatus = 'loading';
    const localEstimate = calculateSegmentedPricing(selectedCourtId, selectedStartTime, selectedDuration);
    applyQuoteToDom({ ...localEstimate, isServerVerified: false });
    const reqId = ++quoteRequestCounter;
    fetchServerQuote(reqId);
  } else {
    applyQuoteToDom({
      standardRate: latestServerQuote.standardRate,
      peakRate: latestServerQuote.peakRate,
      normalMinutes: latestServerQuote.normalMinutes,
      peakMinutes: latestServerQuote.peakMinutes,
      normalSubtotal: latestServerQuote.normalAmount,
      peakSubtotal: latestServerQuote.peakAmount,
      grossCourtFee: latestServerQuote.courtFee,
      discountPercent: latestServerQuote.discountPercent,
      discountAmount: latestServerQuote.discountAmount,
      netCourtFee: latestServerQuote.amountDue,
      memberTier: latestServerQuote.memberTier || 'Khách tiêu chuẩn',
      isServerVerified: true
    });
  }
}

function copyGuestAccessToken() {
  if (!lastIssuedGuestToken) return;
  if (navigator.clipboard && navigator.clipboard.writeText) {
    navigator.clipboard.writeText(lastIssuedGuestToken).then(() => {
      showToast('Đã sao chép mã bảo mật', 'Hãy lưu lại mã này để tra cứu hoặc quản lý vé của bạn.', 'success');
    }).catch(() => {});
  }
}

async function submitBooking() {
  if (isSubmittingBooking) return;

  if (!HAS_SERVER_STATE) {
    showToast('Chưa đồng bộ máy chủ!', 'Hệ thống chưa tải được dữ liệu sân thực tế. Vui lòng thử kết nối lại.', 'error');
    return;
  }

  const court = getCourtById(selectedCourtId);
  if (!court) {
    showToast('Sân không hợp lệ!', 'Vui lòng chọn một sân thi đấu đang hoạt động.', 'error');
    return;
  }

  if (!selectedStartTime) {
    showToast('Hết khung giờ trống!', 'Vui lòng chọn sân thi đấu khác hoặc ngày thi đấu khác còn giờ trống.', 'error');
    return;
  }

  const name = document.getElementById('bookingName')?.value.trim();
  const phone = document.getElementById('bookingPhone')?.value.trim();
  const players = parseInt(document.getElementById('bookingPlayers')?.value || '4', 10);
  const skillLevel = document.getElementById('bookingSkill')?.value || 'Giao lưu (DUPR 3.0)';

  if (!name || name.length < 2) {
    showToast('Thiếu thông tin!', 'Vui lòng nhập Họ và tên người đặt (tối thiểu 2 ký tự).', 'error');
    document.getElementById('bookingName')?.focus();
    return;
  }

  if (!phone || !/^(0|\+84)[0-9]{8,10}$/.test(phone.replace(/[\s.-]/g, ''))) {
    showToast('Số điện thoại chưa hợp lệ!', 'Vui lòng nhập đúng số điện thoại 10 chữ số Việt Nam.', 'error');
    document.getElementById('bookingPhone')?.focus();
    return;
  }

  const check = checkCourtConflict(selectedCourtId, selectedDate, selectedStartTime, selectedDuration);
  if (check.conflict) {
    showToast('Trùng khung giờ!', check.reason, 'error');
    return;
  }

  const currentKey = makeQuoteKey(selectedCourtId, selectedDate, selectedStartTime, selectedDuration);
  if (!latestServerQuote || quoteStatus !== 'ready' || latestQuoteKey !== currentKey) {
    showToast('Đang xác thực báo giá máy chủ', 'Vui lòng chờ báo giá chính thức từ máy chủ hoàn tất trước khi xác nhận.', 'warning');
    updatePriceBreakdown(true);
    return;
  }

  const expectedAmountDue = Number(latestServerQuote.amountDue);
  const pricingVersion = latestServerQuote.pricingVersion || PRICING_VERSION || '';
  const idempotencyKey = generateIdempotencyKey('BK');

  isSubmittingBooking = true;
  syncSubmitButtonState();

  try {
    const res = await apiFetch('/api/bookings', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        courtId: Number(selectedCourtId),
        customerName: name,
        customerPhone: phone,
        date: selectedDate,
        time: selectedStartTime,
        duration: selectedDuration,
        players,
        skillLevel,
        paymentMethod: 'cash',
        pricingVersion,
        expectedAmountDue,
        idempotencyKey
      })
    });

    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      if (data && data.latestQuote) {
        latestServerQuote = data.latestQuote;
        latestQuoteKey = currentKey;
        quoteStatus = 'ready';
        if (data.latestQuote.pricingVersion) {
          PRICING_VERSION = data.latestQuote.pricingVersion;
        }
        applyQuoteToDom({
          standardRate: data.latestQuote.standardRate,
          peakRate: data.latestQuote.peakRate,
          normalMinutes: data.latestQuote.normalMinutes,
          peakMinutes: data.latestQuote.peakMinutes,
          normalSubtotal: data.latestQuote.normalAmount,
          peakSubtotal: data.latestQuote.peakAmount,
          grossCourtFee: data.latestQuote.courtFee,
          discountPercent: data.latestQuote.discountPercent,
          discountAmount: data.latestQuote.discountAmount,
          netCourtFee: data.latestQuote.amountDue,
          memberTier: data.latestQuote.memberTier || 'Khách tiêu chuẩn',
          isServerVerified: true
        });
        showToast('Bảng giá vừa được cập nhật!', `${data.error || 'Giá sân vừa thay đổi.'} Vui lòng kiểm tra tổng tiền mới (${formatMoney(data.latestQuote.amountDue)}) và bấm Xác nhận lại.`, 'warning');
        return;
      }

      showToast('Không thể đặt sân!', data.error || 'Khung giờ hoặc bảng giá vừa thay đổi, vui lòng kiểm tra lại.', 'error');
      await syncDatabaseFromServer(true);
      ensureValidSelectedTime();
      renderCourtOptions();
      renderTimeline();
      renderTimeSlotGrid();
      updatePriceBreakdown(true);
      return;
    }

    const createdBooking = data.booking;
    const guestAccessToken = data.guestAccessToken || '';
    lastIssuedGuestToken = guestAccessToken;

    if (guestAccessToken && createdBooking && createdBooking.id) {
      try {
        window.sessionStorage.setItem(`pb_guest_token_${createdBooking.id}`, guestAccessToken);
      } catch (_) {}
    }

    await syncDatabaseFromServer(true);

    const bookedCourtName = getCourtDisplayName(createdBooking.courtId, createdBooking.courtName);
    if (window.SpikeTicket && document.getElementById('bookingDigitalTicketHost')) {
      SpikeTicket.mountTicket('bookingDigitalTicketHost', createdBooking, {
        accessToken: guestAccessToken,
        theme: 'light'
      });
    }

    // UI-C01: Populate text-only test metadata WITHOUT creating a duplicate QR <img>
    const setEl = (id, val) => {
      const el = document.getElementById(id);
      if (el) el.textContent = val;
    };
    setEl('qrCodeBadge', createdBooking.id);

    const dlBtn = document.getElementById('qrDownloadPngBtn');
    if (dlBtn) {
      dlBtn.href = getBookingQrPngUrl(createdBooking.id, guestAccessToken);
      dlBtn.setAttribute('download', `Spike-QR-${createdBooking.id}.png`);
    }

    const tokenBox = document.getElementById('guestAccessTokenBox');
    const tokenValEl = document.getElementById('qrGuestTokenValue');
    if (tokenBox && tokenValEl) {
      if (guestAccessToken) {
        tokenValEl.textContent = guestAccessToken;
        tokenBox.style.display = 'block';
      } else {
        tokenBox.style.display = 'none';
      }
    }

    const lookupLink = document.getElementById('qrOpenLookupLink');
    if (lookupLink) {
      lookupLink.href = `/Lookup?code=${encodeURIComponent(createdBooking.id)}`;
    }

    const amountDue = getBookingGrandTotal(createdBooking);
    const paidAmount = Number(createdBooking.paidAmount || 0);
    const remainingDue = Math.max(0, amountDue - paidAmount);

    setEl('qrRef', createdBooking.id);
    setEl('qrStatusText', createdBooking.status === 'approved' ? 'Đã duyệt · Sẵn sàng Check-in đúng giờ' : 'Chờ lễ tân duyệt vé');
    setEl('qrName', `${createdBooking.name} (${createdBooking.phone})`);
    setEl('qrDate', formatDateVN(createdBooking.date));
    setEl('qrCourt', bookedCourtName);
    setEl('qrTime', `${createdBooking.time} → ${getEndTime(createdBooking.time, createdBooking.duration)} (${createdBooking.duration} phút)`);
    setEl('qrTotal', `Phải TT: ${formatMoney(amountDue)} · Đã TT: ${formatMoney(paidAmount)} · Còn lại: ${formatMoney(remainingDue)}`);

    openModal('modalQR');
    showToast('Đặt sân thành công!', `Mã vé điện tử của bạn là ${createdBooking.id}`);
  } catch (err) {
    showToast('Lỗi kết nối!', 'Không thể gửi yêu cầu đặt sân tới máy chủ.', 'error');
  } finally {
    isSubmittingBooking = false;
    syncSubmitButtonState();
  }
}

document.addEventListener('DOMContentLoaded', initBookingPage);
window.addEventListener('pickleball:db-synced', () => {
  resolveCourtSelectionAfterSync();
  ensureValidSelectedTime();
  renderPeakBadge();
  renderDateStrip();
  renderCourtOptions();
  renderTimeline();
  renderTimeSlotGrid();
  updatePriceBreakdown(true);
});
window.addEventListener('pickleball:sync-status', () => {
  if (!HAS_SERVER_STATE) {
    renderCourtOptions();
    renderTimeline();
    renderTimeSlotGrid();
    updateQuoteBadgeDom();
    syncSubmitButtonState();
  }
});
