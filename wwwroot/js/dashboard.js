/* ==========================================================================
   SPIKE PICKLEBALL CLUB — HOMEPAGE & LIVE ARENA CONTROLLER (V5)
   Strict Server Truth (UI-A01), Daily Ledger KPI (UI-A03), Dynamic CMS (UI-B03),
   Keyboard Accessible Matrix (UI-C02), Semantic State Banners (UI-C03)
   ========================================================================== */

function handleQuickBookSubmit(e) {
  e.preventDefault();
  if (!HAS_SERVER_STATE || COURTS.length === 0) {
    showToast('Chưa đồng bộ trạng thái sân', 'Vui lòng chờ hệ thống kết nối máy chủ trước khi đặt sân.', 'warning');
    return;
  }
  const dateVal = document.getElementById('qbDate')?.value || getTodayISO();
  const courtVal = document.getElementById('qbCourt')?.value || String(COURTS[0].id);
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
  const todayISO = getTodayISO();
  const nowMins = getNowMinutes();

  const qbDate = document.getElementById('qbDate');
  if (qbDate) {
    qbDate.min = todayISO;
    qbDate.max = getOffsetDateISO(IS_STAFF_OR_ADMIN ? 60 : 30);
    if (!qbDate.value || qbDate.value < todayISO) qbDate.value = todayISO;
  }

  // CMS Dynamic Content (P2-05, UI-B03)
  const openStr = CMS_CONFIG.openTime || '06:00';
  const closeStr = CMS_CONFIG.closeTime || '22:00';
  const peakStartStr = CMS_CONFIG.peakStart || '17:00';
  const peakExtra = Number(CMS_CONFIG.peakSurcharge) ?? 30000;

  const heroOpenEl = document.getElementById('heroOpenHours');
  if (heroOpenEl) heroOpenEl.textContent = `${openStr} – ${closeStr}`;

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

  const stdTag = document.getElementById('pricingStdTag');
  if (stdTag) stdTag.textContent = `Khung Giờ Tiêu Chuẩn (${openStr} – ${peakStartStr})`;

  const peakTag = document.getElementById('pricingPeakTag');
  if (peakTag) peakTag.textContent = `Khung Giờ Vàng (${peakStartStr} – ${closeStr})`;

  const stdCard = document.getElementById('priceStandardCard');
  if (stdCard) stdCard.innerHTML = `${formatMoney(COURT_PRICE_PER_HOUR)} <span>/ giờ</span>`;

  const peakCard = document.getElementById('pricePeakCard');
  if (peakCard) peakCard.innerHTML = `${formatMoney(COURT_PRICE_PER_HOUR + peakExtra)} <span>/ giờ</span>`;

  const heroMiniEl = document.getElementById('heroMiniCourts');
  const dashStatsEl = document.getElementById('dashStats');
  const courtsGridEl = document.getElementById('courtsGrid');
  const matrixEl = document.getElementById('dashMatrixTable');
  const qbCourt = document.getElementById('qbCourt');

  // UI-A01: Never display fake "4 / 4 sân trống" when server state has not been verified!
  if (!HAS_SERVER_STATE) {
    const syncState = (SPIKE_SYNC_STATE === 'offline' || SPIKE_SYNC_STATE === 'error') ? SPIKE_SYNC_STATE : 'loading';
    if (qbCourt) {
      qbCourt.innerHTML = '<option value="">Đang đồng bộ danh sách sân...</option>';
      qbCourt.disabled = true;
    }
    if (heroMiniEl) {
      heroMiniEl.innerHTML = `<div style="grid-column:1/-1;">${renderUiStateBanner(syncState, { compact: true })}</div>`;
    }
    if (dashStatsEl) {
      dashStatsEl.innerHTML = `
        <div class="stat-widget">
          <div class="stat-widget-top">
            <span class="stat-label">Cấu trúc mặt sân</span>
            <div class="stat-icon" style="background:var(--color-primary-50);color:var(--color-primary)"><iconify-icon icon="lucide:shield-check"></iconify-icon></div>
          </div>
          <div class="stat-value">Cushion 8 Lớp</div>
          <div class="stat-detail">Giảm chấn bảo vệ cổ chân &amp; khớp gối</div>
        </div>
        <div class="stat-widget" data-testid="kpi-available-courts">
          <div class="stat-widget-top">
            <span class="stat-label">Sân trống hiện tại</span>
            <div class="stat-icon" style="background:var(--color-indigo-50);color:var(--color-indigo)"><iconify-icon icon="lucide:loader-2"></iconify-icon></div>
          </div>
          <div class="stat-value text-mono">-- / -- sân</div>
          <div class="stat-detail">${syncState === 'loading' ? 'Đang kiểm tra trạng thái thực tế...' : 'Chưa thể kết nối máy chủ'}</div>
        </div>
        <div class="stat-widget">
          <div class="stat-widget-top">
            <span class="stat-label">Đơn giá thuê sân</span>
            <div class="stat-icon" style="background:var(--color-indigo-50);color:var(--color-indigo)"><iconify-icon icon="lucide:tag"></iconify-icon></div>
          </div>
          <div class="stat-value primary text-mono">${formatMoney(COURT_PRICE_PER_HOUR)}<span style="font-size:0.9rem">/h</span></div>
          <div class="stat-detail">Giờ vàng từ ${escapeHtml(peakStartStr)}: ${formatMoney(COURT_PRICE_PER_HOUR + peakExtra)}/h</div>
        </div>
        <div class="stat-widget">
          <div class="stat-widget-top">
            <span class="stat-label">Giờ hoạt động</span>
            <div class="stat-icon" style="background:var(--color-warning-light);color:#B45309"><iconify-icon icon="lucide:clock"></iconify-icon></div>
          </div>
          <div class="stat-value text-mono">${escapeHtml(openStr)}–${escapeHtml(closeStr)}</div>
          <div class="stat-detail">Hotline: ${escapeHtml(CMS_CONFIG.hotline || '0901 234 567')}</div>
        </div>
      `;
    }
    if (courtsGridEl && (syncState === 'offline' || syncState === 'error')) {
      courtsGridEl.innerHTML = `<div style="grid-column:1/-1;">${renderUiStateBanner(syncState)}</div>`;
    }
    if (matrixEl) {
      matrixEl.innerHTML = `<tbody><tr><td style="padding:1.25rem;">${renderUiStateBanner(syncState, { compact: true })}</td></tr></tbody>`;
    }
    return;
  }

  reconcileBookingsRealTime();

  const todayBookings = BOOKINGS.filter(b =>
    (b.date || todayISO) === todayISO &&
    !['rejected', 'cancelled', 'expired', 'no_show'].includes(b.status) &&
    (b.status !== 'pending' || isPendingBookingHoldActive(b))
  );
  const pendings = BOOKINGS.filter(b => b.status === 'pending' && isPendingBookingHoldActive(b));

  const occupiedNow = todayBookings.filter(b => {
    const s = timeToMinutes(b.time);
    const e = s + (Number(b.duration) || 60);
    if (nowMins < s || nowMins >= e) return false;
    return b.status === 'playing' || b.status === 'occupied';
  });

  const isAdmin = CURRENT_USER && (CURRENT_USER.role === 'admin' || CURRENT_USER.role === 'staff');

  if (qbCourt) {
    if (COURTS.length === 0) {
      qbCourt.innerHTML = '<option value="">Chưa có sân khả dụng</option>';
      qbCourt.disabled = true;
    } else {
      qbCourt.disabled = false;
      const currentSelected = qbCourt.value;
      qbCourt.innerHTML = COURTS.map(c => `<option value="${Number(c.id)}">${escapeHtml(c.name)} (${c.type === 'Indoor' ? 'Trong nhà' : 'Ngoài trời'})</option>`).join('');
      if (currentSelected && COURTS.some(c => String(c.id) === String(currentSelected))) {
        qbCourt.value = currentSelected;
      }
    }
  }

  const liveCountTag = document.getElementById('heroLiveCountTag');
  if (liveCountTag) {
    liveCountTag.textContent = `Trạng thái ${COURTS.length} sân trực tiếp`;
  }

  // 1. Hero Mini Court Status Grid
  if (heroMiniEl) {
    if (COURTS.length === 0) {
      heroMiniEl.innerHTML = `<div style="grid-column:1/-1;">${renderUiStateBanner('empty', { compact: true, title: 'Chưa có sân hoạt động' })}</div>`;
    } else {
      heroMiniEl.innerHTML = COURTS.map(c => {
        const busySlot = todayBookings.find(b => {
          if (Number(b.courtId) !== Number(c.id)) return false;
          const s = timeToMinutes(b.time);
          const e = s + (Number(b.duration) || 60);
          return nowMins >= s && nowMins < e;
        });
        const activeLock = getCourtCurrentLock(c.id, todayISO, nowMins);
        const isLockedNow = !!activeLock;
        const isPlayingNow = busySlot && busySlot.status === 'playing';
        const isReservedNow = busySlot && !isPlayingNow;

        const dotColor = isLockedNow ? '#EF4444' : isPlayingNow ? '#FF5520' : isReservedNow ? '#3B82F6' : '#10B981';
        const statusTxt = isLockedNow ? 'Bảo trì' : isPlayingNow ? 'Đang thi đấu' : isReservedNow ? 'Đã kín giờ này' : 'Đang trống';
        const shortName = String(c.name || '').replace(' (Arena A)', '').replace(' (Arena B)', '');
        return `
          <a href="/Booking?court=${Number(c.id)}" class="hero-mini-court-item">
            <div style="display:flex;align-items:center;justify-content:space-between;gap:0.35rem;">
              <strong style="font-size:0.86rem;color:#FFF;">${escapeHtml(shortName)}</strong>
              <span style="width:8px;height:8px;border-radius:50%;background:${dotColor};flex-shrink:0;"></span>
            </div>
            <span style="font-size:0.74rem;color:#CBD5E1;">${c.type === 'Indoor' ? 'Sân Indoor mái vòm' : 'Sân Outdoor'} — <strong style="color:#FFFFFF;">${statusTxt}</strong></span>
          </a>
        `;
      }).join('');
    }
  }

  // 2. Stats / Highlights Strip (UI-A03: Ledger-driven daily KPI for Staff/Admin)
  const busyCourtsNowCount = COURTS.filter(c => {
    if (getCourtCurrentLock(c.id, todayISO, nowMins)) return true;
    return todayBookings.some(b => {
      if (Number(b.courtId) !== Number(c.id)) return false;
      const s = timeToMinutes(b.time);
      const e = s + (Number(b.duration) || 60);
      return nowMins >= s && nowMins < e;
    });
  }).length;
  const freeCourtsNowCount = Math.max(0, COURTS.length - busyCourtsNowCount);

  if (dashStatsEl) {
    if (isAdmin) {
      const ledger = TODAY_LEDGER_SUMMARY || {};
      const netCollectedToday = Number(ledger.netCollectedToday || 0);
      const receivableToday = Number(ledger.todayReceivableTotal || 0);
      const unpaidToday = Number(ledger.todayUnpaidReceivable || 0);
      const refundedToday = Number(ledger.refundedToday || 0);
      const ledgerTooltip = `Sổ cái ngày ${formatDateVN(todayISO)} (Asia/Ho_Chi_Minh): Thực thu ròng = ${formatMoney(netCollectedToday)} | Tổng phải thu hôm nay = ${formatMoney(receivableToday)} | Công nợ chưa thu = ${formatMoney(unpaidToday)} | Hoàn tiền = ${formatMoney(refundedToday)}`;

      dashStatsEl.innerHTML = `
        <div class="stat-widget" data-testid="kpi-today-ledger" title="${escapeAttr(ledgerTooltip)}">
          <div class="stat-widget-top">
            <span class="stat-label">Thực thu trong ngày (Sổ cái)</span>
            <div class="stat-icon" style="background:var(--color-primary-50);color:var(--color-primary)"><iconify-icon icon="lucide:wallet"></iconify-icon></div>
          </div>
          <div class="stat-value primary text-mono" id="kpiCollectedRevenue">${formatMoney(netCollectedToday)}</div>
          <div class="stat-detail">Phải thu hôm nay: ${formatMoney(receivableToday)} · Công nợ: ${formatMoney(unpaidToday)}</div>
        </div>
        <div class="stat-widget" data-testid="kpi-today-bookings">
          <div class="stat-widget-top">
            <span class="stat-label">Lượt đặt hôm nay</span>
            <div class="stat-icon" style="background:var(--color-indigo-50);color:var(--color-indigo)"><iconify-icon icon="lucide:calendar-check-2"></iconify-icon></div>
          </div>
          <div class="stat-value indigo">${todayBookings.length} ca</div>
          <div class="stat-detail">Đang bận giờ này: ${occupiedNow.length} sân</div>
        </div>
        <div class="stat-widget" data-testid="kpi-pending-bookings">
          <div class="stat-widget-top">
            <span class="stat-label">Yêu cầu chờ duyệt</span>
            <div class="stat-icon" style="background:var(--color-warning-light);color:#B45309"><iconify-icon icon="lucide:bell-ring"></iconify-icon></div>
          </div>
          <div class="stat-value warning">${pendings.length}</div>
          <div class="stat-detail">${pendings.length > 0 ? '<a href="/Admin/Home/Management" style="color:var(--color-primary);font-weight:700">Mở trang duyệt đơn</a>' : 'Tất cả đơn đã xử lý'}</div>
        </div>
        <div class="stat-widget" data-testid="kpi-available-courts">
          <div class="stat-widget-top">
            <span class="stat-label">Sân trống hiện tại</span>
            <div class="stat-icon" style="background:var(--color-success-light);color:var(--color-success)"><iconify-icon icon="lucide:check-circle-2"></iconify-icon></div>
          </div>
          <div class="stat-value accent">${freeCourtsNowCount} / ${COURTS.length} sân</div>
          <div class="stat-detail">Sẵn sàng nhận khách mới</div>
        </div>
      `;
    } else {
      dashStatsEl.innerHTML = `
        <div class="stat-widget">
          <div class="stat-widget-top">
            <span class="stat-label">Cấu trúc mặt sân</span>
            <div class="stat-icon" style="background:var(--color-primary-50);color:var(--color-primary)"><iconify-icon icon="lucide:shield-check"></iconify-icon></div>
          </div>
          <div class="stat-value">Cushion 8 Lớp</div>
          <div class="stat-detail">Giảm chấn bảo vệ cổ chân &amp; khớp gối</div>
        </div>
        <div class="stat-widget" data-testid="kpi-available-courts">
          <div class="stat-widget-top">
            <span class="stat-label">Sân trống hiện tại</span>
            <div class="stat-icon" style="background:var(--color-success-light);color:var(--color-success)"><iconify-icon icon="lucide:check-circle-2"></iconify-icon></div>
          </div>
          <div class="stat-value accent">${freeCourtsNowCount} / ${COURTS.length} sân</div>
          <div class="stat-detail">Cấp mã vé QR định chuẩn 24/7</div>
        </div>
        <div class="stat-widget">
          <div class="stat-widget-top">
            <span class="stat-label">Đơn giá thuê sân</span>
            <div class="stat-icon" style="background:var(--color-indigo-50);color:var(--color-indigo)"><iconify-icon icon="lucide:tag"></iconify-icon></div>
          </div>
          <div class="stat-value primary text-mono">${formatMoney(COURT_PRICE_PER_HOUR)}<span style="font-size:0.9rem">/h</span></div>
          <div class="stat-detail">Giờ vàng từ ${escapeHtml(peakStartStr)}: ${formatMoney(COURT_PRICE_PER_HOUR + peakExtra)}/h</div>
        </div>
        <div class="stat-widget">
          <div class="stat-widget-top">
            <span class="stat-label">Giờ hoạt động</span>
            <div class="stat-icon" style="background:var(--color-warning-light);color:#B45309"><iconify-icon icon="lucide:clock"></iconify-icon></div>
          </div>
          <div class="stat-value text-mono">${escapeHtml(openStr)}–${escapeHtml(closeStr)}</div>
          <div class="stat-detail">Hotline: ${escapeHtml(CMS_CONFIG.hotline || '0901 234 567')}</div>
        </div>
      `;
    }
  }

  // 3. Architectural Court Cards Grid (Zero-PII safe for anonymous users, P0-06)
  if (courtsGridEl) {
    if (COURTS.length === 0) {
      courtsGridEl.innerHTML = `<div style="grid-column:1/-1;">${renderUiStateBanner('empty', {
        title: 'Hiện chưa có sân thi đấu nào đang mở',
        message: 'Danh sách sân thi đấu đang được cập nhật trên cơ sở dữ liệu.'
      })}</div>`;
    } else {
      courtsGridEl.innerHTML = COURTS.map(court => {
        const currentBusySlot = todayBookings.find(b => {
          if (Number(b.courtId) !== Number(court.id)) return false;
          const s = timeToMinutes(b.time);
          const e = s + (Number(b.duration) || 60);
          return nowMins >= s && nowMins < e;
        });
        const nextBooking = todayBookings
          .filter(b => Number(b.courtId) === Number(court.id) && timeToMinutes(b.time) > nowMins)
          .sort((a, b) => timeToMinutes(a.time) - timeToMinutes(b.time))[0];

        const activeLock = getCourtCurrentLock(court.id, todayISO, nowMins);
        const upcomingLock = getCourtUpcomingLock(court.id, todayISO, nowMins);

        const isLockedNow = !!activeLock;
        const isPlaying = currentBusySlot && currentBusySlot.status === 'playing';
        const isOccupiedAnon = currentBusySlot && currentBusySlot.status === 'occupied';
        const isApprovedWaiting = currentBusySlot && (currentBusySlot.status === 'approved' || currentBusySlot.status === 'pending');
        const baseRate = getCourtHourlyRate(court.id, openStr);

        let badgeHtml = `<span class="match-live-badge available"><span class="match-live-dot"></span> Sẵn sàng đặt lịch</span>`;
        if (isLockedNow) {
          badgeHtml = `<span class="match-live-badge locked">Đang bảo trì</span>`;
        } else if (isPlaying) {
          badgeHtml = `<span class="match-live-badge"><span class="match-live-dot"></span> Đang thi đấu</span>`;
        } else if (isOccupiedAnon || isApprovedWaiting) {
          badgeHtml = `<span class="match-live-badge" style="background:rgba(59,130,246,0.9);color:#FFF;">Khung giờ đã đặt</span>`;
        }

        let statusDetailHtml = '';
        if (isLockedNow) {
          const endLock = minutesToTime(timeToMinutes(activeLock.startTime) + Number(activeLock.duration));
          statusDetailHtml = `
            <div class="court-status-callout is-locked">
              <div>
                <strong>Tạm ngưng bảo trì:</strong> ${escapeHtml(activeLock.reason || 'Bảo trì sân')} (${escapeHtml(activeLock.startTime)} – ${escapeHtml(endLock)})
              </div>
            </div>
          `;
        } else if (isPlaying && isAdmin) {
          const endTime = minutesToTime(timeToMinutes(currentBusySlot.time) + Number(currentBusySlot.duration));
          const timing = getBookingTimingState(currentBusySlot);
          statusDetailHtml = `
            <div class="court-status-callout is-playing">
              <div>
                <div style="font-size:0.75rem;color:var(--text-secondary);">Ca đang chơi (${escapeHtml(currentBusySlot.time)} – ${escapeHtml(endTime)} · còn ${timing.minsRemaining}p)</div>
                <div style="font-weight:700;font-size:0.92rem;color:var(--text-primary);">${escapeHtml(currentBusySlot.name || 'Khách đặt sân')}</div>
              </div>
              <span class="badge badge-accent">Đang thi đấu</span>
            </div>
          `;
        } else if (currentBusySlot) {
          const endTime = minutesToTime(timeToMinutes(currentBusySlot.time) + Number(currentBusySlot.duration));
          const ownerNote = isAdmin && currentBusySlot.name ? ` (${escapeHtml(currentBusySlot.name)})` : '';
          statusDetailHtml = `
            <div class="court-status-callout is-free" style="border-color:rgba(59,130,246,0.3);background:rgba(59,130,246,0.06);">
              <div>
                <div style="font-size:0.75rem;color:var(--text-secondary);">Khung giờ hiện tại (${escapeHtml(currentBusySlot.time)} – ${escapeHtml(endTime)})</div>
                <div style="font-weight:700;font-size:0.9rem;color:#2563EB;">
                  Đã có lịch đặt${ownerNote} — Chọn khung giờ trống kế tiếp
                </div>
              </div>
              <span class="badge badge-primary">Đã giữ chỗ</span>
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

        const cleanSurface = String(court.surface || 'Acrylic Cushion 8 lớp').replace(/USAPA\s*/gi, '');

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
                <p style="font-size:0.82rem;color:#E2E8F0;">${escapeHtml(court.typeLabel)} — Mặt sân ${escapeHtml(cleanSurface)}</p>
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
  }

  // 4. Quick Availability Matrix (Dynamic Open -> Close hours, P1-09, P2-05, UI-C02)
  if (matrixEl) {
    if (COURTS.length === 0) {
      matrixEl.innerHTML = `<tbody><tr><td style="padding:1.25rem;">${renderUiStateBanner('empty', { compact: true, title: 'Chưa có lịch sân để hiển thị' })}</td></tr></tbody>`;
      return;
    }

    const openHour = Math.max(0, Math.floor((timeToMinutes(openStr) || 360) / 60));
    const closeHour = Math.min(24, Math.ceil((timeToMinutes(closeStr) || 1320) / 60));
    const hours = [];
    for (let h = openHour; h < closeHour; h++) {
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
        const isPast = startMins < (nowMins - 10);
        const conflict = checkSlotConflict(court.id, todayISO, startMins, endMins);

        if (conflict.hasConflict) {
          if (conflict.type === 'lock') {
            tbody += `<td class="matrix-cell locked-cell" title="Bảo trì: ${escapeHtml(conflict.record.reason || 'Bảo trì')}">Bảo trì</td>`;
          } else {
            const b = conflict.record;
            const cls = b.status === 'pending' ? 'pending-cell' : 'occupied';
            const label = b.status === 'pending' ? 'Chờ duyệt' : (b.status === 'playing' ? 'Đang chơi' : 'Đã đặt');
            const tip = isAdmin && b.name ? `${b.name} (${b.time})` : `Khung giờ đã kín (${b.time})`;
            tbody += `<td class="matrix-cell ${cls}" title="${escapeAttr(tip)}">${label}</td>`;
          }
        } else if (isPast) {
          tbody += `<td class="matrix-cell" style="color:var(--text-muted);background:#F8FAFC;font-size:0.72rem;" title="Đã qua giờ">Qua giờ</td>`;
        } else {
          const targetUrl = `/Booking?court=${Number(court.id)}&date=${encodeURIComponent(todayISO)}&time=${encodeURIComponent(timeStr)}`;
          tbody += `<td class="matrix-cell free" onclick="window.location.href='${escapeAttr(targetUrl)}'">
            <button type="button" class="matrix-slot-btn" onclick="event.stopPropagation();window.location.href='${escapeAttr(targetUrl)}'" aria-label="Đặt ${escapeAttr(court.name)} lúc ${timeStr}" title="Bấm để đặt ${escapeAttr(court.name)} lúc ${timeStr}">+ Đặt</button>
          </td>`;
        }
      });

      tbody += `</tr>`;
    });
    tbody += `</tbody>`;
    matrixEl.innerHTML = thead + tbody;
  }
}

document.addEventListener('DOMContentLoaded', renderDashboard);
window.addEventListener('pickleball:db-synced', renderDashboard);
window.addEventListener('pickleball:sync-status', () => {
  if (!HAS_SERVER_STATE) {
    renderDashboard();
  }
});
