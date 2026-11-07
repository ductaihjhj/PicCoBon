/* ==========================================================================
   SPIKE PICKLEBALL CLUB — SHARED CORE ENGINE (ASP.NET CORE MVC + SQL SERVER)
   Connected directly to SQL Server (.\SQLEXPRESS -> PickleballClub)
   ========================================================================== */

const DB_SCHEMA_VERSION = 'spike_sqlserver_v6_utf8';

(function purgeLegacyMockData() {
  const rawMenu = localStorage.getItem('pb_menu') || '';
  const rawCms = localStorage.getItem('pb_cms_config') || '';
  const hasMojibake = /\u00E1\u00BB|\u00C4\u0091|\u00C6\u00B0|\u00C3\u00A1/.test(rawMenu + rawCms);
  if (localStorage.getItem('pb_schema_version') !== DB_SCHEMA_VERSION || hasMojibake) {
    localStorage.removeItem('pb_bookings');
    localStorage.removeItem('pb_court_locks');
    localStorage.removeItem('pb_cms_config');
    localStorage.removeItem('pb_menu');
    const oldAuth = JSON.parse(localStorage.getItem('pb_auth_user') || 'null');
    if (oldAuth && oldAuth.email === 'khachhang@pickleball.vn') {
      localStorage.removeItem('pb_auth_user');
    }
    localStorage.setItem('pb_schema_version', DB_SCHEMA_VERSION);
  }
})();

