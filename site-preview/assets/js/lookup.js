/* ==========================================================================
   SPIKE PICKLEBALL CLUB — SECURE LOOKUP & E-TICKET QR CONTROLLER (V2 Hardened)
   Strict ownership: AccountId OR BookingCode + AccessToken (NEVER phone number!)
   ========================================================================== */

let currentLookupList = [];
let activeLookupAccessToken = '';

function resolveStoredGuestToken(code) {
  if (!code) return '';
  try {
    return window.sessionStorage.getItem(`pb_guest_token_${code.toUpperCase()}`) || '';
  } catch (_) {
    return '';
  }
}

async function initLookup() {
  const user = getCurrentUser();
  const showAllBtn = document.getElementById('lookupShowAllBtn');
  const codeInput = document.getElementById('lookupCode');
  const tokenInput = document.getElementById('lookupAccessToken');

  if (showAllBtn) {
    if (isAdminUser()) {
      showAllBtn.style.display = '';
      showAllBtn.querySelector('span').textContent = 'Xem tất cả đơn (Quản lý)';
    } else if (user) {
      showAllBtn.style.display = '';
      showAllBtn.querySelector('span').textContent = 'Xem vé của tôi';
    } else {
      showAllBtn.style.display = 'none';
    }
  }

  const params = new URLSearchParams(window.location.search);
  const urlCode = (params.get('code') || '').trim();
  const urlToken = (params.get('accessToken') || '').trim();

  if (urlCode && codeInput) {
    codeInput.value = urlCode;
    const savedTok = urlToken || resolveStoredGuestToken(urlCode);
    if (savedTok && tokenInput) {
      tokenInput.value = savedTok;
    }
    if (savedTok || user) {
      await searchLookup();
      return;
    }
  }

  if (user) {
    await showAllLookup();
  }
}

async function showAllLookup() {
  const user = getCurrentUser();
  if (!user) {
    showToast('Yêu cầu xác thực', 'Khách vãng lai vui lòng nhập Mã đặt sân (PB-...) kèm Mã bảo mật vé (bk_...) hoặc bấm vé mẫu bên dưới.', 'warning');
    return;
  }

  await SpikeDemo.ensureLoaded(false);
  const raw = SpikeDemo.getRawState();
  activeLookupAccessToken = '';
  currentLookupList = isAdminUser()
    ? [...(raw.bookings || [])]
    : (raw.bookings || []).filter(b => Number(b.accountId) === Number(user.id) || (user.phone && b.phone === user.phone));

  const label = isAdminUser()
    ? `Tất cả đơn đặt sân mẫu trong hệ thống (${currentLookupList.length} lượt)`
    : `Danh sách vé đặt sân tài khoản Demo của bạn (${currentLookupList.length} lượt)`;
  renderLookupTable(currentLookupList, label);
}

function quickDemoLookup(code, token) {
  const cEl = document.getElementById('lookupCode');
  const tEl = document.getElementById('lookupAccessToken');
  if (cEl) cEl.value = code || '';
  if (tEl) tEl.value = token || '';
  searchLookup();
}

async function searchLookup() {
  const codeVal = (document.getElementById('lookupCode')?.value || '').trim().toUpperCase();
  let tokenVal = (document.getElementById('lookupAccessToken')?.value || '').trim();
  const card = document.getElementById('lookupResultCard');
  const empty = document.getElementById('lookupEmpty');
  if (!card || !empty) return;

  if (!tokenVal && codeVal) {
    tokenVal = resolveStoredGuestToken(codeVal);
  }

  const user = getCurrentUser();
  if (!codeVal && !tokenVal) {
    if (user) {
      await showAllLookup();
    } else {
      showToast('Nhập thông tin tra cứu', 'Vui lòng nhập Mã đặt sân (PB-...) kèm Mã bảo mật vé (bk_...).', 'warning');
    }
    return;
  }

  await SpikeDemo.ensureLoaded(false);
  const all = SpikeDemo.getRawState()?.bookings || [];
  const matches = all.filter(b => {
    const codeMatch = !codeVal || String(b.id || '').toUpperCase() === codeVal;
    const isOwner = user && (isAdminUser() || Number(b.accountId) === Number(user.id) || (user.phone && b.phone === user.phone));
    const tokenMatch = tokenVal && (
      String(b.accessToken || '').toLowerCase() === tokenVal.toLowerCase() ||
      String(b.checkInToken || '').toLowerCase() === tokenVal.toLowerCase()
    );
    return codeMatch && (isOwner || tokenMatch);
  });

  if (matches.length === 0) {
    card.style.display = 'none';
    empty.style.display = 'flex';
    showToast('Không tìm thấy vé khớp mã', 'Thử chọn nhanh một mã vé mẫu hoặc kiểm tra lại Mã đặt sân & Mã bảo mật.', 'warning');
    return;
  }

  activeLookupAccessToken = tokenVal;
  currentLookupList = matches;
  renderLookupTable(currentLookupList, `Tìm thấy ${currentLookupList.length} vé đặt sân mẫu`);
}

