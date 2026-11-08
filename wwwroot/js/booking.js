/* ================================================================
   SPIKE PICKLEBALL CLUB — BOOKING PAGE ENGINE (Server-Authoritative V2)
   Dynamic CMS hours, server /api/bookings/quote, pricingVersion, real QR & guest token
   ================================================================ */

let selectedCourtId = 1;
let selectedDate = getTodayStr();
let selectedDuration = 60;
let selectedStartTime = '17:00';
let isSubmittingBooking = false;
let latestServerQuote = null;
let lastIssuedGuestToken = '';
let quoteRequestCounter = 0;

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

function ensureValidSelectedTime() {
  const slots = getDynamicTimeSlots(selectedDuration);
  if (slots.length === 0) return;
  if (!slots.includes(selectedStartTime)) {
    const preferred = slots.find(s => s === '17:00') || slots[0];
    selectedStartTime = preferred;
  }
}

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
  if (dateParam && /^\d{4}-\d{2}-\d{2}$/.test(dateParam)) {
    selectedDate = dateParam;
  }
  if (timeParam && /^\d{2}:\d{2}$/.test(timeParam)) {
    selectedStartTime = timeParam;
  }

  ensureValidSelectedTime();

  const user = getCurrentUser();
  if (user) {
    const nameEl = document.getElementById('bookingName');
    const phoneEl = document.getElementById('bookingPhone');
    if (nameEl && !nameEl.value && user.name) nameEl.value = user.name;
    if (phoneEl && !phoneEl.value && user.phone) phoneEl.value = user.phone;
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
  if (!strip) return;

  const dayNames = ['Chủ nhật', 'Thứ 2', 'Thứ 3', 'Thứ 4', 'Thứ 5', 'Thứ 6', 'Thứ 7'];
  const days = [];

  for (let i = 0; i < 7; i++) {
    const dateStr = getOffsetDateStr(i);
    const d = new Date(dateStr + 'T00:00:00');
    const dow = i === 0 ? 'Hôm nay' : i === 1 ? 'Ngày mai' : dayNames[d.getDay()];
    const dd = String(d.getDate()).padStart(2, '0');
    const mm = `Thg ${String(d.getMonth() + 1).padStart(2, '0')}`;
    const active = dateStr === selectedDate ? 'active' : '';

    days.push(`
      <button type="button" class="date-pill ${active}" onclick="selectBookingDate('${escapeAttr(dateStr)}')">
        <span class="date-pill-day">${escapeHtml(dow)}</span>
        <span class="date-pill-num">${escapeHtml(dd)}</span>
        <span class="date-pill-month">${escapeHtml(mm)}</span>
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
  updatePriceBreakdown(true);
}

function renderCourtOptions() {
  const container = document.getElementById('courtOptions');
  if (!container) return;

  container.innerHTML = COURTS.map(court => {
    const active = Number(court.id) === Number(selectedCourtId) ? 'active' : '';
    const bookingsCount = BOOKINGS.filter(b =>
      Number(b.courtId) === Number(court.id) &&
      b.date === selectedDate &&
      !['rejected', 'cancelled', 'expired', 'no_show'].includes(b.status)
    ).length;

    const stdRate = (Number(court.basePrice) || Number(CMS_CONFIG.courtPrice) || 120000) + (Number(court.surcharge) || 0);
    const peakRate = stdRate + (Number(CMS_CONFIG.peakSurcharge) ?? 30000);
    const cleanSurface = String(court.surface || 'Acrylic Cushion 8 lớp').replace(/USAPA\s*/gi, '');

    return `
      <div class="court-option ${active}" onclick="selectCourt(${Number(court.id)})">
        <div class="flex-between mb-sm">
          <span class="badge ${court.type === 'Indoor' ? 'badge-primary' : 'badge-neutral'}">${escapeHtml(court.type)}</span>
          <span class="text-xs text-muted">${bookingsCount} ca đã đặt</span>
        </div>
        <div class="court-option-name">${escapeHtml(court.name)}</div>
        <div class="court-option-meta">${escapeHtml(cleanSurface)}</div>
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
  updatePriceBreakdown(true);
}

function renderDurationPills() {
  document.querySelectorAll('#durationPills .duration-pill').forEach(btn => {
    const dur = parseInt(btn.getAttribute('data-dur'));
    btn.classList.toggle('active', dur === selectedDuration);
  });
}

function setDuration(dur) {
  selectedDuration = dur;
  ensureValidSelectedTime();
  renderDurationPills();
  renderTimeline();
  renderTimeSlotGrid();
  updatePriceBreakdown(true);
}

function renderTimeline() {
  const bar = document.getElementById('bookingTimeline');
  const ticks = document.getElementById('bookingTicks');
  if (!bar) return;

  const openMins = timeToMinutes(CMS_CONFIG.openTime || '06:00') || (6 * 60);
  const closeMins = timeToMinutes(CMS_CONFIG.closeTime || '22:00') || (22 * 60);

  const slots = [];
  for (let m = openMins; m < closeMins; m += 30) {
    const t = minutesToTime(m);
    const check = checkCourtConflict(selectedCourtId, selectedDate, t, 30);
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
    }

    const canPick = m + selectedDuration <= closeMins;
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

  const slots = getDynamicTimeSlots(selectedDuration);
  const todayStr = getTodayStr();
  const now = new Date();
  const nowMins = now.getHours() * 60 + now.getMinutes();

  if (slots.length === 0) {
    grid.innerHTML = `<div class="empty-state" style="grid-column:1/-1;padding:1.25rem;">Không có khung giờ phù hợp với thời lượng ${selectedDuration} phút trong giờ mở cửa CLB.</div>`;
    return;
  }

  grid.innerHTML = slots.map(time => {
    const check = checkCourtConflict(selectedCourtId, selectedDate, time, selectedDuration);
    const isPastToday = selectedDate === todayStr && timeToMinutes(time) < (nowMins - 5);
    const isConflict = check.conflict || isPastToday;
    const isSelected = time === selectedStartTime && !isConflict;
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
        ${isConflict ? 'disabled' : ''}
        onclick="selectStartTime('${time}')">
        <span class="slot-btn-time">${time} – ${getEndTime(time, selectedDuration)}</span>
        <span class="slot-btn-price">${escapeHtml(subText)}</span>
      </button>
    `;
  }).join('');

  if (select) {
    select.innerHTML = slots.map(t => {
      const c = checkCourtConflict(selectedCourtId, selectedDate, t, selectedDuration);
      const isPast = selectedDate === todayStr && timeToMinutes(t) < (nowMins - 5);
      const st = isPast ? 'Qua giờ' : (c.conflict ? 'Đã kín' : 'Còn trống');
      return `<option value="${t}" ${t === selectedStartTime ? 'selected' : ''} ${isPast || c.conflict ? 'disabled' : ''}>${t} (${st})</option>`;
    }).join('');
  }
}

function selectStartTime(time) {
  const slots = getDynamicTimeSlots(selectedDuration);
  if (!slots.includes(time)) return;
  selectedStartTime = time;
  renderTimeSlotGrid();
  updatePriceBreakdown(true);
}

async function fetchServerQuote(reqId) {
  try {
    const qs = new URLSearchParams({
      courtId: String(selectedCourtId),
      date: selectedDate,
      time: selectedStartTime,
      duration: String(selectedDuration)
    });
    const res = await fetch(`/api/bookings/quote?${qs.toString()}`, {
      headers: { 'Accept': 'application/json' },
      credentials: 'same-origin',
      cache: 'no-store'
    });
    if (!res.ok) return;
    const data = await res.json();
    if (reqId !== quoteRequestCounter || !data || !data.ok || !data.quote) return;

    latestServerQuote = data.quote;
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
      memberTier: data.quote.memberTier || 'Khách tiêu chuẩn'
    });
  } catch (_) {}
}

function applyQuoteToDom(quote) {
  const court = getCourtById(selectedCourtId);
  const endTime = getEndTime(selectedStartTime, selectedDuration);
  const grandTotal = quote.netCourtFee;
  const players = parseInt(document.getElementById('bookingPlayers')?.value || '4');
  const perPerson = Math.ceil(grandTotal / Math.max(1, players));

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
      feeEl.innerHTML = `<span style="text-decoration:line-through;color:var(--text-muted);margin-right:6px;">${formatMoney(quote.grossCourtFee)}</span><strong style="color:var(--color-success)">${formatMoney(quote.netCourtFee)} (-${Number(quote.discountPercent)}% ${escapeHtml(quote.memberTier)})</strong>`;
    } else {
      feeEl.textContent = formatMoney(quote.netCourtFee);
    }
  }

  const totalEl = document.getElementById('summaryTotal');
  if (totalEl) totalEl.textContent = formatMoney(grandTotal);

  const splitEl = document.getElementById('summarySplitPerPerson');
  if (splitEl) splitEl.textContent = `~${formatMoney(perPerson)} / người (${players} VĐV)`;

  const conflictBanner = document.getElementById('conflictBanner');
  const conflictText = document.getElementById('conflictBannerText');
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
    } else {
      conflictBanner.classList.remove('show');
    }
  }
}

function updatePriceBreakdown(fetchAuthoritative = false) {
  const localQuote = calculateSegmentedPricing(selectedCourtId, selectedStartTime, selectedDuration);
  applyQuoteToDom(localQuote);
  if (fetchAuthoritative) {
    const reqId = ++quoteRequestCounter;
    fetchServerQuote(reqId);
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

  const name = document.getElementById('bookingName')?.value.trim();
  const phone = document.getElementById('bookingPhone')?.value.trim();
  const players = parseInt(document.getElementById('bookingPlayers')?.value || '4');
  const skillLevel = document.getElementById('bookingSkill')?.value || 'Giao lưu (DUPR 3.0)';

  if (!name || name.length < 2) {
    showToast('Thiếu thông tin!', 'Vui lòng nhập Họ và tên (tối thiểu 2 ký tự).', 'error');
    return;
  }

  if (!phone || !/^(0|\+84)[0-9]{8,10}$/.test(phone.replace(/[\s.-]/g, ''))) {
    showToast('Số điện thoại chưa hợp lệ!', 'Vui lòng nhập đúng số điện thoại 10 chữ số Việt Nam.', 'error');
    return;
  }

  const check = checkCourtConflict(selectedCourtId, selectedDate, selectedStartTime, selectedDuration);
  if (check.conflict) {
    showToast('Trùng khung giờ!', check.reason, 'error');
    return;
  }

  const localQuote = calculateSegmentedPricing(selectedCourtId, selectedStartTime, selectedDuration);
  const expectedAmountDue = latestServerQuote ? Number(latestServerQuote.amountDue) : localQuote.netCourtFee;
  const pricingVersion = (latestServerQuote && latestServerQuote.pricingVersion) || PRICING_VERSION || '';
  const idempotencyKey = generateIdempotencyKey('BK');

  isSubmittingBooking = true;
  const btn = document.getElementById('btnSubmitBooking');
  if (btn) btn.disabled = true;

  try {
    const res = await apiFetch('/api/bookings', {
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
        paymentMethod: 'cash',
        pricingVersion,
        expectedAmountDue,
        idempotencyKey
      })
    });

    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      showToast('Không thể đặt sân!', data.error || 'Khung giờ hoặc bảng giá vừa thay đổi, vui lòng kiểm tra lại.', 'error');
      await syncDatabaseFromServer(true);
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

    const court = getCourtById(createdBooking.courtId);
    const qrPlaceholder = document.querySelector('#modalQR .qr-placeholder');
    if (qrPlaceholder) {
      qrPlaceholder.innerHTML = `
        ${renderBookingQrHtml(createdBooking, 168, guestAccessToken)}
        <div class="text-xs font-bold text-mono mt-sm" id="qrCodeBadge" style="color:var(--color-indigo)">${escapeHtml(createdBooking.id)}</div>
      `;
    }

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

    const setEl = (id, val) => {
      const el = document.getElementById(id);
      if (el) el.textContent = val;
    };

    setEl('qrRef', createdBooking.id);
    setEl('qrStatusText', createdBooking.status === 'approved' ? 'Đã duyệt · Sẵn sàng Check-in đúng giờ' : 'Chờ lễ tân duyệt vé');
    setEl('qrName', `${createdBooking.name} (${createdBooking.phone})`);
    setEl('qrDate', formatDateVN(createdBooking.date));
    setEl('qrCourt', court ? court.name : `Sân #${createdBooking.courtId}`);
    setEl('qrTime', `${createdBooking.time} → ${getEndTime(createdBooking.time, createdBooking.duration)} (${createdBooking.duration} phút)`);
    setEl('qrTotal', `${formatMoney(createdBooking.amountDue || createdBooking.courtFee)} (Chưa thu — Thanh toán tại quầy)`);

    openModal('modalQR');
    showToast('Đặt sân thành công!', `Mã vé điện tử của bạn là ${createdBooking.id}`);
  } catch (err) {
    showToast('Lỗi kết nối!', 'Không thể gửi yêu cầu đặt sân tới máy chủ.', 'error');
  } finally {
    isSubmittingBooking = false;
    if (btn) btn.disabled = false;
  }
}

document.addEventListener('DOMContentLoaded', initBookingPage);
window.addEventListener('pickleball:db-synced', () => {
  ensureValidSelectedTime();
  renderPeakBadge();
  renderCourtOptions();
  renderTimeline();
  renderTimeSlotGrid();
  updatePriceBreakdown(true);
});