function getTodayISO() {
  const now = new Date();
  const y = now.getFullYear();
  const m = String(now.getMonth() + 1).padStart(2, '0');
  const d = String(now.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

function getOffsetDateISO(offsetDays = 0) {
  const dt = new Date();
  dt.setDate(dt.getDate() + offsetDays);
  const y = dt.getFullYear();
  const m = String(dt.getMonth() + 1).padStart(2, '0');
  const d = String(dt.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

function formatDateVN(isoStr) {
  if (!isoStr) return 'Hôm nay';
  const today = getTodayISO();
  const tomorrow = getOffsetDateISO(1);
  const parts = isoStr.split('-');
  if (parts.length !== 3) return isoStr;
  const formatted = `${parts[2]}/${parts[1]}/${parts[0]}`;
  if (isoStr === today) return `Hôm nay (${formatted})`;
  if (isoStr === tomorrow) return `Ngày mai (${formatted})`;
  return formatted;
}

/* ---------- CMS Website Configuration ---------- */
const DEFAULT_CMS = {
  heroTag: 'USAPA STANDARD ARENA',
  heroSubtag: 'Tổ hợp 4 sân tiêu chuẩn thi đấu quốc tế',
  heroTitle: 'Nâng Tầm Trải Nghiệm Pickleball Đỉnh Cao',
  heroSubtitle: 'Mặt sân Cushion 8 lớp giảm chấn bảo vệ khớp gối, hệ thống chiếu sáng LED chống lóa 1000 Lux và đặt lịch nhận mã QR tức thì.',
  courtPrice: 120000,
  openTime: '06:00',
  closeTime: '22:00',
  hotline: '0901 234 567',
  clubTitle: 'Thẻ Hội Viên Spike VIP',
  clubDesc: 'Giảm ngay 15% giá thuê sân mọi khung giờ, miễn phí nước khoáng điện giải và ưu tiên giữ lịch cố định hàng tuần.'
};

let CMS_CONFIG = JSON.parse(localStorage.getItem('pb_cms_config')) || JSON.parse(JSON.stringify(DEFAULT_CMS));
let COURT_PRICE_PER_HOUR = Number(CMS_CONFIG.courtPrice) || 120000;

function saveCmsConfig(newConfig) {
  CMS_CONFIG = { ...CMS_CONFIG, ...newConfig };
  localStorage.setItem('pb_cms_config', JSON.stringify(CMS_CONFIG));
  COURT_PRICE_PER_HOUR = Number(CMS_CONFIG.courtPrice) || 120000;
  SpikeDB.pushPartialToSQLServer({ cmsConfig: CMS_CONFIG });
  SpikeDB.refreshActivePageViews();
}

/* ---------- Courts Catalog ---------- */
const DEFAULT_COURTS = [
  { id: 1, name: 'Sân VIP 01 (Arena A)', type: 'Indoor',  typeLabel: 'Trong nhà · Mái vòm & Đèn LED Pro', surface: 'USAPA Cushion 8 lớp', basePrice: 120000, surcharge: 20000 },
  { id: 2, name: 'Sân VIP 02 (Arena B)', type: 'Indoor',  typeLabel: 'Trong nhà · Mái vòm & Đèn LED Pro', surface: 'USAPA Cushion 8 lớp', basePrice: 120000, surcharge: 20000 },
  { id: 3, name: 'Sân Tiêu Chuẩn 03',    type: 'Outdoor', typeLabel: 'Ngoài trời · Khán đài thoáng mát', surface: 'Acrylic Hard Court', basePrice: 120000, surcharge: 0 },
  { id: 4, name: 'Sân Tiêu Chuẩn 04',    type: 'Outdoor', typeLabel: 'Ngoài trời · Khán đài thoáng mát', surface: 'Acrylic Hard Court', basePrice: 120000, surcharge: 0 },
];
let COURTS = JSON.parse(localStorage.getItem('pb_courts')) || JSON.parse(JSON.stringify(DEFAULT_COURTS));

/* ---------- Dynamic Pricing ---------- */
function isPeakHour(timeStr) {
  const mins = timeToMinutes(timeStr);
  return mins >= 17 * 60 && mins < 21 * 60;
}

function getCourtHourlyRate(courtId, timeStr = '08:00') {
  const cmsBase = Number(CMS_CONFIG.courtPrice) || 120000;
  const court = getCourtById(Number(courtId));
  const courtBase = (court && Number(court.basePrice) > 0 && Number(court.basePrice) !== 120000)
    ? Number(court.basePrice)
    : cmsBase;
  const vipExtra = court
    ? (Number(court.surcharge) > 0 ? Number(court.surcharge) : (court.type === 'Indoor' ? 20000 : 0))
    : 0;
  const peakExtra = isPeakHour(timeStr) ? 30000 : 0;
  return courtBase + vipExtra + peakExtra;
}

function calculateBookingTotal(courtId, startTime, durationMins, extrasTotal = 0) {
  const hourly = getCourtHourlyRate(courtId, startTime);
  const courtFee = Math.round((durationMins / 60) * hourly);
  return {
    hourlyRate: hourly,
    courtFee,
    extrasTotal: Number(extrasTotal) || 0,
    grandTotal: courtFee + (Number(extrasTotal) || 0)
  };
}

/* ---------- Authentication Connected to SQL Server (tb_Account) ---------- */
let CURRENT_USER = JSON.parse(localStorage.getItem('pb_auth_user')) || null;

function getAuthSession() {
  if (!CURRENT_USER) return { loggedIn: false };
  return { loggedIn: true, ...CURRENT_USER };
}

async function registerUser(userData, redirectUrl) {
  const inputEmail = (userData.email || '').trim().toLowerCase();
  const inputPhone = (userData.phone || '').trim();
  const name = (userData.name || '').trim();
  const pass = (userData.password || '').trim();

  if (!name || !inputPhone || !pass) {
    showToast('Thiếu thông tin', 'Vui lòng nhập họ tên, số điện thoại và mật khẩu.', 'warning');
    return { ok: false, error: 'Vui lòng nhập đầy đủ thông tin bắt buộc.' };
  }

  try {
    const res = await fetch('/api/auth/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name, phone: inputPhone, email: inputEmail, password: pass })
    });
    const data = await res.json();
    if (!res.ok || !data.ok) {
      showToast('Đăng ký thất bại', data.error || 'Tài khoản đã được đăng ký.', 'danger');
      return { ok: false, error: data.error || 'Số điện thoại hoặc Email này đã được sử dụng.' };
    }

    CURRENT_USER = data.user;
    localStorage.setItem('pb_auth_user', JSON.stringify(CURRENT_USER));
    showToast('Đăng ký thành công', `Chào mừng hội viên ${CURRENT_USER.name}!`, 'success');

    setTimeout(() => {
      const urlParams = new URLSearchParams(window.location.search);
      const target = redirectUrl || urlParams.get('redirect');
      window.location.href = target ? decodeURIComponent(target) : '/';
    }, 450);
    return { ok: true, user: CURRENT_USER };
  } catch (err) {
    showToast('Lỗi kết nối', 'Không thể kết nối tới hệ thống, vui lòng thử lại.', 'danger');
    return { ok: false, error: 'Không thể kết nối tới hệ thống.' };
  }
}

async function login(identifier, password, redirectUrl) {
  const input = (identifier || '').trim();
  const pass = (password || '').trim();

  try {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ identifier: input, password: pass })
    });
    const data = await res.json();
    if (!res.ok || !data.ok) {
      showToast('Đăng nhập thất bại', data.error || 'Thông tin đăng nhập không chính xác.', 'danger');
      return { ok: false, error: data.error || 'Email/Số điện thoại hoặc mật khẩu không chính xác.' };
    }

    CURRENT_USER = data.user;
    localStorage.setItem('pb_auth_user', JSON.stringify(CURRENT_USER));
    showToast('Đăng nhập thành công', `Xin chào, ${CURRENT_USER.name}`, 'success');

    setTimeout(() => {
      const urlParams = new URLSearchParams(window.location.search);
      const target = redirectUrl || urlParams.get('redirect');
      if (target) {
        window.location.href = decodeURIComponent(target);
      } else if (CURRENT_USER.role === 'admin') {
        window.location.href = '/Admin';
      } else {
        window.location.href = '/';
      }
    }, 450);
    return { ok: true, user: CURRENT_USER };
  } catch (err) {
    showToast('Lỗi kết nối', 'Không thể kết nối tới hệ thống, vui lòng thử lại.', 'danger');
    return { ok: false, error: 'Không thể kết nối tới hệ thống.' };
  }
}

