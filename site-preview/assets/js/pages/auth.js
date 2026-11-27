/* ==========================================================================
   SPIKE OS — MODERN SPORTS MANAGEMENT PLATFORM
   Authentication Controller (/Account/Login — Login, Register & OTP Notice)
   ========================================================================== */

let currentAuthMode = 'login';
let redirectTargetUrl = '';

document.addEventListener('DOMContentLoaded', async () => {
  await SpikeDemo.ensureLoaded(false);
  const params = new URLSearchParams(window.location.search);
  redirectTargetUrl = params.get('redirect') || '';
  const requestedMode = params.get('mode') || '';
  const noticeMsg = params.get('notice') || '';

  const u = SpikeDemo.getActiveAccount();
  if (u && u.id) {
    const banner = document.getElementById('activeSessionBanner');
    const nameEl = document.getElementById('activeSessionName');
    const roleEl = document.getElementById('activeSessionRole');
    if (banner && nameEl && roleEl) {
      banner.style.display = 'flex';
      nameEl.textContent = u.name;
      roleEl.textContent = u.roleLabel || u.role;
    }
  }

  if (noticeMsg) {
    const alertBox = document.getElementById('redirectAlert');
    const alertText = document.getElementById('redirectAlertText');
    if (alertBox && alertText) {
      alertBox.style.display = 'flex';
      alertText.textContent = noticeMsg;
    }
  }

  if (requestedMode === 'register') {
    switchAuthMode('register');
  }
});

async function quickDemoLogin(roleKey) {
  await SpikeDemo.ensureLoaded(false);
  const raw = SpikeDemo.getRawState();
  const acc = (raw?.accounts || []).find(a => a.role === roleKey) || (raw?.accounts || [])[0];
  if (!acc) return;
  SpikeDemo.setActiveAccountId(acc.id);
  showToast('Đăng nhập Demo thành công', `Đang trải nghiệm với vai trò: ${acc.name} (${acc.roleLabel})`, 'success');
  setTimeout(() => {
    if (acc.role === 'admin') window.location.href = SpikeRoute('/Admin');
    else if (acc.role === 'staff') window.location.href = SpikeRoute('/Management');
    else window.location.href = SpikeRoute('/Account/Profile');
  }, 420);
}

function switchAuthMode(mode) {
  currentAuthMode = mode === 'register' ? 'register' : 'login';
  const stageBox = document.getElementById('authStageBox');
  const mobLogin = document.getElementById('mobTabLogin');
  const mobReg = document.getElementById('mobTabRegister');
  const loginPanel = document.getElementById('authPanelLogin');
  const regPanel = document.getElementById('authPanelRegister');

  hideLoginError();

  if (currentAuthMode === 'register') {
    if (stageBox) stageBox.classList.add('right-panel-active');
    if (mobLogin) mobLogin.classList.remove('active');
    if (mobReg) mobReg.classList.add('active');
    if (loginPanel) loginPanel.style.display = 'none';
    if (regPanel) regPanel.style.display = 'block';
  } else {
    if (stageBox) stageBox.classList.remove('right-panel-active');
    if (mobReg) mobReg.classList.remove('active');
    if (mobLogin) mobLogin.classList.add('active');
    if (regPanel) regPanel.style.display = 'none';
    if (loginPanel) loginPanel.style.display = 'block';
  }
}

function toggleAnimatedPassword(inputId, iconId) {
  const passInput = document.getElementById(inputId);
  const eyeIcon = document.getElementById(iconId);
  if (!passInput) return;

  if (passInput.type === 'password') {
    passInput.type = 'text';
    if (eyeIcon) eyeIcon.setAttribute('icon', 'lucide:eye-off');
  } else {
    passInput.type = 'password';
    if (eyeIcon) eyeIcon.setAttribute('icon', 'lucide:eye');
  }
}

function updatePasswordStrength(val) {
  const segs = [
    document.getElementById('passSeg1'),
    document.getElementById('passSeg2'),
    document.getElementById('passSeg3'),
    document.getElementById('passSeg4')
  ];
  const lbl = document.getElementById('passStrengthLabel');
  if (!lbl || segs.some(s => !s)) return;

  let score = 0;
  if (val.length >= 6) score++;
  if (val.length >= 8 && /[A-Z]/.test(val)) score++;
  if (/\d/.test(val)) score++;
  if (/[^A-Za-z0-9]/.test(val) || val.length >= 10) score++;

  const colors = ['var(--border-default)', '#DC2626', '#D97706', '#2E8B57', '#226B48'];
  const labels = ['Chưa nhập', 'Yếu (Cần ít nhất 6 ký tự)', 'Trung bình', 'Mạnh', 'Rất mạnh & An toàn'];

  segs.forEach((s, idx) => {
    s.style.background = idx < score ? colors[score] : 'var(--border-default)';
  });
  lbl.textContent = 'Độ mạnh mật khẩu: ' + (val.length === 0 ? labels[0] : labels[score] || labels[1]);
}

