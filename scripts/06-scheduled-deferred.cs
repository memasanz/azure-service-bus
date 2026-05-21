#:package Azure.Messaging.ServiceBus@7.18.2
#:project ../src/SbDemo.Shared/SbDemo.Shared.csproj
/*
 * 06 · Scheduled & Deferred Messages
 * ----------------------------------
 * Scheduled: sender decides a future enqueue time.
 * Deferred:  receiver decides to set the message aside until needed.
 */

using Azure.Messaging.ServiceBus;

await using var client = new ServiceBusClient(Config.ConnectionString);
await using var sender = client.CreateSender(Config.QueueName);
await using var receiver = client.CreateReceiver(Config.QueueName);

// 1. Schedule
var enqueueAt = DateTimeOffset.UtcNow.AddSeconds(10);
long seq = await sender.ScheduleMessageAsync(new ServiceBusMessage("future-message"), enqueueAt);
Console.WriteLine($"Scheduled (sequence={seq}) for {enqueueAt:HH:mm:ss}");

var arrived = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(15));
Console.WriteLine($"Received at {DateTimeOffset.UtcNow:HH:mm:ss}: {arrived?.Body}");
if (arrived is not null) await receiver.CompleteMessageAsync(arrived);

// 2. Defer
await sender.SendMessageAsync(new ServiceBusMessage("needs-extra-context") { MessageId = "ctx-1" });

var firstPass = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));
long deferredSeq = firstPass!.SequenceNumber;
await receiver.DeferMessageAsync(firstPass);
Console.WriteLine($"Deferred {firstPass.MessageId} (seq={deferredSeq})");

var none = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(2));
Console.WriteLine($"Normal receive saw: {(none?.Body?.ToString() ?? "(nothing)")}");

var deferred = await receiver.ReceiveDeferredMessageAsync(deferredSeq);
Console.WriteLine($"Got back: {deferred!.MessageId}  body={deferred.Body}");
await receiver.CompleteMessageAsync(deferred);
