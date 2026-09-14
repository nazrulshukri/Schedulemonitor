(() => {
  const storageKey = "assemblyRecipeTheme";

  const applyTheme = (theme) => {
    const nextTheme = theme === "dark" ? "dark" : "light";
    document.documentElement.dataset.theme = nextTheme;
    localStorage.setItem(storageKey, nextTheme);

    document.querySelectorAll("#themeToggle").forEach((button) => {
      button.setAttribute("aria-pressed", nextTheme === "dark" ? "true" : "false");
      button.setAttribute("title", nextTheme === "dark" ? "Switch to light mode" : "Switch to dark mode");
      button.setAttribute("aria-label", nextTheme === "dark" ? "Switch to light mode" : "Switch to dark mode");
    });
  };

  applyTheme(localStorage.getItem(storageKey) || document.documentElement.dataset.theme);

  document.querySelectorAll("#themeToggle").forEach((button) => {
    button.addEventListener("click", () => {
      const currentTheme = document.documentElement.dataset.theme === "dark" ? "dark" : "light";
      applyTheme(currentTheme === "dark" ? "light" : "dark");
    });
  });
})();
