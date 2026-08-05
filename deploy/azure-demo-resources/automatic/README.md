# ScaleTrigger Azure Scaling Demo — one-click deploy

The same demo environment as [`../manual`](../manual/README.md) — five Azure scaling
scenarios (VM, VM Scale Set, App Service, Azure SQL Serverless) plus a live monitoring
dashboard, each running [ScaleTrigger](https://github.com/dopiskur/scaleTrigger) — but
as a **single Bicep template**, deployable with one click and no PowerShell.

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fdopiskur%2FscaleTrigger%2Fmaster%2Fdeploy%2Fazure-demo-resources%2Fautomatic%2Fmain.json)

Click it, fill in a password (the only required field), and deploy. Everything else —
resource groups, the VM/VMSS/App Service/SQL scenarios, the monitoring dashboard, and
the Automation Account with its teardown/shutdown runbooks — finishes inside that one
deployment. No script to run afterward.

## How this differs from `manual/`

`manual/` and `automatic/` provision **the same resources with the same defaults** —
same six resource groups, same scaling scenarios, same dashboard, same estimated cost
(see below). The difference is entirely in how the deployment itself is orchestrated:

| | `manual/` | `automatic/` (this folder) |
|---|---|---|
| Entry point | `Deploy.ps1` (PowerShell) | One Bicep template, one click |
| Templates | 7 separate deployments, ordered by the script | 1 deployment, dependency order inferred by Bicep |
| Runbook content | Uploaded + published by `Deploy.ps1` after the fact (`Import-`/`Publish-AzAutomationRunbook`) | Published natively at deploy time (`publishContentLink` pointing at this repo's raw runbook scripts) |
| Daily VMSS shutdown schedule | Registered by `Deploy.ps1` (`New-AzAutomationSchedule` + `Register-AzAutomationScheduledRunbook`), in your local time zone | Native ARM resources (`.../schedules` + `.../jobSchedules`), computed in **UTC** — see note below |
| SQL connectivity check | `Deploy.ps1` waits (up to 10 min) for the freshly created SQL Server to accept a connection, adding a firewall rule for your own IP first | Not needed — nobody's local machine needs DB access for a portal-driven deploy, and the VM/VMSS/App Service already retry their own DB connection independently |
| Az PowerShell / Bicep CLI install | `Deploy.ps1` installs both automatically if missing | Not needed — the portal (or `az deployment`) handles this |

Pick `manual/` if you want more control (deploy one scenario at a time, tune every
parameter, re-run pieces independently) or you're scripting this into something else.
Pick `automatic/` (here) for the fastest path to "everything is up."

**UTC shutdown time:** the daily VMSS auto-shutdown schedule fires at `autoShutdownHour`
UTC, not converted to a named local time zone — `manual/`'s PowerShell-driven schedule
does that conversion via `-TimeZone`, but doing it declaratively in Bicep would need a
timezone/DST-aware function Bicep doesn't have. Adjust `autoShutdownHour` if you want it
to line up with a specific local morning.

## Deploying by hand instead of the button

```bash
az deployment sub create \
  --location eastus \
  --template-file main.bicep \
  --parameters adminPassword='<a-strong-password>'
```

## Parameters

Same defaults as `manual/`'s `Deploy.ps1`, just camelCase (Bicep convention) instead of
PowerShell's PascalCase, and no `-Mode`/`-Module` (this template always deploys
everything — see `manual/` for deploying one scenario at a time).

