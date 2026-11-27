/* ==========================================================================
   SPIKE OS — STATIC UI PREVIEW ENGINE (Mock Data + Local State + Demo AI)
   Runs 100% client-side for GitHub Pages (/PicCoBon/) without backend/DB.
   ========================================================================== */

(function () {
  const STORAGE_KEY = 'spike_os_ui_demo_state_v1';
  const AUTH_STORAGE_KEY = 'spike_os_ui_demo_user_id_v1';
  const VIETNAM_OFFSET_MS = 7 * 60 * 60 * 1000;

  function getRootPrefix() {
    const attr = document.documentElement.getAttribute('data-root');
    if (attr !== null && attr !== undefined) return attr;
    return './';
  }

  function spikeAsset(rawPath) {
    const clean = String(rawPath || '').trim();
    if (!clean) return '';
    if (/^(https?:|data:|blob:)/i.test(clean)) return clean;

    const root = getRootPrefix();
    let rel = clean.replace(/^(?:\.\.?\/|\/)+/, '');

    // Normalize legacy ASP.NET wwwroot paths to site-preview/assets/
    if (rel.startsWith('assets/assistant/')) {
      rel = 'assets/mascot/' + rel.slice('assets/assistant/'.length);
    } else if (rel.startsWith('assets/products/')) {
      rel = 'assets/images/products/placeholders/' + rel.slice('assets/products/'.length);
    } else if (rel.startsWith('assets/') && !rel.startsWith('assets/images/') && !rel.startsWith('assets/css/') && !rel.startsWith('assets/js/') && !rel.startsWith('assets/mascot/')) {
      rel = 'assets/images/' + rel.slice('assets/'.length);
    } else if (rel.startsWith('images/')) {
      rel = 'assets/' + rel;
    } else if (rel.startsWith('uploads/products/')) {
      rel = 'assets/images/products/' + rel.slice('uploads/products/'.length);
    }

    return root + rel;
  }

  function spikeRoute(rawRoute) {
    const raw = String(rawRoute || '/').trim();
    if (/^(https?:|mailto:|tel:|javascript:|#)/i.test(raw)) return raw;

    const root = getRootPrefix();
    const qIdx = raw.indexOf('?');
    const pathPart = (qIdx >= 0 ? raw.slice(0, qIdx) : raw).toLowerCase().replace(/\/+$/, '') || '/';
    const queryPart = qIdx >= 0 ? raw.slice(qIdx) : '';

    const routeMap = {
      '/': '',
      '/home': '',
      '/home/index': '',
      '/booking': 'booking/',
      '/product': 'shop/',
      '/shop': 'shop/',
      '/products': 'products/',
      '/lookup': 'my-bookings/',
      '/my-bookings': 'my-bookings/',
      '/qr': 'qr/',
      '/account/login': 'account/login/',
      '/account/register': 'account/login/?mode=register',
      '/account/profile': 'account/profile/',
      '/admin': 'admin/',
      '/admin/home': 'admin/',
      '/admin/home/index': 'admin/',
      '/admin/bookings': 'admin/bookings/',
      '/admin/courts': 'admin/courts/',
      '/admin/products': 'admin/products/',
      '/admin/accounts': 'admin/accounts/',
      '/admin/reports': 'admin/reports/',
      '/reports': 'admin/reports/',
      '/admin/home/reports': 'admin/reports/',
      '/admin/cms': 'admin/cms/',
      '/cms': 'admin/cms/',
      '/admin/home/cms': 'admin/cms/',
      '/management': 'staff/',
      '/staff': 'staff/',
      '/admin/home/management': 'staff/',
      '/checkin': 'staff/checkin/',
      '/staff/checkin': 'staff/checkin/',
      '/admin/home/checkin': 'staff/checkin/',
      '/payment/paypalreturn': 'payment/paypal-return/',
      '/payment/paypal-return': 'payment/paypal-return/',
      '/payment/paypalcancel': 'payment/paypal-cancel/',
      '/payment/paypal-cancel': 'payment/paypal-cancel/'
    };

    const mapped = routeMap[pathPart];
    if (mapped !== undefined) {
      if (mapped.includes('?') && queryPart) {
        return root + mapped + '&' + queryPart.slice(1);
      }
      return root + mapped + queryPart;
    }

    const stripped = raw.replace(/^\/+/, '');
    return root + stripped;
  }

  function getVietnamNow(offsetDays = 0) {
    return new Date(Date.now() + VIETNAM_OFFSET_MS + (Number(offsetDays) || 0) * 86400000);
  }

  function isoDateOffset(offsetDays = 0) {
    const d = getVietnamNow(offsetDays);
    const y = d.getUTCFullYear();
    const m = String(d.getUTCMonth() + 1).padStart(2, '0');
    const day = String(d.getUTCDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
  }

  function timeToMins(t) {
    const [h, m] = String(t || '00:00').split(':').map(Number);
    return (h || 0) * 60 + (m || 0);
  }

  function minsToTime(mins) {
    const clamped = Math.max(0, Math.min(24 * 60, mins));
    if (clamped === 24 * 60) return '24:00';
    return `${String(Math.floor(clamped / 60)).padStart(2, '0')}:${String(clamped % 60).padStart(2, '0')}`;
  }

  // Deterministic SVG QR generator for UI demonstration (never a live venue credential)
  function generateQrSvg(seedText) {
    const str = String(seedText || 'SPK1-DEMO-TICKET');
    const size = 21;
    const grid = Array.from({ length: size }, () => Array(size).fill(false));

    function drawFinder(r0, c0) {
      for (let r = 0; r < 7; r++) {
        for (let c = 0; c < 7; c++) {
          const border = r === 0 || r === 6 || c === 0 || c === 6;
          const center = r >= 2 && r <= 4 && c >= 2 && c <= 4;
          grid[r0 + r][c0 + c] = border || center;
        }
      }
    }

    drawFinder(0, 0);
    drawFinder(0, size - 7);
    drawFinder(size - 7, 0);

    for (let i = 8; i < size - 8; i++) {
      grid[6][i] = i % 2 === 0;
      grid[i][6] = i % 2 === 0;
    }

    let hash = 2166136261;
    for (let i = 0; i < str.length; i++) {
      hash ^= str.charCodeAt(i);
      hash = Math.imul(hash, 16777619);
    }

    for (let r = 0; r < size; r++) {
      for (let c = 0; c < size; c++) {
        const inTopLeft = r < 8 && c < 8;
        const inTopRight = r < 8 && c >= size - 8;
        const inBottomLeft = r >= size - 8 && c < 8;
        if (inTopLeft || inTopRight || inBottomLeft || r === 6 || c === 6) continue;
        hash ^= (r * 31 + c * 17 + 13);
        hash = Math.imul(hash, 16777619);
        grid[r][c] = ((hash >>> 0) % 100) < 48;
      }
    }

    const rects = [];
    for (let r = 0; r < size; r++) {
      for (let c = 0; c < size; c++) {
        if (grid[r][c]) {
          rects.push(`<rect x="${c + 2}" y="${r + 2}" width="1" height="1" fill="#0F172A"/>`);
        }
      }
    }
    const vb = size + 4;
    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${vb} ${vb}" shape-rendering="crispEdges" role="img" aria-label="Mã QR demo ${str}"><rect width="${vb}" height="${vb}" fill="#FFFFFF"/>${rects.join('')}</svg>`;
  }

  let _state = null;
  let _loadPromise = null;

  async function fetchJson(relPath) {
    const url = getRootPrefix() + relPath;
    const res = await fetch(url, { cache: 'no-cache' });
    if (!res.ok) throw new Error('Failed to load ' + url);
    return res.json();
  }

  function hydrateSeedData(courtsData, productsData, bookingsData, dashData) {
    const courts = (courtsData.courts || []).map(c => ({
      ...c,
      imageUrl: c.imageUrl
    }));

    const products = (productsData.products || []).map(p => ({
      ...p,
      imageUrl: p.imageUrl
    }));

    const bookings = (bookingsData.bookings || []).map(b => {
      const dateStr = isoDateOffset(b.dayOffset || 0);
      const holdExp = b.holdMinutesFromNow
        ? new Date(Date.now() + b.holdMinutesFromNow * 60000).toISOString()
        : null;
      return {
        ...b,
        date: dateStr,
        endTime: minsToTime(timeToMins(b.time) + (Number(b.duration) || 60)),
        holdExpiresAtUtc: holdExp,
        qrSvg: generateQrSvg(b.checkInToken || b.id)
      };
    });

    const courtLocks = (bookingsData.courtLocks || []).map(l => ({
      ...l,
      date: isoDateOffset(l.dayOffset || 0)
    }));

    const orders = (bookingsData.orders || []).map(o => ({
      ...o,
      createdAt: `${isoDateOffset(o.dayOffset || 0)} ${o.createdTime || '12:00'}`
    }));

    const accounts = (dashData.accounts || []).map(a => ({
      ...a,
      avatar: a.avatar
    }));

    const transactions = (dashData.transactions || []).map(t => ({
      ...t,
      date: isoDateOffset(t.dayOffset || 0),
      createdAt: `${isoDateOffset(t.dayOffset || 0)} ${t.timeStr || '12:00'}`
    }));

    const paypalTransactions = (dashData.paypalTransactions || []).map(p => ({
      ...p,
      createdAtUtc: new Date(Date.now() + (p.dayOffset || 0) * 86400000).toISOString()
    }));

    return {
      cmsConfig: { ...(dashData.cmsConfig || {}) },
      paypalConfig: { ...(dashData.paypalConfig || { enabled: true, environment: 'Sandbox', currency: 'USD', vndPerUsd: 25400 }) },
      courts,
      products,
      bookings,
      courtLocks,
      orders,
      accounts,
      transactions,
      paypalTransactions,
      menus: dashData.menus || [],
      weeklyTrendBase: dashData.weeklyTrendBase || []
    };
  }

  function saveState() {
    if (!_state) return;
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(_state));
    } catch (_) {}
  }

  async function ensureLoaded(forceReset = false) {
    if (_state && !forceReset) return _state;
    if (_loadPromise && !forceReset) return _loadPromise;

    _loadPromise = (async () => {
      if (!forceReset) {
        try {
          const savedRaw = localStorage.getItem(STORAGE_KEY);
          if (savedRaw) {
            const parsed = JSON.parse(savedRaw);
            if (parsed && Array.isArray(parsed.courts) && Array.isArray(parsed.products)) {
              _state = parsed;
              return _state;
            }
          }
        } catch (_) {}
      }

      const [courtsData, productsData, bookingsData, dashData] = await Promise.all([
        fetchJson('mock-data/courts.json'),
        fetchJson('mock-data/products.json'),
        fetchJson('mock-data/bookings.json'),
        fetchJson('mock-data/dashboard.json')
      ]);

      _state = hydrateSeedData(courtsData, productsData, bookingsData, dashData);
      saveState();
      return _state;
    })().finally(() => {
      _loadPromise = null;
    });

    return _loadPromise;
  }

  function getActiveAccount() {
    if (!_state) return null;
    const path = window.location.pathname.toLowerCase();
    let storedId = null;
    try {
      storedId = localStorage.getItem(AUTH_STORAGE_KEY);
    } catch (_) {}

    if (storedId === 'guest') {
      // On admin or staff portals, auto-provide demo operator context
      if (path.includes('/admin') || path.includes('/staff')) {
        return _state.accounts.find(a => a.role === (path.includes('/staff') ? 'staff' : 'admin')) || _state.accounts[0];
      }
      return null;
    }

    if (storedId) {
      const found = _state.accounts.find(a => String(a.id) === String(storedId));
      if (found) return found;
    }

    if (path.includes('/admin')) {
      return _state.accounts.find(a => a.role === 'admin') || _state.accounts[0];
    }
    if (path.includes('/staff')) {
      return _state.accounts.find(a => a.role === 'staff') || _state.accounts[1] || _state.accounts[0];
    }
    return _state.accounts.find(a => a.id === 101) || _state.accounts[2] || _state.accounts[0];
  }

  function setActiveAccountId(accountId) {
    try {
      localStorage.setItem(AUTH_STORAGE_KEY, accountId ? String(accountId) : 'guest');
    } catch (_) {}
  }

  function computeQuote(courtId, dateStr, startTime, durationMins) {
    const state = _state;
    const court = state.courts.find(c => Number(c.id) === Number(courtId));
    const cms = state.cmsConfig;
    const base = Number(court?.basePrice ?? cms.courtPrice ?? 120000);
    const surcharge = Number(court?.surcharge ?? 0);
    const peakSurcharge = Number(cms.peakSurcharge ?? 30000);
    const peakStartMins = timeToMins(cms.peakStart || '17:00');
    const standardRate = base + surcharge;
    const peakRate = standardRate + peakSurcharge;

    const dur = Math.max(30, Number(durationMins) || 60);
    const slots = Math.round(dur / 30);
    const startMins = timeToMins(startTime || '17:00');

    let normalMinutes = 0;
    let peakMinutes = 0;
    let normalAmount = 0;
    let peakAmount = 0;

    for (let i = 0; i < slots; i++) {
      const segStart = startMins + i * 30;
      const isPeak = segStart >= peakStartMins;
      const halfFee = Math.round((isPeak ? peakRate : standardRate) / 2);
      if (isPeak) {
        peakMinutes += 30;
        peakAmount += halfFee;
      } else {
        normalMinutes += 30;
        normalAmount += halfFee;
      }
    }

    const courtFee = normalAmount + peakAmount;
    const user = getActiveAccount();
    const discountPercent = user ? Number(user.discountPercent || 0) : 0;
    const discountAmount = discountPercent > 0 ? Math.round((courtFee * (discountPercent / 100)) / 1000) * 1000 : 0;
    const amountDue = Math.max(0, courtFee - discountAmount);

    return {
      standardHourlyRate: standardRate,
      peakHourlyRate: peakRate,
      standardRate,
      peakRate,
      normalMinutes,
      peakMinutes,
      normalAmount,
      peakAmount,
      courtFeeBeforeDiscount: courtFee,
      courtFee,
      discountPercent,
      discountAmount,
      amountDue,
      memberTier: user?.memberTier || 'Khách vãng lai',
      pricingVersion: cms.pricingVersion || 'DEMO-V2026.1'
    };
  }

  function getTodayLedgerSummary() {
    if (!_state) return { netCollectedToday: 0, todayReceivableTotal: 0, todayUnpaidReceivable: 0, refundedToday: 0 };
    const today = isoDateOffset(0);
    const todayTx = _state.transactions.filter(t => t.date === today);
    let collected = 0;
    let refunded = 0;
    todayTx.forEach(t => {
      if (t.type === 'refund' || t.amount < 0) refunded += Math.abs(Number(t.amount) || 0);
      else collected += Number(t.amount) || 0;
    });

    const todayBookings = _state.bookings.filter(b => b.date === today && !['cancelled', 'rejected', 'expired'].includes(b.status));
    const todayOrders = _state.orders.filter(o => (o.createdAt || '').startsWith(today) && o.status !== 'cancelled' && !o.bookingCode);

    const bkDue = todayBookings.reduce((s, b) => s + (Number(b.amountDue) || 0), 0);
    const bkPaid = todayBookings.reduce((s, b) => s + (Number(b.paidAmount) || 0), 0);
    const ordDue = todayOrders.reduce((s, o) => s + (Number(o.totalAmount) || 0), 0);
    const ordPaid = todayOrders.reduce((s, o) => s + (Number(o.paidAmount) || 0), 0);

    return {
      netCollectedToday: collected - refunded,
      todayReceivableTotal: bkDue + ordDue,
      todayUnpaidReceivable: Math.max(0, (bkDue - bkPaid) + (ordDue - ordPaid)),
      refundedToday: refunded
    };
  }

  function getStatePayload() {
    const user = getActiveAccount();
    const isStaffOrAdmin = Boolean(user && (user.role === 'admin' || user.role === 'staff'));
    const myBookings = user
      ? _state.bookings.filter(b => Number(b.accountId) === Number(user.id) || (user.phone && b.phone === user.phone))
      : [];

    return {
      ok: true,
      serverTimeUtc: new Date().toISOString(),
      isStaffOrAdmin,
      currentUser: user,
      courts: _state.courts.filter(c => c.isActive !== false),
      allCourts: _state.courts,
      bookings: _state.bookings,
      myBookings,
      orders: _state.orders,
      courtLocks: _state.courtLocks,
      menu: _state.products.filter(p => p.isActive !== false),
      allProducts: _state.products,
      cmsConfig: _state.cmsConfig,
      todayLedgerSummary: getTodayLedgerSummary()
    };
  }

  function getReportSummary(dateFrom, dateTo) {
    const from = dateFrom || `${isoDateOffset(0).slice(0, 7)}-01`;
    const to = dateTo || isoDateOffset(0);
    const txInRange = _state.transactions.filter(t => t.date >= from && t.date <= to);

    let cashNet = 0;
    let transferNet = 0;
    let paypalNet = 0;
    let collectedBooking = 0;
    let standaloneShop = 0;
    let totalRefunds = 0;

    txInRange.forEach(t => {
      const amt = Number(t.amount) || 0;
      if (t.type === 'refund' || amt < 0) {
        totalRefunds += Math.abs(amt);
        return;
      }
      if (t.method === 'transfer') transferNet += amt;
      else if (t.method === 'paypal') paypalNet += amt;
      else cashNet += amt;

      if (t.targetType === 'order') standaloneShop += amt;
      else collectedBooking += amt;
    });

    const totalCollectedRevenue = Math.max(0, (cashNet + transferNet + paypalNet) - totalRefunds);

    const activeBookings = _state.bookings.filter(b => b.date >= from && b.date <= to && !['cancelled', 'rejected', 'expired'].includes(b.status));
    const activeOrders = _state.orders.filter(o => {
      const d = (o.createdAt || '').slice(0, 10);
      return d >= from && d <= to && o.status !== 'cancelled' && !o.bookingCode;
    });

    const unpaidBookingReceivables = activeBookings.reduce((s, b) => s + Math.max(0, (Number(b.amountDue) || 0) - (Number(b.paidAmount) || 0)), 0);
    const unpaidShopReceivables = activeOrders.reduce((s, o) => s + Math.max(0, (Number(o.totalAmount) || 0) - (Number(o.paidAmount) || 0)), 0);

    const courtBreakdown = _state.courts.map(c => {
      const cBookings = activeBookings.filter(b => Number(b.courtId) === Number(c.id));
      const collectedRevenue = cBookings.reduce((s, b) => s + (Number(b.paidAmount) || 0), 0);
      const unpaidReceivable = cBookings.reduce((s, b) => s + Math.max(0, (Number(b.amountDue) || 0) - (Number(b.paidAmount) || 0)), 0);
      const hoursBooked = Number((cBookings.reduce((s, b) => s + (Number(b.duration) || 0), 0) / 60).toFixed(1));
      return {
        courtId: c.id,
        courtName: c.name,
        collectedRevenue,
        unpaidReceivable,
        sessionsCount: cBookings.length,
        hoursBooked
      };
    });

    const topMembersMap = new Map();
    _state.accounts.filter(a => a.role === 'user').forEach(a => {
      topMembersMap.set(a.phone, {
        name: a.name,
        phone: a.phone,
        memberTier: a.memberTier,
        netPaidInRange: 0
      });
    });

    activeBookings.forEach(b => {
      const paid = Number(b.paidAmount) || 0;
      if (paid <= 0) return;
      const cur = topMembersMap.get(b.phone) || {
        name: b.name,
        phone: b.phone,
        memberTier: 'Khách vãng lai',
        netPaidInRange: 0
      };
      cur.netPaidInRange += paid;
      topMembersMap.set(b.phone, cur);
    });

    const topMembers = Array.from(topMembersMap.values())
      .filter(m => m.netPaidInRange > 0)
      .sort((a, b) => b.netPaidInRange - a.netPaidInRange)
      .slice(0, 6);

    return {
      ok: true,
      totalCollectedRevenue,
      cashNetCollected: cashNet,
      transferNetCollected: transferNet,
      paypalNetCollected: paypalNet,
      collectedBookingRevenue: collectedBooking,
      standaloneRetailRevenue: standaloneShop,
      unpaidReceivables: unpaidBookingReceivables + unpaidShopReceivables,
      unpaidBookingReceivables,
      unpaidShopReceivables,
      totalRefunds,
      courtBreakdown,
      topMembers,
      recentTransactions: [...txInRange].reverse()
    };
  }

  function createDemoBooking(payload) {
    const code = 'PB-' + Math.random().toString(36).toUpperCase().replace(/[^A-Z0-9]/g, '').slice(0, 6).padEnd(6, '8');
    const guestToken = 'bk_demo_' + Math.random().toString(36).slice(2, 10);
    const court = _state.courts.find(c => Number(c.id) === Number(payload.courtId)) || _state.courts[0];
    const quote = computeQuote(court.id, payload.date, payload.time, payload.duration);
    const user = getActiveAccount();

    const booking = {
      id: code,
      courtId: Number(court.id),
      courtName: court.name,
      courtType: court.type,
      accountId: user ? user.id : null,
      name: payload.customerName,
      phone: payload.customerPhone,
      date: payload.date,
      time: payload.time,
      endTime: minsToTime(timeToMins(payload.time) + Number(payload.duration || 60)),
      duration: Number(payload.duration || 60),
      players: Number(payload.players || 4),
      skillLevel: payload.skillLevel || 'Giao lưu (DUPR 3.0)',
      courtFee: quote.courtFee,
      posTotal: 0,
      discountAmount: quote.discountAmount,
      amountDue: quote.amountDue,
      paidAmount: 0,
      paymentStatus: 'unpaid',
      paymentMethod: payload.paymentMethod || 'cash',
      status: payload.status || 'pending',
      holdExpiresAtUtc: new Date(Date.now() + 15 * 60000).toISOString(),
      accessToken: guestToken,
      checkInToken: `SPK1.${code}.DEMO_TOKEN`,
      qrSvg: generateQrSvg(`SPK1.${code}.DEMO_TOKEN`),
      posItems: []
    };

    _state.bookings.unshift(booking);
    saveState();
    return { ok: true, booking, guestAccessToken: guestToken };
  }

  function createDemoOrder(payload) {
    const items = (payload.items || []).map(row => {
      const prod = _state.products.find(p => Number(p.id) === Number(row.productId));
      if (!prod) return null;
      const qty = Math.max(1, Number(row.qty) || 1);
      prod.stock = Math.max(0, (Number(prod.stock) || 0) - qty);
      return {
        productId: prod.id,
        name: prod.name,
        qty,
        price: prod.price
      };
    }).filter(Boolean);

    const totalAmount = items.reduce((s, it) => s + it.price * it.qty, 0);
    const code = 'ORD-' + Math.floor(2700 + Math.random() * 7000);

    if (payload.bookingCode) {
      const targetBk = _state.bookings.find(b => b.id.toUpperCase() === String(payload.bookingCode).toUpperCase());
      if (targetBk) {
        targetBk.posItems = [...(targetBk.posItems || []), ...items.map(i => ({ id: i.productId, name: i.name, price: i.price, qty: i.qty }))];
        targetBk.posTotal = (Number(targetBk.posTotal) || 0) + totalAmount;
        targetBk.amountDue = (Number(targetBk.amountDue) || 0) + totalAmount;
      }
    }

    const ord = {
      code,
      customerName: payload.customerName,
      phone: payload.phone,
      address: payload.address || 'Nhận tại quầy Pro Shop',
      bookingCode: payload.bookingCode || null,
      totalAmount,
      paidAmount: 0,
      paymentStatus: 'unpaid',
      paymentMethod: payload.paymentMethod || 'cash',
      status: 'pending',
      createdAt: `${isoDateOffset(0)} ${minsToTime(getVietnamNow().getUTCHours() * 60 + getVietnamNow().getUTCMinutes())}`,
      items
    };

    _state.orders.unshift(ord);
    saveState();
    return { ok: true, order: ord, guestAccessToken: 'ord_demo_' + Math.random().toString(36).slice(2, 8) };
  }

  function simulatePayPalCreate(targetType, targetCode) {
    const clean = String(targetCode || '').toUpperCase();
    let amountVnd = 150000;
    if (targetType === 'order') {
      const ord = _state.orders.find(o => o.code.toUpperCase() === clean);
      if (ord) amountVnd = Math.max(0, ord.totalAmount - (ord.paidAmount || 0));
    } else {
      const bk = _state.bookings.find(b => b.id.toUpperCase() === clean);
      if (bk) amountVnd = Math.max(0, (bk.amountDue || bk.courtFee) - (bk.paidAmount || 0));
    }

    const rate = _state.paypalConfig.vndPerUsd || 25400;
    const amountUsd = Math.max(0.01, Math.round((amountVnd / rate) * 100) / 100);
    const attemptCode = 'PP-DEMO-' + Math.floor(1000 + Math.random() * 8999);
    const payPalOrderId = 'DEMO-ORD-' + Math.random().toString(36).toUpperCase().slice(2, 10);

    const record = {
      attemptCode,
      targetType,
      targetCode: clean,
      amountVnd,
      amountUsd,
      exchangeRateVndPerUsd: rate,
      payPalOrderId,
      payPalCaptureId: null,
      payPalRefundId: null,
      localStatus: 'AwaitingApproval',
      createdAtUtc: new Date().toISOString()
    };
    _state.paypalTransactions.unshift(record);
    saveState();
    return record;
  }

  function simulatePayPalCapture(attemptCode, targetType, targetCode) {
    let tx = _state.paypalTransactions.find(t => t.attemptCode === attemptCode || (targetCode && t.targetCode === targetCode));
    if (!tx) {
      tx = simulatePayPalCreate(targetType || 'booking', targetCode || 'PB-2601E5');
    }
    if (tx.localStatus !== 'Completed') {
      tx.localStatus = 'Completed';
      tx.payPalCaptureId = tx.payPalCaptureId || ('DEMO-CAP-' + Math.random().toString(36).toUpperCase().slice(2, 10));

      if (tx.targetType === 'order') {
        const ord = _state.orders.find(o => o.code === tx.targetCode);
        if (ord) {
          ord.paidAmount = ord.totalAmount;
          ord.paymentStatus = 'paid';
          ord.paymentMethod = 'paypal';
        }
      } else {
        const bk = _state.bookings.find(b => b.id === tx.targetCode);
        if (bk) {
          bk.paidAmount = bk.amountDue;
          bk.paymentStatus = 'paid';
          bk.paymentMethod = 'paypal';
          if (bk.status === 'pending') bk.status = 'approved';
        }
      }

      _state.transactions.push({
        id: 'TX-' + Math.floor(1100 + Math.random() * 8800),
        type: 'collect',
        targetType: tx.targetType,
        targetCode: tx.targetCode,
        customerName: _state.bookings.find(b => b.id === tx.targetCode)?.name || _state.orders.find(o => o.code === tx.targetCode)?.customerName || 'Khách Demo',
        method: 'paypal',
        referenceCode: tx.payPalCaptureId,
        note: 'PayPal Sandbox UI Demo (Giả lập)',
        amount: tx.amountVnd,
        recordedBy: 'PayPal Sandbox Demo',
        date: isoDateOffset(0),
        createdAt: `${isoDateOffset(0)} ${minsToTime(getVietnamNow().getUTCHours() * 60 + getVietnamNow().getUTCMinutes())}`
      });
      saveState();
    }

    const bk = tx.targetType === 'booking' ? _state.bookings.find(b => b.id === tx.targetCode) : null;
    const ord = tx.targetType === 'order' ? _state.orders.find(o => o.code === tx.targetCode) : null;
    return {
      ok: true,
      ...tx,
      environment: 'Sandbox',
      currencyCode: 'USD',
      booking: bk,
      order: ord
    };
  }

  // Local Predefined Assistant Brain (No Gemini API Key, 100% client-side)
  function askAssistant(rawQuestion) {
    const q = String(rawQuestion || '').toLowerCase();
    const cms = _state.cmsConfig;
    const today = isoDateOffset(0);
    const stdPrice = Number(cms.courtPrice || 120000).toLocaleString('vi-VN');
    const peakPrice = (Number(cms.courtPrice || 120000) + Number(cms.peakSurcharge || 30000)).toLocaleString('vi-VN');

    if (q.includes('giá') || q.includes('tiền') || q.includes('bao nhiêu') || q.includes('ưu đãi') || q.includes('vip')) {
      return {
        ok: true,
        pose: 'idea',
        reply: `**Bảng giá thuê sân tham khảo (Dữ liệu Demo):**\n• Giờ tiêu chuẩn (${cms.openTime} – ${cms.peakStart}): **${stdPrice} đ/giờ** (Sân Ngoài trời) · **${(Number(cms.courtPrice || 120000) + 20000).toLocaleString('vi-VN')} đ/giờ** (Sân VIP Indoor).\n• Giờ vàng (${cms.peakStart} – ${cms.closeTime}): **${peakPrice} đ/giờ** (Ngoài trời) · **${(Number(cms.courtPrice || 120000) + Number(cms.peakSurcharge || 30000) + 20000).toLocaleString('vi-VN')} đ/giờ** (VIP Indoor).\n• Hội viên Bạc giảm **5%**, Hội viên Vàng VIP giảm **15%** tự động theo phân đoạn 30 phút.`,
        links: [
          { label: 'Đặt sân & xem báo giá', url: '/Booking' },
          { label: 'Đăng nhập Hội viên Demo', url: '/Account/Login' }
        ],
        suggestedQuestions: [
          'Tối nay 19:00 còn sân trống không?',
          'Pro Shop đang bán những sản phẩm gì?',
          'Hướng dẫn đặt sân và sử dụng vé QR'
        ]
      };
    }

    if (q.includes('trống') || q.includes('19:00') || q.includes('18:00') || q.includes('tối nay') || q.includes('đặt sân') || q.includes('lịch')) {
      const busy19 = _state.bookings
        .filter(b => b.date === today && !['cancelled', 'rejected'].includes(b.status))
        .filter(b => {
          const s = timeToMins(b.time);
          const e = s + Number(b.duration || 60);
          return s < 19 * 60 + 60 && e > 19 * 60;
        })
        .map(b => Number(b.courtId));
      const freeCourts = _state.courts.filter(c => c.isActive && !busy19.includes(Number(c.id)));
      const freeText = freeCourts.length > 0
        ? freeCourts.map(c => `• **${c.name}** (${c.type === 'Indoor' ? 'Trong nhà VIP' : 'Ngoài trời'})`).join('\n')
        : 'Hiện khung 19:00 hôm nay đã kín trên bộ dữ liệu mẫu. Bạn có thể chọn khung 20:30 hoặc ngày mai.';

      return {
        ok: true,
        pose: 'chat',
        reply: `**Kiểm tra nhanh khung 19:00 hôm nay (Demo):**\n${freeText}\n\nBạn có thể mở trang Đặt sân để xem ma trận trực quan cả 4 sân và thử quy trình giữ lịch mẫu.`,
        links: [
          { label: 'Mở trang Đặt sân trực tuyến', url: '/Booking' },
          { label: 'Tra cứu vé của tôi', url: '/Lookup' }
        ],
        suggestedQuestions: [
          'Giá thuê sân 1 giờ tiêu chuẩn và giờ vàng?',
          'Pro Shop đang bán những sản phẩm gì?',
          'Địa chỉ và giờ mở cửa CLB ở đâu?'
        ]
      };
    }

    if (q.includes('pro shop') || q.includes('sản phẩm') || q.includes('nước') || q.includes('vợt') || q.includes('bóng') || q.includes('mua')) {
      const sample = _state.products.slice(0, 5).map(p => `• **${p.name}** — ${Number(p.price).toLocaleString('vi-VN')} đ (Còn ${p.stock})`).join('\n');
      return {
        ok: true,
        pose: 'happy',
        reply: `**Pro Shop & F&B tại sảnh Spike Pickleball Club (Demo):**\n${sample}\n…và còn tổng cộng **${_state.products.length} mặt hàng** gồm nước điện giải, đồ ăn nhẹ và thuê vợt Carbon T700 / bóng Franklin X-40.`,
        links: [
          { label: 'Mở Cửa hàng Pro Shop', url: '/Product' },
          { label: 'Xem danh mục sản phẩm', url: '/Products' }
        ],
        suggestedQuestions: [
          'Hướng dẫn đặt sân và sử dụng vé QR',
          'Tối nay 19:00 còn sân trống không?',
          'Thông tin tài khoản & hạng thẻ hội viên'
        ]
      };
    }

    if (q.includes('qr') || q.includes('hướng dẫn') || q.includes('check-in') || q.includes('paypal') || q.includes('vé')) {
      return {
        ok: true,
        pose: 'idea',
        reply: `**Quy trình đặt sân & Vé QR mẫu trên Spike OS:**\n1. Chọn ngày, sân và khung giờ tại trang **Đặt sân**.\n2. Điền thông tin liên hệ và bấm **Xác nhận đặt sân & Nhận vé QR**.\n3. Hệ thống hiển thị thẻ vé điện tử với mã mẫu \`PB-...\`, hỗ trợ đổi giao diện Light/Dark và tải ảnh xem trước.\n\n*Lưu ý: Đây là bản demo giao diện — mã QR không có giá trị check-in thực tế.*`,
        links: [
          { label: 'Xem mẫu Vé điện tử QR', url: '/qr' },
          { label: 'Tra cứu vé QR', url: '/Lookup' },
          { label: 'Thử Trạm soát vé Staff', url: '/Checkin' }
        ],
        suggestedQuestions: [
          'Địa chỉ và giờ mở cửa CLB ở đâu?',
          'Thông tin tài khoản & hạng thẻ hội viên',
          'Pro Shop đang bán những sản phẩm gì?'
        ]
      };
    }

    if (q.includes('địa chỉ') || q.includes('giờ mở') || q.includes('hotline') || q.includes('liên hệ') || q.includes('clb') || q.includes('ở đâu')) {
      return {
        ok: true,
        pose: 'wave',
        reply: `**Thông tin Spike Pickleball Club (Demo):**\n• Địa chỉ: **${cms.address}**\n• Giờ hoạt động: **${cms.openTime} – ${cms.closeTime}** mỗi ngày\n• Hotline lễ tân: **${cms.hotline}**\n• Quy mô: **${_state.courts.length} sân** (2 sân Indoor VIP mái vòm + 2 sân Outdoor).`,
        links: [
          { label: 'Đặt sân ngay', url: '/Booking' },
          { label: 'Trang chủ CLB', url: '/' }
        ]
      };
    }

    if (q.includes('tài khoản') || q.includes('hội viên') || q.includes('hồ sơ') || q.includes('đăng nhập') || q.includes('admin') || q.includes('staff')) {
      return {
        ok: true,
        pose: 'chat',
        reply: `**Hỗ trợ Tài khoản & Vai trò Demo:**\nBạn có thể vào trang **Đăng nhập** để chuyển nhanh giữa các vai trò mô phỏng (Hội viên Vàng VIP, Nhân viên Staff, Quản trị viên Admin) mà không cần mật khẩu thật, hoặc mở **Hồ sơ cá nhân** để thử chọn & tải ảnh đại diện xem trước.`,
        links: [
          { label: 'Chuyển vai trò / Đăng nhập', url: '/Account/Login' },
          { label: 'Hồ sơ Vận động viên', url: '/Account/Profile' },
          { label: 'Admin Dashboard', url: '/Admin' }
        ]
      };
    }

    return {
      ok: true,
      pose: 'chat',
      reply: `Mình đã ghi nhận câu hỏi: *"${rawQuestion}"*.\nHiện mình đang chạy ở chế độ **AI Demo — Phản hồi giả lập** không kết nối Gemini API. Bạn có thể hỏi mình về **sân trống**, **bảng giá**, **Pro Shop**, **vé QR**, **giờ mở cửa CLB** hoặc dùng các nút gợi ý bên dưới nhé!`,
      links: [
        { label: 'Đặt sân trực tuyến', url: '/Booking' },
        { label: 'Cửa hàng Pro Shop', url: '/Product' },
        { label: 'Tra cứu vé QR', url: '/Lookup' }
      ]
    };
  }

  function resetDemoData() {
    try {
      localStorage.removeItem(STORAGE_KEY);
      localStorage.removeItem(AUTH_STORAGE_KEY);
    } catch (_) {}
    return ensureLoaded(true).then(() => {
      window.location.reload();
    });
  }

  window.SpikeAsset = spikeAsset;
  window.SpikeRoute = spikeRoute;
  window.SpikeDemo = {
    ensureLoaded,
    getStatePayload,
    getRawState: () => _state,
    saveState,
    getActiveAccount,
    setActiveAccountId,
    computeQuote,
    getTodayLedgerSummary,
    getReportSummary,
    createDemoBooking,
    createDemoOrder,
    simulatePayPalCreate,
    simulatePayPalCapture,
    generateQrSvg,
    askAssistant,
    resetDemoData,
    isoDateOffset
  };
})();
