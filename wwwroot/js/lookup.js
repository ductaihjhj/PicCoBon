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
    showToast('Yêu cầu xác thực', 'Khách vãng lai vui lòng nhập Mã đặt sân (PB-...) kèm Mã bảo mật vé (bk_...) để tra cứu.', 'warning');
    return;
  }

  try {
    const endpoint = isAdminUser() ? '/api/bookings/lookup' : '/api/bookings/me';
    const res = await fetch(endpoint, {
      headers: { 'Accept': 'application/json' },
      credentials: 'same-origin',
      cache: 'no-store'
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      showToast('Không thể tải danh sách', data.error || 'Vui lòng thử lại.', 'error');
      return;
    }

    activeLookupAccessToken = '';
    currentLookupList = data.bookings || [];
    const label = isAdminUser()
      ? `Tất cả đơn đặt sân trong hệ thống (${currentLookupList.length} lượt)`
      : `Danh sách vé đặt sân tài khoản của bạn (${currentLookupList.length} lượt)`;
    renderLookupTable(currentLookupList, label);
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể tải dữ liệu vé đặt sân.', 'error');
  }
}

async function searchLookup() {
  const codeVal = (document.getElementById('lookupCode')?.value || '').trim();
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

  try {
    const qs = new URLSearchParams();
    if (codeVal) qs.set('code', codeVal);
    if (tokenVal) qs.set('accessToken', tokenVal);

    const res = await fetch(`/api/bookings/lookup?${qs.toString()}`, {
      headers: { 'Accept': 'application/json' },
      credentials: 'same-origin',
      cache: 'no-store'
    });
    const data = await res.json().catch(() => ({}));

    if (!res.ok || !data.ok) {
      card.style.display = 'none';
      empty.style.display = 'flex';
      showToast('Xác thực quyền sở hữu vé', data.error || 'Cần đúng Mã đặt sân và Mã bảo mật vé (bk_...) hoặc tài khoản đặt vé.', 'warning');
      return;
    }

    activeLookupAccessToken = tokenVal;
    currentLookupList = data.bookings || [];
    if (currentLookupList.length === 0) {
      card.style.display = 'none';
      empty.style.display = 'flex';
      return;
    }

    renderLookupTable(currentLookupList, `Tìm thấy ${currentLookupList.length} vé đặt sân hợp lệ`);
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể thực hiện tra cứu lúc này.', 'error');
  }
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

    return `
      <tr>
        <td><span class="text-mono font-bold" style="color:var(--color-indigo)">${escapeHtml(b.id)}</span></td>
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

async function cancelMyBooking(id) {
  if (!confirm(`Bạn có chắc chắn muốn hủy đơn đặt sân ${id}? (Lưu ý: Lịch đã duyệt chỉ có thể tự hủy trước giờ thi đấu tối thiểu 2 tiếng)`)) {
    return;
  }

  const accessToken = activeLookupAccessToken
    || (document.getElementById('lookupAccessToken')?.value || '').trim()
    || resolveStoredGuestToken(id);

  try {
    const res = await apiFetch(`/api/bookings/${encodeURIComponent(id)}/cancel`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        accessToken,
        reason: 'Khách hàng yêu cầu hủy trên trang Tra cứu'
      })
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      showToast('Không thể hủy lịch', data.error || 'Đã quá thời hạn tự hủy lịch hoặc thiếu mã bảo mật vé.', 'error');
      return;
    }

    showToast('Đã hủy lịch đặt sân', `Đơn ${id} đã được hủy thành công.`, 'success');
    await syncDatabaseFromServer(true);
    if (getCurrentUser() && !activeLookupAccessToken) {
      await showAllLookup();
    } else {
      await searchLookup();
    }
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể gửi yêu cầu hủy đơn lúc này.', 'error');
  }
}

document.addEventListener('DOMContentLoaded', initLookup);
window.addEventListener('pickleball:db-synced', () => {
  if (currentLookupList.length === 0 && getCurrentUser()) {
    initLookup();
  }
});
