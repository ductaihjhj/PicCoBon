/* ==========================================================================
   SPIKE PICKLEBALL CLUB — CHECK-IN TERMINAL CONTROLLER (V2 Hardened)
   Real Camera Stream, Server ZXing Image Upload Decode & Approved-Only Check-In
   ========================================================================== */

let activeCameraStream = null;
let cameraScanTimer = null;
let isDecodingFrame = false;

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
        <div class="empty-state-title">Chưa có vé nào trong danh sách hôm nay</div>
        <div class="empty-state-text">Khi khách đặt sân trong ngày, vé sẽ hiển thị tại đây để lễ tân đối chiếu.</div>
      </div>`;
    return;
  }

  listEl.innerHTML = readyList.map(b => {
    const court = getCourtById(b.courtId);
    const endTime = getEndTime(b.time, b.duration);
    const isPlaying = b.status === 'playing';
    const isPending = b.status === 'pending';
    const timing = getBookingTimingState(b);

    let actionBtnHtml = '';
    if (isPlaying) {
      actionBtnHtml = `<span class="badge badge-success"><iconify-icon icon="lucide:check-circle-2" width="13"></iconify-icon> Đang thi đấu trên sân</span>`;
    } else if (isPending) {
      actionBtnHtml = `<a href="/Management" class="btn btn-outline btn-sm" title="Vé chờ duyệt không được phép Check-in trực tiếp">Cần duyệt vé trước</a>`;
    } else if (!timing.canCheckIn) {
      actionBtnHtml = `<span class="badge badge-neutral" title="Chỉ mở Check-in từ 30 phút trước giờ bắt đầu">Mở lúc ${escapeHtml(minutesToTime(Math.max(0, timeToMinutes(b.time) - 30)))}</span>`;
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
    const res = await apiFetch('/api/checkin/verify', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        tokenOrCode: tokenOrCode.trim(),
        requireSignedQr: false
      })
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      showCheckInResult(tokenOrCode.slice(0, 28), false, data.error || 'Mã vé không hợp lệ, chưa được duyệt hoặc ngoài khung giờ Check-in!');
      return;
    }

    stopCameraScanner();
    const b = data.booking;
    const court = getCourtById(b.courtId);
    await syncDatabaseFromServer(true);
    showCheckInResult(
      b.id,
      true,
      `Xác thực hợp lệ! Khách: ${b.name} (${b.phone}) — Đã mở ${court.name} (${b.time} → ${getEndTime(b.time, b.duration)})`
    );
    renderReadyCheckinList();
  } catch (_) {
    showCheckInResult(tokenOrCode.slice(0, 24), false, 'Lỗi kết nối máy chủ khi xác thực vé.');
  }
}

async function decodeImageBlobViaServer(fileOrBlob, displayRef = 'QR IMAGE') {
  const formData = new FormData();
  formData.append('file', fileOrBlob, fileOrBlob.name || 'qr-frame.png');

  try {
    const res = await apiFetch('/api/checkin/decode-qr', {
      method: 'POST',
      body: formData
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      return { ok: false, error: data.error || 'Không phát hiện mã QR hợp lệ trong ảnh.', decodedText: data.decodedText || displayRef };
    }
    return { ok: true, booking: data.booking, decodedText: data.decodedText };
  } catch (_) {
    return { ok: false, error: 'Không thể tải ảnh QR lên máy chủ để giải mã.', decodedText: displayRef };
  }
}

async function handleQrFileSelect(event) {
  const file = event?.target?.files?.[0];
  if (!file) return;

  // If user selected an SVG file downloaded earlier, check if we can rasterize it onto canvas -> PNG for ZXing
  if (file.type === 'image/svg+xml' || file.name.toLowerCase().endsWith('.svg')) {
    try {
      const svgText = await file.text();
      const svgBlob = new Blob([svgText], { type: 'image/svg+xml;charset=utf-8' });
      const url = URL.createObjectURL(svgBlob);
      const img = new Image();
      const pngBlob = await new Promise((resolve, reject) => {
        img.onload = () => {
          const canvas = document.createElement('canvas');
          canvas.width = 400;
          canvas.height = 400;
          const ctx = canvas.getContext('2d');
          ctx.fillStyle = '#FFFFFF';
          ctx.fillRect(0, 0, 400, 400);
          ctx.drawImage(img, 0, 0, 400, 400);
          URL.revokeObjectURL(url);
          canvas.toBlob(b => (b ? resolve(b) : reject(new Error('toBlob failed'))), 'image/png');
        };
        img.onerror = reject;
        img.src = url;
      });
      const res = await decodeImageBlobViaServer(pngBlob, file.name);
      await handleDecodedCheckinResponse(res, file.name);
      return;
    } catch (_) {}
  }

  const res = await decodeImageBlobViaServer(file, file.name);
  await handleDecodedCheckinResponse(res, file.name);
}

async function handleDecodedCheckinResponse(res, fallbackLabel) {
  if (!res.ok || !res.booking) {
    showCheckInResult(res.decodedText ? res.decodedText.slice(0, 26) : fallbackLabel, false, res.error);
    return;
  }

  stopCameraScanner();
  const b = res.booking;
  const court = getCourtById(b.courtId);
  await syncDatabaseFromServer(true);
  showCheckInResult(
    b.id,
    true,
    `Giải mã QR thành công! Khách: ${b.name} (${b.phone}) — Đã mở ${court.name} (${b.time} → ${getEndTime(b.time, b.duration)})`
  );
  renderReadyCheckinList();
}

async function startCameraScanner() {
  const banner = document.getElementById('cameraStatusBanner');
  const wrap = document.getElementById('cameraViewportWrap');
  const video = document.getElementById('qrCameraVideo');
  const btnStart = document.getElementById('btnStartCamera');
  const btnStop = document.getElementById('btnStopCamera');

  if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
    if (banner) banner.textContent = 'Trình duyệt hoặc thiết bị này không hỗ trợ API Camera. Vui lòng dùng mục (3) Tải ảnh QR hoặc (1) Nhập mã vé.';
    showToast('Không tìm thấy Camera', 'Vui lòng tải ảnh QR hoặc nhập mã đặt sân.', 'warning');
    return;
  }

  try {
    stopCameraScanner();
    activeCameraStream = await navigator.mediaDevices.getUserMedia({
      video: { facingMode: { ideal: 'environment' }, width: { ideal: 640 }, height: { ideal: 480 } },
      audio: false
    });
    if (video) {
      video.srcObject = activeCameraStream;
      await video.play();
    }
    if (wrap) wrap.style.display = 'block';
    if (btnStart) btnStart.style.display = 'none';
    if (btnStop) btnStop.style.display = '';
    if (banner) banner.textContent = 'Camera đang hoạt động — Đưa mã QR vào giữa khung hình hoặc bấm "Chụp khung hình & Giải mã QR ngay".';

    // Auto-scan via BarcodeDetector when supported by browser
    if ('BarcodeDetector' in window) {
      const detector = new window.BarcodeDetector({ formats: ['qr_code'] });
      cameraScanTimer = setInterval(async () => {
        if (!activeCameraStream || !video || video.readyState < 2 || isDecodingFrame) return;
        isDecodingFrame = true;
        try {
          const codes = await detector.detect(video);
          if (codes && codes.length > 0 && codes[0].rawValue) {
            await performCheckInByTokenOrCode(codes[0].rawValue);
          }
        } catch (_) {
        } finally {
          isDecodingFrame = false;
        }
      }, 900);
    }
  } catch (err) {
    if (banner) {
      banner.textContent = 'Không thể mở Camera (Thiết bị không có webcam hoặc chưa cấp quyền). Bạn có thể tải ảnh QR ở mục (3) bên dưới.';
    }
    showToast('Chưa cấp quyền Camera', 'Hãy cho phép quyền truy cập Camera hoặc tải trực tiếp ảnh vé QR.', 'warning');
  }
}

function stopCameraScanner() {
  if (cameraScanTimer) {
    clearInterval(cameraScanTimer);
    cameraScanTimer = null;
  }
  if (activeCameraStream) {
    activeCameraStream.getTracks().forEach(t => t.stop());
    activeCameraStream = null;
  }
  const video = document.getElementById('qrCameraVideo');
  if (video) video.srcObject = null;
  const wrap = document.getElementById('cameraViewportWrap');
  if (wrap) wrap.style.display = 'none';
  const btnStart = document.getElementById('btnStartCamera');
  if (btnStart) btnStart.style.display = '';
  const btnStop = document.getElementById('btnStopCamera');
  if (btnStop) btnStop.style.display = 'none';
}

async function captureCameraFrameAndDecode() {
  const video = document.getElementById('qrCameraVideo');
  const canvas = document.getElementById('qrCameraCanvas');
  if (!video || !canvas || video.readyState < 2) {
    showToast('Camera chưa sẵn sàng', 'Vui lòng chờ hình ảnh camera hiển thị rồi thử lại.', 'warning');
    return;
  }

  canvas.width = video.videoWidth || 640;
  canvas.height = video.videoHeight || 480;
  const ctx = canvas.getContext('2d');
  ctx.drawImage(video, 0, 0, canvas.width, canvas.height);

  canvas.toBlob(async blob => {
    if (!blob) return;
    const res = await decodeImageBlobViaServer(blob, 'CAMERA-FRAME');
    await handleDecodedCheckinResponse(res, 'KHUNG HÌNH CAMERA');
  }, 'image/png');
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

window.addEventListener('beforeunload', stopCameraScanner);
document.addEventListener('DOMContentLoaded', initCheckin);
window.addEventListener('pickleball:db-synced', renderReadyCheckinList);
