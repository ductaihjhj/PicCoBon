/* ==========================================================================
   SPIKE PICKLEBALL CLUB — PRO SHOP, F&B & COURT-SIDE ORDERING CONTROLLER (V2)
   Atomic Stock, Ownership-Verified Court Attachment & CSRF Hardened
   ========================================================================== */

let activeMenuCategory = 'all';
let shopCart = [];
let isSubmittingShopOrder = false;

function filterMenuCategory(cat) {
  activeMenuCategory = cat;
  document.querySelectorAll('#menuCategoryTabs .filter-tab').forEach(btn => {
    const isMatch = (cat === 'all' && btn.textContent.includes('Tất cả')) ||
                    (cat === 'drink' && btn.textContent.includes('Đồ uống')) ||
                    (cat === 'food' && btn.textContent.includes('Đồ ăn')) ||
                    (cat === 'other' && btn.textContent.includes('Thuê vợt'));
    btn.classList.toggle('active', isMatch);
  });
  renderMenu();
}

function toggleGuestBookingAttach() {
  const sel = document.getElementById('shopOrderBooking');
  const box = document.getElementById('guestBookingAttachFields');
  if (!sel || !box) return;
  box.style.display = sel.value === '__GUEST_ATTACH__' ? 'block' : 'none';
}

function autoFillShopCustomer() {
  const user = getCurrentUser();
  if (user) {
    const nameEl = document.getElementById('shopOrderName');
    const phoneEl = document.getElementById('shopOrderPhone');
    if (nameEl && !nameEl.value) nameEl.value = user.name || '';
    if (phoneEl && !phoneEl.value) phoneEl.value = user.phone || '';
  }

  const bookingSelect = document.getElementById('shopOrderBooking');
  if (!bookingSelect) return;

  const sourceList = isAdminUser() ? BOOKINGS : MY_BOOKINGS;
  const activeBookings = (sourceList || []).filter(b => ['approved', 'playing'].includes(b.status) && !b.isAnonymized);

  const currentVal = bookingSelect.value;
  let optionsHtml = `<option value="">-- Mua lẻ / Nhận trực tiếp tại quầy --</option>`;
  optionsHtml += activeBookings.map(b => {
    const court = getCourtById(b.courtId);
    return `<option value="${escapeAttr(b.id)}">Gộp vào vé của tôi: ${escapeHtml(b.id)} (${escapeHtml(court.name)} · ${escapeHtml(b.time)})</option>`;
  }).join('');

  if (!user) {
    optionsHtml += `<option value="__GUEST_ATTACH__">Gộp vào vé khách vãng lai (Cần Mã đặt sân + Mã bảo mật bk_...)</option>`;
  }

  bookingSelect.innerHTML = optionsHtml;
  if (currentVal && (currentVal === '__GUEST_ATTACH__' || activeBookings.some(b => b.id === currentVal))) {
    bookingSelect.value = currentVal;
  }
  toggleGuestBookingAttach();
}

function renderMenu() {
  const grid = document.getElementById('menuGrid');
  const countEl = document.getElementById('menuCount');
  const menuData = Array.isArray(MENU) && MENU.length > 0 ? MENU : MENU_ITEMS;
  if (countEl) countEl.textContent = menuData.length;
  if (!grid) return;

  const isStrictAdm = typeof isStrictAdmin === 'function' ? isStrictAdmin() : isAdminUser();
  const addCard = document.getElementById('addMenuItemCard');
  if (addCard) addCard.style.display = isStrictAdm ? '' : 'none';

  const keyword = (document.getElementById('menuSearchInput')?.value || '').trim().toLowerCase();

  const filtered = menuData.filter(item => {
    const matchCat = activeMenuCategory === 'all' || item.type === activeMenuCategory;
    const matchKw = !keyword || (item.name && item.name.toLowerCase().includes(keyword));
    return matchCat && matchKw;
  });

  if (filtered.length === 0) {
    grid.innerHTML = `
      <div class="card empty-state" style="grid-column:1/-1">
        <div class="empty-state-icon"><iconify-icon icon="lucide:coffee" width="28"></iconify-icon></div>
        <div class="empty-state-title">Không tìm thấy sản phẩm nào phù hợp</div>
        <div class="empty-state-text">Hãy thử từ khóa khác hoặc chuyển sang danh mục Tất cả.</div>
      </div>`;
    return;
  }

  grid.innerHTML = filtered.map(item => {
    const typeLabel = item.type === 'drink' ? 'Đồ uống' : item.type === 'food' ? 'Đồ ăn nhẹ' : 'Dụng cụ';
    const inCart = shopCart.find(c => Number(c.id) === Number(item.id));
    const stock = typeof item.stock === 'number' ? item.stock : 0;
    const outOfStock = stock <= 0;

    return `
      <div class="menu-item ${inCart ? 'is-selected' : ''}" ${outOfStock ? 'style="opacity:0.65"' : ''}>
        <div class="menu-item-top">
          <div class="menu-item-icon">${sanitizeIconHtml(item.icon)}</div>
          <div class="menu-item-top-right">
            <span class="menu-item-tag">${escapeHtml(typeLabel)}</span>
            <span class="badge ${outOfStock ? 'badge-danger' : 'badge-neutral'}" style="font-size:0.68rem;">${outOfStock ? 'Hết hàng' : `Còn ${stock}`}</span>
          </div>
        </div>
        <div class="menu-item-body">
          <div class="menu-item-name">${escapeHtml(item.name)}</div>
        </div>
        <div class="menu-item-footer">
          <div class="menu-item-price text-mono">${formatMoney(item.price)}</div>
          <button type="button"
            class="btn ${inCart ? 'btn-primary' : 'btn-outline'} btn-xs menu-item-cta"
            ${outOfStock ? 'disabled' : ''}
            onclick="addToShopCart(${Number(item.id)})">
            <iconify-icon icon="${inCart ? 'lucide:check' : 'lucide:plus'}" width="14"></iconify-icon>
            <span>${outOfStock ? 'Hết hàng' : inCart ? `Đã chọn (${inCart.qty})` : 'Chọn mua'}</span>
          </button>
        </div>
      </div>
    `;
  }).join('');
}

