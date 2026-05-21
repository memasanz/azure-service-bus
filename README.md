# Azure Service Bus — C# Notebook Walkthrough

A progressive set of [Polyglot Notebooks](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.dotnet-interactive-vscode) (`.ipynb`, C# kernel) that teach the [`Azure.Messaging.ServiceBus`](https://learn.microsoft.com/dotnet/api/overview/azure/messaging.servicebus-readme) SDK from the basics up through advanced features.

Each notebook is self-contained: it restores NuGet packages, reads a connection string from an environment variable, and demonstrates one concept at a time.

## Prerequisites

- [.NET SDK 8.0+](https://dotnet.microsoft.com/download)
- [VS Code](https://code.visualstudio.com/) with the **Polyglot Notebooks** extension
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) (`az`) — logged in via `az login`
- An Azure subscription

## 1. Deploy the Service Bus infrastructure

The `infra/` folder contains a Bicep template that provisions everything the notebooks need:

- **Service Bus namespace** (Standard tier)
- Queues: `demo-queue`, `demo-sessions` (session-enabled), `demo-dlq`
- Topic: `demo-topic` with subscriptions `all`, `high-priority` (SQL filter), `orders` (correlation filter)

Deploy it with PowerShell:

```powershell
cd infra
./deploy.ps1 -ResourceGroup rg-sbdemo -Location eastus -NamespaceName sbdemo$((Get-Random))
```

Or directly with the Azure CLI:

```bash
az group create -n rg-sbdemo -l eastus
az deployment group create \
  -g rg-sbdemo \
  -f infra/main.bicep \
  -p namespaceName=sbdemo$RANDOM
```

The deployment outputs a **primary connection string**. Set it as an environment variable so the notebooks can pick it up:

```powershell
$env:SERVICEBUS_CONNECTION_STRING = "<paste connection string here>"
$env:SERVICEBUS_NAMESPACE         = "<namespace>.servicebus.windows.net"
```

(For notebook `10-managed-identity.ipynb` you only need `SERVICEBUS_NAMESPACE`.)

## 2. Open the notebooks

Open this folder in VS Code and run the notebooks in order:

| # | Notebook | Concept |
|---|----------|---------|
| 00 | `00-setup.ipynb` | Deploy infra, verify connection |
| 01 | `01-queue-send-receive.ipynb` | Send / receive, PeekLock vs ReceiveAndDelete |
| 02 | `02-message-properties.ipynb` | System & application properties |
| 03 | `03-batching.ipynb` | `ServiceBusMessageBatch`, size limits |
| 04 | `04-processor.ipynb` | `ServiceBusProcessor`, concurrency, error handler |
| 05 | `05-dead-letter.ipynb` | DLQ semantics, resubmit |
| 06 | `06-scheduled-deferred.ipynb` | Scheduled & deferred messages |
| 07 | `07-sessions.ipynb` | Session-enabled queues, FIFO per session |
| 08 | `08-topics-subscriptions.ipynb` | Pub/sub, SQL & correlation filters |
| 09 | `09-transactions.ipynb` | Atomic send + complete via `TransactionScope` |
| 10 | `10-managed-identity.ipynb` | `DefaultAzureCredential`, RBAC |

## 3. Clean up

```bash
az group delete -n rg-sbdemo --yes --no-wait
```

## Repo layout

```
servicebus-demo/
├── infra/                # Bicep + deploy script
├── notebooks/            # Polyglot notebooks (C#)
├── src/shared/           # Helpers shared across notebooks via #load
└── README.md
```
