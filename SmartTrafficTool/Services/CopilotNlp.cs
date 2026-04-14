using System.Text.RegularExpressions;

namespace SmartTrafficTool.Services;

/// <summary>
/// Natural-language parsing aligned with forensic search POC (regex + keyword filters).
/// </summary>
public static class CopilotNlp
{
    /// <summary>
    /// Removes leading insight/summary phrasing so <see cref="ParseSearchQuery"/> sees plate type, country, window, and plate text.
    /// </summary>
    public static string StripInsightsPrefix(string? rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return string.Empty;
        }

        var t = rawMessage.Trim();
        var leads = new[]
        {
            "give me a summary of ", "give me summary of ", "show me a summary of ", "show me summary of ", "show summary of ",
            "summary of plate with text ", "summary for plate with text ",
            "search plate with text ", "search plate text ",
            "summary of ", "summary for ", "summarize ", "summarise ",
            "insight summary for ", "insights summary for ", "insight summary of ", "insights summary of ",
            "insights on ", "insights for ", "insight on ", "insight for ",
            "overview of ", "overview for ",
            "briefing on ", "briefing for ", "briefing about ",
            "anomaly summary for ", "anomalies for ", "anomaly insights for ", "anomaly overview for ",
            "situation overview for ", "situation summary for ", "risk snapshot for ",
            "co-pilot insights for ", "copilot insights for ",
            "tell me about ", "what about ",
            "summary ", "insights ", "insight ", "overview ", "briefing "
        };

        foreach (var p in leads.OrderByDescending(x => x.Length))
        {
            if (t.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            {
                t = t[p.Length..].Trim();
            }
        }

        return t.Trim();
    }

    public static ParsedSearchQuery ParseInsightsFilters(string? rawMessage)
    {
        return ParseSearchQuery(StripInsightsPrefix(rawMessage));
    }

    public static ParsedSearchQuery ParseSearchQuery(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new ParsedSearchQuery();
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

        var window = text.Contains("last 24") || text.Contains("24 hour") || text.Contains("24 hours") ? "24h"
            : text.Contains("today") ? "today"
            : text.Contains("7 days") || text.Contains("last 7") || Regex.IsMatch(text, @"\b7\s+days?\b") ? "7d"
            : string.Empty;

        var plateMatch = Regex.Match(input, @"[A-Z]{2,4}[-\s]?[A-Z]?\d{3,5}", RegexOptions.IgnoreCase);
        if (plateMatch.Success)
        {
            return new ParsedSearchQuery(plateMatch.Value.Replace(" ", string.Empty), plateType, country, window);
        }

        for (var digitMatch = Regex.Match(input, @"\b(\d{2,8})\b"); digitMatch.Success; digitMatch = digitMatch.NextMatch())
        {
            var val = digitMatch.Groups[1].Value;
            if (val == "24" && (text.Contains("24 hour") || text.Contains("24 hours") || text.Contains("last 24")))
            {
                continue;
            }

            return new ParsedSearchQuery(val, plateType, country, window);
        }

        var keywords = new[]
        {
            "taxi", "taxis", "taxes", "taxies", "transport", "commercial", "government", "private",
            "qatar", "ksa", "saudi", "uae", "emirates", "oman", "last 24", "24 hour", "24 hours", "today", "7 days", "last 7",
            "insight", "insights",
            "search", "find", "look", "plate", "show", "list"
        };

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hasOnlyKeywords = keywords.Any(k => text.Contains(k)) && words.Length <= 8;
        return new ParsedSearchQuery(hasOnlyKeywords ? string.Empty : input.Trim(), plateType, country, window);
    }

    public record ParsedSearchQuery(string? QueryText = null, string? PlateType = null, string? Country = null, string? Window = null);
}
