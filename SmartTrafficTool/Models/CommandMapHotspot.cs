using System.ComponentModel.DataAnnotations;

namespace SmartTrafficTool.Models;

public class CommandMapHotspot
{
    public int Id { get; set; }

    [StringLength(120)]
    public string Name { get; set; } = string.Empty;

    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double RadiusMeters { get; set; } = 800;

    [StringLength(32)]
    public string RiskLevel { get; set; } = "High";
}
