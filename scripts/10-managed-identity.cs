#:package Azure.Messaging.ServiceBus@7.18.2
#:project ../src/SbDemo.Shared/SbDemo.Shared.csproj
#:package Azure.Identity@1.13.1
/*
 * 10 · DefaultAzureCredential & Managed Identity
 * ----------------------------------------------
 * Uses Entra credentials instead of a connection string.
 * Requires SERVICEBUS_NAMESPACE in .env and a Data Sender/Receiver/Owner
 * role assignment for your identity (see README).
 */

using Azure.Identity;
using Azure.Messaging.ServiceBus;

var credential = new DefaultAzureCredential();
await using var client = new ServiceBusClient(Config.FullyQualifiedNamespace, credential);
Console.WriteLine($"Connected to {client.FullyQualifiedNamespace} via Entra credential.");

await using var sender = client.CreateSender(Config.QueueName);
await sender.SendMessageAsync(new ServiceBusMessage("hello from DefaultAzureCredential"));

await using var receiver = client.CreateReceiver(Config.QueueName);
var msg = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));
Console.WriteLine($"Got: {msg?.Body}");
if (msg is not null) await receiver.CompleteMessageAsync(msg);