function logout() {
  localStorage.removeItem('pb_auth_user');
  CURRENT_USER = null;
  showToast('Đã đăng xuất', 'Hẹn gặp lại bạn tại sân!', 'info');
  setTimeout(() => {
    window.location.href = '/Account/Logout';
  }, 350);
}

function requireAuth(redirectPath) {
  if (!CURRENT_USER) {
    const currentPath = redirectPath || window.location.pathname || '/';
    showToast('Yêu cầu đăng nhập', 'Vui lòng đăng nhập hoặc đăng ký tài khoản để đặt sân.', 'warning');
    setTimeout(() => {
      window.location.href = `/Account/Login?redirect=${encodeURIComponent(currentPath)}&notice=${encodeURIComponent('Vui lòng đăng nhập tài khoản để tiếp tục đặt sân.')}`;
    }, 450);
    return false;
  }
  return true;
}

function requireRole(role) {
  const currentPath = window.location.pathname || '/';
  if (!CURRENT_USER) {
    showToast('Khu vực Quản trị', 'Vui lòng đăng nhập tài khoản Quản trị viên.', 'warning');
    setTimeout(() => {
      window.location.href = `/Account/Login?redirect=${encodeURIComponent(currentPath)}&notice=${encodeURIComponent('Vui lòng đăng nhập tài khoản Quản trị viên CLB.')}`;
    }, 450);
    return false;
  }
  if (CURRENT_USER.role !== role) {
    showToast('Từ chối truy cập', 'Trang này chỉ dành cho Ban quản trị CLB.', 'danger');
    setTimeout(() => {
      window.location.href = '/';
    }, 450);
    return false;
  }
  return true;
}

