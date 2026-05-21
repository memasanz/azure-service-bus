#:package Azure.Messaging.ServiceBus@7.18.2
#:project ../src/SbDemo.Shared/SbDemo.Shared.csproj
/*
 * 03 · Batching
 * -------------
 * Uses ServiceBusMessageBatch to send many messages efficiently while
 * respecting the per-batch size limit.
 */

using Azure.Messaging.ServiceBus;

await using var client = new ServiceBusClient(Config.ConnectionString);
await using var sender = client.CreateSender(Config.QueueName);

var toSend = Enumerable.Range(1, 5_000)
    .Select(i => new ServiceBusMessage($"message-{i:D5}"))
    .ToList();

int totalSent = 0, batchNumber = 0, i = 0;

while (i < toSend.Count)
{
    using ServiceBusMessageBatch batch = await sender.CreateMessageBatchAsync();

    if (!batch.TryAddMessage(toSend[i]))
        throw new Exception($"Message {i} too large for an empty batch.");
    i++;

    while (i < toSend.Count && batch.TryAddMessage(toSend[i])) i++;

    await sender.SendMessagesAsync(batch);
    totalSent += batch.Count;
    batchNumber++;
    Console.WriteLine($"Batch #{batchNumber}: sent {batch.Count} (running total {totalSent})");
}

Console.WriteLine($"\nSent {totalSent} messages in {batchNumber} batches.");

// Drain so the queue is clean for later scripts
await using var receiver = client.CreateReceiver(Config.QueueName, new ServiceBusReceiverOptions
{
    ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete
});
int drained = 0;
while (true)
{
    var batch = await receiver.ReceiveMessagesAsync(500, TimeSpan.FromSeconds(2));
    if (batch.Count == 0) break;
    drained += batch.Count;
}
Console.WriteLine($"Drained {drained} messages.");
