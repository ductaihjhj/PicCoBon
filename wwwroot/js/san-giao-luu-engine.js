/* ==========================================================================
   SPIKE OS — SÂN GIAO LƯU (SOCIAL PICKLEBALL COURT) ENGINE
   Single-loop 2D Pixel Court + Multi-Character Autonomous Roaming & Matching
   ========================================================================== */
(function (global) {
  "use strict";

  const COURT_SIZE = { width: 640, height: 374 };

  // Walkable zones preventing net, bench, wall, and border clipping
  const ZONES = {
    "left-court": { left: 112, right: 282, top: 120, bottom: 288 },
    "right-court": { left: 356, right: 526, top: 120, bottom: 288 },
    "bench-lounge": { left: 162, right: 432, top: 320, bottom: 356 }
  };

  // Obstacles (net + benches) tested along movement segments
  const OBSTACLES = [
    { left: 304, right: 338, top: 58, bottom: 318 }, // Net & posts
    { left: 82, right: 148, top: 334, bottom: 362 }, // Left bench
    { left: 444, right: 510, top: 334, bottom: 362 } // Right bench
  ];

  const MIN_SEPARATION = 44;
  const MIN_STEP_DISTANCE = 38;
  const MAX_DEST_ATTEMPTS = 12;

  const between = (min, max) => min + Math.random() * (max - min);
  const dist = (a, b) => Math.hypot(a.x - b.x, a.y - b.y);

  function segmentIntersectsRect(a, b, r) {
    // Sample 10 points along segment (fast & exact enough for 640x374 court)
    for (let i = 0; i <= 10; i++) {
      const t = i / 10;
      const x = a.x + (b.x - a.x) * t;
      const y = a.y + (b.y - a.y) * t;
      if (x >= r.left && x <= r.right && y >= r.top && y <= r.bottom) {
        return true;
      }
    }
    return false;
  }

  function drawPixelCourt(ctx) {
    function rect(color, x, y, w, h) {
      ctx.fillStyle = color;
      ctx.fillRect(x, y, w, h);
    }
    function tree(x, y) {
      rect("#1b3c31", x - 6, y + 26, 45, 18);
      rect("#795740", x + 14, y + 19, 8, 20);
      rect("#244d3b", x, y + 6, 38, 23);
      rect("#306245", x + 4, y, 30, 24);
      rect("#3d7650", x + 7, y, 21, 13);
      rect("#518258", x + 11, y + 3, 10, 4);
    }

    // Grass base + subtle pixel blades
    rect("#2b5945", 0, 0, 640, 374);
    for (let y = 0; y < 374; y += 12) {
      for (let x = 0; x < 640; x += 13) {
        const n = (x * 17 + y * 11 + (x / 13) * (y / 12)) % 29;
        if (n < 5) rect(n < 3 ? "#2e6049" : "#335f48", x + 2, y + 2, 3, 2);
      }
    }

    // Back fence
    rect("#50714f", 0, 35, 640, 7);
    rect("#69906a", 0, 34, 640, 2);
    [20, 83, 146, 209, 272, 335, 398, 461, 524, 587, 630].forEach(x => {
      rect("#203d32", x, 25, 4, 25);
      rect("#83a282", x, 24, 5, 3);
    });

    // Outer apron & court surface
    rect("#244438", 64, 54, 514, 283);
    rect("#4b826a", 60, 48, 520, 281);
    rect("#84a98b", 64, 52, 512, 273);
    rect("#56917f", 68, 56, 504, 265);
    rect("#1d4b52", 82, 67, 476, 242);
    rect("#306777", 87, 71, 466, 231);
    rect("#377487", 91, 75, 458, 223);
    // Kitchen (NVZ)
    rect("#447f90", 252, 75, 136, 223);
    for (let y = 80; y < 296; y += 8) rect("#ffffff06", 91, y, 458, 1);

    // Chalk lines
    const chalk = "#e9edcc";
    rect(chalk, 91, 75, 458, 3);
    rect(chalk, 91, 295, 458, 3);
    rect(chalk, 91, 75, 3, 223);
    rect(chalk, 546, 75, 3, 223);
    rect(chalk, 251, 75, 3, 223);
    rect(chalk, 386, 75, 3, 223);
    rect(chalk, 91, 185, 162, 3);
    rect(chalk, 388, 185, 161, 3);

    // Center net & posts
    rect("#203e4988", 323, 65, 13, 243);
    rect("#223c44", 314, 65, 10, 243);
    for (let y = 70; y < 307; y += 6) rect("#527582", 316, y, 7, 1);
    rect("#c6dbcb", 314, 65, 2, 243);
    [63, 307].forEach(y => {
      rect("#203238", 309, y, 17, 8);
      rect("#a0b4a5", 310, y, 14, 2);
    });

    // Waiting benches
    [88, 450].forEach(x => {
      rect("#1b4134", x - 2, 342, 57, 16);
      rect("#795c46", x, 339, 52, 11);
      rect("#b69061", x, 339, 52, 3);
      rect("#bc9a6c", x, 346, 52, 3);
      rect("#443d2c", x + 3, 349, 4, 7);
      rect("#443d2c", x + 44, 349, 4, 7);
    });

    // Trees & grass flowers
    [18, 583].forEach(x => {
      tree(x, 54);
      tree(x + 3, 290);
    });
    rect("#b1c977", 583, 188, 5, 3);
    rect("#a6bf72", 601, 226, 3, 3);
    rect("#b1c977", 36, 152, 3, 4);
  }

  function seqDuration(sequence) {
    return sequence.reduce((sum, f) => sum + f.ms, 0);
  }

  function frameAt(sequence, elapsed) {
    let progress = elapsed % seqDuration(sequence);
    for (const f of sequence) {
      if (progress < f.ms) return f.index;
      progress -= f.ms;
    }
    return sequence[0].index;
  }

  class SanGiaoLuuCourt {
    constructor(options) {
      this.mapCanvas = options.mapCanvas;
      this.actorsLayer = options.actorsLayer;
      this.autoToggle = options.autoToggle;
      this.assetBaseUrl = (options.assetBaseUrl || "/assets/san-giao-luu").replace(/\/$/, "");
      this.onSelectMember = options.onSelectMember || (() => {});
      this.reduceMotion = global.matchMedia("(prefers-reduced-motion: reduce)");
      this.canHover = global.matchMedia("(hover: hover)");

      this.atlasCache = new Map();
      this.customAvatarCache = new Map();
      this.actors = [];
      this.selectedId = null;
      this.highlightedIds = new Set();
      this.rafId = null;
      this.isHidden = document.hidden;

      this.drawBackground();
      this._bindGlobalEvents();
    }

    drawBackground() {
      if (!this.mapCanvas) return;
      const ctx = this.mapCanvas.getContext("2d");
      this.mapCanvas.width = COURT_SIZE.width;
      this.mapCanvas.height = COURT_SIZE.height;
      ctx.imageSmoothingEnabled = false;
      drawPixelCourt(ctx);
    }

    async loadManifest() {
      const res = await fetch(`${this.assetBaseUrl}/manifest.json`);
      if (!res.ok) throw new Error("Không tải được manifest sprite Sân giao lưu.");
      this.manifest = await res.json();
      return this.manifest;
    }

    resolveSpriteId(member) {
      if (member.avatar) {
        const pathOnly = String(member.avatar).split("?")[0].trim();
        if (this.manifest?.presetMap?.[pathOnly]) {
          return this.manifest.presetMap[pathOnly];
        }
        const lowerKey = Object.keys(this.manifest?.presetMap || {}).find(
          k => k.toLowerCase() === pathOnly.toLowerCase()
        );
        if (lowerKey) {
          return this.manifest.presetMap[lowerKey];
        }
      }
      if (member.spriteId && this.manifest?.characters?.[member.spriteId]) {
        return member.spriteId;
      }
      const keys = Object.keys(this.manifest?.characters || { maya: {} });
      let hash = 0;
      const seed = String(member.id || member.displayName || "spike");
      for (let i = 0; i < seed.length; i++) hash = ((hash << 5) - hash + seed.charCodeAt(i)) | 0;
      return keys[Math.abs(hash) % keys.length] || "maya";
    }

    loadCustomPixelAvatar(avatarUrl) {
      if (!avatarUrl) return Promise.resolve(null);
      if (this.customAvatarCache.has(avatarUrl)) return this.customAvatarCache.get(avatarUrl);
      const promise = new Promise(resolve => {
        const img = new Image();
        img.crossOrigin = "anonymous";
        img.onload = () => {
          try {
            const tiny = document.createElement("canvas");
            tiny.width = 14;
            tiny.height = 14;
            const tCtx = tiny.getContext("2d");
            tCtx.imageSmoothingEnabled = true;
            const side = Math.min(img.naturalWidth || img.width, img.naturalHeight || img.height);
            const sx = Math.max(0, Math.floor(((img.naturalWidth || img.width) - side) / 2));
            const sy = Math.max(0, Math.floor(((img.naturalHeight || img.height) - side) / 2));
            tCtx.drawImage(img, sx, sy, side, side, 0, 0, 14, 14);

            const out = document.createElement("canvas");
            out.width = 32;
            out.height = 32;
            const oCtx = out.getContext("2d");
            oCtx.imageSmoothingEnabled = false;
            oCtx.fillStyle = "#17251d";
            oCtx.fillRect(0, 0, 32, 32);
            oCtx.fillStyle = "#eaf7ee";
            oCtx.fillRect(2, 2, 28, 28);
            oCtx.drawImage(tiny, 0, 0, 14, 14, 2, 2, 28, 28);
            resolve(out);
          } catch {
            resolve(null);
          }
        };
        img.onerror = () => resolve(null);
        img.src = avatarUrl;
      });
      this.customAvatarCache.set(avatarUrl, promise);
      return promise;
    }

    _buildBlinkCanvas(img, meta) {
      try {
        const { width, height } = meta;
        const c = document.createElement("canvas");
        c.width = width;
        c.height = height;
        const ctx = c.getContext("2d");
        ctx.imageSmoothingEnabled = false;
        ctx.drawImage(img, 0, 0, width, height, 0, 0, width, height);
        const imgData = ctx.getImageData(0, 0, width, height);
        const d = imgData.data;

        const xMin = Math.floor(width * 0.34);
        const xMax = Math.ceil(width * 0.66);
        const yMin = Math.floor(height * 0.23);
        const yMax = Math.ceil(height * 0.43);

        // Sample dominant light skin/fur tone in the eye band
        let skinR = 242, skinG = 201, skinB = 166, skinCount = 0;
        let sumR = 0, sumG = 0, sumB = 0;
        for (let y = yMin; y <= yMax; y++) {
          for (let x = xMin; x <= xMax; x++) {
            const idx = (y * width + x) * 4;
            const r = d[idx], g = d[idx + 1], b = d[idx + 2], a = d[idx + 3];
            if (a > 220 && r > 155 && g > 115 && !(r > 240 && g > 240 && b > 240)) {
              sumR += r; sumG += g; sumB += b; skinCount++;
            }
          }
        }
        if (skinCount > 0) {
          skinR = Math.round(sumR / skinCount);
          skinG = Math.round(sumG / skinCount);
          skinB = Math.round(sumB / skinCount);
        }

        // Find dark eye-interior pixels (surrounded closely by skin/white, not outer border)
        const eyeRows = new Map();
        for (let y = yMin + 1; y < yMax - 1; y++) {
          for (let x = xMin + 1; x < xMax - 1; x++) {
            const idx = (y * width + x) * 4;
            const r = d[idx], g = d[idx + 1], b = d[idx + 2], a = d[idx + 3];
            const isDark = a > 220 && r < 72 && g < 72 && b < 72;
            const isEyeWhite = a > 220 && r > 230 && g > 230 && b > 230;
            if (!isDark && !isEyeWhite) continue;

            // Check that at least one neighbor within 2px is skin/fur tone (avoids hair/hat outline)
            let hasSkinNeighbor = false;
            for (let dy = -2; dy <= 2 && !hasSkinNeighbor; dy++) {
              for (let dx = -2; dx <= 2; dx++) {
                const nIdx = ((y + dy) * width + (x + dx)) * 4;
                const nr = d[nIdx], ng = d[nIdx + 1], nb = d[nIdx + 2], na = d[nIdx + 3];
                if (na > 220 && nr > 150 && ng > 110 && !(nr > 240 && ng > 240 && nb > 240)) {
                  hasSkinNeighbor = true;
                  break;
                }
              }
            }
            if (!hasSkinNeighbor) continue;

            if (!eyeRows.has(x)) eyeRows.set(x, []);
            eyeRows.get(x).push(y);
          }
        }

        for (const [x, ys] of eyeRows.entries()) {
          if (ys.length < 2 || ys.length > 7) continue;
          const bottomY = Math.max(...ys);
          for (const y of ys) {
            const idx = (y * width + x) * 4;
            if (y === bottomY) {
              // Crisp 1px closed-eyelid line
              d[idx] = 28; d[idx + 1] = 24; d[idx + 2] = 22; d[idx + 3] = 255;
            } else {
              d[idx] = skinR; d[idx + 1] = skinG; d[idx + 2] = skinB; d[idx + 3] = 255;
            }
          }
        }

        ctx.putImageData(imgData, 0, 0);
        return c;
      } catch {
        return null;
      }
    }

    loadSpriteAtlas(spriteId) {
      if (this.atlasCache.has(spriteId)) return this.atlasCache.get(spriteId);
      const entry = this.manifest.characters[spriteId] || this.manifest.characters.maya;
      const promise = new Promise((resolve, reject) => {
        const img = new Image();
        img.onload = () => {
          const blinkCanvas = this._buildBlinkCanvas(img, entry.animation);
          resolve({ img, blinkCanvas, meta: entry.animation, entry });
        };
        img.onerror = () => reject(new Error(`Không tải được atlas ${spriteId}`));
        img.src = entry.atlas.startsWith("/") && this.assetBaseUrl.startsWith(".")
          ? `${this.assetBaseUrl}/${spriteId}-atlas.png`
          : entry.atlas;
      });
      this.atlasCache.set(spriteId, promise);
      return promise;
    }

    async setMembers(members) {
      this.stopLoop();
      this.actorsLayer.innerHTML = "";
      this.actors = [];

      const zoneNames = ["left-court", "right-court", "bench-lounge"];
      const now = performance.now();

      for (let i = 0; i < members.length; i++) {
        const m = members[i];
        const spriteId = this.resolveSpriteId(m);
        const zoneKey = m.zone && ZONES[m.zone] ? m.zone : zoneNames[i % zoneNames.length];
        const zone = ZONES[zoneKey];

        // Pick non-overlapping initial position inside zone
        let pos = { x: (zone.left + zone.right) / 2, y: (zone.top + zone.bottom) / 2 };
        for (let attempt = 0; attempt < 20; attempt++) {
          const cand = { x: between(zone.left, zone.right), y: between(zone.top, zone.bottom) };
          const overlaps = this.actors.some(other => dist(other.pos, cand) < MIN_SEPARATION);
          if (!overlaps) {
            pos = cand;
            break;
          }
        }

        const btn = document.createElement("button");
        btn.type = "button";
        btn.className = "sgl-actor";
        btn.dataset.memberId = String(m.id);
        btn.setAttribute(
          "aria-label",
          `Xem hồ sơ ${m.displayName} — ${m.readyToPlay ? "Sẵn sàng giao lưu" : "Hội viên Spike"}`
        );
        btn.setAttribute("aria-expanded", "false");
        btn.style.left = `${(100 * pos.x) / COURT_SIZE.width}%`;
        btn.style.top = `${(100 * pos.y) / COURT_SIZE.height}%`;
        btn.style.zIndex = String(Math.round(pos.y));

        const plate = document.createElement("span");
        plate.className = "sgl-nameplate";
        if (m.readyToPlay) {
          const dot = document.createElement("span");
          dot.className = "sgl-dot";
          dot.setAttribute("title", "Sẵn sàng giao lưu");
          plate.appendChild(dot);
        }
        const nameSpan = document.createElement("span");
        nameSpan.textContent = m.shortName || m.displayName;
        plate.appendChild(nameSpan);

        const canvas = document.createElement("canvas");
        canvas.className = "sgl-sprite";
        canvas.setAttribute("aria-hidden", "true");

        btn.appendChild(plate);
        btn.appendChild(canvas);
        this.actorsLayer.appendChild(btn);

        const actor = {
          id: String(m.id),
          member: m,
          spriteId,
          zoneKey,
          btn,
          canvas,
          ctx: canvas.getContext("2d"),
          pos,
          action: "idle",
          started: now,
          facing: pos.x < COURT_SIZE.width / 2 ? 1 : -1,
          walk: null,
          paintedFrame: -1,
          paintedFacing: 0,
          paintedBlink: false,
          nextAutomatic: now + between(700, 2600) + i * 280,
          nextBlink: now + between(1800, 4400) + i * 240,
          blinkUntil: 0,
          doubleBlinkQueued: false,
          hopUntil: 0,
          lastAutomatic: "idle",
          speedPxPerMs: between(0.048, 0.068),
          atlasData: null
        };

        btn.addEventListener("pointerenter", e => {
          if (e.pointerType === "mouse" && !this.reduceMotion.matches) {
            const t = performance.now();
            actor.blinkUntil = t + 140;
            if (actor.action === "walk") {
              this._setActorAction(actor, "idle", t);
            } else if (actor.action === "idle" && Math.random() < 0.55) {
              this._setActorAction(actor, "wave", t);
              actor.nextAutomatic = t + 1800;
            }
          }
        });
        btn.addEventListener("focus", () => {
          const t = performance.now();
          if (!this.reduceMotion.matches) {
            actor.blinkUntil = t + 140;
          }
          if (actor.action === "walk") {
            this._setActorAction(actor, "idle", t);
          }
        });
        btn.addEventListener("click", e => {
          this.selectMember(actor.id, e.currentTarget);
        });

        this.actors.push(actor);
        const isCustomUpload = m.avatarMode === "custom-upload" && Boolean(m.avatar);
        Promise.all([
          this.loadSpriteAtlas(spriteId),
          isCustomUpload ? this.loadCustomPixelAvatar(m.avatar) : Promise.resolve(null)
        ]).then(([data, customPixelCanvas]) => {
          actor.atlasData = data;
          actor.customPixelCanvas = customPixelCanvas;
          actor.canvas.width = data.meta.width;
          actor.canvas.height = data.meta.height;
          actor.ctx.imageSmoothingEnabled = false;
          this._paintActor(actor, performance.now());
        });
      }

      if (this.autoToggle) {
        this.autoToggle.setAttribute("aria-pressed", String(!this.reduceMotion.matches));
        this.autoToggle.hidden = this.reduceMotion.matches;
      }

      this.startLoop();
    }

    selectMember(memberId, openerEl = null) {
      this.selectedId = memberId ? String(memberId) : null;
      const now = performance.now();
      for (const actor of this.actors) {
        const isSel = actor.id === this.selectedId;
        actor.btn.setAttribute("aria-expanded", String(isSel));
        actor.btn.classList.toggle("is-selected", isSel);
        if (isSel) {
          this._setActorAction(actor, "idle", now);
        }
      }
      const found = this.actors.find(a => a.id === this.selectedId);
      if (found) {
        this.onSelectMember(found.member, openerEl || found.btn);
      }
    }

    clearSelection() {
      this.selectedId = null;
      const now = performance.now();
      for (const actor of this.actors) {
        actor.btn.setAttribute("aria-expanded", "false");
        actor.btn.classList.remove("is-selected");
        actor.nextAutomatic = Math.max(actor.nextAutomatic, now + between(1200, 2200));
      }
    }

    highlightMembers(ids) {
      this.highlightedIds = new Set((ids || []).map(String));
      const now = performance.now();
      for (const actor of this.actors) {
        const isHl = this.highlightedIds.has(actor.id);
        actor.btn.classList.toggle("is-highlighted", isHl);
        if (isHl && !this.reduceMotion.matches) {
          this._setActorAction(actor, "wave", now);
        }
      }
    }

    triggerSelectedAction(actionName) {
      const target = this.actors.find(a => a.id === this.selectedId) || this.actors[0];
      if (!target) return;
      const now = performance.now();
      this._setActorAction(target, actionName, now);
      target.nextAutomatic = now + 6000;
    }

    _chooseDestination(actor) {
      const zoneNames = ["left-court", "right-court", "bench-lounge"];
      if (Math.random() < 0.25) {
        actor.zoneKey = zoneNames[Math.floor(Math.random() * zoneNames.length)];
      }
      const zone = ZONES[actor.zoneKey];
      for (let attempt = 0; attempt < MAX_DEST_ATTEMPTS; attempt++) {
        const cand = { x: between(zone.left, zone.right), y: between(zone.top, zone.bottom) };
        if (dist(actor.pos, cand) < MIN_STEP_DISTANCE) continue;
        if (OBSTACLES.some(obs => segmentIntersectsRect(actor.pos, cand, obs))) continue;

        const occupied = this.actors.some(other => {
          if (other.id === actor.id) return false;
          const otherTarget = other.action === "walk" && other.walk ? other.walk.to : other.pos;
          return dist(otherTarget, cand) < MIN_SEPARATION || dist(other.pos, cand) < MIN_SEPARATION * 0.85;
        });
        if (!occupied) return cand;
      }
      return null;
    }

    _setActorAction(actor, name, now) {
      if (name === "look") {
        actor.facing = actor.facing > 0 ? -1 : 1;
        actor.action = "idle";
        actor.started = now;
        actor.blinkUntil = now + 135;
        actor.nextAutomatic = now + between(900, 1700);
        return;
      }
      if (name === "hop") {
        actor.hopUntil = now + 380;
        actor.action = "idle";
        actor.started = now;
        actor.nextAutomatic = now + between(1000, 1900);
        return;
      }
      if (name === "walk") {
        const to = this._chooseDestination(actor);
        if (!to) {
          actor.action = "idle";
          actor.started = now;
          actor.nextAutomatic = now + between(900, 1700);
          return;
        }
        const d = dist(actor.pos, to);
        actor.walk = {
          from: { ...actor.pos },
          to,
          ms: Math.max(760, d / actor.speedPxPerMs)
        };
        actor.facing = to.x >= actor.pos.x ? 1 : -1;
      }
      actor.action = name;
      actor.started = now;
    }

    _paintActor(actor, now) {
      if (!actor.atlasData) return;
      const { img, blinkCanvas, meta } = actor.atlasData;
      const seq = meta.actions[actor.action] || meta.actions.idle;
      const elapsed = Math.max(0, now - actor.started);
      const frame = this.reduceMotion.matches
        ? seq[actor.action === "wave" || actor.action === "swing" ? 1 : 0].index
        : frameAt(seq, elapsed);
      const dir = actor.facing || 1;
      const isBlinking = !this.reduceMotion.matches && now < actor.blinkUntil && (frame === 0 || frame === 1) && Boolean(blinkCanvas);

      if (actor.hopUntil > now && !this.reduceMotion.matches) {
        const p = 1 - (actor.hopUntil - now) / 380;
        const hopOffset = Math.round(Math.sin(p * Math.PI) * 6);
        actor.canvas.style.transform = `translateY(${-hopOffset}px)`;
      } else if (actor.canvas.style.transform) {
        actor.canvas.style.transform = "";
      }

      if (frame !== actor.paintedFrame || dir !== actor.paintedFacing || isBlinking !== actor.paintedBlink) {
        actor.paintedFrame = frame;
        actor.paintedFacing = dir;
        actor.paintedBlink = isBlinking;
        const { width, height } = meta;
        actor.ctx.setTransform(1, 0, 0, 1, 0, 0);
        actor.ctx.clearRect(0, 0, width, height);
        if (dir < 0) actor.ctx.setTransform(-1, 0, 0, 1, width, 0);
        if (isBlinking) {
          const yShift = frame === 1 ? 1 : 0;
          actor.ctx.drawImage(blinkCanvas, 0, 0, width, height, 0, yShift, width, height);
        } else {
          actor.ctx.drawImage(img, frame * width, 0, width, height, 0, 0, width, height);
        }
        if (actor.customPixelCanvas) {
          actor.ctx.setTransform(1, 0, 0, 1, 0, 0);
          const bobY = frame === 1 || frame === 3 || frame === 5 ? 1 : 0;
          const headX = Math.round((width - actor.customPixelCanvas.width) / 2);
          const headY = 12 + bobY;
          actor.ctx.drawImage(actor.customPixelCanvas, headX, headY);
        }
      }
    }

    _pickNextAutonomousAction(actor) {
      const hasNeighbor = this.actors.some(other => other.id !== actor.id && dist(other.pos, actor.pos) < 130);
      const roll = Math.random();
      if (actor.lastAutomatic === "walk") {
        if (hasNeighbor && roll < 0.38) return "wave";
        if (roll < 0.56) return "swing";
        if (roll < 0.78) return "look";
        return "hop";
      }
      if (roll < 0.62) return "walk";
      if (roll < 0.78) return "wave";
      if (roll < 0.90) return "swing";
      return "look";
    }

    _tick = now => {
      if (this.isHidden) {
        this.rafId = null;
        return;
      }

      const autoOn = !this.autoToggle || this.autoToggle.getAttribute("aria-pressed") === "true";

      for (const actor of this.actors) {
        const isPausedByUser =
          actor.id === this.selectedId ||
          actor.btn.matches(":focus-visible") ||
          (this.canHover.matches && actor.btn.matches(":hover"));

        if (!this.reduceMotion.matches && now >= actor.nextBlink) {
          actor.blinkUntil = now + between(115, 155);
          if (!actor.doubleBlinkQueued && Math.random() < 0.24) {
            actor.doubleBlinkQueued = true;
            actor.nextBlink = now + between(240, 320);
          } else {
            actor.doubleBlinkQueued = false;
            actor.nextBlink = now + between(2400, 5200);
          }
        }

        if (actor.action === "idle" && autoOn && !this.reduceMotion.matches && !isPausedByUser && now >= actor.nextAutomatic) {
          const nextAct = this._pickNextAutonomousAction(actor);
          actor.lastAutomatic = nextAct;
          this._setActorAction(actor, nextAct, now);

          const meta = actor.atlasData?.meta;
          const actMs = actor.action === "walk" && actor.walk
            ? actor.walk.ms
            : (meta ? seqDuration(meta.actions[actor.action] || meta.actions.idle) : 900);
          if (actor.action !== "idle") {
            actor.nextAutomatic = now + actMs + between(1100, 2900);
          }
        }

        const elapsed = now - actor.started;
        if (actor.action === "walk" && actor.walk) {
          const progress = Math.min(1, elapsed / actor.walk.ms);
          if (!this.reduceMotion.matches) {
            actor.pos.x = actor.walk.from.x + (actor.walk.to.x - actor.walk.from.x) * progress;
            actor.pos.y = actor.walk.from.y + (actor.walk.to.y - actor.walk.from.y) * progress;
            actor.btn.style.left = `${(100 * actor.pos.x) / COURT_SIZE.width}%`;
            actor.btn.style.top = `${(100 * actor.pos.y) / COURT_SIZE.height}%`;
            actor.btn.style.zIndex = String(Math.round(actor.pos.y));
          }
          if (progress >= 1) {
            this._setActorAction(actor, "idle", now);
          }
        } else if (actor.action !== "idle" && actor.atlasData) {
          const seq = actor.atlasData.meta.actions[actor.action];
          if (seq && elapsed >= seqDuration(seq)) {
            this._setActorAction(actor, "idle", now);
          }
        }

        this._paintActor(actor, now);
      }

      this.rafId = requestAnimationFrame(this._tick);
    };

    startLoop() {
      if (this.rafId == null && !this.isHidden) {
        this.rafId = requestAnimationFrame(this._tick);
      }
    }

    stopLoop() {
      if (this.rafId != null) {
        cancelAnimationFrame(this.rafId);
        this.rafId = null;
      }
    }

    _bindGlobalEvents() {
      document.addEventListener("visibilitychange", () => {
        this.isHidden = document.hidden;
        if (this.isHidden) {
          this.stopLoop();
        } else {
          const now = performance.now();
          for (const actor of this.actors) {
            actor.started = now;
            if (actor.action === "walk") this._setActorAction(actor, "idle", now);
            actor.nextAutomatic = now + between(900, 2600);
          }
          this.startLoop();
        }
      });

      if (this.autoToggle) {
        this.autoToggle.addEventListener("click", () => {
          const enabled = this.autoToggle.getAttribute("aria-pressed") === "true";
          this.autoToggle.setAttribute("aria-pressed", String(!enabled));
          const now = performance.now();
          for (const actor of this.actors) {
            if (enabled) this._setActorAction(actor, "idle", now);
            actor.nextAutomatic = now + between(900, 2200);
          }
        });
      }

      this.reduceMotion.addEventListener("change", () => {
        if (this.autoToggle) {
          this.autoToggle.hidden = this.reduceMotion.matches;
          this.autoToggle.setAttribute("aria-pressed", String(!this.reduceMotion.matches));
        }
        const now = performance.now();
        for (const actor of this.actors) {
          this._setActorAction(actor, "idle", now);
          actor.nextAutomatic = now + between(1600, 3200);
        }
      });
    }
  }

  global.SanGiaoLuuCourt = SanGiaoLuuCourt;
})(window);
