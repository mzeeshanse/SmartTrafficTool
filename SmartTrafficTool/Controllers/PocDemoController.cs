using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTrafficTool.Data;
using SmartTrafficTool.Models;
using SmartTrafficTool.ViewModels;

namespace SmartTrafficTool.Controllers;

/// <summary>
/// POC-only route simulator: insert ANPR rows along a chosen camera path. Gated by Development or config.
/// </summary>
public class PocDemoController : Controller
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public PocDemoController(AppDbContext db, IWebHostEnvironment env, IConfiguration config)
    {
        _db = db;
        _env = env;
        _config = config;
    }

    private bool PocDemoToolsEnabled =>
        _env.IsDevelopment() || _config.GetValue<bool>("PocDemo:AllowAppendRecentData");

    private static List<int> ParseDeviceIdsFromCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return [];
        }

        return csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToList();
    }

    private async Task LoadSavedPathOptionsAsync(PocRouteSimulatorViewModel model)
    {
        var rows = await _db.PocSavedRoutes.AsNoTracking()
            .OrderBy(r => r.Name)
            .ToListAsync();
        model.SavedPaths = rows.Select(r => new PocSavedPathOption
        {
            Id = r.Id,
            Name = r.Name,
            DeviceIdsCsv = r.DeviceIdsCsv,
            CameraCount = string.IsNullOrWhiteSpace(r.DeviceIdsCsv)
                ? 0
                : r.DeviceIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries).Length
        }).ToList();
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? routeId)
    {
        if (!PocDemoToolsEnabled)
        {
            return NotFound();
        }

        var cameras = await _db.Devices.AsNoTracking()
            .Where(d => d.Type.Contains("Camera"))
            .OrderBy(d => d.Name)
            .ToListAsync();

        var model = new PocRouteSimulatorViewModel
        {
            AnprCameras = cameras,
            PlateText = "Q123456",
            PlateType = "Private",
            Country = "Qatar"
        };

        await LoadSavedPathOptionsAsync(model);

        if (routeId is > 0)
        {
            var route = await _db.PocSavedRoutes.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == routeId.Value);
            if (route != null)
            {
                model.PathDeviceIds = ParseDeviceIdsFromCsv(route.DeviceIdsCsv);
                model.SelectedSavedRouteId = route.Id;
            }
        }
        else if (model.SavedPaths.Count > 0)
        {
            var first = model.SavedPaths[0];
            model.PathDeviceIds = ParseDeviceIdsFromCsv(first.DeviceIdsCsv);
            model.SelectedSavedRouteId = first.Id;
        }

        ViewData["Title"] = "POC route simulator";
        ViewData["PageSubtitle"] = "Insert demo ANPR transactions along a camera path (last 24 hours UTC)";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePath(string? savePathName, [FromForm(Name = "pathDeviceIds")] List<int> pathDeviceIds)
    {
        if (!PocDemoToolsEnabled)
        {
            return NotFound();
        }

        pathDeviceIds ??= [];
        pathDeviceIds = pathDeviceIds.Where(id => id > 0).ToList();
        var name = (savePathName ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(name))
        {
            TempData["PocDemoError"] = "Enter a name before saving the path.";
            return RedirectToAction(nameof(Index));
        }

        if (pathDeviceIds.Count == 0)
        {
            TempData["PocDemoError"] = "Add at least one camera to the path before saving.";
            return RedirectToAction(nameof(Index));
        }

        if (name.Length > 120)
        {
            TempData["PocDemoError"] = "Path name must be 120 characters or fewer.";
            return RedirectToAction(nameof(Index));
        }

        var csv = string.Join(",", pathDeviceIds);
        var routes = await _db.PocSavedRoutes.ToListAsync();
        var existing = routes.FirstOrDefault(r =>
            string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.DeviceIdsCsv = csv;
            existing.CreatedUtc = DateTime.UtcNow;
        }
        else
        {
            _db.PocSavedRoutes.Add(new PocSavedRoute
            {
                Name = name,
                DeviceIdsCsv = csv,
                CreatedUtc = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        TempData["PocDemoSuccess"] = $"Saved path “{name}” ({pathDeviceIds.Count} camera(s)).";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(PocRouteSimulatorViewModel model)
    {
        if (!PocDemoToolsEnabled)
        {
            return NotFound();
        }

        model.AnprCameras = await _db.Devices.AsNoTracking()
            .Where(d => d.Type.Contains("Camera"))
            .OrderBy(d => d.Name)
            .ToListAsync();

        await LoadSavedPathOptionsAsync(model);

        model.PathDeviceIds ??= [];
        model.PathDeviceIds = model.PathDeviceIds.Where(id => id > 0).ToList();

        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "POC route simulator";
            ViewData["PageSubtitle"] = "Insert demo ANPR transactions along a camera path (last 24 hours UTC)";
            return View("Index", model);
        }

        if (model.PathDeviceIds.Count == 0)
        {
            ModelState.AddModelError(nameof(model.PathDeviceIds), "Add at least one camera to the path (in order).");
            ViewData["Title"] = "POC route simulator";
            ViewData["PageSubtitle"] = "Insert demo ANPR transactions along a camera path (last 24 hours UTC)";
            return View("Index", model);
        }

        var (inserted, error) = SeedData.AppendTransactionsAlongPath(
            _db,
            model.PlateText,
            model.PlateType,
            model.Country,
            model.PathDeviceIds,
            model.AlertOnLastSite,
            model.SpeedViolationOnLastSite);

        if (error != null)
        {
            ModelState.AddModelError(string.Empty, error);
            ViewData["Title"] = "POC route simulator";
            ViewData["PageSubtitle"] = "Insert demo ANPR transactions along a camera path (last 24 hours UTC)";
            return View("Index", model);
        }

        TempData["PocDemoSuccess"] =
            $"Inserted {inserted} transaction(s) for plate “{model.PlateText.Trim()}” along {model.PathDeviceIds.Count} camera(s). Times increase along the path; use Search with Last 24 Hours.";
        return RedirectToAction(nameof(Index));
    }
}
