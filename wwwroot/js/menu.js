/* ==========================================================================
   PICKLEBALL PRO ARENA — PRO SHOP, F&B & COURT-SIDE ORDERING CONTROLLER
   Category Filtering, Search, Interactive Cart & Direct Booking Tab Integration
   ========================================================================== */

let activeMenuCategory = 'all';
let shopCart = [];

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

function autoFillShopCustomer() {
  if (CURRENT_USER) {
    const nameEl = document.getElementById('shopOrderName');
    const phoneEl = document.getElementById('shopOrderPhone');
    if (nameEl && !nameEl.value) nameEl.value = CURRENT_USER.name || '';
    if (phoneEl && !phoneEl.value) phoneEl.value = CURRENT_USER.phone || '';
  }

  // Populate active bookings dropdown so customer can attach F&B directly to their court
  const bookingSelect = document.getElementById('shopOrderBooking');
  if (!bookingSelect) return;

  const activeBookings = BOOKINGS.filter(b => {
    if (!['pending', 'approved', 'playing'].includes(b.status)) return false;
    if (!CURRENT_USER || CURRENT_USER.role === 'admin') return true;
    return (b.userId && Number(b.userId) === Number(CURRENT_USER.id)) ||
           (CURRENT_USER.phone && b.phone === CURRENT_USER.phone);
  });

  const currentVal = bookingSelect.value;
  bookingSelect.innerHTML = `<option value="">-- Mua lẻ / Nhận trực tiếp tại quầy --</option>` +
    activeBookings.map(b => {
      const court = getCourtById(b.courtId);
      return `<option value="${b.id}">Gộp vào vé ${b.id} (${court.name} · ${b.time})</option>`;
    }).join('');
  if (currentVal && activeBookings.some(b => b.id === currentVal)) {
    bookingSelect.value = currentVal;
  }
}

function renderMenu() {
  const grid = document.getElementById('menuGrid');
  const countEl = document.getElementById('menuCount');
  if (countEl) countEl.textContent = MENU.length;
  if (!grid) return;

  const isAdmin = CURRENT_USER && CURRENT_USER.role === 'admin';
  const addCard = document.getElementById('addMenuItemCard');
  if (addCard) addCard.style.display = isAdmin ? '' : 'none';

  const keyword = (document.getElementById('menuSearchInput')?.value || '').trim().toLowerCase();

  const filtered = MENU.filter(item => {
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

    return `
      <div class="menu-item ${inCart ? 'is-selected' : ''}">
        <div class="menu-item-top">
          <div class="menu-item-icon">${item.icon}</div>
          <div class="menu-item-top-right">
            <span class="menu-item-tag">${typeLabel}</span>
            ${isAdmin ? `<button type="button" class="menu-item-del-btn" onclick="deleteMenuItem(${item.id})" title="Xóa mặt hàng"><iconify-icon icon="lucide:trash-2" width="14"></iconify-icon></button>` : ''}
          </div>
        </div>
        <div class="menu-item-body">
          <div class="menu-item-name">${item.name}</div>
        </div>
        <div class="menu-item-footer">
          <div class="menu-item-price text-mono">${formatMoney(item.price)}</div>
          <button type="button" class="btn ${inCart ? 'btn-primary' : 'btn-outline'} btn-xs menu-item-cta" onclick="addToShopCart(${item.id})">
            <iconify-icon icon="${inCart ? 'lucide:check' : 'lucide:plus'}" width="14"></iconify-icon>
            <span>${inCart ? `Đã chọn (${inCart.qty})` : 'Chọn mua'}</span>
          </button>
        </div>
      </div>
    `;
  }).join('');
}

