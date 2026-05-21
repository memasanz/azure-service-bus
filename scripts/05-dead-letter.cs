#:package Azure.Messaging.ServiceBus@7.18.2
#:project ../src/SbDemo.Shared/SbDemo.Shared.csproj
/*
 * 05 · Dead-Letter Queue
 * ----------------------
 * Explicitly dead-letter a message, inspect the DLQ, then resubmit.
 */

using Azure.Messaging.ServiceBus;

await using var client = new ServiceBusClient(Config.ConnectionString);
await using var sender = client.CreateSender(Config.DlqDemoQueueName);

await sender.SendMessageAsync(new ServiceBusMessage("bad-payload")
{
    ApplicationProperties = { ["expectFailure"] = true }
});
Console.WriteLine("Sent a 'bad' message to demo-dlq.");

await using var receiver = client.CreateReceiver(Config.DlqDemoQueueName);
var msg = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));
Console.WriteLine($"Got: {msg!.Body}  (DeliveryCount={msg.DeliveryCount})");

await receiver.DeadLetterMessageAsync(msg,
    deadLetterReason: "ValidationFailed",
    deadLetterErrorDescription: "Payload failed schema validation");
Console.WriteLine("Dead-lettered.");

await using var dlq = client.CreateReceiver(Config.DlqDemoQueueName, new ServiceBusReceiverOptions
{
    SubQueue = SubQueue.DeadLetter
});
var dead = await dlq.ReceiveMessageAsync(TimeSpan.FromSeconds(5));

Console.WriteLine($"Body              : {dead!.Body}");
Console.WriteLine($"DeadLetterReason  : {dead.DeadLetterReason}");
Console.WriteLine($"DeadLetterDescr.  : {dead.DeadLetterErrorDescription}");

// Resubmit
var resubmitted = new ServiceBusMessage(dead.Body)
{
    MessageId = dead.MessageId,
    ContentType = dead.ContentType,
    Subject = dead.Subject,
};
foreach (var kv in dead.ApplicationProperties)
    if (!kv.Key.StartsWith("DeadLetter")) resubmitted.ApplicationProperties[kv.Key] = kv.Value;

await sender.SendMessageAsync(resubmitted);
await dlq.CompleteMessageAsync(dead);
Console.WriteLine("Resubmitted and removed from DLQ.");
