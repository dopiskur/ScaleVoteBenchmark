<#
.SYNOPSIS
    Drives load against the five scaling scenarios (A, B, C, VMSS, App Service) and
    collects the timing values for chapter 7's tables.

.DESCRIPTION
    Per scenario: benchmarks the node and calibrates CpuIterationsPerVote via
    /api/loadconfig (dashboard's "CPU calibration" formula; scenario B sets a fixed
    DbCpuIterationsPerVote instead - no benchmark endpoint exists for the database),
    starts scaleTriggerLoad.py, polls Azure Monitor until the alert threshold crosses,
    queries Log Analytics (KQL) for the exact scaling timestamp, and saves a CSV.

    URLs, resource names, and the workspace ID are auto-detected from
    {ResourceGroupPrefix}-SingleVM/-ScaleSet/-ServicePlan/-Database/-Logs - a fresh
    deploy needs only -ResourceGroupPrefix/-ResourcePrefix/-SubscriptionId.
    -XxxApiUrl overrides anything auto-detection gets wrong.

    Every scenario has Auth:Enabled=true with AdminUser set from the deployment's own
    -AdminUsername/-AdminPassword, so -AdminPassword is REQUIRED - forwarded to this
    script's own calls and to scaleTriggerLoad.py's --username/--password.

.PARAMETER Scenario
    A, B, C, VMSS, AppService, or All. REQUIRED - no parameters at all prints usage instead.

.PARAMETER Path
    Directory for result CSV/HTML files. REQUIRED.

.PARAMETER ResourceGroupPrefix
    Which deployment to auto-detect from. Matches Deploy.ps1's -ResourceGroupPrefix. Default: ScaleTriggerDemo.

.PARAMETER ResourcePrefix
    Which deployment to auto-detect from. Matches Deploy.ps1's -ResourcePrefix. Default: ScaleTrigger.

.PARAMETER SubscriptionId
    Switches the active Az context if given; otherwise uses whatever's already active.

.PARAMETER VmApiUrl
    Overrides scenario A's auto-detected URL (the VM's public IP/DNS).

.PARAMETER DatabaseApiUrl
    Overrides scenario B's auto-detected URL (defaults to the App Service - the VM uses local SQLite).

.PARAMETER AppServiceApiUrl
    Overrides scenario C's and AppService's URL (same Web App, one override covers both).

.PARAMETER VmssApiUrl
    Overrides scenario VMSS's auto-detected URL (the load balancer's public IP).

.PARAMETER AdminUsername
    Login for /api/auth/login - whatever -AdminUsername was passed at deploy time. Default: demoadmin.

.PARAMETER AdminPassword
    Password for the login above. REQUIRED unless -Scenario Report - every scenario has Auth:Enabled=true.

.PARAMETER DbCpuIterationsMin
    Fixed Min for scenario B's DbCpuIterationsPerVote. Default: 5000.

.PARAMETER DbCpuIterationsMax
    Fixed Max for scenario B's DbCpuIterationsPerVote. Default: 5000.

.PARAMETER BenchmarkTargetVotes
    Target vote count (N) for the CPU calibration formula. Default: 100 (matches the dashboard).

.PARAMETER SkipHtmlReport
    Skip generating the HTML report at the end.

.PARAMETER AzContextPath
    Internal - used when scenario C runs as a background job. Don't set manually.

.PARAMETER ScenarioCTimeoutMinutes
    Minutes scenario C waits for manual Logic App approval. Default: 240.

