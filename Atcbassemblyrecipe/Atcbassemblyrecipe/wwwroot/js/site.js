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
  const zoomOutButtons = document.querySelectorAll("[data-zoom-out]");
  const zoomInButtons = document.querySelectorAll("[data-zoom-in]");
  const zoomResetButtons = document.querySelectorAll("[data-zoom-reset]");
  const zoomValues = document.querySelectorAll("[data-zoom-value]");

  if (!zoomOutButtons.length || !zoomInButtons.length || !zoomResetButtons.length || !zoomValues.length) {
    return;
  }

  const clampZoom = (value) => Math.min(1.35, Math.max(0.8, value));
  const readZoom = () => {
    const stored = Number.parseFloat(localStorage.getItem(zoomStorageKey) || "1");
    return Number.isFinite(stored) ? clampZoom(stored) : 1;
  };

  const applyZoom = (value) => {
    const nextZoom = clampZoom(value);
    document.documentElement.style.setProperty("--app-zoom", nextZoom.toString());
    localStorage.setItem(zoomStorageKey, nextZoom.toString());
    zoomValues.forEach((zoomValue) => {
      zoomValue.value = `${Math.round(nextZoom * 100)}%`;
      zoomValue.textContent = zoomValue.value;
    });
  };

  applyZoom(readZoom());

  zoomOutButtons.forEach((button) => button.addEventListener("click", () => applyZoom(readZoom() - 0.05)));
  zoomInButtons.forEach((button) => button.addEventListener("click", () => applyZoom(readZoom() + 0.05)));
  zoomResetButtons.forEach((button) => button.addEventListener("click", () => applyZoom(1)));
})();
