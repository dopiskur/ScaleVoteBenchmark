<#
.SYNOPSIS
    Deploys the ScaleTrigger demo Azure environment (seven independent Bicep templates)
    using the Az PowerShell module.

.DESCRIPTION
    Provisions six resource groups, each containing one scaling scenario: a single VM
    (vertical scaling), a Virtual Machine Scale Set (horizontal scaling), an App Service
    plan (horizontal scaling, plus a vertical scenario requiring manual approval), an
    Azure SQL Database (Serverless, platform-native vertical scaling), a Log Analytics
    workspace with a monitoring dashboard, and an Automation Account with supporting
    Logic Apps and runbooks.

    Missing Az modules (Az.Accounts, Az.Resources, Az.Automation, Az.Sql) and the Bicep
    CLI are installed automatically on first run.

.PARAMETER Mode
    'All' deploys all seven templates in the required order. 'Single' deploys only one,
    selected via -Module. If omitted, an interactive menu is shown.

.PARAMETER Module
    Used with -Mode Single. One of '01'..'07'.

.PARAMETER AdminUsername
    Administrator username for the VM, VMSS, and SQL Server. Default: demoadmin.

.PARAMETER AdminPassword
    Administrator password for the VM, VMSS, and SQL Server. Required, no default.
    Must satisfy Azure's complexity requirements (12+ characters, 3 of 4 character
    classes).

.PARAMETER ResourceGroupPrefix
    Prefix applied to all resource group names. Default: ScaleTriggerDemo.
    Example: with the default prefix, the VM scenario is deployed into
    "ScaleTriggerDemo-SingleVM".

.PARAMETER ResourcePrefix
    Prefix applied to all resource names within those resource groups. Default:
    ScaleTrigger. Example: the VM itself is named "ScaleTrigger-vm". Globally unique
    resources (the SQL Server and the Web App) additionally get a random suffix.

.PARAMETER SubscriptionId
    Optional. If provided, switches the active Az context to this subscription before
    deploying. If omitted, whatever subscription is currently active in the Az context
    is used.

.PARAMETER Location
    Azure region. Default: eastus.

.PARAMETER ApprovalNotificationUpn
    Azure AD account that receives a push notification (via the Azure mobile app) when
    the App Service plan's vertical scaling alert fires. Default: dummy@somemail.com.
    This default will not receive anything meaningful; replace it with a real account
    UPN before relying on the approval workflow.

.PARAMETER AutoShutdownHour
    Hour of the day (0-23, local time zone configured in the Bicep templates) at which
    the VM and VMSS instances are automatically shut down / stopped to limit cost when
    idle. Default: 5 (05:00). Applies to module 02's own auto-shutdown schedule and to
    module 06's daily VMSS-stop runbook schedule.

.PARAMETER DeploymentMaxAttempts
    How many times to retry a module's deployment when it fails with the known
    transient "metric not yet available on a freshly created resource" error. Default:
    5. All other deployment errors are surfaced immediately, without retrying.

.PARAMETER DeploymentRetryDelaySeconds
    Seconds to wait between deployment retry attempts (see -DeploymentMaxAttempts).
    Default: 60.

.PARAMETER SqlWarmupRetryDelaySeconds
    Seconds to wait between connection attempts while waiting for the freshly created
    Azure SQL Serverless database to become reachable (module 05 only). Default: 15.
    Retries for up to 10 minutes total before failing.

.EXAMPLE
    .\Deploy.ps1 -AdminPassword (Read-Host -Prompt "Password" -AsSecureString)

.EXAMPLE
    .\Deploy.ps1 -Mode All -AdminPassword $securePassword -ApprovalNotificationUpn you@example.com

.EXAMPLE
    .\Deploy.ps1 -Mode Single -Module 04 -AdminPassword $securePassword -ResourceGroupPrefix MyDemo -ResourcePrefix MyApp
#>

