/* ================================================================
   SPIKE PICKLEBALL CLUB — SHARED CORE, SECURITY & STATE ENGINE
   Server-Authoritative Architecture (V2 Hardened: CSRF, ETag, Real QR)
   ================================================================ */

const DEFAULT_COURTS = [
  {
    id: 1,
    name: 'Sân VIP 01 (Arena A)',
    type: 'Indoor',
    typeLabel: 'Trong nhà · Mái vòm & Đèn LED Pro',
    surface: 'Acrylic Cushion 8 lớp giảm chấn',
    surcharge: 20000,
    basePrice: 120000,
    imageUrl: '/assets/court-vip-arena.png'
  },
  {
    id: 2,
    name: 'Sân VIP 02 (Arena B)',
    type: 'Indoor',
    typeLabel: 'Trong nhà · Mái vòm & Đèn LED Pro',
    surface: 'Acrylic Cushion 8 lớp giảm chấn',
    surcharge: 20000,
    basePrice: 120000,
    imageUrl: '/assets/court-vip-arena.png'
  },
  {
    id: 3,
    name: 'Sân Tiêu Chuẩn 03',
    type: 'Outdoor',
    typeLabel: 'Ngoài trời · Thoáng mát',
    surface: 'Acrylic Pro 6 lớp',
    surcharge: 0,
    basePrice: 120000,
    imageUrl: '/assets/hero-pickleball-club.png'
  },
  {
    id: 4,
    name: 'Sân Tiêu Chuẩn 04',
    type: 'Outdoor',
    typeLabel: 'Ngoài trời · Thoáng mát',
    surface: 'Acrylic Pro 6 lớp',
    surcharge: 0,
    basePrice: 120000,
    imageUrl: '/assets/hero-pickleball-club.png'
  }
];

const DEFAULT_CMS = {
  heroTag: 'CỤM SÂN PICKLEBALL TIÊU CHUẨN THI ĐẤU',
  heroSubtag: 'Mặt sân Cushion giảm chấn · Đặt lịch trực tuyến 24/7',
  heroTitle: 'TRẢI NGHIỆM SÂN ĐẤU PICKLEBALL CHUYÊN NGHIỆP',
  heroSubtitle: 'Mặt sân chống trượt Acrylic 8 lớp, hệ thống chiếu sáng LED chống chói, đặt lịch trực tuyến nhận mã QR thật và dịch vụ Pro Shop ngay tại sân.',
  courtPrice: 120000,
  peakSurcharge: 30000,
  peakStart: '17:00',
  openTime: '06:00',
  closeTime: '22:00',
  clubTitle: 'Đặc Quyền Hội Viên Spike Pickleball Club',
  clubDesc: 'Tích lũy chi tiêu thực thu trên sổ cái để nâng hạng Hội viên Bạc (giảm 5%) và Hội viên Vàng VIP (giảm 15% toàn bộ khung giờ đặt sân).',
  hotline: '0901 234 567',
  address: 'Thảo Điền, TP. Thủ Đức, TP.HCM',
  pricingVersion: ''
};

// Purge any legacy client-side auth storage (P2-06)
try {
  window.localStorage.removeItem('pb_auth_user');
} catch (_) {}

// ================================================================
// CSRF TOKEN & SAFE FETCH WRAPPER (P1-17)
// ================================================================
function getCsrfToken() {
  const meta = document.querySelector('meta[name="csrf-token"]');
  return meta ? (meta.getAttribute('content') || '').trim() : '';
}

function setCsrfToken(token) {
  if (!token) return;
  let meta = document.querySelector('meta[name="csrf-token"]');
  if (!meta) {
    meta = document.createElement('meta');
    meta.setAttribute('name', 'csrf-token');
    document.head.appendChild(meta);
  }
  meta.setAttribute('content', token);
}

async function refreshCsrfToken() {
  try {
    const res = await fetch('/api/security/csrf', {
      method: 'GET',
      credentials: 'same-origin',
      headers: { 'Accept': 'application/json' },
      cache: 'no-store'
    });
    if (!res.ok) return '';
    const data = await res.json();
    if (data && data.csrfToken) {
      setCsrfToken(data.csrfToken);
      return data.csrfToken;
    }
  } catch (_) {}
  return '';
}

