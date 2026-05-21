#:package Azure.Messaging.ServiceBus@7.18.2
#:project ../src/SbDemo.Shared/SbDemo.Shared.csproj

/*
 * ============================================================================
 * 07 · Sessions — FIFO Within a Logical Stream
 * ============================================================================
 *
 * OBJECTIVE
 *   Use a session-enabled queue to get guaranteed FIFO ordering within a
 *   logical partition (e.g. per customer, per order, per chat) while still
 *   processing different partitions in parallel.
 *
 * THE PROBLEM SESSIONS SOLVE
 *   A plain Service Bus queue does NOT guarantee strict FIFO when there are
 *   multiple receivers — messages can be processed out of order across
 *   concurrent receivers, and Abandon/redeliver makes it worse.
 *
 *   Sometimes you NEED order — e.g. for one chat conversation, "user
 *   sent message", "assistant replied", "user followed up" must be processed
 *   in that order. But across DIFFERENT chats, order between conversations
 *   doesn't matter.
 *
 *   Sessions = ordered processing PER session, parallel processing ACROSS
 *   sessions.
 *
 * HOW THEY WORK
 *   1. The queue (or subscription) is created with RequiresSession=true.
 *      We set this on demo-sessions in Bicep.
 *   2. Senders set SessionId on each message. Messages with the same
 *      SessionId form a logical FIFO stream.
 *   3. Receivers don't just "receive" — they ACCEPT a session, which
 *      locks the entire session to that receiver. While locked, no other
 *      receiver can touch any message in that session.
 *   4. The receiver drains messages in order, then closes the session.
 *      Another receiver can then accept that session (or a different one).
 *
 * SESSION STATE
 *   Each session has a small (~256 KB) blob you can SetSessionStateAsync /
 *   GetSessionStateAsync. This is broker-side scratch space ideal for
 *   tracking "where am I in the conversation" without an external store.
 *
 * TWO RECEIVE APIS
 *   ServiceBusSessionReceiver       — pull-based, one session at a time
 *                                     (analog of ServiceBusReceiver)
 *
 *   ServiceBusSessionProcessor      — push-based, handles multiple sessions
 *                                     in parallel (analog of ServiceBusProcessor)
 *
 * RUN
 *   dotnet run scripts/07-sessions.cs
 * ============================================================================
 */

using Azure.Messaging.ServiceBus;

await using var client = new ServiceBusClient(Config.ConnectionString);
await using var sender = client.CreateSender(Config.SessionQueueName);


// ---------------------------------------------------------------------------
// 1) Send messages for two sessions, interleaved
//
//    Although we send them mixed in (A,B,A,B,A,B), the broker stores them
//    such that "all alice messages stay in alice's FIFO stream" and same
//    for bob. Watch the receive output below — alice's messages come out
//    A1, A2, A3 in order, never interleaved with bob's.
// ---------------------------------------------------------------------------
await sender.SendMessagesAsync(new[]
{
    new ServiceBusMessage("A1") { SessionId = "alice" },
    new ServiceBusMessage("B1") { SessionId = "bob"   },
    new ServiceBusMessage("A2") { SessionId = "alice" },
    new ServiceBusMessage("B2") { SessionId = "bob"   },
    new ServiceBusMessage("A3") { SessionId = "alice" },
    new ServiceBusMessage("B3") { SessionId = "bob"   },
});
Console.WriteLine("Sent 3 messages each for alice and bob (interleaved).\n");


// ---------------------------------------------------------------------------
// 2) Pull-based session receive — accept whichever session the broker
//    hands us first, drain it in order, close it.
//
//    AcceptNextSessionAsync blocks until a session becomes available, then
//    locks that SessionId so no other receiver can see any of its messages.
//    Alternatively, AcceptSessionAsync("alice") locks that specific one
//    (waiting if it's currently held by someone else).
// ---------------------------------------------------------------------------
var sessionReceiver = await client.AcceptNextSessionAsync(Config.SessionQueueName);
Console.WriteLine($"Locked session: {sessionReceiver.SessionId}");

// While this session is locked, no other receiver can see any of its messages.
// We drain it fully here; in a real app you'd loop until empty or for a
// budgeted amount of time.
while (true)
{
    var msg = await sessionReceiver.ReceiveMessageAsync(TimeSpan.FromSeconds(2));
    if (msg is null) break;
    Console.WriteLine($"  [{sessionReceiver.SessionId}] {msg.Body}  (seq={msg.SequenceNumber})");
    await sessionReceiver.CompleteMessageAsync(msg);
}


// ---------------------------------------------------------------------------
// 3) Session state — broker-side per-session scratch space
//
//    Common pattern: store a checkpoint, the last processed sequence, or
//    a small state-machine cursor. Limited to ~256 KB.
// ---------------------------------------------------------------------------
await sessionReceiver.SetSessionStateAsync(new BinaryData($"processed-up-to:{DateTimeOffset.UtcNow:o}"));
var state = await sessionReceiver.GetSessionStateAsync();
Console.WriteLine($"\nSession state for {sessionReceiver.SessionId}: {state}");

await sessionReceiver.CloseAsync();   // release the session lock


// ---------------------------------------------------------------------------
// 4) Push-based session processor — the production-ready API
//
//    SessionProcessor is to SessionReceiver what Processor is to Receiver.
//    It locks one session per "slot", drains it, then moves on. With
//    MaxConcurrentSessions=2 we'll process two sessions in parallel.
// ---------------------------------------------------------------------------
Console.WriteLine("\n--- ServiceBusSessionProcessor demo ---");

// Seed work for two new sessions so the processor has something to do.
await sender.SendMessagesAsync(new[]
{
    new ServiceBusMessage("X1") { SessionId = "carol" },
    new ServiceBusMessage("X2") { SessionId = "carol" },
    new ServiceBusMessage("Y1") { SessionId = "dave"  },
    new ServiceBusMessage("Y2") { SessionId = "dave"  },
});

await using var sp = client.CreateSessionProcessor(Config.SessionQueueName, new ServiceBusSessionProcessorOptions
{
    MaxConcurrentSessions = 2,        // two sessions handled in parallel
    AutoCompleteMessages  = true,
});

sp.ProcessMessageAsync += async args =>
{
    // args.SessionId tells you which session this message belongs to.
    // Within one session, messages are processed strictly in order; across
    // sessions they're parallel.
    Console.WriteLine($"[session {args.SessionId}] {args.Message.Body}");
    await Task.Delay(50);
};

sp.ProcessErrorAsync += args =>
{
    Console.WriteLine($"ERROR: {args.Exception.Message}");
    return Task.CompletedTask;
};

await sp.StartProcessingAsync();
await Task.Delay(TimeSpan.FromSeconds(4));
await sp.StopProcessingAsync();

Console.WriteLine("\nDone. Next: 08-topics-subscriptions.cs");