.NOTES
    Scenario C runs as a background job when multiple scenarios are queued (it waits on
    manual approval, so it can't block the rest) - fold its result in later with
    "-Scenario Report".

    Don't copy this script out of deploy/azure-demo-resources/ - -LoadScriptPath's default
    depends on the fixed ../../scripts/scaleTriggerLoad.py offset from here.

    VM/VMSS use a self-signed cert. This script and scaleTriggerLoad.py both disable TLS
    validation for their own requests - otherwise every vote fails silently.

.EXAMPLE
    .\Run-ScalingScenarios.ps1 -Scenario All -Path .\results -AdminPassword "MyDeployPassword123!"

.EXAMPLE
    .\Run-ScalingScenarios.ps1 -Scenario A -Path .\results -AdminPassword "MyDeployPassword123!" -VmApiUrl "https://20.1.2.3"

.EXAMPLE
    .\Run-ScalingScenarios.ps1 -Scenario Report -Path .\results   # just assemble the HTML report from existing CSVs - no password needed
#>

[CmdletBinding()]
param(
    # No defaults - see the usage block below if either is left out.
    [ValidateSet('A', 'B', 'C', 'VMSS', 'AppService', 'All', 'Report')]
    [string]$Scenario,

    [string]$Path,

    [string]$ResourceGroupPrefix = "ScaleTriggerDemo",
    [string]$ResourcePrefix = "ScaleTrigger",
    [string]$SubscriptionId = "",

    [string]$VmApiUrl,
    [string]$DatabaseApiUrl,
    [string]$AppServiceApiUrl,
    [string]$VmssApiUrl,

    # Every scenario has Auth:Enabled=true from these exact values - no "no auth" case here.
    [string]$AdminUsername = "demoadmin",
    [string]$AdminPassword,

    [int]$DbCpuIterationsMin = 5000,
    [int]$DbCpuIterationsMax = 5000,

    # Target vote count (N) for the CPU calibration formula - same default as the dashboard.
    [int]$BenchmarkTargetVotes = 100,

    # Anchored to $PSScriptRoot so it works regardless of cwd (see .NOTES for the caveat).
    [string]$PythonExe = "python",
    [string]$LoadScriptPath = (Join-Path $PSScriptRoot "..\..\scripts\scaleTriggerLoad.py"),

    [switch]$SkipHtmlReport,

    [string]$AzContextPath = "",

    [int]$ScenarioCTimeoutMinutes = 240
)

function Show-UsageHelp {
    Write-Host ""
    Write-Host "Example:" -ForegroundColor Cyan
    Write-Host '  .\Run-ScalingScenarios.ps1 -Scenario All -Path .\results -AdminPassword "MyDeployPassword123!"' -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Parameters:"
    Write-Host "  -Scenario <A|B|C|VMSS|AppService|All|Report>  REQUIRED. Which scenario to run."
    Write-Host "  -Path <string>                           REQUIRED. Directory the result CSV/HTML files are saved to."
    Write-Host "  -AdminPassword <string>                  REQUIRED unless -Scenario Report. The password set at deploy time -"
    Write-Host "                                            this demo always has Auth:Enabled=true, there's no case without it."
    Write-Host "  -ResourceGroupPrefix <string>            [optional] Which deployment to auto-detect from. Default: ScaleTriggerDemo."
    Write-Host "  -ResourcePrefix <string>                 [optional] Which deployment to auto-detect from. Default: ScaleTrigger."
    Write-Host "  -SubscriptionId <string>                 [optional] Az subscription to switch to before running. Default: current context."
    Write-Host "  -VmApiUrl <string>                       [optional] Overrides auto-detected scenario A URL."
    Write-Host "  -DatabaseApiUrl <string>                 [optional] Overrides auto-detected scenario B URL."
    Write-Host "  -AppServiceApiUrl <string>                [optional] Overrides auto-detected scenario C/AppService URL."
    Write-Host "  -VmssApiUrl <string>                     [optional] Overrides auto-detected scenario VMSS URL."
    Write-Host "  -AdminUsername <string>                  [optional] The username set at deploy time. Default: demoadmin."
    Write-Host "  -DbCpuIterationsMin/-DbCpuIterationsMax  [optional] Fixed DbCpuIterationsPerVote range for scenario B. Default: 5000/5000."
    Write-Host "  -BenchmarkTargetVotes <int>              [optional] Target vote count (N) for CPU calibration. Default: 100."
    Write-Host "  -PythonExe <string>                      [optional] Path to the Python executable. Default: python."
    Write-Host "  -LoadScriptPath <string>                 [optional] Path to scaleTriggerLoad.py. Default: resolved next to this script."
    Write-Host "  -SkipHtmlReport                          [optional] Switch - skip generating the HTML report at the end."
    Write-Host "  -ScenarioCTimeoutMinutes <int>           [optional] Minutes scenario C waits for manual approval. Default: 240."
    Write-Host ""
    Write-Host "Full parameter reference: Get-Help .\Run-ScalingScenarios.ps1 -Full"
    Write-Host "Tip: URLs and resource names are auto-detected from what's actually deployed - the flags"
    Write-Host "     above are only needed to override that."
    Write-Host ""
}

if (-not $Scenario -or -not $Path -or ($Scenario -ne 'Report' -and -not $AdminPassword)) {
    Show-UsageHelp
    return
}

# ============================================================================
# CONFIGURATION - defaults, filled in by Resolve-DeployedResources/-XxxApiUrl below
# ============================================================================

# Two resource names include a random uniqueString() suffix at deploy time and can't be
# derived from either prefix alone - Resolve-DeployedResources looks these up directly.
$sqlServerNamePlaceholder      = "<$ResourcePrefix-sqlserver-xxxxxxxxxxxxx>"
$appServicePlanNamePlaceholder = "<app-service-plan-name>"

$Config = @{
    # Resolved after Assert-AzConnection runs, from -SubscriptionId if given, otherwise
    # from whatever subscription ends up active in the Az context.
    SubscriptionId          = $null

    LogAnalyticsWorkspaceId = "<workspace-id-for-$ResourcePrefix-logs-in-$ResourceGroupPrefix-Logs>"

    # --- Scenario A: virtual machine (chapter 7.3.2) ---
    ScenarioA = @{
        ResourceGroup   = "$ResourceGroupPrefix-SingleVM"
        VMName          = "$ResourcePrefix-vm"
        MetricName      = "Percentage CPU"
        MetricNamespace = "Microsoft.Compute/virtualMachines"
        ThresholdValue  = 80
        LogicAppName    = "$ResourcePrefix-la-vm-resize"
        ApiUrl          = "https://<vm-public-ip-or-dns>"
        LoadArgs        = "--ramp true --votes 100 --duration 1200"
    }

    # --- Scenario B: Azure SQL Serverless (chapter 7.3.3) ---
    ScenarioB = @{
        ResourceGroup   = "$ResourceGroupPrefix-Database"
        ServerName      = $sqlServerNamePlaceholder
        DatabaseName    = "$ResourcePrefix-sqldb"
        MetricName      = "cpu_percent"
        MetricNamespace = "Microsoft.Sql/servers/databases"
        ApiUrl          = "https://<scaletrigger-instance-url-for-db-test>"
        LoadArgs        = "--ramp true --votes 100 --duration 1200"
    }

    # --- Scenario C: App Service plan with human approval (chapter 7.3.4) ---
    ScenarioC = @{
        ResourceGroup   = "$ResourceGroupPrefix-ServicePlan"
        AppServicePlan  = $appServicePlanNamePlaceholder
        MetricName      = "CpuPercentage"
        MetricNamespace = "Microsoft.Web/serverfarms"
        ThresholdValue  = 80
        LogicAppName    = "$ResourcePrefix-la-plan-resize-approval"
        ApiUrl          = "https://<webapp-name>.azurewebsites.net"
        LoadArgs        = "--ramp true --votes 100 --duration 2400"
    }

    # --- Horizontal scaling: VMSS (chapter 7.4) ---
    ScenarioVMSS = @{
        ResourceGroup   = "$ResourceGroupPrefix-ScaleSet"
        VMSSName        = "$ResourcePrefix-vmss"
        MetricName      = "Percentage CPU"
        MetricNamespace = "Microsoft.Compute/virtualMachineScaleSets"
        ApiUrl          = "http://<scaleset-lb-public-ip>"
        LoadArgs        = "--ramp true --votes 100 --duration 900"
    }

    # --- Horizontal scaling: App Service plan (chapter 7.5) ---
    ScenarioAppService = @{
        ResourceGroup   = "$ResourceGroupPrefix-ServicePlan"
        AppServicePlan  = $appServicePlanNamePlaceholder
        MetricName      = "CpuPercentage"
        MetricNamespace = "Microsoft.Web/serverfarms"
        ApiUrl          = "https://<webapp-name>.azurewebsites.net"
        LoadArgs        = "--ramp true --votes 100 --duration 900"
    }
}

# Command-line URL overrides win over auto-detection.
if ($VmApiUrl)          { $Config.ScenarioA.ApiUrl = $VmApiUrl }
if ($DatabaseApiUrl)    { $Config.ScenarioB.ApiUrl = $DatabaseApiUrl }
if ($AppServiceApiUrl)  { $Config.ScenarioC.ApiUrl = $AppServiceApiUrl; $Config.ScenarioAppService.ApiUrl = $AppServiceApiUrl }
if ($VmssApiUrl)        { $Config.ScenarioVMSS.ApiUrl = $VmssApiUrl }

# ============================================================================
# TLS setup - VM/VMSS use a self-signed cert (Nginx); disable validation like scaleTriggerLoad.py does
# ============================================================================

$script:authTokens = @{}

if ($PSVersionTable.PSVersion.Major -lt 6) {
    if (-not ([System.Management.Automation.PSTypeName]'ScaleTriggerCertBypass').Type) {
        Add-Type @"
using System.Net;
using System.Security.Cryptography.X509Certificates;
public class ScaleTriggerCertBypass : ICertificatePolicy {
    public bool CheckValidationResult(ServicePoint sp, X509Certificate cert, WebRequest req, int problem) { return true; }
}
"@
    }
    [System.Net.ServicePointManager]::CertificatePolicy = New-Object ScaleTriggerCertBypass
    $script:restMethodCertArgs = @{}
} else {
    $script:restMethodCertArgs = @{ SkipCertificateCheck = $true }
}

# ============================================================================
# HELPER FUNCTIONS
# ============================================================================

function Resolve-DeployedResources {
    <#
        Fills in unset placeholder values by querying what's actually deployed, scoped to
        $NeededScenarios; anything it can't find is left as-is with a Write-Warning.
    #>
    param([string[]]$NeededScenarios)

    function Get-PublicEndpointUrl {
        param([string]$ResourceGroupName)
        $pip = Get-AzPublicIpAddress -ResourceGroupName $ResourceGroupName -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $pip) { return $null }
        $hostName = if ($pip.DnsSettings -and $pip.DnsSettings.Fqdn) { $pip.DnsSettings.Fqdn } else { $pip.IpAddress }
        if (-not $hostName) { return $null }
        return "https://$hostName"
    }

    if (($NeededScenarios -contains 'A') -and ($Config.ScenarioA.ApiUrl -match '<')) {
        $url = Get-PublicEndpointUrl -ResourceGroupName $Config.ScenarioA.ResourceGroup
        if ($url) {
            $Config.ScenarioA.ApiUrl = $url
            Write-Host "Auto-detected VM URL: $url" -ForegroundColor DarkGray
        } else {
            Write-Warning "Could not auto-detect the VM's public IP in '$($Config.ScenarioA.ResourceGroup)' - pass -VmApiUrl."
        }
    }

    if (($NeededScenarios -contains 'VMSS') -and ($Config.ScenarioVMSS.ApiUrl -match '<')) {
        $url = Get-PublicEndpointUrl -ResourceGroupName $Config.ScenarioVMSS.ResourceGroup
        if ($url) {
            $Config.ScenarioVMSS.ApiUrl = $url
            Write-Host "Auto-detected VMSS URL: $url" -ForegroundColor DarkGray
        } else {
            Write-Warning "Could not auto-detect the Scale Set's load balancer public IP in '$($Config.ScenarioVMSS.ResourceGroup)' - pass -VmssApiUrl."
        }
    }

    # Scenario B has no compute of its own - it reuses the App Service URL (VM uses local SQLite).
    $needsAppService = ($NeededScenarios -contains 'C') -or ($NeededScenarios -contains 'AppService') -or
        (($NeededScenarios -contains 'B') -and ($Config.ScenarioB.ApiUrl -match '<'))
    if ($needsAppService) {
        $appServiceRg = $Config.ScenarioC.ResourceGroup
        $webApp = Get-AzWebApp -ResourceGroupName $appServiceRg -ErrorAction SilentlyContinue | Select-Object -First 1
        $appServiceUrl = if ($webApp -and $webApp.DefaultHostName) { "https://$($webApp.DefaultHostName)" } else { $null }
        $plan = Get-AzAppServicePlan -ResourceGroupName $appServiceRg -ErrorAction SilentlyContinue | Select-Object -First 1

        if ($appServiceUrl) {
            if ($Config.ScenarioC.ApiUrl -match '<') { $Config.ScenarioC.ApiUrl = $appServiceUrl }
            if ($Config.ScenarioAppService.ApiUrl -match '<') { $Config.ScenarioAppService.ApiUrl = $appServiceUrl }
            if ($Config.ScenarioB.ApiUrl -match '<') { $Config.ScenarioB.ApiUrl = $appServiceUrl }
            Write-Host "Auto-detected App Service URL: $appServiceUrl" -ForegroundColor DarkGray
        } else {
            Write-Warning "Could not auto-detect the Web App in '$appServiceRg' - pass -AppServiceApiUrl/-DatabaseApiUrl."
        }

        if ($plan -and $plan.Name) {
            if ($Config.ScenarioC.AppServicePlan -match '<') { $Config.ScenarioC.AppServicePlan = $plan.Name }
            if ($Config.ScenarioAppService.AppServicePlan -match '<') { $Config.ScenarioAppService.AppServicePlan = $plan.Name }
            Write-Host "Auto-detected App Service Plan name: $($plan.Name)" -ForegroundColor DarkGray
        } else {
            Write-Warning "Could not auto-detect the App Service Plan name in '$appServiceRg'."
        }
    }

    if (($NeededScenarios -contains 'B') -and ($Config.ScenarioB.ServerName -match '<')) {
        $server = Get-AzSqlServer -ResourceGroupName $Config.ScenarioB.ResourceGroup -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($server -and $server.ServerName) {
            $Config.ScenarioB.ServerName = $server.ServerName
            Write-Host "Auto-detected SQL Server name: $($server.ServerName)" -ForegroundColor DarkGray
        } else {
            Write-Warning "Could not auto-detect the SQL Server name in '$($Config.ScenarioB.ResourceGroup)'."
        }
    }

    if ($Config.LogAnalyticsWorkspaceId -match '<') {
        $logsRg = "$ResourceGroupPrefix-Logs"
        $workspace = Get-AzOperationalInsightsWorkspace -ResourceGroupName $logsRg -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($workspace -and $workspace.CustomerId) {
            $Config.LogAnalyticsWorkspaceId = $workspace.CustomerId.ToString()
            Write-Host "Auto-detected Log Analytics workspace ID." -ForegroundColor DarkGray
        } else {
            Write-Warning "Could not auto-detect the Log Analytics workspace in '$logsRg'."
        }
    }
}

