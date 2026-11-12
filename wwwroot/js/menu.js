/* ==========================================================================
   SPIKE PICKLEBALL CLUB — PRO SHOP & SERVICE ORDERING CONTROLLER (V6)
   Real Product Packshots · Keyed DOM Reconciliation · Tactile Microinteractions
   ========================================================================== */

let activeCategory = 'all';
let menuSearchKeyword = '';
let quickShopCart = []; // [{ id, qty }]
let menuGridDelegationBound = false;

function prefersReducedMotion() {
  return Boolean(
    typeof window !== 'undefined' &&
    window.matchMedia &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches
  );
}

function getCategoryPlaceholderUrl(type) {
  const normalized = String(type || '').trim().toLowerCase();
  if (normalized === 'drink') return '/assets/products/placeholder-drink.svg';
  if (normalized === 'food') return '/assets/products/placeholder-food.svg';
  if (normalized === 'other') return '/assets/products/placeholder-other.svg';
  return '/assets/products/placeholder-default.svg';
}

function resolveProductImageUrl(item) {
  if (!item) return '/assets/products/placeholder-default.svg';
  const candidate = String(item.imageUrl || item.image || '').trim();
  if (
    candidate.startsWith('/uploads/products/') ||
    candidate.startsWith('/assets/products/')
  ) {
    return candidate;
  }
  return getCategoryPlaceholderUrl(item.type);
}

function getProductCategoryLabel(type) {
  if (type === 'drink') return 'Đồ uống';
  if (type === 'food') return 'Đồ ăn';
  return 'Phụ kiện';
}

function getProductDescriptionText(item) {
  const customDesc = String(item?.description || '').trim();
  if (customDesc) return customDesc;
  if (item?.type === 'drink') return 'Phục vụ ướp lạnh tức thì tại sân';
  if (item?.type === 'food') return 'Chế biến nóng · Giao nhanh tận sân';
  return 'Phụ kiện thi đấu chuẩn USAPA';
}

function getProductCardState(item) {
  const stock = typeof item.stock === 'number' ? item.stock : 0;
  const outOfStock = stock <= 0;
  const inCartRow = quickShopCart.find(r => Number(r.id) === Number(item.id));
  const inCartQty = inCartRow ? Number(inCartRow.qty) || 0 : 0;
  const atMaxStock = !outOfStock && inCartQty >= stock;

  let stockBadgeClass = 'is-ok';
  let stockBadgeText = `Còn ${stock}`;
  if (outOfStock) {
    stockBadgeClass = 'is-empty';
    stockBadgeText = 'Hết hàng';
  } else if (stock <= 5) {
    stockBadgeClass = 'is-low';
    stockBadgeText = `Sắp hết · ${stock}`;
  }

  let ctaIcon = 'lucide:shopping-bag';
  let ctaLabel = 'Chọn mua';
  if (outOfStock) {
    ctaIcon = 'lucide:ban';
    ctaLabel = 'Tạm hết';
  } else if (atMaxStock && inCartQty > 0) {
    ctaIcon = 'lucide:check-check';
    ctaLabel = `Đã chọn tối đa (${inCartQty})`;
  } else if (inCartQty > 0) {
    ctaIcon = 'lucide:check';
    ctaLabel = `Đã chọn (${inCartQty})`;
  }

  return {
    stock,
    outOfStock,
    inCartQty,
    atMaxStock,
    stockBadgeClass,
    stockBadgeText,
    ctaIcon,
    ctaLabel,
    imgUrl: resolveProductImageUrl(item),
    catLabel: getProductCategoryLabel(item.type),
    descText: getProductDescriptionText(item)
  };
}

