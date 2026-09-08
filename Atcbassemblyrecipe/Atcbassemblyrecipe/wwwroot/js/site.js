(() => {
  const sidebarToggle = document.getElementById("sidebarToggle");
  const storageKey = "assemblyRecipeSidebar";

  if (sidebarToggle) {
    const root = document.documentElement;

    // "sidebar-open" is the only state: open means the full sidebar with its
    // labels, closed means it is out of the layout entirely and the topbar
    // hamburger is the way back. The inline script in _Layout.cshtml sets the
    // initial class before first paint.
    const syncToggle = () => {
      const isOpen = root.classList.contains("sidebar-open");
      sidebarToggle.setAttribute("aria-pressed", isOpen ? "true" : "false");
      sidebarToggle.setAttribute("aria-expanded", isOpen ? "true" : "false");
      sidebarToggle.setAttribute("aria-label", isOpen ? "Close sidebar" : "Open sidebar");
      sidebarToggle.setAttribute("title", isOpen ? "Close sidebar" : "Open sidebar");
    };

    syncToggle();

    sidebarToggle.addEventListener("click", () => {
      const isOpen = root.classList.toggle("sidebar-open");
      localStorage.setItem(storageKey, isOpen ? "open" : "closed");
      syncToggle();
    });
  }

  const zoomStorageKey = "assemblyRecipeZoom";
  const clampZoom = (value) => Math.min(1.35, Math.max(0.8, value));
  const readZoom = () => {
    const stored = Number.parseFloat(localStorage.getItem(zoomStorageKey) || "1");
    return Number.isFinite(stored) ? clampZoom(stored) : 1;
  };

  // The spreadsheet grids scroll inside their own panel so the sticky header
  // has something to stick to (see the frozen-header block in site.css). The
  // height that makes that work is "whatever is left of the window below the
  // panel", which only JavaScript can measure.
  const gridPanels = Array.from(document.querySelectorAll(".excel-panel"));

  const sizeGridPanels = () => {
    if (!gridPanels.length) {
      return;
    }

    const zoom = readZoom();

    // The panel is measured against the topbar rather than against its own
    // position on the page. Sizing it to "what is left below me" would leave
    // a five-row window once the heading and the toolbar have had their share
    // of a laptop screen; this way the page scrolls the heading away first and
    // the panel then fills the window, its header pinned clear of the topbar.
    const topbar = document.querySelector(".topbar");
    const topbarHeight = topbar ? topbar.getBoundingClientRect().height : 108;

    gridPanels.forEach((panel) => {
      const pagination = panel.parentElement?.querySelector(".pagination-bar");
      const reserved = topbarHeight + (pagination?.getBoundingClientRect().height ?? 0) + 42;
      // getBoundingClientRect and innerHeight are on-screen pixels while the
      // panel lays itself out inside the zoomed shell, so the zoom has to come
      // back out before this is written as a CSS length.
      const available = (window.innerHeight - reserved) / zoom;

      panel.style.setProperty("--grid-max-height", `${Math.max(260, Math.round(available))}px`);
    });
  };

  const zoomOutButtons = document.querySelectorAll("[data-zoom-out]");
  const zoomInButtons = document.querySelectorAll("[data-zoom-in]");
  const zoomResetButtons = document.querySelectorAll("[data-zoom-reset]");
  const zoomValues = document.querySelectorAll("[data-zoom-value]");

  if (zoomOutButtons.length && zoomInButtons.length && zoomResetButtons.length && zoomValues.length) {
    const applyZoom = (value) => {
      const nextZoom = clampZoom(value);
      document.documentElement.style.setProperty("--app-zoom", nextZoom.toString());
      localStorage.setItem(zoomStorageKey, nextZoom.toString());
      zoomValues.forEach((zoomValue) => {
        zoomValue.value = `${Math.round(nextZoom * 100)}%`;
        zoomValue.textContent = zoomValue.value;
      });
      // The grid height is measured against the window, so it is stale the
      // moment the zoom changes.
      sizeGridPanels();
    };

    applyZoom(readZoom());

    zoomOutButtons.forEach((button) => button.addEventListener("click", () => applyZoom(readZoom() - 0.05)));
    zoomInButtons.forEach((button) => button.addEventListener("click", () => applyZoom(readZoom() + 0.05)));
    zoomResetButtons.forEach((button) => button.addEventListener("click", () => applyZoom(1)));
  }

  if (gridPanels.length) {
    sizeGridPanels();

    let resizeFrame = 0;
    window.addEventListener("resize", () => {
      window.cancelAnimationFrame(resizeFrame);
      resizeFrame = window.requestAnimationFrame(sizeGridPanels);
    });

    // Re-measured once everything above the panel has settled: the logo and
    // the web fonts both land after this script runs.
    window.addEventListener("load", sizeGridPanels);
  }

  const backToTop = document.getElementById("backToTop");

  if (backToTop) {
    // A page with a pagination bar keeps the button inside it. Floating at the
    // bottom-right corner put it straight on top of the Next link, because on
    // a grid page the pagination bar is pinned to the bottom of the window at
    // every row count. Seated in the bar it covers nothing, and it stays in
    // one place for the user to aim at rather than fading in and out.
    const paginationBar = document.querySelector(".pagination-bar");

    if (paginationBar) {
      paginationBar.appendChild(backToTop);
      backToTop.classList.add("is-inline", "is-visible");
    }

    // Two things can be scrolled on a grid page: the window, and the grid
    // panel itself. The button watches and rewinds both.
    const scrolledAway = () =>
      window.scrollY > 200 || gridPanels.some((panel) => panel.scrollTop > 200);

    const syncBackToTop = () => {
      if (backToTop.classList.contains("is-inline")) {
        return;
      }

      backToTop.classList.toggle("is-visible", scrolledAway());
    };

    window.addEventListener("scroll", syncBackToTop, { passive: true });
    gridPanels.forEach((panel) => panel.addEventListener("scroll", syncBackToTop, { passive: true }));

    backToTop.addEventListener("click", () => {
      const behavior = window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth";
      gridPanels.forEach((panel) => panel.scrollTo({ top: 0, behavior }));
      window.scrollTo({ top: 0, behavior });
    });

    syncBackToTop();
  }
})();
