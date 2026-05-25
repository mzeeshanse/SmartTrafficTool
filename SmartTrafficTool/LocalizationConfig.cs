namespace SmartTrafficTool;

public static class LocalizationConfig
{
    public const string DefaultCulture = "ar-QA";

    public static readonly HashSet<string> SupportedCultureNames =
    [
        "en-QA",
        "ar-QA",
    ];

    /// <returns>Supported <see cref="System.Globalization.CultureInfo"/> instances in UI order.</returns>
    public static System.Globalization.CultureInfo[] SupportedCultures() =>
    [
        System.Globalization.CultureInfo.GetCultureInfo("en-QA"),
        System.Globalization.CultureInfo.GetCultureInfo("ar-QA"),
    ];
}
