# ScaleTrigger Azure Scaling Demo

Provisions a small Azure environment demonstrating five different Azure scaling
mechanisms, each running the [ScaleTrigger](https://github.com/dopiskur/scaleTrigger)
load-simulation app: a VM (vertical scaling via Azure Monitor + Logic App), an Azure SQL
Database in the Serverless tier (vertical scaling built into the platform), a VM Scale
Set (horizontal scaling via native Autoscale), and an App Service plan (horizontal
scaling via native Autoscale, plus a second, approval-gated vertical scaling scenario),
plus a live monitoring dashboard covering all five and a PowerShell script
([`Run-ScalingScenarios.ps1`](Run-ScalingScenarios.ps1), see below) that drives load
against each one and collects the timing data needed to write it up.

One subscription-scope Bicep template deploys everything - resource groups, all five
scenarios, the monitoring dashboard, and an Automation Account with teardown/shutdown
runbooks - in a single deployment. No script to run afterward.

**Budget 30-40 minutes.** That's dominated by two things the template can't skip: Log
Analytics deliberately waits ~10 minutes after enabling the VM Insights solution before
anything can reference the `InsightsMetrics` table it provisions (see "First-deploy
checklist and known issues" below), and Azure SQL Serverless provisioning alone
typically takes 5-10 minutes.

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
port 443 (self-signed certificate), with port 80 redirecting to it. The App Service
deploys the app directly from the public GitHub repository.

Two resource names are made globally unique automatically (the SQL Server and the Web
App), since Azure requires this regardless of how they're deployed.

---

## Deploying

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fdopiskur%2FscaleTrigger%2Fmaster%2Fdeploy%2Fazure-demo-resources%2Fmain.json)

Click it, fill in a password (the only required field), and deploy.

### Deploying by hand instead of the button

```bash
az deployment sub create \
  --location eastus \
  --template-file main.bicep \
  --parameters adminPassword='<a-strong-password>'
```

There's no `location` parameter in the template itself: every resource deploys to
`deployment().location`, i.e. whatever region is picked in the portal blade's own
"Region" selector (or `--location` above).

### Parameters

camelCase (Bicep convention).

| Parameter | Default | Notes |
|---|---|---|
| `adminPassword` | *(required)* | The only required parameter. Must meet Azure's password complexity rules. Avoid `;`, `"`, and backtick - it's interpolated unescaped into a SQL connection string and into command lines built by `Run-ScalingScenarios.ps1`. |
| `adminUsername` | `demoadmin` | Admin login for the VM, VMSS, and SQL Server. |
| `resourceGroupPrefix` | `ScaleTriggerDemo` | Applied to all resource group names, e.g. `ScaleTriggerDemo-SingleVM`. |
| `resourcePrefix` | `ScaleTrigger` | Applied to resource names inside those groups, e.g. `ScaleTrigger-vm`. |
| `approvalNotificationUpn` | `dummy@somemail.com` | Azure AD account that receives the push notification for the approval-gated scaling scenario. **Replace this**, or that scenario notifies no one. |
| `autoShutdownHour` | `5` | UTC hour (0-23) the VM/VMSS auto-shut down. |
| `autoShutdownEnabled` | `true` | Whether to create the daily VMSS shutdown schedule at all. |

### Structure

```
deploy/azure-demo-resources/
  main.bicep              - entry point (subscription scope), what the button deploys
  main.json               - compiled from main.bicep, what the button URL points at
  modules/                - one module per resource group above
  scripts/                - the two runbook scripts, published from here by URL
  Run-ScalingScenarios.ps1 - see "Measuring scaling scenarios" below
```

### Keeping main.json in sync

[`.github/workflows/build-bicep.yml`](../../.github/workflows/build-bicep.yml) runs
`az bicep build` against `main.bicep` on every push that touches it (or any module under
`modules/`) and commits the regenerated `main.json` back to the branch automatically -
you never need to run `az bicep build` by hand after editing the template, though
`az bicep build --file main.bicep` locally is the fastest way to check an edit before
pushing.

### Why there's no portal wizard

A nicer, multi-step deploy wizard (instead of the portal's default flat, alphabetical
parameter list) was attempted and abandoned after repeated portal-side failures. Both
the classic `createUiDefinition.json` format (Azure Managed Applications) and the newer
`uiFormDefinitionUri` Form view format (delivered via the portal URL parameter) were
tried against the generic "Deploy a custom template" blade for this *subscription-scope*
template; the Form view attempt got as far as the Review step before crashing
identically twice in a row - `getFormTemplateDeploymentOptions: Cannot read properties
of undefined (reading 'location')` in `Microsoft_Azure_CreateUIDef` - once with
standalone `SubscriptionSelector`/`LocationSelector` elements and again after switching
to the composite `Microsoft.Common.ResourceScope` control (the one pattern an official
Microsoft tutorial demonstrates working end-to-end). The identical error surviving that
change points at a bug or unsupported combination in how the portal handles
`view.outputs.kind: "Subscription"` outside an actual Template Spec resource, not at
anything fixable by further editing.

The button points at `main.json` alone (`Microsoft.Template/uri/...`, no
`createUIDefinitionUri`/`uiFormDefinitionUri` suffix) - the portal's standard,
always-reliable flat parameter list. Not much of a downside for 7 parameters, 1 of them
required. If someone wants to revisit this: Form view is documented almost entirely
around actual `Microsoft.Resources/templateSpecs` resources (`az ts create
--ui-form-definition ...`), which is a genuinely different delivery mechanism from the
raw-URI button and untested here - that's the more promising starting point than
retrying the raw-URI path again.

---

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
signal that actually matters for a scaling demo (did it scale, and to what), and Azure
SQL Database doesn't expose a network metric in the first place.

Each scaling scenario that has automation behind it (everything except SQL, which scales
itself) also gets an event-history table, so you can see *when* and *why* a scaling
decision fired, not just infer it from a tile changing value on refresh:

| Scenario | Event-history tile | Source |
|---|---|---|
| VM | "Resize Logic App - recent runs" | Logic App run log (`WorkflowRuntime` diagnostic category) |
| VM Scale Set | "Autoscale - recent scale actions" | Autoscale diagnostic log (`AutoscaleScaleActions` category) |
| App Service | "Autoscale - recent scale actions" **and** "Approval-gated resize Logic App - recent runs" | Same two sources, since App Service has both an autoscale rule and the approval-gated vertical Logic App |

How it's wired:

- CPU/disk for every resource, and CPU for the App Service and SQL Database, come from
  the platform metrics every resource already forwards to the shared Log Analytics
  workspace (`AllMetrics` diagnostic setting) - no extra agent needed, since Azure
  exposes those at the host/PaaS level.
- Guest-level memory (and per-instance CPU) for the VM and VMSS is **not** available at
  the platform level, so both also get the Azure Monitor Agent and a Data Collection
  Rule that collects `\Processor\PercentProcessorTime` and `\Memory\% Used Memory` into
  the same workspace.
- All "current size/SKU" and instance-count tiles run a live Azure Resource Graph query
  against the resource's own properties (`hardwareProfile.vmSize`, `sku.name`,
  `sku.capacity`, `properties.numberOfWorkers`, `properties.minCapacity`) rather than a
  metric, so they reflect the current state essentially immediately.
