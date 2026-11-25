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

  // Page Navigation Loader
  document.addEventListener('click', (e) => {
    const link = e.target instanceof Element ? e.target.closest('a[href]') : null;
    if (!link || link.hasAttribute('data-no-loader') || link.getAttribute('target') === '_blank') return;
    if (e.defaultPrevented || e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;
    const href = link.getAttribute('href');
    if (!href || href.startsWith('#') || href.startsWith('javascript:') || href.startsWith('mailto:') || href.startsWith('tel:')) return;
    try {
      const targetUrl = new URL(link.href, window.location.href);
      if (targetUrl.origin === window.location.origin && (targetUrl.pathname !== window.location.pathname || targetUrl.search !== window.location.search)) {
        window.a2ShowLoader('Loading Page...', 'Please wait while the page loads.');
      }
    } catch {}
  });

  window.addEventListener('pageshow', () => {
    window.a2HideLoader();
  });

  // Role Delete Modal
  document.addEventListener('click', (e) => {
    const btn = e.target instanceof Element ? e.target.closest('[data-delete-role]') : null;
    if (btn) {
      const roleName = btn.getAttribute('data-delete-role') || '';
      const modal = document.getElementById('deleteRoleModal');
      const nameEl = document.getElementById('deleteRoleModalName');
      const inputEl = document.getElementById('deleteRoleInputName');
      if (nameEl) nameEl.textContent = roleName;
      if (inputEl) inputEl.value = roleName;
      if (modal) modal.classList.remove('hidden');
    }

    const closeBtn = e.target instanceof Element ? e.target.closest('[data-role-modal-close]') : null;
    if (closeBtn) {
      const modal = document.getElementById('deleteRoleModal');
      if (modal) modal.classList.add('hidden');
    }
  });

  // Interactive Tags Input ([data-tags-input])
  function updateTagsHiddenInput(container) {
    const hiddenId = container.getAttribute('data-tags-input');
    const hiddenInput = hiddenId ? document.getElementById(hiddenId) : null;
    if (!hiddenInput) return;
    const chips = container.querySelectorAll('.a2-tags-list [data-tag]');
    const values = Array.from(chips).map(c => c.getAttribute('data-tag')).filter(Boolean);
    hiddenInput.value = values.join(' ');
  }

  document.addEventListener('keydown', (e) => {
    const input = e.target instanceof HTMLInputElement && e.target.classList.contains('a2-tags-add-input') ? e.target : null;
    if (!input) return;
    const container = input.closest('[data-tags-input]');
    if (!container) return;

    if (e.key === 'Enter' || e.key === ',') {
      e.preventDefault();
      const val = input.value.replace(/,/g, '').trim();
      if (val) {
        const list = container.querySelector('.a2-tags-list');
        const existing = list?.querySelector(`[data-tag="${val}"]`);
        if (!existing && list) {
          const chip = document.createElement('span');
          chip.className = 'inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md text-xs font-medium bg-blue-50 text-blue-700 dark:bg-blue-900/50 dark:text-blue-200 border border-blue-200 dark:border-blue-700';
          chip.setAttribute('data-tag', val);
          chip.innerHTML = `<span>${val}</span><button type="button" class="text-blue-400 hover:text-blue-600 dark:hover:text-blue-100 font-bold ml-0.5 cursor-pointer" data-remove-tag aria-label="Remove ${val}">&times;</button>`;
          list.appendChild(chip);
          updateTagsHiddenInput(container);
        }
        input.value = '';
      }
    } else if (e.key === 'Backspace' && input.value === '') {
      const list = container.querySelector('.a2-tags-list');
      const lastChip = list?.lastElementChild;
      if (lastChip) {
        lastChip.remove();
        updateTagsHiddenInput(container);
      }
    }
  });

  document.addEventListener('click', (e) => {
    const removeBtn = e.target instanceof Element ? e.target.closest('[data-remove-tag]') : null;
    if (removeBtn) {
      const chip = removeBtn.closest('[data-tag], [data-tag-value]');
      const container = removeBtn.closest('[data-tags-input], [data-tag-multiselect]');
      if (chip) {
        chip.remove();
        if (container && container.hasAttribute('data-tags-input')) {
          updateTagsHiddenInput(container);
        } else if (container && container.hasAttribute('data-tag-multiselect')) {
          updateMultiSelectTagsHidden(container);
        }
      }
    }
  });

  // --- DatePicker Component Logic ---
  const DP_MONTH_NAMES = [
    'January', 'February', 'March', 'April', 'May', 'June',
    'July', 'August', 'September', 'October', 'November', 'December'
  ];

  function renderDatePicker(wrapper) {
    const popup = wrapper.querySelector('.a2-datepicker-popup');
    if (!popup) return;
    const input = wrapper.querySelector('.a2-datepicker-input');
    const titleEl = popup.querySelector('.a2-dp-title');
    const daysEl = popup.querySelector('.a2-dp-days');
    const clearBtn = wrapper.querySelector('.a2-datepicker-clear');

    let year = parseInt(popup.getAttribute('data-view-year') || '', 10);
    let month = parseInt(popup.getAttribute('data-view-month') || '', 10);
    if (isNaN(year) || isNaN(month)) {
      const dVal = input?.value ? new Date(input.value) : new Date();
      year = isNaN(dVal.getFullYear()) ? new Date().getFullYear() : dVal.getFullYear();
      month = isNaN(dVal.getMonth()) ? new Date().getMonth() : dVal.getMonth();
      popup.setAttribute('data-view-year', year);
      popup.setAttribute('data-view-month', month);
    }

    if (titleEl) {
      titleEl.textContent = `${DP_MONTH_NAMES[month]} ${year}`;
    }

    const selectedVal = input?.value || '';
    if (clearBtn) {
      clearBtn.classList.toggle('hidden', !selectedVal);
    }

    if (!daysEl) return;
    daysEl.innerHTML = '';

    const firstDayIndex = new Date(year, month, 1).getDay(); // 0 = Sun
    const daysInMonth = new Date(year, month + 1, 0).getDate();
    const daysInPrevMonth = new Date(year, month, 0).getDate();

    const today = new Date();
    const todayStr = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;

    // Prev month padding days
    for (let i = firstDayIndex - 1; i >= 0; i--) {
      const dNum = daysInPrevMonth - i;
      const prevMonth = month === 0 ? 11 : month - 1;
      const prevYear = month === 0 ? year - 1 : year;
      const dStr = `${prevYear}-${String(prevMonth + 1).padStart(2, '0')}-${String(dNum).padStart(2, '0')}`;
      const dBtn = document.createElement('button');
      dBtn.type = 'button';
      dBtn.className = 'py-1.5 rounded-lg text-gray-400 dark:text-gray-600 hover:bg-gray-100 dark:hover:bg-gray-700/50 cursor-pointer';
      dBtn.textContent = String(dNum);
      dBtn.setAttribute('data-dp-date', dStr);
      daysEl.appendChild(dBtn);
    }

    // Current month days
    for (let d = 1; d <= daysInMonth; d++) {
      const dStr = `${year}-${String(month + 1).padStart(2, '0')}-${String(d).padStart(2, '0')}`;
      const isSelected = dStr === selectedVal;
      const isToday = dStr === todayStr;

      const dBtn = document.createElement('button');
      dBtn.type = 'button';
      dBtn.setAttribute('data-dp-date', dStr);

      let classes = 'py-1.5 rounded-lg cursor-pointer transition-colors font-medium ';
      if (isSelected) {
        classes += 'bg-blue-600 text-white font-bold shadow-sm';
      } else if (isToday) {
        classes += 'border border-blue-500 text-blue-600 dark:text-blue-400 font-bold hover:bg-blue-50 dark:hover:bg-blue-900/30';
      } else {
        classes += 'text-gray-800 dark:text-gray-200 hover:bg-blue-50 dark:hover:bg-blue-900/30';
      }
      dBtn.className = classes;
      dBtn.textContent = String(d);
      daysEl.appendChild(dBtn);
    }
  }

  // --- Clock / TimePicker Component Logic ---
  function updateTimePickerDisplay(wrapper) {
    const popup = wrapper.querySelector('.a2-timepicker-popup');
    if (!popup) return;
    const is24h = wrapper.getAttribute('data-use-24h') === 'true';

    let h = popup.getAttribute('data-hour') || (is24h ? '12' : '12');
    let m = popup.getAttribute('data-minute') || '00';
    let ampm = popup.getAttribute('data-ampm') || 'AM';

    const hDisplay = popup.querySelector('.a2-tp-btn-hour');
    const mDisplay = popup.querySelector('.a2-tp-btn-min');
    if (hDisplay) hDisplay.textContent = h;
    if (mDisplay) mDisplay.textContent = m;

    if (!is24h) {
      popup.querySelectorAll('.a2-tp-ampm').forEach(btn => {
        const active = btn.getAttribute('data-ampm') === ampm;
        btn.className = active
          ? 'a2-tp-ampm px-2 py-1 rounded-md bg-blue-600 text-white cursor-pointer font-bold'
          : 'a2-tp-ampm px-2 py-1 rounded-md text-gray-600 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-gray-600 cursor-pointer';
      });
    }

    popup.querySelectorAll('.a2-tp-hour-item').forEach(btn => {
      const isSel = btn.getAttribute('data-hour') === h;
      btn.className = isSel
        ? 'a2-tp-hour-item py-2 rounded-lg font-bold bg-blue-600 text-white shadow-xs cursor-pointer'
        : 'a2-tp-hour-item py-2 rounded-lg font-medium text-gray-800 dark:text-gray-200 hover:bg-blue-50 dark:hover:bg-blue-900/30 cursor-pointer transition-colors';
    });

    popup.querySelectorAll('.a2-tp-minute-item').forEach(btn => {
      const isSel = btn.getAttribute('data-minute') === m;
      btn.className = isSel
        ? 'a2-tp-minute-item py-2 rounded-lg font-bold bg-blue-600 text-white shadow-xs cursor-pointer'
        : 'a2-tp-minute-item py-2 rounded-lg font-medium text-gray-800 dark:text-gray-200 hover:bg-blue-50 dark:hover:bg-blue-900/30 cursor-pointer transition-colors';
    });
  }

  function setTimePickerTab(wrapper, tab) {
    const popup = wrapper.querySelector('.a2-timepicker-popup');
    if (!popup) return;
    const hourGrid = popup.querySelector('.a2-tp-hours-grid');
    const minGrid = popup.querySelector('.a2-tp-minutes-grid');
    const tabHour = popup.querySelector('.a2-tp-tab-hour');
    const tabMin = popup.querySelector('.a2-tp-tab-min');

    if (tab === 'hour') {
      hourGrid?.classList.remove('hidden');
      minGrid?.classList.add('hidden');
      if (tabHour) tabHour.className = 'a2-tp-tab-hour text-blue-600 dark:text-blue-400 border-b-2 border-blue-600 pb-1 cursor-pointer';
      if (tabMin) tabMin.className = 'a2-tp-tab-min text-gray-400 dark:text-gray-500 pb-1 cursor-pointer hover:text-gray-700 dark:hover:text-gray-300';
    } else {
      hourGrid?.classList.add('hidden');
      minGrid?.classList.remove('hidden');
      if (tabHour) tabHour.className = 'a2-tp-tab-hour text-gray-400 dark:text-gray-500 pb-1 cursor-pointer hover:text-gray-700 dark:hover:text-gray-300';
      if (tabMin) tabMin.className = 'a2-tp-tab-min text-blue-600 dark:text-blue-400 border-b-2 border-blue-600 pb-1 cursor-pointer';
    }
  }

  // Tag Multi-Select Hidden Input Sync
  function updateMultiSelectTagsHidden(container) {
    const hiddenId = container.getAttribute('data-tag-multiselect');
    const hiddenInput = hiddenId ? document.getElementById(hiddenId) : null;
    if (!hiddenInput) return;
    const chips = container.querySelectorAll('.a2-selected-tags [data-tag-value]');
    const values = Array.from(chips).map(c => c.getAttribute('data-tag-value')).filter(Boolean);
    hiddenInput.value = values.join(',');
    hiddenInput.dispatchEvent(new Event('input', { bubbles: true }));
    hiddenInput.dispatchEvent(new Event('change', { bubbles: true }));
  }

  // Global Click Handlers for Interactive Components
  document.addEventListener('click', (e) => {
    // 1. Toast / Alert Dismiss
    const dismissAlert = e.target instanceof Element ? e.target.closest('[data-dismiss-alert]') : null;
    if (dismissAlert) {
      const banner = dismissAlert.closest('a2-alert, a2-toast, [role="alert"], .a2-toast-card') || dismissAlert.parentElement;
      banner?.remove();
      return;
    }

    // 2. Combobox Trigger
    const trigger = e.target instanceof Element ? e.target.closest('.a2-combobox-trigger') : null;
    if (trigger) {
      const combobox = trigger.closest('.a2-combobox');
      const dropdown = combobox?.querySelector('.a2-combobox-dropdown');
      if (dropdown) {
        const isHidden = dropdown.classList.contains('hidden');
        document.querySelectorAll('.a2-combobox-dropdown').forEach(d => d.classList.add('hidden'));
        if (isHidden) {
          dropdown.classList.remove('hidden');
          const searchInput = dropdown.querySelector('.a2-combobox-search');
          if (searchInput) { searchInput.value = ''; searchInput.focus(); }
          dropdown.querySelectorAll('.a2-combobox-option').forEach(o => o.classList.remove('hidden'));
        }
      }
      return;
    }

    // 3. Combobox Option Click
    const opt = e.target instanceof Element ? e.target.closest('.a2-combobox-option') : null;
    if (opt) {
      const combobox = opt.closest('.a2-combobox');
      const hiddenId = combobox?.getAttribute('data-combobox');
      const hiddenInput = hiddenId ? document.getElementById(hiddenId) : null;
      const display = combobox?.querySelector('.a2-combobox-display');
      const val = opt.getAttribute('data-value') || '';
      const label = opt.getAttribute('data-label') || opt.textContent?.trim() || val;
      if (hiddenInput) {
        hiddenInput.value = val;
        hiddenInput.dispatchEvent(new Event('input', { bubbles: true }));
        hiddenInput.dispatchEvent(new Event('change', { bubbles: true }));
      }
      if (display) display.textContent = label;
      combobox?.querySelectorAll('.a2-combobox-option').forEach(o => {
        const badge = o.querySelector('.text-blue-600, .text-blue-400');
        if (badge) badge.remove();
      });
      const badge = document.createElement('span');
      badge.className = 'text-xs text-blue-600 dark:text-blue-400 font-semibold';
      badge.textContent = 'Selected';
      opt.appendChild(badge);
      opt.closest('.a2-combobox-dropdown')?.classList.add('hidden');
      return;
    }

    if (!e.target.closest('.a2-combobox')) {
      document.querySelectorAll('.a2-combobox-dropdown').forEach(d => d.classList.add('hidden'));
    }

    // 4. Tag Multi-Select Remove Chip
    const tagRemoveBtn = e.target instanceof Element ? e.target.closest('[data-remove-tag]') : null;
    if (tagRemoveBtn) {
      const chip = tagRemoveBtn.closest('[data-tag-value]');
      const wrapper = tagRemoveBtn.closest('.a2-tag-multiselect');
      chip?.remove();
      if (wrapper) updateMultiSelectTagsHidden(wrapper);
      return;
    }

    // 5. Tag Multi-Select Open Dropdown
    const tagContainer = e.target instanceof Element ? e.target.closest('.a2-tag-multiselect-container') : null;
    if (tagContainer) {
      const wrapper = tagContainer.closest('.a2-tag-multiselect');
      const dropdown = wrapper?.querySelector('.a2-tag-dropdown');
      if (dropdown) dropdown.classList.remove('hidden');
      wrapper?.querySelector('.a2-tag-search-input')?.focus();
      return;
    }

    // 6. Tag Multi-Select Add Option
    const tagOpt = e.target instanceof Element ? e.target.closest('.a2-tag-options-list li') : null;
    if (tagOpt) {
      const wrapper = tagOpt.closest('.a2-tag-multiselect');
      const val = tagOpt.getAttribute('data-value') || '';
      const label = tagOpt.querySelector('span')?.textContent || val;
      const tagsBox = wrapper?.querySelector('.a2-selected-tags');
      if (wrapper && tagsBox && val && !tagsBox.querySelector(`[data-tag-value="${val}"]`)) {
        const chip = document.createElement('span');
        chip.className = 'inline-flex items-center gap-1 px-2.5 py-1 rounded-md text-xs font-medium bg-blue-100 dark:bg-blue-900/50 text-blue-800 dark:text-blue-200';
        chip.setAttribute('data-tag-value', val);
        chip.innerHTML = `<span>${label}</span><button type="button" class="text-blue-500 hover:text-blue-700 dark:hover:text-blue-100 font-bold ml-1 cursor-pointer" data-remove-tag>&times;</button>`;
        tagsBox.appendChild(chip);
        updateMultiSelectTagsHidden(wrapper);
      }
      wrapper?.querySelector('.a2-tag-dropdown')?.classList.add('hidden');
      return;
    }

    if (!e.target.closest('.a2-tag-multiselect')) {
      document.querySelectorAll('.a2-tag-dropdown').forEach(d => d.classList.add('hidden'));
    }

    // 7. DatePicker Open / Toggle
    const dpInput = e.target instanceof Element ? e.target.closest('.a2-datepicker-input, .a2-datepicker svg') : null;
    if (dpInput) {
      const wrapper = dpInput.closest('.a2-datepicker');
      const popup = wrapper?.querySelector('.a2-datepicker-popup');
      if (popup) {
        const isHidden = popup.classList.contains('hidden');
        document.querySelectorAll('.a2-datepicker-popup').forEach(p => p.classList.add('hidden'));
        if (isHidden) {
          popup.classList.remove('hidden');
          renderDatePicker(wrapper);
        }
      }
      return;
    }

    // DatePicker Prev Month
    const dpPrev = e.target instanceof Element ? e.target.closest('.a2-dp-prev') : null;
    if (dpPrev) {
      const wrapper = dpPrev.closest('.a2-datepicker');
      const popup = wrapper?.querySelector('.a2-datepicker-popup');
      if (popup) {
        let y = parseInt(popup.getAttribute('data-view-year') || '', 10);
        let m = parseInt(popup.getAttribute('data-view-month') || '', 10);
        m--;
        if (m < 0) { m = 11; y--; }
        popup.setAttribute('data-view-year', y);
        popup.setAttribute('data-view-month', m);
        renderDatePicker(wrapper);
      }
      return;
    }

    // DatePicker Next Month
    const dpNext = e.target instanceof Element ? e.target.closest('.a2-dp-next') : null;
    if (dpNext) {
      const wrapper = dpNext.closest('.a2-datepicker');
      const popup = wrapper?.querySelector('.a2-datepicker-popup');
      if (popup) {
        let y = parseInt(popup.getAttribute('data-view-year') || '', 10);
        let m = parseInt(popup.getAttribute('data-view-month') || '', 10);
        m++;
        if (m > 11) { m = 0; y++; }
        popup.setAttribute('data-view-year', y);
        popup.setAttribute('data-view-month', m);
        renderDatePicker(wrapper);
      }
      return;
    }

    // DatePicker Day Selection
    const dpDate = e.target instanceof Element ? e.target.closest('[data-dp-date]') : null;
    if (dpDate) {
      const wrapper = dpDate.closest('.a2-datepicker');
      const dStr = dpDate.getAttribute('data-dp-date');
      const input = wrapper?.querySelector('.a2-datepicker-input');
      if (input && dStr) {
        input.value = dStr;
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
      }
      wrapper?.querySelector('.a2-datepicker-popup')?.classList.add('hidden');
      wrapper?.querySelector('.a2-datepicker-clear')?.classList.remove('hidden');
      return;
    }

    // DatePicker Today Click
    const dpToday = e.target instanceof Element ? e.target.closest('.a2-dp-today') : null;
    if (dpToday) {
      const wrapper = dpToday.closest('.a2-datepicker');
      const today = new Date();
      const dStr = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
      const input = wrapper?.querySelector('.a2-datepicker-input');
      if (input) {
        input.value = dStr;
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
      }
      wrapper?.querySelector('.a2-datepicker-popup')?.classList.add('hidden');
      wrapper?.querySelector('.a2-datepicker-clear')?.classList.remove('hidden');
      return;
    }

    // DatePicker Close Button
    const dpClose = e.target instanceof Element ? e.target.closest('.a2-dp-close') : null;
    if (dpClose) {
      dpClose.closest('.a2-datepicker-popup')?.classList.add('hidden');
      return;
    }

    // DatePicker Clear Button
    const dpClear = e.target instanceof Element ? e.target.closest('.a2-datepicker-clear') : null;
    if (dpClear) {
      const wrapper = dpClear.closest('.a2-datepicker');
      const input = wrapper?.querySelector('.a2-datepicker-input');
      if (input) {
        input.value = '';
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
      }
      dpClear.classList.add('hidden');
      return;
    }

    if (!e.target.closest('.a2-datepicker')) {
      document.querySelectorAll('.a2-datepicker-popup').forEach(p => p.classList.add('hidden'));
    }

    // 8. TimePicker Open / Toggle
    const tpInput = e.target instanceof Element ? e.target.closest('.a2-timepicker-input, .a2-timepicker svg') : null;
    if (tpInput) {
      const wrapper = tpInput.closest('.a2-timepicker');
      const popup = wrapper?.querySelector('.a2-timepicker-popup');
      if (popup) {
        const isHidden = popup.classList.contains('hidden');
        document.querySelectorAll('.a2-timepicker-popup').forEach(p => p.classList.add('hidden'));
        if (isHidden) {
          popup.classList.remove('hidden');
          const input = wrapper.querySelector('.a2-timepicker-input');
          const val = (input?.value || '').trim();
          const is24h = wrapper.getAttribute('data-use-24h') === 'true';

          if (val) {
            const parts = val.split(/[:\s]/);
            if (parts.length >= 2) {
              popup.setAttribute('data-hour', parts[0]);
              popup.setAttribute('data-minute', parts[1]);
              if (!is24h && parts.length >= 3) {
                popup.setAttribute('data-ampm', parts[2].toUpperCase());
              }
            }
          } else {
            const now = new Date();
            let hr = now.getHours();
            let min = Math.round(now.getMinutes() / 5) * 5;
            if (min === 60) min = 55;
            let ampm = 'AM';
            if (!is24h) {
              if (hr >= 12) { ampm = 'PM'; if (hr > 12) hr -= 12; }
              else if (hr === 0) hr = 12;
            }
            popup.setAttribute('data-hour', String(hr));
            popup.setAttribute('data-minute', String(min).padStart(2, '0'));
            popup.setAttribute('data-ampm', ampm);
          }
          setTimePickerTab(wrapper, 'hour');
          updateTimePickerDisplay(wrapper);
        }
      }
      return;
    }

    // TimePicker Hour Tab
    const tpTabH = e.target instanceof Element ? e.target.closest('.a2-tp-tab-hour, .a2-tp-btn-hour') : null;
    if (tpTabH) {
      const wrapper = tpTabH.closest('.a2-timepicker');
      if (wrapper) setTimePickerTab(wrapper, 'hour');
      return;
    }

    // TimePicker Minute Tab
    const tpTabM = e.target instanceof Element ? e.target.closest('.a2-tp-tab-min, .a2-tp-btn-min') : null;
    if (tpTabM) {
      const wrapper = tpTabM.closest('.a2-timepicker');
      if (wrapper) setTimePickerTab(wrapper, 'min');
      return;
    }

    // TimePicker Hour Item Click
    const tpHourItem = e.target instanceof Element ? e.target.closest('[data-hour]') : null;
    if (tpHourItem) {
      const wrapper = tpHourItem.closest('.a2-timepicker');
      const popup = wrapper?.querySelector('.a2-timepicker-popup');
      if (popup) {
        popup.setAttribute('data-hour', tpHourItem.getAttribute('data-hour') || '12');
        updateTimePickerDisplay(wrapper);
        setTimePickerTab(wrapper, 'min');
      }
      return;
    }

    // TimePicker Minute Item Click
    const tpMinItem = e.target instanceof Element ? e.target.closest('[data-minute]') : null;
    if (tpMinItem) {
      const wrapper = tpMinItem.closest('.a2-timepicker');
      const popup = wrapper?.querySelector('.a2-timepicker-popup');
      if (popup) {
        popup.setAttribute('data-minute', tpMinItem.getAttribute('data-minute') || '00');
        updateTimePickerDisplay(wrapper);
      }
      return;
    }

    // TimePicker AM/PM Click
    const tpAmPm = e.target instanceof Element ? e.target.closest('.a2-tp-ampm') : null;
    if (tpAmPm) {
      const wrapper = tpAmPm.closest('.a2-timepicker');
      const popup = wrapper?.querySelector('.a2-timepicker-popup');
      if (popup) {
        popup.setAttribute('data-ampm', tpAmPm.getAttribute('data-ampm') || 'AM');
        updateTimePickerDisplay(wrapper);
      }
      return;
    }

    // TimePicker Current Time (Now)
    const tpNow = e.target instanceof Element ? e.target.closest('.a2-tp-now') : null;
    if (tpNow) {
      const wrapper = tpNow.closest('.a2-timepicker');
      const popup = wrapper?.querySelector('.a2-timepicker-popup');
      const is24h = wrapper?.getAttribute('data-use-24h') === 'true';
      if (popup) {
        const now = new Date();
        let hr = now.getHours();
        let min = Math.round(now.getMinutes() / 5) * 5;
        if (min === 60) min = 55;
        let ampm = 'AM';
        if (!is24h) {
          if (hr >= 12) { ampm = 'PM'; if (hr > 12) hr -= 12; }
          else if (hr === 0) hr = 12;
        }
        popup.setAttribute('data-hour', String(hr));
        popup.setAttribute('data-minute', String(min).padStart(2, '0'));
        popup.setAttribute('data-ampm', ampm);
        updateTimePickerDisplay(wrapper);
      }
      return;
    }

    // TimePicker Apply Button
    const tpApply = e.target instanceof Element ? e.target.closest('.a2-tp-apply') : null;
    if (tpApply) {
      const wrapper = tpApply.closest('.a2-timepicker');
      const popup = wrapper?.querySelector('.a2-timepicker-popup');
      const input = wrapper?.querySelector('.a2-timepicker-input');
      const is24h = wrapper?.getAttribute('data-use-24h') === 'true';
      if (popup && input) {
        const h = popup.getAttribute('data-hour') || '12';
        const m = popup.getAttribute('data-minute') || '00';
        const ampm = popup.getAttribute('data-ampm') || 'AM';
        input.value = is24h ? `${h}:${m}` : `${h}:${m} ${ampm}`;
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
        wrapper.querySelector('.a2-timepicker-clear')?.classList.remove('hidden');
      }
      popup?.classList.add('hidden');
      return;
    }

    // TimePicker Cancel Button
    const tpCancel = e.target instanceof Element ? e.target.closest('.a2-tp-cancel') : null;
    if (tpCancel) {
      tpCancel.closest('.a2-timepicker-popup')?.classList.add('hidden');
      return;
    }

    // TimePicker Clear Button
    const tpClear = e.target instanceof Element ? e.target.closest('.a2-timepicker-clear') : null;
    if (tpClear) {
      const wrapper = tpClear.closest('.a2-timepicker');
      const input = wrapper?.querySelector('.a2-timepicker-input');
      if (input) {
        input.value = '';
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
      }
      tpClear.classList.add('hidden');
      return;
    }

    if (!e.target.closest('.a2-timepicker')) {
      document.querySelectorAll('.a2-timepicker-popup').forEach(p => p.classList.add('hidden'));
    }
  });

  // Search Filtering for Combobox and MultiSelect
  document.addEventListener('input', (e) => {
    const search = e.target instanceof HTMLInputElement && e.target.classList.contains('a2-combobox-search') ? e.target : null;
    if (search) {
      const query = search.value.toLowerCase().trim();
      const list = search.closest('.a2-combobox-dropdown')?.querySelector('.a2-combobox-list');
      if (list) {
        list.querySelectorAll('.a2-combobox-option').forEach(item => {
          const text = (item.textContent || '').toLowerCase();
          item.classList.toggle('hidden', Boolean(query && !text.includes(query)));
        });
      }
    }

    const tagSearch = e.target instanceof HTMLInputElement && e.target.classList.contains('a2-tag-search-input') ? e.target : null;
    if (tagSearch) {
      const query = tagSearch.value.toLowerCase().trim();
      const wrapper = tagSearch.closest('.a2-tag-multiselect');
      const list = wrapper?.querySelector('.a2-tag-options-list');
      if (list) {
        list.querySelectorAll('li').forEach(item => {
          const text = (item.textContent || '').toLowerCase();
          item.classList.toggle('hidden', Boolean(query && !text.includes(query)));
        });
      }
    }
  });

  // Client-Side & Server-Side Input Validation States matching /admin/p/{key}/components
  function createValidationIcon() {
    const icon = document.createElement('div');
    icon.className = 'a2-validation-icon pointer-events-none absolute inset-y-0 right-0 flex items-center pr-3 text-red-500';
    icon.innerHTML = '<svg class="h-5 w-5" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clip-rule="evenodd" /></svg>';
    return icon;
  }

  function applyValidationIcons() {
    document.querySelectorAll('input.input-validation-error, select.input-validation-error, textarea.input-validation-error').forEach(el => {
      const container = el.parentElement;
      if (!container) return;
      if (!container.classList.contains('relative')) container.classList.add('relative');
      if (!el.classList.contains('pr-10') && el.tagName === 'INPUT') el.classList.add('pr-10');
      if (!container.querySelector('.a2-validation-icon')) {
        container.appendChild(createValidationIcon());
      }
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', applyValidationIcons);
  } else {
    applyValidationIcons();
  }

  document.addEventListener('submit', (e) => {
    const form = e.target instanceof HTMLFormElement && e.target.hasAttribute('novalidate') ? e.target : null;
    if (!form) return;

    let firstInvalid = null;
    const elements = form.querySelectorAll('input:not([type="hidden"]):not([disabled]), select:not([disabled]), textarea:not([disabled])');
    elements.forEach(el => {
      const container = el.parentElement;
      const errEl = container?.querySelector('.a2-validation-error') || container?.parentElement?.querySelector('.a2-validation-error');
      const iconEl = container?.querySelector('.a2-validation-icon');

      if (!el.checkValidity()) {
        el.classList.add('a2-invalid');
        if (!firstInvalid) firstInvalid = el;
        if (container && !container.classList.contains('relative')) container.classList.add('relative');
        if (!el.classList.contains('pr-10') && el.tagName === 'INPUT') el.classList.add('pr-10');

        if (!iconEl && container) {
          container.appendChild(createValidationIcon());
        }

        if (!errEl && container) {
          const msg = document.createElement('p');
          msg.className = 'a2-validation-error mt-1 text-xs text-red-600 dark:text-red-400';
          msg.textContent = el.validationMessage || 'This field is required.';
          if (container.parentElement && container.parentElement.classList.contains('a2-field-wrapper')) {
            container.parentElement.appendChild(msg);
          } else {
            container.appendChild(msg);
          }
        } else if (errEl) {
          errEl.textContent = el.validationMessage || 'This field is required.';
        }
      } else {
        el.classList.remove('a2-invalid');
        if (errEl) errEl.remove();
        if (iconEl) iconEl.remove();
      }
    });

    if (firstInvalid) {
      e.preventDefault();
      firstInvalid.focus();
    }
  });

  document.addEventListener('input', (e) => {
    const el = e.target instanceof HTMLElement && (e.target.classList.contains('a2-invalid') || e.target.classList.contains('input-validation-error')) ? e.target : null;
    if (el && typeof el.checkValidity === 'function' && el.checkValidity()) {
      el.classList.remove('a2-invalid');
      el.classList.remove('input-validation-error');
      const container = el.parentElement;
      const errEl = container?.querySelector('.a2-validation-error') || container?.parentElement?.querySelector('.a2-validation-error') || container?.parentElement?.querySelector('.field-validation-error');
      const iconEl = container?.querySelector('.a2-validation-icon');
      if (errEl) errEl.remove();
      if (iconEl) iconEl.remove();
    }
  });

  // WebAuthn Passkey Handlers
  function bufferToBase64Url(buffer) {
    const bytes = new Uint8Array(buffer);
    let binary = '';
    for (let i = 0; i < bytes.byteLength; i++) binary += String.fromCharCode(bytes[i]);
    return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '');
  }

  function base64UrlToBuffer(base64url) {
    const base64 = base64url.replace(/-/g, '+').replace(/_/g, '/');
    const bin = atob(base64);
    const buf = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) buf[i] = bin.charCodeAt(i);
    return buf.buffer;
  }

  // Passkey Sign In
  document.addEventListener('click', async (e) => {
    const btn = e.target instanceof Element ? e.target.closest('#btnPasskeySignIn') : null;
    if (!btn || !window.PublicKeyCredential) return;

    const alertBox = document.getElementById('passkeyAlert');
    const alertText = document.getElementById('passkeyAlertText');
    const loginInput = document.getElementById('passkeyLoginInput');
    const clientId = document.getElementById('passkeyClientId')?.value || '';
    const returnUrl = document.getElementById('passkeyReturnUrl')?.value || '';

    const showError = (msg) => {
      if (alertText) alertText.textContent = msg;
      if (alertBox) alertBox.classList.remove('hidden');
    };

    try {
      if (alertBox) alertBox.classList.add('hidden');
      window.a2ShowLoader('Contacting Security Device...', 'Please interact with your passkey or biometric sensor.');

      const optUrl = '/account/passkey/request-options' + (clientId ? '?client_id=' + encodeURIComponent(clientId) : '');
      const optRes = await fetch(optUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ login: loginInput?.value?.trim() || null })
      });

      if (!optRes.ok) {
        window.a2HideLoader();
        const err = await optRes.json().catch(() => ({}));
        showError(err.error_description || err.error || 'Failed to request passkey options.');
        return;
      }

      const options = await optRes.json();
      if (typeof options.challenge === 'string') {
        options.challenge = base64UrlToBuffer(options.challenge);
      }
      if (Array.isArray(options.allowCredentials)) {
        options.allowCredentials.forEach(c => {
          if (typeof c.id === 'string') c.id = base64UrlToBuffer(c.id);
        });
      }

      const credential = await navigator.credentials.get({ publicKey: options });
      if (!credential) {
        window.a2HideLoader();
        showError('No credential selected.');
        return;
      }

      const credJson = typeof credential.toJSON === 'function' ? credential.toJSON() : {
        id: credential.id,
        rawId: bufferToBase64Url(credential.rawId),
        type: credential.type,
        response: {
          clientDataJSON: bufferToBase64Url(credential.response.clientDataJSON),
          authenticatorData: bufferToBase64Url(credential.response.authenticatorData),
          signature: bufferToBase64Url(credential.response.signature),
          userHandle: credential.response.userHandle ? bufferToBase64Url(credential.response.userHandle) : null
        }
      };

      const signinUrl = '/account/passkey/signin' + (clientId ? '?client_id=' + encodeURIComponent(clientId) : '');
      const signInRes = await fetch(signinUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ credential: credJson, returnUrl: returnUrl })
      });

      window.a2HideLoader();
      if (signInRes.ok) {
        const result = await signInRes.json().catch(() => ({}));
        window.location.href = result.returnUrl || returnUrl || '/';
      } else {
        const err = await signInRes.json().catch(() => ({}));
        showError(err.error_description || err.error || 'Passkey verification failed.');
      }
    } catch (err) {
      window.a2HideLoader();
      if (err.name !== 'NotAllowedError') {
        showError(err.message || 'Passkey error occurred.');
      }
    }
  });

  // Passkey Registration
  document.addEventListener('click', async (e) => {
    const btn = e.target instanceof Element ? e.target.closest('#btnPasskeySignUp') : null;
    if (!btn || !window.PublicKeyCredential) return;

    const alertBox = document.getElementById('passkeyRegAlert');
    const alertText = document.getElementById('passkeyRegAlertText');
    const nameInput = document.getElementById('passkeyNameInput');
    const clientId = document.getElementById('passkeyRegClientId')?.value || '';
    const returnUrl = document.getElementById('passkeyRegReturnUrl')?.value || '';

    const showError = (msg) => {
      if (alertText) alertText.textContent = msg;
      if (alertBox) alertBox.classList.remove('hidden');
    };

    try {
      if (alertBox) alertBox.classList.add('hidden');
      window.a2ShowLoader('Creating Passkey...', 'Please touch your fingerprint sensor or security key.');

      const optUrl = '/account/passkey/creation-options' + (clientId ? '?client_id=' + encodeURIComponent(clientId) : '');
      const optRes = await fetch(optUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' }
      });

      if (!optRes.ok) {
        window.a2HideLoader();
        const err = await optRes.json().catch(() => ({}));
        showError(err.error_description || err.error || 'Failed to request creation options.');
        return;
      }

      const options = await optRes.json();
      if (typeof options.challenge === 'string') options.challenge = base64UrlToBuffer(options.challenge);
      if (options.user && typeof options.user.id === 'string') options.user.id = base64UrlToBuffer(options.user.id);
      if (Array.isArray(options.excludeCredentials)) {
        options.excludeCredentials.forEach(c => {
          if (typeof c.id === 'string') c.id = base64UrlToBuffer(c.id);
        });
      }

      const credential = await navigator.credentials.create({ publicKey: options });
      if (!credential) {
        window.a2HideLoader();
        showError('No credential created.');
        return;
      }

      const credJson = typeof credential.toJSON === 'function' ? credential.toJSON() : {
        id: credential.id,
        rawId: bufferToBase64Url(credential.rawId),
        type: credential.type,
        response: {
          clientDataJSON: bufferToBase64Url(credential.response.clientDataJSON),
          attestationObject: bufferToBase64Url(credential.response.attestationObject)
        }
      };

      const regUrl = '/account/passkey/register' + (clientId ? '?client_id=' + encodeURIComponent(clientId) : '');
      const regRes = await fetch(regUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ credential: credJson, name: nameInput?.value?.trim() || null })
      });

      window.a2HideLoader();
      if (regRes.ok) {
        window.location.href = returnUrl || '/';
      } else {
        const err = await regRes.json().catch(() => ({}));
        showError(err.error_description || err.error || 'Failed to register passkey.');
      }
    } catch (err) {
      window.a2HideLoader();
      if (err.name !== 'NotAllowedError') {
        showError(err.message || 'Passkey creation failed.');
      }
    }
  });
})();

