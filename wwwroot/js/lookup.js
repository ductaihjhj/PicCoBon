/* ==========================================================================
   PICKLEBALL PRO ARENA — LOOKUP & MY TICKETS CONTROLLER
   Auto-lookup for logged-in users, search by Phone/Ref/Name, QR view & Cancel
   ========================================================================== */

function initLookup() {
  const input = document.getElementById('lookupPhone');
  if (CURRENT_USER && CURRENT_USER.phone && CURRENT_USER.role !== 'admin') {
    if (input) input.value = CURRENT_USER.phone;
    searchLookup();
  } else {
    showAllLookup();
  }
}

function quickSearch(val) {
  const input = document.getElementById('lookupPhone');
  if (input) input.value = val;
  searchLookup();
}

function showAllLookup() {
  const input = document.getElementById('lookupPhone');
  if (input) input.value = '';
  renderLookupTable(BOOKINGS, `Tất cả đơn đặt sân trong hệ thống (${BOOKINGS.length} lượt)`);
}

function searchLookup() {
  const query = (document.getElementById('lookupPhone')?.value || '').trim().toLowerCase();
  const card = document.getElementById('lookupResultCard');
  const empty = document.getElementById('lookupEmpty');
  if (!card || !empty) return;

  if (!query) {
    showAllLookup();
    return;
  }

  const results = BOOKINGS.filter(b =>
    (b.phone && b.phone.toLowerCase().includes(query)) ||
    (b.id && b.id.toLowerCase().includes(query)) ||
    (b.name && b.name.toLowerCase().includes(query))
  );

  if (results.length === 0) {
    card.style.display = 'none';
    empty.style.display = 'flex';
    return;
  }

  renderLookupTable(results, `Tìm thấy ${results.length} lượt đặt sân khớp với "${query}"`);
}

function renderLookupTable(list, titleText) {
  const card = document.getElementById('lookupResultCard');
  const empty = document.getElementById('lookupEmpty');
  const titleEl = document.getElementById('lookupTitle');
  const tbody = document.getElementById('lookupResults');
  if (!card || !tbody) return;

  empty.style.display = 'none';
  card.style.display = 'block';
  if (titleEl) titleEl.textContent = titleText;

  tbody.innerHTML = list.map(b => {
    const court = getCourtById(b.courtId);
    const endTime = minutesToTime(timeToMinutes(b.time) + Number(b.duration));
    const total = Number(b.totalPaid) || Number(b.courtFee) || calculateBookingTotal(b.courtId, b.time, b.duration).grandTotal;
    const canCancel = b.status === 'pending';

    return `
      <tr>
        <td><span class="text-mono font-bold" style="color:var(--color-indigo)">${b.id}</span></td>
        <td>
          <div class="font-bold">${b.name}</div>
          <div class="text-xs text-muted text-mono">${b.phone}</div>
        </td>
        <td><span class="font-semibold text-xs">${formatDateVN(b.date)}</span></td>
        <td><span class="badge badge-neutral">${court.name}</span></td>
        <td class="text-mono font-semibold">${b.time} → ${endTime} <span class="text-xs text-muted">(${b.duration}p)</span></td>
        <td class="text-mono font-bold" style="color:var(--color-primary)">${formatMoney(total)}</td>
        <td><span class="badge ${statusBadgeClass(b.status)}">${statusLabel(b.status, b)}</span></td>
        <td>
          <div style="display:flex;gap:0.35rem;align-items:center">
            ${b.status !== 'rejected' ? `<button class="btn btn-outline btn-sm" onclick="viewTicketQR('${b.id}')"><iconify-icon icon="lucide:qr-code" width="14"></iconify-icon> Mã QR</button>` : ''}
            ${canCancel ? `<button class="btn btn-danger-outline btn-sm" onclick="cancelMyBooking('${b.id}')">Hủy đơn</button>` : ''}
          </div>
        </td>
      </tr>
    `;
  }).join('');
}

function viewTicketQR(id) {
  const b = BOOKINGS.find(x => x.id === id);
  if (!b) return;
  const court = getCourtById(b.courtId);
  const endTime = minutesToTime(timeToMinutes(b.time) + Number(b.duration));
  const total = Number(b.totalPaid) || Number(b.courtFee) || calculateBookingTotal(b.courtId, b.time, b.duration).grandTotal;

  document.getElementById('ticketQrCode').textContent = b.id;
  document.getElementById('ticketQrName').textContent = `${b.name} (${b.phone})`;
  document.getElementById('ticketQrDate').textContent = formatDateVN(b.date);
  document.getElementById('ticketQrCourt').textContent = court.name;
  document.getElementById('ticketQrTime').textContent = `${b.time} → ${endTime} (${b.duration} phút)`;
  document.getElementById('ticketQrTotal').textContent = formatMoney(total);
  openModal('modalTicketQR');
}

function cancelMyBooking(id) {
  const b = BOOKINGS.find(x => x.id === id);
  if (!b) return;
  if (!confirm(`Bạn có chắc chắn muốn hủy đơn đặt sân ${b.id} (${b.time})?`)) return;
  b.status = 'rejected';
  saveData();
  showToast('Đã hủy đơn đặt sân', `Đơn ${b.id} đã được hủy thành công.`, 'info');
  searchLookup();
}

document.addEventListener('DOMContentLoaded', initLookup);
window.addEventListener('pickleball:db-synced', () => {
  const input = document.getElementById('lookupPhone');
  if (input && input.value.trim()) {
    searchLookup();
  } else {
    initLookup();
  }
});