- Azure SQL Database Serverless has no metric for "vCores in use right now" - only
  `cpu_percent`, relative to the configured maximum. The vCore tile instead shows the
  configured min/max envelope the database can scale within, which is why SQL has no
  separate memory tile.
- The CPU charts for the VM, VMSS, and App Service plot the actual autoscale/alert
  threshold(s) as extra series alongside the real CPU line, so a scale event's cause is
  visible on the same chart.

**Refresh delay - be aware of it before relying on this for a live demo:** Azure Monitor
metrics land roughly once a minute, and each chart defaults to a 1-hour window (each has
its own time-range picker). The workbook does not auto-refresh by itself - use the
toolbar's **Auto refresh** control (top-right) and pick 1-5 minutes; there is no
sub-minute "live" tier. The Resource Graph-based size/instance-count tiles are much
closer to instant but still only update when the workbook itself refreshes.

---

## First-deploy checklist and known issues

This workbook is hand-authored JSON, not built through the Portal UI, so verify these
once after the first deploy and adjust the affected KQL queries in
`modules/dashboard.bicep` if needed:

- Open the `InsightsMetrics` table in Log Analytics and confirm the `Namespace`/`Name`
  values for the two custom counters match what the CPU/memory charts filter on
  (`Namespace == "Processor"`/`"Memory"`, `Name == "PercentProcessorTime"`/`"% Used
  Memory"`).
- Trigger a scale-out on the VMSS or App Service and confirm a row shows up in the
  `AutoscaleScaleActionsLog` table.