// ---------------------------------------------------------------------------------------------------------------------
// Form rules. The Server writes the rules of the project (saved in the database) on the inputs as data-a2-rule (JSON, see
// Pages/Account/AccountFormRules.cs) with the texts already in the language of the page. This file only applies them while the
// person types, so a mistake shows under the input at once. The Server checks everything again when the form is sent, so a
// browser without JavaScript still works; it just learns about the mistake after the click.
(() => {
  'use strict';
  const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

  const inputOf = (el) => (el instanceof HTMLInputElement ? el : el.querySelector('input'));
  const regex = (source) => { try { return new RegExp(source); } catch { return null; } };   // a .NET pattern the browser cannot read is skipped; the Server still checks

  /** The first message that does not hold for the value, or null. */
  const check = (rule, input, form) => {
    const raw = input.value;
    const value = input.type === 'password' ? raw : raw.trim();
    if (value === '') return rule.required ?? null;
    if (rule.max != null && value.length > rule.max) return rule.maxMsg ?? null;
    if (rule.min != null && !rule.checks && value.length < rule.min) return rule.minMsg ?? null;
    if (rule.any) return checkAny(rule.any, value);
    if (rule.pattern) { const re = regex(rule.pattern); if (re && !re.test(value)) return rule.patternMsg ?? null; }
    if (rule.email) {
      if (!EMAIL.test(value)) return rule.email;
      const domain = value.slice(value.lastIndexOf('@') + 1).toLowerCase();
      if (rule.block?.some((d) => d.toLowerCase() === domain)) return rule.domainMsg ?? null;
      if (rule.allow?.length && !rule.allow.some((d) => d.toLowerCase() === domain)) return rule.domainMsg ?? null;
    }
    if (rule.phone) { const re = regex(rule.phone); if (re && !re.test(value)) return rule.phoneMsg ?? null; }
    if (rule.checks) { const failed = failedChecks(rule, value); if (failed.length) return failed[0].msg; }
    if (rule.match) { const other = document.getElementById(rule.match); if (other instanceof HTMLInputElement && other.value !== raw) return rule.matchMsg ?? null; }
    return null;
  };

  /**
   * A field that accepts several kinds of value (user name, email, phone number): it is good when the value fits one of them. When it fits none, the message
   * is the one of the kind the value looks like (an "@" is an email, digits with an optional "+" a phone number, anything else a user name).
   */
  const checkAny = (any, value) => {
    const email = () => (any.email && EMAIL.test(value) ? null : any.email ?? null);
    const phone = () => { const re = any.phone && regex(any.phone.regex); return !any.phone || !re || re.test(value) ? null : any.phone.msg; };
    const user = () => {
      const u = any.userName;
      if (!u) return null;
      if (value.length < u.min) return u.minMsg;
      if (value.length > u.max) return u.maxMsg;
      const re = regex(u.pattern);
      return re && !re.test(value) ? u.patternMsg : null;
    };
    const tries = [];
    if (any.userName) tries.push(['user', user]);
    if (any.email) tries.push(['email', email]);
    if (any.phone) tries.push(['phone', phone]);
    const results = tries.map(([kind, run]) => [kind, run()]);
    if (results.some(([, message]) => message === null)) return null;
    const looksLike = value.includes('@') ? 'email' : /^\+?[\d\s()-]+$/.test(value) && any.phone ? 'phone' : 'user';
    const pick = results.find(([kind]) => kind === looksLike) ?? results[0];
    return pick[1];
  };

  /** The password requirements that do not hold yet. */
  const failedChecks = (rule, value) => (rule.checks ?? []).filter((c) => {
    switch (c.id) {
      case 'length': return value.length < (rule.min ?? 0);
      case 'digit': return !/\d/.test(value);
      case 'lower': return !/\p{Ll}/u.test(value);
      case 'upper': return !/\p{Lu}/u.test(value);
      case 'symbol': return !/[^\p{L}\p{N}]/u.test(value);
      case 'unique': return new Set(Array.from(value)).size < (rule.unique ?? 0);
      default: return false;
    }
  });

  const boxOf = (el) => el.closest('.mb-4') ?? el.parentElement;

  const messageOf = (el) => {
    const box = boxOf(el);
    let p = box.querySelector('[data-a2-error]');
    if (!p) {
      p = document.createElement('p');
      p.setAttribute('data-a2-error', '');
      p.setAttribute('role', 'alert');
      p.className = 'mt-1 text-xs text-red-600 dark:text-red-400';
      p.hidden = true;
      const list = box.querySelector('[data-a2-checks]');
      if (list) box.insertBefore(p, list); else box.appendChild(p);
    }
    return p;
  };

  const show = (el, input, message) => {
    const p = messageOf(el);
    p.textContent = message ?? '';
    p.hidden = !message;
    if (message) {
      p.id = p.id || (input.id ? input.id + '_error' : 'a2err_' + Math.random().toString(36).slice(2, 8));
      input.setAttribute('aria-invalid', 'true');
      input.setAttribute('aria-describedby', p.id);
      input.style.borderColor = '#dc2626';
    } else {
      input.removeAttribute('aria-invalid');
      input.removeAttribute('aria-describedby');
      input.style.borderColor = '';
    }
  };

  /** The live list of password requirements under the input. */
  const buildChecks = (el, rule) => {
    const list = document.createElement('ul');
    list.setAttribute('data-a2-checks', '');
    list.className = 'mt-2 space-y-1 text-xs';
    for (const c of rule.checks) {
      const li = document.createElement('li');
      li.setAttribute('data-check', c.id);
      li.className = 'flex items-center gap-1.5 text-gray-500 dark:text-gray-400';
      const mark = document.createElement('span');
      mark.setAttribute('aria-hidden', 'true');
      mark.textContent = '○';
      const text = document.createElement('span');
      text.textContent = c.msg;
      li.append(mark, text);
      list.appendChild(li);
    }
    boxOf(el).appendChild(list);
    return list;
  };

  const updateChecks = (list, rule, value) => {
    const failed = new Set(failedChecks(rule, value).map((c) => c.id));
    for (const li of list.children) {
      const ok = value !== '' && !failed.has(li.getAttribute('data-check'));
      li.className = 'flex items-center gap-1.5 ' + (ok ? 'text-emerald-600 dark:text-emerald-400' : 'text-gray-500 dark:text-gray-400');
      li.firstChild.textContent = ok ? '✓' : '○';
    }
  };

  const setup = () => {
    const fields = [];
    document.querySelectorAll('[data-a2-rule]').forEach((el) => {
      const input = inputOf(el);
      let rule;
      try { rule = JSON.parse(el.getAttribute('data-a2-rule') ?? ''); } catch { return; }
      if (!input || !rule) return;
      const form = input.form;
      const list = rule.checks && !rule.hideList ? buildChecks(el, rule) : null;
      const field = { el, input, rule, form, list, touched: false };
      fields.push(field);

      const run = (display) => {
        if (list) updateChecks(list, rule, input.value);
        if (display) show(el, input, check(rule, input, form));
      };
      input.addEventListener('input', () => run(field.touched));
      input.addEventListener('blur', () => { field.touched = true; run(true); });   // leaving a required field empty tells at once
      // a field that must equal this one is checked again when this one changes
      input.addEventListener('input', () => {
        for (const other of fields) if (other.rule.match === input.id && other.touched) show(other.el, other.input, check(other.rule, other.input, other.form));
      });
      if (input.value !== '') run(false);
    });

    document.addEventListener('submit', (e) => {
      const form = e.target instanceof HTMLFormElement ? e.target : null;
      if (!form) return;
      let first = null;
      for (const f of fields) {
        if (f.form !== form || f.input.disabled) continue;
        f.touched = true;
        const message = check(f.rule, f.input, form);
        show(f.el, f.input, message);
        if (message && !first) first = f.input;
      }
      if (first) { e.preventDefault(); e.stopImmediatePropagation(); first.focus(); }
    }, true);
  };

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', setup); else setup();
})();