| Parameter | Default | Notes |
|---|---|---|
| `adminPassword` | *(required)* | The only required parameter. Must meet Azure's password complexity rules. |
| `adminUsername` | `demoadmin` | Admin login for the VM, VMSS, and SQL Server. |
| `resourceGroupPrefix` | `ScaleTriggerDemo` | Applied to all resource group names, e.g. `ScaleTriggerDemo-SingleVM`. |
| `resourcePrefix` | `ScaleTrigger` | Applied to resource names inside those groups, e.g. `ScaleTrigger-vm`. |
| `location` | `eastus` | Any Azure region (or the region picked in the portal's Basics tab). |
| `approvalNotificationUpn` | `dummy@somemail.com` | Azure AD account that receives the push notification for the approval-gated scaling scenario. **Replace this**, or that scenario notifies no one. |
| `autoShutdownHour` | `5` | UTC hour (0–23) the VM/VMSS auto-shut down. |
| `autoShutdownEnabled` | `true` | Whether to create the daily VMSS shutdown schedule at all. |

Two resource names are made globally unique automatically (the SQL Server and the Web
App), same as `manual/`.

## Structure

```
main.bicep       - entry point (subscription scope), what the button deploys
main.json        - compiled from main.bicep, what the button URL actually points at
modules/         - same modules as manual/scripts/modules, except automation.bicep
                   (rewritten to publish runbooks / register the schedule natively)
scripts/         - the same two runbook scripts as manual/, published from here by URL
README.md        - this file
```

### Keeping main.json in sync

[`.github/workflows/build-bicep.yml`](../../../.github/workflows/build-bicep.yml) runs
`az bicep build` against `main.bicep` on every push that touches it (or any module under
`modules/`) and commits the regenerated `main.json` back to the branch automatically —
you never need to run `az bicep build` by hand after editing the template, though
`az bicep build --file main.bicep` locally is the fastest way to check an edit before
pushing.

## What gets deployed

| Resource group | Scenario |
|---|---|
| `{prefix}-Logs` | Shared Log Analytics workspace + the monitoring dashboard (Azure Workbook) |
| `{prefix}-SingleVM` | Single VM, vertical scaling (B1s → B1ms) via Azure Monitor + Logic App |
| `{prefix}-ScaleSet` | VM Scale Set behind a Standard Load Balancer, horizontal scaling via native Autoscale |
| `{prefix}-ServicePlan` | App Service, horizontal scaling via native Autoscale, plus vertical scaling (P0v3 → P1v3) gated behind a manual approval |
| `{prefix}-Database` | Azure SQL Database, Serverless tier, vertical scaling built into the platform |
| `{prefix}-Automation` | Logic Apps, alerts, and an Automation Account with teardown/shutdown runbooks |

Both the VM and the VMSS install and configure the ScaleTrigger app automatically via
cloud-init: .NET 10, the app itself, a systemd service, and Nginx as a reverse proxy on
ports 80 and 443 (self-signed certificate). The App Service deploys the app directly
from the public GitHub repository.

## Monitoring dashboard

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
  the platform metrics every resource already forwards to the shared Log Analytics
  workspace (`AllMetrics` diagnostic setting) — no extra agent needed, since Azure
  exposes those at the host/PaaS level.
- Guest-level memory (and per-instance CPU) for the VM and VMSS is **not** available at
  the platform level, so both also get the Azure Monitor Agent and a Data Collection
  Rule that collects `\Processor\PercentProcessorTime` and `\Memory\% Used Memory` into
  the same workspace.
- All "current size/SKU" and instance-count tiles run a live Azure Resource Graph query
  against the resource's own properties (`hardwareProfile.vmSize`, `sku.name`,
  `sku.capacity`, `properties.numberOfWorkers`, `properties.minCapacity`) rather than a
  metric, so they reflect the current state essentially immediately.
- Azure SQL Database Serverless has no metric for "vCores in use right now" — only
  `cpu_percent`, relative to the configured maximum. The vCore tile instead shows the
  configured min/max envelope the database can scale within, which is why SQL has no
  separate memory tile.
- The CPU charts for the VM, VMSS, and App Service plot the actual autoscale/alert
  threshold(s) as extra series alongside the real CPU line, so a scale event's cause is
  visible on the same chart.

