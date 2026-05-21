#:package Azure.Messaging.ServiceBus@7.18.2
#:project ../src/SbDemo.Shared/SbDemo.Shared.csproj

/*
 * ============================================================================
 * 01 · Queue — Send & Receive
 * ============================================================================
 *
 * OBJECTIVE
 *   Cover the absolute fundamentals: create a client, send a message, receive
 *   it back. Then explore the two receive modes (PeekLock vs ReceiveAndDelete)
 *   and understand the trade-off between them.
 *
 * MENTAL MODEL
 *   A queue is a FIFO-ish broker-side buffer. Senders push messages in, one
 *   or more receivers pull them out. Each message is delivered to exactly one
 *   receiver (compare with topics in script 08, where every subscription gets
 *   its own copy).
 *
 * KEY APIs
 *   ServiceBusClient        — the data plane entry point. Thread-safe.
 *                             Treat as a singleton in real apps.
 *   ServiceBusSender        — created from the client, scoped to one entity.
 *   ServiceBusReceiver      — same, but for receiving.
 *   ServiceBusMessage       — what you send.
 *   ServiceBusReceivedMessage — what you receive (it adds broker properties
 *                             like SequenceNumber, EnqueuedTime, DeliveryCount).
 *
 * RECEIVE MODES — the most important decision in this script
 *
 *   PeekLock (default, "at-least-once")
 *     • Broker hands you the message and puts a lock on it (default 30s).
 *     • You MUST resolve the lock by calling one of:
 *         CompleteMessageAsync     → success, remove from queue
 *         AbandonMessageAsync      → release lock; DeliveryCount++
 *         DeadLetterMessageAsync   → move to DLQ (see script 05)
 *         DeferMessageAsync        → set aside for later (see script 06)
 *     • If you do nothing, the lock expires and the message is redelivered.
 *     • Safe for production: a crash mid-processing means redelivery, not loss.
 *
 *   ReceiveAndDelete ("at-most-once")
 *     • Broker deletes the message the moment it's handed to you.
 *     • Faster (no round-trip to Complete).
 *     • A crash AFTER receive but BEFORE you persist the work = data loss.
 *     • Use only when occasional loss is acceptable (e.g. high-volume telemetry).
 *
 * RUN
 *   dotnet run scripts/01-queue-send-receive.cs
 * ============================================================================
 */

using Azure.Messaging.ServiceBus;

// The client owns a connection pool. Use one per namespace per process,
// not one per message — repeatedly creating clients is expensive.
await using var client = new ServiceBusClient(Config.ConnectionString);

// A sender is bound to a single queue (or topic). Cheap to create.
await using var sender = client.CreateSender(Config.QueueName);

Console.WriteLine($"Queue: {Config.QueueName}");


// ---------------------------------------------------------------------------
// 1) Send a single message
//
//    We set MessageId explicitly. Two reasons:
//      a) Useful for tracing & debugging.
//      b) If the queue has RequiresDuplicateDetection=true, the broker uses
//         MessageId to reject duplicate sends inside the dedupe window.
// ---------------------------------------------------------------------------
var message = new ServiceBusMessage("Hello, Service Bus!")
{
    MessageId   = Guid.NewGuid().ToString(),
    ContentType = "text/plain"
};
await sender.SendMessageAsync(message);
Console.WriteLine($"Sent message {message.MessageId}");


// ---------------------------------------------------------------------------
// 2) Send a small list in one call
//
//    SendMessagesAsync(IEnumerable<...>) auto-batches under the hood. For
//    finer control (e.g. streaming huge lists), see the explicit batching
//    pattern in script 03.
//
//    Note `Subject` — this is the broker-recognised "label" field. It's
//    routable by CorrelationFilter (cheap), which we'll use in script 08.
// ---------------------------------------------------------------------------
await sender.SendMessagesAsync(new[]
{
    new ServiceBusMessage("apple")  { Subject = "fruit" },
    new ServiceBusMessage("banana") { Subject = "fruit" },
    new ServiceBusMessage("carrot") { Subject = "veg"   },
});
Console.WriteLine("Sent 3 more messages.");


// ---------------------------------------------------------------------------
// 3) Receive in PeekLock mode (the default — note we don't pass options)
//
//    We loop up to 4 times so we drain the messages we just sent. The
//    `maxWaitTime` is how long the broker will hold the request open if
//    nothing is available — this is "long polling", not busy waiting.
// ---------------------------------------------------------------------------
await using var receiver = client.CreateReceiver(Config.QueueName);

for (int i = 0; i < 4; i++)
{
    var msg = await receiver.ReceiveMessageAsync(maxWaitTime: TimeSpan.FromSeconds(5));
    if (msg is null) { Console.WriteLine("(no more messages)"); break; }

    // DeliveryCount > 1 means this message has been delivered before and a
    // previous receiver either crashed or called AbandonMessageAsync.
    Console.WriteLine($"Received: {msg.Body}  | subject={msg.Subject ?? "-"}  | deliveryCount={msg.DeliveryCount}");

    // Complete = "I'm done with this, remove it from the queue." Without
    // this call the lock would eventually expire and the message would be
    // redelivered.
    await receiver.CompleteMessageAsync(msg);
}


// ---------------------------------------------------------------------------
// 4) ReceiveAndDelete mode — for comparison
//
//    Notice we DON'T call CompleteMessageAsync. The broker already deleted
//    the message server-side when it was handed to us. If this process
//    crashed between the await and our Console.WriteLine, the message would
//    be gone forever.
// ---------------------------------------------------------------------------
await sender.SendMessageAsync(new ServiceBusMessage("delete-on-receive demo"));

await using var fast = client.CreateReceiver(Config.QueueName, new ServiceBusReceiverOptions
{
    ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete
});

var m = await fast.ReceiveMessageAsync(TimeSpan.FromSeconds(5));
Console.WriteLine($"Got (and already deleted): {m?.Body}");


// ---------------------------------------------------------------------------
// 5) Final cleanup — drain anything left in the queue
//
//    This uses ReceiveAndDelete intentionally so rerunning the demo starts
//    from an empty queue even if older messages were still sitting there.
// ---------------------------------------------------------------------------
var drainedCount = 0;
while (true)
{
    var leftover = await fast.ReceiveMessageAsync(TimeSpan.FromSeconds(1));
    if (leftover is null) break;
    drainedCount++;
}

Console.WriteLine($"Cleared {drainedCount} leftover message(s) from the queue.");

Console.WriteLine("\nDone. Next: 02-message-properties.cs");
