using SmartTrafficTool.Models;
using Microsoft.EntityFrameworkCore;

namespace SmartTrafficTool.Data;

public static class SeedData
{
    private static readonly string[] DeviceTypes = ["ANPR Camera", "Lidar", "Radar Sensor", "Environmental Sensor"];
    private static readonly string[] Statuses = ["Online", "Online", "Online", "Offline", "Maintenance"];
    private static readonly string[] Areas =
    [
        "Doha", "Lusail", "Al Wakrah", "Al Khor", "Al Rayyan", "Mesaieed", "Umm Salal"
    ];

    public static void EnsureSeeded(AppDbContext db)
    {
        if (db.Devices.Any())
        {
            return;
        }

        var random = new Random(2026);
        var devices = new List<Device>();

        for (var i = 1; i <= 100; i++)
        {
            var lat = RandomInRange(random, 24.5, 26.3);
            var lng = RandomInRange(random, 50.75, 51.75);
            var type = DeviceTypes[random.Next(DeviceTypes.Length)];
            var status = Statuses[random.Next(Statuses.Length)];
            var area = Areas[random.Next(Areas.Length)];

            devices.Add(new Device
            {
                Name = $"QTR-{i:000} {type}",
                Type = type,
                Status = status,
                StreamUrl = "https://www.youtube.com/watch?v=8JCk5M_xrBs",
                LocationDescription = area,
                Latitude = lat,
                Longitude = lng,
                LastSeenUtc = DateTime.UtcNow.AddMinutes(-random.Next(5, 120))
            });
        }

        db.Devices.AddRange(devices);
        db.SaveChanges();
    }

    public static void UpdateCameraStreams(AppDbContext db, string streamUrl)
    {
        var cameras = db.Devices.Where(d => d.Type.Contains("Camera"));
        var updated = false;

        foreach (var device in cameras)
        {
            if (device.StreamUrl != streamUrl)
            {
                device.StreamUrl = streamUrl;
                updated = true;
            }
        }

        if (updated)
        {
            db.SaveChanges();
        }
    }

    public static void NormalizeDeviceTypes(AppDbContext db)
    {
        var updated = false;

        foreach (var device in db.Devices)
        {
            if (device.Type.Equals("IoT Sensor", StringComparison.OrdinalIgnoreCase))
            {
                device.Type = "Lidar";
                if (!string.IsNullOrWhiteSpace(device.Name))
                {
                    device.Name = device.Name.Replace("IoT Sensor", "Lidar", StringComparison.OrdinalIgnoreCase);
                }
                updated = true;
            }
            else if (device.Type.Equals("Traffic Camera", StringComparison.OrdinalIgnoreCase))
            {
                device.Type = "ANPR Camera";
                if (!string.IsNullOrWhiteSpace(device.Name))
                {
                    device.Name = device.Name.Replace("Traffic Camera", "ANPR Camera", StringComparison.OrdinalIgnoreCase);
                }
                updated = true;
            }
        }

        if (updated)
        {
            db.SaveChanges();
        }
    }

    public static void NormalizeDeviceStatuses(AppDbContext db)
    {
        var updated = false;

        foreach (var device in db.Devices)
        {
            if (device.Status.Equals("Degraded", StringComparison.OrdinalIgnoreCase))
            {
                device.Status = "Offline";
                updated = true;
            }
        }

        if (updated)
        {
            db.SaveChanges();
        }
    }

