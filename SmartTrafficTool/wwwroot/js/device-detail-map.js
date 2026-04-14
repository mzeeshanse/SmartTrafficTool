(function () {
  const qatarCenter = { lat: 25.2854, lng: 51.531 };

  const mapStyles = [
    { elementType: "geometry", stylers: [{ color: "#0b1424" }] },
    { elementType: "labels.text.stroke", stylers: [{ color: "#0b1424" }] },
    { elementType: "labels.text.fill", stylers: [{ color: "#8fa1bd" }] },
    { featureType: "administrative", elementType: "geometry", stylers: [{ color: "#223149" }] },
    { featureType: "administrative", elementType: "labels.text.fill", stylers: [{ visibility: "off" }] },
    { featureType: "poi", elementType: "geometry", stylers: [{ color: "#101c30" }] },
    { featureType: "poi", elementType: "labels", stylers: [{ visibility: "off" }] },
    { featureType: "poi.park", elementType: "geometry", stylers: [{ color: "#0f1f2c" }] },
    { featureType: "road", elementType: "geometry", stylers: [{ color: "#1a2536" }] },
    { featureType: "road", elementType: "geometry.stroke", stylers: [{ color: "#1f2f45" }] },
    { featureType: "road.highway", elementType: "geometry", stylers: [{ color: "#283349" }] },
    { featureType: "road.highway", elementType: "geometry.stroke", stylers: [{ color: "#2c3c58" }] },
    { featureType: "transit", elementType: "geometry", stylers: [{ color: "#162233" }] },
    { featureType: "transit", elementType: "labels", stylers: [{ visibility: "off" }] },
    { featureType: "water", elementType: "geometry", stylers: [{ color: "#0a1f33" }] },
    { featureType: "water", elementType: "labels.text.fill", stylers: [{ visibility: "off" }] },
    { featureType: "road", elementType: "labels.text.fill", stylers: [{ visibility: "on" }] },
    { featureType: "road", elementType: "labels.icon", stylers: [{ visibility: "off" }] },
    { featureType: "administrative.locality", elementType: "labels.text.fill", stylers: [{ visibility: "on" }] }
  ];

  function buildDeviceIcon(device) {
    if (window.MapMarkerIcons) {
      return MapMarkerIcons.deviceIcon(google.maps, device.type, device.status);
    }
    return {
      path: google.maps.SymbolPath.CIRCLE,
      fillColor: "#f2c94c",
      fillOpacity: 0.95,
      strokeColor: "#27ae60",
      strokeOpacity: 1,
      strokeWeight: 3,
      scale: 10
    };
  }

  function num(v) {
    const n = typeof v === "number" ? v : parseFloat(v);
    return Number.isFinite(n) ? n : NaN;
  }

  function hasValidCoords(lat, lng) {
    return Number.isFinite(lat) && Number.isFinite(lng) && (Math.abs(lat) > 1e-6 || Math.abs(lng) > 1e-6);
  }

  /** Shape expected by showDeviceDetailsModal / updateSelectedDevice (matches MapDevice JSON). */
  function normalizeDeviceDto(raw) {
    if (!raw || typeof raw !== "object") {
      return null;
    }
    const lat = num(raw.latitude);
    const lng = num(raw.longitude);
    const id = parseInt(String(raw.id), 10);
    return {
      id: Number.isFinite(id) ? id : 0,
      name: raw.name || "Device",
      type: raw.type || "",
      status: raw.status || "",
      location: raw.location || raw.locationDescription || "Qatar",
      latitude: lat,
      longitude: lng,
      streamUrl: raw.streamUrl || null
    };
  }

  function showModal(deviceDto) {
    const fn = window.showDeviceDetailsModal;
    if (typeof fn === "function") {
      fn(deviceDto);
      return;
    }
    const u = window.bootstrap && window.bootstrap.Modal;
    if (deviceDto && typeof window.updateSelectedDevice === "function" && u) {
      window.updateSelectedDevice(deviceDto);
      const el = document.getElementById("deviceDetailsModal");
      if (el) {
        u.getOrCreateInstance(el).show();
      }
    }
  }

  async function openDeviceDetailsFromMap(shell, fallbackDto) {
    const baseUrl = shell && shell.getAttribute("data-map-device-url");
    const idAttr = shell && shell.getAttribute("data-device-id");
    let dto = fallbackDto;

    if (baseUrl && idAttr != null && idAttr !== "") {
      const sep = baseUrl.indexOf("?") >= 0 ? "&" : "?";
      const url = baseUrl + sep + "id=" + encodeURIComponent(idAttr);
      try {
        const res = await fetch(url, { credentials: "same-origin", headers: { Accept: "application/json" } });
        if (res.ok) {
          const json = await res.json();
          dto = normalizeDeviceDto(json) || dto;
        }
      } catch (err) {
        console.error("Device Details map: MapDevice fetch failed", err);
      }
    }

    if (dto) {
      showModal(dto);
    }
  }

  window.initDeviceDetailMap = function initDeviceDetailMap() {
    const mapEl = document.getElementById("deviceDetailMap");
    const jsonEl = document.getElementById("deviceDetailMapJson");
    const shell = document.querySelector(".device-detail-map-shell");
    if (!mapEl || typeof google === "undefined" || !google.maps) {
      return;
    }

    let raw;
    try {
      raw = JSON.parse(jsonEl ? jsonEl.textContent : "{}");
    } catch {
      return;
    }

    const device = normalizeDeviceDto(raw);
    if (!device) {
      return;
    }

    const valid = hasValidCoords(device.latitude, device.longitude);
    const center = valid ? { lat: device.latitude, lng: device.longitude } : qatarCenter;

    const map = new google.maps.Map(mapEl, {
      center,
      zoom: valid ? 16 : 11,
      disableDoubleClickZoom: true,
      streetViewControl: false,
      mapTypeControl: true,
      mapTypeControlOptions: {
        style: google.maps.MapTypeControlStyle.DEFAULT,
        position: google.maps.ControlPosition.LEFT_TOP,
        mapTypeIds: [google.maps.MapTypeId.ROADMAP, google.maps.MapTypeId.SATELLITE]
      },
      zoomControl: true,
      zoomControlOptions: { position: google.maps.ControlPosition.RIGHT_CENTER },
      fullscreenControl: true,
      fullscreenControlOptions: { position: google.maps.ControlPosition.RIGHT_TOP },
      controlSize: 40,
      styles: mapStyles
    });

    if (!valid) {
      const note = document.getElementById("deviceDetailMapNote");
      if (note) note.hidden = false;
      return;
    }

    const marker = new google.maps.Marker({
      position: center,
      map,
      title: (device.name || "Device") + " — click for details",
      icon: buildDeviceIcon(device),
      zIndex: 999999,
      clickable: true,
      optimized: false,
      animation: google.maps.Animation.DROP
    });

    let openLock = false;
    function openFromMap() {
      if (openLock) {
        return;
      }
      openLock = true;
      openDeviceDetailsFromMap(shell, device).finally(function () {
        setTimeout(function () {
          openLock = false;
        }, 350);
      });
    }

    marker.addListener("click", openFromMap);
    map.addListener("click", openFromMap);
  };
})();

if (typeof globalThis !== "undefined" && window.initDeviceDetailMap) {
  globalThis.initDeviceDetailMap = window.initDeviceDetailMap;
}
