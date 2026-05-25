using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SmartTrafficTool.Data;
using SmartTrafficTool.Models;
using SmartTrafficTool.ViewModels;

namespace SmartTrafficTool.Services;

public class CopilotIntentService : ICopilotIntentService
{
    private readonly AppDbContext _db;
    private readonly IStringLocalizer<SharedResource> _t;

    public CopilotIntentService(AppDbContext db, IStringLocalizer<SharedResource> localizer)
    {
        _db = db;
        _t = localizer;
    }

    public async Task<CopilotMessageResponse> ProcessAsync(string message, bool voiceInput, CancellationToken cancellationToken = default)
    {
        var raw = message.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return new CopilotMessageResponse
            {
                Reply = _t["Copilot_ReplyEmpty"].Value,
                Intent = "empty"
            };
        }

        var lower = raw.ToLowerInvariant();

        if (IsGreeting(lower))
        {
            return new CopilotMessageResponse
            {
                Reply = _t["Copilot_ReplyGreeting"].Value,
                Intent = "greeting",
                Speak = voiceInput
            };
        }

        if (lower is "help" or "?" || lower.StartsWith("help ") || lower.Contains("what can you"))
        {
            return HelpResponse(voiceInput);
        }

        if (ContainsAny(lower, "insight", "anomal", "briefing", "situation", "overview", "summary", "risk snapshot")
            || lower.StartsWith("search plate with text ", StringComparison.OrdinalIgnoreCase))
        {
            return await InsightsAsync(raw, voiceInput, cancellationToken);
        }

        var investigation =
            lower.Contains("investigation")
            || lower.Contains("investigate")
            || lower.Contains("forensic mode");

        if (TryOpenDeviceDetails(raw, lower, voiceInput, out var deviceResponse))
        {
            return deviceResponse;
        }

        // Navigation before broad search so phrases like "open all devices" are not turned into a text query.
        if (TryNavigate(lower, out var navHref, out var navLabel, out var navReply))
        {
            return new CopilotMessageResponse
            {
                Reply = navReply,
                Intent = "navigate",
                Actions =
                [
                    new CopilotActionDto { Type = "navigate", Label = navLabel, Href = navHref, Primary = true }
                ],
                Speak = voiceInput
            };
        }

        if (TryBuildSearch(raw, investigation, voiceInput, out var searchResponse))
        {
            return searchResponse;
        }