function buildMenuCardHtml(item) {
  const st = getProductCardState(item);
  const idNum = Number(item.id);
  const safeName = escapeHtml(item.name || 'Sản phẩm Pro Shop');

  return `
    <article class="menu-item ${st.inCartQty > 0 ? 'is-selected' : ''} ${st.outOfStock ? 'is-out-of-stock' : ''}"
             data-product-id="${idNum}"
             data-category="${escapeHtml(item.type || 'drink')}">
      <div class="menu-item-media" data-role="media">
        <img class="menu-item-img"
             data-role="img"
             data-raw-src="${escapeHtml(st.imgUrl)}"
             src="${escapeHtml(st.imgUrl)}"
             alt="${safeName}"
             width="240"
             height="240"
             loading="lazy"
             decoding="async"
             onerror="this.onerror=null;this.src='/assets/products/placeholder-default.svg';" />
        <span class="menu-item-cat-pill" data-role="cat-pill">${escapeHtml(st.catLabel)}</span>
        <span class="menu-item-stock-badge ${st.stockBadgeClass}" data-role="stock-badge">${escapeHtml(st.stockBadgeText)}</span>
        <span class="menu-item-selected-badge ${st.inCartQty > 0 ? 'is-visible' : ''}"
              data-role="selected-badge"
              aria-hidden="${st.inCartQty > 0 ? 'false' : 'true'}">
          <iconify-icon icon="lucide:check-circle-2" width="13"></iconify-icon>
          <span data-role="selected-qty">${st.inCartQty}</span>
        </span>
      </div>

      <div class="menu-item-body">
        <h3 class="menu-item-name" data-role="name" title="${safeName}">${safeName}</h3>
        <p class="menu-item-desc" data-role="desc">${escapeHtml(st.descText)}</p>
      </div>

      <div class="menu-item-footer">
        <div class="menu-item-price-block">
          <span class="menu-item-price-label">Đơn giá</span>
          <span class="menu-item-price text-mono" data-role="price">${formatMoney(item.price)}</span>
        </div>

        <div class="menu-item-actions" data-role="actions">
          <div class="menu-item-stepper ${st.inCartQty > 0 && !st.outOfStock ? 'is-visible' : ''}"
               data-role="stepper"
               aria-label="Điều chỉnh số lượng ${safeName}">
            <button type="button"
                    class="menu-stepper-btn"
                    data-action="dec"
                    data-product-id="${idNum}"
                    aria-label="Giảm số lượng ${safeName}">−</button>
            <span class="menu-stepper-val text-mono" data-role="stepper-qty">${st.inCartQty}</span>
            <button type="button"
                    class="menu-stepper-btn"
                    data-action="inc"
                    data-product-id="${idNum}"
                    ${st.atMaxStock ? 'disabled' : ''}
                    aria-label="Tăng số lượng ${safeName}">+</button>
          </div>

          <button type="button"
                  class="menu-item-cta ${st.inCartQty > 0 ? 'is-added' : ''}"
                  data-role="cta"
                  data-action="buy"
                  data-product-id="${idNum}"
                  ${st.outOfStock || st.atMaxStock ? 'disabled' : ''}>
            <iconify-icon data-role="cta-icon" icon="${st.ctaIcon}" width="15"></iconify-icon>
            <span data-role="cta-label">${escapeHtml(st.ctaLabel)}</span>
          </button>
        </div>
      </div>
    </article>
  `;
}