/* ---------- Pro Shop & F&B Catalog ---------- */
const DEFAULT_MENU = [
  { id: 1, name: 'Revive Điện Giải Chanh Muối', price: 15000, type: 'drink', icon: '<iconify-icon icon="mdi:bottle-soda-classic-outline" width="26"></iconify-icon>' },
  { id: 2, name: 'Pocari Sweat Ion Supply',      price: 20000, type: 'drink', icon: '<iconify-icon icon="mdi:cup-water" width="26"></iconify-icon>' },
  { id: 3, name: 'Nước Khoáng Lạnh Aquafina',    price: 10000, type: 'drink', icon: '<iconify-icon icon="mdi:water-outline" width="26"></iconify-icon>' },
  { id: 4, name: 'Heineken Silver Ướp Lạnh',     price: 25000, type: 'drink', icon: '<iconify-icon icon="mdi:beer-outline" width="26"></iconify-icon>' },
  { id: 5, name: 'Nước Tăng Lực Red Bull',       price: 20000, type: 'drink', icon: '<iconify-icon icon="mdi:lightning-bolt" width="26"></iconify-icon>' },
  { id: 6, name: 'Trà Chanh Giã Tay Ca Lớn',     price: 18000, type: 'drink', icon: '<iconify-icon icon="mdi:tea-outline" width="26"></iconify-icon>' },
  { id: 7, name: 'Mì Trộn Trứng & Xúc Xích',     price: 35000, type: 'food',  icon: '<iconify-icon icon="mdi:noodles" width="26"></iconify-icon>' },
  { id: 8, name: 'Chuối & Thanh Năng Lượng',     price: 25000, type: 'food',  icon: '<iconify-icon icon="mdi:food-apple-outline" width="26"></iconify-icon>' },
  { id: 9, name: 'Thuê Vợt Carbon T700 Pro',     price: 40000, type: 'other', icon: '<iconify-icon icon="mdi:table-tennis" width="26"></iconify-icon>' },
  { id: 10, name: 'Bóng Thi Đấu Franklin X-40',  price: 65000, type: 'other', icon: '<iconify-icon icon="mdi:tennis-ball" width="26"></iconify-icon>' }
];

let BOOKINGS = JSON.parse(localStorage.getItem('pb_bookings')) || [];
let MENU = JSON.parse(localStorage.getItem('pb_menu')) || JSON.parse(JSON.stringify(DEFAULT_MENU));
let COURT_LOCKS = JSON.parse(localStorage.getItem('pb_court_locks')) || [];

/* ==========================================================================
   SPIKEDB — SQL SERVER (.\SQLEXPRESS -> PickleballClub) SYNC ENGINE
   ========================================================================== */
