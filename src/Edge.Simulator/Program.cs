using MQTTnet;
using MQTTnet.Client;
using System.Text.Json;
using System.Text;

var factory = new MqttFactory();
var client = factory.CreateMqttClient();

var options = new MqttClientOptionsBuilder()
    .WithTcpServer("localhost", 1883)
    .Build();

Console.WriteLine("Edge.Simulator starting… connecting to MQTT at localhost:1883");
try
{
    await client.ConnectAsync(options);
    Console.WriteLine("✅ Connected to MQTT broker.");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Failed to connect to MQTT broker: {ex.Message}");
    throw;
}

var rand = new Random();
var httpClient = new HttpClient();

// Function to get devices from DeviceRegistry API
async Task<List<DeviceInfo>> GetDevicesFromRegistry()
{
    try
    {
        // First, get the tenant by slug
        var tenantResponse = await httpClient.GetAsync("http://localhost:5101/api/tenants/by-slug/innovia");
        if (!tenantResponse.IsSuccessStatusCode)
        {
            Console.WriteLine($"❌ Failed to get tenant: {tenantResponse.StatusCode}");
            return new List<DeviceInfo>();
        }
        
        var tenantJson = await tenantResponse.Content.ReadAsStringAsync();
        var tenant = JsonSerializer.Deserialize<TenantInfo>(tenantJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        if (tenant == null)
        {
            Console.WriteLine("❌ Tenant not found");
            return new List<DeviceInfo>();
        }
        
        // Then get devices for this tenant
        var response = await httpClient.GetAsync($"http://localhost:5101/api/tenants/{tenant.Id}/devices");
        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync();
            var devices = JsonSerializer.Deserialize<List<DeviceInfo>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return devices ?? new List<DeviceInfo>();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Failed to fetch devices from DeviceRegistry: {ex.Message}");
    }
    return new List<DeviceInfo>();
}

// Function to determine sensor type and parameters from device model
(string type, double minValue, double maxValue, string unit) GetSensorParameters(string model)
{
    return model.ToLower() switch
    {
        var m when m.Contains("temperature") => ("temperature", 18.0, 35.0, "C"),
        var m when m.Contains("co2") || m.Contains("co₂") => ("co2", 300.0, 2000.0, "ppm"),
        var m when m.Contains("humidity") => ("humidity", 30.0, 80.0, "%"),
        var m when m.Contains("voc") => ("voc", 50.0, 500.0, "ppb"),
        var m when m.Contains("occupancy") => ("occupancy", 0.0, 20.0, "people"),
        var m when m.Contains("door") => ("door", 0.0, 1.0, ""),
        var m when m.Contains("energy") => ("energy", 0.5, 5.0, "kWh"),
        var m when m.Contains("power") => ("power", 50.0, 300.0, "W"),
        _ => ("temperature", 20.0, 30.0, "C") // Default fallback
    };
}

while (true)
{
    // Get devices from DeviceRegistry API
    var devices = await GetDevicesFromRegistry();
    
    if (devices.Count == 0)
    {
        Console.WriteLine("❌ No devices found in DeviceRegistry. Waiting 30 seconds...");
        await Task.Delay(TimeSpan.FromSeconds(30));
        continue;
    }

    Console.WriteLine($"📡 Found {devices.Count} devices in DeviceRegistry");

    foreach (var device in devices)
    {
        // Skip inactive devices
        if (device.Status != "active")
        {
            Console.WriteLine($"⏸️ Skipping inactive device: {device.Serial}");
            continue;
        }

        // Determine sensor type and parameters from device model
        var (sensorType, minValue, maxValue, unit) = GetSensorParameters(device.Model);

        var value = sensorType switch
        {
            "door" => rand.NextDouble() < 0.3 ? 1.0 : 0.0, // 30% chance of door being open
            "occupancy" => rand.Next((int)minValue, (int)maxValue + 1),
            _ => minValue + rand.NextDouble() * (maxValue - minValue)
        };

        var payload = new
        {
            deviceId = device.Serial,
            apiKey = $"{device.Serial}-key",
            timestamp = DateTimeOffset.UtcNow,
            metrics = new object[]
            {
                new { type = sensorType, value = value, unit = unit }
            }
        };

        var topic = $"tenants/innovia/devices/{device.Serial}/measurements";
        var json = JsonSerializer.Serialize(payload);

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(json))
            .Build();

        await client.PublishAsync(message);
        Console.WriteLine($"[{DateTimeOffset.UtcNow:o}] Published {sensorType} = {value:F1} for {device.Serial}");
    }

    Console.WriteLine($"📡 Simulating {devices.Count(d => d.Status == "active")} active devices");
    await Task.Delay(TimeSpan.FromSeconds(10));
}

// DeviceInfo class to match DeviceRegistry API response
public class DeviceInfo
{
    public string Id { get; set; } = "";
    public string Serial { get; set; } = "";
    public string Model { get; set; } = "";
    public string Status { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string RoomId { get; set; } = "";
}

// TenantInfo class to match DeviceRegistry API response
public class TenantInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
}
