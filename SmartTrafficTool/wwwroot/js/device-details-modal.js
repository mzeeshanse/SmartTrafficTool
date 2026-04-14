/**
 * Bootstrap device details modal (stream + telemetry) shared by Devices/Index map and Devices/Details map.
 */
(function () {
  function deviceStatusClass(status) {
    const s = (status || "").toLowerCase();
    if (s.includes("online")) return "device-status device-status--online";
    if (s.includes("maintenance")) return "device-status device-status--maintenance";
    if (s.includes("offline")) return "device-status device-status--offline";
    return "device-status device-status--unknown";
  }

  function deviceModalCategory(type) {
    const t = (type || "").toLowerCase();
    if (t.includes("anpr") || t.includes("traffic")) return "anpr";
    if (t.includes("lidar") || t.includes("iot")) return "lidar";
    if (t.includes("radar")) return "radar";
    if (t.includes("environment")) return "env";
    return "env";
  }

  function deviceTelemetryMix(id) {
    const n = Number.isFinite(id) ? id : 1;
    return {
      a: (n * 17) % 23,
      b: (n * 31) % 19,
      c: (n * 13) % 11,
      d: (n * 7) % 17
    };
  }

  function setText(id, text) {
    const el = document.getElementById(id);
    if (el) {
      el.textContent = text;
    }
  }

  function populateLidarTelemetry(device) {
    const m = deviceTelemetryMix(device.id);
    const hz = 10 + (m.a % 9);
    const rangeM = 160 + m.b * 4;
    const pps = 1.1 + (m.c % 10) / 10;
    const objs = 6 + (m.d % 18);
    setText("lidarScanRate", `${hz} Hz (dual return)`);
    setText("lidarRange", `${rangeM} m`);
    setText("lidarPoints", `${pps.toFixed(1)}M pts/s`);
    setText("lidarObjects", `${objs} road users (last sweep)`);
    setText("lidarFov", `${-12 - (m.a % 5)}° to +${8 + (m.b % 4)}°`);
    setText("lidarWeather", m.c % 3 === 0 ? "Dust suppression on" : "Clear path filter");
    setText("lidarCalibration", `Boresight calibration OK • Last check ${3 + (m.d % 10)} days ago • Mount: ${device.name}`);
  }

  function populateRadarTelemetry(device) {
    const m = deviceTelemetryMix(device.id);
    const lanes = 3 + (m.a % 4);
    const rangeM = 220 + m.b * 8;
    setText("radarAccuracy", "±2 km/h (typical)");
    setText("radarRange", `${rangeM} m`);
    setText("radarLanes", `${lanes} lanes`);
    setText("radarDirection", m.c % 2 === 0 ? "Approach & recede" : "Bidirectional Doppler");
    setText("radarFirmware", `v2.${8 + (m.d % 3)}.${10 + (m.a % 5)}`);
    setText("radarInterference", m.b % 4 === 0 ? "Elevated — nearby worksite" : "Within spec");
    setText("radarAlertNote", `False-positive rate low • Health check passed • Asset ${device.name}`);
  }

  function populateEnvTelemetry(device) {
    const m = deviceTelemetryMix(device.id);
    const aqi = 52 + m.a + m.b;
    const pm = 14 + (m.a % 28);
    const noise = 56 + (m.b % 14);
    const temp = 31 + (m.c % 12);
    const hum = 38 + (m.d % 22);
    const wind = 8 + (m.a % 15);
    setText("envAqi", `${aqi} (moderate)`);
    setText("envPm25", `${pm} µg/m³`);
    setText("envNoise", `${noise} dB(A) (1 min avg)`);
    setText("envTemp", `${temp} °C`);
    setText("envHumidity", `${hum} %`);
    setText("envWind", `${wind} km/h`);
    setText("envCalibration", `Gas & particle sensors calibrated • ${5 + (m.c % 8)} days ago • ${device.location || "Qatar"}`);
  }

  function showDeviceModalSection(category, device) {
    const anpr = document.getElementById("deviceModalSectionAnpr");
    const lidar = document.getElementById("deviceModalSectionLidar");
    const radar = document.getElementById("deviceModalSectionRadar");
    const env = document.getElementById("deviceModalSectionEnv");
    [anpr, lidar, radar, env].forEach((el) => el?.classList.add("d-none"));
    if (category === "anpr") {
      anpr?.classList.remove("d-none");
    } else if (category === "lidar") {
      lidar?.classList.remove("d-none");
      populateLidarTelemetry(device);
    } else if (category === "radar") {
      radar?.classList.remove("d-none");
      populateRadarTelemetry(device);
    } else {
      env?.classList.remove("d-none");
      populateEnvTelemetry(device);
    }
  }

  function setDeviceModalTitleIcon(category) {
    const icon = document.getElementById("selectedDeviceTitleIcon");
    if (!icon) {
      return;
    }
    const fa =
      category === "anpr"
        ? "fa-video"
        : category === "lidar"
          ? "fa-layer-group"
          : category === "radar"
            ? "fa-tower-broadcast"
            : "fa-cloud-sun";
    icon.className = `fa-solid ${fa}`;
  }

  function deviceStatusRiskClass(status) {
    const s = (status || "").toLowerCase();
    if (s.includes("offline")) {
      return "map-popup-risk map-popup-risk--high";
    }
    if (s.includes("maintenance")) {
      return "map-popup-risk map-popup-risk--warn";
    }
    if (s.includes("online")) {
      return "map-popup-risk map-popup-risk--low";
    }
    return "map-popup-risk map-popup-risk--low";
  }

  function formatCoords(lat, lng) {
    const la = typeof lat === "number" ? lat : parseFloat(lat);
    const ln = typeof lng === "number" ? lng : parseFloat(lng);
    if (!Number.isFinite(la) || !Number.isFinite(ln)) {
      return "—";
    }
    return `${la.toFixed(5)}°, ${ln.toFixed(5)}°`;
  }

  function normalizeStreamUrl(rawUrl) {
    if (!rawUrl) {
      return "";
    }

    const url = rawUrl.trim();
    if (!url) {
      return "";
    }

    if (url.includes("youtube.com/embed/")) {
      return url;
    }

    const watchMatch = url.match(/v=([^&]+)/);
    if (watchMatch && watchMatch[1]) {
      return `https://www.youtube.com/embed/${watchMatch[1]}`;
    }

    const shortMatch = url.match(/youtu\.be\/([^?]+)/);
    if (shortMatch && shortMatch[1]) {
      return `https://www.youtube.com/embed/${shortMatch[1]}`;
    }

    return url;
  }

  function updateSelectedDevice(device) {
    const nameEl = document.getElementById("selectedDeviceName");
    const subEl = document.getElementById("selectedDeviceSub");
    const statusPill = document.getElementById("selectedDeviceStatusPill");
    const statusText = document.getElementById("selectedDeviceStatusText");
    const kpiType = document.getElementById("deviceKpiType");
    const kpiStatus = document.getElementById("deviceKpiStatus");
    const kpiZone = document.getElementById("deviceKpiZone");
    const locMeta = document.getElementById("selectedDeviceLocationMeta");
    const coordsEl = document.getElementById("selectedDeviceCoords");
    const detailEl = document.getElementById("selectedDeviceDetail");
    const tagRow = document.getElementById("deviceModalTagRow");
    const frame = document.getElementById("deviceStreamFrame");
    const category = deviceModalCategory(device.type);
    const modalEl = document.getElementById("deviceDetailsModal");
    const manageLink = document.getElementById("deviceModalManageLink");
    const viewLink = document.getElementById("deviceModalViewLink");
    const hero = document.getElementById("deviceModalHero");

    if (nameEl) {
      nameEl.textContent = device.name || "Device";
    }
    setDeviceModalTitleIcon(category);

    const loc = device.location || "Qatar";
    if (subEl) {
      subEl.textContent = `${device.type || "—"} • ${loc}`;
    }
    if (statusPill) {
      statusPill.className = deviceStatusRiskClass(device.status);
    }
    if (statusText) {
      statusText.textContent = device.status || "Unknown";
    }
    if (kpiType) {
      kpiType.textContent = device.type || "—";
    }
    if (kpiStatus) {
      kpiStatus.textContent = device.status || "—";
    }
    if (kpiZone) {
      kpiZone.textContent = loc;
    }
    if (locMeta) {
      locMeta.textContent = loc;
    }
    if (coordsEl) {
      coordsEl.textContent = formatCoords(device.latitude, device.longitude);
    }
    if (detailEl) {
      const cc = formatCoords(device.latitude, device.longitude);
      detailEl.textContent = `Device ID ${device.id ?? "—"} · Coordinates ${cc} · ${category === "anpr" ? "Video stream" : "Telemetry asset"}`;
    }
    if (tagRow) {
      if (category === "anpr") {
        tagRow.innerHTML = '<span class="map-popup-pill map-popup-pill--neutral">Live feed</span>';
      } else if (category === "lidar") {
        tagRow.innerHTML = '<span class="map-popup-pill map-popup-pill--neutral">LiDAR asset</span>';
      } else if (category === "radar") {
        tagRow.innerHTML = '<span class="map-popup-pill map-popup-pill--neutral">Radar asset</span>';
      } else {
        tagRow.innerHTML = '<span class="map-popup-pill map-popup-pill--neutral">Environment sensor</span>';
      }
    }

    const editTpl = modalEl?.getAttribute("data-device-url-edit") || "";
    const detailsTpl = modalEl?.getAttribute("data-device-url-details") || "";
    const idStr = String(device.id ?? "");
    if (manageLink && editTpl) {
      manageLink.href = editTpl.replace("999999", idStr);
    }
    if (viewLink && detailsTpl) {
      viewLink.href = detailsTpl.replace("999999", idStr);
    }

    if (hero) {
      hero.classList.toggle("d-none", category !== "anpr");
    }
    if (frame) {
      if (category === "anpr") {
        const streamUrl = normalizeStreamUrl(device.streamUrl);
        frame.src = streamUrl || "https://www.youtube.com/embed/8JCk5M_xrBs";
        frame.title = `${device.name} — stream`;
      } else {
        frame.src = "about:blank";
        frame.removeAttribute("title");
      }
    }
    showDeviceModalSection(category, device);
  }

  window.deviceStatusClass = deviceStatusClass;
  window.updateSelectedDevice = updateSelectedDevice;
  window.normalizeStreamUrl = normalizeStreamUrl;

  window.showDeviceDetailsModal = function (device) {
    updateSelectedDevice(device);
    const el = document.getElementById("deviceDetailsModal");
    const B = typeof window !== "undefined" && window.bootstrap;
    if (!el || !B || !B.Modal) {
      return;
    }
    B.Modal.getOrCreateInstance(el).show();
  };
})();
