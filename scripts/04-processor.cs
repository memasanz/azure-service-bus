#:package Azure.Messaging.ServiceBus@7.18.2
#:project ../src/SbDemo.Shared/SbDemo.Shared.csproj
/*
 * 04 · ServiceBusProcessor
 * ------------------------
 * Event-driven receive API with built-in concurrency and lock renewal.
 */

using Azure.Messaging.ServiceBus;

await using var client = new ServiceBusClient(Config.ConnectionString);
await using var sender = client.CreateSender(Config.QueueName);

await sender.SendMessagesAsync(Enumerable.Range(1, 20)
    .Select(i => new ServiceBusMessage($"work-item-{i}")));
Console.WriteLine("Seeded 20 work items.");

await using var processor = client.CreateProcessor(Config.QueueName, new ServiceBusProcessorOptions
{
    MaxConcurrentCalls         = 4,
    AutoCompleteMessages       = true,
    MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5)
});

int processed = 0;

processor.ProcessMessageAsync += async args =>
{
    Interlocked.Increment(ref processed);
    Console.WriteLine($"[{Environment.CurrentManagedThreadId}] {args.Message.Body}");
    await Task.Delay(100);
};

processor.ProcessErrorAsync += args =>
{
    Console.WriteLine($"ERROR from {args.EntityPath}: {args.Exception.Message}");
    return Task.CompletedTask;
};

await processor.StartProcessingAsync();
await Task.Delay(TimeSpan.FromSeconds(5));
await processor.StopProcessingAsync();

Console.WriteLine($"\nProcessed {processed} messages.");
