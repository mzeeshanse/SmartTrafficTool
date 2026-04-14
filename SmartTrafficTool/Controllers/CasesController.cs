using Microsoft.AspNetCore.Mvc;

namespace SmartTrafficTool.Controllers;

public class CasesController : Controller
{
    public IActionResult Index()
    {
        ViewData["Title"] = "Case Files";
        return View();
    }
}