function renderLookupTable(list, titleText) {
  const card = document.getElementById('lookupResultCard');
  const empty = document.getElementById('lookupEmpty');
  const titleEl = document.getElementById('lookupTitle');
  const tbody = document.getElementById('lookupResults');
  if (!card || !tbody) return;

  if (!list || list.length === 0) {
    card.style.display = 'none';
    if (empty) empty.style.display = 'flex';
    return;
  }

  if (empty) empty.style.display = 'none';
  card.style.display = 'block';
  if (titleEl) titleEl.textContent = titleText;

  tbody.innerHTML = list.map(b => {
    const courtName = typeof getCourtDisplayName === 'function'
      ? getCourtDisplayName(b.courtId, b.courtName)
      : (getCourtById(b.courtId)?.name || `Sân #${b.courtId}`);
    const endTime = getEndTime(b.time, b.duration);
    const total = Number(b.amountDue) || Number(b.courtFee) || 0;
    const paid = Number(b.paidAmount) || 0;
    const canCancel = b.status === 'pending' || b.status === 'approved';
    const showQr = !['rejected', 'cancelled', 'expired', 'no_show'].includes(b.status);
    const remainingDue = Math.max(0, total - paid);
    const canPayPayPal = remainingDue > 0 && ['pending', 'approved', 'playing'].includes(b.status);

    return `
      <tr>
        <td><span class="text-mono font-bold" style="color:var(--color-indigo);white-space:nowrap;">${escapeHtml(b.id)}</span></td>
        <td>
          <div class="font-bold">${escapeHtml(b.name)}</div>
          <div class="text-xs text-muted text-mono">${escapeHtml(b.phone)}</div>
        </td>
        <td><span class="font-semibold text-xs">${formatDateVN(b.date)}</span></td>
        <td><span class="badge badge-neutral">${escapeHtml(courtName)}</span></td>
        <td class="text-mono font-semibold">${escapeHtml(b.time)} → ${escapeHtml(endTime)} <span class="text-xs text-muted">(${Number(b.duration)}p)</span></td>
        <td>
          <div class="text-mono font-bold" style="color:var(--color-primary)">${formatMoney(total)}</div>
          ${paymentBadgeHtml(paid, total, b.paymentStatus)}
        </td>
        <td>
          <span class="badge ${statusBadgeClass(b.status, b)}">${escapeHtml(statusLabel(b.status, b))}</span>
          ${b.status === 'pending' && b.holdExpiresAtUtc ? `<div class="text-xs text-muted" style="margin-top:3px;">${escapeHtml(getPendingHoldRemainingLabel(b))}</div>` : ''}
        </td>
        <td>
          <div style="display:flex;gap:0.35rem;align-items:center;flex-wrap:wrap;">
            ${showQr ? `<button type="button" class="btn btn-outline btn-sm" onclick="viewTicketQR('${escapeAttr(b.id)}')"><iconify-icon icon="lucide:qr-code" width="14"></iconify-icon> Vé QR</button>` : ''}
            ${canPayPayPal ? `<button type="button" class="btn btn-primary btn-sm" onclick="payBookingRemainingWithPayPal('${escapeAttr(b.id)}', this)"><iconify-icon icon="lucide:globe" width="14"></iconify-icon> PayPal Sandbox</button>` : ''}
            ${canCancel ? `<button type="button" class="btn btn-danger-outline btn-sm" onclick="cancelMyBooking('${escapeAttr(b.id)}')">Hủy đơn</button>` : ''}
          </div>
        </td>
      </tr>
    `;
  }).join('');
}

