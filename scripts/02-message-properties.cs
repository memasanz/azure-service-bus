#:package Azure.Messaging.ServiceBus@7.18.2
#:project ../src/SbDemo.Shared/SbDemo.Shared.csproj

/*
 * ============================================================================
 * 02 · Message Properties — System vs Application
 * ============================================================================
 *
 * OBJECTIVE
 *   Understand the two layers of metadata on every Service Bus message and
 *   when to use each.
 *
 * THE TWO LAYERS
 *
 *   SYSTEM PROPERTIES — fields the broker knows about and acts on.
 *     MessageId        - your idempotency key (used for dedupe)
 *     CorrelationId    - your distributed-trace id (echo back in replies)
 *     Subject ("Label")- routable by CorrelationFilter on subscriptions
 *     ContentType      - MIME hint for receivers (e.g. application/json)
 *     ReplyTo          - the queue/topic the receiver should reply on
 *     SessionId        - groups messages into FIFO partitions (see 07)
 *     TimeToLive       - max age before the broker dead-letters or drops it
 *
 *   APPLICATION PROPERTIES — your own key/value bag.
 *     • Free-form: strings, numbers, booleans, DateTime, etc.
 *     • Used for routing via SqlFilter on subscriptions (see 08).
 *     • The body is opaque to the broker, so put anything you might
 *       route or query by here, not buried in the JSON body.
 *
 *   BROKER-ASSIGNED PROPERTIES (read-only on receive)
 *     SequenceNumber   - monotonically increasing per entity
 *     EnqueuedTime     - when the broker accepted the send
 *     DeliveryCount    - how many times this message has been delivered
 *     LockedUntil      - when the PeekLock expires
 *
 * RULE OF THUMB
 *   • The BODY is for your domain payload (often JSON).
 *   • SYSTEM properties are for protocol concerns (tracing, routing, dedupe).
 *   • APP properties are for your own routing/filtering metadata.
 *
 * RUN
 *   dotnet run scripts/02-message-properties.cs
 * ============================================================================
 */

using Azure.Messaging.ServiceBus;
using System.Text.Json;

await using var client = new ServiceBusClient(Config.ConnectionString);
await using var sender = client.CreateSender(Config.QueueName);


// ---------------------------------------------------------------------------
// 1) Build a richly-decorated message
//
//    The body is JSON; the system properties give it routability and
//    traceability; the application properties carry our business metadata.
// ---------------------------------------------------------------------------
var order = new { OrderId = 42, Customer = "acme", Total = 199.99m };

var msg = new ServiceBusMessage(JsonSerializer.Serialize(order))
{
    // MessageId — also used as the dedupe key by the broker (when enabled).
    MessageId     = order.OrderId.ToString(),

    // CorrelationId — carry through your distributed trace id.
    CorrelationId = "trace-abc-123",

    // Subject is the routable "label" — cheap to filter on (see 08).
    Subject       = "order.created",

    // ContentType is just a hint to receivers; the broker doesn't parse it.
    ContentType   = "application/json",

    // TTL: after this much time, the broker will dead-letter or drop the message.
    TimeToLive    = TimeSpan.FromMinutes(10),

    // ApplicationProperties — your free-form metadata. These get propagated
    // with the message and are visible to receivers AND to subscription
    // filters (see 08).
    ApplicationProperties =
    {
        ["priority"] = "high",
        ["region"]   = "us-east",
        ["tenantId"] = "acme",
    }
};

await sender.SendMessageAsync(msg);
Console.WriteLine($"Sent {msg.MessageId} ({msg.Subject})");


// ---------------------------------------------------------------------------
// 2) Receive it and print every field
//
//    Notice the broker-assigned properties (SequenceNumber, EnqueuedTime,
//    DeliveryCount) that weren't on the message when we sent it.
// ---------------------------------------------------------------------------
await using var receiver = client.CreateReceiver(Config.QueueName);
var received = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));

Console.WriteLine();
Console.WriteLine("=== System / broker properties ===");
Console.WriteLine($"MessageId      : {received!.MessageId}");
Console.WriteLine($"CorrelationId  : {received.CorrelationId}");
Console.WriteLine($"Subject        : {received.Subject}");
Console.WriteLine($"ContentType    : {received.ContentType}");
Console.WriteLine($"SequenceNumber : {received.SequenceNumber}    // broker-assigned, monotonic");
Console.WriteLine($"EnqueuedTime   : {received.EnqueuedTime:o}    // when broker accepted send");
Console.WriteLine($"DeliveryCount  : {received.DeliveryCount}      // 1 on first delivery");
Console.WriteLine($"TimeToLive     : {received.TimeToLive}");

Console.WriteLine();
Console.WriteLine("=== Application properties ===");
foreach (var kv in received.ApplicationProperties)
    Console.WriteLine($"  {kv.Key} = {kv.Value}");

Console.WriteLine();
Console.WriteLine($"Body: {received.Body}");

await receiver.CompleteMessageAsync(received);


// ---------------------------------------------------------------------------
// 3) Bonus — duplicate detection
//
//    Not enabled on demo-queue, but worth knowing. When a queue is created
//    with RequiresDuplicateDetection = true, the broker remembers MessageIds
//    for a configurable window and silently drops any send with a MessageId
//    already seen. This gives you exactly-once-send semantics for retries.
//
//        await admin.CreateQueueAsync(new CreateQueueOptions("dedupe-queue")
//        {
//            RequiresDuplicateDetection         = true,
//            DuplicateDetectionHistoryTimeWindow = TimeSpan.FromMinutes(10),
//        });
// ---------------------------------------------------------------------------

Console.WriteLine("\nDone. Next: 03-batching.cs");
