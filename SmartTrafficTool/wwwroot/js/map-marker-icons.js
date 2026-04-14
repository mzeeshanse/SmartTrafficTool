/**
 * Shared SVG map markers for Google Maps (data URLs). Requires google.maps when building icons.
 */
(function (global) {
  "use strict";

  function svgToDataUrl(svg) {
    return "data:image/svg+xml;charset=UTF-8," + encodeURIComponent(svg.replace(/\s+/g, " ").trim());
  }

  function statusStrokeHex(status) {
    const s = (status || "").toLowerCase();
    if (s.includes("online")) return "#27ae60";
    if (s.includes("maintenance")) return "#f2994a";
    if (s.includes("offline")) return "#eb5757";
    return "#8fa1bd";
  }

  function deviceFillHex(deviceType) {
    const t = (deviceType || "").toLowerCase();
    if (t.includes("anpr") || t.includes("camera") || t.includes("traffic")) return "#2d2610";
    if (t.includes("lidar")) return "#251a30";
    if (t.includes("radar")) return "#0f2418";
    if (t.includes("environment")) return "#0f1f28";
    return "#2a2218";
  }

  function deviceInnerGlyph(deviceType) {
    const t = (deviceType || "").toLowerCase();
    if (t.includes("anpr") || t.includes("camera") || t.includes("traffic")) {
      return (
        '<g transform="translate(0,1)">' +
        '<rect x="11" y="9" width="18" height="14" rx="2.5" fill="#102033" stroke="#ffffff" stroke-width="1.1"/>' +
        '<circle cx="20" cy="16" r="3.8" fill="#56ccf2"/>' +
        '<rect x="24" y="11" width="3" height="2" rx="0.5" fill="#f2c94c"/>' +
        "</g>"
      );
    }
    if (t.includes("lidar")) {
      return (
        '<g transform="translate(0,1)">' +
        '<ellipse cx="20" cy="14" rx="9" ry="4" fill="#1a2536" stroke="#bb6bd9" stroke-width="1.2"/>' +
        '<ellipse cx="20" cy="20" rx="7" ry="3" fill="#1a2536" stroke="#bb6bd9" stroke-width="1" opacity="0.85"/>' +
        '<circle cx="20" cy="17" r="2.5" fill="#bb6bd9"/>' +
        "</g>"
      );
    }
    if (t.includes("radar")) {
      return (
        '<g transform="translate(0,1)">' +
        '<path d="M20 10 L26 22 L14 22 Z" fill="#132a1f" stroke="#27ae60" stroke-width="1.2"/>' +
        '<circle cx="20" cy="18" r="2.8" fill="#27ae60"/>' +
        '<path d="M20 12 Q24 16 20 20 Q16 16 20 12" fill="none" stroke="#ffffff" stroke-width="0.9" opacity="0.7"/>' +
        "</g>"
      );
    }
    if (t.includes("environment")) {
      return (
        '<g transform="translate(0,1)">' +
        '<path d="M12 20 Q20 12 28 20 Q20 24 12 20" fill="#1a2536" stroke="#2d9cdb" stroke-width="1.1"/>' +
        '<circle cx="26" cy="12" r="3.5" fill="#f2c94c" stroke="#ffffff" stroke-width="0.8"/>' +
        "</g>"
      );
    }
    return (
      '<g transform="translate(0,1)">' +
      '<circle cx="20" cy="16" r="7" fill="#102033" stroke="#ffffff" stroke-width="1.3"/>' +
      '<circle cx="20" cy="16" r="3" fill="#f2994a"/>' +
      "</g>"
    );
  }

  /**
   * @param {typeof google.maps} gmaps
   * @param {string} deviceType
   * @param {string} status
   */
  function deviceIcon(gmaps, deviceType, status) {
    const stroke = statusStrokeHex(status);
    const fill = deviceFillHex(deviceType);
    const glyph = deviceInnerGlyph(deviceType);
    const w = 42;
    const h = 56;
    const svg =
      '<svg xmlns="http://www.w3.org/2000/svg" width="' +
      w +
      '" height="' +
      h +
      '" viewBox="0 0 42 56" fill="none">' +
      "<defs>" +
      '<filter id="mapMarkerDeviceShadow" x="-20%" y="-20%" width="140%" height="140%">' +
      '<feDropShadow dx="0" dy="5" stdDeviation="4" flood-color="rgba(0,0,0,0.4)"/>' +
      "</filter>" +
      "</defs>" +
      '<g filter="url(#mapMarkerDeviceShadow)">' +
      '<path d="M21 2.5C12.44 2.5 5.5 9.44 5.5 18c0 12 11.5 30.5 15.5 35.5 4-5 15.5-23.5 15.5-35.5C36.5 9.44 29.56 2.5 21 2.5z" ' +
      'fill="' +
      fill +
      '" stroke="' +
      stroke +
      '" stroke-width="2.5"/>' +
      glyph +
      "</g>" +
      "</svg>";

    return {
      url: svgToDataUrl(svg),
      scaledSize: new gmaps.Size(w, h),
      anchor: new gmaps.Point(w / 2, h - 3)
    };
  }

  function plateTypeIdFill(typeId) {
    switch (Number(typeId)) {
      case 1:
        return "#8fa1bd";
      case 2:
        return "#f2994a";
      case 3:
        return "#56ccf2";
      case 4:
        return "#bb6bd9";
      case 5:
        return "#f2c94c";
      default:
        return "#9db0ca";
    }
  }

  function plateTypeStringFill(plateType) {
    const t = (plateType || "").toLowerCase();
    if (t.includes("taxi")) return "#f2c94c";
    if (t.includes("transport")) return "#bb6bd9";
    if (t.includes("commercial")) return "#f2994a";
    if (t.includes("government")) return "#56ccf2";
    if (t.includes("private")) return "#8fa1bd";
    return "#9db0ca";
  }

  function originIdRing(originId) {
    switch (Number(originId)) {
      case 1:
        return "#c85a7a";
      case 2:
        return "#009a4e";
      case 3:
        return "#0d7d45";
      case 4:
        return "#d62828";
      default:
        return "#56ccf2";
    }
  }

  function countryStringRing(country) {
    const c = (country || "").toLowerCase();
    if (c.includes("qatar")) return "#c85a7a";
    if (c.includes("uae") || c.includes("emirates")) return "#009a4e";
    if (c.includes("ksa") || c.includes("saudi")) return "#0d7d45";
    if (c.includes("oman")) return "#d62828";
    return "#56ccf2";
  }

  /**
   * @param {typeof google.maps} gmaps
   * @param {{ plateType?: string, country?: string, alert?: boolean, plateTypeId?: number, originId?: number }} meta
   */
  function transactionPinIcon(gmaps, meta) {
    const m = meta || {};
    const pinFill =
      m.plateTypeId != null && Number(m.plateTypeId) > 0
        ? plateTypeIdFill(m.plateTypeId)
        : plateTypeStringFill(m.plateType);
    const ring =
      m.originId != null && Number(m.originId) > 0 ? originIdRing(m.originId) : countryStringRing(m.country);
    const alert = !!m.alert;
    const inner = alert
      ? '<text x="19" y="22" text-anchor="middle" fill="#ffffff" font-size="15" font-weight="800" font-family="system-ui,Arial,sans-serif">!</text>'
      : '<circle cx="19" cy="17" r="6" fill="#102033" stroke="#ffffff" stroke-width="1.4"/><circle cx="19" cy="17" r="2.6" fill="' +
        ring +
        '"/>';

    const w = 38;
    const h = 52;
    const svg =
      '<svg xmlns="http://www.w3.org/2000/svg" width="' +
      w +
      '" height="' +
      h +
      '" viewBox="0 0 38 52" fill="none">' +
      "<defs>" +
      '<filter id="mapMarkerTxShadow" x="0" y="0" width="38" height="52" filterUnits="userSpaceOnUse">' +
      '<feDropShadow dx="0" dy="7" stdDeviation="5" flood-color="rgba(0,0,0,0.38)"/>' +
      "</filter>" +
      "</defs>" +
      '<g filter="url(#mapMarkerTxShadow)">' +
      '<path d="M19 2C10.716 2 4 8.716 4 17c0 11.5 15 31 15 31s15-19.5 15-31C34 8.716 27.284 2 19 2z" ' +
      'fill="' +
      pinFill +
      '" stroke="none" stroke-width="0"/>' +
      inner +
      "</g>" +
      "</svg>";

    return {
      url: svgToDataUrl(svg),
      scaledSize: new gmaps.Size(w, h),
      anchor: new gmaps.Point(19, 48)
    };
  }

  global.MapMarkerIcons = {
    deviceIcon: deviceIcon,
    transactionPinIcon: transactionPinIcon
  };
})(typeof window !== "undefined" ? window : globalThis);