const SpikeDB = {
  isServerSynced: false,
  syncPromise: null,

  async syncFromSQLServer() {
    if (this.syncPromise) return this.syncPromise;
    this.syncPromise = (async () => {
      try {
        const res = await fetch('/api/db/state');
        if (!res.ok) return;
        const data = await res.json();
        if (!data || !data.ok) return;

        if (Array.isArray(data.courts) && data.courts.length > 0) {
          COURTS = data.courts;
          localStorage.setItem('pb_courts', JSON.stringify(COURTS));
        }
        if (Array.isArray(data.bookings)) {
          BOOKINGS = data.bookings;
          reconcileBookingsRealTime(false);
          localStorage.setItem('pb_bookings', JSON.stringify(BOOKINGS));
        }
        if (Array.isArray(data.courtLocks)) {
          COURT_LOCKS = data.courtLocks;
          localStorage.setItem('pb_court_locks', JSON.stringify(COURT_LOCKS));
        }
        if (Array.isArray(data.menu) && data.menu.length > 0) {
          MENU = data.menu;
          localStorage.setItem('pb_menu', JSON.stringify(MENU));
        }
        if (data.cmsConfig && Object.keys(data.cmsConfig).length > 0) {
          CMS_CONFIG = { ...DEFAULT_CMS, ...data.cmsConfig };
          COURT_PRICE_PER_HOUR = Number(CMS_CONFIG.courtPrice) || 120000;
          localStorage.setItem('pb_cms_config', JSON.stringify(CMS_CONFIG));
        }
        this.isServerSynced = true;
        this.refreshActivePageViews();
      } catch (_) {
      } finally {
        this.syncPromise = null;
      }
    })();
    return this.syncPromise;
  },

  async pushPartialToSQLServer(partialPayload) {
    try {
      await fetch('/api/db/sync', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(partialPayload)
      });
    } catch (_) {}
  },

  async pushToSQLServer(extraOptions = {}) {
    try {
      if (this.syncPromise) {
        await this.syncPromise;
      }
      await fetch('/api/db/sync', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          bookings: BOOKINGS,
          courtLocks: COURT_LOCKS,
          menu: MENU,
          cmsConfig: CMS_CONFIG,
          ...extraOptions
        })
      });
    } catch (_) {}
  },

  persistAll(extraOptions = {}) {
    localStorage.setItem('pb_bookings', JSON.stringify(BOOKINGS));
    localStorage.setItem('pb_menu', JSON.stringify(MENU));
    localStorage.setItem('pb_court_locks', JSON.stringify(COURT_LOCKS));
    this.pushToSQLServer(extraOptions);
  },

  refreshActivePageViews() {
    renderSiteNavigation();
    if (typeof renderDashboard === 'function') renderDashboard();
    if (typeof renderCourtOptions === 'function') renderCourtOptions();
    if (typeof renderTimelineAndSlots === 'function') renderTimelineAndSlots();
    if (typeof updatePriceBreakdown === 'function') updatePriceBreakdown();
    if (typeof renderMenu === 'function') renderMenu();
    if (typeof searchLookup === 'function') searchLookup();
    if (typeof renderManagement === 'function') renderManagement();
    if (typeof renderCheckin === 'function') renderCheckin();
    if (typeof renderReports === 'function') renderReports();
    window.dispatchEvent(new CustomEvent('pickleball:db-synced'));
  }
};

function saveData() {
  SpikeDB.persistAll();
  renderSiteNavigation();
}

function resetData() {
  if (confirm('Bạn có chắc chắn muốn làm mới toàn bộ lịch đặt sân hiện tại?')) {
    BOOKINGS = [];
    COURT_LOCKS = [];
    MENU = JSON.parse(JSON.stringify(DEFAULT_MENU));
    CMS_CONFIG = JSON.parse(JSON.stringify(DEFAULT_CMS));
    SpikeDB.persistAll({ clearAll: true });
    setTimeout(() => location.reload(), 300);
  }
}

/* ---------- Conflict Detection ---------- */
function checkSlotConflict(courtId, dateISO, startMins, endMins, ignoreBookingId = null) {
  const bookingConflict = BOOKINGS.find(b => {
    if (b.id === ignoreBookingId) return false;
    if (Number(b.courtId) !== Number(courtId)) return false;
    if ((b.date || getTodayISO()) !== dateISO) return false;
    if (b.status === 'rejected') return false;
    const bStart = timeToMinutes(b.time);
    const bEnd = bStart + Number(b.duration);
    return startMins < bEnd && endMins > bStart;
  });

  if (bookingConflict) return { hasConflict: true, type: 'booking', record: bookingConflict };

  const lockConflict = COURT_LOCKS.find(l => {
    if (Number(l.courtId) !== Number(courtId)) return false;
    if (l.date !== dateISO) return false;
    const lStart = timeToMinutes(l.startTime);
    const lEnd = lStart + Number(l.duration);
    return startMins < lEnd && endMins > lStart;
  });

  if (lockConflict) return { hasConflict: true, type: 'lock', record: lockConflict };
  return { hasConflict: false };
}

/* ---------- Utilities ---------- */
function formatMoney(n) {
  return Math.round(Number(n) || 0).toLocaleString('vi-VN') + ' đ';
}

