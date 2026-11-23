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

  const aiAvatarInput = document.getElementById('cmsAssistantAvatar');
  if (aiAvatarInput && CMS_CONFIG.assistantAvatar) {
    aiAvatarInput.value = CMS_CONFIG.assistantAvatar;
    syncAiAvatarPreviewUi(CMS_CONFIG.assistantAvatar);
  }
  updateLivePreview();
}

document.addEventListener('DOMContentLoaded', () => {
  if (typeof requireRole === 'function' && !requireRole('admin')) return;
  populateCmsForm();
});
window.addEventListener('pickleball:db-synced', populateCmsForm);

function syncAiAvatarPreviewUi(avatarUrl) {
  const clean = String(avatarUrl || '').trim() || '/assets/assistant/02_wave.png';
  const hiddenInput = document.getElementById('cmsAssistantAvatar');
  if (hiddenInput) hiddenInput.value = clean;

  const prev1 = document.getElementById('cmsAiAvatarPreview');
  if (prev1) prev1.src = clean;

  const prev2 = document.getElementById('prevAiAvatarImg');
  if (prev2) prev2.src = clean;

  document.querySelectorAll('.cms-ai-preset-btn').forEach(btn => {
    const isMatch = (btn.getAttribute('data-ai-avatar-url') || '').toLowerCase() === clean.toLowerCase();
    btn.classList.toggle('is-selected', isMatch);
    btn.style.borderColor = isMatch ? 'var(--brand-primary)' : 'var(--border-default)';
    btn.style.background = isMatch ? 'var(--brand-primary-tint)' : 'var(--surface)';
  });

  if (window.mascot && typeof window.mascot.setAssistantAvatar === 'function') {
    window.mascot.setAssistantAvatar(clean);
  }
}

async function selectAiAvatarPreset(presetUrl, saveImmediately = false) {
  syncAiAvatarPreviewUi(presetUrl);
  if (!saveImmediately) return;

  try {
    const formData = new FormData();
    formData.append('avatarUrl', presetUrl);
    const res = await apiFetch('/api/assistant/avatar', {
      method: 'POST',
      body: formData
    });
    const data = await res.json().catch(() => ({}));
    if (res.ok && data && data.ok) {
      CMS_CONFIG.assistantAvatar = data.avatarUrl || presetUrl;
      syncAiAvatarPreviewUi(CMS_CONFIG.assistantAvatar);
      showToast('Đã cập nhật ảnh đại diện AI!', data.message || 'Hình đại diện Spike Assistant đã được áp dụng.', 'success');
    } else {
      showToast('Không thể lưu ảnh đại diện AI', (data && data.error) || 'Vui lòng thử lại.', 'error');
    }
  } catch (err) {
    showToast('Lỗi kết nối', 'Không thể cập nhật ảnh đại diện Trợ lý AI.', 'error');
  }
}

async function handleUploadAiAvatarFile(fileInput) {
  if (!fileInput || !fileInput.files || fileInput.files.length === 0) return;
  const file = fileInput.files[0];

  if (file.size > 2 * 1024 * 1024) {
    showToast('Tệp quá lớn', 'Ảnh đại diện Trợ lý AI không được vượt quá 2 MB.', 'error');
    fileInput.value = '';
    return;
  }

  const metaEl = document.getElementById('cmsAiAvatarMeta');
  if (metaEl) metaEl.textContent = `Đang tải lên "${file.name}"...`;

  try {
    const formData = new FormData();
    formData.append('avatarFile', file);
    const res = await apiFetch('/api/assistant/avatar', {
      method: 'POST',
      body: formData
    });
    const data = await res.json().catch(() => ({}));
    if (res.ok && data && data.ok && data.avatarUrl) {
      CMS_CONFIG.assistantAvatar = data.avatarUrl;
      syncAiAvatarPreviewUi(data.avatarUrl);
      if (metaEl) metaEl.textContent = `Đã áp dụng ảnh tải lên: ${file.name}`;
      showToast('Đã cập nhật ảnh đại diện AI!', data.message || 'Ảnh tải lên đã được áp dụng cho Spike Assistant.', 'success');
    } else {
      if (metaEl) metaEl.textContent = (data && data.error) || 'Tải ảnh không thành công.';
      showToast('Không thể tải ảnh', (data && data.error) || 'Định dạng ảnh không hợp lệ.', 'error');
    }
  } catch (err) {
    if (metaEl) metaEl.textContent = 'Lỗi kết nối khi tải ảnh lên.';
    showToast('Lỗi kết nối', 'Không thể tải ảnh đại diện lên máy chủ.', 'error');
  } finally {
    fileInput.value = '';
  }
}

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
  const aiAvatarInput = document.getElementById('cmsAssistantAvatar');
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
    clubDesc: document.getElementById('cmsClubDesc').value.trim(),
    assistantAvatar: aiAvatarInput ? aiAvatarInput.value.trim() : (CMS_CONFIG.assistantAvatar || '/assets/assistant/02_wave.png')
  });
  if (res && res.ok) {
    showToast('Đã lưu cấu hình CMS!', 'Bảng giá, giờ hoạt động và hình đại diện AI đã được cập nhật toàn hệ thống.', 'success');
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