async function apiFetch(url, options = {}) {
  const method = String(options.method || 'GET').toUpperCase();
  const isMutating = ['POST', 'PUT', 'PATCH', 'DELETE'].includes(method);
  const headers = new Headers(options.headers || {});

  if (!headers.has('Accept')) {
    headers.set('Accept', 'application/json');
  }

  if (isMutating) {
    let csrf = getCsrfToken();
    if (!csrf) {
      csrf = await refreshCsrfToken();
    }
    if (csrf) {
      headers.set('RequestVerificationToken', csrf);
    }
  }

  const fetchOpts = {
    ...options,
    method,
    headers,
    credentials: options.credentials || 'same-origin'
  };

  let res = await fetch(url, fetchOpts);

  // If antiforgery token rotated after sign-in/out in another tab, refresh once and retry
  if (isMutating && res.status === 400 && !options._retriedCsrf) {
    const fresh = await refreshCsrfToken();
    if (fresh) {
      headers.set('RequestVerificationToken', fresh);
      res = await fetch(url, { ...fetchOpts, headers, _retriedCsrf: true });
    }
  }

  return res;
}

function generateIdempotencyKey(prefix = 'REQ') {
  if (window.crypto && typeof window.crypto.randomUUID === 'function') {
    return `${prefix}-${window.crypto.randomUUID()}`;
  }
  return `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
}

// ================================================================
// XSS SANITIZATION & SAFE HTML HELPERS (P0-12)
// ================================================================
function escapeHtml(value) {
  if (value === null || value === undefined) return '';
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function escapeAttr(value) {
  return escapeHtml(value);
}

function sanitizeIconHtml(rawIcon) {
  if (!rawIcon) return '<iconify-icon icon="mdi:cup-water" width="26"></iconify-icon>';
  const str = String(rawIcon).trim();
  const match = str.match(/icon=["']([a-zA-Z0-9\-_]+:[a-zA-Z0-9\-_]+)["']/);
  if (match) {
    return `<iconify-icon icon="${match[1]}" width="26"></iconify-icon>`;
  }
  if (/^[a-zA-Z0-9\-_]+:[a-zA-Z0-9\-_]+$/.test(str)) {
    return `<iconify-icon icon="${str}" width="26"></iconify-icon>`;
  }
  return '<iconify-icon icon="mdi:cup-water" width="26"></iconify-icon>';
}

// ================================================================
// DATE & TIME UTILITIES
// ================================================================
function getTodayStr() {
  const d = new Date();
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

function getOffsetDateStr(offsetDays) {
  const d = new Date();
  d.setDate(d.getDate() + offsetDays);
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

function formatDateVN(dateStr) {
  if (!dateStr) return '';
  const parts = String(dateStr).split('-');
  if (parts.length !== 3) return escapeHtml(dateStr);
  const today = getTodayStr();
  const tomorrow = getOffsetDateStr(1);
  const formatted = `${parts[2]}/${parts[1]}/${parts[0]}`;
  if (dateStr === today) return `Hôm nay (${formatted})`;
  if (dateStr === tomorrow) return `Ngày mai (${formatted})`;
  return formatted;
}

function timeToMinutes(t) {
  if (!t || !String(t).includes(':')) return 0;
  const [h, m] = String(t).split(':').map(Number);
  return (h || 0) * 60 + (m || 0);
}

function minutesToTime(mins) {
  const clamped = Math.max(0, Math.min(mins, 24 * 60));
  if (clamped === 24 * 60) return '24:00';
  const h = Math.floor(clamped / 60);
  const m = clamped % 60;
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}`;
}

function getEndTime(startTime, durationMins) {
  return minutesToTime(timeToMinutes(startTime) + (Number(durationMins) || 60));
}

function isTimeOverlap(startA, durA, startB, durB) {
  const a0 = timeToMinutes(startA);
  const a1 = a0 + (Number(durA) || 60);
  const b0 = timeToMinutes(startB);
  const b1 = b0 + (Number(durB) || 60);
  return a0 < b1 && b0 < a1;
}