    public static void EnsureAnprSchema(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS ANPRTRAN (
                TRANSACTION_ID INTEGER PRIMARY KEY,
                DEVICE_ID INTEGER NOT NULL,
                LOCATION_ID INTEGER NOT NULL,
                ZONE_ID INTEGER NOT NULL,
                CREATED_DATE_TIME TEXT NOT NULL,
                CREATED_DATE TEXT NOT NULL,
                PLATE_NUMBER INTEGER NOT NULL,
                PLATE_ALPHA_EN TEXT NULL,
                PLATE_ALPHA TEXT NULL,
                PLATE_TEXT TEXT NULL,
                PLATE_TYPE_ID INTEGER NOT NULL,
                PLATE_ORIGIN_ID INTEGER NOT NULL,
                PLATE_CONFIDENCE INTEGER NOT NULL,
                PLATE_COLOR_ID INTEGER NOT NULL,
                PLATE_XY TEXT NULL,
                TRANSACTION_STATUS INTEGER NOT NULL,
                PLATE_STATUS INTEGER NOT NULL,
                IS_MODIFIED INTEGER NOT NULL,
                IS_ALERTED INTEGER NOT NULL,
                TRAN_TYPE_ID INTEGER NOT NULL,
                SPEED INTEGER NOT NULL,
                DIRECTION_IS_FRONT INTEGER NOT NULL,
                SOURCE_DATE_TIME TEXT NOT NULL,
                MEMBER_ID INTEGER NULL,
                VEHICLE_ID INTEGER NULL
            );
        """);

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS ANPRTRAN_VEH_CLASSIFICATION (
                VEH_CLASSIFICATION_ID INTEGER PRIMARY KEY,
                TRANSACTION_ID INTEGER NOT NULL,
                MNF_ID INTEGER NULL,
                MOD_ID INTEGER NULL,
                YEAR INTEGER NULL,
                CLR_ID INTEGER NULL,
                VEH_TYPE_ID INTEGER NULL
            );
        """);
    }

    /// <summary>
    /// POC route simulator: table for named camera paths (existing SQLite files skip EF-only migrations).
    /// </summary>
    public static void EnsurePocSavedRouteSchema(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS PocSavedRoutes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                DeviceIdsCsv TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL
            );
            """);
    }

    public static void EnsureAnprSeeded(AppDbContext db)
    {
        if (db.ANPRTRANS.Any())
        {
            return;
        }

        var cameraDevices = db.Devices.Where(d => d.Type.Contains("Camera")).ToList();
        if (cameraDevices.Count == 0)
        {
            return;
        }
        var random = new Random(2026);
        var plateAlphas = new[] { "A", "B", "C", "D", "E", "F", "G", "H" };
        var platePool = new List<(string PlateText, int PlateNumber, int PlateTypeId, int OriginId)>();

        for (var i = 0; i < 30; i++)
        {
            var number = random.Next(100, 999999);
            var typeId = GetWeightedPlateTypeId(random); // 1-Private, 2-Commercial, 3-Government, 4-Transport, 5-Taxi
            var roll = random.Next(100);
            var prefix = roll < 20 ? "Q"
                : roll < 35 ? "T"
                : roll < 45 ? "R"
                : string.Empty;
            var plateText = string.IsNullOrEmpty(prefix) ? number.ToString() : $"{prefix}{number}";
            platePool.Add((plateText, number, typeId, 1));
        }

        for (var i = 0; i < 10; i++)
        {
            var number = random.Next(1000, 9999);
            var alpha = plateAlphas[random.Next(plateAlphas.Length)];
            var plateText = $"KSA-{alpha}{number}";
            platePool.Add((plateText, number, 1, 3));
        }

        var trans = new List<ANPRTRAN>();
        var classifications = new List<ANPRTRAN_VEH_CLASSIFICATION>();
        var now = DateTime.UtcNow;

        for (var i = 1; i <= 1500; i++)
        {
            var plateIndex = random.Next(platePool.Count);
            var plateItem = platePool[plateIndex];
            var plateText = plateItem.PlateText;
            var plateNumber = plateItem.PlateNumber;
            var created = now.AddMinutes(-random.Next(0, 1440));
            var transactionId = i;

            var device = cameraDevices[random.Next(cameraDevices.Count)];
            var plateAlphaEn = plateText.Contains('-', StringComparison.Ordinal)
                ? plateText.Substring(plateText.IndexOf('-', StringComparison.Ordinal) + 1, 1)
                : string.Empty;

            trans.Add(new ANPRTRAN
            {
                TRANSACTION_ID = transactionId,
                DEVICE_ID = device.Id,
                LOCATION_ID = random.Next(1, 15),
                ZONE_ID = random.Next(1, 12),
                CREATED_DATE_TIME = created,
                CREATED_DATE = created.Date,
                PLATE_NUMBER = plateNumber,
                PLATE_ALPHA_EN = plateAlphaEn,
                PLATE_ALPHA = plateAlphaEn,
                PLATE_TEXT = plateText,
                PLATE_TYPE_ID = plateItem.PlateTypeId,
                PLATE_ORIGIN_ID = plateItem.OriginId,
                PLATE_CONFIDENCE = random.Next(85, 100),
                PLATE_COLOR_ID = random.Next(1, 6),
                PLATE_XY = $"{random.Next(10, 90)},{random.Next(10, 90)}",
                TRANSACTION_STATUS = random.Next(1, 4),
                PLATE_STATUS = random.Next(1, 4),
                IS_MODIFIED = random.NextDouble() > 0.8,
                IS_ALERTED = random.NextDouble() > 0.9,
                TRAN_TYPE_ID = random.Next(1, 4),
                SPEED = random.Next(40, 160),
                DIRECTION_IS_FRONT = random.NextDouble() > 0.5,
                SOURCE_DATE_TIME = created.AddSeconds(-random.Next(30, 300)),
                MEMBER_ID = random.NextDouble() > 0.85 ? random.Next(1000, 9000) : null,
                VEHICLE_ID = random.NextDouble() > 0.8 ? random.Next(5000, 9000) : null
            });

            classifications.Add(new ANPRTRAN_VEH_CLASSIFICATION
            {
                VEH_CLASSIFICATION_ID = transactionId,
                TRANSACTION_ID = transactionId,
                MNF_ID = random.Next(1, 20),
                MOD_ID = random.Next(1, 100),
                YEAR = random.Next(2012, 2026),
                CLR_ID = random.Next(1, 10),
                VEH_TYPE_ID = GetWeightedVehicleTypeId(random)
            });
        }

        db.ANPRTRANS.AddRange(trans);
        db.ANPRTRAN_VEH_CLASSIFICATIONS.AddRange(classifications);
        db.SaveChanges();
    }

    /// <summary>
    /// Inserts one ANPR transaction per device on <paramref name="pathDeviceIds"/> in order,
    /// with monotonically increasing <see cref="ANPRTRAN.CREATED_DATE_TIME"/> (simulated movement), all within the last 24 hours (UTC).
    /// </summary>
    /// <returns>Inserted count, or (0, error) on validation failure.</returns>
    public static (int Inserted, string? Error) AppendTransactionsAlongPath(
        AppDbContext db,
        string plateText,
        string plateTypeName,
        string countryName,
        IReadOnlyList<int> pathDeviceIds,
        bool alertOnLastSite,
        bool speedViolationOnLastSite)
    {
        var trimmedPlate = (plateText ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmedPlate))
        {
            return (0, "Plate text is required.");
        }

        if (pathDeviceIds == null || pathDeviceIds.Count == 0)
        {
            return (0, "Add at least one camera to the path.");
        }

        var typeId = MapPlateTypeNameToId(plateTypeName);
        var originId = MapCountryNameToOriginId(countryName);
        if (typeId <= 0)
        {
            return (0, "Invalid plate type.");
        }

        if (originId <= 0)
        {
            return (0, "Invalid country.");
        }

        ParsePlateFields(trimmedPlate, out var plateNumber, out var plateAlpha);

        var idSet = pathDeviceIds.ToHashSet();
        var devices = db.Devices
            .Where(d => idSet.Contains(d.Id))
            .ToDictionary(d => d.Id);

        foreach (var id in pathDeviceIds)
        {
            if (!devices.TryGetValue(id, out var dev))
            {
                return (0, $"Device #{id} was not found.");
            }

            if (!dev.Type.Contains("Camera", StringComparison.OrdinalIgnoreCase))
            {
                return (0, $"Device #{id} ({dev.Name}) is not an ANPR camera.");
            }
        }

        var rng = new Random((int)(DateTime.UtcNow.Ticks % int.MaxValue));
        var nextId = db.ANPRTRANS.Any() ? db.ANPRTRANS.Max(t => t.TRANSACTION_ID) + 1 : 1L;
        var now = DateTime.UtcNow;
        var minStart = now.AddHours(-24).AddMinutes(1);

        var nHop = pathDeviceIds.Count;
        var eventTimes = new DateTime[nHop];
        eventTimes[nHop - 1] = now.AddMinutes(-rng.Next(2, 18));
        for (var i = nHop - 2; i >= 0; i--)
        {
            eventTimes[i] = eventTimes[i + 1].AddMinutes(-rng.Next(4, 16));
        }

        if (eventTimes[0] < minStart)
        {
            var shift = minStart - eventTimes[0];
            for (var i = 0; i < nHop; i++)
            {
                eventTimes[i] += shift;
            }
        }

        var trans = new List<ANPRTRAN>();
        var classifications = new List<ANPRTRAN_VEH_CLASSIFICATION>();

        for (var i = 0; i < pathDeviceIds.Count; i++)
        {
            var t = eventTimes[i];
            var deviceId = pathDeviceIds[i];
            var isLast = i == pathDeviceIds.Count - 1;
            var alerted = isLast && alertOnLastSite;
            var speed = isLast && speedViolationOnLastSite
                ? rng.Next(121, 148)
                : rng.Next(52, 108);

            var id = nextId++;
            trans.Add(new ANPRTRAN
            {
                TRANSACTION_ID = id,
                DEVICE_ID = deviceId,
                LOCATION_ID = rng.Next(1, 15),
                ZONE_ID = rng.Next(1, 12),
                CREATED_DATE_TIME = t,
                CREATED_DATE = t.Date,
                PLATE_NUMBER = plateNumber,
                PLATE_ALPHA_EN = plateAlpha,
                PLATE_ALPHA = plateAlpha,
                PLATE_TEXT = trimmedPlate,
                PLATE_TYPE_ID = typeId,
                PLATE_ORIGIN_ID = originId,
                PLATE_CONFIDENCE = rng.Next(88, 100),
                PLATE_COLOR_ID = rng.Next(1, 6),
                PLATE_XY = $"{rng.Next(10, 90)},{rng.Next(10, 90)}",
                TRANSACTION_STATUS = rng.Next(1, 4),
                PLATE_STATUS = rng.Next(1, 4),
                IS_MODIFIED = false,
                IS_ALERTED = alerted,
                TRAN_TYPE_ID = rng.Next(1, 4),
                SPEED = speed,
                DIRECTION_IS_FRONT = rng.NextDouble() > 0.5,
                SOURCE_DATE_TIME = t.AddSeconds(-rng.Next(30, 300)),
                MEMBER_ID = null,
                VEHICLE_ID = null
            });

            classifications.Add(new ANPRTRAN_VEH_CLASSIFICATION
            {
                VEH_CLASSIFICATION_ID = id,
                TRANSACTION_ID = id,
                MNF_ID = rng.Next(1, 20),
                MOD_ID = rng.Next(1, 100),
                YEAR = rng.Next(2012, 2026),
                CLR_ID = rng.Next(1, 10),
                VEH_TYPE_ID = GetWeightedVehicleTypeId(rng)
            });
        }

        db.ANPRTRANS.AddRange(trans);
        db.ANPRTRAN_VEH_CLASSIFICATIONS.AddRange(classifications);
        db.SaveChanges();
        return (trans.Count, null);
    }

    private static int MapPlateTypeNameToId(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return 1;
        }

        return name.Trim().ToLowerInvariant() switch
        {
            "private" => 1,
            "commercial" => 2,
            "government" => 3,
            "transport" => 4,
            "taxi" => 5,
            _ => 0
        };
    }

    private static int MapCountryNameToOriginId(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return 1;
        }

        return name.Trim().ToLowerInvariant() switch
        {
            "qatar" => 1,
            "uae" => 2,
            "ksa" or "saudi" => 3,
            "oman" => 4,
            _ => 0
        };
    }

    private static void ParsePlateFields(string plateText, out int plateNumber, out string? plateAlpha)
    {
        var digits = new string(plateText.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, out var n) && n > 0)
        {
            plateNumber = n;
        }
        else
        {
            plateNumber = Math.Abs(plateText.GetHashCode() % 899_999) + 100_000;
        }

        plateAlpha = null;
        var dash = plateText.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0 && dash < plateText.Length - 1)
        {
            var after = plateText.AsSpan(dash + 1);
            for (var i = 0; i < after.Length; i++)
            {
                if (char.IsLetter(after[i]))
                {
                    plateAlpha = after[i].ToString();
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(plateAlpha) && plateText.Length > 0)
        {
            var c = plateText[0];
            plateAlpha = char.IsLetter(c) ? c.ToString() : "X";
        }
    }

    /// <summary>
    /// Sets all ANPR rows with plate origin KSA (origin id 3) to plate type Private (type id 1).
    /// </summary>
    public static void NormalizeKsaPlatesToPrivate(AppDbContext db)
    {
        if (!db.ANPRTRANS.Any())
        {
            return;
        }

        const int ksaOriginId = 3;
        const int privatePlateTypeId = 1;

        db.ANPRTRANS
            .Where(t => t.PLATE_ORIGIN_ID == ksaOriginId && t.PLATE_TYPE_ID != privatePlateTypeId)
            .ExecuteUpdate(s => s.SetProperty(t => t.PLATE_TYPE_ID, privatePlateTypeId));
    }

    /// <summary>
    /// Creates command map layer tables when using an existing SQLite file (EnsureCreated does not migrate).
    /// </summary>
    public static void EnsureCommandMapSchema(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS CommandMapAlertPoints (
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              Latitude REAL NOT NULL,
              Longitude REAL NOT NULL,
              Intensity REAL NOT NULL
            );
            """);

        db.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS CommandMapViolationPoints (
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              Latitude REAL NOT NULL,
              Longitude REAL NOT NULL,
              Intensity REAL NOT NULL
            );
            """);

        db.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS CommandMapHotspots (
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              Name TEXT NOT NULL,
              Latitude REAL NOT NULL,
              Longitude REAL NOT NULL,
              RadiusMeters REAL NOT NULL,
              RiskLevel TEXT NOT NULL
            );
            """);
    }

    public static void EnsureCommandMapLayersSeeded(AppDbContext db)
    {
        EnsureCommandMapSchema(db);

        if (db.CommandMapAlertPoints.Any())
        {
            return;
        }

        var rng = new Random(4242);

        var alerts = new List<CommandMapAlertPoint>();
        for (var i = 0; i < 160; i++)
        {
            alerts.Add(new CommandMapAlertPoint
            {
                Latitude = RandomInRange(rng, 25.2, 25.45),
                Longitude = RandomInRange(rng, 51.32, 51.62),
                Intensity = RandomInRange(rng, 0.35, 1.0)
            });
        }

        var violations = new List<CommandMapViolationPoint>();
        for (var i = 0; i < 140; i++)
        {
            violations.Add(new CommandMapViolationPoint
            {
                Latitude = RandomInRange(rng, 25.2, 25.45),
                Longitude = RandomInRange(rng, 51.32, 51.62),
                Intensity = RandomInRange(rng, 0.3, 0.95)
            });
        }

        var hotspots = new List<CommandMapHotspot>
        {
            new()
            {
                Name = "West Bay — Congestion & Incidents",
                Latitude = 25.325,
                Longitude = 51.535,
                RadiusMeters = 950,
                RiskLevel = "High"
            },
            new()
            {
                Name = "Corniche — Speed / Night activity",
                Latitude = 25.29,
                Longitude = 51.53,
                RadiusMeters = 720,
                RiskLevel = "High"
            },
            new()
            {
                Name = "Industrial Zone — Violations cluster",
                Latitude = 25.18,
                Longitude = 51.42,
                RadiusMeters = 1100,
                RiskLevel = "Medium"
            },
            new()
            {
                Name = "Lusail Marina — Events",
                Latitude = 25.41,
                Longitude = 51.51,
                RadiusMeters = 800,
                RiskLevel = "Medium"
            },
            new()
            {
                Name = "Hamad Int’l — Perimeter",
                Latitude = 25.27,
                Longitude = 51.61,
                RadiusMeters = 1400,
                RiskLevel = "High"
            },
            new()
            {
                Name = "Al Wakrah — Corridor",
                Latitude = 25.17,
                Longitude = 51.60,
                RadiusMeters = 880,
                RiskLevel = "Medium"
            }
        };

        db.CommandMapAlertPoints.AddRange(alerts);
        db.CommandMapViolationPoints.AddRange(violations);
        db.CommandMapHotspots.AddRange(hotspots);
        db.SaveChanges();
    }

    private static double RandomInRange(Random random, double min, double max)
    {
        return min + (random.NextDouble() * (max - min));
    }

    private static int GetWeightedPlateTypeId(Random random)
    {
        var roll = random.Next(100);
        return roll switch
        {
            < 55 => 1, // Private
            < 75 => 4, // Transport
            < 87 => 5, // Taxi
            < 95 => 2, // Commercial
            _ => 3     // Government
        };
    }

    private static int GetWeightedVehicleTypeId(Random random)
    {
        var roll = random.Next(100);
        return roll switch
        {
            < 30 => 1, // Sedan
            < 60 => 2, // SUV
            < 90 => 3, // Pickup
            < 95 => 4, // Bus
            _ => 5     // Truck
        };
    }
}
