/**
 * Traffic Monitoring Hub map: device markers, ANPR alert/violation/transaction heatmaps (no incident dot markers), dispatch.
 * Transaction heatmap: green (low) → yellow/orange → red (high density).
 * Requires: google.maps (with visualization), MapMarkerIcons, window.commandControlConfig.mapDataUrl
 * Optional 3D: WebGL vector map (renderingType VECTOR) with tilt/heading when supported by the loaded API.
 */
(function () {
  "use strict";

  let map;
  let markers = [];
  let markerClusterer = null;
  let heatmapAlerts = null;
  let heatmapViolations = null;
  let heatmapTransactions = null;
  let lastHeatmapRangeLabel = "Last 48 hours";
  let lastHeatmapWindowSpanHours = 48;
  const c2MaxHeatmapFromHours = 168;
  let lastDeviceIdsOnMap = new Set();
  let lastDevices = [];
  let lastIncidentAlertSites = [];
  let lastIncidentViolationSites = [];
  const deviceMarkerByDeviceId = new Map();
  const lastAlertByDeviceId = new Map();
  const lastViolByDeviceId = new Map();
  let simulateLiveTimerId = null;
  /** Trigger simulated popup / live demo: only these plates are used */
  const C2_SIMULATE_PLATES = ["123456", "112233", "445566"];
  let infoWindow = null;
  let pulseInterval = null;
  let lastMergedHeatSites = [];
  let lastHeatHoverMapEvent = null;
  let heatHoverRafId = 0;
  /** Screen pixels: hit radius scales to ground distance via meters-per-pixel at cursor latitude. */
  const C2_HEAT_HOVER_PX = 56;
  /** At this zoom and tighter, hover uses per-camera counts. Zoomed out farther → sums nearby cameras per heat cell. */
  const C2_HOVER_CLUSTER_MAX_ZOOM = 14;

  let c2HeatHoverClusterCache = {
    zoom: null,
    siteLen: null,
    cellM: null,
    centerKey: null,
    rows: null
  };

  const dohaCenter = { lat: 25.2854, lng: 51.531 };
  /** Target tilt when “3D oblique view” is on; API clamps by zoom (re-applied on zoom_changed). */
  const C2_OBLIQUE_TILT = 67.5;
  const C2_OBLIQUE_HEADING = 24;

  /** Dark command-center roadmap (POI/transit clutter off). Pairs with native ColorScheme.DARK when available. */
  const commandControlMapStyles = [
    { elementType: "geometry", stylers: [{ color: "#030711" }] },
    { elementType: "labels.text.stroke", stylers: [{ color: "#030711" }] },
    { elementType: "labels.text.fill", stylers: [{ color: "#7c8ca3" }] },
    { featureType: "administrative", elementType: "geometry", stylers: [{ color: "#121a2a" }] },
    { featureType: "administrative", elementType: "labels.text.fill", stylers: [{ visibility: "off" }] },
    { featureType: "landscape", elementType: "geometry", stylers: [{ color: "#040a14" }] },
    { featureType: "poi", elementType: "geometry", stylers: [{ color: "#060d18" }] },
    { featureType: "poi", elementType: "labels", stylers: [{ visibility: "off" }] },
    { featureType: "poi", elementType: "labels.icon", stylers: [{ visibility: "off" }] },
    { featureType: "poi.business", stylers: [{ visibility: "off" }] },
    { featureType: "road", elementType: "geometry", stylers: [{ color: "#0f1624" }] },
    { featureType: "road.highway", elementType: "geometry", stylers: [{ color: "#1a2436" }] },
    { featureType: "road.arterial", elementType: "geometry", stylers: [{ color: "#121b2c" }] },
    { featureType: "road.local", elementType: "geometry", stylers: [{ color: "#0c121e" }] },
    { featureType: "transit", elementType: "geometry", stylers: [{ color: "#080f1a" }] },
    { featureType: "transit", elementType: "labels", stylers: [{ visibility: "off" }] },
    { featureType: "transit", elementType: "labels.icon", stylers: [{ visibility: "off" }] },
    { featureType: "water", elementType: "geometry", stylers: [{ color: "#020810" }] },
    { featureType: "water", elementType: "labels.text.fill", stylers: [{ visibility: "off" }] },
    { featureType: "road", elementType: "labels.text.fill", stylers: [{ color: "#8b9bb3" }] },
    { featureType: "road", elementType: "labels.icon", stylers: [{ visibility: "off" }] },
    { featureType: "administrative.locality", elementType: "labels.text.fill", stylers: [{ color: "#a8b8d0" }] }
  ];

  function mapDeviceCategory(type) {
    const t = (type || "").toLowerCase();
    if (t.includes("anpr") || t.includes("camera")) return "Camera";
    if (t.includes("lidar") || t.includes("radar")) return "Sensor";
    if (t.includes("environment")) return "IoT";
    return type || "Device";
  }

  function healthPresentation(status) {
    const s = (status || "").toLowerCase();
    if (s.includes("offline")) return { label: "Offline", key: "offline" };
    if (s.includes("maintenance")) return { label: "Warning", key: "warning" };
    if (s.includes("online")) return { label: "Online", key: "online" };
    return { label: status || "Unknown", key: "unknown" };
  }

  function showToast(message) {
    const el = document.getElementById("c2Toast");
    const body = document.getElementById("c2ToastBody");
    if (!el || !body || typeof bootstrap === "undefined") {
      window.alert(message);
      return;
    }
    body.textContent = message;
    const t = bootstrap.Toast.getOrCreateInstance(el, { delay: 3800 });
    t.show();
  }

  function formatIncidentTime(iso) {
    if (!iso) return "";
    try {
      const dt = new Date(iso);
      return dt.toLocaleString(undefined, { dateStyle: "short", timeStyle: "short" });
    } catch {
      return "";
    }
  }

  function c2HeatmapPointWeight(p) {
    if (typeof p.weight === "number" && p.weight > 0) return p.weight;
    if (typeof p.count === "number" && p.count > 0) return p.count;
    return 1;
  }

  function c2ToHeatmapWeightedLocations(points) {
    const WL = google.maps.visualization && google.maps.visualization.WeightedLocation;
    return (points || []).map(function (p) {
      const w = c2HeatmapPointWeight(p);
      const latLng = new google.maps.LatLng(p.lat, p.lng);
      return typeof WL === "function" ? new WL(latLng, w) : { location: latLng, weight: w };
    });
  }

  function c2MaxHeatmapWeight(weightedArr) {
    let m = 1;
    weightedArr.forEach(function (pt) {
      const w = typeof pt.getWeight === "function" ? pt.getWeight() : pt.weight;
      if (typeof w === "number" && w > m) m = w;
    });
    return m;
  }

  function c2FirstPlateFromItems(items) {
    for (let i = 0; i < (items || []).length; i++) {
      const p = items[i].plate != null && String(items[i].plate).trim() !== "" ? String(items[i].plate).trim() : "";
      if (p) return p;
    }
    return "";
  }

  function buildIncidentsListHtml(items, deviceId, sectionKind, plateRelocatedToFooter) {
    if (!items || !items.length) return "";
    const devId = deviceId != null && Number(deviceId) > 0 ? Number(deviceId) : 0;
    const isAlertSection = sectionKind === "alert";
    const scope = isAlertSection ? "alerts" : "violations";
    const scopeTitle = isAlertSection
      ? `Show all alerts on this device (${lastHeatmapRangeLabel})`
      : `Show all speed violations on this device (${lastHeatmapRangeLabel})`;
    const scopeIcon = isAlertSection ? "fa-bell" : "fa-gauge-high";
    const scopeBtn =
      devId > 0
        ? `<button type="button" class="c2-popup-device-scope" data-c2-device-scope="${scope}" data-c2-device-id="${devId}" title="${escapeHtml(scopeTitle)}">` +
          `<i class="fa-solid ${scopeIcon}" aria-hidden="true"></i>` +
          `<span class="visually-hidden">${escapeHtml(isAlertSection ? "All alerts on device" : "All violations on device")}</span>` +
          `</button>`
        : "";

    const anyPlateInSection = items.some(
      (x) => x.plate != null && String(x.plate).trim() !== ""
    );
    let html = `<ul class="c2-popup-incidents">`;
    items.forEach((it, idx) => {
      const cls = it.simulated
        ? "c2-popup-incident-item c2-popup-incident-item--sim"
        : it.kind === "violation"
          ? "c2-popup-incident-item c2-popup-incident-item--violation"
          : "c2-popup-incident-item";
      const time = formatIncidentTime(it.occurredUtc);
      const plate = (it.plate != null && String(it.plate).trim() !== "" ? String(it.plate).trim() : "");
      const showScopeHere =
        !!scopeBtn && (plate ? true : !anyPlateInSection && idx === 0);
      html += `<li class="${cls}"><strong>${escapeHtml(it.title)}</strong> — ${escapeHtml(it.detail)}`;
      if (time) html += `<time>${escapeHtml(time)}</time>`;
      if (plate || showScopeHere) {
        html += `<div class="c2-popup-incident-actions">`;
        const showPlateInvestigateHere =
          !!plate && (!plateRelocatedToFooter || plate !== plateRelocatedToFooter);
        if (showPlateInvestigateHere) {
          html +=
            `<button type="button" class="c2-popup-investigate" data-c2-investigate-plate="${encodeURIComponent(plate)}" title="Investigate this plate in search">` +
            `<span class="c2-popup-investigate__glow" aria-hidden="true"></span>` +
            `<span class="c2-popup-investigate__inner">` +
            `<i class="fa-solid fa-magnifying-glass c2-popup-investigate__ico" aria-hidden="true"></i>` +
            `<span class="c2-popup-investigate__label">Investigate</span>` +
            `<i class="fa-solid fa-arrow-up-right-from-square c2-popup-investigate__tab" aria-hidden="true"></i>` +
            `</span></button>`;
        }
        if (showScopeHere) html += scopeBtn;
        html += `</div>`;
      }
      html += `</li>`;
    });
    html += `</ul>`;
    return html;
  }

  function buildIncidentsSection(site, sectionKind, plateRelocatedToFooter) {
    if (!site || !site.items || !site.items.length) return "";
    const isAlert = sectionKind === "alert";
    const headClass = isAlert
      ? "c2-popup-incidents-head c2-popup-incidents-head--alert"
      : "c2-popup-incidents-head c2-popup-incidents-head--violation";
    const headText = isAlert ? `Alerts (${lastHeatmapRangeLabel})` : `Speed violations (${lastHeatmapRangeLabel})`;
    return (
      `<section class="c2-popup-incidents-section" aria-label="${isAlert ? "Alerts" : "Speed violations"}">` +
      `<div class="${headClass}">${headText}</div>` +
      buildIncidentsListHtml(site.items, site.deviceId, sectionKind, plateRelocatedToFooter || "") +
      `</section>`
    );
  }

  function findMergedHeatSiteForDevice(d) {
    if (!d || lastMergedHeatSites.length === 0) return null;
    const lat = typeof d.latitude === "number" ? d.latitude : parseFloat(d.latitude);
    const lng = typeof d.longitude === "number" ? d.longitude : parseFloat(d.longitude);
    if (!Number.isFinite(lat) || !Number.isFinite(lng)) return null;
    const key = lat.toFixed(6) + "," + lng.toFixed(6);
    for (let i = 0; i < lastMergedHeatSites.length; i++) {
      const s = lastMergedHeatSites[i];
      const k = Number(s.lat).toFixed(6) + "," + Number(s.lng).toFixed(6);
      if (k === key) return s;
    }
    return null;
  }

  function buildDevicePopupHtml(d) {
    const health = healthPresentation(d.status);
    const cat = mapDeviceCategory(d.type);
    const loc = d.locationDescription
      ? `<div class="c2-popup-row c2-popup-row--last"><span class="c2-popup-k">Location</span>` +
        `<span class="c2-popup-v">${escapeHtml(d.locationDescription)}</span></div>`
      : "";
    const merged = findMergedHeatSiteForDevice(d);
    const summaryInner = merged ? buildC2HeatHoverTipHtml(merged) : "";
    const summaryBlock = summaryInner
      ? `<div class="c2-popup-summary" role="region" aria-label="Detection counts for selected window">${summaryInner}</div>`
      : "";
    return (
      `<div class="c2-popup c2-map-infowindow c2-popup--device" id="c2-popup-${d.id}">` +
      `<header class="c2-popup-head">` +
      `<div class="c2-popup-title">${escapeHtml(d.name)}</div>` +
      `<div class="c2-popup-subtitle c2-popup-subtitle--device">Device overview · ${escapeHtml(cat)}</div>` +
      `</header>` +
      `<div class="c2-popup-spec">` +
      `<div class="c2-popup-row"><span class="c2-popup-k">Status</span>` +
      `<span class="c2-popup-v c2-popup-status c2-popup-status--${health.key}">${escapeHtml(health.label)}</span></div>` +
      `<div class="c2-popup-row"><span class="c2-popup-k">Type</span>` +
      `<span class="c2-popup-v">${escapeHtml(cat)}</span></div>` +
      `<div class="c2-popup-row"><span class="c2-popup-k">Model</span>` +
      `<span class="c2-popup-v">${escapeHtml(d.type)}</span></div>` +
      loc +
      `</div>` +
      summaryBlock +
      `<div class="c2-popup-actions c2-popup-actions--device">` +
      `<div class="c2-popup-device-actions" role="group" aria-label="Device actions">` +
      `<button type="button" class="c2-popup-quick" data-c2-device-action="liveTraffic" title="View demo live traffic ANPR images">` +
      `<i class="fa-solid fa-tower-broadcast c2-popup-quick__ico" aria-hidden="true"></i>` +
      `<span class="c2-popup-quick__label">Live Traffic</span>` +
      `</button>` +
      `<button type="button" class="c2-popup-quick" data-c2-device-action="recentTransactions" title="Open list of recent reads (heatmap time window)">` +
      `<i class="fa-solid fa-list-ul c2-popup-quick__ico" aria-hidden="true"></i>` +
      `<span class="c2-popup-quick__label">Recent Transactions</span>` +
      `</button>` +
      `<button type="button" class="c2-popup-quick" data-c2-device-action="liveSnapshot" title="View demo camera still image">` +
      `<i class="fa-solid fa-camera c2-popup-quick__ico" aria-hidden="true"></i>` +
      `<span class="c2-popup-quick__label">Live Snapshot</span>` +
      `</button>` +
      `<button type="button" class="c2-popup-quick" data-c2-device-action="liveStream" title="Open device details (stream and telemetry)">` +
      `<i class="fa-solid fa-video c2-popup-quick__ico" aria-hidden="true"></i>` +
      `<span class="c2-popup-quick__label">Live Stream</span>` +
      `</button>` +
      `<button type="button" class="c2-popup-quick c2-popup-quick--wide" data-c2-launch-device-search="${d.id}" title="Open investigation search for this device (${escapeHtml(lastHeatmapRangeLabel || "selected window")})">` +
      `<i class="fa-solid fa-magnifying-glass c2-popup-quick__ico" aria-hidden="true"></i>` +
      `<span class="c2-popup-quick__label">Launch search</span>` +
      `</button>` +
      `<button type="button" class="c2-popup-quick c2-popup-quick--wide" data-c2-dispatch="${d.id}" title="Dispatch patrol to this location">` +
      `<i class="fa-solid fa-route c2-popup-quick__ico" aria-hidden="true"></i>` +
      `<span class="c2-popup-quick__label">Dispatch patrol</span>` +
      `</button>` +
      `</div>` +
      `</div></div>`
    );
  }

  function buildIncidentSitePopupHtml(site, kind) {
    const isAlert = kind === "alert";
    const meta = isAlert ? "Alerts only · ANPR camera" : "Speed violations only · ANPR camera";
    const popupId = `c2-popup-incident-${site.deviceId}-${kind}`;
    const incidentMod = isAlert ? "c2-popup--incident-alert" : "c2-popup--incident-violation";
    const footerPlate = c2FirstPlateFromItems(site.items || []);
    const rowSoloClass = footerPlate ? "" : " c2-popup-actions-row--solo";
    const investigateFooter =
      footerPlate !== ""
        ? `<button type="button" class="c2-popup-incident-foot-btn" data-c2-investigate-plate="${encodeURIComponent(footerPlate)}" title="Investigate plate ${escapeHtml(footerPlate)} in search">` +
          `<i class="fa-solid fa-magnifying-glass c2-popup-incident-foot-btn__ico" aria-hidden="true"></i>` +
          `<span class="c2-popup-incident-foot-btn__label">Investigate</span>` +
          `</button>`
        : "";
    return (
      `<div class="c2-popup c2-map-infowindow c2-popup--incident ${incidentMod}" id="${popupId}">` +
      `<header class="c2-popup-head">` +
      `<div class="c2-popup-title">${escapeHtml(site.name)}</div>` +
      `<div class="c2-popup-subtitle c2-popup-subtitle--incident">${escapeHtml(meta)}</div>` +
      `</header>` +
      buildIncidentsSection(site, kind, footerPlate) +
      `<div class="c2-popup-actions">` +
      `<div class="c2-popup-actions-row${rowSoloClass}" role="group" aria-label="Patrol and investigation">` +
      `<button type="button" class="c2-popup-incident-foot-btn" data-c2-dispatch="${site.deviceId}" title="Dispatch patrol to this location">` +
      `<i class="fa-solid fa-route c2-popup-incident-foot-btn__ico" aria-hidden="true"></i>` +
      `<span class="c2-popup-incident-foot-btn__label">Dispatch patrol</span>` +
      `</button>` +
      investigateFooter +
      `</div>` +
      `</div></div>`
    );
  }

  function escapeHtml(s) {
    if (!s) return "";
    return String(s)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function openInvestigationSearchForPlate(plate) {
    const p = (plate || "").trim();
    if (!p) {
      showToast("No plate to search.");
      return;
    }
    const path = window.commandControlConfig?.investigationSearchUrl || "/Search";
    const u = new URL(path, window.location.origin);
    u.searchParams.set("q", p);
    u.searchParams.set("investigationMode", "true");
    u.searchParams.set("window", "24h");
    window.open(u.pathname + u.search, "_blank", "noopener,noreferrer");
  }

  /** Search only supports 24h / 48h / 7d — align device popup launch with current heatmap span */
  function c2SearchWindowFromHeatmapSpan() {
    const span = Number(lastHeatmapWindowSpanHours) || 48;
    if (span <= 24) return "24h";
    if (span <= 48) return "48h";
    return "7d";
  }

  function openDeviceIncidentSearch(deviceId, scope, investigationMode = true, matchHeatmapWindow = false) {
    const id = parseInt(String(deviceId), 10);
    if (!id || id <= 0) {
      showToast("Invalid device.");
      return;
    }
    const path = window.commandControlConfig?.investigationSearchUrl || "/Search";
    const u = new URL(path, window.location.origin);
    u.searchParams.set("deviceId", String(id));
    u.searchParams.set("window", matchHeatmapWindow ? c2SearchWindowFromHeatmapSpan() : "24h");
    u.searchParams.set("investigationMode", investigationMode ? "true" : "false");
    u.searchParams.set("view", "map");
    if (scope === "alerts") {
      u.searchParams.set("alertsOnly", "true");
    } else if (scope === "violations") {
      u.searchParams.set("speedViolationsOnly", "true");
    }
    window.open(u.pathname + u.search, "_blank", "noopener,noreferrer");
  }

  function c2DevicePayloadForModal(d) {
    if (!d) return null;
    return {
      id: d.id,
      name: d.name,
      type: d.type,
      status: d.status,
      latitude: d.latitude,
      longitude: d.longitude,
      location: d.locationDescription || ""
    };
  }

  function openC2LiveTrafficModal(deviceLabel) {
    const cap = document.getElementById("c2LiveTrafficCaption");
    if (cap) {
      cap.textContent = deviceLabel
        ? `Recent ANPR images (demo) — ${deviceLabel}`
        : "Recent ANPR traffic captures (demo).";
    }
    const el = document.getElementById("c2LiveTrafficModal");
    const B = typeof window !== "undefined" && window.bootstrap;
    if (el && B && B.Modal) {
      B.Modal.getOrCreateInstance(el).show();
    } else {
      showToast("Live traffic viewer is not available.");
    }
  }

  function openC2SnapshotModal(deviceLabel) {
    const cap = document.getElementById("c2SnapshotCaption");
    if (cap) {
      cap.textContent = deviceLabel
        ? `Simulated view — ${deviceLabel}`
        : "Simulated ANPR camera frame (demo).";
    }
    const img = document.getElementById("c2SnapshotImg");
    const demoUrl = window.commandControlConfig?.c2SnapshotDemoImageUrl;
    if (img && typeof demoUrl === "string" && demoUrl.length) {
      img.src = demoUrl;
    }
    const el = document.getElementById("c2SnapshotModal");
    const B = typeof window !== "undefined" && window.bootstrap;
    if (el && B && B.Modal) {
      B.Modal.getOrCreateInstance(el).show();
    } else {
      showToast("Snapshot viewer is not available.");
    }
  }

  function openDeviceHubAction(deviceId, action) {
    const id = parseInt(String(deviceId), 10);
    if (!id || id <= 0) {
      showToast("Invalid device.");
      return;
    }
    const key = (action || "").trim();
    if (key === "liveTraffic") {
      const dev = lastDevices.find((x) => x.id === id);
      openC2LiveTrafficModal(dev?.name || `Device ${id}`);
      return;
    }
    if (key === "liveSnapshot") {
      const dev = lastDevices.find((x) => x.id === id);
      openC2SnapshotModal(dev?.name || `Device ${id}`);
      return;
    }
    if (key === "liveStream") {
      const dev = lastDevices.find((x) => x.id === id);
      const payload =
        c2DevicePayloadForModal(dev) ||
        ({
          id,
          name: `Device ${id}`,
          type: "ANPR Camera",
          status: "Unknown",
          latitude: 0,
          longitude: 0,
          location: ""
        });
      if (typeof window.showDeviceDetailsModal === "function") {
        window.showDeviceDetailsModal(payload);
        return;
      }
      const tmpl = window.commandControlConfig?.deviceLiveStreamUrlTemplate;
      if (typeof tmpl === "string" && tmpl.indexOf("{id}") >= 0) {
        window.open(tmpl.split("{id}").join(String(id)), "_blank", "noopener,noreferrer");
        return;
      }
      showToast("Device details are not available on this page.");
      return;
    }
    const path = window.commandControlConfig?.investigationSearchUrl || "/Search";
    const u = new URL(path, window.location.origin);
    u.searchParams.set("deviceId", String(id));
    u.searchParams.set("investigationMode", "false");
    if (key === "recentTransactions") {
      u.searchParams.set("view", "list");
      u.searchParams.set("window", c2SearchWindowFromHeatmapSpan());
    } else {
      return;
    }
    window.open(u.pathname + u.search, "_blank", "noopener,noreferrer");
  }

  function bindInvestigateButtons(root) {
    if (!root) return;
    root.querySelectorAll("[data-c2-investigate-plate]").forEach((btn) => {
      btn.addEventListener("click", (e) => {
        e.preventDefault();
        e.stopPropagation();
        const enc = btn.getAttribute("data-c2-investigate-plate") || "";
        let plate = "";
        try {
          plate = decodeURIComponent(enc);
        } catch {
          plate = enc;
        }
        openInvestigationSearchForPlate(plate);
      });
    });
  }

  function bindDeviceScopeButtons(root) {
    if (!root) return;
    root.querySelectorAll("[data-c2-device-scope]").forEach((btn) => {
      btn.addEventListener("click", (e) => {
        e.preventDefault();
        e.stopPropagation();
        const scope = (btn.getAttribute("data-c2-device-scope") || "").toLowerCase();
        const id = btn.getAttribute("data-c2-device-id");
        openDeviceIncidentSearch(id, scope);
      });
    });
  }

  function openDispatchModal(label) {
    const labelEl = document.getElementById("c2DispatchDeviceLabel");
    if (labelEl) labelEl.textContent = label;
    const modalEl = document.getElementById("c2DispatchModal");
    if (modalEl && typeof bootstrap !== "undefined") {
      const modal = new bootstrap.Modal(modalEl);
      modal.show();
    }
  }

  function bindLaunchDeviceSearchButtons(root) {
    if (!root) return;
    root.querySelectorAll("[data-c2-launch-device-search]").forEach((btn) => {
      btn.addEventListener("click", (e) => {
        e.preventDefault();
        e.stopPropagation();
        const id = btn.getAttribute("data-c2-launch-device-search");
        openDeviceIncidentSearch(id, "all", false, true);
      });
    });
  }

  function bindDeviceQuickActions(root, deviceId) {
    if (!root) return;
    root.querySelectorAll("[data-c2-device-action]").forEach((btn) => {
      btn.addEventListener("click", (e) => {
        e.preventDefault();
        e.stopPropagation();
        const action = btn.getAttribute("data-c2-device-action") || "";
        openDeviceHubAction(deviceId, action);
      });
    });
  }

  function bindPopupDom(d) {
    google.maps.event.addListenerOnce(infoWindow, "domready", () => {
      const root = document.getElementById("c2-popup-" + d.id);
      if (!root) return;
      root.querySelector("[data-c2-dispatch]")?.addEventListener("click", () => {
        openDispatchModal(d.name);
      });
      bindLaunchDeviceSearchButtons(root);
      bindDeviceQuickActions(root, d.id);
      bindInvestigateButtons(root);
      bindDeviceScopeButtons(root);
    });
  }

  function bindIncidentSitePopup(site, kind) {
    const popupId = "c2-popup-incident-" + site.deviceId + "-" + kind;
    google.maps.event.addListenerOnce(infoWindow, "domready", () => {
      const root = document.getElementById(popupId);
      if (!root) return;
      root.querySelector("[data-c2-dispatch]")?.addEventListener("click", () => {
        openDispatchModal(site.name);
      });
      bindInvestigateButtons(root);
      bindDeviceScopeButtons(root);
    });
  }

  /** Cached ESM module; `false` = load failed. */
  let markerClustererModuleCache = null;

  async function ensureMarkerClustererModule() {
    if (markerClustererModuleCache === false) return null;
    if (markerClustererModuleCache) return markerClustererModuleCache;
    try {
      markerClustererModuleCache = await import(
        "https://cdn.jsdelivr.net/npm/@googlemaps/markerclusterer@2.5.3/+esm"
      );
      return markerClustererModuleCache;
    } catch {
      markerClustererModuleCache = false;
      return null;
    }
  }

  /** fitBounds / InfoWindow pan reset tilt on vector maps — re-apply after the camera settles. */
  function scheduleC2ObliqueAfterAnchorChange() {
    if (!map) return;
    google.maps.event.addListenerOnce(map, "idle", snapC2ObliqueCamera);
    window.setTimeout(snapC2ObliqueCamera, 200);
    window.setTimeout(snapC2ObliqueCamera, 550);
  }

  function isC2VectorMap() {
    if (!map || typeof map.getRenderingType !== "function") return false;
    const rt = google.maps.RenderingType;
    if (!rt) return false;
    return map.getRenderingType() === rt.VECTOR;
  }

  function snapC2ObliqueCamera() {
    const el = document.getElementById("c2Layer3dTilt");
    if (!map || !el?.checked || !isC2VectorMap()) return;
    map.setTilt(C2_OBLIQUE_TILT);
    map.setHeading(C2_OBLIQUE_HEADING);
  }

  function applyObliqueViewToggle() {
    const el = document.getElementById("c2Layer3dTilt");
    if (!map || !el) return;
    if (!isC2VectorMap()) return;
    const on = !!el.checked;
    if (on) {
      snapC2ObliqueCamera();
    } else {
      map.setTilt(0);
      map.setHeading(0);
    }
  }

  function hideC2ObliqueToggleIfUnsupported() {
    const row = document.getElementById("c2Layer3dTilt")?.closest(".command-c2-switch-row");
    if (!row) return;
    if (!google.maps.RenderingType) {
      row.setAttribute("hidden", "");
    }
  }

  function applyLayerToggles() {
    const devOn = document.getElementById("c2LayerDevices")?.checked;
    const alertOn = document.getElementById("c2LayerAlerts")?.checked;
    const violOn = document.getElementById("c2LayerViolations")?.checked;
    const txOn = document.getElementById("c2LayerTransactions")?.checked;

    if (markerClusterer) {
      markerClusterer.clearMarkers();
      if (devOn) markerClusterer.addMarkers(markers);
    } else {
      markers.forEach((m) => m.setMap(devOn ? map : null));
    }

    if (heatmapAlerts) heatmapAlerts.setMap(alertOn ? map : null);
    if (heatmapViolations) heatmapViolations.setMap(violOn ? map : null);
    if (heatmapTransactions) heatmapTransactions.setMap(txOn ? map : null);

    document.getElementById("commandControlMapShell")?.classList.toggle("c2-layer-pulse", alertOn);
    hideC2HeatHoverTip();
  }

  function wireToggles() {
    ["c2LayerDevices", "c2LayerAlerts", "c2LayerViolations", "c2LayerTransactions"].forEach((id) => {
      document.getElementById(id)?.addEventListener("change", applyLayerToggles);
    });
    document.getElementById("c2Layer3dTilt")?.addEventListener("change", applyObliqueViewToggle);
  }

  function debounceAsync(fn, ms) {
    let t;
    return function () {
      clearTimeout(t);
      t = setTimeout(() => {
        Promise.resolve(fn()).catch((err) => console.error(err));
      }, ms);
    };
  }

  function applyHeatmapSliderConstraints() {
    const fromEl = document.getElementById("c2HeatmapFromHours");
    const toEl = document.getElementById("c2HeatmapToHours");
    if (!fromEl || !toEl) return;
    let from = Math.min(c2MaxHeatmapFromHours, Math.max(1, parseInt(fromEl.value, 10) || 1));
    let to = Math.min(c2MaxHeatmapFromHours - 1, Math.max(0, parseInt(toEl.value, 10) || 0));

    toEl.max = String(Math.max(0, from - 1));
    to = Math.min(to, parseInt(toEl.max, 10) || 0);

    fromEl.min = String(Math.max(1, to + 1));
    from = Math.max(from, parseInt(fromEl.min, 10) || 1);
    from = Math.min(c2MaxHeatmapFromHours, from);

    if (to >= from) {
      to = from - 1;
    }
    if (to < 0) {
      to = 0;
      from = Math.max(1, to + 1);
    }

    fromEl.value = String(from);
    toEl.value = String(to);
    fromEl.setAttribute("aria-valuenow", String(from));
    fromEl.setAttribute("aria-valuemin", "1");
    fromEl.setAttribute("aria-valuemax", String(c2MaxHeatmapFromHours));
    toEl.setAttribute("aria-valuenow", String(to));
    toEl.setAttribute("aria-valuemin", "0");
    toEl.setAttribute("aria-valuemax", String(Math.max(0, from - 1)));
    const fv = document.getElementById("c2HeatmapFromHoursVal");
    const tv = document.getElementById("c2HeatmapToHoursVal");
    if (fv) fv.textContent = String(from);
    if (tv) tv.textContent = String(to);
  }

  function buildLocalHeatmapRangeLabel(from, to) {
    if (to <= 0) return from === 1 ? "Last hour" : `Last ${from} hours`;
    return `From ${from}h ago → ${to}h ago`;
  }

  function updateHeatmapSummaryDisplay() {
    applyHeatmapSliderConstraints();
    const fromEl = document.getElementById("c2HeatmapFromHours");
    const toEl = document.getElementById("c2HeatmapToHours");
    if (!fromEl || !toEl) return;
    const from = parseInt(fromEl.value, 10);
    const to = parseInt(toEl.value, 10);
    const span = from - to;
    const line = `${buildLocalHeatmapRangeLabel(from, to)} · ${span} h span`;
    const sumEl = document.getElementById("c2HeatmapWindowSummary");
    if (sumEl) sumEl.textContent = line;
    const panelSub = document.getElementById("c2HeatmapPanelSubtitle");
    if (panelSub) panelSub.textContent = line;
  }

  function wireHeatmapWindow(scheduleRefresh) {
    function onHeatmapAdjust() {
      updateHeatmapSummaryDisplay();
      scheduleRefresh();
    }
    document.getElementById("c2HeatmapFromHours")?.addEventListener("input", onHeatmapAdjust);
    document.getElementById("c2HeatmapToHours")?.addEventListener("input", onHeatmapAdjust);
    updateHeatmapSummaryDisplay();
  }

  function buildDeviceFilterParams() {
    const params = new URLSearchParams();
    if (!map || !map.getBounds) return null;
    const b = map.getBounds();
    if (!b) return null;
    const ne = b.getNorthEast();
    const sw = b.getSouthWest();
    params.set("north", ne.lat().toFixed(6));
    params.set("south", sw.lat().toFixed(6));
    params.set("east", ne.lng().toFixed(6));
    params.set("west", sw.lng().toFixed(6));

    applyHeatmapSliderConstraints();
    const hf = document.getElementById("c2HeatmapFromHours");
    const ht = document.getElementById("c2HeatmapToHours");
    if (hf && ht) {
      params.set("heatmapFromHoursAgo", hf.value);
      params.set("heatmapToHoursAgo", ht.value);
    }

    const online = document.getElementById("c2FilterOnline")?.checked;
    const offline = document.getElementById("c2FilterOffline")?.checked;
    const warning = document.getElementById("c2FilterWarning")?.checked;
    const statusPicked = [online, offline, warning].filter(Boolean).length;
    if (statusPicked > 0 && statusPicked < 3) {
      const st = [];
      if (online) st.push("Online");
      if (offline) st.push("Offline");
      if (warning) st.push("Maintenance");
      if (st.length) params.set("status", st.join(","));
    }

    const anpr = document.getElementById("c2TypeAnpr")?.checked;
    const lidar = document.getElementById("c2TypeLidar")?.checked;
    const radar = document.getElementById("c2TypeRadar")?.checked;
    const env = document.getElementById("c2TypeEnv")?.checked;
    const typePicked = [anpr, lidar, radar, env].filter(Boolean).length;
    if (typePicked > 0 && typePicked < 4) {
      const ty = [];
      if (anpr) ty.push("anpr");
      if (lidar) ty.push("lidar");
      if (radar) ty.push("radar");
      if (env) ty.push("environmental");
      if (ty.length) params.set("types", ty.join(","));
    }

    const q = document.getElementById("c2DeviceSearch")?.value?.trim();
    if (q) params.set("q", q);

    return params;
  }

  function clearDynamicLayers() {
    markers.forEach((m) => {
      m.setMap(null);
    });
    markers = [];
    if (markerClusterer) {
      try {
        markerClusterer.clearMarkers();
      } catch (e) {
        /* ignore */
      }
      try {
        markerClusterer.setMap(null);
      } catch (e2) {
        /* ignore */
      }
      markerClusterer = null;
    }
    if (heatmapAlerts) {
      heatmapAlerts.setMap(null);
      heatmapAlerts = null;
    }
    if (heatmapViolations) {
      heatmapViolations.setMap(null);
      heatmapViolations = null;
    }
    if (heatmapTransactions) {
      heatmapTransactions.setMap(null);
      heatmapTransactions = null;
    }
    deviceMarkerByDeviceId.clear();
    lastAlertByDeviceId.clear();
    lastViolByDeviceId.clear();
    lastMergedHeatSites = [];
    invalidateHeatHoverClusterCache();
    hideC2HeatHoverTip();
  }

  function wireDeviceFilters(scheduleRefresh) {
    const ids = [
      "c2FilterOnline",
      "c2FilterOffline",
      "c2FilterWarning",
      "c2TypeAnpr",
      "c2TypeLidar",
      "c2TypeRadar",
      "c2TypeEnv",
      "c2DeviceSearch"
    ];
    ids.forEach((id) => {
      const el = document.getElementById(id);
      if (!el) return;
      el.addEventListener(el.tagName === "INPUT" && el.type === "text" ? "input" : "change", scheduleRefresh);
    });
  }

  function updateDeviceFilterMeta(meta) {
    const el = document.getElementById("c2DeviceFilterMeta");
    if (!el || !meta) return;
    if (!meta.viewportApplied) {
      el.textContent = "Move the map to load devices.";
      return;
    }
    let t = `${meta.deviceCount} device${meta.deviceCount === 1 ? "" : "s"} in view`;
    if (meta.deviceLimitReached) {
      t += " — zoom in or filter (limit reached)";
    }
    el.textContent = t;
  }

  function stopSimulateLiveTimer() {
    if (simulateLiveTimerId != null) {
      window.clearInterval(simulateLiveTimerId);
      simulateLiveTimerId = null;
    }
  }

  function simulateLiveTick() {
    if (!document.getElementById("c2SimulateLive")?.checked) return;
    openSimulatedPopup({ fromTimer: true });
  }

  function openSimulatedPopup(options) {
    const fromTimer = !!options?.fromTimer;
    if (fromTimer && !document.getElementById("c2SimulateLive")?.checked) return;
    if (infoWindow && typeof infoWindow.getMap === "function" && infoWindow.getMap()) return;

    const alertOn = document.getElementById("c2LayerAlerts")?.checked;
    const violOn = document.getElementById("c2LayerViolations")?.checked;
    if (!alertOn && !violOn) {
      showToast("Turn on the Alert and/or Violation layer for simulated popups (not on device markers).");
      return;
    }

    const pools = [];
    if (alertOn && lastIncidentAlertSites.length) {
      pools.push({ kind: "alert", sites: lastIncidentAlertSites });
    }
    if (violOn && lastIncidentViolationSites.length) {
      pools.push({ kind: "violation", sites: lastIncidentViolationSites });
    }
    if (!pools.length) {
      showToast("No alert/violation data in view for the layers you enabled — pan the map or widen filters.");
      return;
    }

    const pick = pools[Math.floor(Math.random() * pools.length)];
    const kind = pick.kind;
    const site = pick.sites[Math.floor(Math.random() * pick.sites.length)];

    const demoSpeed = 110 + Math.floor(Math.random() * 45);
    const demoPlate =
      C2_SIMULATE_PLATES[Math.floor(Math.random() * C2_SIMULATE_PLATES.length)] || "123456";
    const simItem =
      kind === "violation"
        ? {
            kind: "violation",
            title: "Speed violation (simulated)",
            detail: `Demo — plate ${demoPlate} · ${demoSpeed} km/h (≥ threshold)`,
            plate: demoPlate,
            occurredUtc: new Date().toISOString(),
            simulated: true,
            transactionId: 0
          }
        : {
            kind: "alert",
            title: "Alert (simulated)",
            detail: `Demo — plate ${demoPlate}`,
            plate: demoPlate,
            occurredUtc: new Date().toISOString(),
            simulated: true,
            transactionId: 0
          };

    const augmented = {
      deviceId: site.deviceId,
      name: site.name,
      lat: site.lat,
      lng: site.lng,
      count: (site.count || 0) + 1,
      items: [simItem, ...(site.items || [])].slice(0, 1)
    };

    const anchorPos = { lat: site.lat, lng: site.lng };

    hideC2HeatHoverTip();
    infoWindow.setContent(buildIncidentSitePopupHtml(augmented, kind));
    infoWindow.setPosition(anchorPos);
    infoWindow.open({ map });
    scheduleC2ObliqueAfterAnchorChange();

    bindIncidentSitePopup(augmented, kind);
    showToast(`Simulated ${kind} — ${site.name}`);
  }

  function startSimulateLiveTimer() {
    stopSimulateLiveTimer();
    const period = window.commandControlConfig?.simulateIntervalMs || 32000;
    simulateLiveTimerId = window.setInterval(simulateLiveTick, period);
  }

  function wireSimulateLive() {
    const el = document.getElementById("c2SimulateLive");
    if (el) {
      el.addEventListener("change", () => {
        if (el.checked) {
          startSimulateLiveTimer();
          window.setTimeout(simulateLiveTick, 900);
        } else {
          stopSimulateLiveTimer();
        }
      });
    }
    document.getElementById("c2SimulatePopupNow")?.addEventListener("click", () => {
      openSimulatedPopup({ fromTimer: false });
    });
  }

  function wireModals() {
    document.getElementById("c2DispatchConfirm")?.addEventListener("click", function () {
      const name = document.getElementById("c2DispatchDeviceLabel").textContent;
      bootstrap.Modal.getInstance(document.getElementById("c2DispatchModal"))?.hide();
      showToast(`Patrol dispatched — ${name}.`);
    });
  }

  function mapMarkerIcons() {
    return typeof window !== "undefined" && window.MapMarkerIcons ? window.MapMarkerIcons : null;
  }

  function defaultDeviceGlyph(status) {
    const s = (status || "").toLowerCase();
    if (s.includes("offline")) return "https://maps.google.com/mapfiles/ms/icons/red-dot.png";
    if (s.includes("maintenance")) return "https://maps.google.com/mapfiles/ms/icons/yellow-dot.png";
    return "https://maps.google.com/mapfiles/ms/icons/green-dot.png";
  }

  function hideC2HeatHoverTip() {
    const tip = document.getElementById("c2HeatHoverTip");
    if (!tip) return;
    tip.hidden = true;
    tip.innerHTML = "";
  }

  function mergeHeatmapPointsForHover(alerts, violations, transactions) {
    const m = new Map();
    function coordKey(lat, lng) {
      return Number(lat).toFixed(6) + "," + Number(lng).toFixed(6);
    }
    function ingest(arr, prop) {
      (arr || []).forEach(function (p) {
        if (p == null || typeof p.lat !== "number" || typeof p.lng !== "number") return;
        const k = coordKey(p.lat, p.lng);
        let o = m.get(k);
        if (!o) {
          o = { lat: p.lat, lng: p.lng, alerts: 0, violations: 0, transactions: 0 };
          m.set(k, o);
        }
        o[prop] = typeof p.count === "number" ? p.count : 0;
      });
    }
    ingest(alerts, "alerts");
    ingest(violations, "violations");
    ingest(transactions, "transactions");
    return Array.from(m.values());
  }

  function buildC2HeatHoverTipHtml(site) {
    const alertOn = document.getElementById("c2LayerAlerts")?.checked;
    const violOn = document.getElementById("c2LayerViolations")?.checked;
    const txOn = document.getElementById("c2LayerTransactions")?.checked;

    const head =
      '<div class="command-c2-heat-hover-tip__window">' + escapeHtml(lastHeatmapRangeLabel || "Selected window") + "</div>";

    const clusterMeta =
      site.isCluster && site.siteCount > 1
        ? '<div class="command-c2-heat-hover-tip__cluster-meta">Grouped for this heat · <strong>' +
          site.siteCount +
          "</strong> cameras</div>"
        : "";

    function bigCount(n, label, countLabelClass) {
      const labelCls = countLabelClass
        ? "command-c2-heat-hover-tip__count-label " + countLabelClass
        : "command-c2-heat-hover-tip__count-label";
      return (
        '<div class="command-c2-heat-hover-tip__count" aria-label="Detection count">' +
        String(n) +
        "</div>" +
        '<div class="' +
        labelCls +
        '">' +
        escapeHtml(label) +
        "</div>"
      );
    }

    function breakdown() {
      const bits = [];
      if (alertOn && site.alerts > 0) {
        bits.push(
          '<span class="command-c2-heat-hover-line command-c2-heat-hover-line--alert">Alerts: <strong>' +
            site.alerts +
            "</strong></span>"
        );
      }
      if (violOn && site.violations > 0) {
        bits.push(
          '<span class="command-c2-heat-hover-line command-c2-heat-hover-line--viol">Violations: <strong>' +
            site.violations +
            "</strong></span>"
        );
      }
      return bits.length ? '<div class="command-c2-heat-hover-tip__breakdown">' + bits.join("") + "</div>" : "";
    }

    if (txOn) {
      const n = typeof site.transactions === "number" ? site.transactions : 0;
      const txLabel = site.isCluster && site.siteCount > 1 ? "ANPR detection count (area total)" : "ANPR detection count";
      return head + clusterMeta + bigCount(n, txLabel, "command-c2-heat-hover-tip__count-label--tx-density") + breakdown();
    }

    if (alertOn && violOn) {
      const bits = [];
      if (site.alerts > 0) {
        bits.push(
          '<div class="command-c2-heat-hover-tip__dual">' +
            '<span class="command-c2-heat-hover-tip__count command-c2-heat-hover-tip__count--sm" aria-label="Alert detection count">' +
            site.alerts +
            "</span>" +
            '<span class="command-c2-heat-hover-tip__dual-label">alerts</span></div>'
        );
      }
      if (site.violations > 0) {
        bits.push(
          '<div class="command-c2-heat-hover-tip__dual">' +
            '<span class="command-c2-heat-hover-tip__count command-c2-heat-hover-tip__count--sm" aria-label="Violation count">' +
            site.violations +
            "</span>" +
            '<span class="command-c2-heat-hover-tip__dual-label">violations</span></div>'
        );
      }
      if (!bits.length) return "";
      const dualLabel =
        site.isCluster && site.siteCount > 1 ? "Detection counts (area total)" : "Detection counts";
      return head + clusterMeta + '<div class="command-c2-heat-hover-tip__count-label">' + dualLabel + "</div>" + bits.join("");
    }

    if (alertOn && site.alerts > 0) {
      const lbl = site.isCluster && site.siteCount > 1 ? "Alert detection count (area total)" : "Alert detection count";
      return head + clusterMeta + bigCount(site.alerts, lbl);
    }
    if (violOn && site.violations > 0) {
      const lbl = site.isCluster && site.siteCount > 1 ? "Speed violation count (area total)" : "Speed violation count";
      return head + clusterMeta + bigCount(site.violations, lbl);
    }
    return "";
  }

  function c2MetersPerPixelAtLat(lat, zoom) {
    return (156543.03392 * Math.cos((lat * Math.PI) / 180)) / Math.pow(2, zoom);
  }

  function c2HaversineMeters(lat1, lng1, lat2, lng2) {
    const R = 6371008.8;
    const toRad = (deg) => (deg * Math.PI) / 180;
    const dLat = toRad(lat2 - lat1);
    const dLng = toRad(lng2 - lng1);
    const a =
      Math.sin(dLat / 2) * Math.sin(dLat / 2) +
      Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLng / 2) * Math.sin(dLng / 2);
    const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
    return R * c;
  }

  function c2HeatHoverCellMetersForZoom(zoom) {
    const z = typeof zoom === "number" ? zoom : 11;
    if (z >= C2_HOVER_CLUSTER_MAX_ZOOM) return 0;
    return Math.min(42000, Math.max(380, 520 * Math.pow(2, C2_HOVER_CLUSTER_MAX_ZOOM - 1 - z)));
  }

  function invalidateHeatHoverClusterCache() {
    c2HeatHoverClusterCache = { zoom: null, siteLen: null, cellM: null, centerKey: null, rows: null };
  }

  function c2BuildHeatHoverClusters(sites, cellMeters, centerLat) {
    if (!sites.length) return [];
    if (!cellMeters || cellMeters <= 0) {
      return sites.map(function (s) {
        return {
          lat: s.lat,
          lng: s.lng,
          alerts: s.alerts,
          violations: s.violations,
          transactions: s.transactions,
          siteCount: 1,
          isCluster: false
        };
      });
    }
    const clat = typeof centerLat === "number" ? centerLat : sites[0].lat;
    const degLat = cellMeters / 111320;
    const degLng = cellMeters / (111320 * Math.max(0.2, Math.cos((clat * Math.PI) / 180)));
    const groups = new Map();
    sites.forEach(function (s) {
      const gx = Math.floor(s.lat / degLat);
      const gy = Math.floor(s.lng / degLng);
      const key = gx + "," + gy;
      let g = groups.get(key);
      if (!g) {
        g = { sumLat: 0, sumLng: 0, n: 0, alerts: 0, violations: 0, transactions: 0 };
        groups.set(key, g);
      }
      g.sumLat += s.lat;
      g.sumLng += s.lng;
      g.n += 1;
      g.alerts += s.alerts;
      g.violations += s.violations;
      g.transactions += s.transactions;
    });
    return Array.from(groups.values()).map(function (g) {
      return {
        lat: g.sumLat / g.n,
        lng: g.sumLng / g.n,
        alerts: g.alerts,
        violations: g.violations,
        transactions: g.transactions,
        siteCount: g.n,
        isCluster: g.n > 1
      };
    });
  }

  function getHeatHoverCandidates(zoom) {
    if (!map || !lastMergedHeatSites.length) return [];
    const cellM = c2HeatHoverCellMetersForZoom(zoom);
    const c = map.getCenter && map.getCenter();
    const centerKey = c ? c.lat().toFixed(2) + "," + c.lng().toFixed(2) : "0,0";
    const len = lastMergedHeatSites.length;
    if (
      c2HeatHoverClusterCache.rows &&
      c2HeatHoverClusterCache.zoom === zoom &&
      c2HeatHoverClusterCache.siteLen === len &&
      c2HeatHoverClusterCache.cellM === cellM &&
      c2HeatHoverClusterCache.centerKey === centerKey
    ) {
      return c2HeatHoverClusterCache.rows;
    }
    const centerLat = c ? c.lat() : lastMergedHeatSites[0].lat;
    const rows = c2BuildHeatHoverClusters(lastMergedHeatSites, cellM, centerLat);
    c2HeatHoverClusterCache = { zoom, siteLen: len, cellM, centerKey, rows };
    return rows;
  }

  function pickNearestMergedHeatSiteLatLng(latLng) {
    if (!map || !lastMergedHeatSites.length || !latLng) return null;
    const alertOn = document.getElementById("c2LayerAlerts")?.checked;
    const violOn = document.getElementById("c2LayerViolations")?.checked;
    const txOn = document.getElementById("c2LayerTransactions")?.checked;
    if (!alertOn && !violOn && !txOn) return null;

    const lat = typeof latLng.lat === "function" ? latLng.lat() : latLng.lat;
    const lng = typeof latLng.lng === "function" ? latLng.lng() : latLng.lng;
    const zoom = typeof map.getZoom === "function" ? map.getZoom() : 11;
    const mpp = c2MetersPerPixelAtLat(lat, zoom);
    const cellM = c2HeatHoverCellMetersForZoom(zoom);
    const hitMeters = Math.max(mpp * C2_HEAT_HOVER_PX, cellM > 0 ? cellM * 0.42 : 0);

    const candidates = getHeatHoverCandidates(zoom);

    let best = null;
    let bestD = Infinity;
    candidates.forEach(function (s) {
      if (
        (!alertOn || s.alerts <= 0) &&
        (!violOn || s.violations <= 0) &&
        (!txOn || s.transactions <= 0)
      ) {
        return;
      }
      const d = c2HaversineMeters(lat, lng, s.lat, s.lng);
      if (d <= hitMeters && d < bestD) {
        bestD = d;
        best = s;
      }
    });
    return best;
  }

  function showC2HeatHoverTipAt(rootX, rootY, html) {
    const tip = document.getElementById("c2HeatHoverTip");
    const shell = document.getElementById("commandControlMapShell");
    if (!tip || !shell || !html) return;
    tip.innerHTML = html;
    tip.hidden = false;
    const pad = 14;
    const tw = tip.offsetWidth || 200;
    const th = tip.offsetHeight || 60;
    const sw = shell.clientWidth;
    const sh = shell.clientHeight;
    let x = rootX + pad;
    let y = rootY - th - pad;
    if (x + tw > sw - 8) x = Math.max(8, rootX - tw - pad);
    if (y < 8) y = rootY + pad;
    if (y + th > sh - 8) y = Math.max(8, sh - th - 8);
    tip.style.left = x + "px";
    tip.style.top = y + "px";
  }

  function updateC2HeatHoverFromMapEvent(mapEv) {
    if (!map) {
      hideC2HeatHoverTip();
      return;
    }
    if (infoWindow && typeof infoWindow.getMap === "function" && infoWindow.getMap()) {
      hideC2HeatHoverTip();
      return;
    }
    const latLng = mapEv && mapEv.latLng;
    if (!latLng) {
      hideC2HeatHoverTip();
      return;
    }

    const site = pickNearestMergedHeatSiteLatLng(latLng);
    if (!site) {
      hideC2HeatHoverTip();
      return;
    }
    const html = buildC2HeatHoverTipHtml(site);
    if (!html) {
      hideC2HeatHoverTip();
      return;
    }

    const shell = document.getElementById("commandControlMapShell");
    if (!shell) return;
    const srect = shell.getBoundingClientRect();
    const dom = mapEv.domEvent;
    if (dom && typeof dom.clientX === "number" && typeof dom.clientY === "number") {
      const rootX = dom.clientX - srect.left;
      const rootY = dom.clientY - srect.top;
      showC2HeatHoverTipAt(rootX, rootY, html);
      return;
    }

    hideC2HeatHoverTip();
  }

  function scheduleHeatHoverUpdateFromMap() {
    if (heatHoverRafId) return;
    heatHoverRafId = window.requestAnimationFrame(function () {
      heatHoverRafId = 0;
      if (lastHeatHoverMapEvent) updateC2HeatHoverFromMapEvent(lastHeatHoverMapEvent);
    });
  }

  function wireHeatmapHoverInteraction() {
    if (!map || map.__c2HeatHoverWired) return;
    map.__c2HeatHoverWired = true;
    google.maps.event.addListener(map, "mousemove", function (mapEv) {
      lastHeatHoverMapEvent = mapEv;
      scheduleHeatHoverUpdateFromMap();
    });
    google.maps.event.addListener(map, "mouseout", function () {
      hideC2HeatHoverTip();
    });
    google.maps.event.addListener(map, "zoom_changed", function () {
      invalidateHeatHoverClusterCache();
      hideC2HeatHoverTip();
    });
  }

  window.initCommandControlMap = async function () {
    const mapEl = document.getElementById("commandControlMap");
    if (!mapEl || typeof google === "undefined" || !google.maps) return;

    if (!window.commandControlConfig || !window.commandControlConfig.mapDataUrl) {
      showToast("Map data URL is not configured.");
      return;
    }

    if (!google.maps.visualization) {
      console.warn("Traffic Monitoring Hub: visualization library not loaded; heatmaps disabled.");
    }

    try {
      const rt = google.maps.RenderingType;
      const obliqueOn = document.getElementById("c2Layer3dTilt")?.checked !== false;
      const mapOptions = {
        center: dohaCenter,
        zoom: 11,
        mapTypeId: google.maps.MapTypeId.ROADMAP,
        styles: commandControlMapStyles,
        backgroundColor: "#030711",
        mapTypeControl: true,
        mapTypeControlOptions: {
          style: google.maps.MapTypeControlStyle.DEFAULT,
          position: google.maps.ControlPosition.TOP_LEFT,
          mapTypeIds: [google.maps.MapTypeId.ROADMAP, google.maps.MapTypeId.SATELLITE]
        },
        zoomControl: true,
        zoomControlOptions: { position: google.maps.ControlPosition.RIGHT_CENTER },
        streetViewControl: false,
        fullscreenControl: true,
        fullscreenControlOptions: { position: google.maps.ControlPosition.RIGHT_TOP },
        controlSize: 40
      };
      const colorScheme = google.maps.ColorScheme;
      if (colorScheme && typeof colorScheme.DARK !== "undefined") {
        mapOptions.colorScheme = colorScheme.DARK;
      }
      if (rt && typeof rt.VECTOR !== "undefined") {
        mapOptions.renderingType = rt.VECTOR;
        mapOptions.tiltInteractionEnabled = true;
        mapOptions.headingInteractionEnabled = true;
        mapOptions.isFractionalZoomEnabled = true;
        mapOptions.tilt = obliqueOn ? C2_OBLIQUE_TILT : 0;
        mapOptions.heading = obliqueOn ? C2_OBLIQUE_HEADING : 0;
      }
      map = new google.maps.Map(mapEl, mapOptions);
      hideC2ObliqueToggleIfUnsupported();

      map.addListener("maptypeid_changed", () => {
        const id = map.getMapTypeId();
        if (id === google.maps.MapTypeId.ROADMAP || id === "roadmap") {
          map.setOptions({ styles: commandControlMapStyles, backgroundColor: "#030711" });
        } else {
          map.setOptions({ styles: [], backgroundColor: "#030711" });
        }
      });

      map.addListener("zoom_changed", () => {
        snapC2ObliqueCamera();
      });

      map.addListener("dragstart", hideC2HeatHoverTip);

      infoWindow = new google.maps.InfoWindow({ maxWidth: 464 });

      wireHeatmapHoverInteraction();

      async function refreshMapData() {
        const params = buildDeviceFilterParams();
        if (!params) return;
        // Opening a marker triggers map idle → debounced refresh → layer rebuild, which closed the InfoWindow.
        // Hotspot popups use position-only open; same guard keeps them stable too.
        if (infoWindow && typeof infoWindow.getMap === "function" && infoWindow.getMap()) {
          return;
        }
        const url = window.commandControlConfig.mapDataUrl + "?" + params.toString();
        let data;
        try {
          const res = await fetch(url, {
            headers: { Accept: "application/json" },
            credentials: "same-origin"
          });
          if (!res.ok) {
            throw new Error("HTTP " + res.status);
          }
          const ct = res.headers.get("content-type") || "";
          if (!ct.includes("application/json")) {
            throw new Error("Expected JSON from MapData");
          }
          data = await res.json();
        } catch (err) {
          console.error(err);
          showToast("Could not load map data. Check the MapData endpoint.");
          return;
        }

        clearDynamicLayers();

        lastHeatmapRangeLabel = data.meta?.heatmapRangeLabel || lastHeatmapRangeLabel;
        lastHeatmapWindowSpanHours = data.meta?.heatmapWindowHours ?? lastHeatmapWindowSpanHours;

        const devices = data.devices || [];
        const alertSites = data.incidentAlertSites || [];
        const violSites = data.incidentViolationSites || [];
        lastDevices = devices.slice();
        lastIncidentAlertSites = alertSites.slice();
        lastIncidentViolationSites = violSites.slice();
        lastDeviceIdsOnMap = new Set(devices.map((d) => d.id));
        lastAlertByDeviceId.clear();
        lastViolByDeviceId.clear();
        alertSites.forEach((s) => lastAlertByDeviceId.set(s.deviceId, s));
        violSites.forEach((s) => lastViolByDeviceId.set(s.deviceId, s));

        const icons = mapMarkerIcons();
        devices.forEach((d) => {
          const icon =
            icons && typeof icons.deviceIcon === "function"
              ? icons.deviceIcon(google.maps, d.type, d.status)
              : defaultDeviceGlyph(d.status);
          const marker = new google.maps.Marker({
            position: { lat: d.latitude, lng: d.longitude },
            map: null,
            title: d.name,
            icon
          });
          marker.addListener("click", () => {
            hideC2HeatHoverTip();
            infoWindow.setContent(buildDevicePopupHtml(d));
            infoWindow.open({ map, anchor: marker });
            scheduleC2ObliqueAfterAnchorChange();
            bindPopupDom(d);
          });
          markers.push(marker);
          deviceMarkerByDeviceId.set(d.id, marker);
        });

        const clusterMod = await ensureMarkerClustererModule();
        const ClusterClass = clusterMod?.MarkerClusterer;
        const defaultClusterClick = clusterMod?.defaultOnClusterClickHandler;
        if (ClusterClass && markers.length) {
          const onClusterClick =
            typeof defaultClusterClick === "function"
              ? (event, cluster, mapInstance) => {
                  defaultClusterClick(event, cluster, mapInstance);
                  google.maps.event.addListenerOnce(mapInstance, "idle", snapC2ObliqueCamera);
                  window.setTimeout(snapC2ObliqueCamera, 350);
                  window.setTimeout(snapC2ObliqueCamera, 800);
                }
              : undefined;
          markerClusterer = new ClusterClass({
            map,
            markers: [],
            ...(onClusterClick ? { onClusterClick } : {})
          });
        }

        lastMergedHeatSites = mergeHeatmapPointsForHover(
          data.alerts,
          data.violations,
          data.transactions
        );
        invalidateHeatHoverClusterCache();

        const alertWeighted = c2ToHeatmapWeightedLocations(data.alerts || []);
        const violWeighted = c2ToHeatmapWeightedLocations(data.violations || []);
        const txWeighted = c2ToHeatmapWeightedLocations(data.transactions || []);
        const maxAlertW = c2MaxHeatmapWeight(alertWeighted);
        const maxViolW = c2MaxHeatmapWeight(violWeighted);
        const maxTxW = c2MaxHeatmapWeight(txWeighted);

        if (google.maps.visualization) {
          heatmapAlerts = new google.maps.visualization.HeatmapLayer({
            data: alertWeighted,
            map: null,
            radius: 48,
            opacity: 0.92,
            maxIntensity: Math.max(1, maxAlertW * 0.92),
            dissipating: true,
            gradient: [
              "rgba(15, 23, 42, 0)",
              "rgba(254, 202, 202, 0.15)",
              "rgba(252, 165, 165, 0.38)",
              "rgba(248, 113, 113, 0.58)",
              "rgba(239, 68, 68, 0.78)",
              "rgba(220, 38, 38, 0.92)",
              "rgba(127, 29, 29, 0.98)"
            ]
          });

          heatmapViolations = new google.maps.visualization.HeatmapLayer({
            data: violWeighted,
            map: null,
            radius: 46,
            opacity: 0.88,
            maxIntensity: Math.max(1, maxViolW * 0.92),
            dissipating: true,
            gradient: [
              "rgba(15, 23, 42, 0)",
              "rgba(254, 249, 195, 0.18)",
              "rgba(253, 224, 71, 0.42)",
              "rgba(234, 179, 8, 0.62)",
              "rgba(202, 138, 4, 0.82)",
              "rgba(161, 98, 7, 0.94)",
              "rgba(113, 63, 18, 0.98)"
            ]
          });

          heatmapTransactions = new google.maps.visualization.HeatmapLayer({
            data: txWeighted,
            map: null,
            radius: 50,
            opacity: 0.88,
            maxIntensity: Math.max(1, maxTxW * 0.9),
            dissipating: true,
            /* Low density → green; high density → red (yellow/orange mid-band). */
            gradient: [
              "rgba(15, 23, 42, 0)",
              "rgba(20, 83, 45, 0.12)",
              "rgba(34, 197, 94, 0.28)",
              "rgba(132, 204, 22, 0.46)",
              "rgba(234, 179, 8, 0.62)",
              "rgba(249, 115, 22, 0.78)",
              "rgba(239, 68, 68, 0.9)",
              "rgba(185, 28, 28, 0.98)"
            ]
          });
        }

        applyLayerToggles();
        updateDeviceFilterMeta(data.meta);
      }

      const scheduleRefresh = debounceAsync(refreshMapData, 480);
      google.maps.event.addListener(infoWindow, "closeclick", () => {
        scheduleRefresh();
      });
      map.addListener("idle", scheduleRefresh);
      wireDeviceFilters(scheduleRefresh);
      wireHeatmapWindow(scheduleRefresh);
      wireToggles();
      wireSimulateLive();
      wireModals();

      if (pulseInterval) clearInterval(pulseInterval);
      pulseInterval = setInterval(() => {
        document.getElementById("commandControlMapShell")?.classList.toggle("c2-heat-pulse");
      }, 2800);
    } catch (err) {
      console.error("Traffic Monitoring Hub map init failed:", err);
      showToast("Map failed to initialize. See console for details.");
    }
  };
})();
