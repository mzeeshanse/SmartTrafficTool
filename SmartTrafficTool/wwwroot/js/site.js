// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

document.addEventListener("DOMContentLoaded", () => {
  const appShell = document.getElementById("appShell");
  const sidebar = document.getElementById("appSidebar");
  const sidebarToggle = document.querySelector(".sidebar-toggle");
  const topbarMenuToggle = document.querySelector(".topbar-menu-toggle");
  const sidebarToggleIcon = document.querySelector(".sidebar-toggle-icon");

  const mqMobile = window.matchMedia("(max-width: 992px)");

  function isMobileNav() {
    return mqMobile.matches;
  }

  function syncSidebarUi() {
    if (!sidebar || !sidebarToggle) return;

    if (isMobileNav()) {
      appShell?.classList.remove("sidebar-narrow");
      const open = !sidebar.classList.contains("collapsed");
      sidebarToggle.setAttribute("aria-expanded", open ? "true" : "false");
      sidebarToggle.title = open ? "Close menu" : "Open menu";
      if (sidebarToggleIcon) {
        sidebarToggleIcon.className = open
          ? "fa-solid fa-xmark sidebar-toggle-icon"
          : "fa-solid fa-bars sidebar-toggle-icon";
      }
    } else {
      sidebar.classList.remove("collapsed");
      const narrow = appShell?.classList.contains("sidebar-narrow");
      sidebarToggle.setAttribute("aria-expanded", narrow ? "false" : "true");
      sidebarToggle.title = narrow ? "Expand menu" : "Collapse menu";
      if (sidebarToggleIcon) {
        sidebarToggleIcon.className = narrow
          ? "fa-solid fa-angles-right sidebar-toggle-icon"
          : "fa-solid fa-angles-left sidebar-toggle-icon";
      }
    }
  }

  function applyStoredDesktopNarrow() {
    if (!appShell || isMobileNav()) return;
    try {
      if (localStorage.getItem("st-sidebar-narrow") === "1") {
        appShell.classList.add("sidebar-narrow");
      } else {
        appShell.classList.remove("sidebar-narrow");
      }
    } catch {
      /* ignore */
    }
  }

  let resizeTimer;
  window.addEventListener("resize", () => {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(() => {
      applyStoredDesktopNarrow();
      syncSidebarUi();
    }, 120);
  });

  mqMobile.addEventListener("change", () => {
    applyStoredDesktopNarrow();
    syncSidebarUi();
  });

  sidebarToggle?.addEventListener("click", (e) => {
    e.preventDefault();
    if (!sidebar || !appShell) return;
    if (isMobileNav()) {
      sidebar.classList.toggle("collapsed");
    } else {
      appShell.classList.toggle("sidebar-narrow");
      try {
        localStorage.setItem(
          "st-sidebar-narrow",
          appShell.classList.contains("sidebar-narrow") ? "1" : "0"
        );
      } catch {
        /* ignore */
      }
    }
    syncSidebarUi();
  });

  topbarMenuToggle?.addEventListener("click", () => {
    if (!sidebar || !isMobileNav()) return;
    sidebar.classList.remove("collapsed");
    syncSidebarUi();
  });

  applyStoredDesktopNarrow();
  syncSidebarUi();

  const filterToggle = document.querySelector("[data-toggle='filters']");
  const filterPanel = document.querySelector(".filter-panel");
  if (filterToggle && filterPanel) {
    filterToggle.addEventListener("click", () => {
      filterPanel.classList.toggle("active");
    });
  }
});
