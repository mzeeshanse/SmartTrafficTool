/**
 * Traffic Monitoring Hub: exclusive accordion for .command-c2-panel inside .command-c2-layers-stack.
 */
(function () {
  "use strict";

  function wireExclusiveLayerPanels(stack) {
    if (!stack) return;
    var panels = stack.querySelectorAll(".command-c2-panel");
    if (!panels.length) return;
    panels.forEach(function (panel) {
      panel.addEventListener("toggle", function () {
        if (!panel.open) return;
        panels.forEach(function (other) {
          if (other !== panel) {
            other.removeAttribute("open");
          }
        });
      });
    });
  }

  function init() {
    wireExclusiveLayerPanels(document.querySelector(".command-c2-layers-stack"));
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})();