function addToShopCart(itemId) {
  const menuData = Array.isArray(MENU) && MENU.length > 0 ? MENU : MENU_ITEMS;
  const prod = menuData.find(m => Number(m.id) === Number(itemId));
  if (!prod) return;

  const maxStock = typeof prod.stock === 'number' ? prod.stock : 0;
  if (maxStock <= 0) {
    showToast('Hết hàng', `Sản phẩm "${prod.name}" hiện đã hết hàng trong kho.`, 'warning');
    return;
  }

  const existing = shopCart.find(c => Number(c.id) === Number(prod.id));
  if (existing) {
    if (existing.qty + 1 > maxStock) {
      showToast('Vượt tồn kho', `Chỉ còn ${maxStock} sản phẩm "${prod.name}" trong kho.`, 'warning');
      return;
    }
    existing.qty += 1;
  } else {
    shopCart.push({
      id: Number(prod.id),
      name: prod.name,
      price: Number(prod.price) || 0,
      qty: 1
    });
  }

  renderMenu();
  renderShopCart();
}

function changeShopCartQty(itemId, delta) {
  const idx = shopCart.findIndex(c => Number(c.id) === Number(itemId));
  if (idx === -1) return;

  const menuData = Array.isArray(MENU) && MENU.length > 0 ? MENU : MENU_ITEMS;
  const prod = menuData.find(m => Number(m.id) === Number(itemId));
  const maxStock = prod && typeof prod.stock === 'number' ? prod.stock : 0;

  const nextQty = shopCart[idx].qty + delta;
  if (nextQty > maxStock) {
    showToast('Vượt tồn kho', `Kho chỉ còn tối đa ${maxStock} sản phẩm này.`, 'warning');
    return;
  }

  shopCart[idx].qty = nextQty;
  if (shopCart[idx].qty <= 0) {
    shopCart.splice(idx, 1);
  }

  renderMenu();
  renderShopCart();
}

function renderShopCart() {
  const container = document.getElementById('shopCartItems');
  const badge = document.getElementById('shopCartBadge');
  const totalEl = document.getElementById('shopCartTotal');
  if (!container) return;

  const totalCount = shopCart.reduce((s, x) => s + x.qty, 0);
  const totalAmount = shopCart.reduce((s, x) => s + x.price * x.qty, 0);

  if (badge) badge.textContent = `${totalCount} món`;
  if (totalEl) totalEl.textContent = formatMoney(totalAmount);

  if (shopCart.length === 0) {
    container.innerHTML = `
      <div class="shop-cart-empty">
        <iconify-icon icon="lucide:shopping-bag" width="22"></iconify-icon>
        <div>
          <div class="shop-cart-empty-title">Giỏ hàng chưa có sản phẩm</div>
          <div class="shop-cart-empty-sub">Bấm "+ Chọn mua" ở danh sách sản phẩm</div>
        </div>
      </div>
    `;
    return;
  }

  container.innerHTML = shopCart.map(item => `
    <div class="shop-cart-row">
      <div class="shop-cart-row-info">
        <div class="shop-cart-row-name">${escapeHtml(item.name)}</div>
        <div class="shop-cart-row-meta text-mono">${formatMoney(item.price)} × ${item.qty} = <strong>${formatMoney(item.price * item.qty)}</strong></div>
      </div>
      <div class="shop-cart-row-qty">
        <button type="button" class="shop-qty-btn" onclick="changeShopCartQty(${Number(item.id)}, -1)" aria-label="Giảm số lượng">−</button>
        <span class="shop-qty-val text-mono">${item.qty}</span>
        <button type="button" class="shop-qty-btn" onclick="changeShopCartQty(${Number(item.id)}, 1)" aria-label="Tăng số lượng">+</button>
      </div>
    </div>
  `).join('');
}