function formatMoney(v) {
  return Math.round(Number(v) || 0).toLocaleString('vi-VN') + ' đ';
}

function isPeakHour(timeStr) {
  const mins = timeToMinutes(timeStr);
  const peakStartMins = timeToMinutes(CMS_CONFIG.peakStart || '17:00') || (17 * 60);
  return mins >= peakStartMins;
}

// ================================================================
// IN-MEMORY SERVER-HYDRATED STATE (P0-11, P2-04: ETag Conditional Sync)
// ================================================================
let COURTS = [...DEFAULT_COURTS];
let CMS_STATE_VERSION = 0;
let CMS_CONFIG = { ...DEFAULT_CMS };
let PRICING_VERSION = '';
let LAST_STATE_ETAG = null;
let BOOKINGS = [];
let MY_BOOKINGS = [];
let STANDALONE_ORDERS = [];
let COURT_LOCKS = [];
let MENU_ITEMS = [];
let MENU = [];
let SERVER_USER = null;
let CURRENT_USER = null;
let IS_STAFF_OR_ADMIN = false;
let COURT_PRICE_PER_HOUR = 120000;

const getTodayISO = getTodayStr;
const getOffsetDateISO = getOffsetDateStr;
function getNowMinutes() {
  const now = new Date();
  return now.getHours() * 60 + now.getMinutes();
}

const SpikeDB = {
  syncFromSQLServer: (force = false) => syncDatabaseFromServer(force),
  persistAll: () => {}
};

function saveData() {
  window.dispatchEvent(new CustomEvent('pickleball:state-changed'));
}

function getBookingTimingState(b) {
  if (!b) return { phase: 'ended', canCheckIn: false, minsUntilStart: 0, minsRemaining: 0 };
  const todayISO = getTodayStr();
  const bDate = b.date || todayISO;
  const nowMins = getNowMinutes();
  const startMins = timeToMinutes(b.time || '17:00');
  const dur = Number(b.duration) || 60;
  const endMins = startMins + dur;

  if (bDate < todayISO) {
    return { phase: 'past_day', canCheckIn: false, minsUntilStart: -999, minsRemaining: 0 };
  }
  if (bDate > todayISO) {
    return { phase: 'future_day', canCheckIn: false, minsUntilStart: 999, minsRemaining: dur };
  }
  if (nowMins >= endMins) {
    return { phase: 'ended', canCheckIn: false, minsUntilStart: startMins - nowMins, minsRemaining: 0 };
  }
  if (nowMins >= startMins && nowMins < endMins) {
    return { phase: 'live', canCheckIn: true, minsUntilStart: 0, minsRemaining: endMins - nowMins };
  }
  if (nowMins >= startMins - 30 && nowMins < startMins) {
    return { phase: 'checkin_window', canCheckIn: true, minsUntilStart: startMins - nowMins, minsRemaining: dur };
  }
  return { phase: 'upcoming_today', canCheckIn: false, minsUntilStart: startMins - nowMins, minsRemaining: dur };
}

function statusBadgeClass(status) {
  switch (status) {
    case 'playing': return 'badge-success';
    case 'approved':
    case 'occupied':
      return 'badge-primary';
    case 'pending': return 'badge-warning';
    case 'completed': return 'badge-neutral';
    case 'rejected':
    case 'cancelled':
    case 'expired':
    case 'no_show':
      return 'badge-danger';
    default: return 'badge-neutral';
  }
}

function statusLabel(status, b = null) {
  switch (status) {
    case 'playing': return 'Đang thi đấu';
    case 'occupied': return 'Đã kín lịch';
    case 'approved': return b && b.time ? `Chờ check-in (${b.time})` : 'Đã duyệt · Chờ nhận sân';
    case 'pending': return 'Chờ duyệt';
    case 'completed': return 'Hoàn thành';
    case 'cancelled': return 'Đã hủy';
    case 'rejected': return 'Từ chối';
    case 'expired': return 'Quá hạn';
    case 'no_show': return 'Vắng mặt';
    default: return 'Hoàn thành';
  }
}

