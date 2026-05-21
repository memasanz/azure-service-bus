#:package Azure.Messaging.ServiceBus@7.18.2
#:project ../src/SbDemo.Shared/SbDemo.Shared.csproj

/*
 * ============================================================================
 * 03 · Batching — Efficient High-Volume Sends
 * ============================================================================
 *
 * OBJECTIVE
 *   Send thousands of messages efficiently while respecting the broker's
 *   per-batch size limit (256 KB on Standard, 1 MB on Premium).
 *
 * WHY BATCH?
 *   Each network round-trip to Service Bus has fixed overhead. Sending
 *   1000 messages individually = 1000 round-trips. Sending them in batches
 *   of (say) 100 = 10 round-trips. Throughput goes up 10–100×.
 *
 * THREE WAYS TO SEND MULTIPLE MESSAGES
 *
 *   A) SendMessageAsync(msg)                      — one at a time. Slow.
 *
 *   B) SendMessagesAsync(IEnumerable<msg>)        — SDK auto-batches for you.
 *                                                   Easiest. Throws if the
 *                                                   total exceeds one batch
 *                                                   size limit.
 *
 *   C) ServiceBusMessageBatch + TryAddMessage     — explicit, size-aware.
 *                                                   This script.
 *
 * WHEN TO USE (C) — EXPLICIT BATCHING
 *   • You don't know up front whether all messages will fit.
 *   • You're streaming messages and want to flush opportunistically.
 *   • You need to know precisely how many messages went in one batch
 *     (e.g. for checkpointing).
 *
 * THE PATTERN
 *   1. Create a batch from the sender (it knows the link's max size).
 *   2. TryAddMessage in a loop. Returns false when the batch is full.
 *   3. Send the batch. Dispose. Start a new one. Repeat.
 *
 * RUN
 *   dotnet run scripts/03-batching.cs
 * ============================================================================
 */

using Azure.Messaging.ServiceBus;

await using var client = new ServiceBusClient(Config.ConnectionString);
await using var sender = client.CreateSender(Config.QueueName);
Console.WriteLine($"Queue: {Config.QueueName}");

// ---------------------------------------------------------------------------
// 1) Build 5,000 messages to demonstrate batching at scale
// ---------------------------------------------------------------------------
var toSend = Enumerable.Range(1, 5_000)
    .Select(i => new ServiceBusMessage($"message-{i:D5}"))
    .ToList();

int totalSent   = 0;
int batchNumber = 0;
int i           = 0;

// ---------------------------------------------------------------------------
// 2) The explicit batch pattern
//
//    Note: we MUST handle the "single message too large for an empty batch"
//    edge case explicitly — that's a permanent failure that wouldn't be
//    fixed by retrying or starting a new batch.
// ---------------------------------------------------------------------------
while (i < toSend.Count)
{
    // CreateMessageBatchAsync queries the link to learn the negotiated max
    // size, so the batch can self-enforce limits.
    using ServiceBusMessageBatch batch = await sender.CreateMessageBatchAsync();

    // First message into an EMPTY batch — if even one message is too big,
    // no future batch will fit it either. This is a fatal error for the item.
    if (!batch.TryAddMessage(toSend[i]))
        throw new Exception($"Message {i} ({toSend[i].Body.ToMemory().Length} bytes) is too large for an empty batch.");
    i++;

    // Now greedily add until the broker says "no more room".
    while (i < toSend.Count && batch.TryAddMessage(toSend[i])) i++;

    // Flush this batch in a single network round-trip.
    await sender.SendMessagesAsync(batch);

    totalSent += batch.Count;
    batchNumber++;
    Console.WriteLine($"Batch #{batchNumber}: sent {batch.Count}  (running total {totalSent})");
}

Console.WriteLine($"\nSent {totalSent} messages in {batchNumber} batches.");
Console.WriteLine($"Average: {(double)totalSent / batchNumber:F0} messages/batch");


// ---------------------------------------------------------------------------
// 3) Drain the queue so it's clean for subsequent scripts
//
//    ReceiveAndDelete + ReceiveMessagesAsync(maxMessages: ..) is the fastest
//    way to clear a queue. Real apps would never use it like this — but it's
//    perfect for test cleanup.
// ---------------------------------------------------------------------------
await using var receiver = client.CreateReceiver(Config.QueueName, new ServiceBusReceiverOptions
{
    ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete
});

int drained = 0;
while (true)
{
    var batch = await receiver.ReceiveMessagesAsync(maxMessages: 500, maxWaitTime: TimeSpan.FromSeconds(2));
    if (batch.Count == 0) break;
    drained += batch.Count;
}
Console.WriteLine($"Drained {drained} messages from the queue.");

Console.WriteLine("\nDone. Next: 04-processor.cs");