function viewTicketQR(id) {
  const b = currentLookupList.find(x => x.id === id) || MY_BOOKINGS.find(x => x.id === id) || BOOKINGS.find(x => x.id === id);
  if (!b) return;
  const courtName = typeof getCourtDisplayName === 'function'
    ? getCourtDisplayName(b.courtId, b.courtName)
    : (getCourtById(b.courtId)?.name || `Sân #${b.courtId}`);
  const endTime = getEndTime(b.time, b.duration);
  const total = Number(b.amountDue) || Number(b.courtFee) || 0;
  const paid = Number(b.paidAmount) || 0;
  const tok = activeLookupAccessToken || resolveStoredGuestToken(b.id);

  if (window.SpikeTicket && document.getElementById('lookupDigitalTicketHost')) {
    SpikeTicket.mountTicket('lookupDigitalTicketHost', b, {
      accessToken: tok,
      theme: 'light'
    });
  }

  const qrCodeEl = document.getElementById('ticketQrCode');
  if (qrCodeEl) {
    qrCodeEl.textContent = b.id;
  }

  const dlBtn = document.getElementById('ticketQrDownloadPngBtn');
  if (dlBtn) {
    dlBtn.href = getBookingQrPngUrl(b.id, tok);
    dlBtn.setAttribute('download', `Spike-QR-${b.id}.png`);
  }

  document.getElementById('ticketQrName').textContent = `${b.name} (${b.phone})`;
  document.getElementById('ticketQrDate').textContent = formatDateVN(b.date);
  document.getElementById('ticketQrCourt').textContent = courtName;
  document.getElementById('ticketQrTime').textContent = `${b.time} → ${endTime} (${b.duration} phút)`;
  document.getElementById('ticketQrTotal').textContent = `${formatMoney(total)} (${paid >= total && total > 0 ? 'Đã thu đủ' : `Đã thu ${formatMoney(paid)}`})`;
  openModal('modalTicketQR');
}

async function payBookingRemainingWithPayPal(id, btnEl) {
  if (!window.SpikePayPal || !id) return;
  const tok = activeLookupAccessToken
    || (document.getElementById('lookupAccessToken')?.value || '').trim()
    || resolveStoredGuestToken(id)
    || '';

  if (btnEl) btnEl.disabled = true;
  try {
    const res = await SpikePayPal.createAndRedirect({
      targetType: 'booking',
      targetCode: id,
      guestAccessToken: tok
    });
    if (!res || !res.redirecting) {
      showToast('Không thể thanh toán PayPal', (res && res.error) || 'Vui lòng kiểm tra lại điều kiện thanh toán hoặc thử lại sau.', 'error');
    }
  } finally {
    if (btnEl) btnEl.disabled = false;
  }
}

async function cancelMyBooking(id) {
  if (!confirm(`Mô phỏng hủy đơn đặt sân ${id} trong bản UI Demo?`)) {
    return;
  }

  const raw = SpikeDemo.getRawState();
  const target = (raw?.bookings || []).find(b => b.id === id);
  if (!target) {
    showToast('Không tìm thấy lịch', 'Đơn đặt sân không tồn tại trong dữ liệu mẫu.', 'error');
    return;
  }
  target.status = 'cancelled';
  SpikeDemo.saveState();
  showToast('Đã hủy lịch (UI Demo)', `Đơn ${id} đã chuyển sang trạng thái Đã hủy trên trình duyệt.`, 'success');
  await syncDatabaseFromServer(true);
  if (getCurrentUser() && !activeLookupAccessToken) {
    await showAllLookup();
  } else {
    await searchLookup();
  }
}

document.addEventListener('DOMContentLoaded', initLookup);
window.addEventListener('pickleball:db-synced', () => {
  if (currentLookupList.length === 0 && getCurrentUser()) {
    initLookup();
  }
});
