using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
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
    private readonly IStringLocalizer<SharedResource> _t;

    public PocDemoController(
        AppDbContext db,
        IWebHostEnvironment env,
        IConfiguration config,
        IStringLocalizer<SharedResource> t)
    {
        _db = db;
        _env = env;
        _config = config;
        _t = t;
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
            Country = "Qatar",
            CapturedDate = DateOnly.FromDateTime(DateTime.UtcNow)
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
            TempData["PocDemoError"] = _t["Poc_Err_NameRequired"].Value;
            return RedirectToAction(nameof(Index));
        }

        if (pathDeviceIds.Count == 0)
        {
            TempData["PocDemoError"] = _t["Poc_Err_MinOneCameraSave"].Value;
            return RedirectToAction(nameof(Index));
        }

        if (name.Length > 120)
        {
            TempData["PocDemoError"] = _t["Poc_Err_NameLength"].Value;
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
        TempData["PocDemoSuccess"] = string.Format(CultureInfo.CurrentUICulture,
            _t["Poc_Success_SavedFmt"].Value,
            name,
            pathDeviceIds.Count);
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

        if (model.CapturedDate == default)
        {
            model.CapturedDate = DateOnly.FromDateTime(DateTime.UtcNow);
        }

        if (!ModelState.IsValid)
        {
            return View("Index", model);
        }

        if (model.PathDeviceIds.Count == 0)
        {
            ModelState.AddModelError(nameof(model.PathDeviceIds), _t["Poc_Err_AddCameraPathGenerate"].Value);
            return View("Index", model);
        }

        var (inserted, error) = SeedData.AppendTransactionsAlongPath(
            _db,
            model.PlateText,
            model.PlateType,
            model.Country,
            model.PathDeviceIds,
            model.AlertOnLastSite,
            model.SpeedViolationOnLastSite,
            model.CapturedDate);

        if (error != null)
        {
            ModelState.AddModelError(string.Empty, error);
            return View("Index", model);
        }

        var dateStr = model.CapturedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        TempData["PocDemoSuccess"] = string.Format(CultureInfo.CurrentUICulture,
            _t["Poc_Success_InsertedFmt"].Value,
            inserted,
            model.PlateText.Trim(),
            dateStr,
            model.PathDeviceIds.Count);

        return RedirectToAction(nameof(Index));
    }
}
