import { t } from '../i18n/i18n.js';
import { getState } from '../state/store.js';

function formatRuntime(info) {
  if (!info) {
    return '—';
  }
  if (info.electron) {
    return `Electron ${info.electron}`;
  }
  return 'Web preview';
}

export function renderSettings() {
  const { language, theme, reducedMotion, appInfo } = getState();
  const root = document.createElement('div');
  root.className = 'page-stack';
  root.innerHTML = `
    <section class="card">
      <h2 data-i18n="settings.appearance">${t('settings.appearance')}</h2>
      <div class="setting-row">
        <div class="setting-copy">
          <h3 data-i18n="settings.language">${t('settings.language')}</h3>
          <p data-i18n="settings.language.help">${t('settings.language.help')}</p>
        </div>
        <div class="segmented" role="group" aria-labelledby="lang-label">
          <span id="lang-label" hidden data-i18n="settings.language">${t('settings.language')}</span>
          <button type="button" data-language="fa" aria-pressed="${language === 'fa' ? 'true' : 'false'}" data-i18n="settings.language.fa">${t('settings.language.fa')}</button>
          <button type="button" data-language="en" aria-pressed="${language === 'en' ? 'true' : 'false'}" data-i18n="settings.language.en">${t('settings.language.en')}</button>
        </div>
      </div>
      <div class="setting-row">
        <div class="setting-copy">
          <h3 data-i18n="settings.theme">${t('settings.theme')}</h3>
        </div>
        <div class="segmented" role="group">
          <button type="button" data-theme="dark" aria-pressed="${theme === 'dark' ? 'true' : 'false'}" data-i18n="settings.theme.dark">${t('settings.theme.dark')}</button>
          <button type="button" data-theme="light" aria-pressed="${theme === 'light' ? 'true' : 'false'}" data-i18n="settings.theme.light">${t('settings.theme.light')}</button>
        </div>
      </div>
      <div class="setting-row">
        <div class="setting-copy">
          <h3 data-i18n="settings.reducedMotion">${t('settings.reducedMotion')}</h3>
          <p data-i18n="settings.reducedMotion.help">${t('settings.reducedMotion.help')}</p>
        </div>
        <button class="toggle" type="button" data-reduced-motion aria-pressed="${reducedMotion ? 'true' : 'false'}" data-i18n-aria="settings.reducedMotion">
          <span class="toggle-knob"></span>
        </button>
      </div>
    </section>

    <section class="card">
      <h2 data-i18n="settings.about">${t('settings.about')}</h2>
      <div class="meta-list">
        <div class="meta-row">
          <span data-i18n="settings.version">${t('settings.version')}</span>
          <bdi class="value">${appInfo?.version ?? '1.0.0'}</bdi>
        </div>
        <div class="meta-row">
          <span data-i18n="settings.channel">${t('settings.channel')}</span>
          <bdi class="value">${appInfo?.channel ?? 'stable'}</bdi>
        </div>
        <div class="meta-row">
          <span data-i18n="settings.runtime">${t('settings.runtime')}</span>
          <bdi class="value">${formatRuntime(appInfo)}</bdi>
        </div>
        <div class="meta-row">
          <span data-i18n="settings.platform">${t('settings.platform')}</span>
          <bdi class="value">${appInfo?.platform ?? '—'}</bdi>
        </div>
      </div>
    </section>

    <section class="card">
      <h2 data-i18n="settings.coming">${t('settings.coming')}</h2>
      <ul class="coming-list">
        <li data-i18n="settings.coming.catalog">${t('settings.coming.catalog')}</li>
        <li data-i18n="settings.coming.game">${t('settings.coming.game')}</li>
        <li data-i18n="settings.coming.archive">${t('settings.coming.archive')}</li>
        <li data-i18n="settings.coming.downloads">${t('settings.coming.downloads')}</li>
      </ul>
    </section>
  `;
  return root;
}
