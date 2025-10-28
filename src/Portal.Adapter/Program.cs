using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add CORS
builder.Services.AddCors(opt =>
{
    opt.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(
                "http://127.0.0.1:5500",
                "http://localhost:5500",
                "http://localhost:5173",
                "http://localhost:5174"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddDbContext<PortalDbContext>(o =>
    o.UseNpgsql(
        builder.Configuration.GetConnectionString("Db")
        ?? "Host=localhost;Username=postgres;Password=password;Database=innovia_ingest"
    )
);
var app = builder.Build();

// Enable CORS
app.UseCors("Frontend");
// Ensure database and tables exist (quick-start dev convenience)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
    db.Database.EnsureCreated();
}
// Enable Swagger always (not only in Development)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Portal.Adapter v1");
    c.RoutePrefix = "swagger";
});
// Redirect root to Swagger UI for convenience
app.MapGet("/", () => Results.Redirect("/swagger"));

app.MapGet("/portal/{tenant}/rooms/{roomId}/latest", (string tenant, Guid roomId) => Results.Ok(new {
    roomId,
    metrics = new [] { new { type = "co2", value = 950.0, unit = "ppm" }, new { type = "temperature", value = 22.4, unit = "C" } }
}));

app.MapGet("/portal/{tenant}/devices/{deviceId}/series",
    async (Guid tenant, Guid deviceId, string type, DateTimeOffset from, DateTimeOffset to, PortalDbContext db) =>
{
    var points = await db.Measurements
        .Where(m => m.DeviceId == deviceId && m.Type == type && m.Time >= from && m.Time <= to)
        .OrderBy(m => m.Time)
        .Select(m => new { t = m.Time, v = m.Value })
        .ToListAsync();

    return Results.Ok(new { deviceId, type, from, to, points });
});

// Return ALL measurements for a device (optionally filter by type/from/to)
app.MapGet("/portal/{tenant}/devices/{deviceId}/measurements",
    async (Guid tenant, Guid deviceId, DateTimeOffset? from, DateTimeOffset? to, string? type, PortalDbContext db) =>
{
    var q = db.Measurements.Where(m => m.DeviceId == deviceId);
    if (from is { } f) q = q.Where(m => m.Time >= f);
    if (to is { } t) q = q.Where(m => m.Time <= t);
    if (!string.IsNullOrWhiteSpace(type)) q = q.Where(m => m.Type == type);

    var rows = await q
        .OrderBy(m => m.Time)
        .Select(m => new { time = m.Time, type = m.Type, value = m.Value })
        .ToListAsync();

    return Results.Ok(new { deviceId, count = rows.Count, from, to, type, measurements = rows });
});

// API endpoint for frontend - get recent measurements for a device with limit
app.MapGet("/api/devices/{deviceId}/measurements",
    async (Guid deviceId, int? limit, PortalDbContext db) =>
{
    var q = db.Measurements.Where(m => m.DeviceId == deviceId);
    var limitValue = limit ?? 10;

    var rows = await q
        .OrderByDescending(m => m.Time)
        .Take(limitValue)
        .Select(m => new { 
            tenantSlug = "innovia", // Hardcoded for now
            deviceId = m.DeviceId.ToString(),
            type = m.Type, 
            value = m.Value,
            time = m.Time.ToString("o")
        })
        .ToListAsync();

    return Results.Ok(rows);
});

// Return ALL series for a device grouped per type (points per type)
app.MapGet("/portal/{tenant}/devices/{deviceId}/series/all",
    async (Guid tenant, Guid deviceId, DateTimeOffset? from, DateTimeOffset? to, PortalDbContext db) =>
{
    var q = db.Measurements.Where(m => m.DeviceId == deviceId);
    if (from is { } f) q = q.Where(m => m.Time >= f);
    if (to is { } t) q = q.Where(m => m.Time <= t);

    var grouped = await q
        .OrderBy(m => m.Time)
        .GroupBy(m => m.Type)
        .Select(g => new
        {
            type = g.Key,
            points = g.Select(m => new { t = m.Time, v = m.Value })
        })
        .ToListAsync();

    return Results.Ok(new { deviceId, from, to, series = grouped });
});

app.Run();

public class PortalDbContext : DbContext
{
    public PortalDbContext(DbContextOptions<PortalDbContext> options) : base(options) { }
    public DbSet<MeasurementRow> Measurements => Set<MeasurementRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MeasurementRow>().ToTable("Measurements");
        base.OnModelCreating(modelBuilder);
    }
}

public class MeasurementRow
{
    public long Id { get; set; }
    public DateTimeOffset Time { get; set; }
    public Guid TenantId { get; set; }
    public Guid DeviceId { get; set; }
    public string Type { get; set; } = "";
    public double Value { get; set; }
}
