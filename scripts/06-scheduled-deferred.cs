#:package Azure.Messaging.ServiceBus@7.18.2
#:project ../src/SbDemo.Shared/SbDemo.Shared.csproj

/*
 * ============================================================================
 * 06 · Scheduled & Deferred Messages
 * ============================================================================
 *
 * OBJECTIVE
 *   Understand two delay mechanisms that look similar but solve very
 *   different problems.
 *
 *                  SCHEDULED                       DEFERRED
 *                  ------------------------------  ------------------------------
 *   Who decides?   The SENDER, at send time        The RECEIVER, after receiving
 *   When?          Sets ScheduledEnqueueTime       Calls DeferMessageAsync
 *   Visible to     Yes — broker enqueues it at     No — invisible to normal
 *   normal Receive?  the scheduled time              Receive calls
 *   How to retrieve? Auto-delivered when its time   ReceiveDeferredMessageAsync(seq)
 *                    arrives                         — you MUST remember the
 *                                                     SequenceNumber yourself
 *   Cancelable?    Yes (CancelScheduledMessage)    Effectively no (just process it)
 *
 * USE CASES
 *   SCHEDULED
 *     • Reminders ("send this email in 24 hours")
 *     • Backoff between retries
 *     • Time-delayed workflows
 *
 *   DEFERRED
 *     • "I received this order but its customer record isn't created yet —
 *       set it aside until the customer-created event arrives."
 *     • Process steps that need data from another message that hasn't
 *       arrived yet, without blocking the queue.
 *     • Saga / state-machine patterns where messages must be replayed in
 *       a specific order.
 *
 * IMPORTANT WITH DEFERRED
 *   Deferred messages are still locked to the queue but invisible to
 *   normal receives. You MUST persist the SequenceNumber somewhere (your
 *   database, typically) — there is no API to "list" deferred messages.
 *   Lose the sequence number and the message is effectively stuck until
 *   it's TTL-purged.
 *
 * RUN
 *   dotnet run scripts/06-scheduled-deferred.cs
 * ============================================================================
 */

using Azure.Messaging.ServiceBus;

await using var client   = new ServiceBusClient(Config.ConnectionString);
await using var sender   = client.CreateSender(Config.QueueName);
await using var receiver = client.CreateReceiver(Config.QueueName);


// ---------------------------------------------------------------------------
// 1) Schedule a message 10 seconds in the future
//
//    ScheduleMessageAsync returns a SequenceNumber. You can use it to
//    cancel the scheduled message before its time arrives, via
//    CancelScheduledMessageAsync(seq).
// ---------------------------------------------------------------------------
var enqueueAt = DateTimeOffset.UtcNow.AddSeconds(10);

long scheduledSeq = await sender.ScheduleMessageAsync(
    new ServiceBusMessage("future-message"),
    enqueueAt);

Console.WriteLine($"Scheduled (sequence={scheduledSeq}) for {enqueueAt:HH:mm:ss} UTC");
Console.WriteLine("Waiting for it to arrive...");

// Try a couple of receives — first one won't see it (not yet enqueued),
// the second one should.
var early = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(3));
Console.WriteLine($"  After 3s: {(early?.Body?.ToString() ?? "(nothing — still in the future)")}");
if (early is not null) await receiver.CompleteMessageAsync(early);

var arrived = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(15));
Console.WriteLine($"  Received at {DateTimeOffset.UtcNow:HH:mm:ss}: {arrived?.Body}");
if (arrived is not null) await receiver.CompleteMessageAsync(arrived);


// ---------------------------------------------------------------------------
// 2) Defer a message
//
//    Flow:
//      a) Receive the message normally
//      b) Decide we can't process it yet (maybe a dependency isn't ready)
//      c) Call DeferMessageAsync — broker leaves it in the queue but hides
//         it from future ReceiveMessageAsync calls
//      d) Remember its SequenceNumber (we'll print it; a real app would
//         store it in a database)
// ---------------------------------------------------------------------------
Console.WriteLine();

await sender.SendMessageAsync(new ServiceBusMessage("needs-extra-context")
{
    MessageId = "ctx-1"
});

var firstPass = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));
long deferredSeq = firstPass!.SequenceNumber;

await receiver.DeferMessageAsync(firstPass);
Console.WriteLine($"Deferred {firstPass.MessageId} (sequence={deferredSeq}).");
Console.WriteLine("In a real app you would now persist this sequence number.");


// Prove that a normal receive doesn't see deferred messages:
var none = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(2));
Console.WriteLine($"Normal receive sees: {(none?.Body?.ToString() ?? "(nothing — the deferred message is invisible)")}");


// ---------------------------------------------------------------------------
// 3) Retrieve the deferred message later by its SequenceNumber
//
//    ...as if you had just received it. You still need to Complete it.
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("Now we have the context we need — retrieving the deferred message...");

var deferred = await receiver.ReceiveDeferredMessageAsync(deferredSeq);
Console.WriteLine($"Got back: {deferred!.MessageId}  body={deferred.Body}  deliveryCount={deferred.DeliveryCount}");
await receiver.CompleteMessageAsync(deferred);


Console.WriteLine("\nDone. Next: 07-sessions.cs");