function paymentBadgeHtml(paidAmount, amountDue, paymentStatus) {
  const paid = Number(paidAmount) || 0;
  const due = Number(amountDue) || 0;
  if (paymentStatus === 'refunded') {
    return '<span class="badge badge-danger" style="font-size:0.68rem;">Đã hoàn tiền</span>';
  }
  if (due > 0 && paid >= due) {
    return '<span class="badge badge-success" style="font-size:0.68rem;">Đã thu đủ</span>';
  }
  if (paid > 0 && paid < due) {
    return `<span class="badge badge-warning" style="font-size:0.68rem;">Thu 1 phần (${formatMoney(paid)}/${formatMoney(due)})</span>`;
  }
  return '<span class="badge badge-neutral" style="font-size:0.68rem;">Chưa thu tiền</span>';
}

function calculateBookingTotal(courtId, startTime, durationMins, posExtra = 0) {
  const seg = calculateSegmentedPricing(courtId, startTime, durationMins);
  const hourlyRate = seg.peakMinutes > 0 && seg.normalMinutes === 0
    ? seg.peakRate
    : seg.standardRate;
  return {
    hourlyRate,
    courtFee: seg.netCourtFee,
    grossCourtFee: seg.grossCourtFee,
    discountAmount: seg.discountAmount,
    posExtra: Number(posExtra) || 0,
    grandTotal: seg.netCourtFee + (Number(posExtra) || 0)
  };
}

function checkSlotConflict(courtId, dateStr, startMins, endMins, excludeBookingId = null) {
  const startTime = minutesToTime(startMins);
  const durationMins = Math.max(30, endMins - startMins);
  const res = checkCourtConflict(courtId, dateStr, startTime, durationMins, excludeBookingId);
  if (!res.conflict) return { hasConflict: false };
  if (res.type === 'lock') {
    const lockRec = COURT_LOCKS.find(l => Number(l.courtId) === Number(courtId) && l.date === dateStr) || { reason: res.reason };
    return { hasConflict: true, type: 'lock', record: lockRec };
  }
  return { hasConflict: true, type: 'booking', record: res.booking || { name: 'Đã đặt', time: startTime, status: 'occupied' } };
}

function reconcileBookingsRealTime() {
  return reconcileBookingsByRealTime();
}

/**
 * Strict booking status helper (P1-07):
 * NEVER auto-transitions 'approved' to 'playing' by wall clock on the client!
 */
function reconcileBookingsByRealTime() {
  const todayIso = getTodayStr();
  const now = new Date();
  const nowMins = now.getHours() * 60 + now.getMinutes();
  let hasChanged = false;

  BOOKINGS.forEach(b => {
    if (!b || !b.status) return;
    const bDate = b.date || todayIso;
    const startMins = timeToMinutes(b.time || '17:00');
    const dur = Number(b.duration) || 60;
    const endMins = startMins + dur;

    if (bDate < todayIso) {
      if (b.status === 'playing') {
        b.status = 'completed';
        hasChanged = true;
      }
    } else if (bDate === todayIso && nowMins >= endMins && b.status === 'playing') {
      b.status = 'completed';
      hasChanged = true;
    }
  });

  return hasChanged;
}

