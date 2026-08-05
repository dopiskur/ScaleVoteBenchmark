# ScaleTrigger Azure Scaling Demo

Seven independent Bicep templates that provision a small Azure environment demonstrating
five different Azure scaling mechanisms, each running the [ScaleTrigger](https://github.com/dopiskur/scaleTrigger)
load-simulation app: a VM (vertical scaling via Azure Monitor + Logic App), an Azure SQL
Database in the Serverless tier (vertical scaling built into the platform), a VM Scale
Set (horizontal scaling via native Autoscale), and an App Service plan (horizontal
scaling via native Autoscale, plus a second, approval-gated vertical scaling scenario),
plus a live monitoring dashboard covering all four.

## Prerequisites

- An Azure subscription
- PowerShell 5.1 or later
- Internet access from the machine running `Deploy.ps1` (it installs the Az PowerShell
  modules and the Bicep CLI automatically if they are missing)

## Structure

```
Deploy.ps1
README.md
scripts/
  01-log-analytics.bicep
  02-single-vm.bicep
  03-scale-set.bicep
  04-service-plan.bicep
  05-sql-database.bicep
  06-automation.bicep
  07-dashboard.bicep
  07-teardown.sh
  modules/
  teardown-runbook.ps1
  stop-vmss-runbook.ps1
```

`Deploy.ps1` is the only file meant to be run directly; everything it depends on (the
Bicep templates, their shared modules, the teardown script, and the two Automation
runbooks) lives under `scripts/` to keep that distinction obvious.

Each numbered template provisions its own resource group and everything inside it,
except 07, which deploys into 01's resource group (`{prefix}-Logs`) since the
dashboard is a view over that shared Log Analytics workspace, not a resource of its
own. They can be deployed independently, but 05 must run before 03 and 04 (both
connect to the SQL database on first boot), 06 must run after 02 and 04 (it references
their resources by name), and 07 should run last, once the resources it charts exist
(it will still deploy successfully before that, the affected charts just show no data
until their resource exists).

## Deploying

```powershell
.\Deploy.ps1 -AdminPassword (Read-Host -Prompt "Password" -AsSecureString)
```

Run without `-Mode` and you'll get an interactive menu (deploy everything, or one
template at a time). `AdminPassword` is the only required parameter; everything else has
a sensible default.

```powershell
.\Deploy.ps1 -Mode All -AdminPassword $securePassword
.\Deploy.ps1 -Mode Single -Module 04 -AdminPassword $securePassword
```

## Parameters

| Parameter | Default | Notes |
|---|---|---|
| `AdminUsername` | `demoadmin` | Admin login for the VM, VMSS, and SQL Server |
| `AdminPassword` | *(required)* | No default. Must meet Azure's complexity rules |
| `ResourceGroupPrefix` | `ScaleTriggerDemo` | Applied to all resource group names, e.g. `ScaleTriggerDemo-SingleVM` |
| `ResourcePrefix` | `ScaleTrigger` | Applied to resource names inside those groups, e.g. `ScaleTrigger-vm` |
| `SubscriptionId` | *(current context)* | Only switches context if explicitly provided |
| `Location` | `eastus` | Any Azure region |
| `ApprovalNotificationUpn` | `dummy@somemail.com` | Azure AD account that receives the push notification for the approval-gated scaling scenario. **Replace this** with a real account UPN, or that scenario will notify no one. |

Two resource names are made globally unique automatically (the SQL Server and the Web
App), since Azure requires this regardless of which subscription deploys them.

## What each script deploys

| # | Resource group | Scenario |
|---|---|---|
| 01 | `{prefix}-Logs` | Shared Log Analytics workspace |
| 02 | `{prefix}-SingleVM` | Single VM, vertical scaling (B1s → B1ms) via Azure Monitor + Logic App |
| 03 | `{prefix}-ScaleSet` | VM Scale Set behind a Standard Load Balancer, horizontal scaling via native Autoscale |
| 04 | `{prefix}-ServicePlan` | App Service, horizontal scaling via native Autoscale, plus vertical scaling (P0v3 → P1v3) gated behind a manual approval |
| 05 | `{prefix}-Database` | Azure SQL Database, Serverless tier, vertical scaling built into the platform |
| 06 | `{prefix}-Automation` | Logic Apps, alerts, and an Automation Account with teardown/shutdown runbooks |
| 07 | `{prefix}-Logs` | Azure Workbook: CPU/memory/disk/network and instance counts for all four scaling scenarios |

