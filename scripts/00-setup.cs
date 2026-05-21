#:package Azure.Messaging.ServiceBus@7.18.2
#:project ../src/SbDemo.Shared/SbDemo.Shared.csproj
/*
 * 00 · Setup & Connection Check
 * -----------------------------
 * Verifies that the .env file (or process env vars) is configured and that
 * the expected Service Bus entities exist.
 *
 * Run:  dotnet run scripts/00-setup.cs
 */

#:package Azure.Messaging.ServiceBus.Administration@7.18.2

using Azure.Messaging.ServiceBus.Administration;

Console.WriteLine($".env file: {Config.DotEnvPath ?? "(none — using process env vars)"}");
Console.WriteLine($"Connection string length: {Config.ConnectionString.Length} chars (looks good).\n");

var admin = new ServiceBusAdministrationClient(Config.ConnectionString);

Console.WriteLine("Queues:");
await foreach (var q in admin.GetQueuesAsync())
    Console.WriteLine($"  - {q.Name}  (sessions={q.RequiresSession}, maxDeliveryCount={q.MaxDeliveryCount})");

Console.WriteLine("\nTopics:");
await foreach (var t in admin.GetTopicsAsync())
{
    Console.WriteLine($"  - {t.Name}");
    await foreach (var s in admin.GetSubscriptionsAsync(t.Name))
        Console.WriteLine($"      • {s.SubscriptionName}");
}
