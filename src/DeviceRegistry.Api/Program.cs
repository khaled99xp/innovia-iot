using Microsoft.EntityFrameworkCore;
using Innovia.Shared.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<InnoviaDbContext>(o => 
    o.UseNpgsql(builder.Configuration.GetConnectionString("Db")));
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

var app = builder.Build();

// Enable CORS
app.UseCors("Frontend");

// Ensure database and tables exist (quick-start dev convenience)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InnoviaDbContext>();
    db.Database.EnsureCreated();

    // Optional seeding: set DEVICE_REGISTRY_SEED=true to seed a default tenant and 10 devices
    var shouldSeed = Environment.GetEnvironmentVariable("DEVICE_REGISTRY_SEED");
    if (!string.IsNullOrWhiteSpace(shouldSeed) &&
        (string.Equals(shouldSeed, "true", StringComparison.OrdinalIgnoreCase) || shouldSeed == "1"))
    {
        var tenantSlug = Environment.GetEnvironmentVariable("SEED_TENANT_SLUG") ?? "innovia";
        var tenantName = Environment.GetEnvironmentVariable("SEED_TENANT_NAME") ?? "Innovia";

        var tenant = db.Tenants.FirstOrDefault(t => t.Slug == tenantSlug);
        if (tenant == null)
        {
            tenant = new Tenant { Name = tenantName, Slug = tenantSlug };
            db.Tenants.Add(tenant);
            db.SaveChanges();
        }

        // Define 10 default devices (idempotent: only add missing by Serial within tenant)
        var defaultDevices = new (string Serial, string Model, string Status)[]
        {
            ("dev-101", "Acme Temperature Sensor", "active"),
            ("dev-102", "Acme CO₂ Monitor", "active"),
            ("dev-103", "Acme Humidity Sensor", "active"),
            ("dev-104", "Acme Temperature Pro", "active"),
            ("dev-105", "Acme VOC Detector", "active"),
            ("dev-106", "Acme Occupancy Counter", "active"),
            ("dev-107", "Acme Door Sensor", "active"),
            ("dev-108", "Acme Energy Meter", "active"),
            ("dev-109", "Acme Power Monitor", "active"),
            ("dev-110", "Acme CO₂ Pro", "active"),
        };

        foreach (var d in defaultDevices)
        {
            var exists = db.Devices.Any(x => x.TenantId == tenant.Id && x.Serial == d.Serial);
            if (!exists)
            {
                db.Devices.Add(new Device
                {
                    TenantId = tenant.Id,
                    Serial = d.Serial,
                    Model = d.Model,
                    Status = d.Status
                });
            }
        }

        db.SaveChanges();
    }
}

// Enable Swagger always (not only in Development)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "DeviceRegistry.Api v1");
    c.RoutePrefix = "swagger";
});
// Redirect root to Swagger UI for convenience
app.MapGet("/", () => Results.Redirect("/swagger"));

app.MapPost("/api/tenants", async (InnoviaDbContext db, Tenant t) => {
    db.Tenants.Add(t); await db.SaveChangesAsync(); return Results.Created($"/api/tenants/{t.Id}", t);
});

app.MapPost("/api/tenants/{tenantId:guid}/devices", async (Guid tenantId, InnoviaDbContext db, Device d) => {
    d.TenantId = tenantId;
    db.Devices.Add(d); await db.SaveChangesAsync();
    return Results.Created($"/api/tenants/{tenantId}/devices/{d.Id}", d);
});


app.MapGet("/api/tenants/{tenantId:guid}/devices/{deviceId:guid}", async (Guid tenantId, Guid deviceId, InnoviaDbContext db) => {
    var d = await db.Devices.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == deviceId);
    return d is null ? Results.NotFound() : Results.Ok(d);
});

// List all devices for a tenant
app.MapGet("/api/tenants/{tenantId:guid}/devices",
    async (Guid tenantId, InnoviaDbContext db) =>
{
    var list = await db.Devices
        .Where(d => d.TenantId == tenantId)
        .ToListAsync();
    return Results.Ok(list);
});

// Lookup tenant by slug (for cross-service resolution)
app.MapGet("/api/tenants/by-slug/{slug}",
    async (string slug, InnoviaDbContext db) =>
{
    var t = await db.Tenants.FirstOrDefaultAsync(x => x.Slug == slug);
    return t is null ? Results.NotFound() : Results.Ok(t);
});

// Lookup device by serial within a tenant (for cross-service resolution)
app.MapGet("/api/tenants/{tenantId:guid}/devices/by-serial/{serial}",
    async (Guid tenantId, string serial, InnoviaDbContext db) =>
{
    var d = await db.Devices.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Serial == serial);
    return d is null ? Results.NotFound() : Results.Ok(d);
});

// Update device
app.MapPut("/api/tenants/{tenantId:guid}/devices/{deviceId:guid}", async (Guid tenantId, Guid deviceId, InnoviaDbContext db, Device updatedDevice) => {
    var device = await db.Devices.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == deviceId);
    if (device is null) return Results.NotFound();
    
    device.Model = updatedDevice.Model;
    device.Serial = updatedDevice.Serial;
    device.Status = updatedDevice.Status;
    
    await db.SaveChangesAsync();
    return Results.Ok(device);
});

// Delete device
app.MapDelete("/api/tenants/{tenantId:guid}/devices/{deviceId:guid}", async (Guid tenantId, Guid deviceId, InnoviaDbContext db) => {
    var device = await db.Devices.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == deviceId);
    if (device is null) return Results.NotFound();
    
    db.Devices.Remove(device);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.Run();

public class InnoviaDbContext : DbContext
{
    public InnoviaDbContext(DbContextOptions<InnoviaDbContext> o) : base(o) {}
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Device> Devices => Set<Device>();
}

public class Tenant { public Guid Id {get; set;} = Guid.NewGuid(); public string Name {get; set;} = ""; public string Slug {get; set;} = ""; }
public class Device { public Guid Id {get; set;} = Guid.NewGuid(); public Guid TenantId {get; set;} public Guid? RoomId {get; set;} public string Model {get; set;} = ""; public string Serial {get; set;} = ""; public string Status {get; set;} = "active"; }