function updateMenuCardInPlace(cardEl, item) {
  if (!cardEl || !item) return;
  const st = getProductCardState(item);

  cardEl.classList.toggle('is-selected', st.inCartQty > 0);
  cardEl.classList.toggle('is-out-of-stock', st.outOfStock);
  cardEl.setAttribute('data-category', item.type || 'drink');

  const imgEl = cardEl.querySelector('[data-role="img"]');
  if (imgEl) {
    const prevRawSrc = imgEl.getAttribute('data-raw-src') || '';
    if (prevRawSrc !== st.imgUrl) {
      imgEl.setAttribute('data-raw-src', st.imgUrl);
      imgEl.src = st.imgUrl;
    }
    if (imgEl.alt !== (item.name || 'Sản phẩm Pro Shop')) {
      imgEl.alt = item.name || 'Sản phẩm Pro Shop';
    }
  }

  const catPillEl = cardEl.querySelector('[data-role="cat-pill"]');
  if (catPillEl && catPillEl.textContent !== st.catLabel) {
    catPillEl.textContent = st.catLabel;
  }

  const stockBadgeEl = cardEl.querySelector('[data-role="stock-badge"]');
  if (stockBadgeEl) {
    stockBadgeEl.className = `menu-item-stock-badge ${st.stockBadgeClass}`;
    if (stockBadgeEl.textContent !== st.stockBadgeText) {
      stockBadgeEl.textContent = st.stockBadgeText;
    }
  }

  const selectedBadgeEl = cardEl.querySelector('[data-role="selected-badge"]');
  const selectedQtyEl = cardEl.querySelector('[data-role="selected-qty"]');
  if (selectedBadgeEl) {
    selectedBadgeEl.classList.toggle('is-visible', st.inCartQty > 0);
    selectedBadgeEl.setAttribute('aria-hidden', st.inCartQty > 0 ? 'false' : 'true');
  }
  if (selectedQtyEl && selectedQtyEl.textContent !== String(st.inCartQty)) {
    selectedQtyEl.textContent = String(st.inCartQty);
  }

  const nameEl = cardEl.querySelector('[data-role="name"]');
  if (nameEl && nameEl.textContent !== item.name) {
    nameEl.textContent = item.name;
    nameEl.title = item.name;
  }

  const descEl = cardEl.querySelector('[data-role="desc"]');
  if (descEl && descEl.textContent !== st.descText) {
    descEl.textContent = st.descText;
  }

  const priceEl = cardEl.querySelector('[data-role="price"]');
  const formattedPrice = formatMoney(item.price);
  if (priceEl && priceEl.textContent !== formattedPrice) {
    priceEl.textContent = formattedPrice;
  }

  const stepperEl = cardEl.querySelector('[data-role="stepper"]');
  const stepperQtyEl = cardEl.querySelector('[data-role="stepper-qty"]');
  const incBtn = cardEl.querySelector('[data-action="inc"]');
  if (stepperEl) {
    stepperEl.classList.toggle('is-visible', st.inCartQty > 0 && !st.outOfStock);
  }
  if (stepperQtyEl && stepperQtyEl.textContent !== String(st.inCartQty)) {
    stepperQtyEl.textContent = String(st.inCartQty);
  }
  if (incBtn) {
    incBtn.disabled = st.atMaxStock;
  }

  const ctaBtn = cardEl.querySelector('[data-role="cta"]');
  const ctaIconEl = cardEl.querySelector('[data-role="cta-icon"]');
  const ctaLabelEl = cardEl.querySelector('[data-role="cta-label"]');
  if (ctaBtn) {
    ctaBtn.classList.toggle('is-added', st.inCartQty > 0);
    ctaBtn.disabled = st.outOfStock || st.atMaxStock;
  }
  if (ctaIconEl && ctaIconEl.getAttribute('icon') !== st.ctaIcon) {
    ctaIconEl.setAttribute('icon', st.ctaIcon);
  }
  if (ctaLabelEl && ctaLabelEl.textContent !== st.ctaLabel) {
    ctaLabelEl.textContent = st.ctaLabel;
  }
}

function ensureMenuGridDelegation() {
  const grid = document.getElementById('menuGrid');
  if (!grid || menuGridDelegationBound) return;
  menuGridDelegationBound = true;

  grid.addEventListener('click', (evt) => {
    const actionBtn = evt.target.closest('[data-action][data-product-id]');
    if (!actionBtn || actionBtn.disabled) return;

    const productId = Number(actionBtn.getAttribute('data-product-id'));
    const action = actionBtn.getAttribute('data-action');
    if (!productId) return;

    if (action === 'buy') {
      quickBuyItem(productId);
    } else if (action === 'inc') {
      changeQuickCartQty(productId, 1);
    } else if (action === 'dec') {
      changeQuickCartQty(productId, -1);
    }
  });
}

function initMenu() {
  const user = getCurrentUser();
  const nameEl = document.getElementById('shopOrderName');
  const phoneEl = document.getElementById('shopOrderPhone');
  if (user) {
    if (nameEl && !nameEl.value) nameEl.value = user.name || '';
    if (phoneEl && !phoneEl.value) phoneEl.value = user.phone || '';
  }

  const adminBar = document.getElementById('proShopAdminQuickBar');
  if (adminBar) {
    adminBar.style.display = user && user.role === 'admin' ? 'block' : 'none';
  }

  ensureMenuGridDelegation();
  populateActiveBookingOptions();
  renderMenu();
  renderQuickShopCart();
}

