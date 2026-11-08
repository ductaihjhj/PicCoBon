/* ==========================================================================
   SPIKE PICKLEBALL CLUB — HOMEPAGE & LIVE ARENA CONTROLLER
   Real-time Court Status, Accurate Lock Windows, XSS-safe Rendering & KPI Split
   ========================================================================== */

function handleQuickBookSubmit(e) {
  e.preventDefault();
  const dateVal = document.getElementById('qbDate')?.value || getTodayISO();
  const courtVal = document.getElementById('qbCourt')?.value || '1';
  const timeVal = document.getElementById('qbTime')?.value || '17:00';
  window.location.href = `/Booking?date=${encodeURIComponent(dateVal)}&court=${encodeURIComponent(courtVal)}&time=${encodeURIComponent(timeVal)}`;
}

function getCourtCurrentLock(courtId, dateISO, nowMins) {
  return COURT_LOCKS.find(l => {
    if (Number(l.courtId) !== Number(courtId) || l.date !== dateISO) return false;
    const lStart = timeToMinutes(l.startTime);
    const lEnd = lStart + (Number(l.duration) || 60);
    return nowMins >= lStart && nowMins < lEnd;
  });
}

function getCourtUpcomingLock(courtId, dateISO, nowMins) {
  return COURT_LOCKS
    .filter(l => Number(l.courtId) === Number(courtId) && l.date === dateISO && timeToMinutes(l.startTime) > nowMins)
    .sort((a, b) => timeToMinutes(a.startTime) - timeToMinutes(b.startTime))[0];
}