function generateRef() {
  const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  let r = 'PB-';
  for (let i = 0; i < 6; i++) r += chars.charAt(Math.floor(Math.random() * chars.length));
  return r;
}

function timeToMinutes(t) {
  if (!t || !t.includes(':')) return 0;
  const [h, m] = t.split(':').map(Number);
  return h * 60 + m;
}

function minutesToTime(m) {
  const h = Math.floor(m / 60);
  const min = m % 60;
  return `${h.toString().padStart(2, '0')}:${min.toString().padStart(2, '0')}`;
}

function getCourtById(id) {
  return COURTS.find(c => Number(c.id) === Number(id)) || COURTS[0];
}

function getNowMinutes() {
  const now = new Date();
  return now.getHours() * 60 + now.getMinutes();
}

function getBookingTimingState(booking) {
  if (!booking) return { phase: 'unknown', canCheckIn: false };
  const todayISO = getTodayISO();
  const bDate = (booking.date || todayISO).trim();
  const startMins = timeToMinutes(booking.time);
  const dur = Number(booking.duration) || 60;
  const endMins = startMins + dur;
  const nowMins = getNowMinutes();

  if (bDate < todayISO) {
    return { phase: 'ended', canCheckIn: false, startMins, endMins, minsUntilStart: -9999, minsRemaining: 0 };
  }
  if (bDate > todayISO) {
    return { phase: 'future_day', canCheckIn: false, startMins, endMins, minsUntilStart: 9999, minsRemaining: dur };
  }
  if (nowMins >= endMins) {
    return { phase: 'ended', canCheckIn: false, startMins, endMins, minsUntilStart: startMins - nowMins, minsRemaining: 0 };
  }
  if (nowMins >= startMins) {
    return { phase: 'live', canCheckIn: true, startMins, endMins, minsUntilStart: 0, minsRemaining: endMins - nowMins };
  }
  // Cho phép check-in sớm tối đa 15 phút trước giờ bắt đầu
  const minsUntil = startMins - nowMins;
  return {
    phase: minsUntil <= 15 ? 'checkin_window' : 'upcoming_today',
    canCheckIn: minsUntil <= 15,
    startMins,
    endMins,
    minsUntilStart: minsUntil,
    minsRemaining: dur
  };
}

function reconcileBookingsRealTime(pushIfChanged = true) {
  let changed = false;
  BOOKINGS.forEach(b => {
    if (!['approved', 'playing'].includes(b.status)) return;
    const timing = getBookingTimingState(b);
    if (timing.phase === 'ended') {
      // Hết giờ thi đấu -> Tự động dừng ca và chuyển sang Hoàn thành
      if (b.status === 'playing') {
        b.status = 'completed';
        if (!b.totalPaid || Number(b.totalPaid) <= 0) {
          const posExtra = (b.posItems || []).reduce((s, i) => s + Number(i.price) * Number(i.qty), 0);
          b.totalPaid = calculateBookingTotal(b.courtId, b.time, b.duration, posExtra).grandTotal;
        }
        changed = true;
      }
    } else if (timing.phase === 'live') {
      // Đúng khung giờ thi đấu của vé đã duyệt -> Tự động kích hoạt Đang thi đấu
      if (b.status === 'approved') {
        b.status = 'playing';
        changed = true;
      }
    } else if (timing.phase === 'upcoming_today' || timing.phase === 'future_day') {
      // Chưa tới khung giờ thi đấu (còn hơn 15p) -> Không được hiển thị Đang thi đấu sớm
      if (b.status === 'playing') {
        b.status = 'approved';
        changed = true;
      }
    }
  });

  if (changed && pushIfChanged) {
    localStorage.setItem('pb_bookings', JSON.stringify(BOOKINGS));
    SpikeDB.pushToSQLServer();
    SpikeDB.refreshActivePageViews();
  }
  return changed;
}

