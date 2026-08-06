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
config.json.example
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

Running `.\Deploy.ps1` with no arguments prints a short usage summary (one real example
plus a one-line-per-parameter reference) instead of parameters and defaults - the full
`Get-Help .\Deploy.ps1 -Full` reference is still available on request.

### config.json

Copy `config.json.example` to `config.json` (same folder as `Deploy.ps1`) and fill in
your usual values to skip retyping them every run - `config.json` is gitignored, so it's
safe to leave your real prefixes, subscription, etc. in it locally. It never holds the
admin password: when `.\Deploy.ps1` is run with no arguments and finds a `config.json`,
it asks `Load config.json and proceed?`; answering yes loads every other parameter from
the file and then prompts for the password separately, same as always. Answering no (or
having no `config.json` at all) falls back to the usage summary above.

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
port 443 (self-signed certificate), with port 80 redirecting to it. The App Service
deploys the app directly from the public GitHub repository.

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
  are started. Pass `-AutoShutdownEnabled $false` to skip creating module 06's daily
  VMSS-stop schedule if you want the Scale Set to keep running continuously (module 02's
  own VM shutdown schedule is separate and unaffected).
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

## Estimated cost

Pay-as-you-go, East US, retail prices (no reserved instances/savings plans, no free
subscription credit), all default parameter values (`baseCapacity = 1`, default SKUs).
This is the **baseline while everything is actively deployed and in use** — the biggest
lever you have to reduce it is simply not leaving resources up when you're not
demoing (see "Tearing down" below), or deploying only the modules for the scenario
you're actually showing (`-Mode Single`).

| Resource | Daily | Monthly | Notes |
|---|---:|---:|---|
| VM (02) — `Standard_B1s` + 30GB disk + Standard public IP | $0.43 | $12.78 | Roughly doubles while resized to `Standard_B1ms` during the vertical-scaling demo; that's a short-lived spike, not a sustained cost. |
| VM Scale Set (03) — 1× `Standard_B1s` + disk + Standard Load Balancer + public IP | $1.03 | $31.03 | Scales close to linearly up to 4 instances under load — the Load Balancer and public IP are flat regardless of instance count, only compute+disk multiply. |
| App Service Plan (04) — `P0v3` | $1.89 | $56.58 | PaaS — bills 24/7 regardless of traffic, can't be deallocated like a VM. Also scales toward 4× under load (and briefly to `P1v3` for the approval-gated scenario). |
| Azure SQL Database (05) — Serverless GP Gen5, 0.5–4 vCore | $6.37 | $191.02 | **Largest line item, and only correct if the database is queried 24/7.** By default (`autoPauseDelay` = 1440 min = 24h), it **auto-pauses itself** after 24h with no activity — while paused, cost drops to storage only (well under $1/month). This row is the ceiling, not the typical case. |
| Log Analytics workspace (01) | $0.69 | $20.70 | Estimated ingestion (~0.3 GB/day) from `AllMetrics` on every resource, the two Autoscale/Logic App diagnostic logs, and the Azure Monitor Agent counters — scales up with instance count and how much load-testing you actually run. |
| Automation Account + Logic Apps + metric alerts (06) | $0.00 | $0.00 | Free-tier Automation SKU and low execution volume keep this inside the free monthly allowances. |
| Azure Workbook dashboard (07) | $0.00 | $0.00 | The Workbook resource itself is free; it only reads the Log Analytics data already counted above. |
| **Total** | **~$10.40** | **~$312** | |

A few things worth knowing before treating this as a budget:
- SQL Serverless dominates the total specifically because the $191.02 figure assumes the
  database runs 24/7 with no idle gap over 24h. It doesn't have to: by default it
  auto-pauses itself automatically once nothing has queried it for `autoPauseDelayMinutes`
  (1440 = 24h out of the box). Letting it auto-pause between demo sessions (or lowering
  `autoPauseDelayMinutes` so it pauses sooner) is the single biggest cost lever here.
- If everything is left running continuously at **maximum** scale (VMSS and App Service
  both at 4 instances, VM parked at `B1ms`), the realistic ceiling is roughly
  **1.5–1.7× this total** (~$500/month) — but autoscale only holds that ceiling while
  load is actually being generated, not by default.
- Prices retrieved from the [Azure Retail Prices API](https://prices.azure.com/api/retail/prices)
  on 2026-08-05; Azure pricing changes over time and by negotiated agreement, so treat
  this as a planning estimate, not an invoice — re-check with the
  [Azure Pricing Calculator](https://azure.microsoft.com/en-us/pricing/calculator/) before
  relying on it for a real budget.

## Tearing down

Either run the Automation runbook (`teardown-runbook.ps1`, uploaded and published by
`Deploy.ps1` as part of module 06) from the Azure Portal, or run the bash script locally:

```bash
./scripts/07-teardown.sh --prefix ScaleTriggerDemo
./scripts/07-teardown.sh --prefix ScaleTriggerDemo --force --no-wait
```

This permanently deletes all six resource groups and everything inside them.