async function syncDatabaseFromServer(force = false) {
  try {
    const headers = { 'Accept': 'application/json' };
    if (!force && LAST_STATE_ETAG) {
      headers['If-None-Match'] = LAST_STATE_ETAG;
    }

    const res = await fetch('/api/db/state', {
      headers,
      credentials: 'same-origin',
      cache: 'no-cache'
    });

    if (res.status === 304) {
      if (reconcileBookingsByRealTime()) {
        window.dispatchEvent(new CustomEvent('pickleball:db-synced', { detail: { unchanged: true } }));
      }
      return true;
    }

    if (!res.ok) return false;
    const etag = res.headers.get('ETag');
    if (etag) LAST_STATE_ETAG = etag;

    const data = await res.json();
    if (!data || !data.ok) return false;

    IS_STAFF_OR_ADMIN = Boolean(data.isStaffOrAdmin);
    if (data.currentUser) {
      SERVER_USER = data.currentUser;
    } else if (window.SERVER_AUTH_USER && window.SERVER_AUTH_USER.id) {
      SERVER_USER = window.SERVER_AUTH_USER;
    } else {
      SERVER_USER = null;
    }
    CURRENT_USER = SERVER_USER;

    if (Array.isArray(data.courts) && data.courts.length > 0) {
      COURTS = data.courts;
    }
    if (Array.isArray(data.bookings)) {
      BOOKINGS = data.bookings;
    }
    if (Array.isArray(data.myBookings)) {
      MY_BOOKINGS = data.myBookings;
    }
    if (Array.isArray(data.orders)) {
      STANDALONE_ORDERS = data.orders;
    }
    if (Array.isArray(data.courtLocks)) {
      COURT_LOCKS = data.courtLocks;
    }
    if (Array.isArray(data.menu)) {
      MENU_ITEMS = data.menu;
      MENU = data.menu;
    }
    if (data.cmsConfig && typeof data.cmsConfig === 'object') {
      CMS_CONFIG = { ...DEFAULT_CMS, ...data.cmsConfig };
      COURT_PRICE_PER_HOUR = Number(CMS_CONFIG.courtPrice) || 120000;
      PRICING_VERSION = CMS_CONFIG.pricingVersion || '';
    }

    reconcileBookingsByRealTime();
    applyCmsToFooterAndGlobal();
    window.dispatchEvent(new CustomEvent('pickleball:db-synced', { detail: data }));
    return true;
  } catch (err) {
    console.warn('Không thể tải trạng thái từ máy chủ SQL:', err);
    return false;
  }
}

function saveBookings() {
  window.dispatchEvent(new CustomEvent('pickleball:state-changed'));
}

function saveCourtLocks() {
  window.dispatchEvent(new CustomEvent('pickleball:state-changed'));
}

function saveMenu() {
  window.dispatchEvent(new CustomEvent('pickleball:state-changed'));
}

async function saveCmsConfig(newConfig) {
  const payload = { ...CMS_CONFIG, ...newConfig };
  try {
    const res = await apiFetch('/api/admin/cms', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      showToast('Không thể lưu CMS', data.error || 'Bạn cần quyền Quản trị viên để lưu cấu hình.', 'error');
      return { ok: false, error: data.error };
    }
    LAST_STATE_ETAG = null;
    await syncDatabaseFromServer(true);
    return { ok: true, cmsConfig: data.cmsConfig };
  } catch (err) {
    showToast('Lỗi kết nối', 'Không thể lưu cấu hình CMS lên máy chủ.', 'error');
    return { ok: false, error: String(err) };
  }
}

function applyCmsToFooterAndGlobal() {
  document.querySelectorAll('[data-cms="hotline"]').forEach(el => {
    if (CMS_CONFIG.hotline) el.textContent = CMS_CONFIG.hotline;
  });
  const openStr = CMS_CONFIG.openTime || '06:00';
  const closeStr = CMS_CONFIG.closeTime || '22:00';
  document.querySelectorAll('[data-cms="hours"], [data-cms="openHours"]').forEach(el => {
    el.textContent = `${openStr} – ${closeStr}`;
  });
  document.querySelectorAll('[data-cms="address"]').forEach(el => {
    if (CMS_CONFIG.address) el.textContent = CMS_CONFIG.address;
  });
  document.querySelectorAll('[data-cms="peakStart"]').forEach(el => {
    el.textContent = CMS_CONFIG.peakStart || '17:00';
  });
}

// ================================================================
// SEGMENTED PRICING & CONFLICT HELPERS (P1-08, P1-09, P1-11, P2-05)
// ================================================================
function getCourtById(courtId) {
  return COURTS.find(c => Number(c.id) === Number(courtId)) || COURTS[0] || DEFAULT_COURTS[0];
}

function getCourtHourlyRate(courtId, timeStr) {
  const court = getCourtById(courtId);
  const base = Number(court?.basePrice) || Number(CMS_CONFIG.courtPrice) || 120000;
  const surcharge = Number(court?.surcharge) || 0;
  const peakExtra = isPeakHour(timeStr || '17:00') ? (Number(CMS_CONFIG.peakSurcharge) || 30000) : 0;
  return base + surcharge + peakExtra;
}

/**
 * Calculates segmented court fee in 30-minute slices across normal and peak boundaries (P1-08)
 */
