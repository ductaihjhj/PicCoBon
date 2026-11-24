(function () {
  "use strict";

  const bootstrap = window.__SGL_BOOTSTRAP__ || {};
  const assetBase = "/assets/san-giao-luu";

  const mapCanvas = document.getElementById("courtMapCanvas");
  const actorsLayer = document.getElementById("courtActors");
  const emptyCourtState = document.getElementById("courtEmptyState");
  const visibleCountBadge = document.getElementById("visibleCountBadge");
  const memberList = document.getElementById("memberList");
  const searchInput = document.getElementById("searchMembers");
  const readyOnlyBtn = document.getElementById("readyOnlyFilter");
  const profileBody = document.getElementById("profileCardBody");
  const matchPanel = document.getElementById("matchPanel");
  const matchGrid = document.getElementById("matchGrid");
  const findMatchBtn = document.getElementById("findMatchBtn");
  const closeMatchBtn = document.getElementById("closeMatchPanel");
  const poseButtons = Array.from(document.querySelectorAll(".sgl-pose-btn[data-pose]"));
  const autoToggleBtn = document.getElementById("autoRoamToggle");

  // Invites UI
  const invitesContainer = document.getElementById("invitesContainer");
  const tabReceivedBtn = document.getElementById("tabReceivedInvites");
  const tabSentBtn = document.getElementById("tabSentInvites");
  const receivedBadge = document.getElementById("receivedInvitesBadge");

  // Settings Modal UI
  const openSettingsBtn = document.getElementById("openSocialSettingsBtn");
  const emptyEnableBtn = document.getElementById("emptyEnableCourtBtn");
  const settingsModal = document.getElementById("socialSettingsModal");
  const closeSettingsBtns = Array.from(document.querySelectorAll("[data-close-settings]"));
  const saveSettingsBtn = document.getElementById("saveSocialSettingsBtn");
  const optVisible = document.getElementById("optVisibleOnCourt");
  const optReady = document.getElementById("optReadyToPlay");
  const optSuggest = document.getElementById("optAllowMatchSuggestion");
  const optSkillConfirmed = document.getElementById("optSkillConfirmed");
  const optSkillSelect = document.getElementById("optConfirmedSkillLevel");
  const spritePickerGrid = document.getElementById("spritePickerGrid");
  const freeSlotChips = Array.from(document.querySelectorAll(".sgl-slot-chip"));

  const toastEl = document.getElementById("sglToast");

  let myProfile = bootstrap.myProfile || {};
  let players = Array.isArray(bootstrap.players) ? bootstrap.players : [];
  let invites = { sent: [], received: [] };
  let activeInviteTab = "received";
  let onlyReadyFilter = false;
  let selectedAccountId = null;
  let selectedSpriteId = myProfile.spriteId || "maya";
  let selectedFreeSlots = new Set(Array.isArray(myProfile.freeSlots) ? myProfile.freeSlots : []);
  let courtEngine = null;
  let toastTimer = null;

  function getCsrfToken() {
    const meta = document.querySelector('meta[name="csrf-token"]');
    return meta ? meta.getAttribute("content") || "" : "";
  }

  function showToast(message, isError) {
    if (!toastEl) return;
    toastEl.textContent = message;
    toastEl.classList.toggle("is-error", Boolean(isError));
    toastEl.hidden = false;
    if (toastTimer) clearTimeout(toastTimer);
    toastTimer = setTimeout(() => {
      toastEl.hidden = true;
    }, 3600);
  }

  function escapeHtml(value) {
    return String(value == null ? "" : value)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;")
      .replace(/'/g, "&#39;");
  }

  function defaultTomorrowIso() {
    const now = new Date();
    const vn = new Date(now.getTime() + 7 * 60 * 60 * 1000 + 24 * 60 * 60 * 1000);
    return vn.toISOString().slice(0, 10);
  }

  function renderPortraitHtml(player, extraClass) {
    const spriteId = (player.spriteId || "maya").toLowerCase();
    const fallbackSrc = `${assetBase}/${encodeURIComponent(spriteId)}-profile.webp`;
    const avatarSrc = player.avatar && player.avatar.trim() ? player.avatar : fallbackSrc;
    const cls = extraClass ? `sgl-portrait ${extraClass}` : "sgl-portrait";
    return `<div class="${cls}"><img src="${escapeHtml(avatarSrc)}" alt="${escapeHtml(player.displayName)}" onerror="this.onerror=null;this.src='${escapeHtml(fallbackSrc)}';"></div>`;
  }

  function mapEngineMembers(list) {
    return list.map((p) => ({
      id: String(p.accountId),
      accountId: p.accountId,
      spriteId: p.spriteId,
      displayName: p.displayName,
      shortName: p.isSelf ? `${p.displayName} (Bạn)` : p.displayName,
      skillLabel: p.skillLabel,
      skillConfirmed: p.skillConfirmed,
      playStyle: p.playStyle,
      freeSlots: Array.isArray(p.freeSlots) ? p.freeSlots : [],
      bio: p.bio,
      readyToPlay: Boolean(p.readyToPlay),
      isSelf: Boolean(p.isSelf),
      avatar: p.avatar
    }));
  }

  async function renderCourtAndDirectory(syncEngine) {
    if (visibleCountBadge) {
      visibleCountBadge.textContent = `${players.length} hội viên trên sân`;
    }
    if (emptyCourtState) {
      emptyCourtState.hidden = players.length > 0;
    }

    const q = (searchInput?.value || "").trim().toLowerCase();
    const filtered = players.filter((p) => {
      if (onlyReadyFilter && !p.readyToPlay) return false;
      if (!q) return true;
      const hay = `${p.displayName} ${p.skillLabel} ${p.playStyle} ${(p.freeSlots || []).join(" ")}`.toLowerCase();
      return hay.includes(q);
    });

    if (!filtered.length) {
      memberList.innerHTML = `<p class="text-sm" style="color:var(--text-muted);margin:6px 0;">Chưa có hội viên nào khớp bộ lọc hiện tại.</p>`;
    } else {
      memberList.innerHTML = filtered.map((p) => {
        const isSel = Number(selectedAccountId) === Number(p.accountId);
        const statusText = p.readyToPlay ? "Sẵn sàng giao lưu" : "Đang xem sân";
        return `
          <button type="button" class="sgl-member-item ${isSel ? "is-selected" : ""}" data-select-account="${p.accountId}">
            <span class="sgl-member-meta">
              ${renderPortraitHtml(p, "")}
              <span style="min-width:0;">
                <span class="sgl-member-name">${escapeHtml(p.displayName)}${p.isSelf ? " (Bạn)" : ""}</span>
                <span class="sgl-member-sub">${escapeHtml(statusText)} · ${escapeHtml(p.skillLabel)}</span>
              </span>
            </span>
            <span class="badge ${p.readyToPlay ? "badge-success" : "badge-neutral"}">${p.readyToPlay ? "Sẵn sàng" : "Xem sân"}</span>
          </button>
        `;
      }).join("");
    }

    if (courtEngine && syncEngine) {
      await courtEngine.setMembers(mapEngineMembers(players));
      if (selectedAccountId) {
        courtEngine.selectMember(String(selectedAccountId));
      }
    }

    renderSelectedProfile();
  }

  function renderSelectedProfile() {
    const p = players.find((item) => Number(item.accountId) === Number(selectedAccountId));
    if (!p) {
      profileBody.innerHTML = `
        <p class="text-sm" style="color:var(--text-muted);margin:0;">
          Nhấn vào một nhân vật đang đi dạo trên sân hoặc chọn ở danh sách bên trên để xem trình độ tự khai, khung giờ rảnh và gửi lời mời giao lưu.
        </p>
      `;
      return;
    }

    const slotsText = (p.freeSlots && p.freeSlots.length) ? p.freeSlots.join(", ") : "Chưa chia sẻ khung giờ rảnh";
    const tomorrow = defaultTomorrowIso();

    profileBody.innerHTML = `
      <div class="sgl-profile-top">
        <div style="display:flex;align-items:center;gap:12px;min-width:0;">
          ${renderPortraitHtml(p, "sgl-portrait--lg")}
          <div style="min-width:0;">
            <strong style="display:block;font-size:1rem;color:var(--text-primary);">${escapeHtml(p.displayName)}${p.isSelf ? " (Bạn)" : ""}</strong>
            <span class="badge ${p.readyToPlay ? "badge-success" : "badge-neutral"}" style="margin-top:4px;">
              ${p.readyToPlay ? "Sẵn sàng giao lưu" : "Đang xem sân"}
            </span>
          </div>
        </div>
      </div>

      <p class="sgl-profile-bio">${escapeHtml(p.bio)}</p>

      <dl class="sgl-spec-list">
        <div class="sgl-spec-row">
          <dt>Trình độ</dt>
          <dd>${escapeHtml(p.skillLabel)}</dd>
        </div>
        <div class="sgl-spec-row">
          <dt>Lối đánh</dt>
          <dd>${escapeHtml(p.playStyle)}</dd>
        </div>
        <div class="sgl-spec-row">
          <dt>Giờ thường rảnh</dt>
          <dd>${escapeHtml(slotsText)}</dd>
        </div>
        <div class="sgl-spec-row">
          <dt>Nhân vật pixel</dt>
          <dd style="text-transform:uppercase;">${escapeHtml(p.spriteId)}</dd>
        </div>
      </dl>

      ${p.isSelf ? `
        <button type="button" class="btn btn-secondary btn-sm" style="width:100%;" id="editOwnProfileFromCardBtn">
          Chỉnh sửa hồ sơ giao lưu của tôi
        </button>
      ` : `
        <form id="sendSocialInviteForm" class="sgl-invite-form">
          <strong style="font-size:0.875rem;color:var(--text-primary);">Gửi lời mời giao lưu</strong>
          <div class="sgl-invite-grid">
            <div>
              <label class="form-label" for="inviteDateInput">Ngày dự kiến</label>
              <input type="date" id="inviteDateInput" class="form-input" value="${tomorrow}" min="${tomorrow}" required>
            </div>
            <div>
              <label class="form-label" for="inviteTimeSelect">Giờ bắt đầu</label>
              <select id="inviteTimeSelect" class="form-select">
                <option value="06:30">06:30</option>
                <option value="07:30">07:30</option>
                <option value="08:30">08:30</option>
                <option value="17:00">17:00</option>
                <option value="18:00">18:00</option>
                <option value="19:00" selected>19:00</option>
                <option value="20:00">20:00</option>
              </select>
            </div>
          </div>
          <div>
            <label class="form-label" for="inviteDurationSelect">Thời lượng</label>
            <select id="inviteDurationSelect" class="form-select">
              <option value="60" selected>60 phút</option>
              <option value="90">90 phút</option>
              <option value="120">120 phút</option>
            </select>
          </div>
          <div>
            <label class="form-label" for="inviteNoteInput">Lời nhắn (tùy chọn)</label>
            <input type="text" id="inviteNoteInput" class="form-input" maxlength="200" placeholder="VD: Đánh đôi nhẹ tối thứ 6 nhé!">
          </div>
          <button type="submit" class="btn btn-primary btn-sm" id="submitInviteBtn">Gửi lời mời giao lưu</button>
        </form>
      `}
    `;

    const editOwnBtn = document.getElementById("editOwnProfileFromCardBtn");
    if (editOwnBtn) {
      editOwnBtn.addEventListener("click", openSettingsModal);
    }

    const inviteForm = document.getElementById("sendSocialInviteForm");
    if (inviteForm) {
      inviteForm.addEventListener("submit", async (e) => {
        e.preventDefault();
        const submitBtn = document.getElementById("submitInviteBtn");
        if (submitBtn) submitBtn.disabled = true;

        try {
          const res = await fetch("/api/social/invites", {
            method: "POST",
            headers: {
              "Content-Type": "application/json",
              "RequestVerificationToken": getCsrfToken()
            },
            body: JSON.stringify({
              receiverAccountId: p.accountId,
              proposedDate: document.getElementById("inviteDateInput").value,
              proposedStartTime: document.getElementById("inviteTimeSelect").value,
              durationMinutes: Number(document.getElementById("inviteDurationSelect").value),
              note: document.getElementById("inviteNoteInput").value
            })
          });
          const data = await res.json();
          if (!res.ok || !data.ok) {
            showToast(data.message || "Không thể gửi lời mời.", true);
            return;
          }
          showToast(`Đã gửi lời mời giao lưu tới ${p.displayName}.`, false);
          activeInviteTab = "sent";
          syncInviteTabs();
          await refreshInvites();
        } catch {
          showToast("Lỗi kết nối khi gửi lời mời.", true);
        } finally {
          if (submitBtn) submitBtn.disabled = false;
        }
      });
    }
  }

  function statusBadgeHtml(status) {
    switch (status) {
      case "accepted":
        return `<span class="badge badge-success">Đã đồng ý</span>`;
      case "countered":
        return `<span class="badge badge-info">Đề xuất giờ khác</span>`;
      case "declined":
        return `<span class="badge badge-danger">Đã từ chối</span>`;
      case "cancelled":
        return `<span class="badge badge-neutral">Đã hủy</span>`;
      default:
        return `<span class="badge badge-warning">Đang chờ</span>`;
    }
  }

  function renderInvites() {
    const list = activeInviteTab === "received" ? (invites.received || []) : (invites.sent || []);
    const pendingReceivedCount = (invites.received || []).filter((i) => i.status === "pending").length;
    if (receivedBadge) {
      receivedBadge.textContent = String(pendingReceivedCount);
      receivedBadge.hidden = pendingReceivedCount === 0;
    }

    if (!list.length) {
      invitesContainer.innerHTML = `<p class="text-sm" style="color:var(--text-muted);margin:0;">Chưa có lời mời nào trong mục này.</p>`;
      return;
    }

    invitesContainer.innerHTML = list.map((inv) => {
      const bookingDate = inv.status === "countered" && inv.counterDate ? inv.counterDate : inv.proposedDate;
      const bookingTime = inv.status === "countered" && inv.counterStartTime ? inv.counterStartTime : inv.proposedStartTime;
      const bookingHref = `/Booking?date=${encodeURIComponent(bookingDate)}&time=${encodeURIComponent(bookingTime)}`;

      let actionsHtml = "";
      if (inv.direction === "received" && inv.status === "pending") {
        actionsHtml = `
          <div class="sgl-invite-actions">
            <button type="button" class="btn btn-primary btn-xs" data-invite-action="accept" data-invite-id="${inv.inviteId}">Đồng ý</button>
            <button type="button" class="btn btn-secondary btn-xs" data-toggle-counter="${inv.inviteId}">Đề xuất giờ khác</button>
            <button type="button" class="btn btn-ghost btn-xs" data-invite-action="decline" data-invite-id="${inv.inviteId}">Từ chối</button>
          </div>
          <div class="sgl-counter-box" id="counterBox-${inv.inviteId}" hidden>
            <input type="date" class="form-input" style="max-width:155px;" id="counterDate-${inv.inviteId}" value="${escapeHtml(inv.proposedDate)}" min="${defaultTomorrowIso()}">
            <input type="time" class="form-input" style="max-width:120px;" id="counterTime-${inv.inviteId}" value="${escapeHtml(inv.proposedStartTime)}" step="1800">
            <button type="button" class="btn btn-primary btn-xs" data-submit-counter="${inv.inviteId}">Gửi giờ mới</button>
          </div>
        `;
      } else if (inv.direction === "sent" && inv.status === "countered") {
        actionsHtml = `
          <div class="sgl-invite-actions">
            <button type="button" class="btn btn-primary btn-xs" data-invite-action="accept" data-invite-id="${inv.inviteId}">Đồng ý giờ mới</button>
            <button type="button" class="btn btn-ghost btn-xs" data-invite-action="cancel" data-invite-id="${inv.inviteId}">Hủy lời mời</button>
          </div>
        `;
      } else if (inv.direction === "sent" && inv.status === "pending") {
        actionsHtml = `
          <div class="sgl-invite-actions">
            <button type="button" class="btn btn-ghost btn-xs" data-invite-action="cancel" data-invite-id="${inv.inviteId}">Hủy lời mời</button>
          </div>
        `;
      } else if (inv.status === "accepted") {
        actionsHtml = `
          <div class="sgl-invite-actions">
            <a href="${bookingHref}" class="btn btn-primary btn-xs">Xem sân trống tại /Booking</a>
          </div>
        `;
      }

      return `
        <div class="sgl-invite-item">
          <div class="sgl-invite-item-head">
            <div style="display:flex;align-items:center;gap:10px;">
              ${renderPortraitHtml({ displayName: inv.partnerName, spriteId: inv.partnerSpriteId, avatar: inv.partnerAvatar }, "")}
              <div>
                <strong style="font-size:0.9rem;color:var(--text-primary);">${escapeHtml(inv.partnerName)}</strong>
                <span class="sgl-member-sub">${escapeHtml(inv.partnerSkillLabel)}</span>
              </div>
            </div>
            ${statusBadgeHtml(inv.status)}
          </div>
          <div class="text-sm" style="color:var(--text-secondary);">
            Đề xuất: <strong>${escapeHtml(inv.proposedDate)}</strong> lúc <strong>${escapeHtml(inv.proposedStartTime)}</strong> (${inv.durationMinutes} phút)
            ${inv.status === "countered" && inv.counterDate ? `<br>Giờ đề xuất lại: <strong>${escapeHtml(inv.counterDate)}</strong> lúc <strong>${escapeHtml(inv.counterStartTime || "")}</strong>` : ""}
            ${inv.note ? `<br><span style="color:var(--text-muted);">“${escapeHtml(inv.note)}”</span>` : ""}
          </div>
          ${actionsHtml}
        </div>
      `;
    }).join("");
  }

  function syncInviteTabs() {
    tabReceivedBtn?.classList.toggle("is-active", activeInviteTab === "received");
    tabSentBtn?.classList.toggle("is-active", activeInviteTab === "sent");
  }

  async function refreshCourt() {
    const res = await fetch("/api/social/court");
    const data = await res.json();
    if (res.ok && data.ok) {
      myProfile = data.myProfile || myProfile;
      players = Array.isArray(data.players) ? data.players : [];
      await renderCourtAndDirectory(true);
    }
  }

  async function refreshInvites() {
    const res = await fetch("/api/social/invites");
    const data = await res.json();
    if (res.ok && data.ok) {
      invites = {
        sent: Array.isArray(data.sent) ? data.sent : [],
        received: Array.isArray(data.received) ? data.received : []
      };
      renderInvites();
    }
  }

  function openSettingsModal() {
    if (!settingsModal) return;
    optVisible.checked = Boolean(myProfile.isVisibleOnCourt);
    optReady.checked = Boolean(myProfile.isReadyToPlay);
    optSuggest.checked = Boolean(myProfile.allowMatchSuggestion);
    optSkillConfirmed.checked = Boolean(myProfile.skillConfirmed);
    optSkillSelect.value = myProfile.confirmedSkillLevel || "Giao lưu (DUPR 3.0)";
    optSkillSelect.disabled = !optSkillConfirmed.checked;

    selectedSpriteId = myProfile.spriteId || "maya";
    spritePickerGrid.querySelectorAll(".sgl-sprite-option").forEach((btn) => {
      btn.classList.toggle("is-selected", btn.getAttribute("data-sprite-id") === selectedSpriteId);
    });

    selectedFreeSlots = new Set(Array.isArray(myProfile.freeSlots) ? myProfile.freeSlots : []);
    freeSlotChips.forEach((chip) => {
      chip.classList.toggle("is-selected", selectedFreeSlots.has(chip.getAttribute("data-slot")));
    });

    settingsModal.hidden = false;
  }

  function closeSettingsModal() {
    if (settingsModal) settingsModal.hidden = true;
  }

  // Event bindings
  searchInput?.addEventListener("input", () => renderCourtAndDirectory(false));

  readyOnlyBtn?.addEventListener("click", () => {
    onlyReadyFilter = !onlyReadyFilter;
    readyOnlyBtn.classList.toggle("is-active", onlyReadyFilter);
    readyOnlyBtn.setAttribute("aria-pressed", String(onlyReadyFilter));
    renderCourtAndDirectory(false);
  });

  memberList?.addEventListener("click", (e) => {
    const btn = e.target.closest("[data-select-account]");
    if (!btn) return;
    selectedAccountId = Number(btn.getAttribute("data-select-account"));
    renderCourtAndDirectory(false);
    courtEngine?.selectMember(String(selectedAccountId));
  });

  findMatchBtn?.addEventListener("click", async () => {
    findMatchBtn.disabled = true;
    try {
      const res = await fetch("/api/social/matches");
      const data = await res.json();
      const matches = (res.ok && data.ok && Array.isArray(data.suggestions)) ? data.suggestions : [];
      if (!matches.length) {
        matchGrid.innerHTML = `<p class="text-sm" style="color:var(--text-muted);margin:0;">Hiện chưa có hội viên nào khác bật gợi ý ghép cặp. Bạn có thể thử lại sau hoặc mời trực tiếp trên sân.</p>`;
        courtEngine?.highlightMembers([]);
      } else {
        courtEngine?.highlightMembers(matches.map((m) => String(m.accountId)));
        matchGrid.innerHTML = matches.map((m) => `
          <article class="sgl-match-card">
            <div>
              <div class="sgl-match-head">
                ${renderPortraitHtml({ displayName: m.displayName, spriteId: m.spriteId, avatar: m.avatar }, "")}
                <div>
                  <strong style="display:block;font-size:0.9rem;color:var(--text-primary);">${escapeHtml(m.displayName)}</strong>
                  <span style="font-size:0.78rem;font-weight:600;color:var(--brand-primary-ink);">${escapeHtml(m.skillLabel)}</span>
                </div>
              </div>
              <p class="sgl-match-reason" style="margin-top:8px;">${escapeHtml(m.reason)}</p>
            </div>
            <button type="button" class="btn btn-secondary btn-xs" data-focus-match="${m.accountId}">Xem hồ sơ &amp; mời</button>
          </article>
        `).join("");
      }
      matchPanel.hidden = false;
    } finally {
      findMatchBtn.disabled = false;
    }
  });

  closeMatchBtn?.addEventListener("click", () => {
    matchPanel.hidden = true;
    courtEngine?.highlightMembers([]);
  });

  matchGrid?.addEventListener("click", (e) => {
    const btn = e.target.closest("[data-focus-match]");
    if (!btn) return;
    selectedAccountId = Number(btn.getAttribute("data-focus-match"));
    renderCourtAndDirectory(false);
    courtEngine?.selectMember(String(selectedAccountId));
  });

  poseButtons.forEach((btn) => {
    btn.addEventListener("click", () => {
      poseButtons.forEach((b) => b.classList.remove("is-active"));
      btn.classList.add("is-active");
      const pose = btn.getAttribute("data-pose");
      if (courtEngine && pose) {
        courtEngine.triggerSelectedAction(pose);
      }
    });
  });

  autoToggleBtn?.addEventListener("click", () => {
    const isPressed = autoToggleBtn.getAttribute("aria-pressed") === "true";
    autoToggleBtn.classList.toggle("is-active", !isPressed);
  });

  tabReceivedBtn?.addEventListener("click", () => {
    activeInviteTab = "received";
    syncInviteTabs();
    renderInvites();
  });

  tabSentBtn?.addEventListener("click", () => {
    activeInviteTab = "sent";
    syncInviteTabs();
    renderInvites();
  });

  invitesContainer?.addEventListener("click", async (e) => {
    const toggleBtn = e.target.closest("[data-toggle-counter]");
    if (toggleBtn) {
      const id = toggleBtn.getAttribute("data-toggle-counter");
      const box = document.getElementById(`counterBox-${id}`);
      if (box) box.hidden = !box.hidden;
      return;
    }

    const submitCounterBtn = e.target.closest("[data-submit-counter]");
    if (submitCounterBtn) {
      const id = submitCounterBtn.getAttribute("data-submit-counter");
      const cDate = document.getElementById(`counterDate-${id}`)?.value;
      const cTime = document.getElementById(`counterTime-${id}`)?.value;
      await sendInvitePatch(id, { action: "counter", counterDate: cDate, counterStartTime: cTime });
      return;
    }

    const actionBtn = e.target.closest("[data-invite-action]");
    if (actionBtn) {
      const id = actionBtn.getAttribute("data-invite-id");
      const action = actionBtn.getAttribute("data-invite-action");
      await sendInvitePatch(id, { action });
    }
  });

  async function sendInvitePatch(inviteId, payload) {
    try {
      const res = await fetch(`/api/social/invites/${encodeURIComponent(inviteId)}`, {
        method: "PATCH",
        headers: {
          "Content-Type": "application/json",
          "RequestVerificationToken": getCsrfToken()
        },
        body: JSON.stringify(payload)
      });
      const data = await res.json();
      if (!res.ok || !data.ok) {
        showToast(data.message || "Không thể cập nhật lời mời.", true);
        return;
      }
      showToast("Đã cập nhật trạng thái lời mời.", false);
      await refreshInvites();
    } catch {
      showToast("Lỗi kết nối khi phản hồi lời mời.", true);
    }
  }

  openSettingsBtn?.addEventListener("click", openSettingsModal);
  emptyEnableBtn?.addEventListener("click", openSettingsModal);
  closeSettingsBtns.forEach((btn) => btn.addEventListener("click", closeSettingsModal));

  optSkillConfirmed?.addEventListener("change", () => {
    optSkillSelect.disabled = !optSkillConfirmed.checked;
  });

  spritePickerGrid?.addEventListener("click", (e) => {
    const btn = e.target.closest(".sgl-sprite-option");
    if (!btn) return;
    selectedSpriteId = btn.getAttribute("data-sprite-id") || "maya";
    spritePickerGrid.querySelectorAll(".sgl-sprite-option").forEach((el) => {
      el.classList.toggle("is-selected", el === btn);
    });
  });

  freeSlotChips.forEach((chip) => {
    chip.addEventListener("click", () => {
      const slot = chip.getAttribute("data-slot");
      if (!slot) return;
      if (selectedFreeSlots.has(slot)) {
        selectedFreeSlots.delete(slot);
        chip.classList.remove("is-selected");
      } else {
        selectedFreeSlots.add(slot);
        chip.classList.add("is-selected");
      }
    });
  });

  saveSettingsBtn?.addEventListener("click", async () => {
    saveSettingsBtn.disabled = true;
    try {
      const res = await fetch("/api/social/profile", {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
          "RequestVerificationToken": getCsrfToken()
        },
        body: JSON.stringify({
          isVisibleOnCourt: optVisible.checked,
          isReadyToPlay: optReady.checked,
          allowMatchSuggestion: optSuggest.checked,
          spriteId: selectedSpriteId,
          skillConfirmed: optSkillConfirmed.checked,
          confirmedSkillLevel: optSkillSelect.value,
          freeSlots: Array.from(selectedFreeSlots)
        })
      });
      const data = await res.json();
      if (!res.ok || !data.ok) {
        showToast(data.message || "Không thể lưu hồ sơ giao lưu.", true);
        return;
      }
      myProfile = data.profile || myProfile;
      closeSettingsModal();
      showToast("Đã lưu cài đặt Sân giao lưu.", false);
      await refreshCourt();
      if (myProfile.isVisibleOnCourt) {
        selectedAccountId = myProfile.accountId;
        await renderCourtAndDirectory(false);
        courtEngine?.selectMember(String(selectedAccountId));
      }
    } catch {
      showToast("Lỗi kết nối khi lưu cài đặt.", true);
    } finally {
      saveSettingsBtn.disabled = false;
    }
  });

  document.addEventListener("keydown", (e) => {
    if (e.key !== "Escape") return;
    if (settingsModal && !settingsModal.hidden) {
      closeSettingsModal();
      return;
    }
    if (selectedAccountId !== null) {
      selectedAccountId = null;
      courtEngine?.clearSelection();
      renderCourtAndDirectory(false);
    }
  });

  async function init() {
    courtEngine = new window.SanGiaoLuuCourt({
      mapCanvas,
      actorsLayer,
      autoToggle: autoToggleBtn,
      assetBaseUrl: assetBase,
      onSelectMember: (member) => {
        selectedAccountId = member ? Number(member.id) : null;
        renderCourtAndDirectory(false);
      }
    });

    await courtEngine.loadManifest();
    if (players.length > 0) {
      selectedAccountId = players[0].accountId;
    }
    await renderCourtAndDirectory(true);
    await refreshInvites();
  }

  init();
})();