function showFieldError(fieldId, message) {
  const wrap = document.getElementById('fieldWrap' + fieldId.charAt(0).toUpperCase() + fieldId.slice(1));
  const errEl = document.getElementById(fieldId + 'Error');
  if (wrap) wrap.classList.add('has-error');
  if (errEl) {
    if (message) errEl.textContent = message;
    errEl.classList.add('visible');
  }
}

function clearFieldError(fieldId) {
  const wrap = document.getElementById('fieldWrap' + fieldId.charAt(0).toUpperCase() + fieldId.slice(1));
  const errEl = document.getElementById(fieldId + 'Error');
  if (wrap) wrap.classList.remove('has-error');
  if (errEl) errEl.classList.remove('visible');
}

function showLoginError(message, isRegister = false) {
  const boxId = isRegister ? 'registerErrorAlert' : 'loginErrorAlert';
  const txtId = isRegister ? 'registerErrorText' : 'loginErrorText';
  const box = document.getElementById(boxId);
  const txt = document.getElementById(txtId);
  if (box && txt) {
    txt.textContent = message;
    box.style.display = 'flex';
  }
}

function hideLoginError() {
  const b1 = document.getElementById('loginErrorAlert');
  const b2 = document.getElementById('registerErrorAlert');
  if (b1) b1.style.display = 'none';
  if (b2) b2.style.display = 'none';
}

async function triggerAnimatedLogin(emailVal, passVal) {
  const btn = document.getElementById('btnSubmitLogin');
  const btnTxt = document.getElementById('btnSubmitLoginText');
  if (btn) btn.disabled = true;
  if (btnTxt) btnTxt.textContent = 'Đang xác thực tài khoản...';

  const res = await login(emailVal, passVal, redirectTargetUrl);
  if (!res || !res.ok) {
    if (btn) btn.disabled = false;
    if (btnTxt) btnTxt.textContent = 'Đăng nhập vào Sân Đấu';
    showLoginError((res && res.error) || 'Thông tin đăng nhập không chính xác.', false);
  }
}

function handleFormLogin(event) {
  event.preventDefault();
  hideLoginError();

  const emailVal = (document.getElementById('loginEmail')?.value || '').trim();
  const passVal = (document.getElementById('loginPassword')?.value || '').trim();
  let hasError = false;

  if (!emailVal || emailVal.length < 3) {
    showFieldError('loginEmail', 'Vui lòng nhập Tên đăng nhập, Email hoặc Số điện thoại.');
    hasError = true;
  }

  if (!passVal || passVal.length < 6) {
    showFieldError('loginPassword', 'Mật khẩu phải có tối thiểu 6 ký tự.');
    hasError = true;
  }

  if (hasError) {
    showLoginError('Vui lòng kiểm tra lại thông tin được đánh dấu đỏ.', false);
    return;
  }

  triggerAnimatedLogin(emailVal, passVal);
}

async function handleFormRegister(event) {
  event.preventDefault();
  hideLoginError();

  const fullName = (document.getElementById('regFullName')?.value || '').trim();
  const phone = (document.getElementById('regPhone')?.value || '').trim();
  const email = (document.getElementById('regEmail')?.value || '').trim();
  const password = document.getElementById('regPassword')?.value || '';
  const confirmPassword = document.getElementById('regConfirmPassword')?.value || '';
  const agreeTerms = document.getElementById('regAgreeTerms') ? document.getElementById('regAgreeTerms').checked : true;

  let hasError = false;

  if (!fullName || fullName.length < 2) {
    showFieldError('regFullName', 'Nhập họ và tên đầy đủ.');
    hasError = true;
  }

  if (!/^[0-9]{9,11}$/.test(phone.replace(/\s+/g, ''))) {
    showFieldError('regPhone', 'SĐT gồm 9–11 chữ số.');
    hasError = true;
  }

  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
    showFieldError('regEmail', 'Địa chỉ Email không hợp lệ.');
    hasError = true;
  }

  if (!password || password.length < 6) {
    showFieldError('regPassword', 'Tối thiểu 6 ký tự.');
    hasError = true;
  }

  if (!confirmPassword || confirmPassword !== password) {
    showFieldError('regConfirmPassword', 'Mật khẩu xác nhận không khớp.');
    hasError = true;
  }

  if (hasError) {
    showLoginError('Vui lòng điền đầy đủ và chính xác các trường thông tin.', true);
    return;
  }

  if (!agreeTerms) {
    showLoginError('Vui lòng đồng ý với Nội quy Sân đấu & Chính sách CLB.', true);
    return;
  }

  const res = await registerUser({ name: fullName, phone, email, password }, redirectTargetUrl);
  if (!res || !res.ok) {
    showLoginError((res && res.error) || 'Không thể đăng ký tài khoản.', true);
  }
}

function logout() {
  if (typeof logoutUser === 'function') logoutUser();
}

function handleResetPassword() {
  const box = document.getElementById('forgotStatusBox');
  const msg = 'Bản UI Demo không gửi SMS/Email OTP thật. Bạn có thể chọn nhanh tài khoản Demo ở khung đăng nhập.';
  if (box) {
    box.textContent = msg;
    box.style.display = 'block';
  }
  showToast('Chế độ UI Demo', msg, 'info');
}
