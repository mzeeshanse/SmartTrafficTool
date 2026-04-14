using Microsoft.AspNetCore.Mvc;

namespace SmartTrafficTool.Controllers;

public class ForensicAnalyticsController : Controller
{
    public IActionResult Index()
    {
        ViewData["Title"] = "Analytics";
        ViewData["PageSubtitle"] = "AI-powered anomaly detection and behavioral analysis";
        return View();
    }
}
