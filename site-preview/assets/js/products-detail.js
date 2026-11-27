/* ==========================================================================
   SPIKE OS — PRODUCT CATALOG & DETAIL INSPECTOR CONTROLLER (/products/)
   ========================================================================== */

(function () {
  let detailQty = 1;
  let activeCat = 'all';

  function renderProductsPage() {
    if (!HAS_SERVER_STATE) return;
    const raw = SpikeDemo.getRawState();
    const products = raw?.products || [];
    const params = new URLSearchParams(window.location.search);
    const selectedId = Number(params.get('id') || products[0]?.id || 1);
    const product = products.find(p => Number(p.id) === selectedId) || products[0];

    if (product) {
      renderProductDetail(product);
    }
    renderCatalogGrid(products, product?.id);
  }

  function renderProductDetail(p) {
    const host = document.getElementById('productDetailMount');
    if (!host || !p) return;

    const imgUrl = SpikeAsset(p.imageUrl || 'assets/images/products/placeholders/placeholder-default.svg');
    const fallback = SpikeAsset('assets/images/products/placeholders/placeholder-default.svg');
    const catLabel = p.type === 'drink' ? 'Đồ uống & Điện giải' : p.type === 'food' ? 'Đồ ăn nhẹ' : 'Vợt, Bóng & Phụ kiện';
    const stock = Number(p.stock) || 0;

    host.innerHTML = `
      <div class="card" style="overflow:hidden;">
        <div style="display:grid;grid-template-columns:minmax(240px, 360px) 1fr;gap:24px;padding:24px;align-items:center;">
          <div style="background:var(--surface-secondary);border:1px solid var(--border-default);border-radius:var(--radius-lg);padding:24px;display:flex;align-items:center;justify-content:center;min-height:280px;">
            <img src="${escapeAttr(imgUrl)}" alt="${escapeAttr(p.name)}" style="max-width:220px;max-height:220px;object-fit:contain;" onerror="this.onerror=null;this.src='${escapeAttr(fallback)}';" />
          </div>
          <div style="display:flex;flex-direction:column;gap:12px;">
            <div style="display:flex;align-items:center;gap:8px;flex-wrap:wrap;">
              <span class="badge badge-primary">${escapeHtml(catLabel)}</span>
              <span class="badge badge-neutral text-mono">${escapeHtml(p.sku || 'SPK-' + p.id)}</span>
              <span class="badge ${stock > 5 ? 'badge-success' : stock > 0 ? 'badge-warning' : 'badge-danger'}">${stock > 0 ? 'Còn ' + stock + ' sản phẩm' : 'Tạm hết'}</span>
            </div>
            <h2 style="margin:0;font-size:1.55rem;font-weight:800;color:var(--text-primary);">${escapeHtml(p.name)}</h2>
            <p style="margin:0;color:var(--text-secondary);line-height:1.6;">${escapeHtml(p.description || 'Sản phẩm chính hãng tại Pro Shop Spike Pickleball Club.')}</p>
            <div style="display:flex;align-items:baseline;gap:10px;padding:12px 16px;background:var(--surface-secondary);border-radius:var(--radius-md);border:1px solid var(--border-default);">
              <span class="text-sm text-muted">Đơn giá niêm yết:</span>
              <strong class="text-mono tabular-nums" style="font-size:1.5rem;color:var(--brand-primary);">${formatMoney(p.price)}</strong>
            </div>
            <div style="display:flex;align-items:center;gap:12px;flex-wrap:wrap;margin-top:4px;">
              <div style="display:inline-flex;align-items:center;border:1px solid var(--border-default);border-radius:var(--radius-md);overflow:hidden;">
                <button type="button" class="btn btn-ghost btn-sm" onclick="changeDetailQty(-1)" aria-label="Giảm số lượng">−</button>
                <span id="detailQtyVal" class="text-mono font-bold" style="padding:0 14px;">${detailQty}</span>
                <button type="button" class="btn btn-ghost btn-sm" onclick="changeDetailQty(1)" aria-label="Tăng số lượng">+</button>
              </div>
              <button type="button" class="btn btn-primary btn-lg" onclick="addDetailToShopCart(${Number(p.id)})" ${stock <= 0 ? 'disabled' : ''}>
                <iconify-icon icon="lucide:shopping-bag" width="18"></iconify-icon>
                <span>Thêm vào giỏ &amp; Mở Pro Shop</span>
              </button>
              <a href="${escapeAttr(SpikeRoute('/Product'))}" class="btn btn-secondary btn-lg">Quay lại Cửa hàng</a>
            </div>
            <div class="text-xs text-muted">
              * Giao tận sân đang thi đấu hoặc nhận nhanh tại quầy lễ tân CLB (Bản mô phỏng UI Demo).
            </div>
          </div>
        </div>
      </div>
    `;
  }

  function renderCatalogGrid(products, activeId) {
    const grid = document.getElementById('productsCatalogGrid');
    if (!grid) return;

    const list = activeCat === 'all' ? products : products.filter(p => p.type === activeCat);
    const fallback = SpikeAsset('assets/images/products/placeholders/placeholder-default.svg');

    grid.innerHTML = list.map(p => {
      const imgUrl = SpikeAsset(p.imageUrl || fallback);
      const isCurrent = Number(p.id) === Number(activeId);
      return `
        <article class="menu-item ${isCurrent ? 'is-selected' : ''}">
          <div class="menu-item-media">
            <img class="menu-item-img" src="${escapeAttr(imgUrl)}" alt="${escapeAttr(p.name)}" width="220" height="220" loading="lazy" onerror="this.onerror=null;this.src='${escapeAttr(fallback)}';" />
            <span class="menu-item-cat-pill">${escapeHtml(p.categoryTitle || p.type)}</span>
            <span class="menu-item-stock-badge ${p.stock <= 5 ? 'is-low' : 'is-ok'}">Còn ${p.stock}</span>
          </div>
          <div class="menu-item-body">
            <h3 class="menu-item-name">${escapeHtml(p.name)}</h3>
            <p class="menu-item-desc">${escapeHtml(p.description || '')}</p>
          </div>
          <div class="menu-item-footer">
            <div class="menu-item-price-block">
              <span class="menu-item-price-label">Đơn giá</span>
              <span class="menu-item-price text-mono">${formatMoney(p.price)}</span>
            </div>
            <button type="button" class="btn btn-secondary btn-sm" onclick="selectDetailProduct(${Number(p.id)})">Xem chi tiết</button>
          </div>
        </article>
      `;
    }).join('');
  }

  window.changeDetailQty = function (delta) {
    detailQty = Math.max(1, Math.min(20, detailQty + delta));
    const el = document.getElementById('detailQtyVal');
    if (el) el.textContent = String(detailQty);
  };

  window.selectDetailProduct = function (id) {
    detailQty = 1;
    const url = new URL(window.location.href);
    url.searchParams.set('id', String(id));
    window.history.replaceState({}, '', url.toString());
    renderProductsPage();
    document.getElementById('productDetailMount')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  };

  window.filterCatalogProducts = function (cat, btnEl) {
    activeCat = cat || 'all';
    document.querySelectorAll('#productsCatTabs .filter-tab').forEach(b => b.classList.remove('active'));
    if (btnEl) btnEl.classList.add('active');
    renderProductsPage();
  };

  window.addDetailToShopCart = function (productId) {
    try {
      const existing = JSON.parse(sessionStorage.getItem('spike_demo_pending_cart') || '[]');
      const row = existing.find(x => Number(x.id) === Number(productId));
      if (row) row.qty += detailQty;
      else existing.push({ id: Number(productId), qty: detailQty });
      sessionStorage.setItem('spike_demo_pending_cart', JSON.stringify(existing));
    } catch (_) {}
    window.location.href = SpikeRoute('/Product');
  };

  document.addEventListener('DOMContentLoaded', renderProductsPage);
  window.addEventListener('pickleball:db-synced', renderProductsPage);
})();
