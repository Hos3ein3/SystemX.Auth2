// Small behavior for the server-rendered pages. No framework. Every feature also works without JavaScript where it can
// (the confirm dialog only adds a safety question, auto-submit has a submit button next to it).
(() => {
  'use strict';
  try {
    if (localStorage.theme === 'dark' || (!('theme' in localStorage) && window.matchMedia('(prefers-color-scheme: dark)').matches)) {
      document.documentElement.classList.add('dark');
    } else {
      document.documentElement.classList.remove('dark');
    }
  } catch {}

  const ask = (text) => window.confirm(text);

  document.addEventListener('submit', (e) => {
    const el = e.target instanceof HTMLFormElement ? e.target : null;
    const text = el?.dataset.confirm ?? e.submitter?.dataset.confirm;
    if (text && !ask(text)) e.preventDefault();
  });

  document.addEventListener('change', (e) => {
    const el = e.target;
    if (el instanceof HTMLElement && el.hasAttribute('data-autosubmit') && el.closest('form')) el.closest('form').requestSubmit();
    if (el instanceof HTMLSelectElement && el.hasAttribute('data-culture-select')) window.setCulture(el.value);
  });

  document.addEventListener('change', (e) => {
    const all = e.target instanceof HTMLInputElement ? e.target.getAttribute('data-select-all') : null;
    if (all) document.querySelectorAll(all).forEach((c) => { c.checked = e.target.checked; });
  });

  // A form that must go to the provider at once (for example the SAML POST binding): it posts itself.
  document.addEventListener('DOMContentLoaded', () => document.querySelector('form[data-autopost]')?.requestSubmit());

  document.addEventListener('click', async (e) => {
    const t = e.target instanceof Element ? e.target.closest('[data-copy]') : null;
    if (t) {
      const source = t.closest('div')?.querySelector(t.getAttribute('data-copy') || 'pre');
      if (source) { try { await navigator.clipboard.writeText(source.textContent ?? ''); t.textContent = 'Copied'; } catch { /* the text can be selected by hand */ } }
    }
    const toggle = e.target instanceof Element ? e.target.closest('[data-toggle-password]') : null;
    if (toggle) {
      const input = document.getElementById(toggle.getAttribute('data-toggle-password'));
      if (input instanceof HTMLInputElement) input.type = input.type === 'password' ? 'text' : 'password';
    }
    const themeBtn = e.target instanceof Element ? e.target.closest('[data-toggle-theme]') : null;
    if (themeBtn) {
      window.toggleDarkMode();
    }
    const lockOpen = e.target instanceof Element ? e.target.closest('[data-lock-modal-open]') : null;
    if (lockOpen) {
      const modal = document.getElementById('lockModal');
      const id = lockOpen.getAttribute('data-user-id');
      const name = lockOpen.getAttribute('data-user-name');
      const idInput = document.getElementById('lockUserId');
      const nameSpan = document.getElementById('lockTargetName');
      const untilInput = document.getElementById('lockUntilInput');
      if (idInput) idInput.value = id || '';
      if (nameSpan) nameSpan.textContent = name || '';
      if (untilInput) untilInput.value = '';
      if (modal) modal.classList.remove('hidden');
    }
    const lockClose = e.target instanceof Element ? e.target.closest('[data-lock-modal-close]') : null;
    if (lockClose) {
      const modal = document.getElementById('lockModal');
      if (modal) modal.classList.add('hidden');
    }
    const lockDur = e.target instanceof Element ? e.target.closest('[data-lock-duration]') : null;
    if (lockDur) {
      const minutes = parseInt(lockDur.getAttribute('data-lock-duration') || '0', 10);
      const untilInput = document.getElementById('lockUntilInput');
      if (untilInput && minutes > 0) {
        const d = new Date(Date.now() + minutes * 60000);
        d.setMinutes(d.getMinutes() - d.getTimezoneOffset());
        untilInput.value = d.toISOString().slice(0, 16);
      }
    }
  });

  // Multi-select search filtering
  document.addEventListener('input', (e) => {
    const input = e.target instanceof HTMLInputElement ? e.target : null;
    const targetListId = input?.getAttribute('data-multiselect-search');
    if (targetListId) {
      const query = (input.value || '').toLowerCase().trim();
      const container = document.getElementById(targetListId);
      if (container) {
        container.querySelectorAll('[data-search]').forEach((item) => {
          const val = (item.getAttribute('data-search') || '').toLowerCase();
          item.classList.toggle('hidden', Boolean(query && !val.includes(query)));
        });
      }
    }
  });

  // Multi-select counter badge
  document.addEventListener('change', (e) => {
    const cb = e.target instanceof HTMLInputElement && e.target.type === 'checkbox' ? e.target : null;
    if (cb) {
      const container = cb.closest('[data-multiselect-list]');
      if (container) {
        const countId = container.getAttribute('data-count-target');
        const countEl = countId ? document.getElementById(countId) : null;
        if (countEl) {
          countEl.textContent = container.querySelectorAll('input[type="checkbox"]:checked').length;
        }
      }
    }
  });

  // Navigation search filtering
  document.addEventListener('input', (e) => {
    const input = e.target instanceof HTMLInputElement ? e.target : null;
    if (input && input.hasAttribute('data-nav-search')) {
      const query = (input.value || '').toLowerCase().trim();
      const nav = document.querySelector('[data-nav-list]');
      if (nav) {
        nav.querySelectorAll('a').forEach((link) => {
          const text = (link.textContent || '').toLowerCase();
          const matches = !query || text.includes(query);
          link.classList.toggle('hidden', !matches);
        });
        // Check section containers
        nav.querySelectorAll('div.pt-2, div.pt-4').forEach((sec) => {
          const links = sec.querySelectorAll('a');
          if (links.length > 0) {
            const anyVisible = Array.from(links).some((l) => !l.classList.contains('hidden'));
            sec.classList.toggle('hidden', !anyVisible);
          }
        });
      }
    }
  });

  // Select / Dropdown live filter
  document.addEventListener('input', (e) => {
    const input = e.target instanceof HTMLInputElement ? e.target : null;
    const selectId = input?.getAttribute('data-filter-select');
    if (selectId) {
      const query = (input.value || '').toLowerCase().trim();
      const select = document.getElementById(selectId);
      if (select instanceof HTMLSelectElement) {
        let firstMatch = null;
        for (let i = 0; i < select.options.length; i++) {
          const opt = select.options[i];
          if (!opt.value) continue; // Keep header/empty option visible
          const text = (opt.textContent || '').toLowerCase();
          const val = (opt.value || '').toLowerCase();
          const matches = !query || text.includes(query) || val.includes(query);
          opt.hidden = !matches;
          if (matches && !firstMatch) firstMatch = opt;
        }
        if (firstMatch && select.selectedOptions[0]?.hidden) {
          select.value = firstMatch.value;
        }
      }
    }
  });

  // Assignment remove modal
  let activeAssignmentForm = null;
  document.addEventListener('click', (e) => {
    const trigger = e.target instanceof Element ? e.target.closest('[data-assignment-remove-trigger]') : null;
    if (trigger) {
      const form = trigger.closest('form');
      if (form) {
        activeAssignmentForm = form;
        const msg = trigger.getAttribute('data-message') || 'Are you sure you want to remove this assignment?';
        const title = trigger.getAttribute('data-title') || 'Confirm Revocation';
        const msgEl = document.getElementById('removeModalMessage');
        const titleEl = document.getElementById('removeModalTitle');
        if (msgEl) msgEl.textContent = msg;
        if (titleEl) titleEl.textContent = title;
        const modal = document.getElementById('assignmentRemoveModal');
        if (modal) modal.classList.remove('hidden');
      }
    }
    const closeBtn = e.target instanceof Element ? e.target.closest('[data-assignment-modal-close]') : null;
    if (closeBtn) {
      const modal = document.getElementById('assignmentRemoveModal');
      if (modal) modal.classList.add('hidden');
      activeAssignmentForm = null;
    }
    const confirmBtn = e.target instanceof Element ? e.target.closest('#assignmentModalConfirmBtn') : null;
    if (confirmBtn) {
      const modal = document.getElementById('assignmentRemoveModal');
      if (modal) modal.classList.add('hidden');
      if (activeAssignmentForm) {
        activeAssignmentForm.submit();
        activeAssignmentForm = null;
      }
    }
  });

  // Generic table search filtering
  document.addEventListener('input', (e) => {
    const input = e.target instanceof HTMLInputElement ? e.target : null;
    const tableId = input?.getAttribute('data-table-search');
    if (tableId) {
      const query = (input.value || '').toLowerCase().trim();
      const table = document.getElementById(tableId);
      if (table) {
        table.querySelectorAll('tbody tr').forEach((row) => {
          const text = (row.textContent || '').toLowerCase();
          row.classList.toggle('hidden', Boolean(query && !text.includes(query)));
        });
      }
    }
  });

  // Toast preview trigger
  document.addEventListener('click', (e) => {
    const btn = e.target instanceof Element ? e.target.closest('[data-show-toast]') : null;
    if (btn) {
      const toastId = btn.getAttribute('data-show-toast') || 'demoToast';
      const toast = document.getElementById(toastId);
      if (toast) {
        toast.classList.remove('hidden');
        setTimeout(() => toast.classList.add('hidden'), 3500);
      }
    }
    const toastClose = e.target instanceof Element ? e.target.closest('[data-close-toast]') : null;
    if (toastClose) {
      const toast = toastClose.closest('[data-toast]');
      if (toast) toast.classList.add('hidden');
    }
  });

  // Global Loader Lifecycle & Cancel Support
  window.a2ShowLoader = (title, message) => {
    const loader = document.getElementById('a2GlobalLoader');
    if (loader) {
      const titleEl = document.getElementById('a2GlobalLoaderTitle');
      const msgEl = document.getElementById('a2GlobalLoaderMessage');
      if (titleEl && title) titleEl.textContent = title;
      if (msgEl && message) msgEl.textContent = message;
      loader.classList.remove('hidden');
    }
  };

  window.a2HideLoader = () => {
    const loader = document.getElementById('a2GlobalLoader');
    if (loader) loader.classList.add('hidden');
  };

  document.addEventListener('submit', (e) => {
    const form = e.target instanceof HTMLFormElement ? e.target : null;
    if (!form || e.defaultPrevented || form.hasAttribute('data-no-loader')) return;
    if (typeof form.checkValidity === 'function' && !form.checkValidity()) return;
    const title = form.getAttribute('data-loader-title') || 'Processing Request...';
    const msg = form.getAttribute('data-loader-message') || 'Please wait while the server completes your request.';
    window.a2ShowLoader(title, msg);
  });

  document.addEventListener('click', (e) => {
    const cancelBtn = e.target instanceof Element ? e.target.closest('#a2GlobalLoaderCancel') : null;
    if (cancelBtn) {
      try { window.stop(); } catch {}
      window.a2HideLoader();
      document.querySelectorAll('button[type="submit"]:disabled').forEach((b) => b.removeAttribute('disabled'));
    }

    // Interactive showcase triggers
    const triggerBtn = e.target instanceof Element ? e.target.closest('[data-trigger-loader]') : null;
    if (triggerBtn) {
      const type = triggerBtn.getAttribute('data-trigger-loader');
      if (type === 'fullscreen') {
        window.a2ShowLoader('Full-Screen Loader Demo', 'Click "Cancel Operation" to abort.');
      } else if (type === 'partial') {
        const partial = document.getElementById('demoPartialLoader');
        if (partial) partial.classList.remove('hidden');
      }
    }

    const partialCancel = e.target instanceof Element ? e.target.closest('[data-partial-loader-cancel]') : null;
    if (partialCancel) {
      const partial = document.getElementById('demoPartialLoader');
      if (partial) partial.classList.add('hidden');
    }

    const toggleLoadingBtn = e.target instanceof Element ? e.target.closest('[data-toggle-loading]') : null;
    if (toggleLoadingBtn) {
      const isSpinning = toggleLoadingBtn.getAttribute('data-is-loading') === 'true';
      const spinner = toggleLoadingBtn.querySelector('[data-btn-spinner]');
      const text = toggleLoadingBtn.querySelector('[data-btn-text]');
      if (isSpinning) {
        toggleLoadingBtn.setAttribute('data-is-loading', 'false');
        if (spinner) spinner.classList.add('hidden');
        if (text) text.textContent = 'Click to Start Loading';
      } else {
        toggleLoadingBtn.setAttribute('data-is-loading', 'true');
        if (spinner) spinner.classList.remove('hidden');
        if (text) text.textContent = 'Saving Changes...';
      }
    }
  });

  // Dark mode toggle & state management
  window.toggleDarkMode = () => {
    const isDark = document.documentElement.classList.toggle('dark');
    try {
      localStorage.setItem('theme', isDark ? 'dark' : 'light');
    } catch { /* storage restricted */ }
    window.dispatchEvent(new CustomEvent('themechanged', { detail: { theme: isDark ? 'dark' : 'light' } }));
  };

  // Culture / Language switcher
  window.setCulture = (culture) => {
    if (!culture) return;
    document.cookie = ".SystemX.Auth2.Culture=" + encodeURIComponent(culture) + "; path=/; max-age=" + (365 * 86400) + "; SameSite=Lax";
    const url = new URL(window.location.href);
    url.searchParams.set('ui_locales', culture);
    window.location.href = url.toString();
  };
})();
