/*
 * 02 · Message Properties
 * -----------------------
 * Demonstrates system properties (MessageId, CorrelationId, Subject,
 * ContentType, TimeToLive) and ApplicationProperties (custom k/v).
 */

using Azure.Messaging.ServiceBus;
using System.Text.Json;

await using var client = new ServiceBusClient(Config.ConnectionString);
await using var sender = client.CreateSender(Config.QueueName);

var order = new { OrderId = 42, Customer = "acme", Total = 199.99m };
var msg = new ServiceBusMessage(JsonSerializer.Serialize(order))
{
    MessageId     = order.OrderId.ToString(),
    CorrelationId = "trace-abc-123",
    Subject       = "order.created",
    ContentType   = "application/json",
    TimeToLive    = TimeSpan.FromMinutes(10),
    ApplicationProperties =
    {
        ["priority"] = "high",
        ["region"]   = "us-east",
        ["tenantId"] = "acme",
    }
};
await sender.SendMessageAsync(msg);
Console.WriteLine($"Sent {msg.MessageId} ({msg.Subject})");

await using var receiver = client.CreateReceiver(Config.QueueName);
var received = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));

Console.WriteLine($"MessageId      : {received!.MessageId}");
Console.WriteLine($"CorrelationId  : {received.CorrelationId}");
Console.WriteLine($"Subject        : {received.Subject}");
Console.WriteLine($"ContentType    : {received.ContentType}");
Console.WriteLine($"SequenceNumber : {received.SequenceNumber}");
Console.WriteLine($"EnqueuedTime   : {received.EnqueuedTime:o}");
Console.WriteLine($"DeliveryCount  : {received.DeliveryCount}");
Console.WriteLine($"TimeToLive     : {received.TimeToLive}");
Console.WriteLine("ApplicationProperties:");
foreach (var kv in received.ApplicationProperties)
    Console.WriteLine($"  {kv.Key} = {kv.Value}");
Console.WriteLine($"Body: {received.Body}");

await receiver.CompleteMessageAsync(received);
