/* ==========================================================================
   SPIKE PICKLEBALL CLUB — HOMEPAGE & LIVE ARENA CONTROLLER
   ========================================================================== */

function handleQuickBookSubmit(e) {
  e.preventDefault();
  const dateVal = document.getElementById('qbDate')?.value || getTodayISO();
  const courtVal = document.getElementById('qbCourt')?.value || '1';
  const timeVal = document.getElementById('qbTime')?.value || '17:00';
  window.location.href = `/Booking?date=${encodeURIComponent(dateVal)}&court=${encodeURIComponent(courtVal)}&time=${encodeURIComponent(timeVal)}`;
}

function renderDashboard() {
  reconcileBookingsRealTime(false);
  const todayISO = getTodayISO();
  const nowMins = getNowMinutes();
  const todayBookings = BOOKINGS.filter(b => (b.date || todayISO) === todayISO && b.status !== 'rejected');
  const pendings = BOOKINGS.filter(b => b.status === 'pending');
  const playing = todayBookings.filter(b => {
    if (b.status !== 'playing') return false;
    const timing = getBookingTimingState(b);
    return timing.phase === 'live' || timing.phase === 'checkin_window';
  });
  const completedOrPlaying = BOOKINGS.filter(b => ['playing', 'completed', 'approved'].includes(b.status));
  const totalRevenue = completedOrPlaying.reduce((sum, b) => sum + (Number(b.totalPaid) || Number(b.courtFee) || 0), 0);
  const isAdmin = CURRENT_USER && CURRENT_USER.role === 'admin';

  // Init Quick Booking Form
  const qbDate = document.getElementById('qbDate');
  if (qbDate && !qbDate.value) qbDate.value = todayISO;

  const qbCourt = document.getElementById('qbCourt');
  if (qbCourt && qbCourt.options.length !== COURTS.length) {
    const currentSelected = qbCourt.value;
    qbCourt.innerHTML = COURTS.map(c => `<option value="${c.id}">${c.name} (${c.type === 'Indoor' ? 'Trong nhà' : 'Ngoài trời'})</option>`).join('');
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
  if (titleEl && CMS_CONFIG.heroTitle) titleEl.innerHTML = CMS_CONFIG.heroTitle;

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
      const isLocked = COURT_LOCKS.some(l => Number(l.courtId) === Number(c.id) && l.date === todayISO);
      const dotColor = isLocked ? '#EF4444' : isLive ? '#FF5520' : '#10B981';
      const statusTxt = isLocked ? 'Bảo trì' : isLive ? 'Đang thi đấu' : 'Đang trống';
      return `
        <a href="/Booking?court=${c.id}" class="hero-mini-court-item">
          <div style="display:flex;align-items:center;justify-content:space-between;">
            <strong style="font-size:0.86rem;color:#FFF;">${c.name.replace(' (Arena A)', '').replace(' (Arena B)', '')}</strong>
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
            <span class="stat-label">Doanh thu ghi nhận</span>
            <div class="stat-icon" style="background:var(--color-primary-50);color:var(--color-primary)"><iconify-icon icon="lucide:wallet"></iconify-icon></div>
          </div>
          <div class="stat-value primary text-mono">${formatMoney(totalRevenue)}</div>
          <div class="stat-detail">Tổng tiền sân & dịch vụ POS</div>
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
          <div class="stat-value accent">${COURTS.length - playing.length} / ${COURTS.length} sân</div>
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
          <div class="stat-detail">Giảm chấn bảo vệ cổ chân & khớp gối</div>
        </div>
        <div class="stat-widget">
          <div class="stat-widget-top">
            <span class="stat-label">Sân trống hiện tại</span>
            <div class="stat-icon" style="background:var(--color-success-light);color:var(--color-success)"><iconify-icon icon="lucide:check-circle-2"></iconify-icon></div>
          </div>
          <div class="stat-value accent">${COURTS.length - playing.length} / ${COURTS.length} sân</div>
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
          <div class="stat-value text-mono">${CMS_CONFIG.openTime || '06:00'}–${CMS_CONFIG.closeTime || '22:00'}</div>
          <div class="stat-detail">Hotline: ${CMS_CONFIG.hotline || '0901 234 567'}</div>
        </div>
      `;
    }
  }

  // 3. Architectural Court Cards Grid
  const courtsGridEl = document.getElementById('courtsGrid');
  if (courtsGridEl) {
    courtsGridEl.innerHTML = COURTS.map(court => {
      const activeBooking = playing.find(b => Number(b.courtId) === Number(court.id));
      const nextBooking = todayBookings
        .filter(b => Number(b.courtId) === Number(court.id) && ['approved', 'pending'].includes(b.status) && timeToMinutes(b.time) > nowMins)
        .sort((a, b) => timeToMinutes(a.time) - timeToMinutes(b.time))[0];
      const activeLock = COURT_LOCKS.find(l => Number(l.courtId) === Number(court.id) && l.date === todayISO);

      const isPlaying = !!activeBooking;
      const isLocked = !!activeLock;
      const baseRate = getCourtHourlyRate(court.id, '08:00');

      let badgeHtml = `<span class="match-live-badge available"><span class="match-live-dot"></span> Sẵn sàng đặt lịch</span>`;
      if (isLocked) {
        badgeHtml = `<span class="match-live-badge locked">Đang bảo trì</span>`;
      } else if (isPlaying) {
        badgeHtml = `<span class="match-live-badge"><span class="match-live-dot"></span> Đang thi đấu</span>`;
      }

      let statusDetailHtml = '';
      if (isLocked) {
        const endLock = minutesToTime(timeToMinutes(activeLock.startTime) + Number(activeLock.duration));
        statusDetailHtml = `
          <div class="court-status-callout is-locked">
            <div>
              <strong>Tạm ngưng bảo trì:</strong> ${activeLock.reason} (${activeLock.startTime} – ${endLock})
            </div>
          </div>
        `;
      } else if (isPlaying) {
        const endTime = minutesToTime(timeToMinutes(activeBooking.time) + Number(activeBooking.duration));
        const timing = getBookingTimingState(activeBooking);
        statusDetailHtml = `
          <div class="court-status-callout is-playing">
            <div>
              <div style="font-size:0.75rem;color:var(--text-secondary);">Ca đang chơi (${activeBooking.time} – ${endTime} · còn ${timing.minsRemaining}p)</div>
              <div style="font-weight:700;font-size:0.92rem;color:var(--text-primary);">${activeBooking.name} (${activeBooking.players || 4} người)</div>
            </div>
            <span class="badge badge-accent">${activeBooking.skillLevel || 'Giao lưu'}</span>
          </div>
        `;
      } else {
        statusDetailHtml = `
          <div class="court-status-callout is-free">
            <div>
              <div style="font-size:0.75rem;color:var(--text-secondary);">Tình trạng khung giờ hiện tại</div>
              <div style="font-weight:700;font-size:0.9rem;color:var(--color-success);">
                ${nextBooking ? `Trống hiện tại — Ca kế tiếp lúc ${nextBooking.time}` : 'Trống nhiều khung giờ đẹp hôm nay'}
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
              <h3 style="font-family:var(--font-display);font-size:1.55rem;font-weight:700;text-transform:uppercase;">${court.name}</h3>
              <p style="font-size:0.82rem;color:#E2E8F0;">${court.typeLabel} — Mặt sân ${court.surface}</p>
            </div>
          </div>

          <div class="match-card-body">
            ${statusDetailHtml}

            <div class="match-card-footer">
              <div>
                <span class="text-xs text-muted" style="display:block;">Giá thuê từ</span>
                <strong class="text-mono" style="font-size:1.15rem;color:var(--color-primary);">${formatMoney(baseRate)}<span style="font-size:0.8rem;color:var(--text-secondary);font-weight:500;"> / giờ</span></strong>
              </div>
              <a href="/Booking?court=${court.id}" class="btn btn-primary">
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
        <div style="font-weight:800;color:var(--text-primary)">${court.name}</div>
        <div style="font-size:0.72rem;color:var(--text-muted)">${court.type === 'Indoor' ? 'Indoor VIP' : 'Outdoor'}</div>
      </td>`;

      hours.forEach(timeStr => {
        const startMins = timeToMinutes(timeStr);
        const endMins = startMins + 60;
        const conflict = checkSlotConflict(court.id, todayISO, startMins, endMins);

        if (conflict.hasConflict) {
          if (conflict.type === 'lock') {
            tbody += `<td class="matrix-cell locked-cell" title="Bảo trì: ${conflict.record.reason}">Bảo trì</td>`;
          } else {
            const b = conflict.record;
            const cls = b.status === 'pending' ? 'pending-cell' : 'occupied';
            const label = b.status === 'pending' ? 'Chờ duyệt' : (b.status === 'playing' ? 'Đang chơi' : 'Đã đặt');
            tbody += `<td class="matrix-cell ${cls}" title="${b.name} (${b.time})">${label}</td>`;
          }
        } else {
          tbody += `<td class="matrix-cell free" onclick="window.location.href='/Booking?court=${court.id}&time=${timeStr}'" title="Bấm để đặt ${court.name} lúc ${timeStr}">+ Đặt</td>`;
        }
      });

      tbody += `</tr>`;
    });
    tbody += `</tbody>`;
    matrixEl.innerHTML = thead + tbody;
  }
}

document.addEventListener('DOMContentLoaded', renderDashboard);