function calculateSegmentedPricing(courtId, startTime, durationMins, discountPercent = null) {
  const court = getCourtById(courtId);
  const base = Number(court?.basePrice) || Number(CMS_CONFIG.courtPrice) || 120000;
  const surcharge = Number(court?.surcharge) || 0;
  const peakSurcharge = Number(CMS_CONFIG.peakSurcharge) ?? 30000;
  const peakStartMins = timeToMinutes(CMS_CONFIG.peakStart || '17:00') || (17 * 60);

  const standardRate = base + surcharge;
  const peakRate = standardRate + peakSurcharge;

  const totalMins = Math.max(30, Number(durationMins) || 60);
  const slots = Math.round(totalMins / 30);
  const startMins = timeToMinutes(startTime || '17:00');

  let normalMinutes = 0;
  let peakMinutes = 0;
  let normalSubtotal = 0;
  let peakSubtotal = 0;
  const segments = [];

  for (let i = 0; i < slots; i++) {
    const segStart = startMins + i * 30;
    const segEnd = segStart + 30;
    const isPeak = segStart >= peakStartMins;
    const rate = isPeak ? peakRate : standardRate;
    const fee = Math.round(rate / 2);

    if (isPeak) {
      peakMinutes += 30;
      peakSubtotal += fee;
    } else {
      normalMinutes += 30;
      normalSubtotal += fee;
    }

    segments.push({
      start: minutesToTime(segStart),
      end: minutesToTime(segEnd),
      isPeak,
      ratePerHour: rate,
      fee
    });
  }

  const grossCourtFee = normalSubtotal + peakSubtotal;
  const resolvedDiscountPct = discountPercent !== null
    ? Number(discountPercent)
    : Number(SERVER_USER?.discountPercent || 0);

  const discountAmount = resolvedDiscountPct > 0
    ? Math.round((grossCourtFee * (resolvedDiscountPct / 100)) / 1000) * 1000
    : 0;
  const netCourtFee = Math.max(0, grossCourtFee - discountAmount);

  return {
    standardRate,
    peakRate,
    normalMinutes,
    peakMinutes,
    normalSubtotal,
    peakSubtotal,
    grossCourtFee,
    discountPercent: resolvedDiscountPct,
    discountAmount,
    netCourtFee,
    memberTier: SERVER_USER?.memberTier || 'Hội viên Tiêu chuẩn',
    segments
  };
}

function calcBookingPrice(courtId, startTime, durationMins) {
  return calculateSegmentedPricing(courtId, startTime, durationMins).netCourtFee;
}

function checkCourtConflict(courtId, dateStr, startTime, durationMins, excludeBookingId = null) {
  const targetCourtId = Number(courtId);
  const targetDate = dateStr || getTodayStr();

  for (const l of COURT_LOCKS) {
    if (Number(l.courtId) === targetCourtId && l.date === targetDate) {
      if (isTimeOverlap(startTime, durationMins, l.startTime, l.duration)) {
        return {
          conflict: true,
          type: 'lock',
          reason: `Sân đang khóa bảo trì (${l.startTime}–${getEndTime(l.startTime, l.duration)}): ${l.reason || 'Bảo trì'}`
        };
      }
    }
  }

  for (const b of BOOKINGS) {
    if (excludeBookingId && b.id && b.id === excludeBookingId) continue;
    if (['rejected', 'cancelled', 'expired', 'no_show'].includes(b.status)) continue;
    if (Number(b.courtId) === targetCourtId && b.date === targetDate) {
      if (isTimeOverlap(startTime, durationMins, b.time, b.duration)) {
        return {
          conflict: true,
          type: 'booking',
          booking: b,
          reason: `Trùng lịch với khung giờ đã đặt (${b.time}–${getEndTime(b.time, b.duration)})`
        };
      }
    }
  }

  return { conflict: false };
}

function getBookingPosTotal(b) {
  if (!b) return 0;
  if (typeof b.posTotal === 'number' && b.posTotal > 0) return b.posTotal;
  if (!Array.isArray(b.posItems)) return 0;
  return b.posItems.reduce((s, item) => s + (Number(item.price) || 0) * (Number(item.qty) || 0), 0);
}

