#:package Azure.Messaging.ServiceBus@7.18.2
#:project ../src/SbDemo.Shared/SbDemo.Shared.csproj

/*
 * ============================================================================
 * 05 · Dead-Letter Queue (DLQ)
 * ============================================================================
 *
 * OBJECTIVE
 *   Understand how messages end up in the DLQ, how to read them, and how
 *   to resubmit a fixed message back to the main queue.
 *
 * WHAT IS THE DLQ?
 *   Every queue (and every topic subscription) has a sibling sub-queue
 *   called "$DeadLetterQueue". You don't create it — it's free with the
 *   parent entity. Messages land there for THREE reasons:
 *
 *     1. EXPLICIT — your code called DeadLetterMessageAsync(...)
 *
 *     2. EXCEEDED MaxDeliveryCount — the broker tried to deliver the
 *        message MaxDeliveryCount times (default 10; we set 3 on demo-dlq).
 *        Each Abandon or lock expiry increments DeliveryCount.
 *
 *     3. TTL EXPIRED — the message's TimeToLive elapsed AND the queue
 *        has DeadLetteringOnMessageExpiration=true (we set this).
 *
 * KEY POINT
 *   DLQ messages are NOT auto-purged. They stay until something processes
 *   them. You're expected to monitor DLQ depth (use the Admin client's
 *   GetQueueRuntimePropertiesAsync().DeadLetterMessageCount) and alert.
 *
 * DEAD-LETTER METADATA
 *   When you call DeadLetterMessageAsync(reason, description), the broker
 *   stores those strings as special application properties on the message:
 *     ApplicationProperties["DeadLetterReason"]
 *     ApplicationProperties["DeadLetterErrorDescription"]
 *   And exposes them as convenience properties: msg.DeadLetterReason etc.
 *
 * COMMON PATTERNS
 *   • Validation failures           → DLQ explicitly with a clear reason
 *   • Permanent business errors     → DLQ explicitly (don't retry forever)
 *   • Transient errors              → Throw / Abandon and let MaxDelivery do it
 *   • Resubmit fixed messages       → Read from DLQ, send back to main queue
 *
 * RUN
 *   dotnet run scripts/05-dead-letter.cs
 * ============================================================================
 */

using Azure.Messaging.ServiceBus;

await using var client = new ServiceBusClient(Config.ConnectionString);
await using var sender = client.CreateSender(Config.DlqDemoQueueName);


// ---------------------------------------------------------------------------
// 1) Send a message we'll deliberately reject
// ---------------------------------------------------------------------------
await sender.SendMessageAsync(new ServiceBusMessage("bad-payload")
{
    MessageId = "demo-bad-1",
    ApplicationProperties = { ["expectFailure"] = true }
});
Console.WriteLine("Sent a 'bad' message to demo-dlq.\n");


// ---------------------------------------------------------------------------
// 2) Receive and explicitly dead-letter it
//
//    reason / description are FREE-FORM strings that you set so future
//    you (or your operations team) understands WHY a message ended up here.
//    Treat them like exception messages: be specific.
// ---------------------------------------------------------------------------
await using var receiver = client.CreateReceiver(Config.DlqDemoQueueName);
var msg = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));

Console.WriteLine($"Got: {msg!.Body}  (DeliveryCount={msg.DeliveryCount})");

await receiver.DeadLetterMessageAsync(
    msg,
    deadLetterReason:           "ValidationFailed",
    deadLetterErrorDescription: "Payload failed schema validation: missing 'customerId'");

Console.WriteLine("Dead-lettered.\n");


// ---------------------------------------------------------------------------
// 3) Read from the DLQ sub-queue
//
//    The trick: set SubQueue = SubQueue.DeadLetter on the receiver options.
//    Everything else is identical to receiving from the main queue.
// ---------------------------------------------------------------------------
await using var dlq = client.CreateReceiver(Config.DlqDemoQueueName, new ServiceBusReceiverOptions
{
    SubQueue = SubQueue.DeadLetter
});

var dead = await dlq.ReceiveMessageAsync(TimeSpan.FromSeconds(5));

Console.WriteLine("=== From the DLQ ===");
Console.WriteLine($"Body                       : {dead!.Body}");
Console.WriteLine($"MessageId                  : {dead.MessageId}");
Console.WriteLine($"DeadLetterReason           : {dead.DeadLetterReason}");
Console.WriteLine($"DeadLetterErrorDescription : {dead.DeadLetterErrorDescription}");
Console.WriteLine("App properties:");
foreach (var kv in dead.ApplicationProperties)
    Console.WriteLine($"  {kv.Key} = {kv.Value}");


// ---------------------------------------------------------------------------
// 4) Resubmit pattern — send a fresh copy to the main queue, complete the DLQ entry
//
//    NEVER send the received message object back directly — its system
//    properties (SequenceNumber, EnqueuedTime, the DeadLetter* fields...)
//    are read-only on receive. Build a fresh ServiceBusMessage instead.
//
//    We also strip the DeadLetter* keys from ApplicationProperties so the
//    resubmitted message doesn't look like it's been dead-lettered before.
// ---------------------------------------------------------------------------
var resubmitted = new ServiceBusMessage(dead.Body)
{
    MessageId   = dead.MessageId,
    ContentType = dead.ContentType,
    Subject     = dead.Subject,
};

foreach (var kv in dead.ApplicationProperties)
{
    if (kv.Key.StartsWith("DeadLetter")) continue;
    resubmitted.ApplicationProperties[kv.Key] = kv.Value;
}

await sender.SendMessageAsync(resubmitted);
await dlq.CompleteMessageAsync(dead);

Console.WriteLine("\nResubmitted and removed from DLQ.");


// ---------------------------------------------------------------------------
// 5) Tip — monitoring DLQ depth
//
//        var rp = await admin.GetQueueRuntimePropertiesAsync("demo-dlq");
//        Console.WriteLine(rp.Value.DeadLetterMessageCount);
//
//    In Azure Monitor, "DeadletteredMessages" is a built-in metric per
//    namespace — wire an alert on growth rate.
// ---------------------------------------------------------------------------

Console.WriteLine("Done. Next: 06-scheduled-deferred.cs");
