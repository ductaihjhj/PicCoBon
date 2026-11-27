/* ==========================================================================
   SPIKE OS — DIGITAL QR TICKET PREVIEW CONTROLLER (/qr/)
   Uses dummy demonstration tokens — never grants live venue access.
   ========================================================================== */

(function () {
  function renderQrPreviewPage() {
    if (!HAS_SERVER_STATE) return;
    const raw = SpikeDemo.getRawState();
    const bookings = raw?.bookings || [];
    const host = document.getElementById('qrPreviewTicketHost');
    const sel = document.getElementById('qrSampleSelector');
    if (!host) return;

    const params = new URLSearchParams(window.location.search);
    const reqCode = (params.get('code') || '').trim().toUpperCase();

    if (sel && sel.options.length === 0) {
      sel.innerHTML = bookings.map(b =>
        `<option value="${escapeAttr(b.id)}">${escapeHtml(b.id)} — ${escapeHtml(b.name)} (${escapeHtml(b.courtName || ('Sân #' + b.courtId))} · ${escapeHtml(b.time)})</option>`
      ).join('');
    }

    let selected = reqCode
      ? bookings.find(b => String(b.id).toUpperCase() === reqCode)
      : null;
    if (!selected) {
      selected = bookings.find(b => b.status === 'approved') || bookings[0];
    }
    if (sel && selected) {
      sel.value = selected.id;
    }

    if (selected && window.SpikeTicket) {
      SpikeTicket.mountTicket('qrPreviewTicketHost', selected, {
        accessToken: selected.accessToken || 'bk_demo_preview_only',
        theme: SpikeTicket.currentTheme || 'light'
      });
    }

    const tokenDisplay = document.getElementById('qrDemoTokenText');
    if (tokenDisplay && selected) {
      tokenDisplay.textContent = selected.checkInToken || `SPK1.${selected.id}.DEMO_TOKEN`;
    }
  }

  window.switchPreviewTicket = function (code) {
    const url = new URL(window.location.href);
    url.searchParams.set('code', code);
    window.history.replaceState({}, '', url.toString());
    renderQrPreviewPage();
  };

  document.addEventListener('DOMContentLoaded', renderQrPreviewPage);
  window.addEventListener('pickleball:db-synced', renderQrPreviewPage);
})();
