using System.ComponentModel.DataAnnotations;

namespace SmartTrafficTool.Models;

public class Device
{
    public int Id { get; set; }

    [Required]
    [StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Type { get; set; } = "ANPR";

    [Required]
    [StringLength(30)]
    public string Status { get; set; } = "Online";

    [StringLength(200)]
    public string? StreamUrl { get; set; }

    [StringLength(120)]
    public string? LocationDescription { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
}
