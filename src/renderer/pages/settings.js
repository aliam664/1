import { t } from '../i18n/i18n.js';
import { escapeHtml } from '../components/cards.js';
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
  const { language, theme, reducedMotion, appInfo, settings, gameStatus } = getState();
  const gamePath = settings?.gamePath || '';
  const valid = Boolean(gameStatus?.valid);
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
        <div class="segmented" role="group">
          <button type="button" data-language="fa" aria-pressed="${language === 'fa' ? 'true' : 'false'}" data-i18n="settings.language.fa">${t('settings.language.fa')}</button>
          <button type="button" data-language="en" aria-pressed="${language === 'en' ? 'true' : 'false'}" data-i18n="settings.language.en">${t('settings.language.en')}</button>
        </div>
      </div>
      <div class="setting-row">
        <div class="setting-copy"><h3 data-i18n="settings.theme">${t('settings.theme')}</h3></div>
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
        <button class="toggle" type="button" data-reduced-motion aria-pressed="${reducedMotion ? 'true' : 'false'}">
          <span class="toggle-knob"></span>
        </button>
      </div>
    </section>

    <section class="card">
      <h2 data-i18n="settings.game">${t('settings.game')}</h2>
      <p data-i18n="settings.game.help">${t('settings.game.help')}</p>
      <div class="setting-row">
        <input class="field ltr-isolate" data-game-path value="${escapeHtml(gamePath)}" spellcheck="false">
      </div>
      <p class="badge ${valid ? '' : 'badge-warn'}">${valid ? t('settings.game.valid') : t('settings.game.invalid')}</p>
      <div class="card-actions">
        <button class="btn" type="button" data-action="detect-game">${t('settings.game.detect')}</button>
        <button class="btn" type="button" data-action="browse-game">${t('settings.game.browse')}</button>
        <button class="btn btn-primary" type="button" data-action="save-game">${t('common.save')}</button>
      </div>
    </section>

    <section class="card">
      <h2 data-i18n="settings.downloads">${t('settings.downloads')}</h2>
      <div class="setting-row">
        <div class="setting-copy"><h3 data-i18n="settings.concurrency">${t('settings.concurrency')}</h3></div>
        <div class="segmented">
          ${[1, 2, 3, 4].map((n) => `<button type="button" data-concurrency="${n}" aria-pressed="${(settings?.maxConcurrentDownloads || 2) === n ? 'true' : 'false'}">${n}</button>`).join('')}
        </div>
      </div>
    </section>

    <section class="card">
      <h2 data-i18n="settings.archive">${t('settings.archive')}</h2>
      <p data-i18n="settings.archive.help">${t('settings.archive.help')}</p>
      <div class="setting-row">
        <input class="field ltr-isolate" data-tool-path value="${escapeHtml(settings?.externalToolPath || '')}" spellcheck="false">
        <button class="btn" type="button" data-action="browse-tool">${t('settings.archive.browse')}</button>
      </div>
    </section>

    <section class="card">
      <h2 data-i18n="settings.catalog">${t('settings.catalog')}</h2>
      <p data-i18n="settings.catalog.override">${t('settings.catalog.override')}</p>
      <input class="field ltr-isolate" data-catalog-url value="${escapeHtml(settings?.catalogOverrideUrl || '')}" spellcheck="false">
      <div class="card-actions">
        <button class="btn" type="button" data-action="save-catalog">${t('common.save')}</button>
      </div>
    </section>

    <section class="card">
      <h2 data-i18n="settings.about">${t('settings.about')}</h2>
      <div class="meta-list">
        <div class="meta-row"><span data-i18n="settings.version">${t('settings.version')}</span><bdi>${appInfo?.version ?? '1.0.0'}</bdi></div>
        <div class="meta-row"><span data-i18n="settings.channel">${t('settings.channel')}</span><bdi>${appInfo?.channel ?? 'stable'}</bdi></div>
        <div class="meta-row"><span data-i18n="settings.runtime">${t('settings.runtime')}</span><bdi>${formatRuntime(appInfo)}</bdi></div>
        <div class="meta-row"><span data-i18n="settings.platform">${t('settings.platform')}</span><bdi>${appInfo?.platform ?? '—'}</bdi></div>
      </div>
    </section>
  `;
  return root;
}
