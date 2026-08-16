/**
 * Single consistent outline icon set. No emoji.
 */

const svg = (path) =>
  `<svg class="nav-icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
    <path fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" d="${path}"/>
  </svg>`;

export const icons = {
  home: svg('M4 10.5 12 4l8 6.5V20a1 1 0 0 1-1 1h-5v-6H10v6H5a1 1 0 0 1-1-1z'),
  cars: svg('M4 15.5h16M5.5 15.5l1.4-5.2A2 2 0 0 1 8.8 9h6.4a2 2 0 0 1 1.9 1.3L18.5 15.5M7 18.5a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3zm10 0a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3zM8 9l1-3h6l1 3'),
  tracks: svg('M5 19c3-8 11-8 14 0M8 8.5a4 4 0 1 1 8 0c0 2.4-2 4.2-4 6.5-2-2.3-4-4.1-4-6.5z'),
  mods: svg('M12 3v4M12 17v4M3 12h4M17 12h4M6.2 6.2l2.8 2.8M15 15l2.8 2.8M17.8 6.2 15 9M9 15l-2.8 2.8'),
  favorites: svg('M12 19 5.6 13.2A4.2 4.2 0 0 1 12 7.2a4.2 4.2 0 0 1 6.4 6L12 19z'),
  downloads: svg('M12 4v10m0 0 4-4m-4 4-4-4M5 19h14'),
  updates: svg('M20 12a8 8 0 1 1-2.3-5.6M20 5v5h-5'),
  settings: svg('M12 15.2A3.2 3.2 0 1 0 12 8.8a3.2 3.2 0 0 0 0 6.4zM19.4 13a7.7 7.7 0 0 0 .1-2l2-1.5-2-3.4-2.4.6a7.6 7.6 0 0 0-1.7-1L15 3h-6l-.4 2.7a7.6 7.6 0 0 0-1.7 1L4.5 6.1l-2 3.4L4.5 11a7.7 7.7 0 0 0 .1 2l-2 1.5 2 3.4 2.4-.6a7.6 7.6 0 0 0 1.7 1L9 21h6l.4-2.7a7.6 7.6 0 0 0 1.7-1l2.4.6 2-3.4z')
};

export const heroMark = `
<svg class="hero-art" viewBox="0 0 300 168" aria-hidden="true" focusable="false">
  <defs>
    <linearGradient id="sc-line" x1="0" x2="1" y1="0" y2="1">
      <stop offset="0" stop-color="#de4058"/>
      <stop offset="1" stop-color="#63dfc7"/>
    </linearGradient>
  </defs>
  <rect x="12" y="22" width="276" height="124" rx="22" fill="#10151e" stroke="#273140"/>
  <path d="M36 118c42-52 78-52 118 4s86 40 140-16" fill="none" stroke="url(#sc-line)" stroke-width="8" stroke-linecap="round"/>
  <path d="M52 94c28-24 54-22 80 6" fill="none" stroke="#f2f5fa" stroke-opacity=".16" stroke-width="4" stroke-linecap="round"/>
  <circle cx="236" cy="58" r="9" fill="#de4058"/>
  <circle cx="236" cy="58" r="16" fill="none" stroke="#de4058" stroke-opacity=".28"/>
</svg>`;
