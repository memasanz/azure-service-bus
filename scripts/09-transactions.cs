#:package Azure.Messaging.ServiceBus@7.18.2
#:project ../src/SbDemo.Shared/SbDemo.Shared.csproj

/*
 * ============================================================================
 * 09 · Transactions — Atomic Receive + Send + Complete
 * ============================================================================
 *
 * OBJECTIVE
 *   Group multiple Service Bus operations into a single atomic unit:
 *   either all succeed, or none do. Eliminates the "I sent the reply but
 *   crashed before completing the input" failure mode.
 *
 * THE CLASSIC PROBLEM
 *   A worker receives "OrderPlaced", computes something, sends
 *   "OrderProcessed", and completes the input message. Without a
 *   transaction, any crash between the two sends/completes can lead to:
 *
 *     • Input completed but reply NOT sent      → silent data loss
 *     • Reply sent but input NOT completed      → reply will be re-sent
 *                                                  when the input is
 *                                                  redelivered (duplicate)
 *
 *   With a transaction, both operations commit together or roll back together.
 *
 * WHAT TRANSACTIONS CAN GROUP
 *   • Multiple Send / Schedule / CancelScheduled to the same entity
 *   • A Receive's Complete / Abandon / DeadLetter / Defer
 *   • Operations across MULTIPLE entities, IF EnableCrossEntityTransactions
 *     is set on the client AND the operations all flow through the same
 *     "via" entity. Common scenario: receive from queue A, send to topic B,
 *     complete on A.
 *
 * WHAT TRANSACTIONS CAN'T DO
 *   • They do NOT participate in distributed two-phase commit with
 *     external resources like SQL Server. The transaction stays inside
 *     Service Bus. For "send a message AND update the DB" atomically
 *     you need the Outbox pattern, not TransactionScope.
 *   • They cannot span multiple Service Bus namespaces.
 *
 * MECHANICS
 *   Wrap your operations in a System.Transactions.TransactionScope.
 *   Call scope.Complete() to commit. If you DON'T call Complete() (e.g.
 *   because an exception was thrown), Dispose() rolls everything back.
 *
 *   IMPORTANT: use TransactionScopeAsyncFlowOption.Enabled, otherwise the
 *   ambient transaction won't follow async/await across threads.
 *
 * RUN
 *   dotnet run scripts/09-transactions.cs
 * ============================================================================
 */

using Azure.Messaging.ServiceBus;
using System.Transactions;


// ---------------------------------------------------------------------------
// EnableCrossEntityTransactions = true is required when one transaction
// touches more than one entity. For a single-entity tx (like our first
// example) it's not strictly required, but it doesn't hurt.
// ---------------------------------------------------------------------------
await using var client = new ServiceBusClient(Config.ConnectionString, new ServiceBusClientOptions
{
    EnableCrossEntityTransactions = true
});

await using var sender   = client.CreateSender(Config.QueueName);
await using var receiver = client.CreateReceiver(Config.QueueName);


// ---------------------------------------------------------------------------
// 1) Atomic "process then forward" — commit path
//
//    Sequence inside the scope:
//      a) Send the result message
//      b) Complete the input message
//      c) scope.Complete()  → both commit
//
//    If we threw between (a) and (b), the broker would roll back BOTH:
//    the sent message wouldn't appear and the input would be redelivered.
// ---------------------------------------------------------------------------
await sender.SendMessageAsync(new ServiceBusMessage("input-A"));

var msg = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));
Console.WriteLine($"Received: {msg!.Body}");

using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
{
    // Imagine non-trivial processing here:
    var resultBody = $"processed:{msg.Body}";

    await sender.SendMessageAsync(new ServiceBusMessage(resultBody));
    await receiver.CompleteMessageAsync(msg);

    scope.Complete();   // commit
}
Console.WriteLine("Committed: both Send and Complete succeeded atomically.");

// The forwarded message should be visible now:
var forwarded = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));
Console.WriteLine($"Forwarded message visible: {forwarded?.Body}");
if (forwarded is not null) await receiver.CompleteMessageAsync(forwarded);


// ---------------------------------------------------------------------------
// 2) Rollback example — what happens when scope.Complete() is NOT reached
//
//    We deliberately throw before scope.Complete(). The TransactionScope's
//    Dispose() detects the missing Complete() and rolls back: the send
//    is discarded AND the input message remains locked (its lock will
//    expire and it will be redelivered with DeliveryCount incremented).
// ---------------------------------------------------------------------------
Console.WriteLine();
await sender.SendMessageAsync(new ServiceBusMessage("input-B"));
var m = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));
Console.WriteLine($"Received: {m!.Body}");

try
{
    using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

    await sender.SendMessageAsync(new ServiceBusMessage("would-have-been-the-reply"));
    await receiver.CompleteMessageAsync(m);

    throw new Exception("simulated failure before scope.Complete()");
    // scope.Complete();  // never reached
}
catch (Exception ex)
{
    Console.WriteLine($"Caught: {ex.Message}");
    Console.WriteLine("Both ops rolled back: 'would-have-been-the-reply' was discarded.");
}

Console.WriteLine("'input-B' remains in the queue; it will be redelivered once its lock expires.");
Console.WriteLine();


// ---------------------------------------------------------------------------
// 3) Cleanup — abandon the redelivery so this script is idempotent
//
//    Without this, every re-run leaves an extra "input-B" hanging around.
// ---------------------------------------------------------------------------
await Task.Delay(TimeSpan.FromSeconds(35));   // wait out the default 30s lock

await using var cleanup = client.CreateReceiver(Config.QueueName, new ServiceBusReceiverOptions
{
    ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete
});
while (true)
{
    var leftover = await cleanup.ReceiveMessageAsync(TimeSpan.FromSeconds(2));
    if (leftover is null) break;
    Console.WriteLine($"Cleaned up: {leftover.Body}");
}

Console.WriteLine("\nDone. Next: 10-managed-identity.cs");