function statusLabel(s, booking = null) {
  if (booking && s === 'approved') {
    const timing = getBookingTimingState(booking);
    if (timing.phase === 'upcoming_today' || timing.phase === 'checkin_window') {
      return `Chờ tới giờ (${booking.time})`;
    }
    if (timing.phase === 'ended') {
      return 'Đã qua giờ';
    }
  }
  if (booking && s === 'playing') {
    const timing = getBookingTimingState(booking);
    if (timing.phase === 'live') {
      return `Đang thi đấu (còn ${timing.minsRemaining}p)`;
    }
  }
  const map = {
    pending: 'Chờ duyệt',
    approved: 'Đã duyệt',
    playing: 'Đang thi đấu',
    completed: 'Hoàn thành',
    rejected: 'Đã hủy',
    locked: 'Bảo trì'
  };
  return map[s] || s;
}

function statusBadgeClass(s) {
  const map = {
    pending: 'badge-warning',
    approved: 'badge-primary',
    playing: 'badge-success',
    completed: 'badge-neutral',
    rejected: 'badge-danger',
    locked: 'badge-danger'
  };
  return map[s] || 'badge-neutral';
}

function showToast(title, message, type = 'success') {
  let container = document.getElementById('toastContainer');
  if (!container) {
    container = document.createElement('div');
    container.id = 'toastContainer';
    container.className = 'toast-container';
    document.body.appendChild(container);
  }
  const toast = document.createElement('div');
  toast.className = `toast ${type}`;
  toast.innerHTML = `
    <div class="toast-content">
      <h5>${title}</h5>
      ${message ? `<p>${message}</p>` : ''}
    </div>
  `;
  container.appendChild(toast);
  setTimeout(() => { if (toast.parentElement) toast.remove(); }, 3500);
}

function openModal(id) {
  const el = document.getElementById(id);
  if (el) { el.classList.add('open'); el.classList.add('active'); document.body.style.overflow = 'hidden'; }
}

function closeModal(id) {
  const el = document.getElementById(id);
  if (el) { el.classList.remove('open'); el.classList.remove('active'); document.body.style.overflow = ''; }
}

function joinClub() {
  if (!CURRENT_USER) {
    showToast('Đăng ký Hội viên CLB', 'Vui lòng đăng nhập hoặc tạo tài khoản để kích hoạt Thẻ Hội Viên.', 'info');
    setTimeout(() => {
      window.location.href = '/Account/Login?redirect=%2FAccount%2FProfile';
    }, 450);
    return;
  }
  showToast('Hồ sơ Hội viên CLB', 'Đang chuyển tới trang Thẻ Hội Viên & Hồ sơ VĐV của bạn...', 'success');
  setTimeout(() => {
    window.location.href = '/Account/Profile';
  }, 400);
}

function toggleMobileNav() {
  const nav = document.getElementById('siteMainNav');
  if (nav) {
    nav.classList.toggle('is-open');
  }
}