        return new CopilotMessageResponse
        {
            Reply = _t["Copilot_ReplyUnknown"].Value,
            Intent = "unknown",
            Speak = voiceInput
        };
    }

    private static bool IsGreeting(string lower) =>
        lower is "hi" or "hello" or "hey" ||
        lower.StartsWith("hi ") || lower.StartsWith("hello ") || lower.StartsWith("hey ");

    private CopilotMessageResponse HelpResponse(bool voice)
    {
        return new CopilotMessageResponse
        {
            Reply = _t["Copilot_HelpMarkdown"].Value,
            Intent = "help",
            Widgets =
            [
                new CopilotWidgetDto
                {
                    Type = "cards",
                    Title = _t["Copilot_WidgetHeadingTryThese"].Value,
                    Cards =
                    [
                        new CopilotCardItemDto { Label = _t["Copilot_HelpChipLabel_Search"].Value, Value = _t["Copilot_CardSearchExample"].Value },
                        new CopilotCardItemDto { Label = _t["Copilot_HelpChipLabel_Navigation"].Value, Value = _t["Copilot_CardNavigateExample"].Value },
                        new CopilotCardItemDto { Label = _t["Copilot_HelpChipLabel_Analytics"].Value, Value = _t["Copilot_CardAnalyzeExample"].Value }
                    ]
                }
            ],
            Speak = voice
        };
    }

    private bool TryNavigate(string lower, out string href, out string label, out string reply)
    {
        href = "";
        label = "";
        reply = "";

        var navTrim = lower.Trim();
        if (Regex.IsMatch(
                navTrim,
                @"^(launch\s+)?((forensic\s+)?analytics|ai\s+anomalies)(\s+(page|module|dashboard))?[\s!.]*$",
                RegexOptions.IgnoreCase)
            || navTrim.Equals("analytic", StringComparison.OrdinalIgnoreCase))
        {
            href = "/ForensicAnalytics";
            label = _t["Title_AIAnomalies"].Value;
            reply = string.Format(CultureInfo.CurrentUICulture, _t["Copilot_OpenFmt"].Value, label);
            return true;
        }

        var open = lower.Contains("open ") || lower.StartsWith("go to") || lower.Contains("show ") || lower.Contains("navigate ");

        if (
            open
            && lower.Contains("device")
            && !lower.Contains("forensic")
            && Regex.IsMatch(
                lower,
                @"\b(?:open|go\s+to|show|navigate\s+to)\s+(?:the\s+)?(?:all\s+)?devices?\b",
                RegexOptions.IgnoreCase))
        {
            href = "/Devices";
            label = _t["Title_DeviceManagement"].Value;
            reply = string.Format(CultureInfo.CurrentUICulture, _t["Copilot_OpenFmt"].Value, label);
            return true;
        }

        if (
            open
            && (
                lower.Contains("traffic monitoring")
                || lower.Contains("monitoring hub")
                || lower.Contains("command and control")
                || lower.Contains("open command")
                || lower.Contains("go to command")
                || (lower.Contains("command") && (lower.Contains("control") || lower.Contains("map")))
            ))
        {
            href = "/CommandAndControl";
            var hubTitle = _t["Title_TrafficMonitoringHub"].Value;
            label = hubTitle;
            reply = string.Format(CultureInfo.CurrentUICulture, _t["Copilot_OpenTrafficHubFmt"].Value, hubTitle);
            return true;
        }

        if (open && (lower.Contains("analytic") || lower.Contains("report") || Regex.IsMatch(lower, @"\bai\s+anomalies?\b")))
        {
            href = "/ForensicAnalytics";
            label = _t["Title_AIAnomalies"].Value;
            reply = string.Format(CultureInfo.CurrentUICulture, _t["Copilot_OpenFmt"].Value, label);
            return true;
        }

        if (open && lower.Contains("case"))
        {
            href = "/Cases";
            label = _t["Title_Cases"].Value;
            reply = string.Format(CultureInfo.CurrentUICulture, _t["Copilot_OpenCasesFmt"].Value, label);
            return true;
        }

        if ((open && lower.Contains("search") && !lower.Contains("for ")) || lower.Trim() is "search" or "forensic search")
        {
            href = "/Search";
            label = _t["Title_AIForensicSearch"].Value;
            reply = string.Format(CultureInfo.CurrentUICulture, _t["Copilot_OpenFmt"].Value, label);
            return true;
        }

        return false;
    }

    private static bool ContainsAny(string hay, params string[] needles) =>
        needles.Any(hay.Contains);

    /// <summary>
    /// Opens a specific device on Device Management before search NLP runs — otherwise digit patterns (e.g. 57)
    /// are treated as forensic plate search by <see cref="CopilotNlp.ParseSearchQuery"/>.
    /// </summary>
    private bool TryOpenDeviceDetails(string raw, string lower, bool voice, out CopilotMessageResponse response)
    {
        response = null!;

        if (!lower.Contains("device") && !lower.Contains("camera"))
        {
            return false;
        }

        if (lower.Contains("forensic") || lower.Contains("plate") || lower.Contains("anpr tran") || lower.Contains("search for plate"))
        {
            return false;
        }

        var idMatch = Regex.Match(
            raw,
            @"\b(?:open|show|go\s+to|navigate\s+to)\s+(?:the\s+)?(?:device|camera)\s+(?:#|number|no\.?|id)?\s*(\d{1,6})\b",
            RegexOptions.IgnoreCase);

        if (!idMatch.Success)
        {
            idMatch = Regex.Match(
                raw,
                @"\b(?:device|camera)\s*(?:#|number|no\.?|id)?\s*(\d{1,6})\b",
                RegexOptions.IgnoreCase);
        }

        if (!idMatch.Success)
        {
            idMatch = Regex.Match(
                raw,
                @"\bdevice\s+management(?:\s+page)?\s+(?:#|number|no\.?|id)?\s*(\d{1,6})\b",
                RegexOptions.IgnoreCase);
        }

        if (!idMatch.Success || !int.TryParse(idMatch.Groups[1].Value, out var id) || id <= 0)
        {
            return false;
        }

        response = new CopilotMessageResponse
        {
            Reply =
                string.Format(CultureInfo.CurrentUICulture, _t["Copilot_DeviceDetailReplyFmt"].Value, id),
            Intent = "navigate_device",
            Actions =
            [
                new CopilotActionDto
                {
                    Type = "navigate",
                    Label = string.Format(CultureInfo.CurrentUICulture, _t["Copilot_ActionOpenDeviceFmt"].Value, id),
                    Href = $"/Devices/Details/{id}",
                    Primary = true
                },
                new CopilotActionDto
                {
                    Type = "navigate",
                    Label = _t["Copilot_Devices_IndexLabel"].Value,
                    Href = "/Devices",
                    Primary = false
                }
            ],
            Speak = voice
        };

        return true;
    }

    private bool TryBuildSearch(string raw, bool investigation, bool voice, out CopilotMessageResponse response)
    {
        response = null!;
        var stripped = StripLeadPhrases(raw);
        stripped = StripSearchVerbs(stripped);
        var parsed = CopilotNlp.ParseSearchQuery(stripped);
        if (string.IsNullOrWhiteSpace(parsed.QueryText) &&
            string.IsNullOrWhiteSpace(parsed.PlateType) &&
            string.IsNullOrWhiteSpace(parsed.Country) &&
            string.IsNullOrWhiteSpace(parsed.Window))
        {
            return false;
        }

        var qs = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(parsed.QueryText))
        {
            qs.Append("q=").Append(Uri.EscapeDataString(parsed.QueryText));
        }

        void Add(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "All")
            {
                return;
            }

            if (qs.Length > 0)
            {
                qs.Append('&');
            }

            qs.Append(key).Append('=').Append(Uri.EscapeDataString(value));
        }

        Add("plateType", parsed.PlateType);
        Add("country", parsed.Country);
        Add("window", parsed.Window);
        if (investigation)
        {
            Add("investigationMode", "true");
        }

        Add("view", "map");

        var path = "/Search" + (qs.Length > 0 ? "?" + qs : "");
        var human = DescribeParsedQuery(parsed);
        response = new CopilotMessageResponse
        {
            Reply = string.Format(CultureInfo.CurrentUICulture, _t["Copilot_SearchRunningFmt"].Value, human),
            Intent = "search",
            Actions =
            [
                new CopilotActionDto { Type = "navigate", Label = _t["Copilot_Action_OpenSearchResults"].Value, Href = path, Primary = true }
            ],
            Speak = voice
        };
        return true;
    }

    private static string StripLeadPhrases(string raw)
    {
        var t = raw.Trim();
        var phrases = new[]
        {
            "investigate plate number ", "investigate plate no. ", "investigate plate no ", "investigate plate #",
            "investigate plate ",
            "search plate with text ", "search plate text ", "plate with text ",
            "open search for ", "go to search for ", "open forensic search for ",
            "search for ", "find ", "look up ", "look for ", "query "
        };
        foreach (var p in phrases.OrderByDescending(x => x.Length))
        {
            if (t.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            {
                t = t[p.Length..].Trim();
            }
        }

        return t;
    }

    private static string StripSearchVerbs(string raw)
    {
        var t = raw;
        foreach (var prefix in new[] { "search for ", "find ", "look up ", "look for ", "show me ", "query " })
        {
            if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                t = t[prefix.Length..];
            }
        }

        return t.Trim();
    }

    private string DescribeParsedQuery(CopilotNlp.ParsedSearchQuery p)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.QueryText))
        {
            parts.Add($"**{p.QueryText}**");
        }

        if (!string.IsNullOrWhiteSpace(p.PlateType))
        {
            parts.Add(string.Format(CultureInfo.CurrentUICulture, _t["Copilot_DescribeTypeFmt"].Value, p.PlateType));
        }

        if (!string.IsNullOrWhiteSpace(p.Country))
        {
            parts.Add(p.Country);
        }

        if (!string.IsNullOrWhiteSpace(p.Window))
        {
            parts.Add(string.Format(CultureInfo.CurrentUICulture, _t["Copilot_DescribeWindowFmt"].Value, p.Window));
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : _t["Copilot_QueryBroadScope"].Value;
    }

    private static DateTime ResolveInsightSince(string? window)
    {
        var now = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(window))
        {
            return now.AddHours(-24);
        }

        if (window.Equals("24h", StringComparison.OrdinalIgnoreCase))
        {
            return now.AddHours(-24);
        }

        if (window.Equals("today", StringComparison.OrdinalIgnoreCase))
        {
            return now.Date;
        }

        if (window.Equals("7d", StringComparison.OrdinalIgnoreCase))
        {
            return now.AddDays(-7);
        }

        return now.AddHours(-24);
    }

    private string DescribeInsightTimeLabel(string? window)
    {
        if (string.IsNullOrWhiteSpace(window) || window.Equals("24h", StringComparison.OrdinalIgnoreCase))
        {
            return _t["Copilot_Time_Last24Hours"].Value;
        }

        if (window.Equals("today", StringComparison.OrdinalIgnoreCase))
        {
            return _t["Copilot_Time_TodayUtcDay"].Value;
        }

        if (window.Equals("7d", StringComparison.OrdinalIgnoreCase))
        {
            return _t["Copilot_Time_Last7Days"].Value;
        }

        return _t["Copilot_Time_Last24Hours"].Value;
    }

    private static List<int> ResolvePlateTypeIds(string? plateTypeCsv)
    {
        var typeIds = new List<int>();
        if (string.IsNullOrWhiteSpace(plateTypeCsv))
        {
            return typeIds;
        }

        foreach (var token in plateTypeCsv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var typeId = token.ToLowerInvariant() switch
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

        return typeIds;
    }

    private static int ResolveOriginId(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return 0;
        }

        return country.ToLowerInvariant() switch
        {
            "qatar" => 1,
            "uae" => 2,
            "ksa" => 3,
            "saudi" => 3,
            "oman" => 4,
            _ => 0
        };
    }

    private static IQueryable<ANPRTRAN> ApplyInsightFilters(
        IQueryable<ANPRTRAN> query,
        CopilotNlp.ParsedSearchQuery p)
    {
        if (!string.IsNullOrWhiteSpace(p.QueryText))
        {
            var term = p.QueryText.Trim().ToLowerInvariant();
            query = query.Where(t =>
                (t.PLATE_TEXT ?? string.Empty).ToLower().Contains(term) ||
                t.PLATE_NUMBER.ToString().Contains(term) ||
                (t.PLATE_ALPHA ?? string.Empty).ToLower().Contains(term) ||
                (t.PLATE_ALPHA_EN ?? string.Empty).ToLower().Contains(term));
        }

        var typeIds = ResolvePlateTypeIds(p.PlateType);
        if (typeIds.Count > 0)
        {
            query = query.Where(t => typeIds.Contains(t.PLATE_TYPE_ID));
        }

        var originId = ResolveOriginId(p.Country);
        if (originId > 0)
        {
            query = query.Where(t => t.PLATE_ORIGIN_ID == originId);
        }

        return query;
    }

    private string DescribeInsightScope(CopilotNlp.ParsedSearchQuery p, string timeLabel)
    {
        var segments = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.Country))
        {
            segments.Add(string.Format(CultureInfo.CurrentUICulture, _t["Copilot_Insight_CountryFmt"].Value, p.Country));
        }

        if (!string.IsNullOrWhiteSpace(p.PlateType))
        {
            segments.Add(string.Format(CultureInfo.CurrentUICulture, _t["Copilot_Insight_TypesFmt"].Value, p.PlateType));
        }

        if (!string.IsNullOrWhiteSpace(p.QueryText))
        {
            segments.Add(string.Format(CultureInfo.CurrentUICulture, _t["Copilot_Insight_PlateFmt"].Value, p.QueryText));
        }

        segments.Add(timeLabel);
        return string.Join(" · ", segments);
    }

    private static string BuildInsightsSearchHref(CopilotNlp.ParsedSearchQuery p)
    {
        var qs = new StringBuilder();
        void Add(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "All")
            {
                return;
            }

            if (qs.Length > 0)
            {
                qs.Append('&');
            }

            qs.Append(key).Append('=').Append(Uri.EscapeDataString(value));
        }

        if (!string.IsNullOrWhiteSpace(p.QueryText))
        {
            qs.Append("q=").Append(Uri.EscapeDataString(p.QueryText));
        }

        Add("plateType", p.PlateType);
        Add("country", p.Country);
        var windowForUrl = string.IsNullOrWhiteSpace(p.Window) ? "24h" : p.Window;
        Add("window", windowForUrl);
        Add("view", "map");
        return "/Search" + (qs.Length > 0 ? "?" + qs : "?view=map");
    }

    private async Task<CopilotMessageResponse> InsightsAsync(string raw, bool voice, CancellationToken ct)
    {
        var parsed = CopilotNlp.ParseInsightsFilters(raw);
        var since = ResolveInsightSince(parsed.Window);
        var timeLabel = DescribeInsightTimeLabel(parsed.Window);

        var baseQ = _db.ANPRTRANS.AsNoTracking().Where(t => t.CREATED_DATE_TIME >= since);
        baseQ = ApplyInsightFilters(baseQ, parsed);

        var total = await baseQ.CountAsync(ct);
        var alerts = await baseQ.CountAsync(t => t.IS_ALERTED, ct);
        var alertRate = total == 0 ? 0 : Math.Round(100.0 * alerts / total, 1);

        var topPlates = await baseQ
            .Where(t => t.PLATE_TEXT != null && t.PLATE_TEXT != "")
            .GroupBy(t => t.PLATE_TEXT!)
            .OrderByDescending(g => g.Count())
            .Take(6)
            .Select(g => new { Plate = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var typeMix = await baseQ
            .GroupBy(t => t.PLATE_TYPE_ID)
            .Select(g => new { TypeId = g.Key, C = g.Count() })
            .ToListAsync(ct);

        var onlineDevices = await _db.Devices.CountAsync(d => d.Status == "Online", ct);
        var offlineDevices = await _db.Devices.CountAsync(d => d.Status == "Offline", ct);

        var anomalyNote = alertRate >= 18
            ? _t["Copilot_AlertNoteHigh"].Value
            : alertRate >= 10
                ? _t["Copilot_AlertNoteMedium"].Value
                : _t["Copilot_AlertNoteLow"].Value;

        var labels = typeMix.Select(t => t.TypeId switch
        {
            1 => _t["PlateType_Private"].Value,
            2 => _t["PlateType_Commercial"].Value,
            3 => _t["PlateType_Government"].Value,
            4 => _t["PlateType_Transport"].Value,
            5 => _t["PlateType_Taxi"].Value,
            _ => _t["Copilot_PlateKindOther"].Value
        }).ToList();

        var data = typeMix.Select(t => t.C).ToList();
        var colors = new List<string> { "#5b84a2", "#c55272", "#bb6bd9", "#27ae60", "#eb5757", "#8fa1bd" };

        var typeIdsFilter = ResolvePlateTypeIds(parsed.PlateType);
        var skipTypeChart = typeIdsFilter.Count == 1 || (labels.Count <= 1 && typeMix.Sum(x => x.C) > 0);

        var scopeLine = DescribeInsightScope(parsed, timeLabel);
        var pulseTitle = total == 0
            ? _t["Copilot_WidgetPulseNoDetections"].Value
            : _t["Copilot_WidgetPulseScoped"].Value;

        var widgets = new List<CopilotWidgetDto>
        {
            new()
            {
                Type = "cards",
                Title = pulseTitle,
                Cards =
                [
                    new CopilotCardItemDto { Label = _t["Copilot_WidgetLabelDetections"].Value, Value = total.ToString("N0", CultureInfo.CurrentUICulture), Hint = scopeLine },
                    new CopilotCardItemDto { Label = _t["Copilot_WidgetLabelAlerts"].Value, Value = alerts.ToString("N0", CultureInfo.CurrentUICulture), Hint = string.Format(CultureInfo.CurrentUICulture, _t["Copilot_AlertPctHintFmt"].Value, alertRate) },
                    new CopilotCardItemDto { Label = _t["Copilot_WidgetDevicesOnlineLabel"].Value, Value = onlineDevices.ToString("N0", CultureInfo.CurrentUICulture), Hint = string.Format(CultureInfo.CurrentUICulture, _t["Copilot_DevicesOfflineHintFmt"].Value, offlineDevices) }
                ]
            }
        };

        if (!skipTypeChart && labels.Count > 0 && data.Sum() > 0)
        {
            widgets.Add(new CopilotWidgetDto
            {
                Type = "chart",
                Title = _t["Copilot_ChartMixedPlateTypesScoped"].Value,
                Chart = new CopilotChartDto
                {
                    Kind = "doughnut",
                    Title = _t["Copilot_SecondarySubtitlePlateKinds"].Value,
                    Labels = labels,
                    Data = data,
                    Colors = colors.Take(labels.Count).ToList()
                }
            });
        }

        var tableTitle = string.IsNullOrWhiteSpace(parsed.QueryText)
            ? _t["Copilot_TableHotPlatesScoped"].Value
            : _t["Copilot_TableMatchedPlates"].Value;

        widgets.AddRange(new[]
        {
            new CopilotWidgetDto
            {
                Type = "table",
                Title = tableTitle,
                Table = new CopilotTableDto
                {
                    Headers = [_t["Copilot_Insight_Plates_Header"].Value, _t["Copilot_Insight_Hits_Header"].Value],
                    Rows = topPlates.Select(p => new List<string> { p.Plate, p.Count.ToString("N0", CultureInfo.CurrentUICulture) }).ToList()
                }
            },
            new CopilotWidgetDto
            {
                Type = "note",
                Note = total == 0
                    ? _t["Copilot_NoteNoRowsMatched"].Value
                    : anomalyNote
            }
        });

        var searchHref = BuildInsightsSearchHref(parsed);
        return new CopilotMessageResponse
        {
            Reply =
                total == 0
                    ? string.Format(CultureInfo.CurrentUICulture, _t["Copilot_InsightsEmptyReplyFmt"].Value, scopeLine)
                    : string.Format(CultureInfo.CurrentUICulture,
                        _t["Copilot_InsightsReplyFmt"].Value,
                        scopeLine,
                        total.ToString("N0", CultureInfo.CurrentUICulture),
                        alerts.ToString("N0", CultureInfo.CurrentUICulture),
                        anomalyNote),
            Intent = "insights",
            Actions =
            [
                new CopilotActionDto { Type = "navigate", Label = _t["Copilot_Action_ShowMatchingHits"].Value, Href = searchHref, Primary = true },
                new CopilotActionDto { Type = "navigate", Label = _t["Copilot_Action_ShowDeviceAtlas"].Value, Href = "/Devices" }
            ],
            Widgets = widgets,
            Speak = voice
        };
    }
}
