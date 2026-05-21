# Azure Service Bus — C# Walkthrough

A progressive set of **.NET 10 file-based C# scripts** that teach the [`Azure.Messaging.ServiceBus`](https://learn.microsoft.com/dotnet/api/overview/azure/messaging.servicebus-readme) SDK from the basics up through advanced features.

Each script is a single `.cs` file you run with `dotnet run`. NuGet packages are declared inline via `#:package` directives — no project file needed per script.

> **Why not Polyglot Notebooks?** Microsoft deprecated the Polyglot Notebooks VS Code extension and the underlying .NET Interactive runtime in early 2026. Their replacement guidance for C# is exactly this: file-based apps in .NET 10.

## Prerequisites

- **[.NET SDK 10.0+](https://dotnet.microsoft.com/download)** — required for the file-based-app feature (`dotnet run file.cs`)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) — logged in via `az login`
- An Azure subscription
- VS Code with the C# Dev Kit (optional but nice for IntelliSense on `.cs` files)

Check your SDK:

```powershell
dotnet --list-sdks
```

You need a `10.x.x` entry.

## 1. Deploy the Service Bus infrastructure

The `infra/` folder contains a Bicep template that provisions:

- **Service Bus namespace** (Standard tier)
- Queues: `demo-queue`, `demo-sessions` (session-enabled), `demo-dlq`
- Topic: `demo-topic` with subscriptions `all`, `high-priority` (SQL filter), `orders` (correlation filter)

```powershell
cd infra
./deploy.ps1 -ResourceGroup rg-sbdemo -Location eastus -NamespaceName sbdemo$((Get-Random))
```

When the deployment finishes, `deploy.ps1` writes a `.env` file at the repo root with the connection string and namespace hostname. The scripts read it automatically.

If you'd rather deploy manually, copy `.env.example` to `.env` and fill in the values yourself.

## 2. Run the scripts

From the repo root:

```powershell
dotnet run scripts/00-setup.cs
dotnet run scripts/01-queue-send-receive.cs
# ... and so on
```

The first run on a fresh machine will restore NuGet packages — give it a minute.

| # | Script | Concept |
|---|--------|---------|
| 00 | `00-setup.cs` | Verify `.env`, list namespace entities |
| 01 | `01-queue-send-receive.cs` | Send / receive, PeekLock vs ReceiveAndDelete |
| 02 | `02-message-properties.cs` | System + application properties |
| 03 | `03-batching.cs` | `ServiceBusMessageBatch`, size limits |
| 04 | `04-processor.cs` | `ServiceBusProcessor`, concurrency, error handler |
| 05 | `05-dead-letter.cs` | DLQ semantics, resubmit |
| 06 | `06-scheduled-deferred.cs` | Scheduled & deferred messages |
| 07 | `07-sessions.cs` | Session-enabled queues, FIFO per session |
| 08 | `08-topics-subscriptions.cs` | Pub/sub, SQL & correlation filters |
| 09 | `09-transactions.cs` | Atomic send + complete via `TransactionScope` |
| 10 | `10-managed-identity.cs` | `DefaultAzureCredential`, RBAC |

Each script begins with a block comment explaining what it demonstrates; read the source as you go — the narrative lives with the code.

## 3. How the scripts share configuration

Every script declares two directives at the top:

```csharp
#:package Azure.Messaging.ServiceBus@7.18.2
#:project ../src/SbDemo.Shared/SbDemo.Shared.csproj
```

- `#:package` pulls in NuGet packages.
- `#:project` references the **`SbDemo.Shared`** class library, which exposes:
  - `Config.ConnectionString` / `Config.FullyQualifiedNamespace` — read from `.env` or process env vars
  - `Config.QueueName`, `Config.TopicName`, etc. — constants for the entities the Bicep deploys
  - `Config.DotEnvPath` — which `.env` file (if any) was loaded

`.env` parsing walks up from the current directory, so you can run scripts from anywhere in the repo.

## 4. Clean up

```powershell
az group delete -n rg-sbdemo --yes --no-wait
```

## Repo layout

```
servicebus-demo/
├── .env                  # created by deploy.ps1 (gitignored)
├── .env.example
├── infra/
│   ├── main.bicep
│   ├── main.parameters.json
│   └── deploy.ps1
├── scripts/              # .NET 10 file-based C# scripts (run with `dotnet run`)
│   ├── 00-setup.cs
│   └── 01-...10-...cs
└── src/
    └── SbDemo.Shared/    # tiny class library shared by all scripts
        ├── Config.cs
        └── SbDemo.Shared.csproj
```
