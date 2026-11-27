/* ==========================================================================
   SPIKE OS — ADMIN SUBPAGES CONTROLLERS (Bookings, Courts, Products, Accounts)
   ========================================================================== */

(function () {
  /* ---------------- 1. ADMIN BOOKINGS ---------------- */
  let bookingStatusFilter = 'all';

  window.filterAdminBookings = function (statusVal) {
    bookingStatusFilter = statusVal || 'all';
    renderAdminBookingsPage();
  };

  window.submitAdminWalkIn = async function (e) {
    e.preventDefault();
    const form = e.target;
    const customerName = (form.querySelector('[name="customerName"]')?.value || '').trim();
    const phone = (form.querySelector('[name="phone"]')?.value || '').trim();
    const courtId = Number(form.querySelector('[name="courtId"]')?.value || 1);
    const date = form.querySelector('[name="bookingDate"]')?.value || getTodayStr();
    const time = form.querySelector('[name="startTime"]')?.value || '17:00';
    const duration = Number(form.querySelector('[name="duration"]')?.value || 60);

    if (!customerName || !isValidVietnamPhone(phone)) {
      showToast('Thiếu thông tin hợp lệ', 'Vui lòng nhập tên khách và SĐT hợp lệ (9–11 số).', 'warning');
      return;
    }

    const res = SpikeDemo.createDemoBooking({
      courtId,
      customerName,
      customerPhone: phone,
      date,
      time,
      duration,
      players: 4,
      skillLevel: 'Giao lưu (DUPR 3.0)',
      paymentMethod: 'cash',
      status: 'approved'
    });
    form.reset();
    const dateInput = form.querySelector('[name="bookingDate"]');
    if (dateInput) dateInput.value = getTodayStr();
    await syncDatabaseFromServer(true);
    renderAdminBookingsPage();
    showToast('Đã tạo đơn Walk-in (Demo)', `Mã vé mẫu ${res.booking.id} đã thêm vào danh sách.`, 'success');
  };

  window.adminUpdateBookingStatus = async function (id, nextStatus) {
    const raw = SpikeDemo.getRawState();
    const b = (raw?.bookings || []).find(x => x.id === id);
    if (!b) return;
    b.status = nextStatus;
    SpikeDemo.saveState();
    await syncDatabaseFromServer(true);
    renderAdminBookingsPage();
    showToast('Đã cập nhật trạng thái (Demo)', `Đơn ${id} → ${statusLabel(nextStatus, b)}`, 'success');
  };

  window.adminCollectBookingCash = async function (id) {
    const raw = SpikeDemo.getRawState();
    const b = (raw?.bookings || []).find(x => x.id === id);
    if (!b) return;
    const due = Number(b.amountDue) || Number(b.courtFee) || 0;
    const rem = Math.max(0, due - (Number(b.paidAmount) || 0));
    if (rem <= 0) return;

    b.paidAmount = due;
    b.paymentStatus = 'paid';
    raw.transactions.push({
      id: 'TX-' + Math.floor(2000 + Math.random() * 7000),
      type: 'collect',
      targetType: 'booking',
      targetCode: b.id,
      customerName: b.name,
      method: 'cash',
      referenceCode: 'ADM-QUAY',
      note: 'Thu đủ tại quầy (UI Demo)',
      amount: rem,
      recordedBy: 'Admin Demo',
      date: SpikeDemo.isoDateOffset(0),
      createdAt: `${SpikeDemo.isoDateOffset(0)} ${minutesToTime(getNowMinutes())}`
    });
    SpikeDemo.saveState();
    await syncDatabaseFromServer(true);
    renderAdminBookingsPage();
    showToast('Đã ghi nhận thu tiền (Demo)', `Thu đủ ${formatMoney(rem)} cho vé ${b.id}.`, 'success');
  };

  function renderAdminBookingsPage() {
    const tbody = document.getElementById('adminBookingsTableBody');
    const courtSel = document.getElementById('walkInCourtSelect');
    const dateInput = document.getElementById('walkInDateInput');
    if (!tbody) return;

    if (dateInput && !dateInput.value) dateInput.value = getTodayStr();
    if (courtSel && courtSel.options.length === 0 && COURTS.length > 0) {
      courtSel.innerHTML = COURTS.map(c => `<option value="${c.id}">${escapeHtml(c.name)}</option>`).join('');
    }

    const raw = SpikeDemo.getRawState();
    const kw = (document.getElementById('adminBookingSearch')?.value || '').trim().toLowerCase();
    let list = [...(raw?.bookings || [])];
    if (bookingStatusFilter !== 'all') {
      list = list.filter(b => b.status === bookingStatusFilter);
    }
    if (kw) {
      list = list.filter(b =>
        String(b.id).toLowerCase().includes(kw) ||
        String(b.name).toLowerCase().includes(kw) ||
        String(b.phone).toLowerCase().includes(kw)
      );
    }

    const countEl = document.getElementById('adminBookingsCount');
    if (countEl) countEl.textContent = String(list.length);

    if (!list.length) {
      tbody.innerHTML = `<tr><td colspan="6" style="text-align:center;padding:2rem;color:var(--text-muted);">Không có đơn đặt sân phù hợp bộ lọc.</td></tr>`;
      return;
    }

    tbody.innerHTML = list.map(b => {
      const due = Number(b.amountDue) || Number(b.courtFee) || 0;
      const paid = Number(b.paidAmount) || 0;
      const rem = Math.max(0, due - paid);
      return `
        <tr>
          <td>
            <code class="text-mono font-bold" style="color:var(--notion-blue);">${escapeHtml(b.id)}</code>
            <div><a href="${escapeAttr(SpikeRoute('/qr?code=' + encodeURIComponent(b.id)))}" class="text-xs" style="color:var(--brand-primary);font-weight:600;">Xem QR vé</a></div>
          </td>
          <td>
            <div style="font-weight:700;">${escapeHtml(b.name)}</div>
            <div class="text-xs text-mono text-muted">${escapeHtml(b.phone)}</div>
          </td>
          <td class="text-mono">
            <div style="font-weight:650;">${escapeHtml(b.courtName || ('Sân #' + b.courtId))}</div>
            <div class="text-xs text-muted">${escapeHtml(formatDateVN(b.date))} · ${escapeHtml(b.time)} (${b.duration}p)</div>
          </td>
          <td>
            <div class="text-mono font-bold">${formatMoney(due)}</div>
            ${paymentBadgeHtml(paid, due, b.paymentStatus)}
          </td>
          <td><span class="badge ${statusBadgeClass(b.status, b)}">${escapeHtml(statusLabel(b.status, b))}</span></td>
          <td>
            <div style="display:flex;gap:6px;flex-wrap:wrap;">
              ${b.status === 'pending' ? `<button type="button" class="btn btn-success btn-xs" onclick="adminUpdateBookingStatus('${escapeAttr(b.id)}','approved')">Duyệt vé</button>` : ''}
              ${b.status === 'approved' ? `<button type="button" class="btn btn-primary btn-xs" onclick="adminUpdateBookingStatus('${escapeAttr(b.id)}','playing')">Check-in</button>` : ''}
              ${b.status === 'playing' ? `<button type="button" class="btn btn-secondary btn-xs" onclick="adminUpdateBookingStatus('${escapeAttr(b.id)}','completed')">Đóng ca</button>` : ''}
              ${rem > 0 && !['cancelled', 'rejected'].includes(b.status) ? `<button type="button" class="btn btn-outline btn-xs" onclick="adminCollectBookingCash('${escapeAttr(b.id)}')">Thu đủ</button>` : ''}
              ${(b.status === 'pending' || b.status === 'approved') ? `<button type="button" class="btn btn-danger btn-xs" onclick="adminUpdateBookingStatus('${escapeAttr(b.id)}','cancelled')">Hủy</button>` : ''}
            </div>
          </td>
        </tr>
      `;
    }).join('');
  }

  /* ---------------- 2. ADMIN COURTS ---------------- */
  window.toggleAdminCourtActive = async function (courtId) {
    const raw = SpikeDemo.getRawState();
    const c = (raw?.courts || []).find(x => Number(x.id) === Number(courtId));
    if (!c) return;
    c.isActive = c.isActive === false;
    SpikeDemo.saveState();
    await syncDatabaseFromServer(true);
    renderAdminCourtsPage();
    showToast('Cập nhật trạng thái sân (Demo)', `${c.name} → ${c.isActive ? 'Đang hoạt động' : 'Tạm ngưng'}`, 'success');
  };

  window.openEditCourtModal = function (courtId) {
    const raw = SpikeDemo.getRawState();
    const c = (raw?.courts || []).find(x => Number(x.id) === Number(courtId));
    if (!c) return;
    document.getElementById('editCourtId').value = c.id;
    document.getElementById('editCourtName').value = c.name;
    document.getElementById('editCourtBasePrice').value = c.basePrice;
    document.getElementById('editCourtSurcharge').value = c.surcharge;
    openModal('modalEditCourt');
  };

  window.saveEditCourtModal = async function (e) {
    e.preventDefault();
    const id = Number(document.getElementById('editCourtId').value);
    const raw = SpikeDemo.getRawState();
    const c = (raw?.courts || []).find(x => Number(x.id) === id);
    if (!c) return;
    c.name = document.getElementById('editCourtName').value.trim() || c.name;
    c.basePrice = Number(document.getElementById('editCourtBasePrice').value) || 120000;
    c.surcharge = Math.max(0, Number(document.getElementById('editCourtSurcharge').value) || 0);
    SpikeDemo.saveState();
    closeModal('modalEditCourt');
    await syncDatabaseFromServer(true);
    renderAdminCourtsPage();
    showToast('Đã cập nhật thông tin sân (Demo)', `${c.name} · ${formatMoney(c.basePrice)}/h`, 'success');
  };

  function renderAdminCourtsPage() {
    const tbody = document.getElementById('adminCourtsTableBody');
    if (!tbody) return;
    const courts = SpikeDemo.getRawState()?.courts || [];
    const countEl = document.getElementById('adminCourtsCount');
    if (countEl) countEl.textContent = String(courts.length);

    tbody.innerHTML = courts.map(c => `
      <tr>
        <td><code class="text-mono font-bold" style="color:var(--brand-primary-ink);">#${c.id}</code></td>
        <td style="font-weight:700;">${escapeHtml(c.name)}</td>
        <td>${escapeHtml(c.typeLabel || c.type)}</td>
        <td>${escapeHtml(c.surface)}</td>
        <td class="text-mono tabular-nums font-bold" style="color:var(--status-success-text);">${formatMoney(c.basePrice)}</td>
        <td class="text-mono tabular-nums">${formatMoney(c.surcharge)}</td>
        <td>${c.isActive !== false ? '<span class="badge badge-success">● Đang hoạt động</span>' : '<span class="badge badge-danger">✕ Tạm ngưng</span>'}</td>
        <td>
          <div style="display:flex;gap:6px;flex-wrap:wrap;">
            <button type="button" class="btn btn-primary btn-xs" onclick="openEditCourtModal(${c.id})">Sửa giá (Demo)</button>
            <button type="button" class="btn btn-secondary btn-xs" onclick="toggleAdminCourtActive(${c.id})">${c.isActive !== false ? 'Tạm ngưng' : 'Kích hoạt'}</button>
          </div>
        </td>
      </tr>
    `).join('');
  }

  /* ---------------- 3. ADMIN PRODUCTS ---------------- */
  window.openAdminProductModal = function (prodId = null) {
    const raw = SpikeDemo.getRawState();
    const p = prodId ? (raw?.products || []).find(x => Number(x.id) === Number(prodId)) : null;
    document.getElementById('editProdId').value = p ? p.id : '';
    document.getElementById('editProdName').value = p ? p.name : '';
    document.getElementById('editProdType').value = p ? p.type : 'drink';
    document.getElementById('editProdPrice').value = p ? p.price : 20000;
    document.getElementById('editProdStock').value = p ? p.stock : 20;
    document.getElementById('editProdDesc').value = p ? (p.description || '') : '';
    document.getElementById('modalAdminProdTitle').textContent = p ? `Sửa sản phẩm: ${p.name}` : 'Thêm sản phẩm mới (Demo)';
    openModal('modalAdminProduct');
  };

  window.saveAdminProductModal = async function (e) {
    e.preventDefault();
    const raw = SpikeDemo.getRawState();
    const idVal = document.getElementById('editProdId').value;
    const name = document.getElementById('editProdName').value.trim();
    const type = document.getElementById('editProdType').value || 'drink';
    const price = Math.max(1000, Number(document.getElementById('editProdPrice').value) || 20000);
    const stock = Math.max(0, Number(document.getElementById('editProdStock').value) || 0);
    const desc = document.getElementById('editProdDesc').value.trim();
    const catTitle = type === 'drink' ? 'Đồ uống' : type === 'food' ? 'Đồ ăn nhẹ' : 'Phụ kiện';

    if (!name) return;
    if (idVal) {
      const existing = (raw.products || []).find(x => Number(x.id) === Number(idVal));
      if (existing) {
        existing.name = name;
        existing.type = type;
        existing.categoryTitle = catTitle;
        existing.price = price;
        existing.stock = stock;
        existing.description = desc;
      }
    } else {
      const nextId = Math.max(0, ...(raw.products || []).map(x => Number(x.id) || 0)) + 1;
      raw.products.push({
        id: nextId,
        sku: `SPK-NEW-${nextId}`,
        name,
        type,
        categoryTitle: catTitle,
        price,
        stock,
        imageUrl: SpikeAsset(`assets/images/products/placeholders/placeholder-${type}.svg`),
        description: desc || 'Sản phẩm thêm mới trên bản UI Demo',
        isFeatured: false,
        isActive: true
      });
    }
    SpikeDemo.saveState();
    closeModal('modalAdminProduct');
    await syncDatabaseFromServer(true);
    renderAdminProductsPage();
    showToast('Đã lưu sản phẩm (Demo)', `${name} · ${formatMoney(price)} (Kho: ${stock})`, 'success');
  };

  function renderAdminProductsPage() {
    const tbody = document.getElementById('adminProductsTableBody');
    if (!tbody) return;
    const products = SpikeDemo.getRawState()?.products || [];
    const countEl = document.getElementById('adminProductsCount');
    if (countEl) countEl.textContent = String(products.length);

    const fallback = SpikeAsset('assets/images/products/placeholders/placeholder-default.svg');
    tbody.innerHTML = products.map((p, idx) => {
      const stock = Number(p.stock) || 0;
      const imgUrl = SpikeAsset(p.imageUrl || fallback);
      const stockBadge = stock <= 0
        ? '<span class="badge badge-danger">Hết hàng (0)</span>'
        : stock <= 5
          ? `<span class="badge badge-warning">Sắp hết (${stock})</span>`
          : `<span class="badge badge-success">Còn ${stock}</span>`;
      return `
        <tr>
          <td class="text-mono font-bold text-muted">#${idx + 1}</td>
          <td>
            <div style="width:52px;height:52px;border-radius:10px;background:#FFFFFF;border:1px solid var(--border-default);padding:4px;display:flex;align-items:center;justify-content:center;overflow:hidden;">
              <img src="${escapeAttr(imgUrl)}" alt="${escapeAttr(p.name)}" width="44" height="44" style="width:100%;height:100%;object-fit:contain;" onerror="this.onerror=null;this.src='${escapeAttr(fallback)}';" />
            </div>
          </td>
          <td>
            <div style="font-weight:700;">${escapeHtml(p.name)}</div>
            <div class="text-xs text-muted">${escapeHtml(p.description || '')}</div>
          </td>
          <td><span class="badge badge-neutral">${escapeHtml(p.categoryTitle || p.type)}</span></td>
          <td class="text-mono font-bold" style="color:var(--brand-primary);">${formatMoney(p.price)}</td>
          <td>${stockBadge}</td>
          <td>${p.isActive !== false ? '<span class="badge badge-success">Đang bán</span>' : '<span class="badge badge-neutral">Ngừng bán</span>'}</td>
          <td>
            <button type="button" class="btn btn-xs btn-secondary" onclick="openAdminProductModal(${p.id})">Sửa (Demo)</button>
          </td>
        </tr>
      `;
    }).join('');
  }

  /* ---------------- 4. ADMIN ACCOUNTS ---------------- */
  window.createAdminAccountDemo = async function (e) {
    e.preventDefault();
    const form = e.target;
    const name = (form.querySelector('[name="fullName"]')?.value || '').trim();
    const phone = (form.querySelector('[name="phone"]')?.value || '').trim();
    const email = (form.querySelector('[name="email"]')?.value || '').trim();
    const memberTier = form.querySelector('[name="memberTier"]')?.value || 'Hội viên Tiêu chuẩn';
    const role = form.querySelector('[name="role"]')?.value || 'user';

    if (!name || !phone) return;
    const raw = SpikeDemo.getRawState();
    const nextId = Math.max(100, ...(raw.accounts || []).map(a => Number(a.id) || 0)) + 1;
    raw.accounts.push({
      id: nextId,
      username: 'demo_' + nextId,
      name,
      email: email || `member${nextId}@spikepickleball.vn`,
      phone,
      role,
      roleLabel: role === 'admin' ? 'Ban Quản Trị CLB' : role === 'staff' ? 'Nhân viên Vận hành' : 'Hội viên CLB',
      memberTier,
      discountPercent: memberTier.includes('VIP') ? 15 : memberTier.includes('Bạc') ? 5 : 0,
      avatar: SpikeAsset('assets/images/avatars/sunny.png'),
      skillLevel: 'Giao lưu (DUPR 3.0)',
      isActive: true
    });
    SpikeDemo.saveState();
    form.reset();
    await syncDatabaseFromServer(true);
    renderAdminAccountsPage();
    showToast('Đã tạo tài khoản Demo', `${name} (#SPK-${String(nextId).padStart(4, '0')})`, 'success');
  };

  window.updateAccountTierDemo = async function (accId, selectId, roleSelId) {
    const raw = SpikeDemo.getRawState();
    const acc = (raw?.accounts || []).find(a => Number(a.id) === Number(accId));
    if (!acc) return;
    const nextTier = document.getElementById(selectId)?.value;
    const nextRole = document.getElementById(roleSelId)?.value;
    if (nextTier) {
      acc.memberTier = nextTier;
      acc.discountPercent = nextTier.includes('VIP') ? 15 : nextTier.includes('Bạc') ? 5 : 0;
    }
    if (nextRole) {
      acc.role = nextRole;
      acc.roleLabel = nextRole === 'admin' ? 'Ban Quản Trị CLB' : nextRole === 'staff' ? 'Nhân viên Vận hành' : 'Hội viên CLB';
    }
    SpikeDemo.saveState();
    await syncDatabaseFromServer(true);
    renderAdminAccountsPage();
    showToast('Đã cập nhật phân quyền (Demo)', `${acc.name} → ${acc.memberTier} (${acc.role})`, 'success');
  };

  function renderAdminAccountsPage() {
    const tbody = document.getElementById('adminAccountsTableBody');
    if (!tbody) return;
    const raw = SpikeDemo.getRawState();
    const accounts = raw?.accounts || [];
    const bookings = raw?.bookings || [];

    const vipCount = accounts.filter(a => String(a.memberTier || '').includes('VIP')).length;
    const staffCount = accounts.filter(a => a.role === 'staff').length;
    const adminCount = accounts.filter(a => a.role === 'admin').length;

    const setText = (id, v) => { const el = document.getElementById(id); if (el) el.textContent = v; };
    setText('accTotalCount', String(accounts.length));
    setText('accVipCount', String(vipCount));
    setText('accStaffCount', String(staffCount));
    setText('accAdminCount', String(adminCount));
    setText('accListCount', String(accounts.length));

    tbody.innerHTML = accounts.map(acc => {
      const accBookings = bookings.filter(b => Number(b.accountId) === Number(acc.id) || (acc.phone && b.phone === acc.phone));
      const spent = accBookings.reduce((s, b) => s + (Number(b.paidAmount) || 0), 0);
      const tierId = `tierSel_${acc.id}`;
      const roleId = `roleSel_${acc.id}`;
      return `
        <tr>
          <td><span class="text-mono font-bold" style="color:var(--notion-blue);">#SPK-${String(acc.id).padStart(4, '0')}</span></td>
          <td>
            <div style="font-weight:700;">${escapeHtml(acc.name)}</div>
            <div class="text-xs text-muted">@${escapeHtml(acc.username || 'member')}</div>
          </td>
          <td>
            <div class="text-mono font-bold">${escapeHtml(acc.phone || '--')}</div>
            <div class="text-xs text-muted">${escapeHtml(acc.email || '--')}</div>
          </td>
          <td><span class="badge badge-primary">${escapeHtml(acc.skillLevel || 'Giao lưu (DUPR 3.0)')}</span></td>
          <td>
            <div class="text-mono font-bold">${accBookings.length} ca</div>
            <div class="text-xs text-mono" style="color:var(--brand-primary);">${formatMoney(spent)}</div>
          </td>
          <td>
            <div style="display:flex;gap:6px;align-items:center;flex-wrap:wrap;">
              <select id="${tierId}" class="form-select" style="width:auto;min-height:30px;padding:2px 8px;font-size:0.78rem;">
                <option value="Hội viên Tiêu chuẩn" ${acc.memberTier === 'Hội viên Tiêu chuẩn' ? 'selected' : ''}>Tiêu chuẩn</option>
                <option value="Hội viên Bạc" ${acc.memberTier === 'Hội viên Bạc' ? 'selected' : ''}>Hạng Bạc (-5%)</option>
                <option value="Hội viên Vàng VIP" ${acc.memberTier === 'Hội viên Vàng VIP' ? 'selected' : ''}>Hạng Vàng VIP (-15%)</option>
              </select>
              <select id="${roleId}" class="form-select" style="width:auto;min-height:30px;padding:2px 8px;font-size:0.78rem;">
                <option value="user" ${acc.role === 'user' ? 'selected' : ''}>Hội viên</option>
                <option value="staff" ${acc.role === 'staff' ? 'selected' : ''}>Staff</option>
                <option value="admin" ${acc.role === 'admin' ? 'selected' : ''}>Admin</option>
              </select>
              <button type="button" class="btn btn-primary btn-xs" onclick="updateAccountTierDemo(${acc.id}, '${tierId}', '${roleId}')">Lưu</button>
            </div>
          </td>
          <td>${acc.isActive !== false ? '<span class="badge badge-success">Hoạt động</span>' : '<span class="badge badge-danger">Đã khóa</span>'}</td>
        </tr>
      `;
    }).join('');
  }

  function renderAllAdminSubpages() {
    if (!HAS_SERVER_STATE) return;
    renderAdminBookingsPage();
    renderAdminCourtsPage();
    renderAdminProductsPage();
    renderAdminAccountsPage();
  }

  document.addEventListener('DOMContentLoaded', renderAllAdminSubpages);
  window.addEventListener('pickleball:db-synced', renderAllAdminSubpages);
})();