- Trigger the VM or App Service resize Logic App and confirm a row shows up in
  `AzureDiagnostics` with `Category == "WorkflowRuntime"` for that resource.
- Confirm both runbooks (`Remove-DemoResources`, `Stop-ScaleSetInstances`) show as
  **Published**, not just created, in the Automation Account - `publishContentLink`
  should handle this automatically at deploy time, but it's worth a first check.
- Confirm the `daily-vmss-shutdown` schedule's `jobSchedules` link actually triggers the
  `Stop-ScaleSetInstances` runbook with the right `VmssResourceGroup`/`VmssName`
  parameters at its first scheduled run.

**Deploy takes ~10 minutes longer than it looks like it should:** the Log Analytics
module enables the VM Insights solution, then deliberately waits before letting anything
create a data collection rule against the workspace. The wait exists because the
solution reports "Succeeded" in ARM well before the `InsightsMetrics` table it
provisions is actually queryable - without it, the VM/VMSS modules fail with
`InvalidOutputTable`. If you ever see that error, the wait
(`vmInsightsPropagationWaitSeconds` in `modules/log-analytics.bicep`, default 600s)
wasn't long enough for that region/run; increase it and redeploy.

**App Service Plan deploy can fail with `Couldn't find a metric named CpuPercentage`:**
same class of issue - the plan reports "Succeeded" before Azure Monitor's metric
definitions for it are actually queryable, so both the plan's own autoscale rule and
automation.bicep's `alertPlan` can fail if they're created too soon after it. Mitigated
the same way (`appServicePlanMetricWaitSeconds` in `modules/service-plan.bicep`, default
120s); if you still hit it, increase that and redeploy.

**Redeploying on top of a previous failed attempt can fail with `Conflict` / `A
jobSchedule with same id already exists`:** the daily VMSS-stop job schedule is
registered by a `deploymentScripts` resource instead of the declarative `jobSchedules`
ARM resource type, specifically because that type isn't idempotent - a second PUT with
the same deterministic name fails instead of no-op'ing. The script treats "already
registered" as success, so this shouldn't recur; if it somehow does, delete
`{prefix}-Automation` and redeploy.

**Shutdown behavior:** the VM and VMSS shut down automatically at 05:00 UTC to limit
cost when idle. Autoscale settings are untouched, so scaling still works correctly the
next time they are started. The App Service plan has no automatic shutdown; PaaS plans
cannot be deallocated the way a VM can, only deleted and recreated.

---

## Estimated cost

Pay-as-you-go, East US, retail prices (no reserved instances/savings plans, no free
subscription credit), all default parameter values. This is the **baseline while
everything is actively deployed and in use** - the biggest lever you have to reduce it
is simply not leaving resources up when you're not demoing (see "Tearing down" below).

| Resource | Daily | Monthly | Notes |
|---|---:|---:|---|
| VM — `Standard_B1s` + 30GB disk + Standard public IP | $0.43 | $12.78 | Roughly doubles while resized to `Standard_B1ms` during the vertical-scaling demo; a short-lived spike, not a sustained cost. |
| VM Scale Set — 1× `Standard_B1s` + disk + Standard Load Balancer + public IP | $1.03 | $31.03 | Scales close to linearly up to 4 instances under load - the Load Balancer and public IP are flat regardless of instance count, only compute+disk multiply. |
| App Service Plan — `P0v3` | $1.89 | $56.58 | PaaS - bills 24/7 regardless of traffic, can't be deallocated like a VM. Also scales toward 4× under load (and briefly to `P1v3` for the approval-gated scenario). |
| Azure SQL Database — Serverless GP Gen5, 0.5-4 vCore | $6.37 | $191.02 | **Largest line item, and only correct if the database is queried 24/7.** By default (`autoPauseDelay` = 1440 min = 24h), it **auto-pauses itself** after 24h with no activity - while paused, cost drops to storage only (well under $1/month). This row is the ceiling, not the typical case. |
| Log Analytics workspace | $0.69 | $20.70 | Estimated ingestion (~0.3 GB/day) from `AllMetrics` on every resource, the two Autoscale/Logic App diagnostic logs, and the Azure Monitor Agent counters - scales up with instance count and how much load-testing you actually run. |
| Automation Account + Logic Apps + metric alerts | $0.00 | $0.00 | Free-tier Automation SKU and low execution volume keep this inside the free monthly allowances. |
| Azure Workbook dashboard | $0.00 | $0.00 | The Workbook resource itself is free; it only reads the Log Analytics data already counted above. |
| **Total** | **~$10.40** | **~$312** | |

