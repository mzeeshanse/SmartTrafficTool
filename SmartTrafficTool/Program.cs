using Microsoft.EntityFrameworkCore;
using SmartTrafficTool.Data;
using SmartTrafficTool.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
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

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=CommandAndControl}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapControllers();

app.Run();
