using Microsoft.AspNetCore.Mvc;

namespace SmartTrafficTool.Controllers;

public class AnalyticsController : Controller
{
    public IActionResult Index()
    {
        ViewData["Title"] = "Analytics Dashboard";
        return View();
    }
}
