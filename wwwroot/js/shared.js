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
    imageUrl: '/assets/hero-spike-pickleball.png'
  },
  {
    id: 4,
    name: 'Sân Tiêu Chuẩn 04',
    type: 'Outdoor',
    typeLabel: 'Ngoài trời · Thoáng mát',
    surface: 'Acrylic Pro 6 lớp',
    surcharge: 0,
    basePrice: 120000,
    imageUrl: '/assets/hero-spike-pickleball.png'
  }
];

function normalizeCourtAssetUrl(rawUrl, courtType = 'Outdoor') {
  const clean = String(rawUrl || '').trim();
  if (!clean || clean.toLowerCase().includes('hero-pickleball-club.png')) {
    return String(courtType || '').toLowerCase() === 'indoor'
      ? '/assets/court-vip-arena.png'
      : '/assets/hero-spike-pickleball.png';
  }
  return clean;
}

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
    const token = data && (data.token || data.csrfToken);
    if (token) {
      setCsrfToken(token);
      return token;
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

  // V4-B1: Only refresh CSRF token and retry once if the 400 response is specifically a CSRF validation failure
  if (isMutating && res.status === 400 && !options._retriedCsrf) {
    let isCsrfFailure = res.headers.get('X-Csrf-Error') === 'CSRF_TOKEN_INVALID';
    if (!isCsrfFailure) {
      try {
        const cloned = res.clone();
        const errBody = await cloned.json();
        if (errBody && errBody.code === 'CSRF_TOKEN_INVALID') {
          isCsrfFailure = true;
        }
      } catch (_) {}
    }

    if (isCsrfFailure) {
      const fresh = await refreshCsrfToken();
      if (fresh) {
        headers.set('RequestVerificationToken', fresh);
        res = await fetch(url, { ...fetchOpts, headers, _retriedCsrf: true });
      }
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
// IN-MEMORY SERVER-HYDRATED STATE (P0-11, P2-04, UI-A01: Strict Server Truth)
// ================================================================
let COURTS = [];
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
let TODAY_LEDGER_SUMMARY = null;

// UI-A01 & UI-C03: Explicit Sync State Machine (never fake availability before server confirms)
let SPIKE_SYNC_STATE = 'loading'; // 'initial' | 'loading' | 'ready' | 'refreshing' | 'stale' | 'offline' | 'error'
let HAS_SERVER_STATE = false;
let LAST_SYNCED_AT = null;
let LAST_SYNC_ERROR = '';

function formatSyncTimeHHMMSS(dateObj) {
  if (!dateObj || !(dateObj instanceof Date) || Number.isNaN(dateObj.getTime())) return '';
  const hh = String(dateObj.getHours()).padStart(2, '0');
  const mm = String(dateObj.getMinutes()).padStart(2, '0');
  const ss = String(dateObj.getSeconds()).padStart(2, '0');
  return `${hh}:${mm}:${ss}`;
}

function parseLocalDateIso(dateStr) {
  if (!dateStr || typeof dateStr !== 'string') return null;
  const m = dateStr.trim().match(/^(\d{4})-(\d{2})-(\d{2})$/);
  if (!m) return null;
  const year = Number(m[1]);
  const month = Number(m[2]);
  const day = Number(m[3]);
  const dt = new Date(year, month - 1, day, 0, 0, 0, 0);
  if (dt.getFullYear() !== year || dt.getMonth() !== month - 1 || dt.getDate() !== day) {
    return null;
  }
  return dt;
}

function renderUiStateBanner(stateType, options = {}) {
  const title = options.title || (
    stateType === 'loading' ? 'Đang đồng bộ dữ liệu trực tiếp từ máy chủ...'
      : stateType === 'offline' ? 'Mất kết nối mạng — Chưa thể xác thực trạng thái sân'
      : stateType === 'error' ? 'Không thể tải dữ liệu từ máy chủ SQL Server'
      : stateType === 'stale' ? 'Dữ liệu đang hiển thị từ lần đồng bộ trước'
      : stateType === 'filtered-empty' ? 'Không tìm thấy kết quả phù hợp bộ lọc'
      : 'Chưa có dữ liệu trong hệ thống'
  );
  const message = options.message || options.subtitle || (
    stateType === 'loading' ? 'Hệ thống đang kiểm tra lịch đặt sân, báo giá và tồn kho thực tế. Vui lòng chờ trong giây lát.'
      : stateType === 'offline' ? (LAST_SYNC_ERROR || 'Thiết bị đang ngoại tuyến. Để tránh hiển thị sai sân trống hoặc giá tiền, vui lòng kiểm tra mạng và thử lại.')
      : stateType === 'error' ? (LAST_SYNC_ERROR || 'Máy chủ đang bận hoặc trả về lỗi. Tuyệt đối không hiển thị dữ liệu giả lập khi chưa xác thực.')
      : stateType === 'stale' ? `Cập nhật thành công lần cuối lúc ${formatSyncTimeHHMMSS(LAST_SYNCED_AT) || 'trước đó'}. Hệ thống đang thử kết nối lại.`
      : stateType === 'filtered-empty' ? 'Hãy thử thay đổi từ khóa tìm kiếm, chọn danh mục khác hoặc xóa bộ lọc hiện tại.'
      : 'Hiện chưa có bản ghi nào được ghi nhận trên cơ sở dữ liệu.'
  );
  const icon = options.icon || (
    stateType === 'loading' ? 'lucide:loader-2'
      : stateType === 'offline' ? 'lucide:wifi-off'
      : stateType === 'error' ? 'lucide:alert-triangle'
      : stateType === 'stale' ? 'lucide:clock-alert'
      : stateType === 'filtered-empty' ? 'lucide:filter-x'
      : 'lucide:inbox'
  );
  const actionHtml = options.actionHtml !== undefined ? options.actionHtml : (
    (stateType === 'offline' || stateType === 'error' || stateType === 'stale')
      ? `<button type="button" class="btn btn-primary btn-sm" onclick="retryServerSync()">
           <iconify-icon icon="lucide:refresh-cw" width="15"></iconify-icon>
           <span>Thử kết nối lại ngay</span>
         </button>`
      : (stateType === 'filtered-empty' && options.onResetFilter)
        ? `<button type="button" class="btn btn-outline btn-sm" onclick="${escapeAttr(options.onResetFilter)}">
             <iconify-icon icon="lucide:rotate-ccw" width="15"></iconify-icon>
             <span>Xóa bộ lọc</span>
           </button>`
        : ''
  );

  const compactClass = options.compact ? 'ui-state-compact' : '';
  const roleAttr = (stateType === 'error' || stateType === 'offline') ? 'role="alert"' : 'role="status" aria-live="polite"';

  return `
    <div class="ui-state-card ui-state-${escapeAttr(stateType)} ${compactClass}" data-testid="ui-state-${escapeAttr(stateType)}" ${roleAttr}>
      <div class="ui-state-icon-wrap ${stateType === 'loading' ? 'is-spinning' : ''}">
        <iconify-icon icon="${escapeAttr(icon)}" width="26"></iconify-icon>
      </div>
      <div class="ui-state-content">
        <div class="ui-state-title">${escapeHtml(title)}</div>
        <div class="ui-state-desc">${escapeHtml(message)}</div>
        ${actionHtml ? `<div class="ui-state-actions">${actionHtml}</div>` : ''}
      </div>
    </div>
  `;
}

function updateGlobalSyncBanner() {
  let host = document.getElementById('globalSyncStatusBanner');
  if (!host) {
    const mainEl = document.querySelector('main') || document.querySelector('.admin-content') || document.body;
    if (mainEl && mainEl.parentNode) {
      host = document.createElement('div');
      host.id = 'globalSyncStatusBanner';
      host.className = 'global-sync-banner-host';
      mainEl.insertBefore(host, mainEl.firstChild);
    }
  }
  if (!host) return;

  if (SPIKE_SYNC_STATE === 'ready' || SPIKE_SYNC_STATE === 'loading' || SPIKE_SYNC_STATE === 'refreshing') {
    host.innerHTML = '';
    host.style.display = 'none';
    return;
  }

  host.style.display = 'block';
  if (SPIKE_SYNC_STATE === 'stale') {
    const lastTime = formatSyncTimeHHMMSS(LAST_SYNCED_AT);
    host.innerHTML = `
      <div class="container" style="padding-top:0.75rem;">
        ${renderUiStateBanner('stale', {
          compact: true,
          title: `Cảnh báo dữ liệu cũ — Đồng bộ lần cuối lúc ${lastTime || 'vừa xong'}`,
          message: `${LAST_SYNC_ERROR || 'Không thể làm mới trạng thái từ máy chủ.'} Vui lòng kiểm tra kết nối trước khi chốt giao dịch.`
        })}
      </div>
    `;
  } else if (SPIKE_SYNC_STATE === 'offline' || SPIKE_SYNC_STATE === 'error') {
    host.innerHTML = `
      <div class="container" style="padding-top:0.75rem;">
        ${renderUiStateBanner(SPIKE_SYNC_STATE, { compact: true })}
      </div>
    `;
  }
}

async function retryServerSync() {
  LAST_STATE_ETAG = null;
  const ok = await syncDatabaseFromServer(true);
  if (ok) {
    showToast('Đã đồng bộ máy chủ', 'Dữ liệu sân, lịch đặt và tồn kho đã được cập nhật mới nhất.', 'success');
  }
  return ok;
}

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

function isPendingBookingHoldActive(b) {
  if (!b || b.status !== 'pending') return false;
  if (b.effectiveStatus === 'expired' || b.derivedPhase === 'expired_hold') return false;
  if (b.holdExpiresAtUtc) {
    const expMs = new Date(b.holdExpiresAtUtc).getTime();
    if (!Number.isNaN(expMs)) {
      return expMs > Date.now();
    }
  }
  return true;
}

function getPendingHoldRemainingLabel(b) {
  if (!b || b.status !== 'pending') return '';
  if (!b.holdExpiresAtUtc) return 'Đang giữ chỗ tạm (chờ duyệt)';
  const expMs = new Date(b.holdExpiresAtUtc).getTime();
  if (Number.isNaN(expMs)) return 'Đang giữ chỗ tạm';
  const diffSec = Math.floor((expMs - Date.now()) / 1000);
  if (diffSec <= 0) return 'Đã hết thời gian giữ chỗ';
  const mins = Math.ceil(diffSec / 60);
  const expDate = new Date(expMs);
  const hh = String(expDate.getHours()).padStart(2, '0');
  const mm = String(expDate.getMinutes()).padStart(2, '0');
  return `Giữ chỗ đến ${hh}:${mm} (còn ${mins} phút)`;
}

function statusBadgeClass(status, b = null) {
  if (status === 'playing' && b && b.derivedPhase === 'overdue') {
    return 'badge-warning';
  }
  if (status === 'pending' && b && !isPendingBookingHoldActive(b)) {
    return 'badge-danger';
  }
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
  if (status === 'playing' && b && b.derivedPhase === 'overdue') {
    return 'Quá giờ — chờ kết ca';
  }
  if (status === 'pending' && b && !isPendingBookingHoldActive(b)) {
    return 'Hết hạn giữ chỗ';
  }
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
 * V4-A2: Server-authoritative booking lifecycle helper.
 * NEVER mutates b.status on the client! Only computes non-destructive b.derivedPhase ('overdue', 'live', 'starting_soon', 'expired_hold').
 */
function reconcileBookingsByRealTime() {
  const todayIso = getTodayStr();
  const now = new Date();
  const nowMins = now.getHours() * 60 + now.getMinutes();
  let hasChanged = false;

  const updateList = list => {
    if (!Array.isArray(list)) return;
    list.forEach(b => {
      if (!b || !b.status) return;
      const prevPhase = b.derivedPhase || null;
      const bDate = b.date || todayIso;
      const startMins = timeToMinutes(b.time || '17:00');
      const dur = Number(b.duration) || 60;
      const endMins = startMins + dur;

      let nextPhase = null;
      if (b.status === 'playing') {
        if (bDate < todayIso || (bDate === todayIso && nowMins >= endMins)) {
          nextPhase = 'overdue';
        } else {
          nextPhase = 'live';
        }
      } else if (b.status === 'pending') {
        if (!isPendingBookingHoldActive(b)) {
          nextPhase = 'expired_hold';
        }
      } else if (b.status === 'approved') {
        if (bDate === todayIso && nowMins >= startMins - 30 && nowMins < endMins) {
          nextPhase = 'starting_soon';
        }
      }

      if (prevPhase !== nextPhase) {
        b.derivedPhase = nextPhase;
        hasChanged = true;
      }
    });
  };

  updateList(BOOKINGS);
  updateList(MY_BOOKINGS);

  return hasChanged;
}

async function syncDatabaseFromServer(force = false) {
  SPIKE_SYNC_STATE = HAS_SERVER_STATE ? 'refreshing' : 'loading';
  window.dispatchEvent(new CustomEvent('pickleball:sync-status', { detail: { state: SPIKE_SYNC_STATE } }));

  try {
    const headers = { 'Accept': 'application/json' };
    if (!force && LAST_STATE_ETAG && HAS_SERVER_STATE) {
      headers['If-None-Match'] = LAST_STATE_ETAG;
    }

    const res = await fetch('/api/db/state', {
      headers,
      credentials: 'same-origin',
      cache: 'no-cache'
    });

    if (res.status === 304 && HAS_SERVER_STATE) {
      SPIKE_SYNC_STATE = 'ready';
      LAST_SYNCED_AT = new Date();
      LAST_SYNC_ERROR = '';
      updateGlobalSyncBanner();
      if (reconcileBookingsByRealTime()) {
        window.dispatchEvent(new CustomEvent('pickleball:db-synced', { detail: { unchanged: true } }));
      }
      window.dispatchEvent(new CustomEvent('pickleball:sync-status', { detail: { state: SPIKE_SYNC_STATE } }));
      return true;
    }

    if (!res.ok) {
      LAST_SYNC_ERROR = `Máy chủ trả về mã lỗi HTTP ${res.status} khi tải trạng thái CLB.`;
      SPIKE_SYNC_STATE = HAS_SERVER_STATE ? 'stale' : 'error';
      updateGlobalSyncBanner();
      window.dispatchEvent(new CustomEvent('pickleball:sync-status', { detail: { state: SPIKE_SYNC_STATE, error: LAST_SYNC_ERROR } }));
      window.dispatchEvent(new CustomEvent('pickleball:db-sync-failed', { detail: { state: SPIKE_SYNC_STATE, error: LAST_SYNC_ERROR } }));
      return false;
    }

    const etag = res.headers.get('ETag');
    if (etag) LAST_STATE_ETAG = etag;

    const data = await res.json();
    if (!data || !data.ok) {
      LAST_SYNC_ERROR = (data && data.error) || 'Phản hồi dữ liệu từ máy chủ không hợp lệ.';
      SPIKE_SYNC_STATE = HAS_SERVER_STATE ? 'stale' : 'error';
      updateGlobalSyncBanner();
      window.dispatchEvent(new CustomEvent('pickleball:sync-status', { detail: { state: SPIKE_SYNC_STATE, error: LAST_SYNC_ERROR } }));
      window.dispatchEvent(new CustomEvent('pickleball:db-sync-failed', { detail: { state: SPIKE_SYNC_STATE, error: LAST_SYNC_ERROR } }));
      return false;
    }

    IS_STAFF_OR_ADMIN = Boolean(data.isStaffOrAdmin);
    if (data.currentUser) {
      SERVER_USER = data.currentUser;
    } else if (window.SERVER_AUTH_USER && window.SERVER_AUTH_USER.id) {
      SERVER_USER = window.SERVER_AUTH_USER;
    } else {
      SERVER_USER = null;
    }
    CURRENT_USER = SERVER_USER;

    if (Array.isArray(data.courts)) {
      COURTS = data.courts.map(c => ({
        ...c,
        imageUrl: normalizeCourtAssetUrl(c.imageUrl, c.type)
      }));
    } else {
      COURTS = [];
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
    TODAY_LEDGER_SUMMARY = data.todayLedgerSummary || null;
    if (data.cmsConfig && typeof data.cmsConfig === 'object') {
      CMS_CONFIG = { ...DEFAULT_CMS, ...data.cmsConfig };
      COURT_PRICE_PER_HOUR = Number(CMS_CONFIG.courtPrice) || 120000;
      PRICING_VERSION = CMS_CONFIG.pricingVersion || '';
    }

    HAS_SERVER_STATE = true;
    SPIKE_SYNC_STATE = 'ready';
    LAST_SYNCED_AT = new Date();
    LAST_SYNC_ERROR = '';

    reconcileBookingsByRealTime();
    applyCmsToFooterAndGlobal();
    updateGlobalSyncBanner();
    window.dispatchEvent(new CustomEvent('pickleball:db-synced', { detail: data }));
    window.dispatchEvent(new CustomEvent('pickleball:sync-status', { detail: { state: SPIKE_SYNC_STATE } }));
    return true;
  } catch (err) {
    console.warn('Không thể tải trạng thái từ máy chủ SQL:', err);
    const isOffline = typeof navigator !== 'undefined' && navigator.onLine === false;
    LAST_SYNC_ERROR = isOffline
      ? 'Thiết bị đang mất kết nối mạng. Không thể kiểm tra trạng thái sân thực tế.'
      : 'Không thể kết nối tới máy chủ dữ liệu SQL Server. Vui lòng thử lại.';
    SPIKE_SYNC_STATE = HAS_SERVER_STATE ? 'stale' : (isOffline ? 'offline' : 'error');
    updateGlobalSyncBanner();
    window.dispatchEvent(new CustomEvent('pickleball:sync-status', { detail: { state: SPIKE_SYNC_STATE, error: LAST_SYNC_ERROR } }));
    window.dispatchEvent(new CustomEvent('pickleball:db-sync-failed', { detail: { state: SPIKE_SYNC_STATE, error: LAST_SYNC_ERROR } }));
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
  document.querySelectorAll('[data-cms="openTime"]').forEach(el => {
    el.textContent = openStr;
  });
  document.querySelectorAll('[data-cms="closeTime"]').forEach(el => {
    el.textContent = closeStr;
  });
  document.querySelectorAll('[data-cms="hours"], [data-cms="openHours"]').forEach(el => {
    el.textContent = `${openStr} – ${closeStr}`;
  });
  document.querySelectorAll('[data-cms="address"]').forEach(el => {
    if (CMS_CONFIG.address) el.textContent = CMS_CONFIG.address;
  });
  document.querySelectorAll('[data-cms="peakStart"]').forEach(el => {
    el.textContent = CMS_CONFIG.peakStart || '17:00';
  });
  const basePrice = Number(CMS_CONFIG.courtPrice) || 120000;
  const peakExtra = Number(CMS_CONFIG.peakSurcharge) ?? 30000;
  document.querySelectorAll('[data-cms="courtPriceFormatted"], [data-cms-price="courtPrice"]').forEach(el => {
    el.textContent = formatMoney(basePrice) + '/h';
  });
  document.querySelectorAll('[data-cms-peak-total="true"]').forEach(el => {
    el.textContent = formatMoney(basePrice + peakExtra) + '/h';
  });
  document.querySelectorAll('[data-cms="peakSurchargeFormatted"]').forEach(el => {
    el.textContent = '+' + formatMoney(peakExtra) + '/h';
  });
  const vipCourt = COURTS.find(c => Number(c.surcharge) > 0);
  const vipSurcharge = vipCourt ? Number(vipCourt.surcharge) : 20000;
  document.querySelectorAll('[data-cms="vipSurchargeFormatted"]').forEach(el => {
    el.textContent = '+' + formatMoney(vipSurcharge) + '/h';
  });
  if (HAS_SERVER_STATE) {
    document.querySelectorAll('[data-cms="courtCount"]').forEach(el => {
      el.textContent = String(COURTS.length);
    });
  }
}

// ================================================================
// SEGMENTED PRICING & CONFLICT HELPERS (P1-08, P1-09, P1-11, P2-05, UI-A06)
// ================================================================
function getCourtById(courtId) {
  if (courtId === null || courtId === undefined || courtId === '') return null;
  const numId = Number(courtId);
  if (Number.isNaN(numId)) return null;
  return COURTS.find(c => Number(c.id) === numId) || null;
}

function getCourtDisplayName(courtId, fallbackName = '') {
  const court = getCourtById(courtId);
  if (court && court.name) return court.name;
  const cleanFallback = String(fallbackName || '').trim();
  if (cleanFallback && !/^Sân\s*#\d+$/i.test(cleanFallback)) {
    return cleanFallback;
  }
  if (courtId !== null && courtId !== undefined && courtId !== '') {
    return `Sân ngừng hoạt động (#${courtId})`;
  }
  return 'Sân không xác định';
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
    // V4-A1: Expired pending holds release their slot immediately on the client as well as the server
    if (b.status === 'pending' && !isPendingBookingHoldActive(b)) continue;

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
// REAL ISO/IEC 18004 SERVER QR RENDERER & V4-QR PREMIUM DIGITAL TICKET
// Uses QRCoder SVG/PNG generated & signed by the server — ZERO fake matrices
// ================================================================
function getBookingQrPngUrl(bookingId, accessToken = '', size = 512, logo = false) {
  if (!bookingId) return '';
  const params = new URLSearchParams();
  if (accessToken) params.set('accessToken', accessToken);
  if (size) params.set('size', String(size));
  if (logo) params.set('logo', 'true');
  const qs = params.toString();
  return `/api/bookings/${encodeURIComponent(bookingId)}/qr.png${qs ? '?' + qs : ''}`;
}

function getBookingQrSvgUrl(bookingId, accessToken = '', size = 512, logo = false) {
  if (!bookingId) return '';
  const params = new URLSearchParams();
  if (accessToken) params.set('accessToken', accessToken);
  if (size) params.set('size', String(size));
  if (logo) params.set('logo', 'true');
  const qs = params.toString();
  return `/api/bookings/${encodeURIComponent(bookingId)}/qr.svg${qs ? '?' + qs : ''}`;
}

function normalizeInlineQrSvg(svgMarkup) {
  if (!svgMarkup || typeof svgMarkup !== 'string') return '';
  let svg = svgMarkup.trim();
  if (!/viewBox\s*=/i.test(svg)) {
    const wMatch = svg.match(/\bwidth="(\d+)"/i);
    const hMatch = svg.match(/\bheight="(\d+)"/i);
    const w = wMatch ? wMatch[1] : '294';
    const h = hMatch ? hMatch[1] : w;
    svg = svg.replace(/<svg\s+/i, `<svg viewBox="0 0 ${w} ${h}" preserveAspectRatio="xMidYMid meet" `);
  }
  return svg.replace(
    /<svg\s+/i,
    '<svg style="width:100%;height:100%;max-width:100%;max-height:100%;display:block;margin:auto;" '
  );
}

function renderBookingQrHtml(booking, size = 192, accessToken = '', includeLogo = false) {
  if (!booking) return '';
  const wellStyle = `width:${size}px;height:${size}px;margin:0 auto;padding:10px;box-sizing:border-box;background:#FFFFFF;border:2px solid #E2E8F0;border-radius:14px;display:flex;align-items:center;justify-content:center;overflow:hidden;box-shadow:0 6px 18px rgba(15,23,42,0.08);`;
  if (!includeLogo && booking.qrSvg && typeof booking.qrSvg === 'string' && booking.qrSvg.includes('<svg')) {
    return `<div class="server-qr-frame spk-qr-well" style="${wellStyle}">${normalizeInlineQrSvg(booking.qrSvg)}</div>`;
  }
  const url = getBookingQrSvgUrl(booking.id, accessToken, Math.max(size * 2, 512), includeLogo);
  return `<div class="server-qr-frame spk-qr-well" style="${wellStyle}"><img src="${escapeAttr(url)}" width="${size - 24}" height="${size - 24}" alt="Mã QR ${escapeAttr(booking.id || '')}" style="display:block;width:100%;height:100%;max-width:100%;max-height:100%;margin:auto;object-fit:contain;background:#FFFFFF;" /></div>`;
}

const SpikeTicket = {
  currentBooking: null,
  currentAccessToken: '',
  currentTheme: 'light',
  includeLogo: false,
  currentContainerId: '',

  getStatusInstruction(b) {
    if (!b) return '';
    if (b.status === 'pending') {
      if (!isPendingBookingHoldActive(b)) {
        return 'Vé giữ chỗ tạm thời đã hết hiệu lực. Vui lòng tạo lượt đặt sân mới.';
      }
      return `${getPendingHoldRemainingLabel(b)} · Xuất trình mã này tại Quầy Lễ Tân để duyệt vé.`;
    }
    if (b.status === 'approved') {
      return 'Vé hợp lệ · Đưa mã QR cách camera quét tại cổng 15–20 cm để nhận sân.';
    }
    if (b.status === 'playing') {
      if (b.derivedPhase === 'overdue') {
        return 'Ca chơi đã quá giờ dự kiến — vui lòng liên hệ Lễ tân để kết ca & tất toán.';
      }
      return 'Đang trong ca thi đấu trên sân · Chúc bạn có trận cầu bùng nổ!';
    }
    if (b.status === 'completed') {
      return 'Ca thi đấu đã hoàn thành · Cảm ơn bạn đã chọn Spike Pickleball Club!';
    }
    return 'Vé đã hủy hoặc hết hiệu lực sử dụng.';
  },

  renderTicketCardHtml(booking, options = {}) {
    if (!booking) return '';
    const theme = options.theme || this.currentTheme || 'light';
    const logo = options.includeLogo !== undefined ? Boolean(options.includeLogo) : this.includeLogo;
    const accessToken = options.accessToken || booking.accessToken || this.currentAccessToken || '';
    const court = getCourtById(booking.courtId);
    const courtName = getCourtDisplayName(booking.courtId, booking.courtName);
    const courtType = booking.courtType || court?.type || (court ? 'Standard' : 'Ngừng hoạt động');
    const endTime = booking.endTime || getEndTime(booking.time || '17:00', booking.duration || 60);
    const amountDue = getBookingGrandTotal(booking);
    const paidAmount = Math.max(0, Number(booking.paidAmount ?? booking.totalPaid ?? 0) || 0);
    const remainingDue = Math.max(0, Number(booking.remainingDue ?? (amountDue - paidAmount)) || 0);
    const paymentStatusRaw = String(booking.paymentStatus || '').toLowerCase();
    let paymentStatusLabel = 'Chưa thanh toán';
    let paymentStatusBadgeCls = 'badge-warning';
    if (paymentStatusRaw === 'refunded') {
      paymentStatusLabel = 'Đã hoàn tiền';
      paymentStatusBadgeCls = 'badge-danger';
    } else if (amountDue > 0 && paidAmount >= amountDue) {
      paymentStatusLabel = 'Đã thanh toán đủ';
      paymentStatusBadgeCls = 'badge-success';
    } else if (paidAmount > 0 && paidAmount < amountDue) {
      paymentStatusLabel = 'Thanh toán 1 phần';
      paymentStatusBadgeCls = 'badge-warning';
    }

    const badgeCls = statusBadgeClass(booking.status, booking);
    const statusText = statusLabel(booking.status, booking);
    const qrHtml = renderBookingQrHtml(booking, 204, accessToken, logo);
    const instruction = this.getStatusInstruction(booking);
    const clubName = CMS_CONFIG.clubName || 'SPIKE PICKLEBALL CLUB';
    const clubAddr = CMS_CONFIG.address || 'Thảo Điền, TP. Thủ Đức, TP.HCM';

    return `
      <div class="spk-ticket-card spk-ticket-${escapeAttr(theme)}" id="spkDigitalTicketCard" data-testid="spike-ticket-card" data-booking-code="${escapeAttr(booking.id || '')}" data-theme="${escapeAttr(theme)}">
        <div class="spk-ticket-header">
          <div class="spk-ticket-brand">
            <span class="spk-ticket-logo-mark" aria-hidden="true">🏓</span>
            <div style="min-width:0;">
              <div class="spk-ticket-club-title">${escapeHtml(clubName)}</div>
              <div class="spk-ticket-club-sub">OFFICIAL DIGITAL COURT PASS · ${escapeHtml(clubAddr)}</div>
            </div>
          </div>
          <div class="spk-ticket-badges">
            <span class="badge ${badgeCls}" data-testid="ticket-status-badge">${escapeHtml(statusText)}</span>
            <span class="badge ${paymentStatusBadgeCls}" data-testid="ticket-payment-badge">${escapeHtml(paymentStatusLabel)}</span>
          </div>
        </div>

        <div class="spk-ticket-body">
          <div class="spk-ticket-hero-row">
            <div class="spk-ticket-hero-court">
              <div class="spk-ticket-field-label">SÂN THI ĐẤU</div>
              <div class="spk-ticket-court-name" data-testid="ticket-court">${escapeHtml(courtName)}</div>
              <div class="spk-ticket-court-meta">${escapeHtml(courtType)} · ${Number(booking.players) || 4} VĐV</div>
            </div>
            <div class="spk-ticket-hero-code">
              <div class="spk-ticket-field-label">MÃ ĐẶT SÂN</div>
              <button type="button" class="spk-ticket-code-pill" data-testid="ticket-code" onclick="SpikeTicket.copyCode('${escapeAttr(booking.id || '')}')" title="Nhấn để sao chép mã đặt sân" aria-label="Sao chép mã đặt sân ${escapeAttr(booking.id || '')}">
                <span>${escapeHtml(booking.id || '')}</span>
                <iconify-icon icon="lucide:copy" width="14" aria-hidden="true"></iconify-icon>
              </button>
            </div>
          </div>

          <div class="spk-ticket-grid">
            <div class="spk-ticket-cell">
              <span class="spk-ticket-field-label">NGÀY THI ĐẤU</span>
              <strong data-testid="ticket-date">${escapeHtml(formatDateVN(booking.date))}</strong>
            </div>
            <div class="spk-ticket-cell">
              <span class="spk-ticket-field-label">KHUNG GIỜ</span>
              <strong data-testid="ticket-datetime">${escapeHtml(booking.time || '17:00')} – ${escapeHtml(endTime)} (${Number(booking.duration) || 60}p)</strong>
            </div>
            <div class="spk-ticket-cell">
              <span class="spk-ticket-field-label">NGƯỜI ĐẶT SÂN</span>
              <strong data-testid="ticket-customer">${escapeHtml(booking.name || 'Khách đặt sân')}</strong>
            </div>
            <div class="spk-ticket-cell">
              <span class="spk-ticket-field-label">TRẠNG THÁI SỔ CÁI</span>
              <strong data-testid="ticket-payment-status">${escapeHtml(paymentStatusLabel)}</strong>
            </div>
          </div>

          <div class="spk-ticket-ledger-strip" role="group" aria-label="Chi tiết thanh toán trên sổ cái">
            <div class="spk-ticket-ledger-item">
              <span class="spk-ticket-field-label">TỔNG PHẢI THANH TOÁN</span>
              <strong class="spk-ticket-ledger-val" data-testid="ticket-amount-due">${formatMoney(amountDue)}</strong>
            </div>
            <div class="spk-ticket-ledger-item">
              <span class="spk-ticket-field-label">ĐÃ THANH TOÁN</span>
              <strong class="spk-ticket-ledger-val is-paid" data-testid="ticket-paid-amount">${formatMoney(paidAmount)}</strong>
            </div>
            <div class="spk-ticket-ledger-item">
              <span class="spk-ticket-field-label">CÒN PHẢI THU</span>
              <strong class="spk-ticket-ledger-val ${remainingDue > 0 ? 'is-due' : 'is-settled'}" data-testid="ticket-remaining-due">${formatMoney(remainingDue)}</strong>
            </div>
          </div>

          ${accessToken && (String(accessToken).startsWith('GST-') || String(accessToken).startsWith('bk_')) ? `
            <div class="spk-ticket-secret-box">
              <div style="display:flex;justify-content:space-between;align-items:center;gap:8px;flex-wrap:wrap;">
                <div style="min-width:0;flex:1;">
                  <div class="spk-ticket-secret-label">🔑 MÃ BẢO MẬT VÉ VÃNG LAI (LƯU LẠI ĐỂ TRA CỨU)</div>
                  <code class="spk-ticket-secret-code" data-testid="ticket-guest-secret">${escapeHtml(accessToken)}</code>
                </div>
                <button type="button" class="btn btn-outline btn-sm" onclick="SpikeTicket.copySecret('${escapeAttr(accessToken)}')">
                  <iconify-icon icon="lucide:copy" width="14" aria-hidden="true"></iconify-icon> Sao chép mã
                </button>
              </div>
            </div>
          ` : ''}

          <div class="spk-ticket-perforation" aria-hidden="true">
            <span class="spk-notch-left"></span>
            <span class="spk-perforation-line"></span>
            <span class="spk-notch-right"></span>
          </div>

          <div class="spk-ticket-qr-section">
            <div class="spk-ticket-qr-controls no-print" role="group" aria-label="Tùy chọn giao diện vé QR">
              <button type="button" class="spk-chip-btn ${theme === 'light' ? 'active' : ''}" aria-pressed="${theme === 'light' ? 'true' : 'false'}" onclick="SpikeTicket.setTheme('light')">
                ☀️ Light Premium
              </button>
              <button type="button" class="spk-chip-btn ${theme === 'dark' ? 'active' : ''}" aria-pressed="${theme === 'dark' ? 'true' : 'false'}" onclick="SpikeTicket.setTheme('dark')">
                🌙 Dark Sport
              </button>
              <button type="button" class="spk-chip-btn ${logo ? 'active' : ''}" aria-pressed="${logo ? 'true' : 'false'}" onclick="SpikeTicket.toggleLogo()">
                🏓 Logo tâm QR: ${logo ? 'Bật (ECC-H)' : 'Tắt (Chuẩn)'}
              </button>
            </div>

            <button type="button" class="spk-ticket-qr-holder" onclick="SpikeTicket.openZoomModal()" title="Nhấn để phóng to QR độ tương phản cao" aria-label="Phóng to mã QR vé ${escapeAttr(booking.id || '')}">
              ${qrHtml}
            </button>

            <div class="spk-ticket-scan-hint">
              ${escapeHtml(instruction)}
            </div>
          </div>
        </div>

        <div class="spk-ticket-actions no-print">
          <button type="button" class="btn btn-primary btn-sm" onclick="SpikeTicket.downloadTicketPng()">
            <iconify-icon icon="lucide:image-down" width="15" aria-hidden="true"></iconify-icon> Tải Vé PNG
          </button>
          <button type="button" class="btn btn-outline btn-sm" onclick="SpikeTicket.downloadQrPng()">
            <iconify-icon icon="lucide:qr-code" width="15" aria-hidden="true"></iconify-icon> Tải QR PNG
          </button>
          <button type="button" class="btn btn-outline btn-sm" onclick="SpikeTicket.downloadQrSvg()">
            <iconify-icon icon="lucide:file-code" width="15" aria-hidden="true"></iconify-icon> Tải SVG
          </button>
          <button type="button" class="btn btn-outline btn-sm" onclick="SpikeTicket.openZoomModal()">
            <iconify-icon icon="lucide:maximize-2" width="15" aria-hidden="true"></iconify-icon> Phóng to
          </button>
          <button type="button" class="btn btn-outline btn-sm" onclick="SpikeTicket.printTicket()">
            <iconify-icon icon="lucide:printer" width="15" aria-hidden="true"></iconify-icon> In vé
          </button>
        </div>
      </div>
    `;
  },

  mountTicket(containerId, booking, options = {}) {
    if (!booking) return;
    this.currentBooking = booking;
    if (options.accessToken !== undefined) {
      this.currentAccessToken = options.accessToken || '';
    } else if (booking.accessToken) {
      this.currentAccessToken = booking.accessToken;
    }
    if (options.theme) this.currentTheme = options.theme;
    if (options.includeLogo !== undefined) this.includeLogo = Boolean(options.includeLogo);
    this.currentContainerId = containerId;

    const el = typeof containerId === 'string' ? document.getElementById(containerId) : containerId;
    if (!el) return;
    el.innerHTML = this.renderTicketCardHtml(booking, {
      theme: this.currentTheme,
      includeLogo: this.includeLogo,
      accessToken: this.currentAccessToken
    });
  },

  refreshMounted() {
    if (!this.currentBooking || !this.currentContainerId) return;
    this.mountTicket(this.currentContainerId, this.currentBooking, {
      theme: this.currentTheme,
      includeLogo: this.includeLogo,
      accessToken: this.currentAccessToken
    });
  },

  setTheme(theme) {
    this.currentTheme = theme === 'dark' ? 'dark' : 'light';
    this.refreshMounted();
  },

  toggleLogo() {
    this.includeLogo = !this.includeLogo;
    this.refreshMounted();
  },

  copyCode(code) {
    const target = code || this.currentBooking?.id || '';
    if (!target) return;
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(target).then(() => {
        showToast('Đã sao chép Mã đặt sân', target, 'success');
      }).catch(() => {});
    }
  },

  copySecret(secret) {
    const target = secret || this.currentAccessToken || '';
    if (!target) return;
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(target).then(() => {
        showToast('Đã sao chép Mã bảo mật vé', 'Hãy lưu mã này để tra cứu hoặc hủy vé khi cần.', 'success');
      }).catch(() => {});
    }
  },

  downloadQrPng() {
    const b = this.currentBooking;
    if (!b || !b.id) return;
    const url = getBookingQrPngUrl(b.id, this.currentAccessToken, 1024, this.includeLogo);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${b.id}-QR.png`;
    document.body.appendChild(a);
    a.click();
    a.remove();
  },

  downloadQrSvg() {
    const b = this.currentBooking;
    if (!b || !b.id) return;
    const url = getBookingQrSvgUrl(b.id, this.currentAccessToken, 1024, this.includeLogo);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${b.id}-QR.svg`;
    document.body.appendChild(a);
    a.click();
    a.remove();
  },

  async downloadTicketPng() {
    const b = this.currentBooking;
    if (!b || !b.id) return;

    try {
      const qrUrl = getBookingQrPngUrl(b.id, this.currentAccessToken, 640, this.includeLogo);
      const qrImg = await new Promise((resolve, reject) => {
        const img = new Image();
        img.crossOrigin = 'anonymous';
        img.onload = () => resolve(img);
        img.onerror = reject;
        img.src = qrUrl;
      });

      const canvas = document.createElement('canvas');
      canvas.width = 1080;
      canvas.height = 1520;
      const ctx = canvas.getContext('2d');
      const isDark = this.currentTheme === 'dark';

      // Outer background
      ctx.fillStyle = isDark ? '#0F172A' : '#F8FAFC';
      ctx.fillRect(0, 0, canvas.width, canvas.height);

      // Card container
      ctx.fillStyle = isDark ? '#1E293B' : '#FFFFFF';
      ctx.strokeStyle = isDark ? '#334155' : '#CBD5E1';
      ctx.lineWidth = 4;
      ctx.beginPath();
      ctx.roundRect(48, 48, 984, 1424, 36);
      ctx.fill();
      ctx.stroke();

      // Top accent banner
      ctx.fillStyle = '#0F172A';
      ctx.beginPath();
      ctx.roundRect(48, 48, 984, 160, [36, 36, 0, 0]);
      ctx.fill();

      ctx.fillStyle = '#F97316';
      ctx.fillRect(48, 202, 984, 8);

      // Header text
      ctx.fillStyle = '#FFFFFF';
      ctx.font = '900 38px system-ui, -apple-system, sans-serif';
      ctx.fillText(CMS_CONFIG.clubName || 'SPIKE PICKLEBALL CLUB', 96, 122);
      ctx.fillStyle = '#94A3B8';
      ctx.font = '600 22px system-ui, -apple-system, sans-serif';
      ctx.fillText('OFFICIAL DIGITAL COURT PASS · VÉ ĐIỆN TỬ CHECK-IN', 96, 164);

      // Booking Code & Court
      const courtName = getCourtDisplayName(b.courtId, b.courtName);
      const endTime = b.endTime || getEndTime(b.time || '17:00', b.duration || 60);
      const amountDue = getBookingGrandTotal(b);
      const paidAmount = Math.max(0, Number(b.paidAmount ?? b.totalPaid ?? 0) || 0);
      const remainingDue = Math.max(0, Number(b.remainingDue ?? (amountDue - paidAmount)) || 0);

      ctx.fillStyle = isDark ? '#94A3B8' : '#64748B';
      ctx.font = '700 22px system-ui, -apple-system, sans-serif';
      ctx.fillText('SÂN THI ĐẤU', 96, 280);
      ctx.fillText('MÃ ĐẶT SÂN', 740, 280);

      ctx.fillStyle = isDark ? '#F8FAFC' : '#0F172A';
      ctx.font = '900 44px system-ui, -apple-system, sans-serif';
      ctx.fillText(courtName, 96, 336);

      ctx.fillStyle = '#F97316';
      ctx.font = '900 44px ui-monospace, SFMono-Regular, Consolas, monospace';
      ctx.fillText(b.id, 740, 336);

      // Metadata rows
      const drawCell = (label, value, x, y, highlightColor = '') => {
        ctx.fillStyle = isDark ? '#94A3B8' : '#64748B';
        ctx.font = '700 20px system-ui, -apple-system, sans-serif';
        ctx.fillText(label, x, y);
        ctx.fillStyle = highlightColor || (isDark ? '#F8FAFC' : '#0F172A');
        ctx.font = '800 28px system-ui, -apple-system, sans-serif';
        ctx.fillText(value, x, y + 38);
      };

      drawCell('NGÀY THI ĐẤU', formatDateVN(b.date), 96, 420);
      drawCell('KHUNG GIỜ', `${b.time || '17:00'} – ${endTime} (${Number(b.duration) || 60}p)`, 560, 420);
      drawCell('NGƯỜI ĐẶT SÂN', b.name || 'Khách đặt sân', 96, 515);
      drawCell('TỔNG PHẢI TT', formatMoney(amountDue), 560, 515);
      drawCell('ĐÃ THANH TOÁN', formatMoney(paidAmount), 96, 600, '#16A34A');
      drawCell('CÒN PHẢI THU', formatMoney(remainingDue), 560, 600, remainingDue > 0 ? '#EA580C' : '#16A34A');

      // Perforation divider
      ctx.strokeStyle = isDark ? '#475569' : '#CBD5E1';
      ctx.setLineDash([14, 12]);
      ctx.lineWidth = 3;
      ctx.beginPath();
      ctx.moveTo(88, 650);
      ctx.lineTo(992, 650);
      ctx.stroke();
      ctx.setLineDash([]);

      // Pure white QR Well (#FFFFFF mandatory for 100% scanner accuracy)
      const wellSize = 600;
      const wellX = (canvas.width - wellSize) / 2;
      const wellY = 700;
      ctx.fillStyle = '#FFFFFF';
      ctx.strokeStyle = '#E2E8F0';
      ctx.lineWidth = 4;
      ctx.beginPath();
      ctx.roundRect(wellX, wellY, wellSize, wellSize, 28);
      ctx.fill();
      ctx.stroke();

      ctx.imageSmoothingEnabled = false;
      ctx.drawImage(qrImg, wellX + 36, wellY + 36, wellSize - 72, wellSize - 72);

      // Footer instruction
      ctx.fillStyle = isDark ? '#E2E8F0' : '#0F172A';
      ctx.font = '700 24px system-ui, -apple-system, sans-serif';
      ctx.textAlign = 'center';
      ctx.fillText(this.getStatusInstruction(b), canvas.width / 2, 1365);

      ctx.fillStyle = isDark ? '#94A3B8' : '#64748B';
      ctx.font = '600 20px system-ui, -apple-system, sans-serif';
      ctx.fillText(`Trạng thái: ${statusLabel(b.status, b)} · Hotline CLB: ${CMS_CONFIG.hotline || '0988.765.432'}`, canvas.width / 2, 1412);

      const dataUrl = canvas.toDataURL('image/png');
      const a = document.createElement('a');
      a.href = dataUrl;
      a.download = `Spike-Ticket-${b.id}.png`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      showToast('Đã tải Vé điện tử PNG', `Tệp Spike-Ticket-${b.id}.png đã sẵn sàng.`, 'success');
    } catch (err) {
      this.downloadQrPng();
    }
  },

  openZoomModal() {
    const b = this.currentBooking;
    if (!b || !b.id) return;
    this._lastZoomTrigger = document.activeElement;
    let overlay = document.getElementById('spkQrFullscreenOverlay');
    if (!overlay) {
      overlay = document.createElement('div');
      overlay.id = 'spkQrFullscreenOverlay';
      overlay.className = 'modal-overlay';
      overlay.setAttribute('role', 'dialog');
      overlay.setAttribute('aria-modal', 'true');
      overlay.setAttribute('aria-label', `Mã QR phóng to cho vé ${b.id}`);
      overlay.style.zIndex = '9999';
      overlay.addEventListener('click', e => {
        if (e.target === overlay) SpikeTicket.closeZoomModal();
      });
      document.body.appendChild(overlay);
    }

    const svgUrl = getBookingQrSvgUrl(b.id, this.currentAccessToken, 768, this.includeLogo);
    overlay.innerHTML = `
      <div class="modal-dialog" style="background:#FFFFFF;color:#0F172A;padding:28px;border-radius:24px;max-width:440px;width:92vw;text-align:center;box-shadow:0 25px 60px rgba(0,0,0,0.45);">
        <div style="font-size:0.75rem;font-weight:800;letter-spacing:0.08em;color:#475569;margin-bottom:4px;">CHẾ ĐỘ QUÉT ĐỘ TƯƠNG PHẢN TỐI ĐA</div>
        <div style="font-size:1.35rem;font-weight:900;color:#0F172A;font-family:var(--font-mono);margin-bottom:14px;">${escapeHtml(b.id)}</div>
        <div class="spk-qr-well" style="background:#FFFFFF;padding:16px;border:3px solid #0F172A;border-radius:18px;display:inline-block;">
          <img src="${escapeAttr(svgUrl)}" alt="QR ${escapeAttr(b.id)}" style="width:min(320px, 72vw);height:min(320px, 72vw);display:block;background:#FFFFFF;" />
        </div>
        <div style="margin-top:14px;font-size:0.88rem;font-weight:600;color:#334155;">
          ${escapeHtml(getCourtDisplayName(b.courtId, b.courtName))} · ${escapeHtml(b.time || '')} (${escapeHtml(formatDateVN(b.date))})
        </div>
        <div style="margin-top:18px;">
          <button type="button" id="btnCloseQrZoomModal" class="btn btn-primary" style="width:100%;" onclick="SpikeTicket.closeZoomModal()">
            Đóng màn hình phóng to (Esc)
          </button>
        </div>
      </div>
    `;
    overlay.classList.add('open');
    overlay.setAttribute('aria-hidden', 'false');
    setTimeout(() => {
      const closeBtn = document.getElementById('btnCloseQrZoomModal');
      if (closeBtn) closeBtn.focus();
    }, 20);
  },

  closeZoomModal() {
    const overlay = document.getElementById('spkQrFullscreenOverlay');
    if (overlay) {
      overlay.classList.remove('open');
      overlay.setAttribute('aria-hidden', 'true');
    }
    if (this._lastZoomTrigger && typeof this._lastZoomTrigger.focus === 'function') {
      try { this._lastZoomTrigger.focus(); } catch (_) {}
    }
  },

  printTicket() {
    window.print();
  }
};
window.SpikeTicket = SpikeTicket;

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
// MODAL & TOAST UI HELPERS (XSS-Safe & Accessible Focus Trap — UI-C02)
// ================================================================
const _modalTriggerElements = new Map();

function getFocusableModalElements(container) {
  if (!container) return [];
  return Array.from(
    container.querySelectorAll(
      'button:not([disabled]), [href], input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
    )
  ).filter(el => el.offsetParent !== null || el === document.activeElement);
}

function openModal(id) {
  const el = document.getElementById(id);
  if (!el) return;
  if (document.activeElement && document.activeElement !== document.body) {
    _modalTriggerElements.set(id, document.activeElement);
  }
  el.setAttribute('role', 'dialog');
  el.setAttribute('aria-modal', 'true');
  el.setAttribute('aria-hidden', 'false');
  el.classList.add('open');

  setTimeout(() => {
    const focusables = getFocusableModalElements(el);
    if (focusables.length > 0) {
      focusables[0].focus();
    }
  }, 20);
}

function closeModal(id) {
  const el = document.getElementById(id);
  if (!el) return;
  el.classList.remove('open');
  el.setAttribute('aria-hidden', 'true');
  const trigger = _modalTriggerElements.get(id);
  if (trigger && typeof trigger.focus === 'function' && document.contains(trigger)) {
    try { trigger.focus(); } catch (_) {}
  }
  _modalTriggerElements.delete(id);
}

document.addEventListener('keydown', e => {
  const openModals = Array.from(document.querySelectorAll('.modal-overlay.open, .modal-backdrop.open'));
  if (openModals.length === 0) return;
  const topModal = openModals[openModals.length - 1];

  if (e.key === 'Escape') {
    e.preventDefault();
    if (topModal.id === 'spkQrFullscreenOverlay') {
      SpikeTicket.closeZoomModal();
    } else if (topModal.id) {
      closeModal(topModal.id);
    } else {
      topModal.classList.remove('open');
    }
    return;
  }

  if (e.key === 'Tab') {
    const focusables = getFocusableModalElements(topModal);
    if (focusables.length === 0) return;
    const first = focusables[0];
    const last = focusables[focusables.length - 1];
    if (e.shiftKey) {
      if (document.activeElement === first || !topModal.contains(document.activeElement)) {
        e.preventDefault();
        last.focus();
      }
    } else {
      if (document.activeElement === last || !topModal.contains(document.activeElement)) {
        e.preventDefault();
        first.focus();
      }
    }
  }
});

window.addEventListener('online', () => {
  syncDatabaseFromServer(true);
});

window.addEventListener('offline', () => {
  LAST_SYNC_ERROR = 'Thiết bị vừa mất kết nối mạng. Dữ liệu trên màn hình có thể chưa phải mới nhất.';
  SPIKE_SYNC_STATE = HAS_SERVER_STATE ? 'stale' : 'offline';
  updateGlobalSyncBanner();
  window.dispatchEvent(new CustomEvent('pickleball:sync-status', { detail: { state: SPIKE_SYNC_STATE, error: LAST_SYNC_ERROR } }));
});

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