function populateActiveBookingOptions() {
  const sel = document.getElementById('shopOrderBooking');
  if (!sel) return;

  const currentVal = sel.value;
  const user = getCurrentUser();
  const today = getTodayStr();

  const attachable = BOOKINGS.filter(b => {
    const isPlayingOrApproved = (b.status === 'playing' || b.status === 'approved') && b.date >= today;
    if (!isPlayingOrApproved) return false;
    if (!user) return false;
    if (user.role === 'admin' || user.role === 'staff') return true;
    return Number(b.accountId) === Number(user.id) || (user.phone && b.phone === user.phone);
  });

  let html = `<option value="">-- Mua lẻ / Nhận trực tiếp tại quầy --</option>`;
  html += attachable.map(b => {
    const courtName = getCourtDisplayName(b.courtId, b.courtName);
    return `<option value="${escapeHtml(b.id)}">Gộp vào ${escapeHtml(courtName)} · ${escapeHtml(b.time)} (${escapeHtml(b.id)})</option>`;
  }).join('');

  if (!user) {
    html += `<option value="__GUEST_BOOKING__">Nhập mã đặt sân & mã vé bảo mật (Khách vãng lai)...</option>`;
  }

  sel.innerHTML = html;
  if ([...sel.options].some(o => o.value === currentVal)) {
    sel.value = currentVal;
  }
  toggleGuestBookingAttach();
}

function toggleGuestBookingAttach() {
  const sel = document.getElementById('shopOrderBooking');
  const box = document.getElementById('guestBookingAttachFields');
  if (!sel || !box) return;
  box.style.display = sel.value === '__GUEST_BOOKING__' ? 'block' : 'none';
}

function filterMenuCategory(cat) {
  activeCategory = cat || 'all';
  document.querySelectorAll('#menuCategoryTabs .filter-tab').forEach(btn => {
    const isMatch = (btn.getAttribute('data-cat') || 'all') === activeCategory;
    btn.classList.toggle('active', isMatch);
    btn.setAttribute('aria-selected', isMatch ? 'true' : 'false');
  });
  renderMenu();
}

function filterMenu(cat, btnEl) {
  activeCategory = cat || 'all';
  document.querySelectorAll('#menuCategoryTabs .filter-tab').forEach(btn => {
    const isMatch = btnEl ? btn === btnEl : (btn.getAttribute('data-cat') || 'all') === activeCategory;
    btn.classList.toggle('active', isMatch);
    btn.setAttribute('aria-selected', isMatch ? 'true' : 'false');
  });
  renderMenu();
}

function searchMenu(val) {
  menuSearchKeyword = (val || '').trim().toLowerCase();
  renderMenu();
}

function resetMenuFilters() {
  activeCategory = 'all';
  menuSearchKeyword = '';
  const searchInput = document.getElementById('menuSearchInput');
  if (searchInput) searchInput.value = '';
  const tabs = document.querySelectorAll('#menuCategoryTabs .filter-tab');
  tabs.forEach((btn, idx) => {
    const isAll = idx === 0;
    btn.classList.toggle('active', isAll);
    btn.setAttribute('aria-selected', isAll ? 'true' : 'false');
  });
  renderMenu();
}

