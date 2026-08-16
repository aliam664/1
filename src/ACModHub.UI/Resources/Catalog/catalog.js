/* AC Mod Hub Store — rendering and bridge logic.
   Security rules:
   - Remote data is rendered exclusively with textContent / createElement (never innerHTML).
   - The page can only reach the host via chrome.webview.postMessage; commands are
     schema-validated on the C# side.
   - All external navigation is handled by the host.
*/
(function () {
  "use strict";

  var DATA = window.__ACMH__ || { mods: [], strings: {}, lang: "en", dir: "ltr", source: "", warning: "" };
  var S = DATA.strings || {};
  var t = function (key, fallback) { return S[key] || fallback || key; };

  document.documentElement.lang = DATA.lang;
  document.documentElement.dir = DATA.dir;

  var state = {
    search: "",
    category: "all",
    sort: "default",
    installing: null
  };

  var els = {
    grid: document.getElementById("grid"),
    skeleton: document.getElementById("skeleton"),
    stateEmpty: document.getElementById("state-empty"),
    stateError: document.getElementById("state-error"),
    banner: document.getElementById("banner"),
    search: document.getElementById("search-input"),
    chips: document.getElementById("category-chips"),
    sort: document.getElementById("sort-select"),
    refresh: document.getElementById("refresh-btn"),
    retry: document.getElementById("retry-btn"),
    overlay: document.getElementById("progress-overlay"),
    progressText: document.getElementById("progress-text"),
    progressFill: document.getElementById("progress-fill"),
    progressCancel: document.getElementById("progress-cancel"),
    progressClose: document.getElementById("progress-close")
  };

  function applyStaticStrings() {
    document.getElementById("store-title").textContent = t("StoreTitle", "Mod Store");
    document.getElementById("source-label").textContent = DATA.source || "";
    document.querySelector(".brand-title").textContent = t("StoreTitle", "Mod Store");
    els.search.placeholder = t("StoreSearchPlaceholder", "Search…");
    els.search.setAttribute("aria-label", t("StoreSearchPlaceholder", "Search"));
    els.refresh.querySelector(".btn-label").textContent = t("StoreRefresh", "Refresh");
    els.refresh.setAttribute("aria-label", t("StoreRefresh", "Refresh"));
    els.sort.options[0].textContent = t("StoreSortDefault", "Sort: default");
    els.sort.options[1].textContent = t("StoreSortName", "Name");
    els.sort.options[2].textContent = t("StoreSortNewest", "Newest");
    els.sort.options[3].textContent = t("StoreSortVersion", "Version");
    els.sort.setAttribute("aria-label", t("StoreSortDefault", "Sort"));
    els.chips.setAttribute("aria-label", t("StoreAllCategories", "Categories"));
    els.progressCancel.textContent = t("StoreCancelDownload", "Cancel download");
    els.progressClose.textContent = t("Close", "Close");
    els.retry.textContent = t("StoreRetry", "Retry");
    if (DATA.warning) {
      els.banner.textContent = DATA.warning;
      els.banner.hidden = false;
    }
  }

  function send(command, args) {
    var payload = Object.assign({ command: command }, args || {});
    if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
      window.chrome.webview.postMessage(JSON.stringify(payload));
      return true;
    }
    return false;
  }

  function buildCategoryChips() {
    var categories = [{ id: "all", label: t("StoreAllCategories", "All") }];
    var names = {
      car: t("CategoryCar", "Car"), track: t("CategoryTrack", "Track"), skin: t("CategorySkin", "Skin"),
      app: t("CategoryApp", "App"), weather: t("CategoryWeather", "Weather"), csp: t("CategoryCsp", "CSP"),
      miscellaneous: t("CategoryMiscellaneous", "Other")
    };
    var present = {};
    (DATA.mods || []).forEach(function (m) { present[m.category] = true; });
    Object.keys(present).forEach(function (c) {
      categories.push({ id: c, label: names[c] || c });
    });
    els.chips.textContent = "";
    categories.forEach(function (category) {
      var button = document.createElement("button");
      button.type = "button";
      button.className = "chip";
      button.textContent = category.label;
      button.setAttribute("role", "button");
      button.setAttribute("aria-pressed", category.id === state.category ? "true" : "false");
      button.addEventListener("click", function () {
        state.category = category.id;
        Array.prototype.forEach.call(els.chips.children, function (child) {
          child.setAttribute("aria-pressed", "false");
        });
        button.setAttribute("aria-pressed", "true");
        render();
      });
      els.chips.appendChild(button);
    });
  }

  function formatBytes(bytes) {
    if (!bytes && bytes !== 0) return "";
    var units = ["B", "KB", "MB", "GB"];
    var value = bytes;
    var index = 0;
    while (value >= 1024 && index < units.length - 1) { value /= 1024; index++; }
    return (Math.round(value * 10) / 10) + " " + units[index];
  }

  function escapeForAttribute(value) {
    return String(value).replace(/&/g, "&amp;").replace(/"/g, "&quot;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
  }

  function buildCard(mod) {
    var card = document.createElement("article");
    card.className = "card";
    card.setAttribute("aria-label", mod.name);

    // Cover
    var cover = document.createElement("div");
    cover.className = "card-cover";
    if (mod.coverUrl) {
      var img = document.createElement("img");
      img.loading = "lazy";
      img.decoding = "async";
      img.alt = "";
      img.src = escapeForAttribute(mod.coverUrl);
      img.addEventListener("error", function () { img.remove(); showFallback(); });
      cover.appendChild(img);
    }
    var fallbackShown = false;
    function showFallback() {
      if (fallbackShown) return;
      fallbackShown = true;
      var fallback = document.createElement("div");
      fallback.className = "cover-fallback";
      fallback.textContent = mod.category.toUpperCase();
      cover.appendChild(fallback);
    }
    if (!mod.coverUrl) showFallback();

    if (mod.status === "deprecated") {
      var deprecated = document.createElement("div");
      deprecated.className = "card-badge deprecated";
      deprecated.textContent = t("StoreDeprecatedWarning", "Deprecated");
      cover.appendChild(deprecated);
    } else if (mod.status === "revoked") {
      var revoked = document.createElement("div");
      revoked.className = "card-badge revoked";
      revoked.textContent = t("StoreRevokedTitle", "Revoked");
      cover.appendChild(revoked);
    }
    card.appendChild(cover);

    // Body
    var body = document.createElement("div");
    body.className = "card-body";

    var titleRow = document.createElement("div");
    titleRow.className = "card-title-row";
    var title = document.createElement("h3");
    title.className = "card-title";
    title.textContent = mod.name;
    titleRow.appendChild(title);
    if (!mod.installable) {
      var blocked = document.createElement("span");
      blocked.className = "pill-blocked";
      blocked.textContent = t("StoreInstallBlocked", "Blocked");
      titleRow.appendChild(blocked);
    }
    body.appendChild(titleRow);

    var author = document.createElement("p");
    author.className = "card-author";
    author.textContent = mod.author || "—";
    body.appendChild(author);

    if (mod.description) {
      var description = document.createElement("p");
      description.className = "card-desc";
      description.textContent = mod.description;
      body.appendChild(description);
    }

    var meta = document.createElement("p");
    meta.className = "card-meta";
    meta.textContent = t("Category" + capitalize(mod.category), mod.category) + " · v" + mod.version + (mod.size ? " · " + formatBytes(mod.size) : "");
    body.appendChild(meta);

    if (!mod.installable && mod.blockReason) {
      var reason = document.createElement("p");
      reason.className = "card-block-reason";
      reason.textContent = mod.blockReason;
      body.appendChild(reason);
    }

    var actions = document.createElement("div");
    actions.className = "card-actions";
    var install = document.createElement("button");
    install.type = "button";
    install.className = "btn btn-primary";
    install.textContent = t("StoreInstall", "Install");
    if (mod.installable) {
      install.addEventListener("click", function () {
        if (state.installing) return;
        state.installing = mod.id;
        if (!send("installMod", { modId: mod.id })) {
          showProgress(t("StoreErrorHint", "Host unavailable"), null);
          state.installing = null;
        }
      });
    } else {
      install.disabled = true;
      install.textContent = t("StoreInstallBlocked", "Blocked");
    }
    actions.appendChild(install);
    body.appendChild(actions);
    card.appendChild(body);
    return card;
  }

  function capitalize(value) {
    return String(value).charAt(0).toUpperCase() + String(value).slice(1);
  }

  function filteredMods() {
    var mods = (DATA.mods || []).slice();
    if (state.search) {
      var needle = state.search.toLowerCase();
      mods = mods.filter(function (m) {
        return (m.name || "").toLowerCase().indexOf(needle) !== -1
          || (m.author || "").toLowerCase().indexOf(needle) !== -1;
      });
    }
    if (state.category !== "all") {
      mods = mods.filter(function (m) { return m.category === state.category; });
    }
    switch (state.sort) {
      case "name":
        mods.sort(function (a, b) { return (a.name || "").localeCompare(b.name || ""); });
        break;
      case "newest":
        mods.sort(function (a, b) { return (b.publishedAt || "").localeCompare(a.publishedAt || ""); });
        break;
      case "version":
        mods.sort(function (a, b) { return compareVersions(b.version, a.version); });
        break;
      default:
        break;
    }
    return mods;
  }

  function compareVersions(a, b) {
    var left = (a || "0.0.0").split(/[-+]/)[0].split(".").map(Number);
    var right = (b || "0.0.0").split(/[-+]/)[0].split(".").map(Number);
    for (var i = 0; i < 3; i++) {
      var l = left[i] || 0;
      var r = right[i] || 0;
      if (l !== r) return l - r;
    }
    return 0;
  }

  function showSkeleton() {
    els.skeleton.hidden = false;
    els.skeleton.textContent = "";
    for (var i = 0; i < 8; i++) {
      var card = document.createElement("div");
      card.className = "skeleton-card";
      var cover = document.createElement("div");
      cover.className = "skeleton-cover";
      var line = document.createElement("div");
      line.className = "skeleton-line";
      card.appendChild(cover);
      card.appendChild(line);
      els.skeleton.appendChild(card);
    }
  }

  function render() {
    var mods = filteredMods();
    els.grid.textContent = "";
    mods.forEach(function (mod) { els.grid.appendChild(buildCard(mod)); });
    els.stateEmpty.hidden = mods.length > 0;
    els.stateError.hidden = true;
    if (mods.length === 0) {
      var isCatalogEmpty = (DATA.mods || []).length === 0;
      els.stateEmpty.querySelector(".state-title").textContent =
        isCatalogEmpty ? t("StoreEmptyTitle", "Empty") : t("StoreSearchPlaceholder", "Nothing found");
      els.stateEmpty.querySelector(".state-hint").textContent =
        isCatalogEmpty ? t("StoreEmptyHint", "") : "";
    }
  }

  function showError(message) {
    els.grid.textContent = "";
    els.skeleton.hidden = true;
    els.stateEmpty.hidden = true;
    els.stateError.hidden = false;
    els.stateError.querySelector(".state-title").textContent = t("StoreErrorTitle", "Error");
    els.stateError.querySelector(".state-hint").textContent = message || t("StoreErrorHint", "");
  }

  function showProgress(text, percentage) {
    els.overlay.hidden = false;
    els.progressText.textContent = text || "";
    if (percentage === null || percentage === undefined) {
      els.progressFill.style.width = "0%";
    } else {
      els.progressFill.style.width = Math.max(0, Math.min(100, percentage)) + "%";
    }
  }

  function hideProgress() {
    els.overlay.hidden = true;
    state.installing = null;
  }

  /* ---------- host messages ---------- */
  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener("message", function (event) {
      var payload;
      try { payload = JSON.parse(event.data); } catch (e) { return; }
      if (!payload || typeof payload !== "object") return;
      switch (payload.kind) {
        case "download":
          if (payload.cancelable === false) {
            showProgress(payload.text, payload.percentage);
            els.progressCancel.hidden = true;
          } else {
            els.progressCancel.hidden = false;
            showProgress(payload.text, payload.percentage);
          }
          break;
        case "error":
          hideProgress();
          showError(payload.text);
          break;
        case "done":
          hideProgress();
          break;
        default:
          break;
      }
    });
  }

  /* ---------- interactions ---------- */
  els.search.addEventListener("input", function () {
    state.search = els.search.value.trim();
    render();
  });
  els.search.addEventListener("keydown", function (event) {
    if (event.key === "Escape") {
      els.search.value = "";
      state.search = "";
      render();
      els.search.blur();
    }
  });
  document.addEventListener("keydown", function (event) {
    // "/" focuses search; Escape clears it.
    if (event.key === "/" && document.activeElement !== els.search && !isTypingTarget(document.activeElement)) {
      event.preventDefault();
      els.search.focus();
      els.search.select();
    }
    if (event.key === "Escape" && !els.overlay.hidden) {
      hideProgress();
      send("cancelOperation", {});
    }
  });

  function isTypingTarget(element) {
    return element && (element.tagName === "INPUT" || element.tagName === "TEXTAREA" || element.tagName === "SELECT");
  }

  els.sort.addEventListener("change", function () {
    state.sort = els.sort.value;
    render();
  });

  els.refresh.addEventListener("click", function () { send("refreshCatalog", {}); });
  els.retry.addEventListener("click", function () { send("refreshCatalog", {}); });
  els.progressCancel.addEventListener("click", function () {
    hideProgress();
    send("cancelOperation", {});
  });
  els.progressClose.addEventListener("click", function () { hideProgress(); });

  /* ---------- boot ---------- */
  applyStaticStrings();
  buildCategoryChips();
  showSkeleton();
  // The page is always served with complete data; rendering is near-instant.
  render();
  els.skeleton.hidden = true;
})();
