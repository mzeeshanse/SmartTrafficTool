using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartTrafficTool.Data;
using SmartTrafficTool.Services;
using SmartTrafficTool;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture(LocalizationConfig.DefaultCulture);
    options.SupportedCultures = LocalizationConfig.SupportedCultures().ToList();
    options.SupportedUICultures = LocalizationConfig.SupportedCultures().ToList();
    options.ApplyCurrentCultureToResponseHeaders = true;
    options.FallBackToParentCultures = true;
    options.FallBackToParentUICultures = true;
    options.RequestCultureProviders.Clear();
    options.RequestCultureProviders.Add(new CookieRequestCultureProvider());
    options.RequestCultureProviders.Add(new QueryStringRequestCultureProvider());
    options.RequestCultureProviders.Add(new AcceptLanguageHeaderRequestCultureProvider());
});

builder.Services.AddControllersWithViews()
    .AddDataAnnotationsLocalization(options =>
        options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(SharedResource)));

builder.Services.AddScoped<ICopilotIntentService, CopilotIntentService>();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    SeedData.EnsureSeeded(db);
    SeedData.NormalizeDeviceTypes(db);
    SeedData.NormalizeDeviceStatuses(db);
    SeedData.UpdateCameraStreams(db, "https://www.youtube.com/watch?v=8JCk5M_xrBs");
    SeedData.EnsureAnprSchema(db);
    SeedData.EnsurePocSavedRouteSchema(db);
    SeedData.EnsureAnprSeeded(db);
    SeedData.NormalizeKsaPlatesToPrivate(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

var localizationOptions = app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
app.UseRequestLocalization(localizationOptions);

app.UseRouting();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=CommandAndControl}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapControllers();

app.Run();
