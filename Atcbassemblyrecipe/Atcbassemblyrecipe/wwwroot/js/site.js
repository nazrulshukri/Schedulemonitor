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

  // The grid header is sticky against the window, pinned under the topbar,
  // so the CSS needs the topbar's real height - it is 108px at desktop
  // widths but collapses to its content below 780px. The topbar is outside
  // .app-shell and so outside the zoom, which is why this is written raw and
  // the stylesheet divides the zoom back out.
  const measureTopbar = () => {
    const topbar = document.querySelector(".topbar");
    if (topbar) {
      document.documentElement.style.setProperty("--topbar-height", `${Math.round(topbar.getBoundingClientRect().height)}px`);
    }
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
      measureTopbar();
    };

    applyZoom(readZoom());

    zoomOutButtons.forEach((button) => button.addEventListener("click", () => applyZoom(readZoom() - 0.05)));
    zoomInButtons.forEach((button) => button.addEventListener("click", () => applyZoom(readZoom() + 0.05)));
    zoomResetButtons.forEach((button) => button.addEventListener("click", () => applyZoom(1)));
  }

  measureTopbar();

  let resizeFrame = 0;
  window.addEventListener("resize", () => {
    window.cancelAnimationFrame(resizeFrame);
    resizeFrame = window.requestAnimationFrame(measureTopbar);
  });

  // Re-measured once the logo and the web fonts have landed, both of which
  // arrive after this script runs and can change the topbar's height.
  window.addEventListener("load", measureTopbar);

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

    const scrolledAway = () => window.scrollY > 200;

    const syncBackToTop = () => {
      if (backToTop.classList.contains("is-inline")) {
        return;
      }

      backToTop.classList.toggle("is-visible", scrolledAway());
    };

    window.addEventListener("scroll", syncBackToTop, { passive: true });

    backToTop.addEventListener("click", () => {
      window.scrollTo({
        top: 0,
        behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth"
      });
    });

    syncBackToTop();
  }
})();