function getBookingGrandTotal(b) {
  if (!b) return 0;
  if (typeof b.amountDue === 'number' && b.amountDue > 0) return b.amountDue;
  return (Number(b.courtFee) || 0) + getBookingPosTotal(b);
}

// ================================================================
// REAL ISO/IEC 18004 SERVER QR RENDERER (P1-01)
// Uses QRCoder SVG/PNG generated & signed by the server — ZERO fake matrices
// ================================================================
function getBookingQrPngUrl(bookingId, accessToken = '') {
  if (!bookingId) return '';
  const base = `/api/bookings/${encodeURIComponent(bookingId)}/qr.png`;
  return accessToken ? `${base}?accessToken=${encodeURIComponent(accessToken)}` : base;
}

function getBookingQrSvgUrl(bookingId, accessToken = '') {
  if (!bookingId) return '';
  const base = `/api/bookings/${encodeURIComponent(bookingId)}/qr.svg`;
  return accessToken ? `${base}?accessToken=${encodeURIComponent(accessToken)}` : base;
}

function renderBookingQrHtml(booking, size = 160, accessToken = '') {
  if (!booking) return '';
  if (booking.qrSvg && typeof booking.qrSvg === 'string' && booking.qrSvg.includes('<svg')) {
    return `<div class="server-qr-frame" style="width:${size}px;height:${size}px;margin:0 auto;padding:6px;background:#FFFFFF;border:1px solid #E2E8F0;border-radius:10px;display:flex;align-items:center;justify-content:center;overflow:hidden;">${booking.qrSvg.replace('<svg ', `<svg style="width:100%;height:100%;display:block;" `)}</div>`;
  }
  const url = booking.qrSvgUrl || getBookingQrSvgUrl(booking.id, accessToken);
  return `<img src="${escapeAttr(url)}" width="${size}" height="${size}" alt="Mã QR ${escapeAttr(booking.id || '')}" style="display:block;margin:0 auto;border-radius:10px;border:1px solid #E2E8F0;background:#FFFFFF;padding:4px;" />`;
}

// ================================================================
// AUTHENTICATION & SESSION HELPERS (P0-01, P0-02, P2-06)
// ================================================================
function getCurrentUser() {
  if (SERVER_USER && SERVER_USER.id) {
    return SERVER_USER;
  }
  if (window.SERVER_AUTH_USER && window.SERVER_AUTH_USER.id) {
    return window.SERVER_AUTH_USER;
  }
  return null;
}

function getAuthSession() {
  const u = getCurrentUser();
  if (!u || !u.id) return { loggedIn: false };
  return {
    loggedIn: true,
    id: u.id,
    name: u.name || 'Hội viên',
    role: u.role || 'user',
    roleLabel: u.roleLabel || (u.role === 'admin' ? 'Ban Quản Trị CLB' : u.role === 'staff' ? 'Nhân viên CLB' : 'Hội viên CLB')
  };
}

function isSafeLocalUrl(url) {
  return typeof url === 'string' && url.startsWith('/') && !url.startsWith('//') && !url.startsWith('/\\');
}

async function login(identifier, password, redirectUrl = '') {
  try {
    const res = await apiFetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ identifier, password })
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      return { ok: false, error: data.error || 'Tài khoản hoặc mật khẩu không chính xác.' };
    }
    if (data.csrfToken) setCsrfToken(data.csrfToken);

    if (data.mustChangePassword) {
      window.location.href = '/Account/Profile';
      return { ok: true };
    }
    if (isSafeLocalUrl(redirectUrl)) {
      window.location.href = redirectUrl;
      return { ok: true };
    }
    if (data.user && (data.user.role === 'admin' || data.user.role === 'staff')) {
      window.location.href = '/Admin';
    } else {
      window.location.href = '/';
    }
    return { ok: true };
  } catch (_) {
    return { ok: false, error: 'Lỗi kết nối máy chủ khi đăng nhập.' };
  }
}

