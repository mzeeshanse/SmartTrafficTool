using SmartTrafficTool.Models;

namespace SmartTrafficTool.ViewModels;

public class HybridSearchViewModel
{
    public string? Query { get; set; }

    public string? FilterType { get; set; }

    public string? FilterCountry { get; set; }

    public string? FilterWindow { get; set; }

    public int? FilterDeviceId { get; set; }

    /// <summary>When true, results are limited to rows with <c>IS_ALERTED</c>.</summary>
    public bool FilterAlertsOnly { get; set; }

    /// <summary>When true, results are limited to speed violations (e.g. ≥120 km/h), matching the monitoring hub.</summary>
    public bool FilterSpeedViolationsOnly { get; set; }

    public bool IsInvestigationMode { get; set; }

    public string CurrentView { get; set; } = "map";

    public List<Device> DeviceOptions { get; set; } = [];

    public List<SearchResultItem> Results { get; set; } = [];

    public int TotalResults { get; set; }

    public int AlertCount { get; set; }

    public int DistinctPlates { get; set; }

    public int AvgSpeed { get; set; }

    public string PlateTypeTop { get; set; } = "Private";

    public string PlateCountryTop { get; set; } = "Qatar";

    public string VehicleClassTop { get; set; } = "SUV";

    public int ViolationsCount { get; set; }

    public string SummaryText { get; set; } = string.Empty;

    public List<string> InsightBullets { get; set; } = [];

    public bool IsPlateQuery { get; set; }

    public string? SelectedPlate { get; set; }

    public List<string> Timeline { get; set; } = [];

    public List<string> CoTravelPlates { get; set; } = [];

    public string WantedFlag { get; set; } = "No Flag";

    public string OwnerInfo { get; set; } = "Owner: Verification pending";

    public string TrafficFines { get; set; } = "Fines: 0";

    public string LicensePoints { get; set; } = "Points: 0 / 24";

    public int CurrentPage { get; set; }

    public int PageSize { get; set; }

    public int TotalPages { get; set; }

    public double? LatestLat { get; set; }

    public double? LatestLng { get; set; }

    public string? LatestPlate { get; set; }

    public List<ChartSlice> PlateTypeBreakdown { get; set; } = [];

    public List<ChartSlice> PlateCountryBreakdown { get; set; } = [];

    public List<ChartSlice> VehicleClassBreakdown { get; set; } = [];

    public record ChartSlice(string Label, int Count);

    public class SearchResultItem
    {
        public ANPRTRAN Transaction { get; set; } = new();
        public ANPRTRAN_VEH_CLASSIFICATION? Classification { get; set; }
        public Device? Device { get; set; }
    }
}