function Assert-AzConnection {
    if ($AzContextPath -and (Test-Path $AzContextPath)) {
        # Running inside a background job (scenario C) - reuses the login saved by the
        # foreground process, instead of an interactive Connect-AzAccount.
        Import-AzContext -Path $AzContextPath | Out-Null
        return
    }

    $context = Get-AzContext
    if (-not $context) {
        Write-Host "No active Azure login. Running Connect-AzAccount..." -ForegroundColor Yellow
        Connect-AzAccount | Out-Null
    }
    if ($SubscriptionId) {
        Set-AzContext -SubscriptionId $SubscriptionId | Out-Null
    }

    # Resolve once the Az context is established: -SubscriptionId if given, else whatever's active.
    $Config.SubscriptionId = if ($SubscriptionId) { $SubscriptionId } else { (Get-AzContext).Subscription.Id }
}

function Get-ScaleTriggerAuthToken {
    param([Parameter(Mandatory)] [string]$ApiUrl)

    if (-not $AdminPassword) { return $null }

    try {
        $body = @{ username = $AdminUsername; password = $AdminPassword } | ConvertTo-Json
        $response = Invoke-RestMethod -Method Post -Uri "$($ApiUrl.TrimEnd('/'))/api/auth/login" `
            -Body $body -ContentType 'application/json' -ErrorAction Stop @script:restMethodCertArgs
        return $response.token
    }
    catch {
        Write-Warning "  Login to $ApiUrl failed - $($_.Exception.Message)"
        return $null
    }
}

function Invoke-ScaleTriggerApi {
    <#
        POSTs to a ScaleTrigger endpoint; logs in and retries once on 401, caching the
        token per ApiUrl. Returns Success=$false (never throws) so callers can skip gracefully.
    #>
    param(
        [Parameter(Mandatory)] [string]$ApiUrl,
        [Parameter(Mandatory)] [string]$RoutePath,
        [string]$BodyJson = $null
    )

    $uri = "$($ApiUrl.TrimEnd('/'))$RoutePath"
    $headers = @{}
    if ($script:authTokens.ContainsKey($ApiUrl)) {
        $headers['Authorization'] = "Bearer $($script:authTokens[$ApiUrl])"
    }

    try {
        $params = @{ Method = 'Post'; Uri = $uri; Headers = $headers; ErrorAction = 'Stop' } + $script:restMethodCertArgs
        if ($BodyJson) { $params['Body'] = $BodyJson; $params['ContentType'] = 'application/json' }
        $data = Invoke-RestMethod @params
        return [PSCustomObject]@{ Success = $true; Data = $data }
    }
    catch {
        $statusCode = $null
        if ($_.Exception.Response) {
            try { $statusCode = [int]$_.Exception.Response.StatusCode } catch {}
        }
        if ($statusCode -eq 401 -and -not $script:authTokens.ContainsKey($ApiUrl)) {
            $token = Get-ScaleTriggerAuthToken -ApiUrl $ApiUrl
            if ($token) {
                $script:authTokens[$ApiUrl] = $token
                return Invoke-ScaleTriggerApi -ApiUrl $ApiUrl -RoutePath $RoutePath -BodyJson $BodyJson
            }
            Write-Warning "  $uri requires authentication and no -AdminPassword was given - skipping."
        } else {
            Write-Warning "  Request to $uri failed - $($_.Exception.Message)"
        }
        return [PSCustomObject]@{ Success = $false; Data = $null }
    }
}

function Set-RecommendedCpuLoad {
    <#
        Benchmarks the node and pushes a calibrated CpuIterationsPerVote via /api/loadconfig
        (dashboard's "CPU calibration" formula). Non-fatal - warns and leaves it as-is on failure.
    #>
    param(
        [Parameter(Mandatory)] [string]$ApiUrl
    )

    Write-Host "Benchmarking node at $ApiUrl (target: $BenchmarkTargetVotes votes)..." -ForegroundColor Cyan

    $benchmark = Invoke-ScaleTriggerApi -ApiUrl $ApiUrl -RoutePath '/api/nodebenchmark/run'
    if (-not $benchmark.Success -or -not $benchmark.Data.cpuNumbersPerSecond) {
        Write-Warning "  Benchmark failed or returned no CPU score - leaving CpuIterationsPerVote as already configured."
        return
    }

    $cpuScore = $benchmark.Data.cpuNumbersPerSecond
    $ceiling = [math]::Round(0.8 * ($cpuScore / $BenchmarkTargetVotes))
    $floor = [math]::Round(0.5 * $ceiling)
    Write-Host "  CPU score: $([math]::Round($cpuScore)) hashes/sec -> recommended CpuIterationsPerVote $floor-$ceiling" -ForegroundColor Cyan

    $body = ConvertTo-Json -InputObject @(@{ settingName = 'CpuIterationsPerVote'; min = $floor; max = $ceiling })
    $result = Invoke-ScaleTriggerApi -ApiUrl $ApiUrl -RoutePath '/api/loadconfig' -BodyJson $body
    if ($result.Success) {
        Write-Host "  CpuIterationsPerVote set to $floor-$ceiling." -ForegroundColor Green
    }
}

function Set-FixedLoadSetting {
    <# Pushes a fixed Min/Max range for one LoadConfig setting via /api/loadconfig - no benchmark involved. #>
    param(
        [Parameter(Mandatory)] [string]$ApiUrl,
        [Parameter(Mandatory)] [string]$SettingName,
        [Parameter(Mandatory)] [int]$MinValue,
        [Parameter(Mandatory)] [int]$MaxValue
    )
    Write-Host "Setting $SettingName to $MinValue-$MaxValue at $ApiUrl..." -ForegroundColor Cyan
    $body = ConvertTo-Json -InputObject @(@{ settingName = $SettingName; min = $MinValue; max = $MaxValue })
    $result = Invoke-ScaleTriggerApi -ApiUrl $ApiUrl -RoutePath '/api/loadconfig' -BodyJson $body
    if ($result.Success) {
        Write-Host "  $SettingName set to $MinValue-$MaxValue." -ForegroundColor Green
    }
}

function Start-LoadGenerator {
    <# scaleTriggerLoad.py defaults to admin:admin, which never matches this demo - always pass the real ones. #>
    param(
        [Parameter(Mandatory)] [string]$ApiUrl,
        [Parameter(Mandatory)] [string]$LoadArgsString
    )
    $logFile = Join-Path $Path ("load_" + (Get-Date -Format "yyyyMMdd_HHmmss") + ".log")
    $credentialArgs = "--username `"$AdminUsername`" --password `"$AdminPassword`""
    $argumentList = "`"$LoadScriptPath`" --url `"$ApiUrl`" $LoadArgsString $credentialArgs"
    $displayArgumentList = "`"$LoadScriptPath`" --url `"$ApiUrl`" $LoadArgsString --username `"$AdminUsername`" --password ********"
    Write-Host "Starting load generator: $PythonExe $displayArgumentList" -ForegroundColor Cyan
    $process = Start-Process -FilePath $PythonExe -ArgumentList $argumentList `
        -RedirectStandardOutput $logFile -PassThru -NoNewWindow
    return [PSCustomObject]@{
        Process   = $process
        LogFile   = $logFile
        StartTime = Get-Date
    }
}

function Wait-ForMetricThreshold {
    <# Polls the metric until it crosses the threshold; returns the crossing timestamp. #>
    param(
        [Parameter(Mandatory)] [string]$ResourceId,
        [Parameter(Mandatory)] [string]$MetricName,
        [Parameter(Mandatory)] [string]$MetricNamespace,
        [double]$ThresholdValue = 80,
        [int]$PollSeconds = 15,
        [int]$TimeoutMinutes = 30
    )
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    Write-Host "Monitoring metric '$MetricName' on resource $ResourceId (threshold: $ThresholdValue)..." -ForegroundColor Cyan

    while ((Get-Date) -lt $deadline) {
        $metric = Get-AzMetric -ResourceId $ResourceId -MetricName $MetricName -MetricNamespace $MetricNamespace `
            -TimeGrain 00:01:00 -AggregationType Average `
            -StartTime (Get-Date).AddMinutes(-5) -EndTime (Get-Date) -WarningAction SilentlyContinue

        $lastPoint = $metric.Data | Where-Object { $null -ne $_.Average } |
            Sort-Object Timestamp -Descending | Select-Object -First 1

        if ($lastPoint -and $lastPoint.Average -ge $ThresholdValue) {
            Write-Host "Threshold crossed at $($lastPoint.Timestamp.ToUniversalTime()) (value: $([math]::Round($lastPoint.Average,2)))" -ForegroundColor Green
            return $lastPoint.Timestamp.ToUniversalTime()
        }

        Start-Sleep -Seconds $PollSeconds
    }

    Write-Warning "Threshold not reached within $TimeoutMinutes minutes. Check the load profile or increase -TimeoutMinutes."
    return $null
}

function Get-ScalingActivityLog {
    <# Runs the chapter 7.2.2 KQL query against AzureActivity for scaling/resize ops in the window. #>
    param(
        [Parameter(Mandatory)] [string]$ResourceId,
        [Parameter(Mandatory)] [datetime]$FromUtc,
        [datetime]$ToUtc = (Get-Date).ToUniversalTime()
    )
    $fromString = $FromUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")
    $toString   = $ToUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")

    $query = @"
AzureActivity
| where ResourceId == "$ResourceId"
| where OperationNameValue has "scale" or OperationNameValue has "resize" or OperationNameValue has "write"
| where TimeGenerated between (datetime($fromString) .. datetime($toString))
| project TimeGenerated, OperationNameValue, ActivityStatusValue, Caller
| order by TimeGenerated asc
"@

    $result = Invoke-AzOperationalInsightsQuery -WorkspaceId $Config.LogAnalyticsWorkspaceId -Query $query
    return $result.Results
}

function Wait-ForLogicAppRun {
    <# Waits for a new Logic App run (automatic for A, manually triggered for C); returns the run object. #>
    param(
        [Parameter(Mandatory)] [string]$ResourceGroup,
        [Parameter(Mandatory)] [string]$LogicAppName,
        [datetime]$AfterUtc,
        [int]$PollSeconds = 10,
        [int]$TimeoutMinutes = 60
    )
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    Write-Host "Waiting for a new run of Logic App '$LogicAppName' (after $AfterUtc)..." -ForegroundColor Cyan

    while ((Get-Date) -lt $deadline) {
        $runs = Get-AzLogicAppRunHistory -ResourceGroupName $ResourceGroup -Name $LogicAppName |
            Sort-Object StartTime -Descending

        $newRun = $runs | Where-Object { $_.StartTime.ToUniversalTime() -gt $AfterUtc } |
            Select-Object -First 1

        if ($newRun) {
            Write-Host "Logic App run found: start $($newRun.StartTime), status $($newRun.Status)" -ForegroundColor Green
            return $newRun
        }

        Start-Sleep -Seconds $PollSeconds
    }

    Write-Warning "No new Logic App run detected within $TimeoutMinutes minutes."
    return $null
}

function Export-ScenarioResult {
    param(
        [Parameter(Mandatory)] [string]$ScenarioName,
        [Parameter(Mandatory)] [hashtable]$Data
    )
    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Path $Path | Out-Null
    }
    $file = Join-Path $Path "$ScenarioName.csv"
    [PSCustomObject]$Data | Export-Csv -Path $file -NoTypeInformation -Encoding UTF8
    Write-Host "Scenario '$ScenarioName' results saved to $file" -ForegroundColor Green
    [PSCustomObject]$Data | Format-List
}

# ============================================================================
# SCENARIO A - VIRTUAL MACHINE (fully automatic, chapter 7.3.2)
# ============================================================================

function Invoke-ScenarioA {
    $cfg = $Config.ScenarioA
    $resourceId = "/subscriptions/$($Config.SubscriptionId)/resourceGroups/$($cfg.ResourceGroup)" +
                  "/providers/Microsoft.Compute/virtualMachines/$($cfg.VMName)"

    $vmBefore = Get-AzVM -ResourceGroupName $cfg.ResourceGroup -Name $cfg.VMName
    $startingSize = $vmBefore.HardwareProfile.VmSize

    Set-RecommendedCpuLoad -ApiUrl $cfg.ApiUrl
    $load = Start-LoadGenerator -ApiUrl $cfg.ApiUrl -LoadArgsString $cfg.LoadArgs
    $thresholdTime = Wait-ForMetricThreshold -ResourceId $resourceId -MetricName $cfg.MetricName -MetricNamespace $cfg.MetricNamespace `
        -ThresholdValue $cfg.ThresholdValue -TimeoutMinutes 25

    $alarmRun = $null
    if ($thresholdTime) {
        $alarmRun = Wait-ForLogicAppRun -ResourceGroup $cfg.ResourceGroup -LogicAppName $cfg.LogicAppName `
            -AfterUtc $thresholdTime -TimeoutMinutes 20
    }

    Stop-Process -Id $load.Process.Id -Force -ErrorAction SilentlyContinue

    $activity = if ($thresholdTime) {
        Get-ScalingActivityLog -ResourceId $resourceId -FromUtc $thresholdTime.AddMinutes(-1)
    } else { $null }

    $vmAfter = Get-AzVM -ResourceGroupName $cfg.ResourceGroup -Name $cfg.VMName
    $endingSize = $vmAfter.HardwareProfile.VmSize

    $completionTime = if ($alarmRun) { $alarmRun.EndTime } else { $null }
    $totalSeconds = if ($thresholdTime -and $completionTime) {
        [math]::Round(($completionTime - $thresholdTime).TotalSeconds, 0)
    } else { $null }

    Export-ScenarioResult -ScenarioName "ScenarioA_VirtualMachine" -Data @{
        ResourceGroup             = $cfg.ResourceGroup
        StartingInstanceSize      = $startingSize
        TargetInstanceSize        = $endingSize
        ThresholdCrossedTime      = $thresholdTime
        AlarmTriggeredTime        = if ($alarmRun) { $alarmRun.StartTime } else { $null }
        OperationCompletedTime    = $completionTime
        TotalScalingTimeSeconds   = $totalSeconds
        LoadGeneratorLogFile      = $load.LogFile
    }
}

# ============================================================================
# SCENARIO B - AZURE SQL SERVERLESS (built into the platform, chapter 7.3.3)
# ============================================================================

function Invoke-ScenarioB {
    $cfg = $Config.ScenarioB
    $resourceId = "/subscriptions/$($Config.SubscriptionId)/resourceGroups/$($cfg.ResourceGroup)" +
                  "/providers/Microsoft.Sql/servers/$($cfg.ServerName)/databases/$($cfg.DatabaseName)"

    Set-FixedLoadSetting -ApiUrl $cfg.ApiUrl -SettingName 'CpuIterationsPerVote' -MinValue 0 -MaxValue 0
    Set-FixedLoadSetting -ApiUrl $cfg.ApiUrl -SettingName 'DbCpuIterationsPerVote' -MinValue $DbCpuIterationsMin -MaxValue $DbCpuIterationsMax
    $load = Start-LoadGenerator -ApiUrl $cfg.ApiUrl -LoadArgsString $cfg.LoadArgs
    $startUtc = (Get-Date).ToUniversalTime()

    Write-Host "Collecting the cpu_percent time series for 20 minutes to plot the scale-up curve..." -ForegroundColor Cyan
    Start-Sleep -Seconds 1200

    Stop-Process -Id $load.Process.Id -Force -ErrorAction SilentlyContinue
    Set-FixedLoadSetting -ApiUrl $cfg.ApiUrl -SettingName 'DbCpuIterationsPerVote' -MinValue 0 -MaxValue 0
    $endUtc = (Get-Date).ToUniversalTime()

    $series = Get-AzMetric -ResourceId $resourceId -MetricName $cfg.MetricName -MetricNamespace $cfg.MetricNamespace `
        -TimeGrain 00:01:00 -AggregationType Average -StartTime $startUtc -EndTime $endUtc

    $seriesFile = Join-Path $Path "ScenarioB_cpu_percent_series.csv"
    $series.Data | Select-Object Timestamp, Average | Export-Csv -Path $seriesFile -NoTypeInformation -Encoding UTF8

    $peak = $series.Data | Sort-Object Average -Descending | Select-Object -First 1
    $firstAboveHalf = $series.Data | Where-Object { $_.Average -ge 50 } | Sort-Object Timestamp | Select-Object -First 1

    Export-ScenarioResult -ScenarioName "ScenarioB_AzureSQL" -Data @{
        ResourceGroup       = $cfg.ResourceGroup
        DatabaseServerName  = "$($cfg.ServerName) / $($cfg.DatabaseName)"
        TestStartTime       = $startUtc
        TestEndTime         = $endUtc
        Crossed50PercentTime = if ($firstAboveHalf) { $firstAboveHalf.Timestamp } else { $null }
        PeakCpuValue        = if ($peak) { [math]::Round($peak.Average, 2) } else { $null }
        PeakTime            = if ($peak) { $peak.Timestamp } else { $null }
        TimeSeriesFile      = $seriesFile
        Note                = "Assess the curve shape and the auto-pause wake-up latency manually from the attached CSV series."
    }
}

# ============================================================================
# SCENARIO C - APP SERVICE PLAN WITH HUMAN APPROVAL (chapter 7.3.4)
# ============================================================================

function Invoke-ScenarioC {
    $cfg = $Config.ScenarioC
    $resourceId = "/subscriptions/$($Config.SubscriptionId)/resourceGroups/$($cfg.ResourceGroup)" +
                  "/providers/Microsoft.Web/serverfarms/$($cfg.AppServicePlan)"

    $planBefore = Get-AzAppServicePlan -ResourceGroupName $cfg.ResourceGroup -Name $cfg.AppServicePlan
    $startingTier = $planBefore.Sku.Name

    Set-RecommendedCpuLoad -ApiUrl $cfg.ApiUrl
    $load = Start-LoadGenerator -ApiUrl $cfg.ApiUrl -LoadArgsString $cfg.LoadArgs
    $thresholdTime = Wait-ForMetricThreshold -ResourceId $resourceId -MetricName $cfg.MetricName -MetricNamespace $cfg.MetricNamespace `
        -ThresholdValue $cfg.ThresholdValue -TimeoutMinutes 40

    Write-Host "" -ForegroundColor Yellow
    Write-Host "The alert should send a push notification. Manually trigger the Logic App '$($cfg.LogicAppName)' in the Azure Portal ('Run Trigger' button) once you receive the notification." -ForegroundColor Yellow
    Write-Host "The script is waiting to detect the Logic App run..." -ForegroundColor Yellow

    $alarmRun = $null
    if ($thresholdTime) {
        $alarmRun = Wait-ForLogicAppRun -ResourceGroup $cfg.ResourceGroup -LogicAppName $cfg.LogicAppName `
            -AfterUtc $thresholdTime -TimeoutMinutes $ScenarioCTimeoutMinutes
    }

    Stop-Process -Id $load.Process.Id -Force -ErrorAction SilentlyContinue

    $planAfter = Get-AzAppServicePlan -ResourceGroupName $cfg.ResourceGroup -Name $cfg.AppServicePlan
    $endingTier = $planAfter.Sku.Name

    $reactionSeconds = if ($thresholdTime -and $alarmRun) {
        [math]::Round(($alarmRun.StartTime.ToUniversalTime() - $thresholdTime).TotalSeconds, 0)
    } else { $null }
    $totalSeconds = if ($thresholdTime -and $alarmRun) {
        [math]::Round(($alarmRun.EndTime.ToUniversalTime() - $thresholdTime).TotalSeconds, 0)
    } else { $null }

    Export-ScenarioResult -ScenarioName "ScenarioC_AppServicePlan" -Data @{
        ResourceGroup               = $cfg.ResourceGroup
        StartingPlanTier            = $startingTier
        TargetPlanTier              = $endingTier
        ThresholdCrossedTime        = $thresholdTime
        ManualLogicAppTriggerTime   = if ($alarmRun) { $alarmRun.StartTime } else { $null }
        HumanReactionTimeSeconds    = $reactionSeconds
        OperationCompletedTime      = if ($alarmRun) { $alarmRun.EndTime } else { $null }
        TotalScalingTimeSeconds     = $totalSeconds
        Note                        = "The moment the push notification arrives on the device can't be retrieved via API - record it manually."
    }
}

# ============================================================================
# HORIZONTAL SCALING - VMSS (chapter 7.4)
# ============================================================================

function Invoke-ScenarioVMSS {
    $cfg = $Config.ScenarioVMSS
    $resourceId = "/subscriptions/$($Config.SubscriptionId)/resourceGroups/$($cfg.ResourceGroup)" +
                  "/providers/Microsoft.Compute/virtualMachineScaleSets/$($cfg.VMSSName)"

    $vmssBefore = Get-AzVmss -ResourceGroupName $cfg.ResourceGroup -VMScaleSetName $cfg.VMSSName
    $startingCapacity = $vmssBefore.Sku.Capacity

    Set-RecommendedCpuLoad -ApiUrl $cfg.ApiUrl
    $load = Start-LoadGenerator -ApiUrl $cfg.ApiUrl -LoadArgsString $cfg.LoadArgs
    $startUtc = (Get-Date).ToUniversalTime()

    $capacityIncreaseTime = $null
    $deadline = (Get-Date).AddMinutes(20)
    while ((Get-Date) -lt $deadline) {
        $current = Get-AzVmss -ResourceGroupName $cfg.ResourceGroup -VMScaleSetName $cfg.VMSSName
        if ($current.Sku.Capacity -gt $startingCapacity) {
            $capacityIncreaseTime = (Get-Date).ToUniversalTime()
            Write-Host "Instance count increased from $startingCapacity to $($current.Sku.Capacity) at $capacityIncreaseTime" -ForegroundColor Green
            break
        }
        Start-Sleep -Seconds 15
    }

    Stop-Process -Id $load.Process.Id -Force -ErrorAction SilentlyContinue

    $activity = if ($capacityIncreaseTime) {
        Get-ScalingActivityLog -ResourceId $resourceId -FromUtc $startUtc -ToUtc $capacityIncreaseTime
    } else { $null }

    $vmssFinal = Get-AzVmss -ResourceGroupName $cfg.ResourceGroup -VMScaleSetName $cfg.VMSSName

    Export-ScenarioResult -ScenarioName "ScenarioVMSS_Horizontal" -Data @{
        ResourceGroup               = $cfg.ResourceGroup
        StartingInstanceCount       = $startingCapacity
        FirstScaleOutTime           = $capacityIncreaseTime
        TimeToNewInstanceSeconds    = if ($capacityIncreaseTime) { [math]::Round(($capacityIncreaseTime - $startUtc).TotalSeconds, 0) } else { $null }
        FinalInstanceCount          = $vmssFinal.Sku.Capacity
        LoadGeneratorLogFile        = $load.LogFile
        Note                        = "Read the achieved request rate before/after from LoadGeneratorLogFile (the --report output)."
    }
}

# ============================================================================
# HORIZONTAL SCALING - APP SERVICE PLAN (chapter 7.5)
# ============================================================================

function Invoke-ScenarioAppService {
    $cfg = $Config.ScenarioAppService
    $resourceId = "/subscriptions/$($Config.SubscriptionId)/resourceGroups/$($cfg.ResourceGroup)" +
                  "/providers/Microsoft.Web/serverfarms/$($cfg.AppServicePlan)"

    $planBefore = Get-AzAppServicePlan -ResourceGroupName $cfg.ResourceGroup -Name $cfg.AppServicePlan
    $startingCapacity = $planBefore.Sku.Capacity

    Set-RecommendedCpuLoad -ApiUrl $cfg.ApiUrl
    $load = Start-LoadGenerator -ApiUrl $cfg.ApiUrl -LoadArgsString $cfg.LoadArgs
    $startUtc = (Get-Date).ToUniversalTime()

    $capacityIncreaseTime = $null
    $deadline = (Get-Date).AddMinutes(20)
    while ((Get-Date) -lt $deadline) {
        $current = Get-AzAppServicePlan -ResourceGroupName $cfg.ResourceGroup -Name $cfg.AppServicePlan
        if ($current.Sku.Capacity -gt $startingCapacity) {
            $capacityIncreaseTime = (Get-Date).ToUniversalTime()
            Write-Host "Instance count increased from $startingCapacity to $($current.Sku.Capacity) at $capacityIncreaseTime" -ForegroundColor Green
            break
        }
        Start-Sleep -Seconds 15
    }

    Stop-Process -Id $load.Process.Id -Force -ErrorAction SilentlyContinue

    $planFinal = Get-AzAppServicePlan -ResourceGroupName $cfg.ResourceGroup -Name $cfg.AppServicePlan

    Export-ScenarioResult -ScenarioName "ScenarioAppService_Horizontal" -Data @{
        ResourceGroup               = $cfg.ResourceGroup
        StartingInstanceCount       = $startingCapacity
        FirstScaleOutTime           = $capacityIncreaseTime
        TimeToNewInstanceSeconds    = if ($capacityIncreaseTime) { [math]::Round(($capacityIncreaseTime - $startUtc).TotalSeconds, 0) } else { $null }
        FinalInstanceCount          = $planFinal.Sku.Capacity
        LoadGeneratorLogFile        = $load.LogFile
        Note                        = "Read the achieved request rate before/after from LoadGeneratorLogFile (the --report output)."
    }
}

# ============================================================================
# HTML REPORT - ASSEMBLES ALL CSV RESULTS INTO ONE READABLE DOCUMENT
# ============================================================================

function ConvertTo-HtmlDataTable {
    <# Converts a CSV into an HTML table; prints a note instead if the file is missing/empty. #>
    param(
        [Parameter(Mandatory)] [string]$Title,
        [Parameter(Mandatory)] [string]$CsvPath
    )

    if (-not (Test-Path $CsvPath)) {
        return @"
<section class="scenario">
  <h2>$Title</h2>
  <p class="missing">Result not found ($([System.IO.Path]::GetFileName($CsvPath))) - the scenario probably hasn't been run yet.</p>
</section>
"@
    }

    $rows = Import-Csv -Path $CsvPath
    if (-not $rows) {
        return @"
<section class="scenario">
  <h2>$Title</h2>
  <p class="missing">File $([System.IO.Path]::GetFileName($CsvPath)) is empty.</p>
</section>
"@
    }

    $properties = $rows[0].PSObject.Properties.Name
    $headerHtml = ($properties | ForEach-Object { "<th>$_</th>" }) -join ""

    $bodyHtml = ($rows | ForEach-Object {
        $row = $_
        $cells = ($properties | ForEach-Object {
            $value = $row.$_
            if ([string]::IsNullOrWhiteSpace($value)) { $value = "-" }
            "<td>$value</td>"
        }) -join ""
        "<tr>$cells</tr>"
    }) -join "`n"

    return @"
<section class="scenario">
  <h2>$Title</h2>
  <table>
    <thead><tr>$headerHtml</tr></thead>
    <tbody>
$bodyHtml
    </tbody>
  </table>
  <p class="source">Source: $([System.IO.Path]::GetFileName($CsvPath))</p>
</section>
"@
}

function New-HtmlScalingReport {
    param(
        [Parameter(Mandatory)] [string]$Path
    )

    $reportSections = @(
        ConvertTo-HtmlDataTable -Title "Scenario A - Vertical scaling, virtual machine" `
            -CsvPath (Join-Path $Path "ScenarioA_VirtualMachine.csv")
        ConvertTo-HtmlDataTable -Title "Scenario B - Vertical scaling, Azure SQL Serverless" `
            -CsvPath (Join-Path $Path "ScenarioB_AzureSQL.csv")
        ConvertTo-HtmlDataTable -Title "Scenario C - Vertical scaling, App Service plan (with human approval)" `
            -CsvPath (Join-Path $Path "ScenarioC_AppServicePlan.csv")
        ConvertTo-HtmlDataTable -Title "Horizontal scaling - Virtual Machine Scale Set" `
            -CsvPath (Join-Path $Path "ScenarioVMSS_Horizontal.csv")
        ConvertTo-HtmlDataTable -Title "Horizontal scaling - Azure App Service plan" `
            -CsvPath (Join-Path $Path "ScenarioAppService_Horizontal.csv")
    ) -join "`n"

    $generatedAt = Get-Date -Format "dd.MM.yyyy HH:mm"

    $html = @"
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<title>Scaling scenario execution report</title>
<style>
  body { font-family: Segoe UI, Arial, sans-serif; margin: 2rem auto; max-width: 900px; color: #1f2933; line-height: 1.5; }
  h1 { border-bottom: 3px solid #0f6cbd; padding-bottom: 0.5rem; }
  h2 { color: #0f6cbd; margin-top: 2.5rem; }
  table { border-collapse: collapse; width: 100%; margin-top: 0.75rem; font-size: 0.92rem; }
  th, td { border: 1px solid #d1d9e0; padding: 0.5rem 0.7rem; text-align: left; }
  th { background: #eef4fb; }
  tr:nth-child(even) td { background: #fafbfc; }
  .missing { color: #b3261e; font-style: italic; }
  .source { color: #6b7580; font-size: 0.8rem; margin-top: 0.3rem; }
  .meta { color: #6b7580; font-size: 0.9rem; }
  section.scenario { margin-bottom: 1.5rem; }
</style>
</head>
<body>
  <h1>Scaling scenario execution report</h1>
  <p class="meta">Scaling applications in the Microsoft Azure cloud - practical part of the paper (chapter 7)<br>
  Report generated: $generatedAt</p>

$reportSections

</body>
</html>
"@

    $reportPath = Join-Path $Path "report.html"
    $html | Out-File -FilePath $reportPath -Encoding UTF8
    Write-Host "`nHTML report saved to: $reportPath" -ForegroundColor Green
    return $reportPath
}

# ============================================================================
# MAIN FLOW - RUNNING SCENARIOS ONE AT A TIME
# ============================================================================

if (-not (Test-Path $Path)) {
    New-Item -ItemType Directory -Path $Path | Out-Null
}

if ($Scenario -ne 'Report') {

    Assert-AzConnection

    $scenariosToRun = if ($Scenario -eq 'All') { @('A', 'B', 'C', 'VMSS', 'AppService') } else { @($Scenario) }

    Resolve-DeployedResources -NeededScenarios $scenariosToRun

    # Scenario C waits on manual approval, so it's split off into a background job when running multiple.
    $scenarioCJob = $null
    if ($scenariosToRun -contains 'C' -and $scenariosToRun.Count -gt 1) {

        $contextFile = Join-Path $Path "azcontext.json"
        Save-AzContext -Path $contextFile -Force | Out-Null

        Write-Host "`nStarting scenario C (human approval) in the background - not waiting here." -ForegroundColor Cyan
        $scenarioCJob = Start-Job -Name "ScenarioC" -ScriptBlock {
            param($ScriptPath, $RunPath, $PyExe, $LoadScript, $ContextFile, $TimeoutMinutes, $RgPrefix, $ResPrefix, $SubId, $AppUrl, $AdminUser, $AdminPass)
            $childArgs = @{
                Scenario                = 'C'
                Path                    = $RunPath
                PythonExe               = $PyExe
                LoadScriptPath          = $LoadScript
                AzContextPath           = $ContextFile
                ScenarioCTimeoutMinutes = $TimeoutMinutes
                SkipHtmlReport          = $true
                ResourceGroupPrefix     = $RgPrefix
                ResourcePrefix          = $ResPrefix
                SubscriptionId          = $SubId
                AppServiceApiUrl        = $AppUrl
                AdminUsername           = $AdminUser
                AdminPassword           = $AdminPass
            }
            & $ScriptPath @childArgs
        } -ArgumentList $PSCommandPath, $Path, $PythonExe, $LoadScriptPath, $contextFile, $ScenarioCTimeoutMinutes, `
            $ResourceGroupPrefix, $ResourcePrefix, $SubscriptionId, $Config.ScenarioC.ApiUrl, $AdminUsername, $AdminPassword

        $scenariosToRun = $scenariosToRun | Where-Object { $_ -ne 'C' }
    }

    foreach ($s in $scenariosToRun) {
        Write-Host "`n=====================================================" -ForegroundColor Magenta
        Write-Host " Running scenario: $s" -ForegroundColor Magenta
        Write-Host "=====================================================`n" -ForegroundColor Magenta

        switch ($s) {
            'A'          { Invoke-ScenarioA }
            'B'          { Invoke-ScenarioB }
            'C'          { Invoke-ScenarioC }
            'VMSS'       { Invoke-ScenarioVMSS }
            'AppService' { Invoke-ScenarioAppService }
        }

        if ($scenariosToRun.Count -gt 1 -and $s -ne $scenariosToRun[-1]) {
            Write-Host "`nScenario '$s' finished. Press Enter to continue to the next scenario (or Ctrl+C to abort)..." -ForegroundColor Yellow
            Read-Host | Out-Null
        }
    }

    Write-Host "`nAll foreground scenarios are done. Result CSV files are in: $Path" -ForegroundColor Green

    if ($scenarioCJob) {
        if ($scenarioCJob.State -eq 'Completed') {
            Receive-Job -Job $scenarioCJob | Out-Null
            Write-Host "Scenario C (background job) has finished in the meantime as well." -ForegroundColor Green
        } else {
            Write-Host "`nScenario C is still waiting for your approval (manually triggering the Logic App after the push notification)." -ForegroundColor Yellow
            Write-Host "Don't close this PowerShell window until you do - the job is running in the background (Job Id $($scenarioCJob.Id))." -ForegroundColor Yellow
            Write-Host "Check its status with: Get-Job -Id $($scenarioCJob.Id) | Receive-Job -Keep" -ForegroundColor Yellow
            Write-Host "Once the job finishes, fold it into the report with: .\$(Split-Path $PSCommandPath -Leaf) -Scenario Report -Path `"$Path`"" -ForegroundColor Yellow
        }
    }
}

if (-not $SkipHtmlReport) {
    $reportPath = New-HtmlScalingReport -Path $Path
    try {
        Invoke-Item $reportPath
    } catch {
        Write-Host "Open the report manually: $reportPath" -ForegroundColor Yellow
    }
}
