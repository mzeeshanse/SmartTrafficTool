using System.ComponentModel.DataAnnotations;
using SmartTrafficTool.Models;

namespace SmartTrafficTool.ViewModels;

public class PocRouteSimulatorViewModel
{
    [Required(ErrorMessage = "Plate text is required.")]
    [MaxLength(16)]
    public string PlateText { get; set; } = "";

    public string PlateType { get; set; } = "Private";

    public string Country { get; set; } = "Qatar";

    /// <summary>Ordered device IDs defining the vehicle path (first = entry, last = exit).</summary>
    public List<int> PathDeviceIds { get; set; } = [];

    public bool AlertOnLastSite { get; set; }

    public bool SpeedViolationOnLastSite { get; set; }

    /// <summary>ANPR cameras for building the path (GET + validation errors).</summary>
    public List<Device> AnprCameras { get; set; } = [];

    /// <summary>Saved paths for load dropdown.</summary>
    public List<PocSavedPathOption> SavedPaths { get; set; } = [];

    /// <summary>Which saved path is selected in the dropdown (GET: first path or ?routeId=).</summary>
    public int? SelectedSavedRouteId { get; set; }
}

public class PocSavedPathOption
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string DeviceIdsCsv { get; set; } = "";

    public int CameraCount { get; set; }
}