function renderMenu() {
  const grid = document.getElementById('menuGrid');
  if (!grid) return;

  ensureMenuGridDelegation();

  if (!HAS_SERVER_STATE) {
    const syncState = (typeof SPIKE_SYNC_STATE !== 'undefined' && (SPIKE_SYNC_STATE === 'offline' || SPIKE_SYNC_STATE === 'error'))
      ? SPIKE_SYNC_STATE
      : 'loading';
    grid.innerHTML = `<div style="grid-column:1/-1">${renderUiStateBanner(syncState, {
      title: syncState === 'loading' ? 'Đang đồng bộ danh mục Pro Shop...' : undefined,
      subtitle: syncState === 'loading' ? 'Đang tải tồn kho và bảng giá dịch vụ trực tiếp từ máy chủ.' : (typeof LAST_SYNC_ERROR !== 'undefined' ? LAST_SYNC_ERROR : undefined),
      onRetry: 'retryServerSync()'
    })}</div>`;
    return;
  }

  const menuData = Array.isArray(MENU) ? MENU : [];
  const countEl = document.getElementById('menuCount') || document.getElementById('menuAllCount');
  if (countEl) {
    countEl.textContent = String(menuData.length);
  }
  const searchInputEl = document.getElementById('menuSearchInput');
  if (searchInputEl) {
    menuSearchKeyword = (searchInputEl.value || '').trim().toLowerCase();
  }
  let items = activeCategory === 'all' ? menuData : menuData.filter(m => m.type === activeCategory);
  if (menuSearchKeyword) {
    items = items.filter(m => String(m.name || '').toLowerCase().includes(menuSearchKeyword));
  }

  if (items.length === 0) {
    const isFiltered = activeCategory !== 'all' || Boolean(menuSearchKeyword);
    if (isFiltered && menuData.length > 0) {
      grid.innerHTML = `
        <div style="grid-column:1/-1">${renderUiStateBanner('filtered-empty', {
          title: 'Không tìm thấy mặt hàng phù hợp bộ lọc',
          subtitle: menuSearchKeyword
            ? `Không có sản phẩm nào khớp từ khóa "${menuSearchKeyword}".`
            : 'Danh mục đang chọn tạm thời chưa có sản phẩm.',
          onReset: 'resetMenuFilters()',
          resetLabel: 'Xóa bộ lọc & Xem tất cả'
        })}</div>`;
    } else {
      grid.innerHTML = `
        <div style="grid-column:1/-1">${renderUiStateBanner('empty', {
          title: 'Danh mục Pro Shop đang cập nhật',
          subtitle: 'Hiện chưa có mặt hàng nào mở bán trên hệ thống.'
        })}</div>`;
    }
    return;
  }

  // Keyed DOM Reconciliation: if the rendered card sequence matches `items`, update in place!
  const existingCards = Array.from(grid.querySelectorAll(':scope > .menu-item[data-product-id]'));
  const isSameCardSequence =
    existingCards.length === items.length &&
    existingCards.every((el, idx) => Number(el.getAttribute('data-product-id')) === Number(items[idx].id));

  if (isSameCardSequence) {
    for (let i = 0; i < items.length; i++) {
      updateMenuCardInPlace(existingCards[i], items[i]);
    }
    return;
  }

  grid.innerHTML = items.map(item => buildMenuCardHtml(item)).join('');
}

function animateCardSelect(productId, delta = 1) {
  const cardEl = document.querySelector(`#menuGrid .menu-item[data-product-id="${Number(productId)}"]`);
  if (!cardEl) return;

  cardEl.classList.remove('is-just-pulsed');
  void cardEl.offsetWidth;
  cardEl.classList.add('is-just-pulsed');

  if (prefersReducedMotion()) return;

  if (typeof cardEl.animate === 'function') {
    cardEl.animate(
      [
        { transform: 'scale(1)' },
        { transform: delta > 0 ? 'scale(1.022)' : 'scale(0.99)' },
        { transform: 'scale(1)' }
      ],
      { duration: 220, easing: 'cubic-bezier(0.22, 1, 0.36, 1)' }
    );
  }

  const qtyTargets = cardEl.querySelectorAll('[data-role="selected-qty"], [data-role="stepper-qty"]');
  qtyTargets.forEach(el => {
    if (typeof el.animate === 'function') {
      el.animate(
        [
          { transform: 'scale(1)' },
          { transform: 'scale(1.28)' },
          { transform: 'scale(1)' }
        ],
        { duration: 200, easing: 'cubic-bezier(0.22, 1, 0.36, 1)' }
      );
    }
  });
}

function animateCartBump() {
  const targets = [
    document.getElementById('shopCartBadge'),
    document.getElementById('shopCartTotal'),
    document.getElementById('mobileCartCount'),
    document.getElementById('mobileCartTotal')
  ].filter(Boolean);

  targets.forEach(el => {
    el.classList.remove('is-bumping');
    void el.offsetWidth;
    el.classList.add('is-bumping');

    if (!prefersReducedMotion() && typeof el.animate === 'function') {
      el.animate(
        [
          { transform: 'scale(1)' },
          { transform: 'scale(1.16)' },
          { transform: 'scale(1)' }
        ],
        { duration: 210, easing: 'cubic-bezier(0.22, 1, 0.36, 1)' }
      );
    }
  });
}

