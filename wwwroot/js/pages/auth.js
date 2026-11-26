/* ==========================================================================
   SPIKE OS — MODERN SPORTS MANAGEMENT PLATFORM
   Authentication Controller (/Account/Login — Login, Register & Email OTP)
   ========================================================================== */

let currentAuthMode = 'login';
let redirectTargetUrl = '';
let regOtpTimerHandle = null;
let regOtpExpiresAtMs = 0;
let regResendAvailableAtMs = 0;
let lastSubmittedRegisterEmail = '';

document.addEventListener('DOMContentLoaded', async () => {
  const params = new URLSearchParams(window.location.search);
  redirectTargetUrl = params.get('redirect') || '';
  const requestedMode = params.get('mode') || '';
  const noticeMsg = params.get('notice') || '';

  const session = typeof getAuthSession === 'function' ? getAuthSession() : null;
  if (session && session.loggedIn) {
    const banner = document.getElementById('activeSessionBanner');
    const nameEl = document.getElementById('activeSessionName');
    const roleEl = document.getElementById('activeSessionRole');
    if (banner && nameEl && roleEl) {
      banner.style.display = 'flex';
      nameEl.textContent = session.name;
      roleEl.textContent = session.roleLabel;
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

  const initialOtp = window.__INITIAL_REG_OTP_STATE__ || null;
  if (initialOtp && initialOtp.verifiedSuccess) {
    switchAuthMode('register', true);
    showRegistrationSuccessStage(initialOtp.verifiedMessage);
    return;
  }

  if (initialOtp && initialOtp.hasPending) {
    switchAuthMode('register', true);
    activateOtpVerificationStage(initialOtp, { preserveServerAlerts: true });
    return;
  }

  if (requestedMode === 'register' || (initialOtp && (initialOtp.registerError || initialOtp.registerInfo))) {
    switchAuthMode('register', true);
  }

  // Double-check live session state on back/forward cache restore
  if (typeof fetchRegistrationSession === 'function') {
    const livePending = await fetchRegistrationSession();
    if (livePending && livePending.hasPending) {
      switchAuthMode('register', true);
      activateOtpVerificationStage(livePending, { preserveServerAlerts: true });
    }
  }
});

function switchAuthMode(mode, keepRegisterAlerts = false) {
  currentAuthMode = mode === 'register' ? 'register' : 'login';
  const stageBox = document.getElementById('authStageBox');
  const mobLogin = document.getElementById('mobTabLogin');
  const mobReg = document.getElementById('mobTabRegister');
  const loginPanel = document.getElementById('authPanelLogin');
  const regPanel = document.getElementById('authPanelRegister');

  if (!keepRegisterAlerts) {
    hideLoginError();
  }

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

function setRegisterStageView(stageName) {
  const formStep = document.getElementById('regStepForm');
  const otpStep = document.getElementById('regStepVerifyOtp');
  const successStep = document.getElementById('regStepSuccess');

  if (formStep) formStep.style.display = stageName === 'form' ? 'block' : 'none';
  if (otpStep) otpStep.style.display = stageName === 'otp' ? 'block' : 'none';
  if (successStep) successStep.style.display = stageName === 'success' ? 'block' : 'none';
}

function showRegisterInfo(message) {
  const box = document.getElementById('registerInfoAlert');
  const txt = document.getElementById('registerInfoText');
  if (box && txt) {
    if (message) {
      txt.textContent = message;
      box.style.display = 'flex';
    } else {
      box.style.display = 'none';
    }
  }
}

function formatSecondsMMSS(totalSec) {
  const clamped = Math.max(0, Math.floor(Number(totalSec) || 0));
  const mins = Math.floor(clamped / 60);
  const secs = clamped % 60;
  return `${String(mins).padStart(2, '0')}:${String(secs).padStart(2, '0')}`;
}

function startOtpCountdownLoop() {
  if (regOtpTimerHandle) {
    clearInterval(regOtpTimerHandle);
    regOtpTimerHandle = null;
  }

  const tick = () => {
    const now = Date.now();
    const expiryEl = document.getElementById('otpExpiryCountdown');
    if (expiryEl) {
      const remExp = Math.max(0, Math.ceil((regOtpExpiresAtMs - now) / 1000));
      expiryEl.textContent = remExp > 0 ? formatSecondsMMSS(remExp) : 'Đã hết hạn';
    }

    const resendBtn = document.getElementById('btnResendOtp');
    const resendTxt = document.getElementById('btnResendOtpText');
    const remCooldown = Math.max(0, Math.ceil((regResendAvailableAtMs - now) / 1000));
    if (resendBtn && resendTxt) {
      if (remCooldown > 0) {
        resendBtn.disabled = true;
        resendTxt.textContent = `Gửi lại mã (${remCooldown}s)`;
      } else {
        resendBtn.disabled = false;
        resendTxt.textContent = 'Gửi lại mã';
      }
    }
  };

  tick();
  regOtpTimerHandle = setInterval(tick, 1000);
}

function stopOtpCountdownLoop() {
  if (regOtpTimerHandle) {
    clearInterval(regOtpTimerHandle);
    regOtpTimerHandle = null;
  }
}

function activateOtpVerificationStage(state, options = {}) {
  if (!options.preserveServerAlerts) {
    hideLoginError();
  }

  setRegisterStageView('otp');

  const tokenInput = document.getElementById('otpVerificationToken');
  if (tokenInput && state.verificationToken) {
    tokenInput.value = state.verificationToken;
  }

  const emailEl = document.getElementById('otpMaskedEmailDisplay');
  if (emailEl && state.maskedEmail) {
    emailEl.textContent = state.maskedEmail;
  }

  const attemptsEl = document.getElementById('otpRemainingAttemptsText');
  if (attemptsEl) {
    const rem = typeof state.remainingAttempts === 'number' ? state.remainingAttempts : 5;
    attemptsEl.textContent = `Còn ${rem} lần thử`;
  }

  const now = Date.now();
  const expSeconds = Number(state.expiresInSeconds) || 0;
  const cdSeconds = Number(state.resendCooldownSeconds) || 0;
  regOtpExpiresAtMs = now + expSeconds * 1000;
  regResendAvailableAtMs = now + cdSeconds * 1000;
  startOtpCountdownLoop();

  if (state.deliveryFailed && !options.preserveServerAlerts) {
    showLoginError('Lần gửi mã trước chưa thành công do lỗi kết nối máy chủ thư. Vui lòng bấm Gửi lại mã.', true);
  } else if (state.message && !options.preserveServerAlerts) {
    showRegisterInfo(state.message);
  }

  const otpInput = document.getElementById('regOtpInput');
  if (otpInput) {
    otpInput.value = '';
    clearFieldError('regOtpInput');
    setTimeout(() => {
      try { otpInput.focus(); } catch (_) {}
    }, 30);
  }
}

function showRegistrationSuccessStage(message) {
  stopOtpCountdownLoop();
  hideLoginError();
  showRegisterInfo('');
  setRegisterStageView('success');

  const descEl = document.getElementById('regSuccessDesc');
  if (descEl && message) {
    descEl.textContent = message;
  }

  const regPassword = document.getElementById('regPassword');
  const regConfirm = document.getElementById('regConfirmPassword');
  if (regPassword) regPassword.value = '';
  if (regConfirm) regConfirm.value = '';
}

function switchToLoginAfterRegisterSuccess() {
  setRegisterStageView('form');
  switchAuthMode('login');

  const loginInput = document.getElementById('loginEmail');
  const passInput = document.getElementById('loginPassword');
  if (loginInput && lastSubmittedRegisterEmail && !loginInput.value) {
    loginInput.value = lastSubmittedRegisterEmail;
  }

  setTimeout(() => {
    const target = (loginInput && loginInput.value) ? passInput : loginInput;
    if (target) {
      try { target.focus(); } catch (_) {}
    }
  }, 30);
}

function handleOtpDigitInput(inputEl) {
  if (!inputEl) return;
  const digitsOnly = String(inputEl.value || '').replace(/\D+/g, '').slice(0, 6);
  if (inputEl.value !== digitsOnly) {
    inputEl.value = digitsOnly;
  }
  clearFieldError('regOtpInput');
}

function handleOtpPaste(event) {
  if (!event || !event.clipboardData) return;
  const pasted = event.clipboardData.getData('text') || '';
  const digits = pasted.replace(/\D+/g, '').slice(0, 6);
  if (!digits) return;
  event.preventDefault();
  const inputEl = document.getElementById('regOtpInput');
  if (inputEl) {
    inputEl.value = digits;
    clearFieldError('regOtpInput');
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
  if (isRegister) {
    showRegisterInfo('');
  }
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
  const b3 = document.getElementById('registerInfoAlert');
  if (b1) b1.style.display = 'none';
  if (b2) b2.style.display = 'none';
  if (b3) b3.style.display = 'none';
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

  if (!isValidVietnamPhone(phone)) {
    showFieldError('regPhone', 'SĐT gồm 9–11 chữ số (bắt đầu bằng 0 hoặc +84).');
    hasError = true;
  }

  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
    showFieldError('regEmail', 'Vui lòng nhập địa chỉ Email hợp lệ.');
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

  const btn = document.getElementById('btnSubmitRegister');
  const btnTxt = document.getElementById('btnSubmitRegisterText');
  if (btn) btn.disabled = true;
  if (btnTxt) btnTxt.textContent = 'Đang gửi mã xác minh...';

  lastSubmittedRegisterEmail = email.toLowerCase();
  const res = await registerUser({ name: fullName, phone, email, password, confirmPassword });

  if (btn) btn.disabled = false;
  if (btnTxt) btnTxt.textContent = 'Gửi mã xác minh';

  if (!res || !res.ok) {
    if (res && res.verificationToken && res.canResend) {
      activateOtpVerificationStage({
        verificationToken: res.verificationToken,
        maskedEmail: res.maskedEmail,
        expiresInSeconds: 0,
        resendCooldownSeconds: res.resendCooldownSeconds || res.retryAfterSeconds || 15,
        remainingAttempts: 5,
        deliveryFailed: true
      }, { preserveServerAlerts: true });
    }
    showLoginError((res && res.error) || 'Không thể gửi mã xác minh đăng ký.', true);
    return;
  }

  activateOtpVerificationStage(res);
}

async function handleVerifyOtpSubmit(event) {
  event.preventDefault();
  hideLoginError();

  const token = (document.getElementById('otpVerificationToken')?.value || '').trim();
  const otpRaw = (document.getElementById('regOtpInput')?.value || '').replace(/\D+/g, '');

  if (otpRaw.length !== 6) {
    showFieldError('regOtpInput', 'Mã xác minh phải gồm đúng 6 chữ số.');
    return;
  }

  const btn = document.getElementById('btnVerifyOtpSubmit');
  const btnTxt = document.getElementById('btnVerifyOtpSubmitText');
  if (btn) btn.disabled = true;
  if (btnTxt) btnTxt.textContent = 'Đang kiểm tra mã...';

  const res = await verifyRegistrationOtp(token, otpRaw);

  if (btn) btn.disabled = false;
  if (btnTxt) btnTxt.textContent = 'Xác nhận mã & Tạo tài khoản';

  if (!res || !res.ok) {
    if (res && typeof res.remainingAttempts === 'number') {
      const attemptsEl = document.getElementById('otpRemainingAttemptsText');
      if (attemptsEl) {
        attemptsEl.textContent = `Còn ${res.remainingAttempts} lần thử`;
      }
    }
    showLoginError((res && res.error) || 'Mã xác minh không chính xác.', true);
    if (res && res.code === 'OTP_ALREADY_USED') {
      showRegistrationSuccessStage(res.error);
    }
    return;
  }

  showRegistrationSuccessStage(res.message);
}

async function handleResendOtpClick() {
  hideLoginError();
  const token = (document.getElementById('otpVerificationToken')?.value || '').trim();
  const resendBtn = document.getElementById('btnResendOtp');
  const resendTxt = document.getElementById('btnResendOtpText');

  if (resendBtn) resendBtn.disabled = true;
  if (resendTxt) resendTxt.textContent = 'Đang gửi lại...';

  const res = await resendRegistrationOtp(token);
  if (!res || !res.ok) {
    const waitSec = (res && res.retryAfterSeconds) ? Number(res.retryAfterSeconds) : 0;
    if (waitSec > 0) {
      regResendAvailableAtMs = Date.now() + waitSec * 1000;
      startOtpCountdownLoop();
    } else if (resendBtn && resendTxt) {
      resendBtn.disabled = false;
      resendTxt.textContent = 'Gửi lại mã';
    }
    showLoginError((res && res.error) || 'Chưa thể gửi lại mã xác minh.', true);
    return;
  }

  activateOtpVerificationStage(res);
}

async function handleChangeRegisterEmail() {
  stopOtpCountdownLoop();
  hideLoginError();

  const token = (document.getElementById('otpVerificationToken')?.value || '').trim();
  if (typeof cancelRegistrationOtp === 'function') {
    await cancelRegistrationOtp(token);
  }

  const tokenInput = document.getElementById('otpVerificationToken');
  if (tokenInput) tokenInput.value = '';

  setRegisterStageView('form');
  const emailInput = document.getElementById('regEmail');
  if (emailInput) {
    setTimeout(() => {
      try {
        emailInput.focus();
        emailInput.select();
      } catch (_) {}
    }, 30);
  }
}

async function handleResetPassword() {
  const box = document.getElementById('forgotStatusBox');
  try {
    const res = await apiFetch('/api/auth/forgot-password', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({})
    });
    const data = await res.json().catch(() => ({}));
    const msg = data.error || 'Cổng OTP tự động chưa kích hoạt. Vui lòng liên hệ Hotline CLB để được cấp mật khẩu tạm.';
    if (box) {
      box.textContent = msg;
      box.style.display = 'block';
    }
    showToast('Chưa kích hoạt cổng OTP', msg, 'warning');
  } catch (_) {
    showToast('Thông báo', 'Vui lòng liên hệ Quầy lễ tân CLB để được hỗ trợ đặt lại mật khẩu.', 'warning');
  }
}
