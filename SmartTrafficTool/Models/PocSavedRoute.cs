using System.ComponentModel.DataAnnotations;

namespace SmartTrafficTool.Models;

/// <summary>
/// POC-only: named ordered list of device IDs for the route simulator.
/// </summary>
public class PocSavedRoute
{
    public int Id { get; set; }

    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = "";

    /// <summary>Comma-separated device IDs in travel order (e.g. "12,45,78").</summary>
    [Required]
    [MaxLength(2000)]
    public string DeviceIdsCsv { get; set; } = "";

    public DateTime CreatedUtc { get; set; }
}
