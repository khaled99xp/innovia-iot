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
var devices = new[] { "dev-101", "dev-102", "dev-103", "dev-104", "dev-105", "dev-106", "dev-107", "dev-108", "dev-109", "dev-110" };

// Send data for each device independently with precise timing
var tasks = devices.Select(async deviceId =>
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
        
        // Send temperature measurement
        var tempPayload = new
        {
            deviceId = deviceId,
            apiKey = $"{deviceId}-key",
            timestamp = DateTimeOffset.UtcNow,
            metrics = new object[]
            {
                new { type = "temperature", value = 20.0 + rand.NextDouble() * 10, unit = "C" }
            }
        };

        var topic = $"tenants/innovia/devices/{deviceId}/measurements";
        var tempJson = JsonSerializer.Serialize(tempPayload);

        var tempMessage = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(tempJson))
            .Build();

        await client.PublishAsync(tempMessage);
        Console.WriteLine($"[{DateTimeOffset.UtcNow:o}] Published temperature to '{topic}': {tempJson}");
        
        // Small delay between measurements
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        
        // Send CO2 measurement
        var co2Payload = new
        {
            deviceId = deviceId,
            apiKey = $"{deviceId}-key",
            timestamp = DateTimeOffset.UtcNow,
            metrics = new object[]
            {
                new { type = "co2", value = 800 + rand.Next(0, 800), unit = "ppm" }
            }
        };

        var co2Json = JsonSerializer.Serialize(co2Payload);

        var co2Message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(co2Json))
            .Build();

        await client.PublishAsync(co2Message);
        Console.WriteLine($"[{DateTimeOffset.UtcNow:o}] Published CO2 to '{topic}': {co2Json}");
    }
});

// Start all device tasks concurrently
await Task.WhenAll(tasks);