[CmdletBinding()]
param(
    [ValidateSet('All', 'Single')]
    [string]$Mode,

    [ValidateSet('01', '02', '03', '04', '05', '06', '07')]
    [string]$Module,

    [string]$AdminUsername = 'demoadmin',

    [SecureString]$AdminPassword,

    [string]$ResourceGroupPrefix = 'ScaleTriggerDemo',

    [string]$ResourcePrefix = 'ScaleTrigger',

    [string]$SubscriptionId = '',

    [string]$Location = 'eastus',

    [string]$ApprovalNotificationUpn = 'dummy@somemail.com',

    [int]$AutoShutdownHour = 5,

    [int]$DeploymentMaxAttempts = 5,
    [int]$DeploymentRetryDelaySeconds = 60,

    [int]$SqlWarmupRetryDelaySeconds = 15
)

if ($PSBoundParameters.Count -eq 0) {
    Write-Host ""
    Write-Host "Example: .\Deploy.ps1 -AdminPassword (Read-Host -Prompt 'Password' -AsSecureString)" -ForegroundColor Cyan
    Write-Host ""
    Get-Help -Full $PSCommandPath
    return
}

$ErrorActionPreference = 'Stop'

if (-not $AdminPassword) {
    throw "AdminPassword is required. Example: .\Deploy.ps1 -AdminPassword (Read-Host -Prompt 'Password' -AsSecureString)"
}

if ($ApprovalNotificationUpn -eq 'dummy@somemail.com') {
    Write-Host "WARNING: -ApprovalNotificationUpn was left at its default placeholder value. The approval-based vertical scaling scenario (module 06) will not notify anyone useful. Pass a real Azure AD account UPN via -ApprovalNotificationUpn to receive push notifications." -ForegroundColor Yellow
}

$requiredModules = @('Az.Accounts', 'Az.Resources', 'Az.Automation', 'Az.Sql')
foreach ($m in $requiredModules) {
    if (-not (Get-Module -ListAvailable -Name $m)) {
        Install-Module -Name $m -Scope CurrentUser -Force -AllowClobber -Repository PSGallery
    }
    Import-Module $m -ErrorAction Stop
}

$bicepDir = Join-Path $PSScriptRoot '.bicep-tool'
$bicepExe = Join-Path $bicepDir 'bicep.exe'
if (-not (Test-Path $bicepExe)) {
    New-Item -ItemType Directory -Path $bicepDir -Force | Out-Null
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri 'https://github.com/Azure/bicep/releases/latest/download/bicep-win-x64.exe' -OutFile $bicepExe -UseBasicParsing
}
if ($env:PATH -notlike "*$bicepDir*") {
    $env:PATH = "$bicepDir;$env:PATH"
}
$userPath = [Environment]::GetEnvironmentVariable('PATH', 'User')
if ($userPath -notlike "*$bicepDir*") {
    [Environment]::SetEnvironmentVariable('PATH', "$userPath;$bicepDir", 'User')
}

$ModuleMap = [ordered]@{
    '01' = @{ File = 'scripts/01-log-analytics.bicep'; NeedsCredentials = $false; Description = 'Log Analytics workspace' }
    '02' = @{ File = 'scripts/02-single-vm.bicep';      NeedsCredentials = $true;  Description = 'Scenario A - single VM, vertical scaling' }
    '03' = @{ File = 'scripts/03-scale-set.bicep';      NeedsCredentials = $true;  Description = 'Horizontal scaling - VMSS' }
    '04' = @{ File = 'scripts/04-service-plan.bicep';   NeedsCredentials = $true;  Description = 'Horizontal + approval-gated vertical scaling - App Service' }
    '05' = @{ File = 'scripts/05-sql-database.bicep';   NeedsCredentials = $true;  Description = 'Scenario B - Azure SQL Serverless' }
    '06' = @{ File = 'scripts/06-automation.bicep';     NeedsCredentials = $false; Description = 'Automation - Logic Apps, alerts, runbooks' }
    '07' = @{ File = 'scripts/07-dashboard.bicep';      NeedsCredentials = $false; Description = 'Monitoring dashboard - Azure Workbook (CPU/memory/disk/network, instance counts)' }
}

