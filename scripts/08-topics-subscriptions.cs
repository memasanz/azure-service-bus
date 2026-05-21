#:package Azure.Messaging.ServiceBus@7.18.2
#:project ../src/SbDemo.Shared/SbDemo.Shared.csproj
#:package Azure.Messaging.ServiceBus.Administration@7.18.2
/*
 * 08 · Topics, Subscriptions & Filters
 * ------------------------------------
 * Pub/sub with SQL and Correlation filters on subscriptions.
 */

using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

await using var client = new ServiceBusClient(Config.ConnectionString);
await using var sender = client.CreateSender(Config.TopicName);

await sender.SendMessagesAsync(new[]
{
    new ServiceBusMessage("low-priority log")  { ApplicationProperties = { ["priority"] = "low"  } },
    new ServiceBusMessage("urgent alert")      { ApplicationProperties = { ["priority"] = "high" } },
    new ServiceBusMessage("order #42") { Subject = "order", ApplicationProperties = { ["priority"] = "normal" } },
    new ServiceBusMessage("order #43") { Subject = "order", ApplicationProperties = { ["priority"] = "high" } },
});
Console.WriteLine("Sent 4 messages to the topic.");

async Task DrainAsync(string subscription)
{
    await using var receiver = client.CreateReceiver(Config.TopicName, subscription, new ServiceBusReceiverOptions
    {
        ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete
    });
    Console.WriteLine($"\n--- subscription: {subscription} ---");
    while (true)
    {
        var m = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(2));
        if (m is null) break;
        var pri = m.ApplicationProperties.TryGetValue("priority", out var v) ? v : "?";
        Console.WriteLine($"  body={m.Body}  subject={m.Subject ?? "-"}  priority={pri}");
    }
}

await DrainAsync("all");
await DrainAsync("high-priority");
await DrainAsync("orders");

// Add a runtime rule
var admin = new ServiceBusAdministrationClient(Config.ConnectionString);
try { await admin.DeleteRuleAsync(Config.TopicName, "all", "us-east-only"); } catch { }
await admin.CreateRuleAsync(Config.TopicName, "all",
    new CreateRuleOptions("us-east-only", new SqlRuleFilter("region = 'us-east'")));
Console.WriteLine("\nAdded rule 'us-east-only' to subscription 'all'.");
