#:package Azure.Messaging.ServiceBus@7.18.2
#:project ../src/SbDemo.Shared/SbDemo.Shared.csproj
/*
 * 01 · Queue: Send & Receive
 * --------------------------
 *  - Create a ServiceBusClient + Sender + Receiver
 *  - Send a single message and a small batch
 *  - Receive in PeekLock and explicitly Complete
 *  - Demonstrate ReceiveAndDelete mode
 */

using Azure.Messaging.ServiceBus;

await using var client = new ServiceBusClient(Config.ConnectionString);
await using var sender = client.CreateSender(Config.QueueName);

// 1. Single message
var message = new ServiceBusMessage("Hello, Service Bus!")
{
    MessageId   = Guid.NewGuid().ToString(),
    ContentType = "text/plain"
};
await sender.SendMessageAsync(message);
Console.WriteLine($"Sent message {message.MessageId}");

// 2. Small list
await sender.SendMessagesAsync(new[]
{
    new ServiceBusMessage("apple")  { Subject = "fruit" },
    new ServiceBusMessage("banana") { Subject = "fruit" },
    new ServiceBusMessage("carrot") { Subject = "veg"   },
});
Console.WriteLine("Sent 3 more messages.");

// 3. PeekLock receive
await using var receiver = client.CreateReceiver(Config.QueueName);
for (int i = 0; i < 4; i++)
{
    var msg = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));
    if (msg is null) { Console.WriteLine("(no more messages)"); break; }

    Console.WriteLine($"Received: {msg.Body} | subject={msg.Subject} | deliveryCount={msg.DeliveryCount}");
    await receiver.CompleteMessageAsync(msg);
}

// 4. ReceiveAndDelete (at-most-once)
await sender.SendMessageAsync(new ServiceBusMessage("delete-on-receive demo"));
await using var fast = client.CreateReceiver(Config.QueueName, new ServiceBusReceiverOptions
{
    ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete
});
var m = await fast.ReceiveMessageAsync(TimeSpan.FromSeconds(5));
Console.WriteLine($"Got (and already deleted): {m?.Body}");