A few things worth knowing before treating this as a budget:
- Not in the table because it's one-time, not recurring: the Log Analytics module
  briefly spins up a Container Instance and a storage account (auto-deleted after the
  run) to wait out the VM Insights table propagation delay described above - a few
  cents at most. Also creates a small user-assigned managed identity for the
  job-schedule registration script, kept permanently (it's free) so it can be reused on
  redeploy.
- SQL Serverless dominates the total specifically because the $191.02 figure assumes the
  database runs 24/7 with no idle gap over 24h. It doesn't have to: by default it
  auto-pauses itself once nothing has queried it for `autoPauseDelayMinutes` (1440 = 24h
  out of the box). Letting it auto-pause between demo sessions (or lowering
  `autoPauseDelayMinutes`) is the single biggest cost lever here.
- If everything is left running continuously at **maximum** scale (VMSS and App Service
  both at 4 instances, VM parked at `B1ms`), the realistic ceiling is roughly
  **1.5-1.7× this total** (~$500/month) - but autoscale only holds that ceiling while
  load is actually being generated, not by default.
- Prices retrieved from the [Azure Retail Prices API](https://prices.azure.com/api/retail/prices)
  on 2026-08-05; Azure pricing changes over time and by negotiated agreement, so treat
  this as a planning estimate, not an invoice - re-check with the
  [Azure Pricing Calculator](https://azure.microsoft.com/en-us/pricing/calculator/)
  before relying on it for a real budget.

## Tearing down

Run the `Remove-DemoResources` Automation runbook from the Azure Portal (Automation
Account → Runbooks) - published automatically as part of the deploy.

**Or delete the resource groups directly** (`{prefix}-Logs`, `{prefix}-SingleVM`,
`{prefix}-ScaleSet`, `{prefix}-ServicePlan`, `{prefix}-Database`, `{prefix}-Automation`),
e.g. via the Azure CLI:

```bash
for rg in Logs SingleVM ScaleSet ServicePlan Database Automation; do
  az group delete --name "ScaleTriggerDemo-$rg" --yes --no-wait
done
```

This permanently deletes all six resource groups and everything inside them.

---

## Measuring scaling scenarios (Run-ScalingScenarios.ps1)

[`Run-ScalingScenarios.ps1`](Run-ScalingScenarios.ps1) drives load against an
already-deployed environment and collects the exact numbers a write-up of this demo
needs: how long each scenario actually took to scale, and when.

For each scenario, it:

1. benchmarks the target node (`POST /api/nodebenchmark/run`) and pushes a
   `CpuIterationsPerVote` range calibrated to its real throughput (`POST
   /api/loadconfig`) - the same "Run benchmark" -> "Set recommended" calculation the
   dashboard's CPU calibration does, just automated per scenario instead of a one-time
   manual click. Scenario B (SQL) skips this and instead zeroes `CpuIterationsPerVote`
   and sets a fixed `DbCpuIterationsPerVote` range, so its load is pure database CPU
   with no app-side contribution muddying the curve - there's no "benchmark the
   database" endpoint to calibrate the latter from. Once its load generator stops,
   Scenario B resets `DbCpuIterationsPerVote` back to 0 so it doesn't linger into
   whatever scenario runs next,
2. starts the load generator (`../../scripts/scaleTriggerLoad.py`) against the live
   endpoint,
3. polls the relevant Azure Monitor metric until it crosses the alert threshold,
4. once crossed, queries the Log Analytics workspace (KQL against `AzureActivity`) for
   the precise timestamp of the actual scaling operation,
5. prints the result and saves it to a CSV file, and
6. assembles every scenario's CSV into one HTML report at the end.

Scenario C (App Service plan, approval-gated) waits on you to manually trigger a Logic
App after a push notification - when running multiple scenarios together, it's kicked
off as a background job so it doesn't block the others.

### Setup

Nothing to set up for a fresh deploy under the default prefixes - every scenario's URL,
the two resource names with a random uniqueness suffix (SQL Server, App Service Plan),
and the Log Analytics workspace ID are **auto-detected** by querying the actually-
deployed resources in `{ResourceGroupPrefix}-SingleVM`/`-ScaleSet`/`-ServicePlan`/
`-Database`/`-Logs` directly. `-ResourceGroupPrefix`/`-ResourcePrefix`/`-SubscriptionId`
are all it needs.

Auto-detection is scenario-aware (running `-Scenario A` doesn't require the SQL Database
to be deployed) and only fills in gaps - a `-XxxApiUrl` override always takes priority.
Use those when you want to point at something other than what's deployed, or
auto-detection gets something wrong.

Every azure-demo-resources scenario has `Auth:Enabled=true` and configures the app's
`AdminUser:Username`/`Password` from the deployment's own `-AdminUsername`/
`-AdminPassword` (`cloud-init-scaletrigger.bicep`/`service-plan.bicep`) - there's no case
here where auth isn't required, unlike a plain ScaleTrigger deployment. `-AdminPassword`
is used for this script's own benchmark/loadconfig calls **and** forwarded to
`scaleTriggerLoad.py`'s `--username`/`--password`, so its login matches what's actually
configured instead of that script's own `admin:admin` fallback (which this demo never
uses).

### Usage

`-Scenario`, `-Path`, and `-AdminPassword` are required (the password isn't needed for
`-Scenario Report`, which only reads existing CSVs and never touches Azure or the app) -
the script refuses to run without them, rather than guessing which scenario to run,
writing files somewhere unexpected, or letting the load generator fail with a silent 401.
Running it with no parameters (or missing a required one) prints the full parameter
reference instead:

```powershell
.\Run-ScalingScenarios.ps1 -Scenario All -Path .\results -AdminPassword "MyDeployPassword123!"
```

```powershell
.\Run-ScalingScenarios.ps1 -Scenario A -Path .\results -AdminPassword "MyDeployPassword123!"
.\Run-ScalingScenarios.ps1 -Scenario Report -Path .\results   # just rebuild the HTML report from existing CSVs - no password needed
```

Auto-detected URLs can be overridden per-run from the command line - useful for a quick
one-off test without redeploying, e.g. pointing at a VM that isn't the one currently
deployed:

```powershell
.\Run-ScalingScenarios.ps1 -Scenario A -Path .\results -AdminPassword "MyDeployPassword123!" -VmApiUrl "https://20.1.2.3"
```

`-AdminPassword` is a plain string here - this script is a repeatable testing tool, not
a one-time provisioning step, so it favors typing the password directly over
`ConvertTo-SecureString`/`Read-Host` ceremony every run.

| Parameter | Default | Notes |
|---|---|---|
| `-Scenario` | *(required)* | `A`, `B`, `C`, `VMSS`, `AppService`, `All`, or `Report`. |
| `-Path` | *(required)* | Directory the result CSVs and HTML report are saved to. |
| `-AdminPassword` | *(required unless `-Scenario Report`)* | The password set at deploy time - forwarded to the benchmark/loadconfig calls and to `scaleTriggerLoad.py`. |
| `-AdminUsername` | `demoadmin` | The username set at deploy time. |
| `-ResourceGroupPrefix` | `ScaleTriggerDemo` | Which deployment to auto-detect resources from. |
| `-ResourcePrefix` | `ScaleTrigger` | Which deployment to auto-detect resources from. |
| `-SubscriptionId` | current Az context | Only switches context if explicitly provided. |
| `-VmApiUrl` | auto-detected from the VM's public IP/DNS | Overrides it for this run. |
| `-DatabaseApiUrl` | auto-detected (same as `-AppServiceApiUrl`) | Overrides it for this run. |
| `-AppServiceApiUrl` | auto-detected from the Web App's default hostname | Overrides both scenario C and AppService (same Web App) for this run. |
| `-VmssApiUrl` | auto-detected from the Scale Set load balancer's public IP | Overrides it for this run. |
| `-DbCpuIterationsMin` / `-DbCpuIterationsMax` | `5000` / `5000` | Fixed `DbCpuIterationsPerVote` range scenario B sets before its load starts - no benchmark to calibrate this one from. |
| `-BenchmarkTargetVotes` | `100` | Target vote count (N) for the CPU calibration formula, same default as the dashboard's own "Set recommended" button. |
| `-SkipHtmlReport` | off | Skip generating the HTML report at the end. |

Precedence for every URL/name: a command-line flag always wins over auto-detection.

**Run it from this folder.** `-LoadScriptPath` resolves `../../scripts/scaleTriggerLoad.py`
relative to the script's own location, which only holds true inside a clone of this
repo - if you copy the `.ps1` file elsewhere, pass `-LoadScriptPath` pointing at the
real file explicitly.
