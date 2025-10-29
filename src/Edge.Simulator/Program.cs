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

// Define devices with their specific sensor types
var devices = new[]
{
    new { Serial = "dev-101", Type = "temperature", MinValue = 20.0, MaxValue = 35.0, Unit = "C" },
    new { Serial = "dev-102", Type = "co2", MinValue = 400.0, MaxValue = 2000.0, Unit = "ppm" },
    new { Serial = "dev-103", Type = "humidity", MinValue = 30.0, MaxValue = 80.0, Unit = "%" },
    new { Serial = "dev-104", Type = "temperature", MinValue = 18.0, MaxValue = 30.0, Unit = "C" },
    new { Serial = "dev-105", Type = "voc", MinValue = 50.0, MaxValue = 500.0, Unit = "ppb" },
    new { Serial = "dev-106", Type = "occupancy", MinValue = 0.0, MaxValue = 20.0, Unit = "people" },
    new { Serial = "dev-107", Type = "door", MinValue = 0.0, MaxValue = 1.0, Unit = "" },
    new { Serial = "dev-108", Type = "energy", MinValue = 0.5, MaxValue = 5.0, Unit = "kWh" },
    new { Serial = "dev-109", Type = "power", MinValue = 50.0, MaxValue = 300.0, Unit = "W" },
    new { Serial = "dev-110", Type = "co2", MinValue = 300.0, MaxValue = 1500.0, Unit = "ppm" },
    new { Serial = "dev-111", Type = "temperature", MinValue = 22.0, MaxValue = 28.0, Unit = "C" },
    new { Serial = "dev-112", Type = "co2", MinValue = 400.0, MaxValue = 1200.0, Unit = "ppm" }
};

while (true)
{
    foreach (var device in devices)
    {
        // Skip inactive devices (dev-112 is inactive)
        if (device.Serial == "dev-112")
        {
            Console.WriteLine($"⏸️ Skipping inactive device: {device.Serial}");
            continue;
        }

        var value = device.Type switch
        {
            "door" => rand.NextDouble() < 0.3 ? 1.0 : 0.0, // 30% chance of door being open
            "occupancy" => rand.Next((int)device.MinValue, (int)device.MaxValue + 1),
            _ => device.MinValue + rand.NextDouble() * (device.MaxValue - device.MinValue)
        };

        var payload = new
        {
            deviceId = device.Serial,
            apiKey = $"{device.Serial}-key",
            timestamp = DateTimeOffset.UtcNow,
            metrics = new object[]
            {
                new { type = device.Type, value = value, unit = device.Unit }
            }
        };

        var topic = $"tenants/innovia/devices/{device.Serial}/measurements";
        var json = JsonSerializer.Serialize(payload);

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(json))
            .Build();

        await client.PublishAsync(message);
        Console.WriteLine($"[{DateTimeOffset.UtcNow:o}] Published {device.Type} = {value:F1} for {device.Serial}");
    }

    Console.WriteLine($"📡 Simulating {devices.Length - 1} devices"); // -1 for inactive dev-112
    await Task.Delay(TimeSpan.FromSeconds(10));
}
