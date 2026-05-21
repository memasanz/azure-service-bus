#:package Azure.Messaging.ServiceBus@7.18.2
#:project ../src/SbDemo.Shared/SbDemo.Shared.csproj

/*
 * ============================================================================
 * 04 · ServiceBusProcessor — The Event-Driven Receive API
 * ============================================================================
 *
 * OBJECTIVE
 *   Move from "pull-based" Receive loops to the "push-based" Processor API.
 *   This is what you'd use in a real production worker / hosted service.
 *
 * WHY THE PROCESSOR INSTEAD OF A RECEIVE LOOP?
 *
 *   In script 01 you called ReceiveMessageAsync in a loop, processed each
 *   message, and called CompleteMessageAsync. That works, but you'd have to
 *   build the following yourself in production:
 *
 *     • Concurrency       — handle N messages in parallel
 *     • Lock renewal      — extend the lock for long-running handlers
 *     • Retry logic       — for transient errors
 *     • Error reporting   — for non-transient failures
 *     • Graceful shutdown — drain inflight messages, then stop
 *
 *   ServiceBusProcessor gives you all of that. You register two callbacks:
 *
 *     ProcessMessageAsync   — runs once per message (in parallel up to N)
 *     ProcessErrorAsync     — runs once per error (link failures, deserialization
 *                             errors, anything not surfaced through the message
 *                             handler)
 *
 *   ...and the SDK manages everything else.
 *
 * KEY OPTIONS
 *   MaxConcurrentCalls            — how many messages to handle in parallel
 *                                   (per processor instance). Default 1.
 *
 *   AutoCompleteMessages          — if true, the SDK calls CompleteMessageAsync
 *                                   for you when your handler returns normally,
 *                                   or AbandonMessageAsync if it throws.
 *
 *   MaxAutoLockRenewalDuration    — how long the SDK will keep renewing the
 *                                   PeekLock for you. Set this longer than
 *                                   your slowest expected handler.
 *
 *   PrefetchCount                 — pull N extra messages eagerly. Improves
 *                                   throughput at the cost of "starving"
 *                                   other receivers.
 *
 * SCALING
 *   MaxConcurrentCalls is per-receiver. To go beyond one machine, run
 *   multiple processor instances on multiple hosts — Service Bus will fan
 *   the messages out across them.
 *
 * RUN
 *   dotnet run scripts/04-processor.cs
 * ============================================================================
 */

using Azure.Messaging.ServiceBus;

await using var client = new ServiceBusClient(Config.ConnectionString);
await using var sender = client.CreateSender(Config.QueueName);


// ---------------------------------------------------------------------------
// 1) Seed some work so we have something to process
// ---------------------------------------------------------------------------
await sender.SendMessagesAsync(Enumerable.Range(1, 20)
    .Select(i => new ServiceBusMessage($"work-item-{i:D2}")));
Console.WriteLine("Seeded 20 work items.\n");


// ---------------------------------------------------------------------------
// 2) Create and configure the processor
//
//    With MaxConcurrentCalls=4 you'll see 4 different managed-thread IDs
//    interleaved in the output below. That's the SDK calling your handler
//    concurrently on up to 4 messages at a time.
// ---------------------------------------------------------------------------
await using var processor = client.CreateProcessor(Config.QueueName, new ServiceBusProcessorOptions
{
    MaxConcurrentCalls         = 4,
    AutoCompleteMessages       = true,
    MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5),
});

int processed = 0;


// ---------------------------------------------------------------------------
// 3) Wire up handlers
//
//    These are EVENTS — `+=` not assignment. The SDK enforces exactly one
//    subscriber per event; subscribing twice throws.
// ---------------------------------------------------------------------------
processor.ProcessMessageAsync += async args =>
{
    // Interlocked.Increment because this can run on multiple threads at once.
    var count = Interlocked.Increment(ref processed);

    Console.WriteLine($"[thread {Environment.CurrentManagedThreadId,2}] [{count:D2}] {args.Message.Body}");

    // Simulate work. In a real handler this would be DB writes, HTTP calls,
    // computation, etc. AutoCompleteMessages will Complete this on return.
    await Task.Delay(100);

    // If you throw here, the SDK will Abandon the message (DeliveryCount++)
    // and eventually it will land in the DLQ once MaxDeliveryCount is hit.
};

processor.ProcessErrorAsync += args =>
{
    // args.Exception      - what went wrong
    // args.EntityPath     - which queue/topic/subscription
    // args.ErrorSource    - which stage (Receive, RenewLock, Complete, etc.)
    // args.Identifier     - the processor's id (useful when running multiple)
    Console.WriteLine($"ERROR from {args.EntityPath} during {args.ErrorSource}: {args.Exception.Message}");
    return Task.CompletedTask;
};


// ---------------------------------------------------------------------------
// 4) Start, let it drain, stop
//
//    StartProcessingAsync returns immediately — the processing runs in the
//    background on SDK-managed tasks. StopProcessingAsync waits for inflight
//    handlers to finish (graceful shutdown).
// ---------------------------------------------------------------------------
await processor.StartProcessingAsync();
await Task.Delay(TimeSpan.FromSeconds(5));
await processor.StopProcessingAsync();

Console.WriteLine($"\nProcessed {processed} messages.");
Console.WriteLine("Done. Next: 05-dead-letter.cs");