function animateFlyToCart(productId) {
  if (prefersReducedMotion()) return;
  if (typeof window === 'undefined' || window.innerWidth < 768) return;

  const cardImg = document.querySelector(`#menuGrid .menu-item[data-product-id="${Number(productId)}"] [data-role="img"]`);
  const cartBadge = document.getElementById('shopCartBadge');
  if (!cardImg || !cartBadge) return;

  const startRect = cardImg.getBoundingClientRect();
  const endRect = cartBadge.getBoundingClientRect();
  if (startRect.width <= 0 || endRect.width <= 0) return;

  const flyer = document.createElement('img');
  flyer.src = cardImg.currentSrc || cardImg.src;
  flyer.alt = '';
  flyer.setAttribute('aria-hidden', 'true');
  flyer.className = 'proshop-fly-clone';
  const size = 56;
  const startX = startRect.left + startRect.width / 2 - size / 2;
  const startY = startRect.top + startRect.height / 2 - size / 2;
  const endX = endRect.left + endRect.width / 2 - size / 2;
  const endY = endRect.top + endRect.height / 2 - size / 2;

  Object.assign(flyer.style, {
    position: 'fixed',
    left: `${startX}px`,
    top: `${startY}px`,
    width: `${size}px`,
    height: `${size}px`,
    objectFit: 'contain',
    padding: '6px',
    background: '#FFFFFF',
    borderRadius: '14px',
    border: '2px solid #FF5520',
    boxShadow: '0 14px 28px rgba(11, 17, 30, 0.24)',
    pointerEvents: 'none',
    zIndex: '9999'
  });

  document.body.appendChild(flyer);

  const dx = endX - startX;
  const dy = endY - startY;

  let cleaned = false;
  const cleanup = () => {
    if (cleaned) return;
    cleaned = true;
    if (flyer.parentNode) flyer.parentNode.removeChild(flyer);
  };

  const safetyTimer = setTimeout(cleanup, 520);

  if (typeof flyer.animate === 'function') {
    const anim = flyer.animate(
      [
        { transform: 'translate3d(0, 0, 0) scale(1)', opacity: 0.96 },
        { transform: `translate3d(${dx * 0.55}px, ${dy * 0.45 - 36}px, 0) scale(0.76)`, opacity: 0.9 },
        { transform: `translate3d(${dx}px, ${dy}px, 0) scale(0.32)`, opacity: 0.15 }
      ],
      { duration: 400, easing: 'cubic-bezier(0.22, 1, 0.36, 1)' }
    );
    anim.onfinish = () => {
      clearTimeout(safetyTimer);
      cleanup();
    };
    anim.oncancel = () => {
      clearTimeout(safetyTimer);
      cleanup();
    };
  } else {
    clearTimeout(safetyTimer);
    cleanup();
  }
}

function quickBuyItem(itemId) {
  const item = (MENU || []).find(m => Number(m.id) === Number(itemId));
  if (!item) return;

  const stock = typeof item.stock === 'number' ? item.stock : 0;
  const existing = quickShopCart.find(r => Number(r.id) === Number(itemId));
  const curQty = existing ? existing.qty : 0;

  if (curQty + 1 > stock) {
    showToast('Vượt quá tồn kho', `Mặt hàng "${item.name}" chỉ còn ${stock} sản phẩm trong kho.`, 'warning');
    return;
  }

  if (existing) {
    existing.qty += 1;
  } else {
    quickShopCart.push({ id: Number(itemId), qty: 1 });
  }

  renderMenu();
  renderQuickShopCart();
  animateCardSelect(itemId, 1);
  animateFlyToCart(itemId);
  animateCartBump();
}

function changeQuickCartQty(itemId, delta) {
  const idx = quickShopCart.findIndex(r => Number(r.id) === Number(itemId));
  if (idx === -1) {
    if (delta > 0) quickBuyItem(itemId);
    return;
  }

  const item = (MENU || []).find(m => Number(m.id) === Number(itemId));
  const stock = item && typeof item.stock === 'number' ? item.stock : 0;
  const nextQty = quickShopCart[idx].qty + delta;

  if (nextQty <= 0) {
    quickShopCart.splice(idx, 1);
  } else if (nextQty > stock) {
    showToast('Vượt quá tồn kho', `Chỉ còn ${stock} sản phẩm trong kho.`, 'warning');
    return;
  } else {
    quickShopCart[idx].qty = nextQty;
  }

  renderMenu();
  renderQuickShopCart();
  animateCardSelect(itemId, delta);
  if (delta > 0) {
    animateFlyToCart(itemId);
  }
  animateCartBump();
}

