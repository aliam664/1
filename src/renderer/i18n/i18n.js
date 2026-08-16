import fa from './fa.json' with { type: 'json' };
import en from './en.json' with { type: 'json' };

const dictionaries = Object.freeze({
  fa,
  en
});

let currentLanguage = 'fa';

/**
 * @param {'fa' | 'en'} language
 */
export function loadLanguage(language) {
  if (!dictionaries[language]) {
    throw new Error(`Unknown language: ${language}`);
  }
  currentLanguage = language;
  return dictionaries[language];
}

/**
 * @param {string} key
 * @returns {string}
 */
export function t(key) {
  const dict = dictionaries[currentLanguage] || dictionaries.fa;
  const value = dict[key];
  return typeof value === 'string' ? value : key;
}

export function getLanguage() {
  return currentLanguage;
}

export function directionFor(language) {
  return language === 'fa' ? 'rtl' : 'ltr';
}

export function listLanguages() {
  return Object.keys(dictionaries);
}

/**
 * Apply translations to every [data-i18n] node under root.
 * @param {ParentNode} [root]
 */
export function applyTranslations(root = document) {
  root.querySelectorAll('[data-i18n]').forEach((node) => {
    const key = node.getAttribute('data-i18n');
    if (key) {
      node.textContent = t(key);
    }
  });
  root.querySelectorAll('[data-i18n-aria]').forEach((node) => {
    const key = node.getAttribute('data-i18n-aria');
    if (key) {
      node.setAttribute('aria-label', t(key));
    }
  });
  root.querySelectorAll('[data-i18n-title]').forEach((node) => {
    const key = node.getAttribute('data-i18n-title');
    if (key) {
      node.setAttribute('title', t(key));
    }
  });
  root.querySelectorAll('[data-i18n-placeholder]').forEach((node) => {
    const key = node.getAttribute('data-i18n-placeholder');
    if (key) {
      node.setAttribute('placeholder', t(key));
    }
  });
}

/**
 * Flattened key lists used by the parity test and runtime sanity checks.
 */
export function dictionaryKeys(language) {
  return Object.keys(dictionaries[language] || {});
}
