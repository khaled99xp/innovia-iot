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

// Define 10 different sensors with different types
var sensors = new[]
{
    new { DeviceId = "dev-101", Type = "temperature", Unit = "C", MinValue = 20.0, MaxValue = 30.0 },
    new { DeviceId = "dev-102", Type = "co2", Unit = "ppm", MinValue = 800.0, MaxValue = 1600.0 },
    new { DeviceId = "dev-103", Type = "humidity", Unit = "%", MinValue = 30.0, MaxValue = 80.0 },
    new { DeviceId = "dev-104", Type = "temperature", Unit = "C", MinValue = 18.0, MaxValue = 28.0 },
    new { DeviceId = "dev-105", Type = "voc", Unit = "ppb", MinValue = 50.0, MaxValue = 500.0 },
    new { DeviceId = "dev-106", Type = "occupancy", Unit = "count", MinValue = 0.0, MaxValue = 10.0 },
    new { DeviceId = "dev-107", Type = "door", Unit = "state", MinValue = 0.0, MaxValue = 1.0 },
    new { DeviceId = "dev-108", Type = "energy", Unit = "kWh", MinValue = 0.1, MaxValue = 5.0 },
    new { DeviceId = "dev-109", Type = "power", Unit = "W", MinValue = 10.0, MaxValue = 1000.0 },
    new { DeviceId = "dev-110", Type = "co2", Unit = "ppm", MinValue = 400.0, MaxValue = 1200.0 }
};

// Send data for each sensor independently with precise timing
var tasks = sensors.Select(async sensor =>
{
    var nextSendTime = DateTimeOffset.UtcNow.AddSeconds(10);
    
    while (true)
    {
        var now = DateTimeOffset.UtcNow;
        
        // Calculate delay to ensure exact 10-second intervals
        var delay = nextSendTime - now;
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay);
        }
        
        // Update next send time
        nextSendTime = nextSendTime.AddSeconds(10);
        
        // Generate random value based on sensor type
        double value;
        if (sensor.Type == "door")
        {
            // Door sensor: 0 (closed) or 1 (open)
            value = rand.NextDouble() > 0.7 ? 1.0 : 0.0;
        }
        else if (sensor.Type == "occupancy")
        {
            // Occupancy sensor: integer values
            value = rand.Next((int)sensor.MinValue, (int)sensor.MaxValue + 1);
        }
        else
        {
            // Other sensors: continuous values
            value = sensor.MinValue + rand.NextDouble() * (sensor.MaxValue - sensor.MinValue);
        }
        
        // Send measurement
        var payload = new
        {
            deviceId = sensor.DeviceId,
            apiKey = $"{sensor.DeviceId}-key",
            timestamp = DateTimeOffset.UtcNow,
            metrics = new object[]
            {
                new { type = sensor.Type, value = value, unit = sensor.Unit }
            }
        };

        var topic = $"tenants/innovia/devices/{sensor.DeviceId}/measurements";
        var json = JsonSerializer.Serialize(payload);

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(json))
            .Build();

        await client.PublishAsync(message);
        Console.WriteLine($"[{DateTimeOffset.UtcNow:o}] Published {sensor.Type} to '{topic}': {json}");
    }
});

// Start all device tasks concurrently
await Task.WhenAll(tasks);