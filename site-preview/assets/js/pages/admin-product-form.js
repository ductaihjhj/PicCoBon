/* ==========================================================================
   SPIKE OS — MODERN SPORTS MANAGEMENT PLATFORM
   Admin Product Image Upload & Preview Controller (/Admin/Products/Create & Edit)
   ========================================================================== */

(function initAdminProductImageForm() {
  const dropzone = document.getElementById('productImageDropzone');
  const fileInput = document.getElementById('productImageInput');
  const previewImg = document.getElementById('productImagePreviewImg');
  const metaEl = document.getElementById('productImageMeta');
  const errEl = document.getElementById('productImageClientError');
  const badgeEl = document.getElementById('imageStatusBadge');
  const btnChoose = document.getElementById('btnChooseImageFile');
  const btnClear = document.getElementById('btnClearSelectedImage');
  const btnRestore = document.getElementById('btnRestoreOriginalImage');
  const chkRemove = document.getElementById('chkRemoveImage');

  if (!dropzone || !fileInput || !previewImg) return;

  const originalSrc = previewImg.getAttribute('data-original-src') || previewImg.getAttribute('data-default-src') || previewImg.src;
  const placeholderSrc = previewImg.getAttribute('data-placeholder-src') || previewImg.getAttribute('data-default-src') || originalSrc;
  const hasCustomOriginal = previewImg.getAttribute('data-has-custom') === 'true';
  const originalMetaHtml = metaEl ? metaEl.innerHTML : '';

  const maxBytes = 5 * 1024 * 1024;
  const allowedTypes = ['image/png', 'image/jpeg', 'image/jpg', 'image/webp'];
  const allowedExts = ['.png', '.jpg', '.jpeg', '.webp'];
  let objectUrl = null;

  function cleanupUrl() {
    if (objectUrl) {
      URL.revokeObjectURL(objectUrl);
      objectUrl = null;
    }
  }

  function validateAndPreview(file) {
    if (errEl) errEl.textContent = '';
    if (!file) return;

    const lowerName = (file.name || '').toLowerCase();
    const hasValidExt = allowedExts.some(ext => lowerName.endsWith(ext));
    const hasValidMime = allowedTypes.includes((file.type || '').toLowerCase());

    if (!hasValidExt || !hasValidMime) {
      if (errEl) errEl.textContent = 'Định dạng không hợp lệ. Chỉ chấp nhận ảnh PNG, JPG/JPEG hoặc WebP.';
      fileInput.value = '';
      return;
    }

    if (file.size > maxBytes) {
      if (errEl) errEl.textContent = 'Dung lượng ảnh vượt quá 5 MB (' + (file.size / (1024 * 1024)).toFixed(2) + ' MB).';
      fileInput.value = '';
      return;
    }

    cleanupUrl();
    if (chkRemove) chkRemove.checked = false;
    objectUrl = URL.createObjectURL(file);
    previewImg.src = objectUrl;
    const kb = (file.size / 1024).toFixed(1);
    if (metaEl) metaEl.textContent = 'Ảnh mới sẵn sàng lưu: ' + file.name + ' (' + kb + ' KB)';
    if (badgeEl) {
      badgeEl.className = 'badge badge-success';
      badgeEl.textContent = 'Ảnh mới (Chưa lưu)';
    }
    if (btnClear) btnClear.classList.remove('d-none');
    if (btnRestore) btnRestore.classList.remove('d-none');
  }

  dropzone.addEventListener('click', () => fileInput.click());
  dropzone.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      fileInput.click();
    }
  });
  if (btnChoose) btnChoose.addEventListener('click', () => fileInput.click());

  fileInput.addEventListener('change', () => {
    if (fileInput.files && fileInput.files[0]) {
      validateAndPreview(fileInput.files[0]);
    }
  });

  if (btnClear) {
    btnClear.addEventListener('click', () => {
      cleanupUrl();
      fileInput.value = '';
      if (errEl) errEl.textContent = '';
      previewImg.src = placeholderSrc;
      if (metaEl) metaEl.textContent = 'Chưa chọn ảnh riêng — hệ thống sẽ hiển thị ảnh minh họa theo danh mục.';
      btnClear.classList.add('d-none');
    });
  }

  if (chkRemove) {
    chkRemove.addEventListener('change', () => {
      if (errEl) errEl.textContent = '';
      if (chkRemove.checked) {
        cleanupUrl();
        fileInput.value = '';
        previewImg.src = placeholderSrc;
        if (metaEl) metaEl.textContent = 'Ảnh riêng sẽ được gỡ khi bấm "Lưu cập nhật".';
        if (badgeEl) {
          badgeEl.className = 'badge badge-warning';
          badgeEl.textContent = 'Sẽ gỡ ảnh';
        }
        if (btnRestore) btnRestore.classList.remove('d-none');
      } else {
        previewImg.src = originalSrc;
        if (metaEl) metaEl.innerHTML = originalMetaHtml;
        if (badgeEl) {
          badgeEl.className = hasCustomOriginal ? 'badge badge-info' : 'badge badge-secondary';
          badgeEl.textContent = hasCustomOriginal ? 'Đang dùng ảnh sản phẩm' : 'Ảnh minh họa mặc định';
        }
        if (btnRestore) btnRestore.classList.add('d-none');
      }
    });
  }

  if (btnRestore) {
    btnRestore.addEventListener('click', () => {
      cleanupUrl();
      fileInput.value = '';
      if (chkRemove) chkRemove.checked = false;
      if (errEl) errEl.textContent = '';
      previewImg.src = originalSrc;
      if (metaEl) metaEl.innerHTML = originalMetaHtml;
      if (badgeEl) {
        badgeEl.className = hasCustomOriginal ? 'badge badge-info' : 'badge badge-secondary';
        badgeEl.textContent = hasCustomOriginal ? 'Đang dùng ảnh sản phẩm' : 'Ảnh minh họa mặc định';
      }
      btnRestore.classList.add('d-none');
    });
  }

  ['dragenter', 'dragover'].forEach(evt => {
    dropzone.addEventListener(evt, (e) => {
      e.preventDefault();
      e.stopPropagation();
      dropzone.classList.add('is-dragover');
    });
  });

  ['dragleave', 'drop'].forEach(evt => {
    dropzone.addEventListener(evt, (e) => {
      e.preventDefault();
      e.stopPropagation();
      dropzone.classList.remove('is-dragover');
    });
  });

  dropzone.addEventListener('drop', (e) => {
    const dt = e.dataTransfer;
    if (dt && dt.files && dt.files.length > 0) {
      fileInput.files = dt.files;
      validateAndPreview(dt.files[0]);
    }
  });
})();