Both the VM and the VMSS install and configure the ScaleTrigger app automatically via
cloud-init: .NET 10, the app itself, a systemd service, and Nginx as a reverse proxy on
ports 80 and 443 (self-signed certificate). The App Service deploys the app directly
from the public GitHub repository.

## Monitoring dashboard (module 07)

Deploys an Azure Workbook (`{prefix} Scaling Dashboard`, in the Azure Portal under
Monitor → Workbooks) with one section per scaling scenario:

| Scenario | CPU | Memory | Disk | Network | Instance count |
|---|---|---|---|---|---|
| VM | ✓ | ✓ | ✓ (read/write bytes) | ✓ (in/out bytes) | n/a — single VM, vertical scaling only |
| VM Scale Set | ✓ average **and** per-instance | ✓ average **and** per-instance | ✓ (aggregate) | ✓ (aggregate) | ✓ (live, current `sku.capacity`) |
| App Service | ✓ | ✓ | approximated via disk queue length | ✓ (bytes sent/received) | ✓ (live, current worker count) |
| Azure SQL Database | ✓ | ✓ (`app_memory_percent`) | ✓ (storage %) | not exposed by the platform | n/a — single serverless database, scales vCores, not instances |

How it's wired:

- CPU/disk/network for every resource, and CPU/memory for the App Service and SQL
  Database, come from the platform metrics that modules 02–05 already forward to the
  shared Log Analytics workspace (`AllMetrics` diagnostic setting on every resource) —
  no extra agent needed, since Azure exposes those at the host/PaaS level.
- Guest-level memory (and per-instance CPU) for the VM and VMSS is **not** available at
  the platform level, so modules 02 and 03 now also install the Azure Monitor Agent and
  a Data Collection Rule that collects `\Processor\PercentProcessorTime` and
  `\Memory\% Used Memory` into the same workspace.
- Instance-count tiles (VMSS, App Service) run a live Azure Resource Graph query
  (`sku.capacity` / `properties.numberOfWorkers`) rather than a metric, so they reflect
  the current count essentially immediately, not on a metric's ~1-minute delay.
- Charts default to a 1-hour window (each has its own time-range picker). For a live
  view while watching a scenario run, use the workbook toolbar's **Auto refresh**
  control (top-right) — Azure Monitor metrics land roughly once a minute, so a 1–5
  minute refresh is a reasonable match; there is no sub-minute "live" tier.

First-deploy checklist — this workbook is hand-authored JSON, not built through the
Portal UI, so verify these once after the first deploy and adjust the two affected
KQL queries in `scripts/modules/dashboard.bicep` if needed:
- Open the `InsightsMetrics` table in Log Analytics and confirm the `Namespace`/`Name`
  values for the two custom counters match what the CPU/memory charts filter on
  (`Namespace == "Processor"`/`"Memory"`, `Name == "PercentProcessorTime"`/`"% Used
  Memory"`).
- Confirm `app_memory_percent` exists in the SQL Database's metric definitions for your
  subscription (serverless-tier vCore metric; drop the tile if it's missing).
- Confirm the Resource Graph query resolves `properties.numberOfWorkers` on the App
  Service Plan resource.

## Cost and reliability notes

- The VM and VMSS shut down automatically at 05:00 local time to limit cost when idle.
  Autoscale settings are untouched, so scaling still works correctly the next time they
  are started.
- The App Service plan has no automatic shutdown; PaaS plans cannot be deallocated the
  way a VM can, only deleted and recreated.
- `Deploy.ps1` retries automatically on two known transient conditions: a metric not yet
  reported on a freshly created resource, and Azure SQL Serverless taking a short time to
  become reachable after creation. All other errors are surfaced immediately.
- The admin password is only ever passed as a script parameter, never stored in the
  Bicep templates themselves.
- The Azure Monitor Agent on the VM/VMSS (added for the module 07 dashboard's memory
  charts) adds a small amount of Log Analytics ingestion volume — negligible for a demo
  workspace, but not zero.

## Tearing down

Either run the Automation runbook (`teardown-runbook.ps1`, uploaded and published by
`Deploy.ps1` as part of module 06) from the Azure Portal, or run the bash script locally:

```bash
./scripts/07-teardown.sh --prefix ScaleTriggerDemo
./scripts/07-teardown.sh --prefix ScaleTriggerDemo --force --no-wait
```

This permanently deletes all six resource groups and everything inside them.
