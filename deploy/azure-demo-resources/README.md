# ScaleTrigger Azure Scaling Demo

Six independent Bicep templates that provision a small Azure environment demonstrating
five different Azure scaling mechanisms, each running the [ScaleTrigger](https://github.com/dopiskur/scaleTrigger)
load-simulation app: a VM (vertical scaling via Azure Monitor + Logic App), an Azure SQL
Database in the Serverless tier (vertical scaling built into the platform), a VM Scale
Set (horizontal scaling via native Autoscale), and an App Service plan (horizontal
scaling via native Autoscale, plus a second, approval-gated vertical scaling scenario).

## Prerequisites

- An Azure subscription
- PowerShell 5.1 or later
- Internet access from the machine running `Deploy.ps1` (it installs the Az PowerShell
  modules and the Bicep CLI automatically if they are missing)

## Structure

```
01-log-analytics.bicep
02-single-vm.bicep
03-scale-set.bicep
04-service-plan.bicep
05-sql-database.bicep
06-automation.bicep
modules/
Deploy.ps1
teardown-runbook.ps1
stop-vmss-runbook.ps1
07-teardown.sh
```

Each numbered template provisions its own resource group and everything inside it.
They can be deployed independently, but 05 must run before 03 and 04 (both connect to
the SQL database on first boot), and 06 must run after 02 and 04 (it references their
resources by name).

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

Both the VM and the VMSS install and configure the ScaleTrigger app automatically via
cloud-init: .NET 10, the app itself, a systemd service, and Nginx as a reverse proxy on
ports 80 and 443 (self-signed certificate). The App Service deploys the app directly
from the public GitHub repository.

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

## Tearing down

Either run the Automation runbook (`teardown-runbook.ps1`, uploaded and published by
`Deploy.ps1` as part of module 06) from the Azure Portal, or run the bash script locally:

```bash
./07-teardown.sh --prefix ScaleTriggerDemo
./07-teardown.sh --prefix ScaleTriggerDemo --force --no-wait
```

This permanently deletes all six resource groups and everything inside them.
