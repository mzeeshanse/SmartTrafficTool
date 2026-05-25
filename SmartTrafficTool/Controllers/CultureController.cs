using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace SmartTrafficTool.Controllers;

public class CultureController : Controller
{
    /// <summary>Sets localization cookie (<see cref="CookieRequestCultureProvider"/>) and returns to the referrer path.</summary>
    [HttpGet]
    public IActionResult SetCulture(string culture, string? returnUrl = null)
    {
        if (!LocalizationConfig.SupportedCultureNames.Contains(culture))
        {
            culture = LocalizationConfig.DefaultCulture;
        }

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                Path = "/",
                HttpOnly = false,
                SameSite = SameSiteMode.Lax
            });

        if (string.IsNullOrEmpty(returnUrl))
        {
            returnUrl = Url.Content("~/");
        }
        else if (!Url.IsLocalUrl(returnUrl))
        {
            returnUrl = Url.Content("~/");
        }

        return LocalRedirect(returnUrl);
    }
}
