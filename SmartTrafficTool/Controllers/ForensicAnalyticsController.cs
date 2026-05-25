using Microsoft.AspNetCore.Mvc;

namespace SmartTrafficTool.Controllers;

public class ForensicAnalyticsController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}