**Refresh delay — be aware of it before relying on this for a live demo:** Azure Monitor
metrics land roughly once a minute, and each chart defaults to a 1-hour window (each has
its own time-range picker). The workbook does not auto-refresh by itself — use the
toolbar's **Auto refresh** control (top-right) and pick 1–5 minutes; there is no
sub-minute "live" tier. The Resource Graph-based size/instance-count tiles are much
closer to instant but still only update when the workbook itself refreshes.

## First-deploy checklist

Two categories of thing worth verifying once, since neither can be fully checked
without a live subscription:

**Same as `manual/`** (this workbook is hand-authored JSON, not built through the
Portal UI):
- Open the `InsightsMetrics` table in Log Analytics and confirm the `Namespace`/`Name`
  values for the two custom counters match what the CPU/memory charts filter on
  (`Namespace == "Processor"`/`"Memory"`, `Name == "PercentProcessorTime"`/`"% Used
  Memory"`).
- Trigger a scale-out on the VMSS or App Service and confirm a row shows up in the
  `AutoscaleScaleActionsLog` table.
- Trigger the VM or App Service resize Logic App and confirm a row shows up in
  `AzureDiagnostics` with `Category == "WorkflowRuntime"` for that resource.

**Specific to this ARM-native automation path** (the one part of `automatic/` with no
`manual/` equivalent to have already exercised):
- Confirm both runbooks (`Remove-DemoResources`, `Stop-ScaleSetInstances`) show as
  **Published**, not just created, in the Automation Account — `publishContentLink`
  should handle this automatically at deploy time, but it's worth a first check.
- Confirm the `daily-vmss-shutdown` schedule's `jobSchedules` link actually triggers the
  `Stop-ScaleSetInstances` runbook with the right `VmssResourceGroup`/`VmssName`
  parameters at its first scheduled run.

## Estimated cost

Same resources, same defaults as `manual/` — see
[`../manual/README.md#estimated-cost`](../manual/README.md#estimated-cost) for the full
breakdown and caveats. Summary:

| Resource | Daily | Monthly | Notes |
|---|---:|---:|---|
| VM — `Standard_B1s` + 30GB disk + Standard public IP | $0.43 | $12.78 | Roughly doubles while resized to `Standard_B1ms` during the vertical-scaling demo. |
| VM Scale Set — 1× `Standard_B1s` + disk + Standard Load Balancer + public IP | $1.03 | $31.03 | Scales close to linearly up to 4 instances under load. |
| App Service Plan — `P0v3` | $1.89 | $56.58 | PaaS — bills 24/7 regardless of traffic. |
| Azure SQL Database — Serverless GP Gen5, 0.5–4 vCore | $6.37 | $191.02 | **Largest line item** — assumes the database stays active (24h auto-pause by default). |
| Log Analytics workspace | $0.69 | $20.70 | Estimated ingestion; scales with instance count and load-testing intensity. |
| Automation Account + Logic Apps + metric alerts | $0.00 | $0.00 | Within free monthly allowances at this usage level. |
| Azure Workbook dashboard | $0.00 | $0.00 | Free; reads the Log Analytics data already counted above. |
| **Total** | **~$10.40** | **~$312** | |

Prices retrieved from the [Azure Retail Prices API](https://prices.azure.com/api/retail/prices)
on 2026-08-05 for East US, pay-as-you-go — re-check with the
[Azure Pricing Calculator](https://azure.microsoft.com/en-us/pricing/calculator/) before
treating this as a real budget.

## Tearing down

Run the `Remove-DemoResources` Automation runbook from the Azure Portal (Automation
Account → Runbooks), or delete the six resource groups yourself (`{prefix}-Logs`,
`{prefix}-SingleVM`, `{prefix}-ScaleSet`, `{prefix}-ServicePlan`, `{prefix}-Database`,
`{prefix}-Automation`). `manual/`'s [`scripts/07-teardown.sh`](../manual/scripts/07-teardown.sh)
also works here unchanged if you have the Azure CLI handy — it only takes a
`--prefix`, and this template uses the same resource group naming convention.
