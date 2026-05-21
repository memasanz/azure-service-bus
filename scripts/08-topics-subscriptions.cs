#:package Azure.Messaging.ServiceBus@7.18.2
#:project ../src/SbDemo.Shared/SbDemo.Shared.csproj

/*
 * ============================================================================
 * 08 · Topics, Subscriptions & Filters — Pub/Sub
 * ============================================================================
 *
 * OBJECTIVE
 *   Move from point-to-point queues to fan-out pub/sub: one send, many
 *   independent receivers, with each receiver filtering for the messages
 *   it cares about.
 *
 * MENTAL MODEL
 *   Topic               = "publish" endpoint. From the sender's perspective
 *                         it looks exactly like a queue.
 *   Subscription        = a *named* receive endpoint attached to a topic.
 *                         Each subscription gets its OWN copy of every
 *                         message that matches its filter rules.
 *   Rule                = a (filter, optional action) pair. Subscriptions
 *                         start with one default "TrueFilter" rule
 *                         (matches everything). You can replace or add rules.
 *
 *   So if a topic has 3 subscriptions and you send 1 message:
 *     • Each subscription that has a matching rule gets its own copy.
 *     • Each copy lives in its own sub-queue and is independently
 *       received, completed, dead-lettered, etc.
 *
 * OUR DEPLOYED LAYOUT (from Bicep)
 *   topic   = demo-topic
 *     ├── subscription "all"            (default TrueFilter — gets everything)
 *     ├── subscription "high-priority"  (SqlFilter: priority = 'high')
 *     └── subscription "orders"         (CorrelationFilter: label = 'order')
 *
 * THREE FILTER TYPES
 *   SqlFilter
 *     • SQL-92-like expression evaluated against system + app properties.
 *     • Powerful but slower than CorrelationFilter.
 *     • e.g. "priority = 'high' AND region = 'us-east'"
 *
 *   CorrelationFilter
 *     • Exact-match on a small set of system fields (Label/Subject,
 *       MessageId, CorrelationId, To, ReplyTo, SessionId) and/or app props.
 *     • Faster — prefer this when you can.
 *
 *   TrueFilter / FalseFilter
 *     • Catch-all / drop-all. Mostly used as the default rule.
 *
 * MULTIPLE RULES PER SUBSCRIPTION
 *   A subscription matches if ANY of its rules matches (logical OR).
 *   The default "$Default" rule is TrueFilter — if you add a more specific
 *   rule, you typically also delete $Default first.
 *
 * RUN
 *   dotnet run scripts/08-topics-subscriptions.cs
 * ============================================================================
 */

using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

await using var client = new ServiceBusClient(Config.ConnectionString);
await using var sender = client.CreateSender(Config.TopicName);   // <-- a topic, not a queue


// ---------------------------------------------------------------------------
// 1) Send 4 messages with varied subjects and app properties
//
//    Predict where each lands BEFORE looking at the output:
//      "low-priority log"  →  matches "all"                          (1 copy)
//      "urgent alert"      →  matches "all" + "high-priority"        (2 copies)
//      "order #42"         →  matches "all" + "orders"               (2 copies)
//      "order #43"         →  matches "all" + "high-priority" + "orders" (3 copies)
// ---------------------------------------------------------------------------
await sender.SendMessagesAsync(new[]
{
    new ServiceBusMessage("low-priority log")  { ApplicationProperties = { ["priority"] = "low"  } },
    new ServiceBusMessage("urgent alert")      { ApplicationProperties = { ["priority"] = "high" } },
    new ServiceBusMessage("order #42") { Subject = "order", ApplicationProperties = { ["priority"] = "normal" } },
    new ServiceBusMessage("order #43") { Subject = "order", ApplicationProperties = { ["priority"] = "high"   } },
});
Console.WriteLine("Sent 4 messages to the topic.\n");


// ---------------------------------------------------------------------------
// 2) Drain each subscription so we can see what landed where
// ---------------------------------------------------------------------------
async Task DrainAsync(string subscription)
{
    await using var receiver = client.CreateReceiver(Config.TopicName, subscription, new ServiceBusReceiverOptions
    {
        ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete   // simplify the demo
    });

    Console.WriteLine($"--- subscription: {subscription} ---");
    while (true)
    {
        var m = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(2));
        if (m is null) break;
        var pri = m.ApplicationProperties.TryGetValue("priority", out var v) ? v : "?";
        Console.WriteLine($"  body=\"{m.Body}\"  subject={m.Subject ?? "-"}  priority={pri}");
    }
    Console.WriteLine();
}

await DrainAsync("all");
await DrainAsync("high-priority");
await DrainAsync("orders");


// ---------------------------------------------------------------------------
// 3) Add a new rule to an existing subscription at runtime
//
//    Rules are CRUDed via the Administration client. We delete first to
//    make this script idempotent (rules must have unique names per
//    subscription).
// ---------------------------------------------------------------------------
var admin = new ServiceBusAdministrationClient(Config.ConnectionString);

try { await admin.DeleteRuleAsync(Config.TopicName, "all", "us-east-only"); }
catch (Azure.Messaging.ServiceBus.ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound) { }

await admin.CreateRuleAsync(Config.TopicName, "all",
    new CreateRuleOptions("us-east-only", new SqlRuleFilter("region = 'us-east'")));

Console.WriteLine("Added SqlRuleFilter 'us-east-only' (region = 'us-east') to subscription 'all'.");
Console.WriteLine("Rules now on 'all':");
await foreach (var r in admin.GetRulesAsync(Config.TopicName, "all"))
    Console.WriteLine($"  {r.Name,-20}  filter={r.Filter}");


// ---------------------------------------------------------------------------
// 4) Common gotcha
//
//    If you add a custom rule but leave the default "$Default" (TrueFilter)
//    in place, the subscription will match EVERYTHING — TrueFilter ORs with
//    your new rule. Usually you want to delete $Default first:
//
//        await admin.DeleteRuleAsync(topic, "high-priority", "$Default");
//        await admin.CreateRuleAsync(topic, "high-priority",
//            new CreateRuleOptions("only-high",
//                new SqlRuleFilter("priority = 'high'")));
//
//    Our Bicep template did exactly this for "high-priority" and "orders".
// ---------------------------------------------------------------------------

Console.WriteLine("\nDone. Next: 09-transactions.cs");