async function submitShopOrder() {
  if (isSubmittingShopOrder) return;
  if (shopCart.length === 0) {
    showToast('Giỏ hàng trống', 'Vui lòng chọn ít nhất 1 sản phẩm hoặc dịch vụ.', 'warning');
    return;
  }

  const customerName = (document.getElementById('shopOrderName')?.value || '').trim();
  const phone = (document.getElementById('shopOrderPhone')?.value || '').trim();
  const note = (document.getElementById('shopOrderNote')?.value || '').trim() || 'Nhận tại quầy Pro Shop';
  const selectVal = document.getElementById('shopOrderBooking')?.value || '';

  let bookingCode = '';
  let bookingAccessToken = '';

  if (selectVal === '__GUEST_ATTACH__') {
    bookingCode = (document.getElementById('shopGuestBookingCode')?.value || '').trim().toUpperCase();
    bookingAccessToken = (document.getElementById('shopGuestAccessToken')?.value || '').trim();
    if (!bookingCode || !bookingAccessToken) {
      showToast('Thiếu mã xác thực sân', 'Khách lẻ muốn gộp đơn vào sân cần nhập đủ Mã đặt sân (PB-...) và Mã bảo mật vé (bk_...).', 'warning');
      return;
    }
  } else if (selectVal) {
    bookingCode = selectVal;
  }

  if (!customerName || customerName.length < 2) {
    showToast('Thiếu thông tin người nhận', 'Vui lòng nhập Họ tên hợp lệ (tối thiểu 2 ký tự).', 'warning');
    return;
  }

  if (!phone || !/^(0|\+84)[0-9]{8,10}$/.test(phone.replace(/[\s.-]/g, ''))) {
    showToast('Số điện thoại không hợp lệ', 'Vui lòng nhập đúng số điện thoại 10 chữ số Việt Nam.', 'warning');
    return;
  }

  const idempotencyKey = generateIdempotencyKey('ORD');
  isSubmittingShopOrder = true;
  const btn = document.getElementById('btnSubmitShopOrder');
  if (btn) btn.disabled = true;

  try {
    const res = await apiFetch('/api/orders/create', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        customerName,
        phone,
        address: note,
        bookingCode,
        bookingAccessToken,
        idempotencyKey,
        items: shopCart.map(i => ({ productId: i.id, qty: i.qty }))
      })
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      showToast('Không thể tạo đơn', data.error || 'Tồn kho vừa thay đổi hoặc vé sân chưa hợp lệ.', 'error');
      await syncDatabaseFromServer(true);
      renderMenu();
      return;
    }

    document.getElementById('ordSuccessCode').textContent = data.orderCode;
    document.getElementById('ordSuccessName').textContent = `${customerName} (${phone})`;
    document.getElementById('ordSuccessDelivery').textContent = data.bookingCode
      ? `Giao tận sân (Đã gộp vào vé ${data.bookingCode})`
      : `${note} (Đơn lẻ — Thu tiền tại quầy)`;
    document.getElementById('ordSuccessTotal').textContent = formatMoney(data.totalAmount);

    shopCart = [];
    await syncDatabaseFromServer(true);
    renderMenu();
    renderShopCart();
    openModal('modalShopOrder');
    showToast('Đặt dịch vụ thành công!', `Mã hóa đơn ${data.orderCode} đã được ghi nhận.`, 'success');
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể kết nối tới máy chủ, vui lòng thử lại.', 'error');
  } finally {
    isSubmittingShopOrder = false;
    if (btn) btn.disabled = false;
  }
}

document.addEventListener('DOMContentLoaded', () => {
  autoFillShopCustomer();
  renderMenu();
  renderShopCart();
});
window.addEventListener('pickleball:db-synced', () => {
  autoFillShopCustomer();
  renderMenu();
});
