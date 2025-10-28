using Microsoft.AspNetCore.SignalR;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();

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
app.UseCors("Frontend");
app.MapHub<TelemetryHub>("/hub/telemetry").RequireCors("Frontend");
app.Run();

public class TelemetryHub : Hub
{
    public Task JoinTenant(string tenant) =>
        Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{tenant}");

    public async Task PublishMeasurement(RealtimeMeasurement m)
    {
        await Clients.Group($"tenant:{m.TenantSlug}")
            .SendAsync("measurementReceived", m);
    }

    public async Task PublishAlert(AlertData alert)
    {
        await Clients.Group($"tenant:{alert.TenantSlug}")
            .SendAsync("alertRaised", alert);
    }
}

public record RealtimeMeasurement(
    string TenantSlug,
    System.Guid DeviceId,
    string Type,
    double Value,
    System.DateTimeOffset Time
);

public record AlertData(
    string TenantSlug,
    System.Guid DeviceId,
    string Type,
    double Value,
    System.DateTimeOffset Time,
    System.Guid RuleId,
    string Severity,
    string Message
);