$DeployOrder = @('01', '05', '02', '03', '04', '06', '07')

$script:SqlServerName = $null
$script:SqlServerFqdn = $null

function Get-PlainPassword {
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($AdminPassword)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

function Ensure-ClientFirewallRule {
    param(
        [string]$SqlResourceGroupName,
        [string]$SqlServerName
    )

    try {
        $clientIp = (Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 10).Trim()
    }
    catch {
        Write-Host "Could not determine public IP address - skipping automatic firewall rule." -ForegroundColor Yellow
        return
    }

    $ruleName = "deploy-client-$($clientIp -replace '\.', '-')"
    $existingRule = Get-AzSqlServerFirewallRule -ResourceGroupName $SqlResourceGroupName -ServerName $SqlServerName -FirewallRuleName $ruleName -ErrorAction SilentlyContinue
    if (-not $existingRule) {
        New-AzSqlServerFirewallRule -ResourceGroupName $SqlResourceGroupName -ServerName $SqlServerName -FirewallRuleName $ruleName -StartIpAddress $clientIp -EndIpAddress $clientIp | Out-Null
    }
}

function Wait-ForSqlReady {
    param(
        [string]$SqlResourceGroupName,
        [string]$SqlServerName,
        [string]$SqlServerFqdn,
        [string]$SqlDatabaseName
    )

    Ensure-ClientFirewallRule -SqlResourceGroupName $SqlResourceGroupName -SqlServerName $SqlServerName

    $plainPassword = Get-PlainPassword
    $connectionString = "Server=tcp:$SqlServerFqdn,1433;Database=$SqlDatabaseName;User ID=$AdminUsername;Password=$plainPassword;Encrypt=True;TrustServerCertificate=False;Connect Timeout=10;"

    $deadline = (Get-Date).AddMinutes(10)
    $attempt = 0
    $lastError = $null
    while ((Get-Date) -lt $deadline) {
        $attempt++
        try {
            $conn = New-Object System.Data.SqlClient.SqlConnection $connectionString
            $conn.Open()
            $conn.Close()
            return
        }
        catch {
            $lastError = $_
            Write-Host "Attempt $attempt failed ($($_.Exception.Message.Split("`n")[0])), retrying in ${SqlWarmupRetryDelaySeconds}s ..." -ForegroundColor Yellow
            Start-Sleep -Seconds $SqlWarmupRetryDelaySeconds
        }
    }

    throw "SQL Database did not become reachable within 10 minutes. Last error: $($lastError.Exception.Message)"
}

function Deploy-ScaleTriggerModule {
    param([string]$Id)

    $info = $ModuleMap[$Id]
    if (-not $info) { throw "Unknown module '$Id'." }
    if (-not (Test-Path $info.File)) { throw "File '$($info.File)' not found - run this script from the deploy\azure-demo-resources\manual directory." }

    Write-Host ""
    Write-Host "==> [$Id] $($info.Description)" -ForegroundColor Cyan

    $paramObject = @{
        resourceGroupPrefix = $ResourceGroupPrefix
        resourcePrefix      = $ResourcePrefix
        location            = $Location
    }

    if ($info.NeedsCredentials) {
        $paramObject['adminUsername'] = $AdminUsername
        $paramObject['adminPassword'] = Get-PlainPassword
    }
    if ($Id -in '02', '03', '04', '05') {
        $paramObject['logAnalyticsResourceGroupPrefix'] = $ResourceGroupPrefix
    }
    if ($Id -eq '02') {
        $paramObject['autoShutdownTime'] = '{0:D2}00' -f $AutoShutdownHour
    }
    if ($Id -in '03', '04') {
        $paramObject['sqlResourceGroupPrefix'] = $ResourceGroupPrefix
    }
    if ($Id -eq '06') {
        $paramObject['singleVmResourceGroupPrefix'] = $ResourceGroupPrefix
        $paramObject['servicePlanResourceGroupPrefix'] = $ResourceGroupPrefix
        $paramObject['approvalNotificationUpn'] = $ApprovalNotificationUpn
    }
    if ($Id -eq '07') {
        $paramObject['singleVmResourceGroupPrefix'] = $ResourceGroupPrefix
        $paramObject['scaleSetResourceGroupPrefix'] = $ResourceGroupPrefix
        $paramObject['servicePlanResourceGroupPrefix'] = $ResourceGroupPrefix
        $paramObject['sqlResourceGroupPrefix'] = $ResourceGroupPrefix
        $paramObject['logAnalyticsResourceGroupPrefix'] = $ResourceGroupPrefix
    }

    $validationErrors = Test-AzSubscriptionDeployment `
        -Location $Location `
        -TemplateFile $info.File `
        -TemplateParameterObject $paramObject

    if ($validationErrors) {
        $validationErrors | ForEach-Object { Write-Host "    $($_.Message)" -ForegroundColor Red }
        throw "Template '$Id' failed pre-flight validation."
    }

    $deploymentName = "scaletrigger-$Id-$(Get-Date -Format 'yyyyMMddHHmmss')"

    $result = $null
    for ($attempt = 1; $attempt -le $DeploymentMaxAttempts; $attempt++) {
        try {
            $result = New-AzSubscriptionDeployment `
                -Name $deploymentName `
                -Location $Location `
                -TemplateFile $info.File `
                -TemplateParameterObject $paramObject
            break
        }
        catch {
            $isMetricNotReadyYet = $_.Exception.Message -like "*Couldn't find a metric named*"
            if (-not $isMetricNotReadyYet -or $attempt -eq $DeploymentMaxAttempts) {
                throw
            }
            Write-Host "    Metric not yet available on newly created resource, retrying in ${DeploymentRetryDelaySeconds}s (attempt $attempt/$DeploymentMaxAttempts) ..." -ForegroundColor Yellow
            Start-Sleep -Seconds $DeploymentRetryDelaySeconds
        }
    }

    if ($result.ProvisioningState -ne 'Succeeded') {
        throw "Deployment '$Id' did not succeed. State: $($result.ProvisioningState)"
    }

    Write-Host "    Module '$Id' deployed." -ForegroundColor Green

    if ($Id -eq '05') {
        $script:SqlServerName = $result.Outputs.sqlServerFqdn.Value.Split('.')[0]
        $script:SqlServerFqdn = $result.Outputs.sqlServerFqdn.Value
        Wait-ForSqlReady `
            -SqlResourceGroupName $result.Outputs.resourceGroupName.Value `
            -SqlServerName $script:SqlServerName `
            -SqlServerFqdn $script:SqlServerFqdn `
            -SqlDatabaseName "$ResourcePrefix-sqldb"
    }

    if ($Id -eq '06') {
        $runbookPath = Join-Path $PSScriptRoot 'scripts/teardown-runbook.ps1'
        Import-AzAutomationRunbook `
            -ResourceGroupName $result.Outputs.resourceGroupName.Value `
            -AutomationAccountName $result.Outputs.automationAccountName.Value `
            -Name $result.Outputs.teardownRunbookName.Value `
            -Path $runbookPath `
            -Type PowerShell `
            -Force | Out-Null
        Publish-AzAutomationRunbook `
            -ResourceGroupName $result.Outputs.resourceGroupName.Value `
            -AutomationAccountName $result.Outputs.automationAccountName.Value `
            -Name $result.Outputs.teardownRunbookName.Value | Out-Null

        $stopVmssRunbookName = $result.Outputs.stopVmssRunbookName.Value
        if ($stopVmssRunbookName) {
            $stopRunbookPath = Join-Path $PSScriptRoot 'scripts/stop-vmss-runbook.ps1'
            Import-AzAutomationRunbook `
                -ResourceGroupName $result.Outputs.resourceGroupName.Value `
                -AutomationAccountName $result.Outputs.automationAccountName.Value `
                -Name $stopVmssRunbookName `
                -Path $stopRunbookPath `
                -Type PowerShell `
                -Force | Out-Null
            Publish-AzAutomationRunbook `
                -ResourceGroupName $result.Outputs.resourceGroupName.Value `
                -AutomationAccountName $result.Outputs.automationAccountName.Value `
                -Name $stopVmssRunbookName | Out-Null

            $scheduleName = 'daily-vmss-shutdown'
            $nextShutdown = (Get-Date -Hour $AutoShutdownHour -Minute 0 -Second 0)
            if ($nextShutdown -le (Get-Date)) { $nextShutdown = $nextShutdown.AddDays(1) }

            $existingSchedule = Get-AzAutomationSchedule `
                -ResourceGroupName $result.Outputs.resourceGroupName.Value `
                -AutomationAccountName $result.Outputs.automationAccountName.Value `
                -Name $scheduleName -ErrorAction SilentlyContinue
            if (-not $existingSchedule) {
                New-AzAutomationSchedule `
                    -ResourceGroupName $result.Outputs.resourceGroupName.Value `
                    -AutomationAccountName $result.Outputs.automationAccountName.Value `
                    -Name $scheduleName `
                    -StartTime $nextShutdown `
                    -DayInterval 1 `
                    -TimeZone 'Central European Standard Time' | Out-Null
            }

            Register-AzAutomationScheduledRunbook `
                -ResourceGroupName $result.Outputs.resourceGroupName.Value `
                -AutomationAccountName $result.Outputs.automationAccountName.Value `
                -RunbookName $stopVmssRunbookName `
                -ScheduleName $scheduleName `
                -Parameters @{ VmssResourceGroup = "$ResourceGroupPrefix-ScaleSet"; VmssName = "$ResourcePrefix-vmss" } `
                -ErrorAction SilentlyContinue -ErrorVariable registerError | Out-Null
            if ($registerError -and $registerError[0].Exception.Message -notlike '*already*') {
                Write-Host "    WARNING: schedule registration failed: $($registerError[0].Exception.Message)" -ForegroundColor Red
            }
        }
    }
}

function Deploy-All {
    Write-Host "Order: $($DeployOrder -join ' -> ')" -ForegroundColor Yellow
    foreach ($id in $DeployOrder) {
        Deploy-ScaleTriggerModule -Id $id
    }
    Write-Host ""
    Write-Host "All six modules deployed." -ForegroundColor Green
}

$context = Get-AzContext
if (-not $context) {
    Connect-AzAccount | Out-Null
}
if ($SubscriptionId) {
    Set-AzContext -SubscriptionId $SubscriptionId | Out-Null
}

if (-not $Mode) {
    Write-Host ""
    Write-Host "1) Deploy All     - all six templates, in order ($($DeployOrder -join ' -> '))"
    Write-Host "2) Deploy Single  - one selected template"
    $choice = Read-Host -Prompt "Enter 1 or 2"
    switch ($choice) {
        '1' { $Mode = 'All' }
        '2' { $Mode = 'Single' }
        default { throw "Invalid choice '$choice'." }
    }
}

if ($Mode -eq 'Single' -and -not $Module) {
    Write-Host ""
    foreach ($id in $ModuleMap.Keys) {
        Write-Host "  $id - $($ModuleMap[$id].Description)"
    }
    $Module = Read-Host -Prompt "Enter module number (01-06)"
}

switch ($Mode) {
    'All' { Deploy-All }
    'Single' { Deploy-ScaleTriggerModule -Id $Module }
}
