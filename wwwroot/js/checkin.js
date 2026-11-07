/* ==========================================================================
   PICKLEBALL PRO ARENA — CHECK-IN TERMINAL CONTROLLER
   Manual Code / Phone Lookup, QR File Upload & 1-Click Check-in List
   ========================================================================== */

function initCheckin() {
  if (!requireRole('admin')) return;
  renderReadyCheckinList();
}

function renderCheckin() {
  renderReadyCheckinList();
}

function renderReadyCheckinList() {
  const listEl = document.getElementById('readyCheckinList');
  const countEl = document.getElementById('readyCheckinCount');
  if (!listEl) return;

  const readyList = BOOKINGS.filter(b => ['approved', 'pending', 'playing'].includes(b.status));
  if (countEl) countEl.textContent = `${readyList.length} vé`;

  if (readyList.length === 0) {
    listEl.innerHTML = `
      <div class="empty-state">
        <div class="empty-state-icon"><iconify-icon icon="lucide:ticket" width="28"></iconify-icon></div>
        <div class="empty-state-title">Chưa có vé nào chờ nhận sân</div>
      </div>`;
    return;
  }

  listEl.innerHTML = readyList.map(b => {
    const court = getCourtById(b.courtId);
    const endTime = minutesToTime(timeToMinutes(b.time) + Number(b.duration));
    const timing = getBookingTimingState(b);
    const isPlaying = b.status === 'playing' && timing.phase === 'live';
    const displayStatus = isPlaying ? 'playing' : (b.status === 'playing' ? 'approved' : b.status);

    let actionBtnHtml = '';
    if (isPlaying) {
      actionBtnHtml = `<span class="badge badge-success"><iconify-icon icon="lucide:check-circle-2" width="13"></iconify-icon> Đã vào sân (còn ${timing.minsRemaining}p)</span>`;
    } else if (timing.canCheckIn) {
      actionBtnHtml = `<button class="btn btn-primary btn-sm" onclick="performCheckInById('${b.id}')"><iconify-icon icon="lucide:log-in" width="14"></iconify-icon> Check-in ngay</button>`;
    } else {
      actionBtnHtml = `<button class="btn btn-outline btn-sm" onclick="performCheckInById('${b.id}')" title="Chưa tới giờ nhận sân"><iconify-icon icon="lucide:clock" width="14"></iconify-icon> Chờ tới ${b.time}</button>`;
    }

    return `
      <div class="item-row" ${isPlaying ? 'style="background:var(--color-primary-50)"' : ''}>
        <div class="item-avatar" style="background:${isPlaying ? 'var(--color-primary)' : 'var(--color-indigo-50)'};color:${isPlaying ? '#FFF' : 'var(--color-indigo)'}">S${court.id}</div>
        <div class="item-info">
          <div class="item-name">
            <span>${b.name}</span>
            <span class="badge badge-neutral text-mono">${b.id}</span>
            <span class="badge ${statusBadgeClass(displayStatus)}">${statusLabel(displayStatus, b)}</span>
          </div>
          <div class="item-detail">
            <strong>${court.name}</strong> · <span class="text-mono font-bold">${b.time} → ${endTime}</span> · ${formatDateVN(b.date)} · SĐT: <span class="text-mono">${b.phone}</span>
          </div>
        </div>
        <div class="item-actions">
          ${actionBtnHtml}
        </div>
      </div>
    `;
  }).join('');
}

function verifyManualCode() {
  const query = (document.getElementById('manualCodeInput')?.value || '').trim().toLowerCase();
  if (!query) {
    showToast('Nhập mã vé', 'Vui lòng nhập mã đặt sân (VD: PB-...) hoặc số điện thoại khách hàng.', 'warning');
    return;
  }

  const found = BOOKINGS.find(b =>
    (b.id && b.id.toLowerCase() === query) ||
    (b.phone && b.phone.toLowerCase() === query)
  );

  if (found && ['approved', 'pending', 'playing'].includes(found.status)) {
    performCheckInById(found.id);
  } else {
    showCheckInResult(query.toUpperCase(), false, found ? `Vé này đang ở trạng thái: ${statusLabel(found.status, found)}` : 'Không tìm thấy mã vé hoặc số điện thoại này trong hệ thống!');
  }
}

function performCheckInById(id) {
  const b = BOOKINGS.find(x => x.id === id);
  if (!b) return;
  const court = getCourtById(b.courtId);
  const timing = getBookingTimingState(b);

  if (!timing.canCheckIn) {
    showCheckInResult(
      b.id,
      false,
      `Chưa tới giờ nhận sân! Vé ${b.id} của khách ${b.name} có lịch thi đấu lúc ${b.time} (${formatDateVN(b.date)}). Hệ thống chỉ mở Check-in trước 15 phút.`
    );
    return;
  }

  b.status = 'playing';
  saveData();
  showCheckInResult(b.id, true, `Hợp lệ! Khách: ${b.name} (${b.phone}) — Nhận ${court.name} (${b.time})`);
  renderReadyCheckinList();
}

function mockCheckIn() {
  const candidate = BOOKINGS.find(b => b.status === 'approved' || b.status === 'pending') || BOOKINGS.find(b => b.status === 'playing');
  if (candidate) {
    performCheckInById(candidate.id);
  } else {
    showCheckInResult('KHÔNG XÁC ĐỊNH', false, 'Hiện không có đơn đặt sân nào ở trạng thái chờ Check-in.');
  }
}

function showCheckInResult(refCode, isSuccess, messageText) {
  document.getElementById('checkinUpload').style.display = 'none';
  document.getElementById('checkinResult').classList.add('show');
  document.getElementById('checkinRef').textContent = refCode;

  if (isSuccess) {
    document.getElementById('checkinSuccessText').textContent = messageText;
    document.getElementById('checkinSuccess').style.display = 'block';
    document.getElementById('checkinError').style.display = 'none';
    showToast('Check-in thành công!', messageText, 'success');
  } else {
    document.getElementById('checkinErrorText').textContent = messageText;
    document.getElementById('checkinSuccess').style.display = 'none';
    document.getElementById('checkinError').style.display = 'block';
    showToast('Không thể Check-in', messageText, 'error');
  }
}

function resetCheckIn() {
  document.getElementById('checkinUpload').style.display = 'block';
  document.getElementById('checkinResult').classList.remove('show');
  const fileIn = document.getElementById('qrFileInput');
  if (fileIn) fileIn.value = '';
  const codeIn = document.getElementById('manualCodeInput');
  if (codeIn) codeIn.value = '';
}

document.addEventListener('DOMContentLoaded', initCheckin);