function renderQuickShopCart() {
  const listEl = document.getElementById('shopCartItems');
  const totalEl = document.getElementById('shopCartTotal');
  const badgeEl = document.getElementById('shopCartBadge');
  const submitBtn = document.getElementById('btnSubmitShopOrder');
  if (!listEl || !totalEl) return;

  const menuData = Array.isArray(MENU) ? MENU : [];
  let total = 0;
  let totalItems = 0;

  const validRows = quickShopCart
    .map(row => {
      const item = menuData.find(m => Number(m.id) === Number(row.id));
      if (!item) return null;
      const lineTotal = (Number(item.price) || 0) * row.qty;
      total += lineTotal;
      totalItems += row.qty;
      return { ...row, item, lineTotal, imgUrl: resolveProductImageUrl(item) };
    })
    .filter(Boolean);

  if (badgeEl) badgeEl.textContent = `${totalItems} món`;
  totalEl.textContent = formatMoney(total);

  // Sync sticky mobile cart bar (UI-B05)
  const mobileCountEl = document.getElementById('mobileCartCount');
  const mobileTotalEl = document.getElementById('mobileCartTotal');
  const mobileActionBtn = document.getElementById('mobileCartActionBtn');
  if (mobileCountEl) mobileCountEl.textContent = `${totalItems} món đã chọn`;
  if (mobileTotalEl) mobileTotalEl.textContent = formatMoney(total);
  if (mobileActionBtn) {
    mobileActionBtn.innerHTML = totalItems > 0
      ? `<iconify-icon icon="lucide:shopping-bag" width="16"></iconify-icon><span>Xem giỏ (${totalItems}) & Đặt ngay</span>`
      : `<iconify-icon icon="lucide:shopping-bag" width="16"></iconify-icon><span>Xem giỏ hàng</span>`;
  }

  if (validRows.length === 0) {
    listEl.innerHTML = `
      <div class="empty-state" style="padding:1.25rem 0.75rem;">
        <div class="empty-state-icon"><iconify-icon icon="lucide:shopping-bag" width="26"></iconify-icon></div>
        <div class="empty-state-title" style="font-size:0.9rem;">Giỏ hàng dịch vụ đang trống</div>
        <div class="empty-state-text" style="font-size:0.78rem;">Bấm "Chọn mua" trên sản phẩm bên trái để thêm nước uống, đồ ăn hoặc phụ kiện giao tận sân.</div>
      </div>
    `;
    if (submitBtn) submitBtn.disabled = true;
    return;
  }

  if (submitBtn) submitBtn.disabled = false;

  listEl.innerHTML = validRows.map(r => `
    <div class="shop-cart-row" data-cart-product-id="${r.id}">
      <div class="shop-cart-thumb-wrap">
        <img class="shop-cart-thumb"
             src="${escapeHtml(r.imgUrl)}"
             alt="${escapeHtml(r.item.name)}"
             width="44"
             height="44"
             loading="lazy"
             decoding="async"
             onerror="this.onerror=null;this.src='/assets/products/placeholder-default.svg';" />
      </div>
      <div class="shop-cart-row-info">
        <div class="shop-cart-row-name">${escapeHtml(r.item.name)}</div>
        <div class="shop-cart-row-meta text-mono">${formatMoney(r.item.price)} × ${r.qty} = <strong>${formatMoney(r.lineTotal)}</strong></div>
      </div>
      <div class="shop-cart-row-actions">
        <button type="button" class="btn btn-outline btn-xs" aria-label="Giảm ${escapeHtml(r.item.name)}" onclick="changeQuickCartQty(${r.id}, -1)">−</button>
        <span class="shop-cart-row-qty text-mono">${r.qty}</span>
        <button type="button" class="btn btn-outline btn-xs" aria-label="Tăng ${escapeHtml(r.item.name)}" onclick="changeQuickCartQty(${r.id}, 1)">+</button>
      </div>
    </div>
  `).join('');
}

function scrollToShopCartPanel() {
  const panel = document.getElementById('shopCartPanel');
  if (!panel) return;
  panel.scrollIntoView({ behavior: prefersReducedMotion() ? 'auto' : 'smooth', block: 'start' });
}