function addToShopCart(itemId) {
  const prod = MENU.find(m => Number(m.id) === Number(itemId));
  if (!prod) return;

  const existing = shopCart.find(c => Number(c.id) === Number(prod.id));
  if (existing) {
    existing.qty += 1;
  } else {
    shopCart.push({
      id: prod.id,
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

  shopCart[idx].qty += delta;
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
        <div class="shop-cart-row-name">${item.name}</div>
        <div class="shop-cart-row-meta text-mono">${formatMoney(item.price)} × ${item.qty} = <strong>${formatMoney(item.price * item.qty)}</strong></div>
      </div>
      <div class="shop-cart-row-qty">
        <button type="button" class="shop-qty-btn" onclick="changeShopCartQty(${item.id}, -1)" aria-label="Giảm số lượng">−</button>
        <span class="shop-qty-val text-mono">${item.qty}</span>
        <button type="button" class="shop-qty-btn" onclick="changeShopCartQty(${item.id}, 1)" aria-label="Tăng số lượng">+</button>
      </div>
    </div>
  `).join('');
}

async function submitShopOrder() {
  if (shopCart.length === 0) {
    showToast('Giỏ hàng trống', 'Vui lòng chọn ít nhất 1 sản phẩm hoặc dịch vụ.', 'warning');
    return;
  }

  const customerName = (document.getElementById('shopOrderName')?.value || '').trim();
  const phone = (document.getElementById('shopOrderPhone')?.value || '').trim();
  const note = (document.getElementById('shopOrderNote')?.value || '').trim() || 'Nhận tại quầy Pro Shop';
  const bookingCode = document.getElementById('shopOrderBooking')?.value || '';

  if (!customerName || !phone) {
    showToast('Thiếu thông tin người nhận', 'Vui lòng nhập Họ tên và Số điện thoại.', 'warning');
    return;
  }

  try {
    const res = await fetch('/api/orders/create', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        customerName,
        phone,
        address: note,
        bookingCode,
        items: shopCart
      })
    });
    const data = await res.json();
    if (!res.ok || !data.ok) {
      showToast('Không thể tạo đơn', data.error || 'Đã xảy ra lỗi khi lưu đơn hàng.', 'danger');
      return;
    }

    document.getElementById('ordSuccessCode').textContent = data.orderCode;
    document.getElementById('ordSuccessName').textContent = `${customerName} (${phone})`;
    document.getElementById('ordSuccessDelivery').textContent = bookingCode
      ? `Giao tận sân (Gộp vào vé ${bookingCode})`
      : note;
    document.getElementById('ordSuccessTotal').textContent = formatMoney(data.totalAmount);

    shopCart = [];
    renderMenu();
    renderShopCart();
    openModal('modalShopOrder');
    showToast('Đặt dịch vụ thành công!', `Mã hóa đơn ${data.orderCode} đã được ghi nhận.`, 'success');

    // Đồng bộ lại dữ liệu từ SQL Server nếu đơn được gộp vào vé đặt sân
    SpikeDB.syncFromSQLServer();
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể kết nối tới máy chủ, vui lòng thử lại.', 'danger');
  }
}

function addMenuItem() {
  if (!CURRENT_USER || CURRENT_USER.role !== 'admin') {
    showToast('Từ chối truy cập', 'Chỉ có Quản trị viên mới được thêm mặt hàng mới.', 'warning');
    return;
  }
  const name = (document.getElementById('menuNewName')?.value || '').trim();
  const price = parseInt(document.getElementById('menuNewPrice')?.value);
  const type = document.getElementById('menuNewType')?.value || 'drink';

  if (!name || !price || price <= 0) {
    showToast('Thiếu thông tin', 'Vui lòng nhập tên sản phẩm và đơn giá hợp lệ (> 0 đ).', 'warning');
    return;
  }

  const icons = {
    drink: '<iconify-icon icon="mdi:cup-water" width="24"></iconify-icon>',
    food: '<iconify-icon icon="mdi:noodles" width="24"></iconify-icon>',
    other: '<iconify-icon icon="mdi:table-tennis" width="24"></iconify-icon>'
  };

  MENU.push({ id: Date.now(), name, price, type, icon: icons[type] || icons.drink });
  saveData();

  document.getElementById('menuNewName').value = '';
  document.getElementById('menuNewPrice').value = '';
  showToast('Đã thêm thành công', `"${name}" (${formatMoney(price)}) đã được cập nhật vào Menu & POS.`, 'success');
  renderMenu();
}

function deleteMenuItem(id) {
  if (!CURRENT_USER || CURRENT_USER.role !== 'admin') {
    showToast('Từ chối truy cập', 'Chỉ có Quản trị viên mới được xóa mặt hàng.', 'warning');
    return;
  }
  const item = MENU.find(m => Number(m.id) === Number(id));
  if (!item) return;
  MENU = MENU.filter(m => Number(m.id) !== Number(id));
  saveData();
  showToast('Đã xóa mặt hàng', `"${item.name}" đã được xóa khỏi thực đơn.`, 'info');
  renderMenu();
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