/* ---------- Unified Website Header & Footer Renderer ---------- */
function renderSiteNavigation() {
  const authRightMount = document.getElementById('headerAuthActionsMount');
  const adminBarMount = document.getElementById('adminSubbarMount');
  const isAdmin = CURRENT_USER && CURRENT_USER.role === 'admin';
  const pendingCount = BOOKINGS.filter(b => b.status === 'pending').length;
  const path = window.location.pathname.toLowerCase();

  if (authRightMount) {
    authRightMount.innerHTML = CURRENT_USER
      ? `
        <div class="user-pill">
          <a href="/Account/Profile" style="display:inline-flex;align-items:center;gap:0.5rem;color:#FFF;text-decoration:none;" title="Xem và cập nhật Hồ sơ cá nhân">
            <img src="${CURRENT_USER.avatar || 'https://images.unsplash.com/photo-1535713875002-d1d0cf377fde?auto=format&fit=crop&w=100&q=80'}" alt="${CURRENT_USER.name}">
            <span>${CURRENT_USER.name}</span>
          </a>
          <a href="/Account/Profile" class="btn btn-xs btn-ghost" style="color:var(--color-volt);padding:0.15rem 0.45rem;" title="Hồ sơ VĐV">Hồ sơ</a>
          <button type="button" class="btn btn-xs btn-ghost" style="color:#FCA5A5;padding:0.15rem 0.4rem;" onclick="logout()">Thoát</button>
        </div>
      `
      : `<a href="/Account/Login" class="btn btn-sm btn-secondary" style="background:rgba(255,255,255,0.1);border-color:rgba(255,255,255,0.2);color:#FFF;">Đăng nhập</a>`;
  }

  if (adminBarMount) {
    adminBarMount.innerHTML = isAdmin
      ? `
        <div class="admin-subbar">
          <div class="admin-subbar-inner">
            <span style="display:inline-flex;align-items:center;gap:0.45rem;font-weight:700;color:var(--color-volt);">
              <iconify-icon icon="lucide:shield-check" width="16"></iconify-icon>
              <span>Điều hành CLB</span>
            </span>
            <div class="admin-subbar-links">
              <a href="/Admin" class="admin-subbar-link ${path === '/admin' || path === '/admin/home/index' ? 'active' : ''}">
                <iconify-icon icon="lucide:layout-dashboard" width="14"></iconify-icon>
                <span>Tổng quan Quản trị</span>
              </a>
              <a href="/Management" class="admin-subbar-link ${path.includes('management') ? 'active' : ''}">
                <span>Điều phối & POS</span>
                ${pendingCount > 0 ? `<span class="nav-badge">${pendingCount}</span>` : ''}
              </a>
              <a href="/Admin/Bookings" class="admin-subbar-link ${path.includes('/admin/bookings') ? 'active' : ''}">Đơn đặt sân</a>
              <a href="/Admin/Courts" class="admin-subbar-link ${path.includes('/admin/courts') ? 'active' : ''}">Quản lý sân</a>
              <a href="/Admin/Accounts" class="admin-subbar-link ${path.includes('/admin/accounts') ? 'active' : ''}">Hội viên CLB</a>
              <a href="/Admin/Products" class="admin-subbar-link ${path.includes('/admin/products') ? 'active' : ''}">Sản phẩm POS</a>
              <a href="/Admin/Menus" class="admin-subbar-link ${path.includes('/admin/menus') ? 'active' : ''}">Thanh Menu</a>
              <a href="/Checkin" class="admin-subbar-link ${path.includes('checkin') ? 'active' : ''}">Soát vé QR</a>
              <a href="/Reports" class="admin-subbar-link ${path.includes('reports') ? 'active' : ''}">Doanh thu</a>
              <a href="/Cms" class="admin-subbar-link ${path.includes('cms') ? 'active' : ''}">Cấu hình</a>
            </div>
          </div>
        </div>
      `
      : '';
  }

  document.querySelectorAll('[data-role="admin"]').forEach(el => {
    el.style.display = isAdmin ? '' : 'none';
  });
  document.querySelectorAll('[data-role="user-only"]').forEach(el => {
    el.style.display = isAdmin ? 'none' : '';
  });
}

document.addEventListener('DOMContentLoaded', () => {
  reconcileBookingsRealTime(false);
  renderSiteNavigation();
  SpikeDB.syncFromSQLServer();

  // Đồng bộ trạng thái ca chơi theo thời gian thực mỗi 30 giây (Tự động bắt đầu khi tới giờ & tự động dừng khi hết giờ)
  setInterval(() => {
    const changed = reconcileBookingsRealTime(true);
    if (!changed) {
      if (typeof renderDashboard === 'function') renderDashboard();
      if (typeof renderManagement === 'function') renderManagement();
      if (typeof renderCheckin === 'function') renderCheckin();
    }
  }, 30000);

  document.addEventListener('click', (e) => {
    if (e.target.classList.contains('modal-backdrop') || e.target.classList.contains('modal-overlay')) {
      e.target.classList.remove('open');
      e.target.classList.remove('active');
      document.body.style.overflow = '';
    }
  });
});
