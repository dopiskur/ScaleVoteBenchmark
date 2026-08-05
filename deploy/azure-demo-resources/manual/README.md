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
| 07 | `{prefix}-Logs` | Azure Workbook: CPU/memory/disk, current size/SKU, and instance counts for all four scaling scenarios |

Both the VM and the VMSS install and configure the ScaleTrigger app automatically via
cloud-init: .NET 10, the app itself, a systemd service, and Nginx as a reverse proxy on
ports 80 and 443 (self-signed certificate). The App Service deploys the app directly
from the public GitHub repository.

## Monitoring dashboard (module 07)

Deploys an Azure Workbook (`{prefix} Scaling Dashboard`, in the Azure Portal under
Monitor → Workbooks) with one section per scaling scenario. Every scenario that can
scale vertically gets a live "current size/SKU" tile, and every scenario that can also
scale horizontally additionally gets a live instance-count tile:

| Scenario | Vertical (current size/SKU) | Horizontal (instance count) | CPU | Memory | Disk |
|---|---|---|---|---|---|
| VM | ✓ (`hardwareProfile.vmSize`) | n/a — single VM only | ✓ (+ threshold line) | ✓ | ✓ (read/write bytes) |
| VM Scale Set | ✓ (`sku.name`) | ✓ (`sku.capacity`) | ✓ average **and** per-instance (+ threshold lines) | ✓ average **and** per-instance | ✓ (aggregate) |
| App Service | ✓ (`sku.name`) | ✓ (`numberOfWorkers`) | ✓ (+ threshold lines) | ✓ | approximated via disk queue length |
| Azure SQL Database | ✓ (configured vCore min/max) | n/a — single serverless database, scales vCores, not instances | ✓ | — (see note below) | ✓ (storage %) |

Network is intentionally not shown — the size/SKU and instance-count tiles are the
signal that actually matters for a scaling demo (did it scale, and to what), and
Azure SQL Database doesn't expose a network metric in the first place.

Each scaling scenario that has automation behind it (everything except SQL, which
scales itself) also gets an event-history table, so you can see *when* and *why* a
scaling decision fired, not just infer it from a tile changing value on refresh:

| Scenario | Event-history tile | Source |
|---|---|---|
| VM | "Resize Logic App - recent runs" | Logic App run log (`WorkflowRuntime` diagnostic category) |
| VM Scale Set | "Autoscale - recent scale actions" | Autoscale diagnostic log (`AutoscaleScaleActions` category) |
| App Service | "Autoscale - recent scale actions" **and** "Approval-gated resize Logic App - recent runs" | Same two sources, since App Service has both an autoscale rule and the approval-gated vertical Logic App |

How it's wired:

- CPU/disk for every resource, and CPU for the App Service and SQL Database, come from
  the platform metrics that modules 02–05 already forward to the shared Log Analytics
  workspace (`AllMetrics` diagnostic setting on every resource) — no extra agent
  needed, since Azure exposes those at the host/PaaS level.
- Guest-level memory (and per-instance CPU) for the VM and VMSS is **not** available at
  the platform level, so modules 02 and 03 also install the Azure Monitor Agent and a
  Data Collection Rule that collects `\Processor\PercentProcessorTime` and
  `\Memory\% Used Memory` into the same workspace.
- All "current size/SKU" and instance-count tiles run a live Azure Resource Graph query
  against the resource's own properties (`hardwareProfile.vmSize`, `sku.name`,
  `sku.capacity`, `properties.numberOfWorkers`, `properties.minCapacity`) rather than a
  metric, so they reflect the current state essentially immediately — this is what lets
  you watch a VM/VMSS/App Service actually change tier or instance count as it happens,
  rather than waiting on metric latency.
- Azure SQL Database Serverless has no metric for "vCores in use right now" — only
  `cpu_percent`, which is relative to the configured maximum. The vCore tile instead
  shows the configured min/max envelope the database can scale within (`sku.capacity`
  / `properties.minCapacity`), which is why SQL has no separate memory tile.
- The CPU charts for the VM, VMSS, and App Service now plot the actual autoscale/alert
  threshold(s) as extra series alongside the real CPU line (a KQL `extend` adding
  constant columns), so a scale event's cause is visible on the same chart instead of
  needing to be looked up separately.
- The event-history tables read from two new diagnostic-setting sources that module 06
  now also creates: `AutoscaleEvaluations`/`AutoscaleScaleActions` logs on each
  `Microsoft.Insights/autoscalesettings` resource (modules 03 and 04), and
  `WorkflowRuntime` logs on both Logic Apps (module 06) — none of these were being
  captured before, so this is new telemetry, not just a new view on existing data.
  Module 06 now also takes a `logAnalyticsResourceGroupPrefix` param to resolve the
  workspace, wired by `Deploy.ps1` from `-ResourceGroupPrefix` exactly like modules 02–05
  already are.

**Refresh delay — be aware of it before relying on this for a live demo:** Azure Monitor
metrics land roughly once a minute, and each chart defaults to a 1-hour window (each has
its own time-range picker to widen or narrow it). The workbook does not auto-refresh by
itself — use the toolbar's **Auto refresh** control (top-right) and pick 1–5 minutes;
there is no sub-minute "live" tier, so a metric chart will always lag the actual event by
up to roughly a minute. The Resource Graph-based size/instance-count tiles are much
closer to instant (seconds, not a metric's ~1-minute grain) but still only update when
the workbook itself refreshes — same Auto refresh control.

First-deploy checklist — this workbook is hand-authored JSON, not built through the
Portal UI, so verify these once after the first deploy and adjust the affected KQL
queries in `scripts/modules/dashboard.bicep` if needed:
- Open the `InsightsMetrics` table in Log Analytics and confirm the `Namespace`/`Name`
  values for the two custom counters match what the CPU/memory charts filter on
  (`Namespace == "Processor"`/`"Memory"`, `Name == "PercentProcessorTime"`/`"% Used
  Memory"`).
- Trigger a scale-out on the VMSS or App Service and confirm a row shows up in the
  `AutoscaleScaleActionsLog` table (used by the "recent scale actions" tiles) — this is
  the resource-specific table Azure documents for this diagnostic category, but it's
  new telemetry this deploy just started sending, so worth a first check.
- Trigger the VM or App Service resize Logic App and confirm a row shows up in
  `AzureDiagnostics` with `Category == "WorkflowRuntime"` for that resource (used by the
  "recent runs" tiles).

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
