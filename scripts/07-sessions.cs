#:package Azure.Messaging.ServiceBus@7.18.2
#:project ../src/SbDemo.Shared/SbDemo.Shared.csproj
/*
 * 07 · Sessions
 * -------------
 * FIFO ordering within a SessionId, plus session state.
 */

using Azure.Messaging.ServiceBus;

await using var client = new ServiceBusClient(Config.ConnectionString);
await using var sender = client.CreateSender(Config.SessionQueueName);

await sender.SendMessagesAsync(new[]
{
    new ServiceBusMessage("A1") { SessionId = "alice" },
    new ServiceBusMessage("B1") { SessionId = "bob"   },
    new ServiceBusMessage("A2") { SessionId = "alice" },
    new ServiceBusMessage("B2") { SessionId = "bob"   },
    new ServiceBusMessage("A3") { SessionId = "alice" },
    new ServiceBusMessage("B3") { SessionId = "bob"   },
});
Console.WriteLine("Sent 3 messages each for alice and bob.");

var sessionReceiver = await client.AcceptNextSessionAsync(Config.SessionQueueName);
Console.WriteLine($"Locked session: {sessionReceiver.SessionId}");

while (true)
{
    var msg = await sessionReceiver.ReceiveMessageAsync(TimeSpan.FromSeconds(2));
    if (msg is null) break;
    Console.WriteLine($"  [{sessionReceiver.SessionId}] {msg.Body}");
    await sessionReceiver.CompleteMessageAsync(msg);
}
await sessionReceiver.SetSessionStateAsync(new BinaryData($"processed:{DateTimeOffset.UtcNow:o}"));
var state = await sessionReceiver.GetSessionStateAsync();
Console.WriteLine($"Session state: {state}");
await sessionReceiver.CloseAsync();

// Session processor
await sender.SendMessagesAsync(new[]
{
    new ServiceBusMessage("X1") { SessionId = "carol" },
    new ServiceBusMessage("X2") { SessionId = "carol" },
    new ServiceBusMessage("Y1") { SessionId = "dave"  },
});

await using var sp = client.CreateSessionProcessor(Config.SessionQueueName, new ServiceBusSessionProcessorOptions
{
    MaxConcurrentSessions = 2,
    AutoCompleteMessages = true,
});
sp.ProcessMessageAsync += async args =>
{
    Console.WriteLine($"[session {args.SessionId}] {args.Message.Body}");
    await Task.CompletedTask;
};
sp.ProcessErrorAsync += args => { Console.WriteLine(args.Exception.Message); return Task.CompletedTask; };

await sp.StartProcessingAsync();
await Task.Delay(TimeSpan.FromSeconds(4));
await sp.StopProcessingAsync();
