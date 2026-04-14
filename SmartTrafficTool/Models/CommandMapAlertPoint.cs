namespace SmartTrafficTool.Models;

public class CommandMapAlertPoint
{
    public int Id { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Intensity { get; set; } = 1;
}