async function submitShopOrder() {
  if (quickShopCart.length === 0) {
    showToast('Giỏ hàng trống', 'Vui lòng chọn ít nhất 1 mặt hàng trước khi đặt.', 'warning');
    return;
  }

  const customerName = (document.getElementById('shopOrderName')?.value || '').trim();
  const phone = (document.getElementById('shopOrderPhone')?.value || '').trim();
  const deliveryNote = (document.getElementById('shopOrderNote')?.value || '').trim();
  const bookingSel = document.getElementById('shopOrderBooking')?.value || '';

  let bookingCode = bookingSel;
  let bookingAccessToken = '';
  if (bookingSel === '__GUEST_BOOKING__') {
    bookingCode = (document.getElementById('shopGuestBookingCode')?.value || '').trim();
    bookingAccessToken = (document.getElementById('shopGuestAccessToken')?.value || '').trim();
    if (!bookingCode || !bookingAccessToken) {
      showToast('Thiếu mã xác thực sân', 'Vui lòng nhập đầy đủ Mã đặt sân (PB-...) và Mã vé bảo mật (bk_...) để gộp đơn vào sân.', 'warning');
      return;
    }
  }

  if (!customerName) {
    showToast('Thiếu thông tin', 'Vui lòng nhập họ tên người nhận.', 'warning');
    return;
  }

  if (phone && !isValidVietnamPhone(phone)) {
    showToast('Số điện thoại không hợp lệ', 'Vui lòng nhập đúng 10 chữ số (bắt đầu bằng 0).', 'error');
    return;
  }

  const submitBtn = document.getElementById('btnSubmitShopOrder');
  if (submitBtn) submitBtn.disabled = true;

  try {
    const idempotencyKey = (window.crypto && window.crypto.randomUUID)
      ? window.crypto.randomUUID()
      : `ord-${Date.now()}-${Math.random().toString(36).slice(2, 9)}`;

    const res = await apiFetch('/api/orders', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Idempotency-Key': idempotencyKey
      },
      body: JSON.stringify({
        customerName,
        phone,
        deliveryNote: deliveryNote || 'Nhận tại quầy Pro Shop',
        bookingCode: bookingCode || null,
        bookingAccessToken: bookingAccessToken || null,
        paymentMethod: 'cash',
        items: quickShopCart.map(r => ({ productId: r.id, quantity: r.qty }))
      })
    });

    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      showToast('Không thể đặt dịch vụ', data.error || 'Vui lòng kiểm tra lại tồn kho.', 'error');
      await syncDatabaseFromServer(true);
      renderMenu();
      renderQuickShopCart();
      return;
    }

    quickShopCart = [];
    await syncDatabaseFromServer(true);
    populateActiveBookingOptions();
    renderMenu();
    renderQuickShopCart();

    const ord = data.order;
    document.getElementById('ordSuccessCode').textContent = ord.code;
    document.getElementById('ordSuccessName').textContent = `${ord.customerName} ${ord.phone ? `(${ord.phone})` : ''}`;
    document.getElementById('ordSuccessDelivery').textContent = ord.bookingCode
      ? `Đã gộp vào công nợ đặt sân #${ord.bookingCode}`
      : `${ord.deliveryNote} (Thanh toán tại quầy)`;
    document.getElementById('ordSuccessTotal').textContent = formatMoney(ord.totalAmount);

    openModal('modalShopOrder');
    showToast('Đã ghi nhận đơn dịch vụ!', `Mã đơn: ${ord.code} · Tổng: ${formatMoney(ord.totalAmount)}`, 'success');
  } catch (_) {
    showToast('Lỗi kết nối', 'Không thể gửi đơn hàng lúc này.', 'error');
  } finally {
    if (submitBtn) submitBtn.disabled = quickShopCart.length === 0;
  }
}

document.addEventListener('DOMContentLoaded', () => {
  initMenu();
});

window.addEventListener('pickleball:db-synced', () => {
  initMenu();
});

window.addEventListener('pickleball:sync-status', () => {
  renderMenu();
});

window.addEventListener('spike:state-synced', () => {
  populateActiveBookingOptions();
  renderMenu();
  renderQuickShopCart();
});
