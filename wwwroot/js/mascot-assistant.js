/* ==========================================================================
   SPIKE OS — MASCOT POSE ANIMATION CONTROLLER & GEMINI AI CHAT WIDGET
   6-Pose PNG Crossfade, Preloader, Idle Loop & Live Assistant Chat UI
   ========================================================================== */

(function () {
  const POSE_ORDER = ["idle", "wave", "thinking", "chat", "idea", "happy"];

  const POSE_CATALOG = {
    idle: {
      key: "idle",
      index: 0,
      label: "01 · Idle (Đứng nghỉ)",
      src: "/assets/assistant/01_idle.png",
    },
    wave: {
      key: "wave",
      index: 1,
      label: "02 · Wave (Vẫy chào)",
      src: "/assets/assistant/02_wave.png",
    },
    thinking: {
      key: "thinking",
      index: 2,
      label: "03 · Thinking (Suy nghĩ)",
      src: "/assets/assistant/03_thinking.png",
    },
    chat: {
      key: "chat",
      index: 3,
      label: "04 · Chat (Trò chuyện)",
      src: "/assets/assistant/04_chat.png",
    },
    idea: {
      key: "idea",
      index: 4,
      label: "05 · Idea (Ý tưởng)",
      src: "/assets/assistant/05_idea.png",
    },
    happy: {
      key: "happy",
      index: 5,
      label: "06 · Happy / Jump (Vui mừng)",
      src: "/assets/assistant/06_jump.png",
    },
  };

  const POSE_ALIASES = {
    idle: "idle",
    default: "idle",
    wave: "wave",
    greeting: "wave",
    thinking: "thinking",
    think: "thinking",
    chat: "chat",
    talk: "chat",
    idea: "idea",
    lightbulb: "idea",
    happy: "happy",
    jump: "happy",
    cheer: "happy",
  };

  const CLICK_REACTION_POOL = ["idea", "happy", "chat"];
  const IDLE_VARIATION_POOL = ["wave", "thinking", "chat"];
  const VERIFIED_SITE_ROUTES = new Set([
    "/",
    "/Booking",
    "/Product",
    "/Lookup",
    "/Account/Profile",
    "/Account/Login",
  ]);

  const DEFAULT_WELCOME_TEXT =
    "Xin chào! 🏓 Mình là Spike Assistant. Mình có thể giúp bạn tìm sân, xem giá, tìm sản phẩm và hướng dẫn sử dụng Spike OS.";

  const DEFAULT_SUGGESTIONS = [
    "Tối nay 19:00 còn sân trống không?",
    "Giá thuê sân 1 giờ tiêu chuẩn và giờ vàng?",
    "Pro Shop đang bán những sản phẩm gì?",
    "Hướng dẫn đặt sân và sử dụng vé QR",
  ];

  class SpikeMascotController {
    constructor(rootEl, options = {}) {
      this.root = rootEl;
      this.options = {
        autoIdleVariation: options.autoIdleVariation !== false,
        sequenceHoldMs: options.sequenceHoldMs || 1300,
        idleFloatEnabled: options.idleFloatEnabled !== false,
        enableChatWidget: options.enableChatWidget === true,
        onPoseChange:
          typeof options.onPoseChange === "function"
            ? options.onPoseChange
            : null,
      };

      this.currentPose = "idle";
      this.activeLayerIndex = 0;
      this.transitionToken = 0;
      this.clickCycleIndex = 0;
      this.speedMultiplier = 1;
      this.isPointerInside = false;
      this.isSequencePlaying = false;
      this.isStopped = false;
      this.preloaded = false;
      this.preloadMap = new Map();

      this.isChatOpen = false;
      this.isChatExpanded = false;
      this.isSuggestionsCollapsed = false;
      this.isRequestInFlight = false;
      this.conversationId = "conv-" + Math.random().toString(36).slice(2, 12);
      this.lastFailedMessage = "";
      this.assistantAvatarUrl =
        (this.root && this.root.getAttribute("data-assistant-avatar")) ||
        POSE_CATALOG.wave.src;

      this.returnToIdleTimer = null;
      this.idleVariationTimer = null;
      this.sequenceTimer = null;
      this.typingPoseTimer = null;

      this.reducedMotionQuery =
        typeof window.matchMedia === "function"
          ? window.matchMedia("(prefers-reduced-motion: reduce)")
          : null;

      this._buildDomIfNeeded();
      if (this.options.enableChatWidget) {
        this._buildChatPanelIfNeeded();
      }
      this._bindEvents();
      this._syncReducedMotion();
      this._syncMobileBottomBarClearance();

      this.preloadAll().then(() => {
        if (!this.isStopped) {
          this._scheduleNextIdleVariation();
        }
      });
    }

    _buildDomIfNeeded() {
      if (!this.root) return;

      this.root.classList.add("spk-mascot-stage");
      if (this.options.idleFloatEnabled) {
        this.root.classList.add("is-idle-floating");
      }

      const badgeHtml = this.options.enableChatWidget
        ? `<span class="spk-mascot-badge" aria-hidden="true">
             <svg class="spk-mascot-badge-icon" viewBox="0 0 16 16" width="13" height="13" fill="none">
               <path d="M8 1.5L9.6 5.6L13.8 7.2L9.6 8.8L8 13L6.4 8.8L2.2 7.2L6.4 5.6L8 1.5Z" fill="currentColor"/>
               <path d="M13.2 1.8L13.7 3.1L15 3.6L13.7 4.1L13.2 5.4L12.7 4.1L11.4 3.6L12.7 3.1L13.2 1.8Z" fill="currentColor" opacity="0.8"/>
             </svg>
             <span>Hỏi Spike AI</span>
           </span>`
        : "";

      let motionEl = this.root.querySelector(".spk-mascot-motion");
      if (!motionEl) {
        this.root.innerHTML = `
          ${badgeHtml}
          <span class="spk-mascot-scale-wrap">
            <span class="spk-mascot-shadow" aria-hidden="true"></span>
            <span class="spk-mascot-motion" data-pose="idle">
              <img class="spk-mascot-layer spk-mascot-layer-a is-active"
                   src="${POSE_CATALOG.idle.src}"
                   alt="Spike OS Mascot"
                   width="512"
                   height="512"
                   decoding="async" />
              <img class="spk-mascot-layer spk-mascot-layer-b"
                   src="${POSE_CATALOG.idle.src}"
                   alt=""
                   aria-hidden="true"
                   width="512"
                   height="512"
                   decoding="async" />
            </span>
          </span>
        `;
      }

      this.motionEl = this.root.querySelector(".spk-mascot-motion");
      this.layers = [
        this.root.querySelector(".spk-mascot-layer-a"),
        this.root.querySelector(".spk-mascot-layer-b"),
      ];
    }

    _buildChatPanelIfNeeded() {
      if (document.getElementById("spikeAssistantPanel")) {
        this.panelEl = document.getElementById("spikeAssistantPanel");
        return;
      }

      const panel = document.createElement("section");
      panel.id = "spikeAssistantPanel";
      panel.className = "spk-ai-panel";
      panel.setAttribute("role", "dialog");
      panel.setAttribute("aria-label", "Trợ lý AI Spike Assistant");
      panel.setAttribute("aria-hidden", "true");

      panel.innerHTML = `
        <header class="spk-ai-header">
          <div class="spk-ai-header-info">
            <span class="spk-ai-header-avatar">
              <img id="spikeAssistantHeaderImg" src="${this.assistantAvatarUrl}" alt="Spike Assistant" width="38" height="38" />
            </span>
            <div class="spk-ai-header-text">
              <span class="spk-ai-header-title">Spike Assistant</span>
              <span class="spk-ai-header-status">
                <span class="spk-ai-status-dot"></span>
                <span>Gemini AI • Dữ liệu Spike OS</span>
              </span>
            </div>
          </div>
          <div class="spk-ai-header-actions">
            <button type="button" class="spk-ai-icon-btn" id="spikeAssistantExpandBtn" title="Mở rộng / Thu nhỏ khung chat" aria-label="Mở rộng hoặc thu nhỏ khung chat" aria-pressed="false">
              <iconify-icon icon="lucide:maximize-2" width="14" id="spikeAssistantExpandIcon"></iconify-icon>
            </button>
            <button type="button" class="spk-ai-icon-btn" id="spikeAssistantResetBtn" title="Làm mới hội thoại" aria-label="Làm mới hội thoại">
              <iconify-icon icon="lucide:rotate-ccw" width="14"></iconify-icon>
            </button>
            <button type="button" class="spk-ai-icon-btn" id="spikeAssistantCloseBtn" title="Đóng khung chat" aria-label="Đóng khung chat">
              <iconify-icon icon="lucide:x" width="16"></iconify-icon>
            </button>
          </div>
        </header>

        <div class="spk-ai-messages" id="spikeAssistantMessages" aria-live="polite"></div>

        <div class="spk-ai-typing" id="spikeAssistantTyping" hidden>
          <span class="spk-ai-typing-dots" aria-hidden="true"><span></span><span></span><span></span></span>
          <span>Spike Assistant đang tra cứu...</span>
        </div>

        <div class="spk-ai-suggestions-drawer" id="spikeAssistantSuggestionsWrap">
          <button type="button"
                  class="spk-ai-suggestions-toggle"
                  id="spikeAssistantSuggestionsToggle"
                  aria-expanded="true"
                  aria-controls="spikeAssistantSuggestions"
                  title="Thu gọn hoặc mở rộng danh sách câu hỏi gợi ý">
            <span class="spk-ai-suggestions-toggle-left">
              <iconify-icon icon="lucide:lightbulb" width="13"></iconify-icon>
              <span>Câu hỏi gợi ý nhanh</span>
            </span>
            <span class="spk-ai-suggestions-toggle-right">
              <span id="spikeAssistantSuggestionsToggleLabel">Thu gọn</span>
              <iconify-icon icon="lucide:chevron-down" width="14" class="spk-ai-suggestions-chevron"></iconify-icon>
            </span>
          </button>
          <div class="spk-ai-suggestions" id="spikeAssistantSuggestions" aria-label="Câu hỏi gợi ý"></div>
        </div>

        <footer class="spk-ai-footer">
          <form class="spk-ai-form" id="spikeAssistantForm" novalidate>
            <input type="text"
                   id="spikeAssistantInput"
                   class="spk-ai-input"
                   maxlength="500"
                   autocomplete="off"
                   placeholder="Hỏi về sân trống, bảng giá, Pro Shop, vé QR..."
                   aria-label="Nhập câu hỏi cho Spike Assistant" />
            <button type="submit" class="spk-ai-send-btn" id="spikeAssistantSendBtn" title="Gửi câu hỏi" aria-label="Gửi câu hỏi">
              <iconify-icon icon="lucide:arrow-up" width="17"></iconify-icon>
            </button>
          </form>
          <div class="spk-ai-privacy">
            Trợ lý AI dùng dữ liệu Spike OS. Không nhập mật khẩu hoặc mã OTP.
          </div>
        </footer>
      `;

      document.body.appendChild(panel);
      this.panelEl = panel;
      this.messagesEl = panel.querySelector("#spikeAssistantMessages");
      this.typingEl = panel.querySelector("#spikeAssistantTyping");
      this.suggestionsWrapEl = panel.querySelector("#spikeAssistantSuggestionsWrap");
      this.suggestionsToggleBtn = panel.querySelector("#spikeAssistantSuggestionsToggle");
      this.suggestionsToggleLabel = panel.querySelector("#spikeAssistantSuggestionsToggleLabel");
      this.suggestionsEl = panel.querySelector("#spikeAssistantSuggestions");
      this.inputEl = panel.querySelector("#spikeAssistantInput");
      this.sendBtnEl = panel.querySelector("#spikeAssistantSendBtn");
      this.headerImgEl = panel.querySelector("#spikeAssistantHeaderImg");
      this.expandBtnEl = panel.querySelector("#spikeAssistantExpandBtn");
      this.expandIconEl = panel.querySelector("#spikeAssistantExpandIcon");

      this._renderWelcomeMessage();
      this._renderSuggestions(DEFAULT_SUGGESTIONS);
    }

    setAssistantAvatar(avatarUrl) {
      const clean = String(avatarUrl || "").trim() || POSE_CATALOG.wave.src;
      this.assistantAvatarUrl = clean;
      if (this.root) {
        this.root.setAttribute("data-assistant-avatar", clean);
      }
      if (this.headerImgEl) {
        this.headerImgEl.src = clean;
      }
      return this;
    }

    setSuggestionsCollapsed(collapsed) {
      this.isSuggestionsCollapsed = Boolean(collapsed);
      if (this.suggestionsWrapEl) {
        this.suggestionsWrapEl.classList.toggle("is-collapsed", this.isSuggestionsCollapsed);
      }
      if (this.suggestionsToggleBtn) {
        this.suggestionsToggleBtn.setAttribute("aria-expanded", this.isSuggestionsCollapsed ? "false" : "true");
      }
      if (this.suggestionsToggleLabel) {
        this.suggestionsToggleLabel.textContent = this.isSuggestionsCollapsed ? "Mở gợi ý" : "Thu gọn";
      }
      return this;
    }

    toggleSuggestionsCollapsed() {
      return this.setSuggestionsCollapsed(!this.isSuggestionsCollapsed);
    }

    toggleChatExpanded() {
      if (!this.panelEl) return this;
      this.isChatExpanded = !this.isChatExpanded;
      this.panelEl.classList.toggle("is-expanded", this.isChatExpanded);
      if (this.expandBtnEl) {
        this.expandBtnEl.setAttribute("aria-pressed", this.isChatExpanded ? "true" : "false");
      }
      if (this.expandIconEl) {
        this.expandIconEl.setAttribute("icon", this.isChatExpanded ? "lucide:minimize-2" : "lucide:maximize-2");
      }
      this._scrollToBottom();
      return this;
    }

    _resolveRouteIcon(url) {
      const u = String(url || "").toLowerCase();
      if (u.startsWith("/lookup")) return "lucide:qr-code";
      if (u.startsWith("/booking")) return "lucide:calendar-plus";
      if (u.startsWith("/product")) return "lucide:shopping-bag";
      if (u.startsWith("/account/profile")) return "lucide:user-check";
      if (u.startsWith("/account/login")) return "lucide:log-in";
      return "lucide:compass";
    }

    _createLinkButton(item) {
      const a = document.createElement("a");
      a.className = "spk-ai-link-pill";
      a.href = item.url;

      const leftIcon = document.createElement("iconify-icon");
      leftIcon.setAttribute("icon", this._resolveRouteIcon(item.url));
      leftIcon.setAttribute("width", "14");
      leftIcon.className = "spk-ai-link-pill-icon";

      const textSpan = document.createElement("span");
      textSpan.textContent = item.label || item.url;

      const rightArrow = document.createElement("iconify-icon");
      rightArrow.setAttribute("icon", "lucide:arrow-up-right");
      rightArrow.setAttribute("width", "13");
      rightArrow.className = "spk-ai-link-pill-arrow";

      a.appendChild(leftIcon);
      a.appendChild(textSpan);
      a.appendChild(rightArrow);
      return a;
    }

    _renderWelcomeMessage() {
      if (!this.messagesEl) return;
      this.messagesEl.innerHTML = "";

      const wrap = document.createElement("div");
      wrap.className = "spk-ai-msg spk-ai-msg--assistant spk-ai-msg--welcome";

      const bubble = document.createElement("div");
      bubble.className = "spk-ai-bubble spk-ai-welcome-card";
      bubble.innerHTML = `
        <div class="spk-ai-welcome-head">
          <span class="spk-ai-welcome-kicker">Trợ lý sân đấu trực tuyến</span>
          <strong class="spk-ai-welcome-title">Xin chào! Mình là Spike Assistant </strong>
        </div>
        <p class="spk-ai-welcome-desc">Mình kết nối trực tiếp với dữ liệu Spike OS để hỗ trợ bạn ngay tức thì:</p>
        <ul class="spk-ai-welcome-list">
          <li><span class="spk-ai-welcome-bullet"></span><span>Tra cứu <strong>sân trống</strong> &amp; báo giá theo khung giờ</span></li>
          <li><span class="spk-ai-welcome-bullet"></span><span>Tìm vợt, bóng, đồ uống tại <strong>Pro Shop</strong></span></li>
          <li><span class="spk-ai-welcome-bullet"></span><span>Hướng dẫn đặt sân, <strong>vé QR</strong> &amp; thanh toán</span></li>
        </ul>
      `;
      wrap.appendChild(bubble);

      const defaultLinks = [
        { label: "Đặt sân trực tuyến", url: "/Booking" },
        { label: "Cửa hàng Pro Shop", url: "/Product" },
        { label: "Tra cứu vé QR", url: "/Lookup" },
      ];
      const linksRow = document.createElement("div");
      linksRow.className = "spk-ai-msg-links";
      defaultLinks.forEach((item) => {
        linksRow.appendChild(this._createLinkButton(item));
      });
      wrap.appendChild(linksRow);

      this.messagesEl.appendChild(wrap);
    }

    _resolveChipIcon(questionText) {
      const lower = String(questionText || "").toLowerCase();
      if (
        lower.includes("giá") ||
        lower.includes("cost") ||
        lower.includes("price") ||
        lower.includes("ưu đãi")
      ) {
        return "lucide:tag";
      }
      if (
        lower.includes("pro shop") ||
        lower.includes("sản phẩm") ||
        lower.includes("đồ uống") ||
        lower.includes("products")
      ) {
        return "lucide:shopping-bag";
      }
      if (
        lower.includes("qr") ||
        lower.includes("hướng dẫn") ||
        lower.includes("paypal")
      ) {
        return "lucide:qr-code";
      }
      if (
        lower.includes("sân") ||
        lower.includes("court") ||
        lower.includes("19:00") ||
        lower.includes("18:00")
      ) {
        return "lucide:calendar-clock";
      }
      return "lucide:sparkles";
    }

    _renderSuggestions(items) {
      if (!this.suggestionsEl) return;
      const list =
        Array.isArray(items) && items.length > 0 ? items : DEFAULT_SUGGESTIONS;
      this.suggestionsEl.innerHTML = "";
      list.slice(0, 4).forEach((q) => {
        const btn = document.createElement("button");
        btn.type = "button";
        btn.className = "spk-ai-chip";

        const icon = document.createElement("iconify-icon");
        icon.setAttribute("icon", this._resolveChipIcon(q));
        icon.setAttribute("width", "13");
        icon.className = "spk-ai-chip-icon";

        const label = document.createElement("span");
        label.textContent = q;

        btn.appendChild(icon);
        btn.appendChild(label);
        btn.addEventListener("click", () => {
          this.sendMessage(q);
        });
        this.suggestionsEl.appendChild(btn);
      });
    }

    _appendUserMessage(text) {
      if (!this.messagesEl) return;
      const wrap = document.createElement("div");
      wrap.className = "spk-ai-msg spk-ai-msg--user";
      const bubble = document.createElement("div");
      bubble.className = "spk-ai-bubble";
      bubble.textContent = text;
      wrap.appendChild(bubble);
      this.messagesEl.appendChild(wrap);
      this._scrollToBottom();
    }

    _appendAssistantMessage(
      text,
      links = [],
      isError = false,
      retryMessage = "",
    ) {
      if (!this.messagesEl) return;
      const wrap = document.createElement("div");
      wrap.className = "spk-ai-msg spk-ai-msg--assistant";

      const bubble = document.createElement("div");
      bubble.className =
        "spk-ai-bubble" + (isError ? " spk-ai-bubble--error" : "");
      bubble.innerHTML = this._formatSafeMessageHtml(text);
      wrap.appendChild(bubble);

      if (Array.isArray(links) && links.length > 0) {
        const linksRow = document.createElement("div");
        linksRow.className = "spk-ai-msg-links";
        links.forEach((item) => {
          if (!item || !item.url || !VERIFIED_SITE_ROUTES.has(item.url)) return;
          linksRow.appendChild(this._createLinkButton(item));
        });
        if (linksRow.childNodes.length > 0) {
          wrap.appendChild(linksRow);
        }
      }

      if (isError && retryMessage) {
        const retryBtn = document.createElement("button");
        retryBtn.type = "button";
        retryBtn.className = "spk-ai-retry-btn";
        retryBtn.textContent = "↻ Thử gửi lại";
        retryBtn.addEventListener("click", () => {
          this.sendMessage(retryMessage);
        });
        wrap.appendChild(retryBtn);
      }

      this.messagesEl.appendChild(wrap);
      this._scrollToBottom();
    }

    _formatSafeMessageHtml(rawText) {
      let safe = String(rawText || "")
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#39;");

      const linkTokens = [];
      const storeLinkToken = (url, label) => {
        if (!VERIFIED_SITE_ROUTES.has(url)) return label;
        const cleanLabel = String(label || url).replace(/\*\*/g, "").trim() || url;
        const token = `__SPK_LINK_${linkTokens.length}__`;
        linkTokens.push(
          `<a href="${url}" class="spk-ai-inline-link"><strong>${cleanLabel}</strong></a>`,
        );
        return token;
      };

      // 1. Convert Markdown links [Label](/VerifiedRoute) or **[Label](/VerifiedRoute)** into clickable bold inline links
      safe = safe.replace(
        /(?:\*\*)?\[([^\]]+?)\]\s*\(\s*(\/[A-Za-z0-9/_-]*)\s*\)(?:\*\*)?/g,
        (full, label, route) => {
          return VERIFIED_SITE_ROUTES.has(route)
            ? storeLinkToken(route, label)
            : full;
        },
      );

      // 2. Convert standalone route paths (/Lookup, /Booking, /Product, /Account/Profile, /Account/Login) into clickable bold links
      const routeLabels = {
        "/Lookup": "Tra cứu vé QR (/Lookup)",
        "/Booking": "Đặt sân trực tuyến (/Booking)",
        "/Product": "Cửa hàng Pro Shop (/Product)",
        "/Account/Profile": "Hồ sơ Hội viên (/Account/Profile)",
        "/Account/Login": "Đăng nhập (/Account/Login)",
      };
      safe = safe.replace(
        /(^|[\s(])(\/(?:Account\/Profile|Account\/Login|Booking|Product|Lookup))(?=$|[\s).,!?:;])/g,
        (full, prefix, route) => {
          if (!VERIFIED_SITE_ROUTES.has(route)) return full;
          return prefix + storeLinkToken(route, routeLabels[route] || route);
        },
      );

      // 3. Convert bold action phrases (**Tra cứu vé**, **Đặt sân**, **Pro Shop**, **Hồ sơ**, **Đăng nhập**) into clickable bold links
      const phraseRouteMap = [
        { regex: /\*\*(tra cứu vé(?:\s+qr)?|trang tra cứu(?:\s+vé)?)\*\*/gi, route: "/Lookup" },
        { regex: /\*\*(đặt sân(?:\s+trực tuyến)?|trang đặt sân)\*\*/gi, route: "/Booking" },
        { regex: /\*\*(?:cửa hàng\s+)?pro\s*shop\*\*/gi, route: "/Product" },
        { regex: /\*\*(hồ sơ(?:\s+hội viên|\s+cá nhân)?)\*\*/gi, route: "/Account/Profile" },
        { regex: /\*\*(đăng nhập(?:\s+tài khoản)?)\*\*/gi, route: "/Account/Login" },
      ];
      phraseRouteMap.forEach(({ regex, route }) => {
        safe = safe.replace(regex, (full) => {
          const inner = full.replace(/^\*\*|\*\*$/g, "");
          return storeLinkToken(route, inner);
        });
      });

      // 4. Standard bold & line breaks
      safe = safe
        .replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>")
        .replace(/\n/g, "<br />");

      // 5. Restore protected link tokens
      linkTokens.forEach((html, idx) => {
        safe = safe.replace(`__SPK_LINK_${idx}__`, html);
      });

      return safe;
    }

    _scrollToBottom() {
      if (!this.messagesEl) return;
      this.messagesEl.scrollTop = this.messagesEl.scrollHeight;
    }

    openChat() {
      if (!this.panelEl) return this;
      this.isChatOpen = true;
      this.panelEl.classList.add("is-open");
      this.panelEl.setAttribute("aria-hidden", "false");
      if (this.root) this.root.classList.add("is-chat-open");
      this.play("wave", { autoReturnMs: 1400 });
      setTimeout(() => {
        if (this.inputEl) this.inputEl.focus();
      }, 120);
      return this;
    }

    closeChat() {
      if (!this.panelEl) return this;
      this.isChatOpen = false;
      this.panelEl.classList.remove("is-open");
      this.panelEl.setAttribute("aria-hidden", "true");
      if (this.root) this.root.classList.remove("is-chat-open");
      this.play("idle");
      return this;
    }

    toggleChat() {
      return this.isChatOpen ? this.closeChat() : this.openChat();
    }

    async resetChat() {
      if (this.isRequestInFlight) return;
      this._renderWelcomeMessage();
      this._renderSuggestions(DEFAULT_SUGGESTIONS);
      this.setSuggestionsCollapsed(false);
      this.play("wave", { autoReturnMs: 1200 });

      try {
        const csrf = this._getCsrfToken();
        const res = await fetch("/api/assistant/reset", {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            RequestVerificationToken: csrf,
          },
          body: JSON.stringify({ conversationId: this.conversationId }),
        });
        const data = await res.json().catch(() => ({}));
        if (data && data.conversationId) {
          this.conversationId = data.conversationId;
        }
      } catch (_) {}
    }

    _getCsrfToken() {
      const meta = document.querySelector('meta[name="csrf-token"]');
      return meta ? meta.getAttribute("content") || "" : "";
    }

    _findGuestAccessToken(messageText) {
      try {
        const codeMatch = String(messageText || "")
          .toUpperCase()
          .match(/\b(BK-[A-Z0-9]{4,12})\b/);
        if (codeMatch) {
          const t = window.sessionStorage.getItem(
            "pb_guest_token_" + codeMatch[1],
          );
          if (t) return { bookingCode: codeMatch[1], guestAccessToken: t };
        }
        for (let i = 0; i < window.sessionStorage.length; i++) {
          const k = window.sessionStorage.key(i);
          if (k && k.startsWith("pb_guest_token_")) {
            const code = k.replace("pb_guest_token_", "");
            const token = window.sessionStorage.getItem(k);
            if (token) return { bookingCode: code, guestAccessToken: token };
          }
        }
      } catch (_) {}
      return { bookingCode: null, guestAccessToken: null };
    }

    async sendMessage(overrideText) {
      if (!this.panelEl || this.isRequestInFlight) return;
      const text =
        typeof overrideText === "string"
          ? overrideText.trim()
          : this.inputEl
            ? this.inputEl.value.trim()
            : "";

      if (!text) return;

      if (!this.isChatOpen) {
        this.openChat();
      }

      if (this.inputEl && typeof overrideText !== "string") {
        this.inputEl.value = "";
      }

      // Collapse suggestion chips automatically when user asks a question so answers have full space
      this.setSuggestionsCollapsed(true);

      this._appendUserMessage(text);
      this.isRequestInFlight = true;
      if (this.sendBtnEl) this.sendBtnEl.disabled = true;
      if (this.typingEl) {
        this.typingEl.hidden = false;
        this.messagesEl.appendChild(this.typingEl);
        this._scrollToBottom();
      }

      // Switch mascot to thinking pose while waiting for Gemini & tool execution
      this.play("thinking");

      try {
        const csrf = this._getCsrfToken();
        const guestInfo = this._findGuestAccessToken(text);
        const res = await fetch("/api/assistant/chat", {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            RequestVerificationToken: csrf,
          },
          body: JSON.stringify({
            message: text,
            conversationId: this.conversationId,
            bookingCode: guestInfo.bookingCode,
            guestAccessToken: guestInfo.guestAccessToken,
          }),
        });

        const data = await res.json().catch(() => ({}));
        if (this.typingEl) this.typingEl.hidden = true;

        if (!res.ok || !data || !data.ok) {
          const errMsg =
            (data && data.error) ||
            "Trợ lý AI tạm thời không phản hồi. Vui lòng thử lại sau ít giây.";
          this.lastFailedMessage = text;
          this.play("thinking", { autoReturnMs: 2200 });
          this._appendAssistantMessage(
            errMsg,
            (data && data.links) || [
              { label: "Đặt sân trực tuyến", url: "/Booking" },
              { label: "Cửa hàng Pro Shop", url: "/Product" },
              { label: "Tra cứu vé QR", url: "/Lookup" },
            ],
            true,
            text,
          );
          return;
        }

        if (data.conversationId) {
          this.conversationId = data.conversationId;
        }

        const responsePose = this.resolvePoseKey(data.pose || "chat");
        this.play(responsePose, { autoReturnMs: 2400 });

        this._appendAssistantMessage(data.reply, data.links || []);
        if (
          Array.isArray(data.suggestedQuestions) &&
          data.suggestedQuestions.length > 0
        ) {
          this._renderSuggestions(data.suggestedQuestions);
        }
      } catch (_) {
        if (this.typingEl) this.typingEl.hidden = true;
        this.play("thinking", { autoReturnMs: 2200 });
        this._appendAssistantMessage(
          "Mất kết nối tạm thời khi gửi câu hỏi. Bạn có thể thử lại hoặc truy cập trực tiếp các trang bên dưới.",
          [
            { label: "Đặt sân trực tuyến", url: "/Booking" },
            { label: "Cửa hàng Pro Shop", url: "/Product" },
            { label: "Tra cứu vé QR", url: "/Lookup" },
          ],
          true,
          text,
        );
      } finally {
        this.isRequestInFlight = false;
        if (this.sendBtnEl) this.sendBtnEl.disabled = false;
      }
    }

    preloadAll() {
      if (this.preloadPromise) return this.preloadPromise;

      const promises = POSE_ORDER.map((key) => {
        const entry = POSE_CATALOG[key];
        return new Promise((resolve) => {
          const img = new Image();
          img.decoding = "async";
          img.onload = () => {
            this.preloadMap.set(key, img);
            resolve({ key, ok: true });
          };
          img.onerror = () => {
            resolve({ key, ok: false });
          };
          img.src = entry.src;
        });
      });

      this.preloadPromise = Promise.all(promises).then((results) => {
        this.preloaded = results.every((r) => r.ok);
        if (this.root) {
          this.root.dataset.preloaded = this.preloaded ? "true" : "partial";
        }
        return results;
      });

      return this.preloadPromise;
    }

    resolvePoseKey(name) {
      if (!name || typeof name !== "string") return "idle";
      const normalized = name.trim().toLowerCase();
      return (
        POSE_ALIASES[normalized] ||
        (POSE_CATALOG[normalized] ? normalized : "idle")
      );
    }

    getPoseInfo(name) {
      const key = this.resolvePoseKey(name || this.currentPose);
      return POSE_CATALOG[key];
    }

    getCatalog() {
      return POSE_ORDER.map((k) => ({ ...POSE_CATALOG[k] }));
    }

    _clearTimers() {
      if (this.returnToIdleTimer) {
        clearTimeout(this.returnToIdleTimer);
        this.returnToIdleTimer = null;
      }
      if (this.idleVariationTimer) {
        clearTimeout(this.idleVariationTimer);
        this.idleVariationTimer = null;
      }
      if (this.sequenceTimer) {
        clearTimeout(this.sequenceTimer);
        this.sequenceTimer = null;
      }
      if (this.typingPoseTimer) {
        clearTimeout(this.typingPoseTimer);
        this.typingPoseTimer = null;
      }
    }

    _transitionToPose(targetKey) {
      if (!this.root || !this.motionEl || !this.layers[0] || !this.layers[1])
        return this;
      const pose = POSE_CATALOG[targetKey] || POSE_CATALOG.idle;

      this.motionEl.setAttribute("data-pose", pose.key);
      this.root.setAttribute("data-current-pose", pose.key);
      if (this.headerImgEl) {
        const isDefaultWaveAvatar =
          !this.assistantAvatarUrl ||
          this.assistantAvatarUrl === POSE_CATALOG.wave.src;
        this.headerImgEl.src = isDefaultWaveAvatar
          ? pose.src
          : this.assistantAvatarUrl;
      }

      const currentActiveLayer = this.layers[this.activeLayerIndex];
      if (
        this.currentPose === pose.key &&
        currentActiveLayer &&
        currentActiveLayer.getAttribute("src") === pose.src &&
        currentActiveLayer.classList.contains("is-active")
      ) {
        this._notifyPoseChange(pose);
        return this;
      }

      this.transitionToken += 1;
      const token = this.transitionToken;
      const nextLayerIndex = this.activeLayerIndex === 0 ? 1 : 0;
      const incomingLayer = this.layers[nextLayerIndex];
      const outgoingLayer = this.layers[this.activeLayerIndex];

      const commitSwap = () => {
        if (token !== this.transitionToken) return;
        incomingLayer.classList.add("is-active");
        outgoingLayer.classList.remove("is-active");
        this.activeLayerIndex = nextLayerIndex;
        this.currentPose = pose.key;
        this._notifyPoseChange(pose);
      };

      if (incomingLayer.getAttribute("src") !== pose.src) {
        incomingLayer.setAttribute("src", pose.src);
      }

      if (incomingLayer.complete && incomingLayer.naturalWidth > 0) {
        commitSwap();
      } else {
        incomingLayer.onload = () => {
          incomingLayer.onload = null;
          commitSwap();
        };
      }

      return this;
    }

    _notifyPoseChange(pose) {
      if (this.options.onPoseChange) {
        try {
          this.options.onPoseChange(pose, this);
        } catch (_) {}
      }
      if (typeof window.CustomEvent === "function") {
        window.dispatchEvent(
          new CustomEvent("spike:mascot-pose", {
            detail: { pose: pose.key, label: pose.label, src: pose.src },
          }),
        );
      }
    }

    play(stateName, options = {}) {
      const targetKey = this.resolvePoseKey(stateName);
      this.isStopped = false;
      if (this.root) this.root.classList.remove("is-paused");

      if (!options.keepSequence && this.isSequencePlaying) {
        this.isSequencePlaying = false;
      }

      if (this.returnToIdleTimer) {
        clearTimeout(this.returnToIdleTimer);
        this.returnToIdleTimer = null;
      }

      this._transitionToPose(targetKey);

      const autoReturnMs =
        typeof options.autoReturnMs === "number" ? options.autoReturnMs : 0;

      if (autoReturnMs > 0 && targetKey !== "idle") {
        const scaledMs = Math.max(
          450,
          Math.round(autoReturnMs / this.speedMultiplier),
        );
        this.returnToIdleTimer = setTimeout(() => {
          this.returnToIdleTimer = null;
          if (
            !this.isStopped &&
            !this.isSequencePlaying &&
            !this.isPointerInside &&
            !this.isRequestInFlight
          ) {
            this._transitionToPose("idle");
            this._scheduleNextIdleVariation();
          }
        }, scaledMs);
      } else if (targetKey === "idle" && !this.isSequencePlaying) {
        this._scheduleNextIdleVariation();
      }

      return this;
    }

    nextPose() {
      const currentIdx = POSE_ORDER.indexOf(this.currentPose);
      const nextIdx = (currentIdx + 1) % POSE_ORDER.length;
      return this.play(POSE_ORDER[nextIdx], {
        keepSequence: this.isSequencePlaying,
      });
    }

    prevPose() {
      const currentIdx = POSE_ORDER.indexOf(this.currentPose);
      const prevIdx = (currentIdx - 1 + POSE_ORDER.length) % POSE_ORDER.length;
      return this.play(POSE_ORDER[prevIdx], {
        keepSequence: this.isSequencePlaying,
      });
    }

    startSequence(holdMs) {
      if (typeof holdMs === "number" && holdMs >= 400) {
        this.options.sequenceHoldMs = holdMs;
      }
      this._clearTimers();
      this.isStopped = false;
      this.isSequencePlaying = true;
      if (this.root) this.root.classList.remove("is-paused");

      const step = () => {
        if (!this.isSequencePlaying || this.isStopped || document.hidden)
          return;
        const interval = Math.max(
          450,
          Math.round(this.options.sequenceHoldMs / this.speedMultiplier),
        );
        this.sequenceTimer = setTimeout(() => {
          if (!this.isSequencePlaying || this.isStopped) return;
          this.nextPose();
          step();
        }, interval);
      };

      step();
      return this;
    }

    stop() {
      this.isStopped = true;
      this.isSequencePlaying = false;
      this._clearTimers();
      if (this.root) {
        this.root.classList.add("is-paused");
      }
      return this;
    }

    setSpeed(multiplier) {
      const num = Number(multiplier);
      if (!Number.isFinite(num) || num <= 0) return this;
      this.speedMultiplier = Math.min(4, Math.max(0.25, num));
      if (this.root) {
        this.root.style.setProperty(
          "--mascot-speed",
          String(this.speedMultiplier),
        );
      }
      if (this.isSequencePlaying) {
        this.startSequence();
      }
      return this;
    }

    setIdleMotion(enabled) {
      this.options.idleFloatEnabled = Boolean(enabled);
      if (this.root) {
        this.root.classList.toggle(
          "is-idle-floating",
          this.options.idleFloatEnabled,
        );
      }
      return this;
    }

    _scheduleNextIdleVariation() {
      if (this.idleVariationTimer) {
        clearTimeout(this.idleVariationTimer);
        this.idleVariationTimer = null;
      }
      if (
        !this.options.autoIdleVariation ||
        this.isStopped ||
        this.isSequencePlaying ||
        this.isRequestInFlight ||
        this.isReadReducedMotion() ||
        document.hidden
      ) {
        return;
      }

      const delayMs = Math.round(
        (11000 + Math.random() * 4000) / this.speedMultiplier,
      );
      this.idleVariationTimer = setTimeout(() => {
        this.idleVariationTimer = null;
        if (
          this.isStopped ||
          this.isSequencePlaying ||
          this.isPointerInside ||
          this.isRequestInFlight ||
          document.hidden ||
          this.currentPose !== "idle"
        ) {
          this._scheduleNextIdleVariation();
          return;
        }
        const pick =
          IDLE_VARIATION_POOL[
            Math.floor(Math.random() * IDLE_VARIATION_POOL.length)
          ];
        this.play(pick, { autoReturnMs: 1850 });
      }, delayMs);
    }

    isReadReducedMotion() {
      return Boolean(
        this.reducedMotionQuery && this.reducedMotionQuery.matches,
      );
    }

    _syncReducedMotion() {
      if (!this.root) return;
      const reduced = this.isReadReducedMotion();
      this.root.classList.toggle("is-reduced-motion", reduced);
    }

    _syncMobileBottomBarClearance() {
      if (!this.root || !this.root.classList.contains("spk-mascot-widget"))
        return;
      const hasBar = Boolean(
        document.querySelector(".sn-mobile-booking-bar, .mobile-shop-cart-bar"),
      );
      this.root.classList.toggle("has-mobile-bottom-bar", hasBar);
      if (this.panelEl) {
        this.panelEl.classList.toggle("has-mobile-bottom-bar", hasBar);
      }
    }

    _bindEvents() {
      if (!this.root) return;

      this.root.addEventListener("pointerenter", () => {
        this.isPointerInside = true;
        if (this.isStopped || this.isSequencePlaying || this.isRequestInFlight)
          return;
        this.play("wave");
      });

      this.root.addEventListener("pointerleave", () => {
        this.isPointerInside = false;
        if (this.isStopped || this.isSequencePlaying || this.isRequestInFlight)
          return;
        this.play("idle");
      });

      this.root.addEventListener("click", (e) => {
        e.preventDefault();
        if (this.options.enableChatWidget) {
          this.toggleChat();
          return;
        }
        if (this.isSequencePlaying) return;
        const nextReaction =
          CLICK_REACTION_POOL[
            this.clickCycleIndex % CLICK_REACTION_POOL.length
          ];
        this.clickCycleIndex += 1;
        this.play(nextReaction, { autoReturnMs: 1550 });
      });

      if (this.panelEl) {
        const closeBtn = this.panelEl.querySelector("#spikeAssistantCloseBtn");
        if (closeBtn) {
          closeBtn.addEventListener("click", () => this.closeChat());
        }

        const expandBtn = this.panelEl.querySelector("#spikeAssistantExpandBtn");
        if (expandBtn) {
          expandBtn.addEventListener("click", () => this.toggleChatExpanded());
        }

        const resetBtn = this.panelEl.querySelector("#spikeAssistantResetBtn");
        if (resetBtn) {
          resetBtn.addEventListener("click", () => this.resetChat());
        }

        if (this.suggestionsToggleBtn) {
          this.suggestionsToggleBtn.addEventListener("click", () =>
            this.toggleSuggestionsCollapsed(),
          );
        }

        const form = this.panelEl.querySelector("#spikeAssistantForm");
        if (form) {
          form.addEventListener("submit", (e) => {
            e.preventDefault();
            this.sendMessage();
          });
        }

        if (this.inputEl) {
          this.inputEl.addEventListener("input", () => {
            if (this.isRequestInFlight || this.isSequencePlaying) return;
            if (
              this.inputEl.value.trim().length > 0 &&
              this.currentPose === "idle"
            ) {
              this.play("chat", { autoReturnMs: 1800 });
            }
          });
        }

        document.addEventListener("keydown", (e) => {
          if (e.key === "Escape" && this.isChatOpen) {
            this.closeChat();
          }
        });
      }

      if (
        this.reducedMotionQuery &&
        typeof this.reducedMotionQuery.addEventListener === "function"
      ) {
        this.reducedMotionQuery.addEventListener("change", () =>
          this._syncReducedMotion(),
        );
      }

      document.addEventListener("visibilitychange", () => {
        if (document.hidden) {
          if (this.root) this.root.classList.add("is-paused");
          if (this.idleVariationTimer) {
            clearTimeout(this.idleVariationTimer);
            this.idleVariationTimer = null;
          }
          if (this.sequenceTimer) {
            clearTimeout(this.sequenceTimer);
            this.sequenceTimer = null;
          }
        } else if (!this.isStopped) {
          if (this.root) this.root.classList.remove("is-paused");
          if (this.isSequencePlaying) {
            this.startSequence();
          } else if (this.currentPose === "idle") {
            this._scheduleNextIdleVariation();
          }
        }
      });
    }
  }

  window.SpikeMascotController = SpikeMascotController;

  function initGlobalMascot() {
    const host = document.getElementById("spikeFloatingMascot");
    if (!host) return;
    const isStandalonePreview = window.location.pathname
      .toLowerCase()
      .includes("mascot-preview");
    const instance = new SpikeMascotController(host, {
      autoIdleVariation: true,
      idleFloatEnabled: true,
      enableChatWidget: !isStandalonePreview,
    });
    window.mascot = instance;
    window.SpikeMascot = instance;
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initGlobalMascot);
  } else {
    initGlobalMascot();
  }
})();
