# AC Mod Hub 2.0 — UI/UX Research & Design Decisions

This document records the design research performed **before** the 2.0 redesign and the
decisions derived from it. It is the source of truth for the visual language implemented in
`src/ACModHub.UI/Resources/Theme.xaml` and `src/ACModHub.UI/Resources/Catalog/`.

Sources reviewed (public documentation, no copyrighted assets copied):

1. **Fluent 2 Design System** (Microsoft) — layered elevation model, neutral surfaces, accent
   color usage, density guidance, focus treatment, motion guidance.
2. **Microsoft Xbox Accessibility Guidelines (XAG)** — contrast, focus, navigation, error
   messaging, motion/reduced-motion guidance.
3. **Playnite, Steam, Xbox App and other modern game launchers** — navigation, library cards,
   download queue UX, update banners, empty/loading states.
4. **Motorsport dashboards** (visual identity only) — restrained use of a motorsport accent for
   brand identity, not for decoration.

## 1. What we keep from Fluent 2

| Fluent 2 principle | Applied as |
| --- | --- |
| Layered elevation | Four surface tokens: `Surface`, `RaisedSurface`, `ElevatedSurface`, over `Background`/`Sidebar` (see Design Tokens below). |
| Restrained accent | The motorsport red is reserved for the **primary action** and **danger states only**. Never used for decoration or large surfaces. |
| Neutral surfaces | Cards are neutral dark; color is used only for semantic states (verified/warning/error). |
| Focus is always visible | `FocusVisualStyle` draws a 2px mint (`Safe`) outline around every focusable control, plus default keyboard focus ring. |
| Motion for feedback only | No persistent/pulsing animations. Hover/press feedback only, ≤ 220 ms, disabled entirely under `ReducedMotion`. |
| Corner radius | Controls 7–10 px (8px default), cards 10px. |
| 8-px grid | All spacing multiples of 4, layout grid multiples of 8. |

## 2. What we keep from Xbox Accessibility Guidelines

- Normal text contrast ≥ **4.5:1** against its surface. Primary text `#F2F5FA` on `#101722`
  ≈ 15:1; secondary text `#96A2B5` on `#101722` ≈ 7:1; subtle text `#68758A` is used only for
  non-essential metadata and never for required information.
- **Color is never the only indicator**: every state also carries text and/or an icon
  (e.g. verified mint pill contains a check icon + the word "Verified/تأییدشده").
- Minimum interactive target **40 px**, 44 px for primary actions.
- Errors always include an icon and a human sentence — never a raw exception string on screen.
- Reduced-motion setting removes transitions entirely.
- Destructive actions require explicit confirmation and explain consequences (uninstall flow,
  update restart).

## 3. What we learned from Playnite / Steam / Xbox App

| Pattern | Decision |
| --- | --- |
| Persistent left navigation with section headers | New shell groups navigation into **DISCOVER / LIBRARY / SYSTEM** with an active indicator rail. |
| Keyboard-first store search | Store gains `/` to focus search, `Esc` to clear/close. |
| Download queue as first-class citizen | Downloads page shows queue with real progress, speed, ETA, pause/resume/retry/cancel. |
| Non-blocking update check | Update banner appears after the window is shown; "Later / View release / Download & install" actions. |
| Skeleton + empty + offline + error states | Every page has explicit localized states; the store renders skeletons before data arrives. |
| Detail pane in library | Library uses a two-pane layout: virtualized list + selected-mod detail panel. |

## 4. Final direction: "Fluent 2 Dark + Premium Motorsport"

- Dark, calm, layered surfaces. No permanent neon, no heavy glow, no busy gradients, no
  animated backgrounds, no over-colored cards.
- One motorsport accent (`#DE4058`) used sparingly: primary buttons, active nav indicator,
  destructive actions.
- One "safe" mint (`#63DFC7`) used for: verified/ready states and focus.
- Icons: single consistent vector set (Path geometry in WPF; inline SVG paths in the store
  HTML), **no mixed Unicode emoji icons**.
- Typography: one family (Segoe UI Variable on Win11, Segoe UI fallback), one heading scale
  (page 28 / section 17 / card 14), body 14, caption 12; consistent line heights.
- RTL-first: Persian is the default; English is LTR. Every visible string is localized.

## 5. Design Tokens (single source of truth)

Defined once in `Theme.xaml` as `Color` + `SolidColorBrush` resources and mirrored as CSS
variables in `catalog.css`. All pages, the shell, and the HTML store consume only these tokens.

| Token | Value | Role |
| --- | --- | --- |
| Background | `#080B11` | Window background |
| Sidebar | `#0C1119` | Navigation / title bar |
| Surface | `#101722` | Cards, panels |
| RaisedSurface | `#161E2A` | Inputs, interactive cards |
| ElevatedSurface | `#1B2533` | Popovers, detail panels |
| Border | `#263244` | Default hairlines |
| StrongBorder | `#3A485C` | Focus-adjacent / emphasized borders |
| PrimaryText | `#F2F5FA` | Headings, body |
| SecondaryText | `#96A2B5` | Labels, metadata |
| SubtleText | `#68758A` | Captions (non-essential) |
| MotorsportPrimary | `#DE4058` | Primary action, danger, active nav |
| PrimaryHover | `#EC5269` | Hover of primary |
| Safe | `#63DFC7` | Verified / ready / focus |
| Warning | `#F0B35C` | Warnings |
| Error | `#F06478` | Errors |

Rules:

- Motorsport red only for primary action and danger states.
- Mint only for verified/safe/ready/focus.
- Status always = color + text + icon.
- Normal text contrast ≥ 4.5:1.
- Focus = visible 2px border (mint).
- Spacing on the 8-px grid.
- Radius 7–10 px for controls.
- Interactive control height ≥ 40 px (44 px for primary).
- Motion only 150–220 ms feedback; disabled with Reduced Motion; High Contrast considered
  (all state communication is text+icon, never color-only).

## 6. Known deviations / notes

- High-contrast system themes: our custom dark palette remains, but because every state also
  carries text/icon and focus uses a border (not a color fill alone), information survives
  high-contrast rendering. Full system-theme mirroring is tracked in `docs/AUDIT-2.0.md`.
- No assets, artwork, or layout were copied from any copyrighted product; the "premium
  motorsport" direction is expressed purely through typography, spacing, and the documented
  tokens above.
