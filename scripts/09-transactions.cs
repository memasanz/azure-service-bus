#:package Azure.Messaging.ServiceBus@7.18.2
#:project ../src/SbDemo.Shared/SbDemo.Shared.csproj
/*
 * 09 · Transactions
 * -----------------
 * Atomic receive+send+complete using TransactionScope. Requires
 * EnableCrossEntityTransactions when the tx spans multiple entities.
 */

using Azure.Messaging.ServiceBus;
using System.Transactions;

await using var client = new ServiceBusClient(Config.ConnectionString, new ServiceBusClientOptions
{
    EnableCrossEntityTransactions = true
});

await using var sender = client.CreateSender(Config.QueueName);
await using var receiver = client.CreateReceiver(Config.QueueName);

await sender.SendMessageAsync(new ServiceBusMessage("input-A"));

var msg = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));
Console.WriteLine($"Received: {msg!.Body}");

using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
{
    await sender.SendMessageAsync(new ServiceBusMessage($"processed:{msg.Body}"));
    await receiver.CompleteMessageAsync(msg);
    scope.Complete();
}
Console.WriteLine("Committed.");

var forwarded = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));
Console.WriteLine($"Forwarded message visible: {forwarded?.Body}");
if (forwarded is not null) await receiver.CompleteMessageAsync(forwarded);

// Rollback example
await sender.SendMessageAsync(new ServiceBusMessage("input-B"));
var m = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));
try
{
    using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
    await sender.SendMessageAsync(new ServiceBusMessage("shouldnt-exist"));
    await receiver.CompleteMessageAsync(m!);
    throw new Exception("simulated failure before scope.Complete()");
}
catch (Exception ex)
{
    Console.WriteLine($"Caught: {ex.Message}  → both ops rolled back.");
}

Console.WriteLine("input-B remains for redelivery (after its lock expires).");