async function registerUser(payload, redirectUrl = '') {
  try {
    const res = await apiFetch('/api/auth/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || !data.ok) {
      return { ok: false, error: data.error || 'Không thể đăng ký tài khoản.' };
    }
    if (data.csrfToken) setCsrfToken(data.csrfToken);
    window.location.href = isSafeLocalUrl(redirectUrl) ? redirectUrl : '/';
    return { ok: true };
  } catch (_) {
    return { ok: false, error: 'Lỗi kết nối máy chủ khi đăng ký tài khoản.' };
  }
}

function joinClub() {
  const u = getCurrentUser();
  if (u) {
    showToast('Bạn đã là Hội viên CLB', `Hạng hiện tại: ${u.memberTier || 'Hội viên Tiêu chuẩn'}. Đặt sân và thanh toán để tích lũy nâng hạng tự động!`, 'success');
    setTimeout(() => { window.location.href = '/Booking'; }, 800);
    return;
  }
  window.location.href = '/Account/Login?mode=register';
}

function isAdminUser() {
  const u = getCurrentUser();
  return u && (u.role === 'admin' || u.role === 'staff');
}

function isStrictAdmin() {
  const u = getCurrentUser();
  return u && u.role === 'admin';
}

function logoutUser() {
  window.location.href = '/Account/Logout';
}

function requireRole(requiredRole) {
  const user = getCurrentUser();
  if (!user) {
    window.location.href = '/Account/Login';
    return false;
  }
  if (requiredRole === 'admin' && user.role !== 'admin' && user.role !== 'staff') {
    showToast('Truy cập bị từ chối!', 'Trang này chỉ dành cho Ban Quản Trị & Nhân viên vận hành CLB.', 'error');
    setTimeout(() => { window.location.href = '/'; }, 900);
    return false;
  }
  return true;
}

// ================================================================
// MODAL & TOAST UI HELPERS (XSS-Safe)
// ================================================================
function openModal(id) {
  const el = document.getElementById(id);
  if (el) el.classList.add('open');
}

function closeModal(id) {
  const el = document.getElementById(id);
  if (el) el.classList.remove('open');
}

function showToast(title, message = '', type = 'success') {
  let container = document.getElementById('toastContainer');
  if (!container) {
    container = document.createElement('div');
    container.id = 'toastContainer';
    container.className = 'toast-container';
    document.body.appendChild(container);
  }

  const toast = document.createElement('div');
  toast.className = `toast ${type !== 'success' ? type : ''}`;

  const iconName = type === 'error'
    ? 'lucide:alert-circle'
    : type === 'warning'
      ? 'lucide:alert-triangle'
      : 'lucide:check-circle-2';
  const iconColor = type === 'error'
    ? 'var(--color-danger)'
    : type === 'warning'
      ? 'var(--color-warning)'
      : 'var(--color-success)';

  toast.innerHTML = `
    <div style="color:${iconColor};display:flex;padding-top:2px">
      <iconify-icon icon="${iconName}" width="20" height="20"></iconify-icon>
    </div>
    <div>
      <div class="toast-title">${escapeHtml(title)}</div>
      ${message ? `<div class="toast-msg">${escapeHtml(message)}</div>` : ''}
    </div>
  `;

  container.appendChild(toast);
  setTimeout(() => {
    toast.style.opacity = '0';
    toast.style.transition = 'opacity 0.25s ease';
    setTimeout(() => toast.remove(), 260);
  }, 3600);
}

// ================================================================
// INITIALIZATION & CONDITIONAL ETAG POLLING (P2-04)
// ================================================================
document.addEventListener('DOMContentLoaded', () => {
  syncDatabaseFromServer();

  // Conditional ETag check every 15s when tab is visible (304 Not Modified when idle)
  setInterval(() => {
    if (document.visibilityState === 'visible') {
      syncDatabaseFromServer(false);
    }
  }, 15000);

  document.querySelectorAll('.modal-overlay').forEach(overlay => {
    overlay.addEventListener('click', e => {
      if (e.target === overlay) overlay.classList.remove('open');
    });
  });

  const user = getCurrentUser();
  if (user) {
    const nameInput = document.getElementById('bookingName');
    const phoneInput = document.getElementById('bookingPhone');
    if (nameInput && !nameInput.value && user.name) nameInput.value = user.name;
    if (phoneInput && !phoneInput.value && user.phone) phoneInput.value = user.phone;
  }
});
