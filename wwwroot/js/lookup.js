/* ==========================================================================
   SPIKE PICKLEBALL CLUB — SECURE LOOKUP & E-TICKET QR CONTROLLER
   Fixes IDOR (P0-07), renders signed QR SVG (P1-01), enforces cutoff policy (P1-12)
   ========================================================================== */

let currentLookupList = [];

async function initLookup() {
  const user = getCurrentUser();
  const codeInput = document.getElementById('lookupCode');
  const phoneInput = document.getElementById('lookupPhone');
  const showAllBtn = document.getElementById('lookupShowAllBtn');

  if (showAllBtn) {
    // Only show "Xem lịch của tôi" for logged-in users or "Xem tất cả" for Admin/Staff (P0-07)
    if (isAdminUser()) {
      showAllBtn.style.display = '';
      showAllBtn.querySelector('span').textContent = 'Xem tất cả đơn (Admin)';
    } else if (user) {
      showAllBtn.style.display = '';
      showAllBtn.querySelector('span').textContent = 'Xem vé của tôi';
    } else {
      showAllBtn.style.display = 'none';
    }
  }

  if (user) {
    if (phoneInput && !phoneInput.value && user.phone) {
      phoneInput.value = user.phone;
    }
    await showAllLookup();
  }
}

async function showAllLookup() {
  const user = getCurrentUser();
  if (!user) {
    showToast('Yêu cầu xác thực', 'Khách vãng lai vui lòng nhập đồng thời Mã đặt sân (PB-...) và Số điện thoại để tra cứu vé.', 'warning');
    return;
  }

  try {
    const endpoint = isAdminUser() ? '/api/bookings/lookup' : '/api/bookings/me';
    const res = await fetch(endpoint, { headers: { 'Accept': 'application/json' }, cache: 'no-store' });
    const data = await res.json();
    if (!res.ok || !data.ok) {
      showToast('Không thể tải danh sách', data.error || 'Vui lòng thử lại.', 'error');
      return;
    }

    currentLookupList = data.bookings || [];
    const label = isAdminUser()
      ? `Tất cả đơn đặt sân trong hệ thống (${currentLookupList.length} lượt)`
      : `Danh sách vé đặt sân của bạn (${currentLookupList.length} lượt)`;
    renderLookupTable(currentLookupList, label);
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể tải dữ liệu vé đặt sân.', 'error');
  }
}

async function searchLookup() {
  const rawInput = (document.getElementById('lookupPhone')?.value || '').trim();
  const codeInputVal = (document.getElementById('lookupCode')?.value || '').trim();
  const card = document.getElementById('lookupResultCard');
  const empty = document.getElementById('lookupEmpty');
  if (!card || !empty) return;

  let code = codeInputVal;
  let phone = rawInput;

  // Allow user to type "PB-XXXX 0901234567" in a single input or use separate inputs
  if (!code && rawInput.includes(' ')) {
    const parts = rawInput.split(/\s+/);
    const codePart = parts.find(p => p.toUpperCase().startsWith('PB-') || p.toUpperCase().startsWith('SPK1.'));
    const phonePart = parts.find(p => /^[0+][0-9]{8,11}$/.test(p));
    if (codePart) code = codePart;
    if (phonePart) phone = phonePart;
  } else if (!code && (rawInput.toUpperCase().startsWith('PB-') || rawInput.toUpperCase().startsWith('SPK1.'))) {
    code = rawInput;
    phone = getCurrentUser()?.phone || '';
  }

  if (!code && !phone) {
    if (getCurrentUser()) {
      await showAllLookup();
    } else {
      showToast('Nhập thông tin tra cứu', 'Vui lòng nhập Mã đặt sân (PB-...) kèm Số điện thoại đặt sân.', 'warning');
    }
    return;
  }

  try {
    const qs = new URLSearchParams();
    if (code) qs.set('code', code);
    if (phone) qs.set('phone', phone);

    const res = await fetch(`/api/bookings/lookup?${qs.toString()}`, {
      headers: { 'Accept': 'application/json' },
      cache: 'no-store'
    });
    const data = await res.json();

    if (!res.ok || !data.ok) {
      card.style.display = 'none';
      empty.style.display = 'flex';
      showToast('Tra cứu bảo mật', data.error || 'Vui lòng nhập đúng Mã đặt sân và Số điện thoại.', 'warning');
      return;
    }

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
    const court = getCourtById(b.courtId);
    const endTime = getEndTime(b.time, b.duration);
    const total = Number(b.amountDue) || Number(b.courtFee) || 0;
    const canCancel = b.status === 'pending' || b.status === 'approved';
    const paymentBadge = b.paymentStatus === 'paid'
      ? '<span class="badge badge-success" style="font-size:0.68rem;">Đã thanh toán</span>'
      : '<span class="badge badge-neutral" style="font-size:0.68rem;">Thu tại quầy</span>';

    return `
      <tr>
        <td><span class="text-mono font-bold" style="color:var(--color-indigo)">${escapeHtml(b.id)}</span></td>
        <td>
          <div class="font-bold">${escapeHtml(b.name)}</div>
          <div class="text-xs text-muted text-mono">${escapeHtml(b.phone)}</div>
        </td>
        <td><span class="font-semibold text-xs">${formatDateVN(b.date)}</span></td>
        <td><span class="badge badge-neutral">${escapeHtml(court.name)}</span></td>
        <td class="text-mono font-semibold">${escapeHtml(b.time)} → ${escapeHtml(endTime)} <span class="text-xs text-muted">(${Number(b.duration)}p)</span></td>
        <td>
          <div class="text-mono font-bold" style="color:var(--color-primary)">${formatMoney(total)}</div>
          ${paymentBadge}
        </td>
        <td><span class="badge ${statusBadgeClass(b.status)}">${escapeHtml(statusLabel(b.status, b))}</span></td>
        <td>
          <div style="display:flex;gap:0.35rem;align-items:center;flex-wrap:wrap;">
            ${b.status !== 'rejected' && b.status !== 'cancelled' ? `<button class="btn btn-outline btn-sm" onclick="viewTicketQR('${escapeAttr(b.id)}')"><iconify-icon icon="lucide:qr-code" width="14"></iconify-icon> Mã QR</button>` : ''}
            ${canCancel ? `<button class="btn btn-danger-outline btn-sm" onclick="cancelMyBooking('${escapeAttr(b.id)}', '${escapeAttr(b.phone)}')">Hủy đơn</button>` : ''}
          </div>
        </td>
      </tr>
    `;
  }).join('');
}

