using Microsoft.EntityFrameworkCore;
using SmartTrafficTool.Models;

namespace SmartTrafficTool.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<ANPRTRAN> ANPRTRANS => Set<ANPRTRAN>();

    public DbSet<ANPRTRAN_VEH_CLASSIFICATION> ANPRTRAN_VEH_CLASSIFICATIONS => Set<ANPRTRAN_VEH_CLASSIFICATION>();

    public DbSet<CommandMapAlertPoint> CommandMapAlertPoints => Set<CommandMapAlertPoint>();

    public DbSet<CommandMapViolationPoint> CommandMapViolationPoints => Set<CommandMapViolationPoint>();

    public DbSet<CommandMapHotspot> CommandMapHotspots => Set<CommandMapHotspot>();

    public DbSet<PocSavedRoute> PocSavedRoutes => Set<PocSavedRoute>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ANPRTRAN>().ToTable("ANPRTRAN");
        modelBuilder.Entity<ANPRTRAN_VEH_CLASSIFICATION>().ToTable("ANPRTRAN_VEH_CLASSIFICATION");
        modelBuilder.Entity<CommandMapAlertPoint>().ToTable("CommandMapAlertPoints");
        modelBuilder.Entity<CommandMapViolationPoint>().ToTable("CommandMapViolationPoints");
        modelBuilder.Entity<CommandMapHotspot>().ToTable("CommandMapHotspots");
        modelBuilder.Entity<PocSavedRoute>().ToTable("PocSavedRoutes");
    }
}
