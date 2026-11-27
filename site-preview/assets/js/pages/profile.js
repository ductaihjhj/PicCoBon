/* ==========================================================================
   SPIKE OS — MODERN SPORTS MANAGEMENT PLATFORM
   Member Profile & Athlete Passport Controller (/Account/Profile)
   ========================================================================== */

(function () {
  const MAX_AVATAR_BYTES = 2 * 1024 * 1024;
  const ALLOWED_AVATAR_MIME = new Set(['image/jpeg', 'image/png', 'image/webp']);
  const ALLOWED_AVATAR_EXT = /\.(jpe?g|png|webp)$/i;
  const DEFAULT_UPLOAD_HINT = 'Kéo thả ảnh vào đây hoặc chọn tệp JPG, JPEG, PNG, WEBP (tối đa 2 MB)';

  let activeObjectUrl = null;
  let pulseTimer = null;

  function getEls() {
    return {
      studio: document.getElementById('avatarStudio'),
      dropzone: document.getElementById('avatarDropzone'),
      fileInput: document.getElementById('profAvatarFile'),
      presetInput: document.getElementById('selectedPresetAvatar'),
      clearUploadBtn: document.getElementById('avatarClearUploadBtn'),
      uploadMeta: document.getElementById('avatarUploadMeta'),
      sourcePill: document.getElementById('avatarSourcePill'),
      errorBanner: document.getElementById('avatarUploadError'),
      errorText: document.getElementById('avatarUploadErrorText'),
      livePreview: document.getElementById('liveAvatarPreview'),
      liveFallback: document.getElementById('liveAvatarFallback'),
      inlinePreview: document.getElementById('inlineAvatarPreview'),
      inlineFallback: document.getElementById('inlineAvatarFallback'),
      passportWrap: document.querySelector('.member-avatar-wrap'),
      form: document.getElementById('profileUpdateForm'),
      submitBtn: document.getElementById('profileSaveSubmitBtn'),
      ajaxAlert: document.getElementById('profileAjaxAlert')
    };
  }

  function formatKb(bytes) {
    if (bytes >= 1024 * 1024) {
      return (bytes / (1024 * 1024)).toFixed(2) + ' MB';
    }
    return Math.max(1, Math.round(bytes / 1024)) + ' KB';
  }

  function revokeActiveObjectUrl() {
    if (activeObjectUrl) {
      try { URL.revokeObjectURL(activeObjectUrl); } catch (_) {}
      activeObjectUrl = null;
    }
  }

  function setError(message) {
    const { errorBanner, errorText } = getEls();
    if (!errorBanner || !errorText) return;
    if (message) {
      errorText.textContent = message;
      errorBanner.hidden = false;
      if (typeof window.showToast === 'function') {
        window.showToast('Ảnh đại diện chưa hợp lệ', message, 'error');
      }
    } else {
      errorText.textContent = '';
      errorBanner.hidden = true;
    }
  }

  function showProfileSaveAlert(isSuccess, message) {
    document.querySelectorAll('[data-server-profile-alert="true"]').forEach(el => {
      el.hidden = true;
    });
    const { ajaxAlert } = getEls();
    if (!ajaxAlert) return;
    ajaxAlert.className = isSuccess ? 'sh-alert-success' : 'sh-alert-danger';
    ajaxAlert.style.display = 'flex';
    ajaxAlert.style.alignItems = 'center';
    ajaxAlert.style.gap = '0.65rem';
    ajaxAlert.hidden = false;
    ajaxAlert.innerHTML = '';

    const icon = document.createElement('iconify-icon');
    icon.setAttribute('icon', isSuccess ? 'lucide:check-circle-2' : 'lucide:alert-circle');
    icon.setAttribute('width', '20');
    icon.style.color = isSuccess ? 'var(--color-success)' : 'var(--color-danger)';
    icon.style.flexShrink = '0';

    const span = document.createElement('span');
    span.textContent = message || (isSuccess ? 'Đã cập nhật hồ sơ thành công!' : 'Không thể cập nhật hồ sơ.');

    ajaxAlert.appendChild(icon);
    ajaxAlert.appendChild(span);
  }

  function pulseAvatarCard() {
    const { passportWrap } = getEls();
    if (!passportWrap) return;
    passportWrap.classList.remove('is-updating');
    void passportWrap.offsetWidth;
    passportWrap.classList.add('is-updating');
    clearTimeout(pulseTimer);
    pulseTimer = setTimeout(() => {
      passportWrap.classList.remove('is-updating');
    }, 360);
  }

  function updatePreviewPair(imgEl, fbEl, src) {
    if (!imgEl || !fbEl) return;
    if (src) {
      if (imgEl.getAttribute('src') !== src) {
        imgEl.src = src;
      }
      imgEl.hidden = false;
      fbEl.hidden = true;
    } else {
      imgEl.removeAttribute('src');
      imgEl.hidden = true;
      fbEl.hidden = false;
    }
  }

  function updatePreviews(src, withPulse) {
    const { livePreview, liveFallback, inlinePreview, inlineFallback } = getEls();
    updatePreviewPair(livePreview, liveFallback, src);
    updatePreviewPair(inlinePreview, inlineFallback, src);
    if (withPulse) {
      pulseAvatarCard();
    }
  }

  function updatePresetSelection(activeUrl, hasQueuedUpload) {
    const tiles = document.querySelectorAll('.spk-avatar-tile');
    let matchedName = null;
    const normalized = (activeUrl || '').trim();

    tiles.forEach(tile => {
      const tileUrl = tile.dataset.avatarUrl || '';
      const isMatch = !hasQueuedUpload && normalized !== '' && tileUrl === normalized;
      tile.classList.toggle('is-selected', isMatch);
      tile.setAttribute('aria-pressed', isMatch ? 'true' : 'false');
      if (isMatch) {
        matchedName = tile.dataset.avatarName || 'Spike OS';
      }
    });

    return matchedName;
  }

  function updateSourceState(hasQueuedUpload, file) {
    const { presetInput, sourcePill, dropzone, clearUploadBtn, uploadMeta } = getEls();
    const currentUrl = presetInput ? presetInput.value.trim() : '';

    if (hasQueuedUpload && file) {
      if (dropzone) dropzone.classList.add('has-upload');
      if (clearUploadBtn) clearUploadBtn.hidden = false;
      if (uploadMeta) {
        uploadMeta.textContent = `Sẵn sàng lưu: ${file.name} (${formatKb(file.size)}) — Nhấn "Lưu hồ sơ" để cập nhật`;
      }
      if (sourcePill) {
        sourcePill.textContent = 'Ảnh từ thiết bị (chờ lưu)';
        sourcePill.classList.add('is-active');
      }
      updatePresetSelection('', true);
      return;
    }

    if (dropzone) dropzone.classList.remove('has-upload');
    if (clearUploadBtn) clearUploadBtn.hidden = true;
    if (uploadMeta) uploadMeta.textContent = DEFAULT_UPLOAD_HINT;

    const matchedPreset = updatePresetSelection(currentUrl, false);
    if (sourcePill) {
      if (matchedPreset) {
        sourcePill.textContent = `Nhân vật: ${matchedPreset}`;
        sourcePill.classList.add('is-active');
      } else if (currentUrl.startsWith('/uploads/avatars/')) {
        sourcePill.textContent = 'Ảnh tải lên đã lưu';
        sourcePill.classList.add('is-active');
      } else if (currentUrl !== '') {
        sourcePill.textContent = 'Ảnh đại diện đã lưu';
        sourcePill.classList.add('is-active');
      } else {
        sourcePill.textContent = 'Chưa đặt ảnh đại diện';
        sourcePill.classList.remove('is-active');
      }
    }
  }

  function validateFile(file) {
    if (!file) return 'Không tìm thấy tệp ảnh đã chọn.';
    if (file.size <= 0) return 'Tệp ảnh rỗng, vui lòng chọn tệp khác.';
    if (!ALLOWED_AVATAR_EXT.test(file.name || '') || (file.type && !ALLOWED_AVATAR_MIME.has(file.type))) {
      return 'Định dạng không hỗ trợ. Vui lòng chọn tệp JPG, JPEG, PNG hoặc WEBP.';
    }
    if (file.size > MAX_AVATAR_BYTES) {
      return `Dung lượng ảnh vượt quá giới hạn 2 MB (tệp hiện tại: ${formatKb(file.size)}).`;
    }
    return null;
  }

  function applySelectedFile(file, assignToInput) {
    const { fileInput } = getEls();
    const validationError = validateFile(file);
    if (validationError) {
      if (fileInput) fileInput.value = '';
      setError(validationError);
      return;
    }

    if (assignToInput && fileInput) {
      try {
        const dt = new DataTransfer();
        dt.items.add(file);
        fileInput.files = dt.files;
      } catch (_) {
        setError('Trình duyệt không hỗ trợ kéo thả tệp trực tiếp, vui lòng dùng nút "Chọn ảnh từ máy".');
        return;
      }
    }

    setError(null);
    revokeActiveObjectUrl();
    activeObjectUrl = URL.createObjectURL(file);
    updatePreviews(activeObjectUrl, true);
    updateSourceState(true, file);
  }

  function clearQueuedUpload() {
    const { studio, fileInput, presetInput } = getEls();
    if (fileInput) fileInput.value = '';
    revokeActiveObjectUrl();
    setError(null);

    const savedUrl = (studio && studio.dataset.savedAvatar) || '';
    if (presetInput) presetInput.value = savedUrl;

    updatePreviews(savedUrl, true);
    updateSourceState(false, null);
  }

  async function handleProfileSubmit(e) {
    const { form, submitBtn, studio, fileInput, presetInput } = getEls();
    if (!form) return;
    e.preventDefault();
    setError(null);

    if (submitBtn) {
      submitBtn.disabled = true;
      submitBtn.dataset.originalHtml = submitBtn.innerHTML;
      submitBtn.textContent = 'Đang lưu hồ sơ Demo...';
    }

    try {
      await SpikeDemo.ensureLoaded(false);
      const raw = SpikeDemo.getRawState();
      const user = SpikeDemo.getActiveAccount() || (raw?.accounts || []).find(a => a.id === 101) || (raw?.accounts || [])[0];
      if (!user) return;

      let nextAvatar = presetInput ? presetInput.value.trim() : (user.avatar || '');
      const queuedFile = fileInput && fileInput.files && fileInput.files[0];
      if (queuedFile) {
        nextAvatar = await new Promise((resolve) => {
          const reader = new FileReader();
          reader.onload = () => resolve(String(reader.result || activeObjectUrl || ''));
          reader.onerror = () => resolve(activeObjectUrl || nextAvatar);
          reader.readAsDataURL(queuedFile);
        });
      }

      user.name = (document.getElementById('profFullName')?.value || user.name).trim();
      user.phone = (document.getElementById('profPhone')?.value || user.phone).trim();
      user.email = (document.getElementById('profEmail')?.value || user.email).trim();
      user.address = (document.getElementById('profAddress')?.value || '').trim();
      user.skillLevel = document.getElementById('profSkill')?.value || user.skillLevel;
      user.dominantHand = document.getElementById('profHand')?.value || user.dominantHand;
      user.paddleBrand = (document.getElementById('profPaddle')?.value || '').trim();
      user.playStyle = document.getElementById('profStyle')?.value || user.playStyle;
      user.bio = (document.getElementById('profBio')?.value || '').trim();
      if (nextAvatar) user.avatar = nextAvatar;

      SpikeDemo.saveState();
      if (fileInput) fileInput.value = '';
      revokeActiveObjectUrl();

      if (studio) studio.dataset.savedAvatar = user.avatar || '';
      if (presetInput) presetInput.value = user.avatar || '';
      updatePreviews(user.avatar || '', true);
      updateSourceState(false, null);

      const liveName = document.getElementById('liveNamePreview');
      if (liveName) liveName.textContent = user.name;
      const heroTitle = document.getElementById('profileHeroTitle');
      if (heroTitle) heroTitle.textContent = `Hồ Sơ Cá Nhân — ${user.name}`;
      const contactEl = document.getElementById('passportContact');
      if (contactEl) contactEl.textContent = `${user.phone || 'Chưa cập nhật SĐT'}${user.email ? ' · ' + user.email : ''}`;
      const bioEl = document.getElementById('passportBioBox');
      if (bioEl) {
        bioEl.textContent = user.bio ? `“${user.bio}”` : '';
        bioEl.style.display = user.bio ? 'block' : 'none';
      }

      if (window.SpikeAvatarSync) {
        window.SpikeAvatarSync.setGlobalAvatar(user.avatar || '', user.name);
      }

      const okMsg = 'Đã cập nhật Hồ sơ Hội viên Demo trên trình duyệt (Chưa lưu lên máy chủ thật).';
      showProfileSaveAlert(true, okMsg);
      if (typeof window.showToast === 'function') {
        window.showToast('Đã lưu hồ sơ Demo', okMsg, 'success');
      }
    } finally {
      if (submitBtn) {
        submitBtn.disabled = false;
        if (submitBtn.dataset.originalHtml) {
          submitBtn.innerHTML = submitBtn.dataset.originalHtml;
        }
      }
    }
  }

  function initAvatarStudio() {
    const { dropzone, fileInput, clearUploadBtn, livePreview, inlinePreview, form } = getEls();
    if (!dropzone || !fileInput) return;

    const handlePreviewError = (imgEl) => {
      if (!imgEl) return;
      imgEl.addEventListener('error', () => {
        if (window.SpikeAvatarSync) {
          window.SpikeAvatarSync.handleImageError(imgEl);
        }
      });
    };
    handlePreviewError(livePreview);
    handlePreviewError(inlinePreview);

    fileInput.addEventListener('change', () => {
      const file = fileInput.files && fileInput.files[0];
      if (!file) {
        clearQueuedUpload();
        return;
      }
      applySelectedFile(file, false);
    });

    if (clearUploadBtn) {
      clearUploadBtn.addEventListener('click', (e) => {
        e.preventDefault();
        e.stopPropagation();
        clearQueuedUpload();
      });
    }

    dropzone.addEventListener('click', (e) => {
      if (e.target.closest('label[for="profAvatarFile"], button')) return;
      fileInput.click();
    });

    dropzone.addEventListener('dragenter', (e) => {
      e.preventDefault();
      dropzone.classList.add('is-dragover');
    });

    dropzone.addEventListener('dragover', (e) => {
      e.preventDefault();
      dropzone.classList.add('is-dragover');
    });

    dropzone.addEventListener('dragleave', (e) => {
      if (!dropzone.contains(e.relatedTarget)) {
        dropzone.classList.remove('is-dragover');
      }
    });

    dropzone.addEventListener('drop', (e) => {
      e.preventDefault();
      dropzone.classList.remove('is-dragover');
      const file = e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files[0];
      if (file) {
        applySelectedFile(file, true);
      }
    });

    if (form) {
      form.addEventListener('submit', handleProfileSubmit);
    }

    updateSourceState(false, null);
  }

  window.switchProfileTab = function (tabKey, btnEl) {
    document.querySelectorAll('.profile-tab-btn').forEach(b => b.classList.remove('active'));
    document.querySelectorAll('.profile-tab-pane').forEach(p => p.classList.remove('active'));
    if (btnEl) btnEl.classList.add('active');
    const target = document.getElementById('tab-' + tabKey);
    if (target) target.classList.add('active');
  };

  window.selectPresetAvatar = function (url, btnEl) {
    const { fileInput, presetInput } = getEls();
    if (fileInput) fileInput.value = '';
    revokeActiveObjectUrl();
    setError(null);

    if (presetInput) presetInput.value = url;
    updatePreviews(url, true);
    updateSourceState(false, null);
    if (btnEl) btnEl.focus();
  };

  window.setPaddleBrand = function (brand) {
    const input = document.getElementById('profPaddle');
    const preview = document.getElementById('livePaddlePreview');
    if (input) input.value = brand;
    if (preview) preview.textContent = brand;
  };

  window.payProfileBookingPayPal = async function (bookingCode, btnEl) {
    if (!window.SpikePayPal || !bookingCode) return;
    if (btnEl) btnEl.disabled = true;
    try {
      const res = await SpikePayPal.createAndRedirect({
        targetType: 'booking',
        targetCode: bookingCode
      });
      if (!res || !res.redirecting) {
        showToast('Không thể thanh toán PayPal', (res && res.error) || 'Vui lòng thử lại sau.', 'error');
      }
    } finally {
      if (btnEl) btnEl.disabled = false;
    }
  };

  async function hydrateProfilePage() {
    await SpikeDemo.ensureLoaded(false);
    const raw = SpikeDemo.getRawState();
    let user = SpikeDemo.getActiveAccount();
    if (!user) {
      user = (raw?.accounts || []).find(a => a.id === 101) || (raw?.accounts || [])[0];
      if (user) SpikeDemo.setActiveAccountId(user.id);
    }
    if (!user) return;

    const myBookings = (raw?.bookings || []).filter(b => Number(b.accountId) === Number(user.id) || (user.phone && b.phone === user.phone));
    const totalMatches = myBookings.length;
    const totalHours = Number((myBookings.reduce((s, b) => s + (Number(b.duration) || 60), 0) / 60).toFixed(1));
    const totalSpent = myBookings.reduce((s, b) => s + (Number(b.paidAmount) || 0), 0);

    const setText = (id, val) => { const el = document.getElementById(id); if (el) el.textContent = val; };
    const setVal = (id, val) => { const el = document.getElementById(id); if (el) el.value = val || ''; };

    setText('profileHeroTitle', `Hồ Sơ Cá Nhân — ${user.name}`);
    setText('passportCode', `#SPK-${String(user.id).padStart(4, '0')}`);
    setText('passportTier', user.memberTier || 'Hội viên Tiêu chuẩn');
    setText('liveNamePreview', user.name || 'Hội viên CLB');
    setText('passportContact', `${user.phone || 'Chưa cập nhật SĐT'}${user.email ? ' · ' + user.email : ''}`);
    setText('passportKpiMatches', String(totalMatches));
    setText('passportKpiHours', `${totalHours} h`);
    setText('passportKpiSpent', `${Math.round(totalSpent / 1000).toLocaleString('vi-VN')}k`);
    setText('liveSkillPreview', user.skillLevel || 'Giao lưu (DUPR 3.0)');
    setText('liveHandPreview', user.dominantHand || 'Tay phải');
    setText('livePaddlePreview', user.paddleBrand || 'Chưa cập nhật');
    setText('liveStylePreview', user.playStyle || 'Đánh đôi phối hợp');
    setText('passportAddressText', user.address || 'TP.HCM');
    const bioEl = document.getElementById('passportBioBox');
    if (bioEl) {
      bioEl.textContent = user.bio ? `“${user.bio}”` : '';
      bioEl.style.display = user.bio ? 'block' : 'none';
    }

    setVal('profFullName', user.name);
    setVal('profPhone', user.phone);
    setVal('profEmail', user.email);
    setVal('profAddress', user.address);
    setVal('profSkill', user.skillLevel);
    setVal('profHand', user.dominantHand);
    setVal('profPaddle', user.paddleBrand);
    setVal('profStyle', user.playStyle);
    setVal('profBio', user.bio);

    const { studio, presetInput, liveFallback, inlineFallback } = getEls();
    const avatarUrl = user.avatar ? SpikeAsset(user.avatar) : '';
    const initials = (user.name || 'U').trim().charAt(0).toUpperCase();
    if (liveFallback) liveFallback.textContent = initials;
    if (inlineFallback) inlineFallback.textContent = initials;
    if (studio) studio.dataset.savedAvatar = avatarUrl;
    if (presetInput) presetInput.value = avatarUrl;
    updatePreviews(avatarUrl, false);
    updateSourceState(false, null);

    setText('profileBookingsTabCount', String(myBookings.length));
    setText('profileBookingsListCount', String(myBookings.length));

    const tbody = document.getElementById('profileBookingsTbody');
    if (tbody) {
      if (myBookings.length === 0) {
        tbody.innerHTML = `<tr><td colspan="8" style="text-align:center;padding:2rem;color:var(--text-muted);">Chưa có lịch đặt sân nào cho tài khoản Demo này.</td></tr>`;
      } else {
        tbody.innerHTML = myBookings.map(b => {
          const fee = b.amountDue > 0 ? b.amountDue : b.courtFee;
          const remaining = Math.max(0, fee - (b.paidAmount || 0));
          const canPay = remaining > 0 && ['pending', 'approved', 'playing'].includes(b.status);
          return `
            <tr>
              <td><span class="text-mono font-bold" style="color:var(--color-indigo);">${escapeHtml(b.id)}</span></td>
              <td><span class="font-semibold text-xs">${escapeHtml(formatDateVN(b.date))}</span></td>
              <td><span class="badge badge-neutral">${escapeHtml(b.courtName || ('Sân #' + b.courtId))}</span></td>
              <td class="text-mono font-semibold">${escapeHtml(b.time)} <span class="text-xs text-muted">(${b.duration}p)</span></td>
              <td>${b.players || 4} người</td>
              <td class="text-mono font-bold" style="color:var(--color-primary);">
                <div>${formatMoney(fee)}</div>
                ${remaining > 0 ? `<div class="text-xs text-muted">Còn lại: ${formatMoney(remaining)}</div>` : ''}
              </td>
              <td><span class="badge ${statusBadgeClass(b.status, b)}">${escapeHtml(statusLabel(b.status, b))}</span></td>
              <td>
                <div style="display:flex;gap:4px;align-items:center;flex-wrap:wrap;">
                  <a href="${escapeAttr(SpikeRoute('/Lookup?code=' + encodeURIComponent(b.id)))}" class="btn btn-outline btn-xs">Xem QR</a>
                  ${canPay ? `<button type="button" class="btn btn-primary btn-xs" onclick="payProfileBookingPayPal('${escapeAttr(b.id)}', this)">PayPal Demo</button>` : ''}
                </div>
              </td>
            </tr>
          `;
        }).join('');
      }
    }

    const secForm = document.getElementById('profileSecurityForm');
    if (secForm && !secForm.dataset.boundDemo) {
      secForm.dataset.boundDemo = 'true';
      secForm.addEventListener('submit', (ev) => {
        ev.preventDefault();
        showToast('Chế độ UI Demo', 'Bản xem trước giao diện không lưu mật khẩu thật.', 'info');
      });
    }
  }

  document.addEventListener('DOMContentLoaded', () => {
    initAvatarStudio();
    hydrateProfilePage();
  });
  window.addEventListener('pickleball:db-synced', hydrateProfilePage);
  window.addEventListener('beforeunload', revokeActiveObjectUrl);
})();
