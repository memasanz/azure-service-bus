#:package Azure.Messaging.ServiceBus@7.18.2
#:package Azure.Identity@1.13.1
#:project ../src/SbDemo.Shared/SbDemo.Shared.csproj

/*
 * ============================================================================
 * 10 · DefaultAzureCredential & Managed Identity — No More Connection Strings
 * ============================================================================
 *
 * OBJECTIVE
 *   Replace the SAS connection string with an Azure AD / Entra credential.
 *   This is what you should be doing in production — connection strings
 *   are long-lived secrets that you have to rotate and protect.
 *
 * WHY ENTRA INSTEAD OF SAS?
 *   • No secrets to leak or rotate — credentials are short-lived tokens
 *     issued by Azure AD, refreshed automatically by the SDK.
 *   • Fine-grained RBAC: assign Data Sender to a sender, Data Receiver to
 *     a worker, Data Owner only to operators.
 *   • Auditable: every operation has a real identity behind it, not just
 *     "RootManageSharedAccessKey was used".
 *   • Works seamlessly with Managed Identity for Azure-hosted code.
 *
 * RBAC ROLES
 *   Azure Service Bus Data Sender    — can send
 *   Azure Service Bus Data Receiver  — can receive + complete
 *   Azure Service Bus Data Owner     — everything (incl. manage entities)
 *
 *   Assign at the scope you want: namespace / topic / queue / subscription.
 *
 * HOW DefaultAzureCredential WORKS
 *   It walks a CHAIN of credential sources, trying each one until it gets
 *   a token. The default chain (in order):
 *
 *     1. Environment variables (AZURE_CLIENT_ID/SECRET/TENANT_ID)
 *     2. Workload Identity Federation (for AKS / GitHub Actions)
 *     3. Managed Identity (for Azure App Service, VM, Container Apps...)
 *     4. Visual Studio / VS Code signed-in user
 *     5. Azure CLI (`az login`)        ← what you'll use locally
 *     6. Azure PowerShell (`Connect-AzAccount`)
 *     7. Interactive browser (last resort)
 *
 *   The SAME code therefore works locally (via `az login`) and in
 *   production (via Managed Identity) with no changes.
 *
 * PRE-REQS FOR THIS SCRIPT
 *   1. SERVICEBUS_NAMESPACE in .env (no connection string needed)
 *   2. Your signed-in identity has a Data Owner / Data Sender + Receiver
 *      role on the namespace.
 *
 *   If you passed -PrincipalId to deploy.ps1, the role is already assigned.
 *   Otherwise:
 *
 *     az role assignment create \
 *       --assignee $(az ad signed-in-user show --query id -o tsv) \
 *       --role "Azure Service Bus Data Owner" \
 *       --scope $(az servicebus namespace show -g rg-sbdemo -n <namespace> --query id -o tsv)
 *
 *   Note: role assignments can take 1–2 minutes to propagate.
 *
 * RUN
 *   dotnet run scripts/10-managed-identity.cs
 * ============================================================================
 */

using Azure.Identity;
using Azure.Messaging.ServiceBus;


// ---------------------------------------------------------------------------
// 1) Build a credential and a client
//
//    Note the DIFFERENT ServiceBusClient constructor: it takes a
//    "fully-qualified namespace" (e.g. sbdemo123.servicebus.windows.net),
//    NOT a connection string. The credential is what authenticates.
// ---------------------------------------------------------------------------
var credential = new DefaultAzureCredential();

await using var client = new ServiceBusClient(Config.FullyQualifiedNamespace, credential);
Console.WriteLine($"Connected to {client.FullyQualifiedNamespace} via Entra credential.\n");


// ---------------------------------------------------------------------------
// 2) From here, everything is identical to the connection-string examples
// ---------------------------------------------------------------------------
await using var sender = client.CreateSender(Config.QueueName);
await sender.SendMessageAsync(new ServiceBusMessage("hello from DefaultAzureCredential"));
Console.WriteLine("Sent via Entra-authenticated client.");

await using var receiver = client.CreateReceiver(Config.QueueName);
var msg = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));
Console.WriteLine($"Got: {msg?.Body}");
if (msg is not null) await receiver.CompleteMessageAsync(msg);


// ---------------------------------------------------------------------------
// 3) Troubleshooting cheat sheet
//
//   Symptom                          Likely cause
//   ───────────────────────────────  ────────────────────────────────────────
//   401 / Unauthorized               Missing RBAC role assignment, OR change
//                                    hasn't propagated yet (wait 1–2 min)
//   "DefaultAzureCredential failed   No credential in the chain succeeded.
//    to retrieve a token..."         Try `az login`, or set AZURE_CLIENT_ID
//                                    env vars, or check Managed Identity.
//   Works locally, fails in Azure    Managed Identity not enabled on the
//                                    host, or assigned at the wrong scope.
//   Works at namespace scope, not    Role assigned to a queue/topic but the
//   at entity scope                  client referenced a different entity.
//
//   To see which credential succeeded, wrap with a logged credential:
//
//       var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
//       {
//           Diagnostics = { IsLoggingEnabled = true, IsLoggingContentEnabled = true }
//       });
//
//   ...and turn on Azure SDK event-source logging.
// ---------------------------------------------------------------------------

Console.WriteLine();
Console.WriteLine("That's the full walkthrough. From here, look into:");
Console.WriteLine("  • ServiceBusModelFactory for unit-testing message handlers");
Console.WriteLine("  • Azure.Monitor.OpenTelemetry.AspNetCore for distributed tracing");
Console.WriteLine("  • Premium tier: geo-DR, 100 MB messages, VNet integration");
Console.WriteLine("  • Schema Registry for centralised payload contracts");