function viewTicketQR(id) {
  const b = currentLookupList.find(x => x.id === id) || MY_BOOKINGS.find(x => x.id === id) || BOOKINGS.find(x => x.id === id);
  if (!b) return;
  const court = getCourtById(b.courtId);
  const endTime = getEndTime(b.time, b.duration);
  const total = Number(b.amountDue) || Number(b.courtFee) || 0;
  const qrToken = b.checkInToken || b.id;

  const qrBox = document.querySelector('#modalTicketQR .qr-placeholder');
  if (qrBox) {
    qrBox.innerHTML = `
      ${renderQrCodeSvg(qrToken, 152)}
      <div class="text-xs font-bold text-mono mt-sm" id="ticketQrCode" style="color:var(--color-indigo)">${escapeHtml(b.id)}</div>
      <div style="font-size:0.68rem;color:var(--text-muted);font-family:var(--font-mono);margin-top:2px;">Token bảo mật: ${escapeHtml(String(qrToken).slice(0, 24))}...</div>
    `;
  }

  document.getElementById('ticketQrName').textContent = `${b.name} (${b.phone})`;
  document.getElementById('ticketQrDate').textContent = formatDateVN(b.date);
  document.getElementById('ticketQrCourt').textContent = court.name;
  document.getElementById('ticketQrTime').textContent = `${b.time} → ${endTime} (${b.duration} phút)`;
  document.getElementById('ticketQrTotal').textContent = `${formatMoney(total)} (${b.paymentStatus === 'paid' ? 'Đã thanh toán' : 'Thanh toán tại quầy'})`;
  openModal('modalTicketQR');
}

async function cancelMyBooking(id, phone) {
  if (!confirm(`Bạn có chắc chắn muốn hủy đơn đặt sân ${id}? (Lưu ý: Lịch đã duyệt chỉ có thể tự hủy trước giờ thi đấu tối thiểu 2 tiếng)`)) {
    return;
  }

  try {
    const res = await fetch(`/api/bookings/${encodeURIComponent(id)}/cancel`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        phone: phone || document.getElementById('lookupPhone')?.value || '',
        reason: 'Khách hàng yêu cầu hủy trên trang Tra cứu'
      })
    });
    const data = await res.json();
    if (!res.ok || !data.ok) {
      showToast('Không thể hủy lịch', data.error || 'Đã quá thời hạn tự hủy lịch.', 'error');
      return;
    }

    showToast('Đã hủy lịch đặt sân', `Đơn ${id} đã được hủy thành công.`, 'success');
    await syncDatabaseFromServer();
    await searchLookup();
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
