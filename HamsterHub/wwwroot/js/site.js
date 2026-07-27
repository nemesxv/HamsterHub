document.addEventListener("DOMContentLoaded", () => {
  const themeButton = document.querySelector("[data-theme-toggle]");
  themeButton?.addEventListener("click", () => {
    const nextTheme = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
    document.documentElement.dataset.theme = nextTheme;
    localStorage.setItem("hamsterhub-theme", nextTheme);
  });

  const openDialog = window.hamsterHubDialog;
  const modalId = openDialog === "login"
    ? "loginModal"
    : openDialog === "signup"
      ? "signupModal"
      : null;

  if (modalId) {
    const modalElement = document.getElementById(modalId);
    if (modalElement) {
      bootstrap.Modal.getOrCreateInstance(modalElement).show();
    }
  }

  document.querySelectorAll("[data-switch-modal]").forEach((button) => {
    button.addEventListener("click", () => {
      const currentModal = button.closest(".modal");
      const nextModal = document.getElementById(button.dataset.switchModal);

      if (!currentModal || !nextModal) {
        return;
      }

      const currentInstance = bootstrap.Modal.getOrCreateInstance(currentModal);
      currentModal.addEventListener("hidden.bs.modal", () => {
        bootstrap.Modal.getOrCreateInstance(nextModal).show();
      }, { once: true });
      currentInstance.hide();
    });
  });

  document.querySelectorAll("form[data-confirm]").forEach((form) => {
    form.addEventListener("submit", (event) => {
      if (!window.confirm(form.dataset.confirm)) {
        event.preventDefault();
      }
    });
  });
});
