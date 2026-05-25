using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTrafficTool.Data;
using SmartTrafficTool.ViewModels;

namespace SmartTrafficTool.Controllers;

public class SearchController : Controller
{
    private const int SpeedViolationThresholdKph = 120;

    private readonly AppDbContext _db;

    public SearchController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? q,
        string? plateType,
        string? plateTypeRaw,
        string? country,
        string? window,
        int? deviceId,
        bool alertsOnly = false,
        bool speedViolationsOnly = false,
        bool investigationMode = false,
        string? view = "map",
        int page = 1)
    {
        const int pageSize = 30;
        var safePage = page < 1 ? 1 : page;

        var parsedInput = ParseTextQuery(q);
        var query = _db.ANPRTRANS.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(parsedInput.QueryText))
        {
            var term = parsedInput.QueryText.Trim().ToLower();
            query = query.Where(t =>
                (t.PLATE_TEXT ?? string.Empty).ToLower().Contains(term) ||
                t.PLATE_NUMBER.ToString().Contains(term) ||
                (t.PLATE_ALPHA ?? string.Empty).ToLower().Contains(term));
        }

        var effectivePlateType = !string.IsNullOrWhiteSpace(plateTypeRaw) && !plateTypeRaw.Equals("All", StringComparison.OrdinalIgnoreCase)
            ? plateTypeRaw
            : (!string.IsNullOrWhiteSpace(plateType) && !plateType.Equals("All", StringComparison.OrdinalIgnoreCase)
                ? plateType
                : parsedInput.PlateType);
        if (!string.IsNullOrWhiteSpace(effectivePlateType) && !effectivePlateType.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            var typeTokens = effectivePlateType
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.ToLower())
                .ToList();

            var typeIds = new List<int>();
            foreach (var token in typeTokens)
            {
                var typeId = token switch
                {
                    "private" => 1,
                    "commercial" => 2,
                    "government" => 3,
                    "transport" => 4,
                    "taxi" => 5,
                    _ => 0
                };
                if (typeId > 0)
                {
                    typeIds.Add(typeId);
                }
            }

            if (typeIds.Count > 0)
            {
                query = query.Where(t => typeIds.Contains(t.PLATE_TYPE_ID));
            }
        }

        var effectiveCountry = string.IsNullOrWhiteSpace(country) ? parsedInput.Country : country;
        if (!string.IsNullOrWhiteSpace(effectiveCountry) && !effectiveCountry.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            var originId = effectiveCountry.ToLower() switch
            {
                "qatar" => 1,
                "uae" => 2,
                "ksa" => 3,
                "saudi" => 3,
                "oman" => 4,
                _ => 0
            };
            if (originId > 0)
            {
                query = query.Where(t => t.PLATE_ORIGIN_ID == originId);
            }
        }

        var effectiveWindow = string.IsNullOrWhiteSpace(window) ? parsedInput.Window : window;
        if (!string.IsNullOrWhiteSpace(effectiveWindow))
        {
            var now = DateTime.UtcNow;
            if (effectiveWindow.Equals("24h", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(t => t.CREATED_DATE_TIME >= now.AddHours(-24));
            }
            else if (effectiveWindow.Equals("today", StringComparison.OrdinalIgnoreCase))
            {
                var start = now.Date;
                query = query.Where(t => t.CREATED_DATE_TIME >= start);
            }
            else if (effectiveWindow.Equals("7d", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(t => t.CREATED_DATE_TIME >= now.AddDays(-7));
            }
            else if (effectiveWindow.Equals("48h", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(t => t.CREATED_DATE_TIME >= now.AddHours(-48));
            }
        }

        if (deviceId.HasValue && deviceId.Value > 0)
        {
            query = query.Where(t => t.DEVICE_ID == deviceId.Value);
        }

        if (alertsOnly)
        {
            query = query.Where(t => t.IS_ALERTED);
        }

        if (speedViolationsOnly)
        {
            query = query.Where(t => t.SPEED >= SpeedViolationThresholdKph);
        }

        var totalCount = await query.CountAsync();
        var allTransactions = await query.ToListAsync();
        var transactions = allTransactions
            .OrderByDescending(t => t.CREATED_DATE_TIME)
            .Skip((safePage - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var allTransactionIds = allTransactions.Select(t => t.TRANSACTION_ID).ToList();
        var allClassifications = await _db.ANPRTRAN_VEH_CLASSIFICATIONS.AsNoTracking()
            .Where(c => allTransactionIds.Contains(c.TRANSACTION_ID))
            .ToListAsync();

        var allDeviceIds = allTransactions.Select(t => t.DEVICE_ID).Distinct().ToList();
        var allDevices = await _db.Devices.AsNoTracking()
            .Where(d => allDeviceIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id);

        var allResults = allTransactions.Select(t => new HybridSearchViewModel.SearchResultItem
        {
            Transaction = t,
            Classification = allClassifications.FirstOrDefault(c => c.TRANSACTION_ID == t.TRANSACTION_ID),
            Device = allDevices.TryGetValue(t.DEVICE_ID, out var device) ? device : null
        }).ToList();

        var deviceIds = transactions.Select(t => t.DEVICE_ID).Distinct().ToList();
        var devices = await _db.Devices.AsNoTracking()
            .Where(d => deviceIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id);

        var results = transactions.Select(t => new HybridSearchViewModel.SearchResultItem
        {
            Transaction = t,
            Classification = allClassifications.FirstOrDefault(c => c.TRANSACTION_ID == t.TRANSACTION_ID),
            Device = devices.TryGetValue(t.DEVICE_ID, out var device) ? device : null
        }).ToList();

        var plateTypeGroups = allResults
            .GroupBy(r => r.Transaction.PLATE_TYPE_ID)
            .Select(g => new { TypeId = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToList();

        var plateCountryGroups = allResults
            .GroupBy(r => r.Transaction.PLATE_ORIGIN_ID)
            .Select(g => new { OriginId = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToList();

        var plateTypeLeader = MapPlateType(plateTypeGroups.FirstOrDefault()?.TypeId ?? 1);
        var plateTypeTop = plateTypeGroups.Count > 1
            ? $"Mixed (Top: {plateTypeLeader})"
            : plateTypeLeader;

        var plateCountryLeader = MapPlateCountry(plateCountryGroups.FirstOrDefault()?.OriginId ?? 1);
        var plateCountryTop = plateCountryGroups.Count > 1
            ? $"Mixed (Top: {plateCountryLeader})"
            : plateCountryLeader;

        var vehicleClassGroups = allResults
            .GroupBy(r => r.Classification?.VEH_TYPE_ID ?? 0)
            .OrderByDescending(g => g.Count())
            .Select(g => new { ClassId = g.Key, Count = g.Count() })
            .ToList();

        var vehicleClassLeader = MapVehicleClass(vehicleClassGroups.FirstOrDefault()?.ClassId ?? 2);
        var vehicleClassTop = vehicleClassGroups.Count > 1
            ? $"Mixed (Top: {vehicleClassLeader})"
            : vehicleClassLeader;

        var violationCount = allResults.Count(r => r.Transaction.IS_ALERTED);

        var topPlate = allResults
            .GroupBy(r => r.Transaction.PLATE_TEXT)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

        var normalizedQuery = q?.Trim();
        var selectedPlate = !string.IsNullOrWhiteSpace(normalizedQuery)
            ? allResults.FirstOrDefault(r => string.Equals(r.Transaction.PLATE_TEXT, normalizedQuery, StringComparison.OrdinalIgnoreCase))?.Transaction.PLATE_TEXT
                ?? allResults.FirstOrDefault(r => (r.Transaction.PLATE_TEXT ?? string.Empty).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))?.Transaction.PLATE_TEXT
            : topPlate;

        var isPlateQuery = !string.IsNullOrWhiteSpace(selectedPlate) && !string.IsNullOrWhiteSpace(q);
        var timeline = new List<string>();
        var coTravelPlates = new List<string>();

        if (!string.IsNullOrWhiteSpace(selectedPlate))
        {
            var plateTransactions = allTransactions
                .Where(t => t.PLATE_TEXT == selectedPlate)
                .OrderByDescending(t => t.CREATED_DATE_TIME)
                .Take(6)
                .ToList();

            timeline = plateTransactions
                .Select(t =>
                {
                    allDevices.TryGetValue(t.DEVICE_ID, out var device);
                    var deviceLabel = device?.Name ?? $"Device {t.DEVICE_ID}";
                    return $"{t.CREATED_DATE_TIME.ToLocalTime():HH:mm} — {deviceLabel} — Speed {t.SPEED} km/h";
                })
                .ToList();

            var latest = plateTransactions.FirstOrDefault()?.CREATED_DATE_TIME ?? DateTime.UtcNow;
            var windowStart = latest.AddHours(-2);
            coTravelPlates = allTransactions
                .Where(t => t.CREATED_DATE_TIME >= windowStart && t.CREATED_DATE_TIME <= latest)
                .Select(t => t.PLATE_TEXT ?? string.Empty)
                .Where(t => t != selectedPlate && t != string.Empty)
                .Distinct()
                .Take(5)
                .ToList();
        }

        var summaryText = selectedPlate == null
            ? "Search results are ready for investigation and risk profiling."
            : $"Plate {selectedPlate} shows repeated activity with flagged events in the last 24 hours.";

        var insightBullets = new List<string>
        {
            $"{allResults.Count} detections in scope",
            $"{violationCount} violations flagged",
            $"{vehicleClassTop} dominates the classification mix"
        };

        if (plateTypeGroups.Count > 1)
        {
            var typeBreakdown = string.Join(", ",
                plateTypeGroups
                    .Select(g => $"{g.Count} {MapPlateType(g.TypeId)}"));
            insightBullets.Add($"Plate types: {typeBreakdown}");
        }

        if (plateCountryGroups.Count > 1)
        {
            var countryBreakdown = string.Join(", ",
                plateCountryGroups
                    .Select(g => $"{g.Count} {MapPlateCountry(g.OriginId)}"));
            insightBullets.Add($"Plate countries: {countryBreakdown}");
        }

        var deviceOptions = await _db.Devices.AsNoTracking()
            .Where(d => d.Type.ToLower().Contains("anpr"))
            .OrderBy(d => d.Name)
            .ToListAsync();

        var model = new HybridSearchViewModel
        {
            Query = parsedInput.QueryText,
            FilterType = effectivePlateType,
            FilterCountry = effectiveCountry,
            FilterWindow = effectiveWindow,
            FilterDeviceId = deviceId,
            FilterAlertsOnly = alertsOnly,
            FilterSpeedViolationsOnly = speedViolationsOnly,
            IsInvestigationMode = investigationMode,
            CurrentView = string.IsNullOrWhiteSpace(view) ? "map" : view,
            DeviceOptions = deviceOptions,
            Results = results,
            TotalResults = totalCount,
            AlertCount = allResults.Count(r => r.Transaction.IS_ALERTED),
            DistinctPlates = allResults.Select(r => r.Transaction.PLATE_TEXT).Distinct().Count(),
            AvgSpeed = allResults.Count == 0 ? 0 : (int)Math.Round(allResults.Average(r => r.Transaction.SPEED)),
            PlateTypeTop = plateTypeTop,
            PlateCountryTop = plateCountryTop,
            VehicleClassTop = vehicleClassTop,
            ViolationsCount = violationCount,
            SummaryText = summaryText,
            InsightBullets = insightBullets,
            IsPlateQuery = isPlateQuery,
            SelectedPlate = selectedPlate,
            Timeline = timeline,
            CoTravelPlates = coTravelPlates,
            WantedFlag = violationCount > 2 ? "Wanted List" : "No Flag",
            OwnerInfo = selectedPlate == null ? "Owner: Verification pending" : $"Owner: {selectedPlate} (Pending)",
            TrafficFines = $"Fines: QAR {(violationCount * 250)}",
            LicensePoints = $"Points: {Math.Min(violationCount * 2, 24)} / 24",
            CurrentPage = safePage,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            LatestLat = allResults
                .OrderByDescending(r => r.Transaction.CREATED_DATE_TIME)
                .Select(r => r.Device?.Latitude)
                .FirstOrDefault(),
            LatestLng = allResults
                .OrderByDescending(r => r.Transaction.CREATED_DATE_TIME)
                .Select(r => r.Device?.Longitude)
                .FirstOrDefault(),
            LatestPlate = allResults
                .OrderByDescending(r => r.Transaction.CREATED_DATE_TIME)
                .Select(r => r.Transaction.PLATE_TEXT)
                .FirstOrDefault(),
            PlateTypeBreakdown = plateTypeGroups
                .Select(g => new HybridSearchViewModel.ChartSlice(MapPlateType(g.TypeId), g.Count))
                .ToList(),
            PlateCountryBreakdown = plateCountryGroups
                .Select(g => new HybridSearchViewModel.ChartSlice(MapPlateCountry(g.OriginId), g.Count))
                .ToList(),
            VehicleClassBreakdown = vehicleClassGroups
                .Select(g => new HybridSearchViewModel.ChartSlice(MapVehicleClass(g.ClassId), g.Count))
                .ToList()
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> MapMarkers(
        string? q,
        string? plateType,
        string? country,
        string? window,
        int? deviceId,
        bool alertsOnly = false,
        bool speedViolationsOnly = false)
    {
        var parsedInput = ParseTextQuery(q);
        var query = _db.ANPRTRANS.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(parsedInput.QueryText))
        {
            var term = parsedInput.QueryText.Trim().ToLower();
            query = query.Where(t =>
                (t.PLATE_TEXT ?? string.Empty).ToLower().Contains(term) ||
                t.PLATE_NUMBER.ToString().Contains(term) ||
                (t.PLATE_ALPHA ?? string.Empty).ToLower().Contains(term));
        }

        var effectivePlateType = string.IsNullOrWhiteSpace(plateType) ? parsedInput.PlateType : plateType;
        if (!string.IsNullOrWhiteSpace(effectivePlateType) && !effectivePlateType.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            var typeTokens = effectivePlateType
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.ToLower())
                .ToList();

            var typeIds = new List<int>();
            foreach (var token in typeTokens)
            {
                var typeId = token switch
                {
                    "private" => 1,
                    "commercial" => 2,
                    "government" => 3,
                    "transport" => 4,
                    "taxi" => 5,
                    _ => 0
                };
                if (typeId > 0)
                {
                    typeIds.Add(typeId);
                }
            }

            if (typeIds.Count > 0)
            {
                query = query.Where(t => typeIds.Contains(t.PLATE_TYPE_ID));
            }
        }

        var effectiveCountry = string.IsNullOrWhiteSpace(country) ? parsedInput.Country : country;
        if (!string.IsNullOrWhiteSpace(effectiveCountry) && !effectiveCountry.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            var originId = effectiveCountry.ToLower() switch
            {
                "qatar" => 1,
                "uae" => 2,
                "ksa" => 3,
                "saudi" => 3,
                "oman" => 4,
                _ => 0
            };
            if (originId > 0)
            {
                query = query.Where(t => t.PLATE_ORIGIN_ID == originId);
            }
        }

        var effectiveWindow = string.IsNullOrWhiteSpace(window) ? parsedInput.Window : window;
        if (!string.IsNullOrWhiteSpace(effectiveWindow))
        {
            var now = DateTime.UtcNow;
            if (effectiveWindow.Equals("24h", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(t => t.CREATED_DATE_TIME >= now.AddHours(-24));
            }
            else if (effectiveWindow.Equals("today", StringComparison.OrdinalIgnoreCase))
            {
                var start = now.Date;
                query = query.Where(t => t.CREATED_DATE_TIME >= start);
            }
            else if (effectiveWindow.Equals("7d", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(t => t.CREATED_DATE_TIME >= now.AddDays(-7));
            }
            else if (effectiveWindow.Equals("48h", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(t => t.CREATED_DATE_TIME >= now.AddHours(-48));
            }
        }

        if (deviceId.HasValue && deviceId.Value > 0)
        {
            query = query.Where(t => t.DEVICE_ID == deviceId.Value);
        }

        if (alertsOnly)
        {
            query = query.Where(t => t.IS_ALERTED);
        }

        if (speedViolationsOnly)
        {
            query = query.Where(t => t.SPEED >= SpeedViolationThresholdKph);
        }

        var transactions = await query.ToListAsync();
        var transactionIds = transactions.Select(t => t.TRANSACTION_ID).ToList();
        var classifications = await _db.ANPRTRAN_VEH_CLASSIFICATIONS.AsNoTracking()
            .Where(c => transactionIds.Contains(c.TRANSACTION_ID))
            .ToDictionaryAsync(c => c.TRANSACTION_ID);
        var deviceIds = transactions.Select(t => t.DEVICE_ID).Distinct().ToList();
        var devices = await _db.Devices.AsNoTracking()
            .Where(d => deviceIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id);

        var markers = transactions
            .Select(t =>
            {
                devices.TryGetValue(t.DEVICE_ID, out var device);
                classifications.TryGetValue(t.TRANSACTION_ID, out var classification);
                return new
                {
                    plate = t.PLATE_TEXT ?? "Plate",
                    device = device?.Name ?? $"Device {t.DEVICE_ID}",
                    lat = device?.Latitude,
                    lng = device?.Longitude,
                    time = t.CREATED_DATE_TIME,
                    speed = t.SPEED,
                    status = t.TRANSACTION_STATUS,
                    alert = t.IS_ALERTED,
                    vehicle = MapVehicleClass(classification?.VEH_TYPE_ID ?? 0),
                    plateType = MapPlateType(t.PLATE_TYPE_ID),
                    country = MapPlateCountry(t.PLATE_ORIGIN_ID),
                    plateTypeId = t.PLATE_TYPE_ID,
                    originId = t.PLATE_ORIGIN_ID
                };
            })
            .Where(m => m.lat.HasValue && m.lng.HasValue)
            .Select(m => new
            {
                m.plate,
                m.device,
                lat = m.lat!.Value,
                lng = m.lng!.Value,
                time = m.time,
                m.speed,
                m.status,
                m.alert,
                m.vehicle,
                m.plateType,
                m.country,
                m.plateTypeId,
                m.originId
            })
            .ToList();

        return Json(markers);
    }

    private static ParsedInput ParseTextQuery(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new ParsedInput();
        }

        var text = input.ToLowerInvariant();
        var plateTypeTokens = new List<string>();
        if (text.Contains("taxi") || text.Contains("taxis") || text.Contains("taxes") || text.Contains("taxies"))
        {
            plateTypeTokens.Add("Taxi");
        }
        if (text.Contains("transport"))
        {
            plateTypeTokens.Add("Transport");
        }
        if (text.Contains("commercial"))
        {
            plateTypeTokens.Add("Commercial");
        }
        if (text.Contains("government"))
        {
            plateTypeTokens.Add("Government");
        }
        if (text.Contains("private"))
        {
            plateTypeTokens.Add("Private");
        }

        var plateType = plateTypeTokens.Count > 0 ? string.Join(", ", plateTypeTokens) : string.Empty;

        var country = text.Contains("qatar") ? "Qatar"
            : text.Contains("saudi") || text.Contains("ksa") ? "KSA"
            : text.Contains("uae") || text.Contains("emirates") ? "UAE"
            : text.Contains("oman") ? "Oman"
            : string.Empty;

        var window = text.Contains("last 24") || text.Contains("24 hours") ? "24h"
            : text.Contains("today") ? "today"
            : text.Contains("7 days") || text.Contains("last 7") ? "7d"
            : string.Empty;

        var plateMatch = System.Text.RegularExpressions.Regex.Match(input, @"[A-Z]{2,4}[-\s]?[A-Z]?\d{3,5}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (plateMatch.Success)
        {
            return new ParsedInput(plateMatch.Value.Replace(" ", string.Empty), plateType, country, window);
        }

        var digitMatch = System.Text.RegularExpressions.Regex.Match(input, @"\b(\d{2,5})\b");
        if (digitMatch.Success)
        {
            return new ParsedInput(digitMatch.Groups[1].Value, plateType, country, window);
        }

        var keywords = new[]
        {
            "taxi", "taxis", "taxes", "taxies", "transport", "commercial", "government", "private",
            "qatar", "ksa", "saudi", "uae", "emirates", "oman", "last 24", "24 hours", "today", "7 days", "last 7"
        };

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hasOnlyKeywords = keywords.Any(k => text.Contains(k)) && words.Length <= 5;
        return new ParsedInput(hasOnlyKeywords ? string.Empty : input, plateType, country, window);
    }

    private record ParsedInput(string? QueryText = null, string? PlateType = null, string? Country = null, string? Window = null);

    private static string MapPlateType(int id)
    {
        return id switch
        {
            1 => "Private",
            2 => "Commercial",
            3 => "Government",
            4 => "Transport",
            5 => "Taxi",
            _ => "Private"
        };
    }

    private static string MapPlateCountry(int id)
    {
        return id switch
        {
            1 => "Qatar",
            2 => "UAE",
            3 => "KSA",
            4 => "Oman",
            _ => "Qatar"
        };
    }

    private static string MapVehicleClass(int id)
    {
        return id switch
        {
            1 => "Sedan",
            2 => "SUV",
            3 => "Pickup",
            4 => "Bus",
            5 => "Truck",
            _ => "SUV"
        };
    }
}