function renderDashboard() {
  reconcileBookingsRealTime(false);
  const todayISO = getTodayISO();
  const nowMins = getNowMinutes();
  const todayBookings = BOOKINGS.filter(b => (b.date || todayISO) === todayISO && !['rejected', 'cancelled'].includes(b.status));
  const pendings = BOOKINGS.filter(b => b.status === 'pending');

  // P1-07: Only bookings with status === 'playing' (checked in) are shown as actively playing
  const playing = todayBookings.filter(b => {
    if (b.status !== 'playing') return false;
    const timing = getBookingTimingState(b);
    return timing.phase === 'live' || timing.phase === 'checkin_window';
  });

  // P1-06: Separate actual collected revenue (paidAmount) from receivables
  const activeBookings = BOOKINGS.filter(b => !['rejected', 'cancelled'].includes(b.status));
  const collectedRevenue = activeBookings.reduce((sum, b) => sum + (Number(b.paidAmount) || 0), 0);
  const totalAmountDue = activeBookings.reduce((sum, b) => sum + (Number(b.amountDue) || Number(b.totalPaid) || Number(b.courtFee) || 0), 0);
  const isAdmin = CURRENT_USER && (CURRENT_USER.role === 'admin' || CURRENT_USER.role === 'staff');

  // Init Quick Booking Form
  const qbDate = document.getElementById('qbDate');
  if (qbDate && !qbDate.value) qbDate.value = todayISO;

  const qbCourt = document.getElementById('qbCourt');
  if (qbCourt && qbCourt.options.length !== COURTS.length) {
    const currentSelected = qbCourt.value;
    qbCourt.innerHTML = COURTS.map(c => `<option value="${Number(c.id)}">${escapeHtml(c.name)} (${c.type === 'Indoor' ? 'Trong nhà' : 'Ngoài trời'})</option>`).join('');
    if (currentSelected && COURTS.some(c => String(c.id) === String(currentSelected))) {
      qbCourt.value = currentSelected;
    }
  }

  // CMS Dynamic Content
  const heroOpenEl = document.getElementById('heroOpenHours');
  if (heroOpenEl) heroOpenEl.textContent = `${CMS_CONFIG.openTime || '06:00'} – ${CMS_CONFIG.closeTime || '22:00'}`;

  const tagEl = document.getElementById('heroTagText');
  if (tagEl && CMS_CONFIG.heroTag) tagEl.textContent = CMS_CONFIG.heroTag;

  const titleEl = document.getElementById('heroMainTitle');
  if (titleEl && CMS_CONFIG.heroTitle) titleEl.textContent = CMS_CONFIG.heroTitle;

  const subEl = document.getElementById('heroSubText');
  if (subEl && (CMS_CONFIG.heroSubtitle || CMS_CONFIG.heroDesc)) {
    subEl.textContent = CMS_CONFIG.heroSubtitle || CMS_CONFIG.heroDesc;
  }

  const clubTitleEl = document.getElementById('promoTitleText');
  if (clubTitleEl && CMS_CONFIG.clubTitle) clubTitleEl.textContent = CMS_CONFIG.clubTitle;

  const clubDescEl = document.getElementById('promoDescText');
  if (clubDescEl && CMS_CONFIG.clubDesc) clubDescEl.textContent = CMS_CONFIG.clubDesc;

  const stdCard = document.getElementById('priceStandardCard');
  if (stdCard) stdCard.innerHTML = `${formatMoney(COURT_PRICE_PER_HOUR)} <span>/ giờ</span>`;

  const peakCard = document.getElementById('pricePeakCard');
  if (peakCard) peakCard.innerHTML = `${formatMoney(COURT_PRICE_PER_HOUR + 30000)} <span>/ giờ</span>`;

  // 1. Hero Mini Court Status Grid
  const heroMiniEl = document.getElementById('heroMiniCourts');
  if (heroMiniEl) {
    heroMiniEl.innerHTML = COURTS.map(c => {
      const isLive = playing.some(b => Number(b.courtId) === Number(c.id));
      const activeLock = getCourtCurrentLock(c.id, todayISO, nowMins);
      const isLockedNow = !!activeLock;
      const dotColor = isLockedNow ? '#EF4444' : isLive ? '#FF5520' : '#10B981';
      const statusTxt = isLockedNow ? 'Bảo trì' : isLive ? 'Đang thi đấu' : 'Đang trống';
      const shortName = String(c.name || '').replace(' (Arena A)', '').replace(' (Arena B)', '');
      return `
        <a href="/Booking?court=${Number(c.id)}" class="hero-mini-court-item">
          <div style="display:flex;align-items:center;justify-content:space-between;">
            <strong style="font-size:0.86rem;color:#FFF;">${escapeHtml(shortName)}</strong>
            <span style="width:8px;height:8px;border-radius:50%;background:${dotColor};"></span>
          </div>
          <span style="font-size:0.74rem;color:#94A3B8;">${c.type === 'Indoor' ? 'Sân Indoor mái vòm' : 'Sân Outdoor'} — <strong style="color:#E2E8F0;">${statusTxt}</strong></span>
        </a>
      `;
    }).join('');
  }

  // 2. Stats / Highlights Strip
  const dashStatsEl = document.getElementById('dashStats');
  if (dashStatsEl) {
    if (isAdmin) {
      dashStatsEl.innerHTML = `
        <div class="stat-widget">
          <div class="stat-widget-top">
            <span class="stat-label">Doanh thu thực thu</span>
            <div class="stat-icon" style="background:var(--color-primary-50);color:var(--color-primary)"><iconify-icon icon="lucide:wallet"></iconify-icon></div>
          </div>
          <div class="stat-value primary text-mono">${formatMoney(collectedRevenue)}</div>
          <div class="stat-detail">Tổng phải thu: ${formatMoney(totalAmountDue)}</div>
        </div>
        <div class="stat-widget">
          <div class="stat-widget-top">
            <span class="stat-label">Lượt đặt hôm nay</span>
            <div class="stat-icon" style="background:var(--color-indigo-50);color:var(--color-indigo)"><iconify-icon icon="lucide:calendar-check-2"></iconify-icon></div>
          </div>
          <div class="stat-value indigo">${todayBookings.length} đơn</div>
          <div class="stat-detail">Đang thi đấu: ${playing.length} sân</div>
        </div>
        <div class="stat-widget">
          <div class="stat-widget-top">
            <span class="stat-label">Yêu cầu chờ duyệt</span>
            <div class="stat-icon" style="background:var(--color-warning-light);color:#B45309"><iconify-icon icon="lucide:bell-ring"></iconify-icon></div>
          </div>
          <div class="stat-value warning">${pendings.length}</div>
          <div class="stat-detail">${pendings.length > 0 ? '<a href="/Management" style="color:var(--color-primary);font-weight:700">Mở trang duyệt đơn</a>' : 'Tất cả đơn đã xử lý'}</div>
        </div>
        <div class="stat-widget">
          <div class="stat-widget-top">
            <span class="stat-label">Sân trống hiện tại</span>
            <div class="stat-icon" style="background:var(--color-success-light);color:var(--color-success)"><iconify-icon icon="lucide:check-circle-2"></iconify-icon></div>
          </div>
          <div class="stat-value accent">${Math.max(0, COURTS.length - playing.length)} / ${COURTS.length} sân</div>
          <div class="stat-detail">Sẵn sàng nhận khách mới</div>
        </div>
      `;
    } else {
      dashStatsEl.innerHTML = `
        <div class="stat-widget">
          <div class="stat-widget-top">
            <span class="stat-label">Chuẩn mặt sân</span>
            <div class="stat-icon" style="background:var(--color-primary-50);color:var(--color-primary)"><iconify-icon icon="lucide:shield-check"></iconify-icon></div>
          </div>
          <div class="stat-value">USAPA 8 Lớp</div>
          <div class="stat-detail">Giảm chấn bảo vệ cổ chân &amp; khớp gối</div>
        </div>
        <div class="stat-widget">
          <div class="stat-widget-top">
            <span class="stat-label">Sân trống hiện tại</span>
            <div class="stat-icon" style="background:var(--color-success-light);color:var(--color-success)"><iconify-icon icon="lucide:check-circle-2"></iconify-icon></div>
          </div>
          <div class="stat-value accent">${Math.max(0, COURTS.length - playing.length)} / ${COURTS.length} sân</div>
          <div class="stat-detail">Xác nhận mã QR tức thì 24/7</div>
        </div>
        <div class="stat-widget">
          <div class="stat-widget-top">
            <span class="stat-label">Đơn giá thuê sân</span>
            <div class="stat-icon" style="background:var(--color-indigo-50);color:var(--color-indigo)"><iconify-icon icon="lucide:tag"></iconify-icon></div>
          </div>
          <div class="stat-value primary text-mono">${formatMoney(COURT_PRICE_PER_HOUR)}<span style="font-size:0.9rem">/h</span></div>
          <div class="stat-detail">Hỗ trợ tự động chia tiền nhóm</div>
        </div>
        <div class="stat-widget">
          <div class="stat-widget-top">
            <span class="stat-label">Giờ hoạt động</span>
            <div class="stat-icon" style="background:var(--color-warning-light);color:#B45309"><iconify-icon icon="lucide:clock"></iconify-icon></div>
          </div>
          <div class="stat-value text-mono">${escapeHtml(CMS_CONFIG.openTime || '06:00')}–${escapeHtml(CMS_CONFIG.closeTime || '22:00')}</div>
          <div class="stat-detail">Hotline: ${escapeHtml(CMS_CONFIG.hotline || '0901 234 567')}</div>
        </div>
      `;
    }
  }

  // 3. Architectural Court Cards Grid
  const courtsGridEl = document.getElementById('courtsGrid');
  if (courtsGridEl) {
    courtsGridEl.innerHTML = COURTS.map(court => {
      const activeBooking = playing.find(b => Number(b.courtId) === Number(court.id));
      const currentSlotApproved = todayBookings.find(b => {
        if (Number(b.courtId) !== Number(court.id) || b.status !== 'approved') return false;
        const s = timeToMinutes(b.time);
        const e = s + (Number(b.duration) || 60);
        return nowMins >= s && nowMins < e;
      });
      const nextBooking = todayBookings
        .filter(b => Number(b.courtId) === Number(court.id) && ['approved', 'pending'].includes(b.status) && timeToMinutes(b.time) > nowMins)
        .sort((a, b) => timeToMinutes(a.time) - timeToMinutes(b.time))[0];

      const activeLock = getCourtCurrentLock(court.id, todayISO, nowMins);
      const upcomingLock = getCourtUpcomingLock(court.id, todayISO, nowMins);

      const isPlaying = !!activeBooking;
      const isLockedNow = !!activeLock;
      const baseRate = getCourtHourlyRate(court.id, '08:00');

      let badgeHtml = `<span class="match-live-badge available"><span class="match-live-dot"></span> Sẵn sàng đặt lịch</span>`;
      if (isLockedNow) {
        badgeHtml = `<span class="match-live-badge locked">Đang bảo trì</span>`;
      } else if (isPlaying) {
        badgeHtml = `<span class="match-live-badge"><span class="match-live-dot"></span> Đang thi đấu</span>`;
      } else if (currentSlotApproved) {
        badgeHtml = `<span class="match-live-badge" style="background:rgba(59,130,246,0.9);color:#FFF;">Đã đặt · Chờ Check-in</span>`;
      }

      let statusDetailHtml = '';
      if (isLockedNow) {
        const endLock = minutesToTime(timeToMinutes(activeLock.startTime) + Number(activeLock.duration));
        statusDetailHtml = `
          <div class="court-status-callout is-locked">
            <div>
              <strong>Tạm ngưng bảo trì:</strong> ${escapeHtml(activeLock.reason)} (${escapeHtml(activeLock.startTime)} – ${escapeHtml(endLock)})
            </div>
          </div>
        `;
      } else if (isPlaying) {
        const endTime = minutesToTime(timeToMinutes(activeBooking.time) + Number(activeBooking.duration));
        const timing = getBookingTimingState(activeBooking);
        statusDetailHtml = `
          <div class="court-status-callout is-playing">
            <div>
              <div style="font-size:0.75rem;color:var(--text-secondary);">Ca đang chơi (${escapeHtml(activeBooking.time)} – ${escapeHtml(endTime)} · còn ${timing.minsRemaining}p)</div>
              <div style="font-weight:700;font-size:0.92rem;color:var(--text-primary);">${escapeHtml(activeBooking.name)} (${Number(activeBooking.players) || 4} người)</div>
            </div>
            <span class="badge badge-accent">${escapeHtml(activeBooking.skillLevel || 'Giao lưu')}</span>
          </div>
        `;
      } else if (currentSlotApproved) {
        const endTime = minutesToTime(timeToMinutes(currentSlotApproved.time) + Number(currentSlotApproved.duration));
        statusDetailHtml = `
          <div class="court-status-callout is-free" style="border-color:rgba(59,130,246,0.3);background:rgba(59,130,246,0.06);">
            <div>
              <div style="font-size:0.75rem;color:var(--text-secondary);">Khung giờ hiện tại (${escapeHtml(currentSlotApproved.time)} – ${escapeHtml(endTime)})</div>
              <div style="font-weight:700;font-size:0.9rem;color:#2563EB;">
                Đã giữ chỗ (${escapeHtml(currentSlotApproved.name)}) — Đang chờ khách Check-in
              </div>
            </div>
            <span class="badge badge-primary">Chờ Check-in</span>
          </div>
        `;
      } else {
        const lockHint = upcomingLock ? ` · Bảo trì lúc ${escapeHtml(upcomingLock.startTime)}` : '';
        statusDetailHtml = `
          <div class="court-status-callout is-free">
            <div>
              <div style="font-size:0.75rem;color:var(--text-secondary);">Tình trạng khung giờ hiện tại</div>
              <div style="font-weight:700;font-size:0.9rem;color:var(--color-success);">
                ${nextBooking ? `Trống hiện tại — Ca kế tiếp lúc ${escapeHtml(nextBooking.time)}${lockHint}` : `Trống nhiều khung giờ đẹp hôm nay${lockHint}`}
              </div>
            </div>
            <span class="badge badge-success">Sẵn sàng</span>
          </div>
        `;
      }

      return `
        <div class="match-card">
          <div class="match-card-media">
            <div class="match-card-top">
              <span class="badge" style="background:rgba(11,17,30,0.78);color:#FFF;backdrop-filter:blur(6px);">
                ${court.type === 'Indoor' ? 'Sân Trong Nhà VIP' : 'Sân Ngoài Trời'}
              </span>
              ${badgeHtml}
            </div>
            <div>
              <h3 style="font-family:var(--font-display);font-size:1.55rem;font-weight:700;text-transform:uppercase;">${escapeHtml(court.name)}</h3>
              <p style="font-size:0.82rem;color:#E2E8F0;">${escapeHtml(court.typeLabel)} — Mặt sân ${escapeHtml(court.surface)}</p>
            </div>
          </div>

          <div class="match-card-body">
            ${statusDetailHtml}

            <div class="match-card-footer">
              <div>
                <span class="text-xs text-muted" style="display:block;">Giá thuê từ</span>
                <strong class="text-mono" style="font-size:1.15rem;color:var(--color-primary);">${formatMoney(baseRate)}<span style="font-size:0.8rem;color:var(--text-secondary);font-weight:500;"> / giờ</span></strong>
              </div>
              <a href="/Booking?court=${Number(court.id)}" class="btn btn-primary">
                <span>Đặt sân này</span>
              </a>
            </div>
          </div>
        </div>
      `;
    }).join('');
  }

  // 4. Quick Availability Matrix
  const matrixEl = document.getElementById('dashMatrixTable');
  if (matrixEl) {
    const hours = [];
    for (let h = 6; h <= 21; h++) {
      hours.push(`${String(h).padStart(2, '0')}:00`);
    }

    let thead = `<thead><tr><th class="matrix-court-col">Sân thi đấu</th>`;
    hours.forEach(t => { thead += `<th>${t}</th>`; });
    thead += `</tr></thead>`;

    let tbody = `<tbody>`;
    COURTS.forEach(court => {
      tbody += `<tr><td class="matrix-court-col">
        <div style="font-weight:800;color:var(--text-primary)">${escapeHtml(court.name)}</div>
        <div style="font-size:0.72rem;color:var(--text-muted)">${court.type === 'Indoor' ? 'Indoor VIP' : 'Outdoor'}</div>
      </td>`;

      hours.forEach(timeStr => {
        const startMins = timeToMinutes(timeStr);
        const endMins = startMins + 60;
        const conflict = checkSlotConflict(court.id, todayISO, startMins, endMins);

        if (conflict.hasConflict) {
          if (conflict.type === 'lock') {
            tbody += `<td class="matrix-cell locked-cell" title="Bảo trì: ${escapeHtml(conflict.record.reason)}">Bảo trì</td>`;
          } else {
            const b = conflict.record;
            const cls = b.status === 'pending' ? 'pending-cell' : 'occupied';
            const label = b.status === 'pending' ? 'Chờ duyệt' : (b.status === 'playing' ? 'Đang chơi' : 'Đã đặt');
            tbody += `<td class="matrix-cell ${cls}" title="${escapeHtml(b.name)} (${escapeHtml(b.time)})">${label}</td>`;
          }
        } else {
          tbody += `<td class="matrix-cell free" onclick="window.location.href='/Booking?court=${Number(court.id)}&time=${encodeURIComponent(timeStr)}'" title="Bấm để đặt ${escapeHtml(court.name)} lúc ${timeStr}">+ Đặt</td>`;
        }
      });

      tbody += `</tr>`;
    });
    tbody += `</tbody>`;
    matrixEl.innerHTML = thead + tbody;
  }
}

document.addEventListener('DOMContentLoaded', renderDashboard);
