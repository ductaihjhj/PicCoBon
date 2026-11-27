/* ==========================================================================
   SPIKE OS — MODERN SPORTS MANAGEMENT PLATFORM
   Admin CMS & Dynamic Pricing Configuration Controller (/Cms)
   ========================================================================== */

function populateCmsForm() {
  const heroTag = document.getElementById('cmsHeroTag');
  if (!heroTag) return;
  heroTag.value = CMS_CONFIG.heroTag || '';
  document.getElementById('cmsHeroSubtag').value = CMS_CONFIG.heroSubtag || '';
  document.getElementById('cmsHeroTitle').value = CMS_CONFIG.heroTitle || '';
  document.getElementById('cmsHeroSubtitle').value = CMS_CONFIG.heroSubtitle || '';
  document.getElementById('cmsCourtPrice').value = resolveNumericConfig(CMS_CONFIG.courtPrice, 120000);
  document.getElementById('cmsPeakSurcharge').value = resolveNumericConfig(CMS_CONFIG.peakSurcharge, 30000);
  document.getElementById('cmsPeakStart').value = CMS_CONFIG.peakStart || '17:00';
  document.getElementById('cmsHotline').value = CMS_CONFIG.hotline || '0901 234 567';
  document.getElementById('cmsOpenTime').value = CMS_CONFIG.openTime || '06:00';
  document.getElementById('cmsCloseTime').value = CMS_CONFIG.closeTime || '22:00';
  document.getElementById('cmsClubTitle').value = CMS_CONFIG.clubTitle || '';
  document.getElementById('cmsClubDesc').value = CMS_CONFIG.clubDesc || '';
  updateLivePreview();
}

document.addEventListener('DOMContentLoaded', () => {
  if (typeof requireRole === 'function' && !requireRole('admin')) return;
  populateCmsForm();
});
window.addEventListener('pickleball:db-synced', populateCmsForm);

function updateLivePreview() {
  const prevHeroTag = document.getElementById('prevHeroTag');
  if (!prevHeroTag) return;
  prevHeroTag.textContent = document.getElementById('cmsHeroTag').value || 'PRO ARENA';
  document.getElementById('prevHeroSubtag').textContent = document.getElementById('cmsHeroSubtag').value || '';
  document.getElementById('prevHeroTitle').textContent = document.getElementById('cmsHeroTitle').value || '';
  document.getElementById('prevHeroSubtitle').textContent = document.getElementById('cmsHeroSubtitle').value || '';
  const price = resolveNumericConfig(document.getElementById('cmsCourtPrice').value, 120000);
  const peakExtra = resolveNumericConfig(document.getElementById('cmsPeakSurcharge').value, 0);
  document.getElementById('prevPrice').textContent = `${formatMoney(price)} / ${formatMoney(price + peakExtra)}`;
  const openT = document.getElementById('cmsOpenTime').value || '06:00';
  const closeT = document.getElementById('cmsCloseTime').value || '22:00';
  const peakT = document.getElementById('cmsPeakStart').value || '17:00';
  document.getElementById('prevHours').textContent = `${openT} – ${closeT} (Vàng từ ${peakT})`;
  document.getElementById('prevClubTitle').textContent = document.getElementById('cmsClubTitle').value || '';
  document.getElementById('prevClubDesc').textContent = document.getElementById('cmsClubDesc').value || '';
}

async function handleSaveCMS() {
  const res = await saveCmsConfig({
    heroTag: document.getElementById('cmsHeroTag').value.trim(),
    heroSubtag: document.getElementById('cmsHeroSubtag').value.trim(),
    heroTitle: document.getElementById('cmsHeroTitle').value.trim(),
    heroSubtitle: document.getElementById('cmsHeroSubtitle').value.trim(),
    courtPrice: resolveNumericConfig(document.getElementById('cmsCourtPrice').value, 120000),
    peakSurcharge: resolveNumericConfig(document.getElementById('cmsPeakSurcharge').value, 0),
    peakStart: document.getElementById('cmsPeakStart').value || '17:00',
    hotline: document.getElementById('cmsHotline').value.trim(),
    openTime: document.getElementById('cmsOpenTime').value || '06:00',
    closeTime: document.getElementById('cmsCloseTime').value || '22:00',
    clubTitle: document.getElementById('cmsClubTitle').value.trim(),
    clubDesc: document.getElementById('cmsClubDesc').value.trim()
  });
  if (res && res.ok) {
    showToast('Đã lưu cấu hình CMS!', 'Bảng giá, giờ hoạt động và PricingVersion đã được cập nhật toàn hệ thống.', 'success');
  }
}

async function handleResetCMS() {
  if (confirm('Khôi phục nội dung CMS về mặc định ban đầu?')) {
    const res = await saveCmsConfig(DEFAULT_CMS);
    if (res && res.ok) {
      populateCmsForm();
      showToast('Đã khôi phục mặc định', 'Cấu hình CMS đã trở về giá trị chuẩn.', 'success');
    }
  }
}
