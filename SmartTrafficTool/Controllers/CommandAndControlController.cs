using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTrafficTool.Data;
using SmartTrafficTool.Models;

namespace SmartTrafficTool.Controllers;

public class CommandAndControlController : Controller
{
    private const int DefaultDeviceLimit = 600;
    private const int MaxDeviceLimit = 2000;
    private const int DefaultHeatmapFromHoursAgo = 48;
    private const int DefaultHeatmapToHoursAgo = 0;
    private const int MaxHeatmapFromHoursAgo = 168;
    private const int SpeedViolationThresholdKph = 120;
    private const int MaxIncidentTransactionsPerSite = 1;

    private readonly AppDbContext _db;

    public CommandAndControlController(AppDbContext db)
    {
        _db = db;
    }

    public IActionResult Index()
    {
        ViewData["Title"] = "Traffic Monitoring Hub";
        ViewData["PageSubtitle"] = "Live situational awareness — devices, alerts, violations, and ANPR transaction intensity";
        return View();
    }

    /// <summary>
    /// Map payloads. Devices are filtered by viewport (north/south/east/west) and optional status/type/name filters — not the full fleet at once.
    /// </summary>
    [HttpGet]
    public IActionResult MapData(
        double? north,
        double? south,
        double? east,
        double? west,
        string? status,
        string? types,
        string? q,
        int? deviceLimit = null,
        int? heatmapFromHoursAgo = null,
        int? heatmapToHoursAgo = null)
    {
        var limit = Math.Clamp(deviceLimit ?? DefaultDeviceLimit, 1, MaxDeviceLimit);
        NormalizeHeatmapWindow(heatmapFromHoursAgo, heatmapToHoursAgo, out var heatFrom, out var heatTo);
        var heatmapWindowHours = heatFrom - heatTo;
        var heatmapRangeLabel = BuildHeatmapRangeLabel(heatFrom, heatTo);
        var hasBounds = TryNormalizeBounds(north, south, east, west, out var n, out var s, out var e, out var w);

        if (!hasBounds)
        {
            return Json(new
            {
                devices = Array.Empty<object>(),
                alerts = Array.Empty<object>(),
                violations = Array.Empty<object>(),
                transactions = Array.Empty<object>(),
                incidentAlertSites = Array.Empty<object>(),
                incidentViolationSites = Array.Empty<object>(),
                meta = new
                {
                    viewportApplied = false,
                    deviceCount = 0,
                    deviceLimit = limit,
                    deviceLimitReached = false,
                    heatmapFromHoursAgo = heatFrom,
                    heatmapToHoursAgo = heatTo,
                    heatmapWindowHours,
                    heatmapRangeLabel,
                    message = "Pan or zoom the map to load devices and heatmaps for the visible area."
                }
            });
        }

        IQueryable<Device> deviceQuery = _db.Devices.AsNoTracking()
            .Where(d => d.Latitude >= s && d.Latitude <= n && d.Longitude >= w && d.Longitude <= e);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var parts = status
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.ToLowerInvariant())
                .Distinct()
                .ToList();
            if (parts.Count > 0)
            {
                deviceQuery = deviceQuery.Where(d => parts.Contains(d.Status.ToLower()));
            }
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var qq = q.Trim();
            deviceQuery = deviceQuery.Where(d => d.Name.ToLower().Contains(qq.ToLower()));
        }

        var typeTokens = (types ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0)
            .Select(t => t.ToLowerInvariant())
            .Distinct()
            .ToList();

        // Widen scan slightly before in-memory type OR-filter (EF-friendly).
        var rawDevices = deviceQuery.OrderBy(d => d.Id).Take(MaxDeviceLimit).ToList();
        if (typeTokens.Count > 0)
        {
            rawDevices = rawDevices
                .Where(d => typeTokens.Any(t => d.Type.Contains(t, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        var deviceLimitReached = rawDevices.Count > limit;
        var deviceList = rawDevices
            .Take(limit)
            .Select(d => new
            {
                d.Id,
                d.Name,
                d.Type,
                d.Status,
                d.Latitude,
                d.Longitude,
                d.LocationDescription
            })
            .ToList();

        var anprDevicesForAgg = rawDevices
            .Where(d => IsAnprCameraType(d.Type))
            .Select(d => new { d.Id, d.Name, d.Latitude, d.Longitude })
            .ToList();
        var anprIdSet = anprDevicesForAgg.Select(x => x.Id).ToHashSet();

        var now = DateTime.UtcNow;
        var since = now.AddHours(-heatFrom);
        var until = heatTo <= 0 ? now : now.AddHours(-heatTo);
        List<(long TransactionId, int DeviceId, DateTime When, string? Plate, int Speed, bool IsAlerted)> relevantTx;
        if (anprIdSet.Count == 0)
        {
            relevantTx = new List<(long, int, DateTime, string?, int, bool)>();
        }
        else
        {
            var rows = _db.ANPRTRANS.AsNoTracking()
                .Where(t => anprIdSet.Contains(t.DEVICE_ID) && t.CREATED_DATE_TIME >= since && t.CREATED_DATE_TIME <= until)
                .Select(t => new
                {
                    t.TRANSACTION_ID,
                    t.DEVICE_ID,
                    t.CREATED_DATE_TIME,
                    t.PLATE_TEXT,
                    t.SPEED,
                    t.IS_ALERTED
                })
                .ToList();
            relevantTx = rows
                .Select(t => (
                    TransactionId: t.TRANSACTION_ID,
                    DeviceId: t.DEVICE_ID,
                    When: t.CREATED_DATE_TIME,
                    Plate: t.PLATE_TEXT,
                    Speed: t.SPEED,
                    IsAlerted: t.IS_ALERTED))
                .ToList();
        }

        var alertCounts = relevantTx
            .Where(t => t.IsAlerted)
            .GroupBy(t => t.DeviceId)
            .ToDictionary(g => g.Key, g => g.Count());

        var violationCounts = relevantTx
            .Where(t => t.Speed >= SpeedViolationThresholdKph)
            .GroupBy(t => t.DeviceId)
            .ToDictionary(g => g.Key, g => g.Count());

        var transactionCounts = relevantTx
            .GroupBy(t => t.DeviceId)
            .ToDictionary(g => g.Key, g => g.Count());

        // Heatmap pixel intensity scales with weight; use a mild power curve so low counts still differ
        // while busy sites read clearly hotter (Google Maps sums weighted contributions per pixel).
        static float HeatmapWeight(int count)
        {
            var c = Math.Max(count, 1);
            var scaled = MathF.Pow(c, 0.92f) * 1.35f;
            return Math.Min(Math.Max(scaled, 1f), 220f);
        }

        var alerts = new List<object>();
        var violations = new List<object>();
        var transactions = new List<object>();
        foreach (var dev in anprDevicesForAgg)
        {
            if (alertCounts.TryGetValue(dev.Id, out var ac) && ac > 0)
            {
                alerts.Add(new
                {
                    lat = dev.Latitude,
                    lng = dev.Longitude,
                    count = ac,
                    weight = HeatmapWeight(ac)
                });
            }

            if (violationCounts.TryGetValue(dev.Id, out var vc) && vc > 0)
            {
                violations.Add(new
                {
                    lat = dev.Latitude,
                    lng = dev.Longitude,
                    count = vc,
                    weight = HeatmapWeight(vc)
                });
            }

            if (transactionCounts.TryGetValue(dev.Id, out var tc) && tc > 0)
            {
                transactions.Add(new
                {
                    lat = dev.Latitude,
                    lng = dev.Longitude,
                    count = tc,
                    weight = HeatmapWeight(tc)
                });
            }
        }

        var incidentAlertSites = new List<object>();
        var incidentViolationSites = new List<object>();
        foreach (var dev in anprDevicesForAgg)
        {
            var alertTx = relevantTx
                .Where(t => t.DeviceId == dev.Id && t.IsAlerted)
                .OrderByDescending(t => t.When)
                .Take(MaxIncidentTransactionsPerSite)
                .ToList();
            if (alertTx.Count > 0)
            {
                var alertItems = alertTx
                    .Select(t => BuildAlertIncidentItem(t, SpeedViolationThresholdKph))
                    .ToList();
                incidentAlertSites.Add(new
                {
                    deviceId = dev.Id,
                    name = dev.Name,
                    lat = dev.Latitude,
                    lng = dev.Longitude,
                    count = alertItems.Count,
                    items = alertItems
                });
            }

            var violTx = relevantTx
                .Where(t => t.DeviceId == dev.Id && t.Speed >= SpeedViolationThresholdKph)
                .OrderByDescending(t => t.When)
                .Take(MaxIncidentTransactionsPerSite)
                .ToList();
            if (violTx.Count > 0)
            {
                var violItems = violTx
                    .Select(t => BuildViolationIncidentItem(t, SpeedViolationThresholdKph))
                    .ToList();
                incidentViolationSites.Add(new
                {
                    deviceId = dev.Id,
                    name = dev.Name,
                    lat = dev.Latitude,
                    lng = dev.Longitude,
                    count = violItems.Count,
                    items = violItems
                });
            }
        }

        return Json(new
        {
            devices = deviceList,
            alerts,
            violations,
            transactions,
            incidentAlertSites,
            incidentViolationSites,
            meta = new
            {
                viewportApplied = hasBounds,
                deviceCount = deviceList.Count,
                deviceLimit = limit,
                deviceLimitReached,
                heatmapFromHoursAgo = heatFrom,
                heatmapToHoursAgo = heatTo,
                heatmapWindowHours,
                heatmapRangeLabel,
                incidentLookbackHours = heatmapWindowHours,
                speedViolationThresholdKph = SpeedViolationThresholdKph,
                message = (string?)null
            }
        });
    }

    private static void NormalizeHeatmapWindow(int? fromHoursAgo, int? toHoursAgo, out int fromAg, out int toAg)
    {
        fromAg = Math.Clamp(fromHoursAgo ?? DefaultHeatmapFromHoursAgo, 1, MaxHeatmapFromHoursAgo);
        toAg = Math.Clamp(toHoursAgo ?? DefaultHeatmapToHoursAgo, 0, MaxHeatmapFromHoursAgo - 1);
        if (toAg >= fromAg)
        {
            toAg = fromAg - 1;
            if (toAg < 0)
            {
                toAg = 0;
                fromAg = 1;
            }
        }
    }

    private static string BuildHeatmapRangeLabel(int fromAg, int toAg)
    {
        if (toAg <= 0)
        {
            return fromAg == 1 ? "Last hour" : $"Last {fromAg} hours";
        }

        return $"From {fromAg}h ago → {toAg}h ago";
    }

    private static bool IsAnprCameraType(string? type) =>
        !string.IsNullOrWhiteSpace(type) && type.Contains("ANPR", StringComparison.OrdinalIgnoreCase);

    private static object BuildAlertIncidentItem(
        (long TransactionId, int DeviceId, DateTime When, string? Plate, int Speed, bool IsAlerted) t,
        int speedThresholdKph)
    {
        var alsoSpeedViol = t.Speed >= speedThresholdKph;
        var detail = alsoSpeedViol
            ? $"Plate {t.Plate ?? "—"} · {t.Speed} km/h · flagged read"
            : $"Plate {t.Plate ?? "—"} · flagged read";

        return new
        {
            kind = "alert",
            title = "Alert",
            detail,
            transactionId = t.TransactionId,
            plate = t.Plate,
            speedKph = t.Speed,
            occurredUtc = t.When.ToUniversalTime().ToString("O")
        };
    }

    private static object BuildViolationIncidentItem(
        (long TransactionId, int DeviceId, DateTime When, string? Plate, int Speed, bool IsAlerted) t,
        int speedThresholdKph)
    {
        var detail = t.IsAlerted
            ? $"Plate {t.Plate ?? "—"} · {t.Speed} km/h (≥{speedThresholdKph}) · also flagged"
            : $"Plate {t.Plate ?? "—"} · {t.Speed} km/h (≥{speedThresholdKph})";

        return new
        {
            kind = "violation",
            title = "Speed violation",
            detail,
            transactionId = t.TransactionId,
            plate = t.Plate,
            speedKph = t.Speed,
            occurredUtc = t.When.ToUniversalTime().ToString("O")
        };
    }

    private static bool TryNormalizeBounds(double? north, double? south, double? east, double? west, out double n, out double s, out double e, out double w)
    {
        n = s = e = w = 0;
        if (!north.HasValue || !south.HasValue || !east.HasValue || !west.HasValue)
        {
            return false;
        }

        n = north.Value;
        s = south.Value;
        e = east.Value;
        w = west.Value;

        if (double.IsNaN(n) || double.IsNaN(s) || double.IsNaN(e) || double.IsNaN(w))
        {
            return false;
        }

        if (n <= s)
        {
            return false;
        }

        // Reject absurd spans (bad client input)
        if (n - s > 25 || Math.Abs(e - w) > 40)
        {
            return false;
        }

        return true;
    }
}
