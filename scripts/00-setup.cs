#:package Azure.Messaging.ServiceBus@7.18.2
#:package Azure.Messaging.ServiceBus.Administration@7.18.2
#:project ../src/SbDemo.Shared/SbDemo.Shared.csproj

/*
 * ============================================================================
 * 00 · Setup & Connection Check
 * ============================================================================
 *
 * OBJECTIVE
 *   Verify that your local environment can talk to the Service Bus namespace
 *   that the Bicep template deployed. This is the "hello world" — if this
 *   script fails, none of the other scripts will work.
 *
 * WHAT YOU'LL LEARN
 *   - The difference between the *management* plane and the *data* plane
 *   - How to use ServiceBusAdministrationClient to list entities
 *   - Where the scripts read configuration from (.env or process env vars)
 *
 * KEY APIs
 *   ServiceBusAdministrationClient   — manages queues / topics / subscriptions
 *                                      (think: ARM operations, not messaging)
 *
 *   ServiceBusClient                 — the data plane (you'll use this in 01+)
 *
 * RUN
 *   dotnet run scripts/00-setup.cs
 * ============================================================================
 */

using Azure.Messaging.ServiceBus.Administration;

// ---------------------------------------------------------------------------
// 1) Show where configuration came from. Config.cs walks up from CWD looking
//    for a .env file; if none is found it falls back to process env vars.
// ---------------------------------------------------------------------------
Console.WriteLine($".env file: {Config.DotEnvPath ?? "(none — using process env vars)"}");
Console.WriteLine($"Connection string length: {Config.ConnectionString.Length} chars (looks good).\n");

// ---------------------------------------------------------------------------
// 2) The Administration client is the *management* surface. It's used to
//    create/list/delete queues, topics, subscriptions, and rules. It does
//    NOT send or receive messages — that's what ServiceBusClient is for.
//
//    Listing entities is a great smoke test: it requires network access
//    AND a valid SAS key / RBAC role.
// ---------------------------------------------------------------------------
var admin = new ServiceBusAdministrationClient(Config.ConnectionString);

Console.WriteLine("Queues:");
await foreach (var q in admin.GetQueuesAsync())
{
    // RequiresSession + MaxDeliveryCount are the two settings we'll exercise
    // most in later scripts (07-sessions, 05-dead-letter).
    Console.WriteLine($"  - {q.Name}  (sessions={q.RequiresSession}, maxDeliveryCount={q.MaxDeliveryCount})");
}

Console.WriteLine("\nTopics:");
await foreach (var t in admin.GetTopicsAsync())
{
    Console.WriteLine($"  - {t.Name}");

    // Each topic can have many subscriptions; each subscription is its own
    // "view" of the topic's message stream filtered by its rules (see 08).
    await foreach (var s in admin.GetSubscriptionsAsync(t.Name))
        Console.WriteLine($"      • {s.SubscriptionName}");
}

Console.WriteLine("\nAll good. You can proceed to 01-queue-send-receive.cs");
