using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTrafficTool.Data;
using SmartTrafficTool.Models;

namespace SmartTrafficTool.Controllers;

public class DevicesController : Controller
{
    private readonly AppDbContext _db;
    private static readonly string[] DeviceTypes =
    [
        "ANPR Camera",
        "Lidar",
        "Radar Sensor",
        "Environmental Sensor"
    ];

    private static readonly string[] StatusOptions =
    [
        "Online",
        "Maintenance",
        "Offline"
    ];

    private static readonly string[] LocationOptions =
    [
        "Doha",
        "Lusail",
        "Al Wakrah",
        "Al Khor",
        "Al Rayyan",
        "Mesaieed",
        "Umm Salal",
        "Qatar"
    ];

    public DevicesController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Device Management";
        var devices = await _db.Devices.AsNoTracking().OrderBy(d => d.Name).ToListAsync();
        return View(devices);
    }

    public async Task<IActionResult> Details(int id)
    {
        var device = await _db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
        if (device == null)
        {
            return NotFound();
        }

        return View(device);
    }

    public IActionResult Create()
    {
        LoadDropdownOptions();
        return View(new Device());
    }

    [HttpPost]
    public async Task<IActionResult> Create(Device device)
    {
        if (!ModelState.IsValid)
        {
            LoadDropdownOptions();
            return View(device);
        }

        device.LastSeenUtc = DateTime.UtcNow;
        _db.Devices.Add(device);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var device = await _db.Devices.FindAsync(id);
        if (device == null)
        {
            return NotFound();
        }

        LoadDropdownOptions();
        return View(device);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, Device device)
    {
        if (id != device.Id)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            LoadDropdownOptions();
            return View(device);
        }

        device.LastSeenUtc = DateTime.UtcNow;
        _db.Update(device);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int id)
    {
        var device = await _db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
        if (device == null)
        {
            return NotFound();
        }

        LoadDropdownOptions();
        return View(device);
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var device = await _db.Devices.FindAsync(id);
        if (device == null)
        {
            return NotFound();
        }

        _db.Devices.Remove(device);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> MapData()
    {
        var devices = await _db.Devices.AsNoTracking()
            .Select(d => new DeviceMapDto(
                d.Id,
                d.Name,
                d.Type,
                d.Status,
                d.LocationDescription ?? "Qatar",
                d.Latitude,
                d.Longitude,
                d.StreamUrl))
            .ToListAsync();

        return Json(devices);
    }

    [HttpGet]
    public async Task<IActionResult> MapDevice(int id)
    {
        var device = await _db.Devices.AsNoTracking()
            .Select(d => new DeviceMapDto(
                d.Id,
                d.Name,
                d.Type,
                d.Status,
                d.LocationDescription ?? "Qatar",
                d.Latitude,
                d.Longitude,
                d.StreamUrl))
            .FirstOrDefaultAsync(d => d.Id == id);

        if (device == null)
        {
            return NotFound();
        }

        return Json(device);
    }

    [HttpPost]
    public async Task<IActionResult> UpdateLocation([FromBody] DeviceLocationUpdate update)
    {
        var device = await _db.Devices.FindAsync(update.Id);
        if (device == null)
        {
            return NotFound();
        }

        device.Latitude = update.Latitude;
        device.Longitude = update.Longitude;
        device.LastSeenUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok();
    }

    [HttpPost]
    public async Task<IActionResult> CreateFromMap([FromBody] DeviceCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Type))
        {
            return BadRequest("Name and Type are required.");
        }

        var device = new Device
        {
            Name = request.Name.Trim(),
            Type = request.Type.Trim(),
            Status = string.IsNullOrWhiteSpace(request.Status) ? "Online" : request.Status.Trim(),
            LocationDescription = request.LocationDescription?.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            StreamUrl = request.StreamUrl,
            LastSeenUtc = DateTime.UtcNow
        };

        _db.Devices.Add(device);
        await _db.SaveChangesAsync();

        return Json(new DeviceMapDto(
            device.Id,
            device.Name,
            device.Type,
            device.Status,
            device.LocationDescription ?? "Qatar",
            device.Latitude,
            device.Longitude,
            device.StreamUrl));
    }

    [HttpPost]
    public async Task<IActionResult> UpdateFromMap([FromBody] DeviceUpdateRequest request)
    {
        var device = await _db.Devices.FindAsync(request.Id);
        if (device == null)
        {
            return NotFound();
        }

        device.Name = request.Name.Trim();
        device.Type = request.Type.Trim();
        device.Status = string.IsNullOrWhiteSpace(request.Status) ? device.Status : request.Status.Trim();
        device.LocationDescription = request.LocationDescription?.Trim();
        device.StreamUrl = request.StreamUrl?.Trim();
        device.Latitude = request.Latitude;
        device.Longitude = request.Longitude;
        device.LastSeenUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return Json(new DeviceMapDto(
            device.Id,
            device.Name,
            device.Type,
            device.Status,
            device.LocationDescription ?? "Qatar",
            device.Latitude,
            device.Longitude,
            device.StreamUrl));
    }

    public record DeviceMapDto(
        int Id,
        string Name,
        string Type,
        string Status,
        string Location,
        double Latitude,
        double Longitude,
        string? StreamUrl);

    public record DeviceLocationUpdate(int Id, double Latitude, double Longitude);

    public record DeviceCreateRequest(
        string Name,
        string Type,
        string? Status,
        string? LocationDescription,
        double Latitude,
        double Longitude,
        string? StreamUrl);

    public record DeviceUpdateRequest(
        int Id,
        string Name,
        string Type,
        string? Status,
        string? LocationDescription,
        double Latitude,
        double Longitude,
        string? StreamUrl);

    private void LoadDropdownOptions()
    {
        ViewBag.DeviceTypes = DeviceTypes;
        ViewBag.StatusOptions = StatusOptions;
        ViewBag.LocationOptions = LocationOptions;
    }
}
