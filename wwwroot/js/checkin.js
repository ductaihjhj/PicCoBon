/* ==========================================================================
   SPIKE PICKLEBALL CLUB — CHECK-IN TERMINAL CONTROLLER
   Server-Authoritative Signed QR Verification & 1-Click Check-in (P1-01, P1-07)
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

  const todayIso = getTodayStr();
  const readyList = BOOKINGS.filter(b =>
    ['approved', 'pending', 'playing'].includes(b.status) &&
    b.date === todayIso
  );

  if (countEl) countEl.textContent = `${readyList.length} vé hôm nay`;

  if (readyList.length === 0) {
    listEl.innerHTML = `
      <div class="empty-state">
        <div class="empty-state-icon"><iconify-icon icon="lucide:ticket" width="28"></iconify-icon></div>
        <div class="empty-state-title">Chưa có vé nào chờ nhận sân hôm nay</div>
      </div>`;
    return;
  }

  listEl.innerHTML = readyList.map(b => {
    const court = getCourtById(b.courtId);
    const endTime = getEndTime(b.time, b.duration);
    const isPlaying = b.status === 'playing';

    let actionBtnHtml = '';
    if (isPlaying) {
      actionBtnHtml = `<span class="badge badge-success"><iconify-icon icon="lucide:check-circle-2" width="13"></iconify-icon> Đang thi đấu trên sân</span>`;
    } else {
      actionBtnHtml = `<button class="btn btn-primary btn-sm" onclick="performCheckInByTokenOrCode('${escapeAttr(b.checkInToken || b.id)}')"><iconify-icon icon="lucide:log-in" width="14"></iconify-icon> Check-in mở sân</button>`;
    }

    return `
      <div class="item-row" ${isPlaying ? 'style="background:var(--color-primary-50)"' : ''}>
        <div class="item-avatar" style="background:${isPlaying ? 'var(--color-primary)' : 'var(--color-indigo-50)'};color:${isPlaying ? '#FFF' : 'var(--color-indigo)'}">S${Number(court.id)}</div>
        <div class="item-info">
          <div class="item-name">
            <span>${escapeHtml(b.name)}</span>
            <span class="badge badge-neutral text-mono">${escapeHtml(b.id)}</span>
            <span class="badge ${statusBadgeClass(b.status)}">${escapeHtml(statusLabel(b.status, b))}</span>
          </div>
          <div class="item-detail">
            <strong>${escapeHtml(court.name)}</strong> · <span class="text-mono font-bold">${escapeHtml(b.time)} → ${escapeHtml(endTime)}</span> · ${escapeHtml(formatDateVN(b.date))} · SĐT: <span class="text-mono">${escapeHtml(b.phone)}</span>
          </div>
        </div>
        <div class="item-actions">
          ${actionBtnHtml}
        </div>
      </div>
    `;
  }).join('');
}

async function verifyManualCode() {
  const query = (document.getElementById('manualCodeInput')?.value || '').trim();
  if (!query) {
    showToast('Nhập mã vé / Token QR', 'Vui lòng nhập mã đặt sân (VD: PB-XXXXXX) hoặc chuỗi Token QR (SPK1...).', 'warning');
    return;
  }

  await performCheckInByTokenOrCode(query);
}

async function performCheckInByTokenOrCode(tokenOrCode) {
  try {
    const res = await fetch('/api/checkin/verify', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        tokenOrCode: tokenOrCode.trim(),
        requireSignedQr: false
      })
    });
    const data = await res.json();
    if (!res.ok || !data.ok) {
      showCheckInResult(tokenOrCode.toUpperCase(), false, data.error || 'Mã vé không hợp lệ hoặc đã được sử dụng!');
      return;
    }

    const b = data.booking;
    const court = getCourtById(b.courtId);
    await syncDatabaseFromServer();
    showCheckInResult(
      b.id,
      true,
      `Xác thực hợp lệ! Khách: ${b.name} (${b.phone}) — Đã mở ${court.name} (${b.time} → ${getEndTime(b.time, b.duration)})`
    );
    renderReadyCheckinList();
  } catch (_) {
    showCheckInResult(tokenOrCode, false, 'Lỗi kết nối máy chủ khi xác thực vé.');
  }
}

async function handleQrFileSelect(event) {
  const file = event?.target?.files?.[0];
  if (!file) return;

  // If SVG or text file containing data-qr-token, extract signed token
  try {
    const text = await file.text();
    const tokenMatch = text.match(/SPK1\.[A-Z0-9\-]+\.[0-9]+\.[0-9\-]+\.[0-9]+\.[A-F0-9]{16}/i);
    if (tokenMatch) {
      await performCheckInByTokenOrCode(tokenMatch[0]);
      return;
    }

    // Try BarcodeDetector API if browser supports it
    if ('BarcodeDetector' in window) {
      const bitmap = await createImageBitmap(file);
      const detector = new window.BarcodeDetector({ formats: ['qr_code'] });
      const codes = await detector.detect(bitmap);
      if (codes && codes.length > 0 && codes[0].rawValue) {
        await performCheckInByTokenOrCode(codes[0].rawValue);
        return;
      }
    }

    showCheckInResult(
      file.name,
      false,
      'Trình duyệt không giải mã được ảnh QR tĩnh này. Vui lòng nhập mã PB-XXXXXX ở ô bên trên hoặc bấm "Check-in mở sân" ở danh sách bên phải.'
    );
  } catch (_) {
    showCheckInResult('QR FILE', false, 'Không thể đọc tệp ảnh QR.');
  }
}

// Keep mockCheckIn alias mapped to safe file handler so existing onchange="mockCheckIn()" in view works safely without faking check-in
function mockCheckIn() {
  const fileInput = document.getElementById('qrFileInput');
  if (fileInput && fileInput.files && fileInput.files[0]) {
    handleQrFileSelect({ target: fileInput });
  } else {
    showToast('Chọn vé cụ thể', 'Vui lòng nhập mã vé PB-XXXXXX hoặc bấm "Check-in mở sân" bên cạnh tên khách hàng.', 'warning');
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
window.addEventListener('pickleball:db-synced', renderReadyCheckinList);
