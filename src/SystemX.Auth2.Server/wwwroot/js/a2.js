// Small behavior for the server-rendered pages. No framework. Every feature also works without JavaScript where it can
// (the confirm dialog only adds a safety question, auto-submit has a submit button next to it).
(() => {
  'use strict';
  const ask = (text) => window.confirm(text);

  document.addEventListener('submit', (e) => {
    const el = e.target instanceof HTMLFormElement ? e.target : null;
    const text = el?.dataset.confirm ?? e.submitter?.dataset.confirm;
    if (text && !ask(text)) e.preventDefault();
  });

  document.addEventListener('change', (e) => {
    const el = e.target;
    if (el instanceof HTMLElement && el.hasAttribute('data-autosubmit') && el.closest('form')) el.closest('form').requestSubmit();
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
  });
})();
