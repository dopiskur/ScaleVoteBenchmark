<#
.SYNOPSIS
    Automated execution of the five scaling scenarios (A, B, C, VMSS, App Service)
    and collection of the values needed to fill in the tables in chapter 7.

.DESCRIPTION
    For each scenario, the script:
      1. benchmarks the target node (POST /api/nodebenchmark/run) and pushes a
         CpuIterationsPerVote range calibrated to its real throughput (POST
         /api/loadconfig) - the same "Run benchmark" -> "Set recommended" calculation
         the dashboard does, just automated per scenario instead of a one-time manual
         click. Scenario B (SQL) sets a fixed DbCpuIterationsPerVote range instead -
         there's no "benchmark the database" endpoint to calibrate from,
      2. starts the load generator (scaleTriggerLoad.py) using the profile from chapter 7.2.1,
      3. monitors the relevant Azure Monitor metric in the background until it crosses the
         alert threshold,
      4. once detected, queries the Log Analytics workspace (KQL, chapter 7.2.2) to retrieve
         precise timestamps for the actual scaling operation,
      5. prints the results to the screen and saves them to a CSV file ready to be copied
         into the corresponding table in the paper.

    Every value that's specific to your subscription (each scenario's URL, the two resource
    names with a random uniqueness suffix, the Log Analytics workspace ID) is auto-detected:
    the script queries the actually-deployed resources in {ResourceGroupPrefix}-SingleVM/
    -ScaleSet/-ServicePlan/-Database/-Logs directly, so a fresh deploy needs nothing more
    than -ResourceGroupPrefix/-ResourcePrefix/-SubscriptionId to just work. The -XxxApiUrl
    parameters below still work as explicit overrides for anything auto-detection gets
    wrong, or when you want to point at something other than what's currently deployed.

    Every azure-demo-resources deployment sets Auth:Enabled=true and configures the app's
    AdminUser:Username/Password from the deployment's own -AdminUsername/-AdminPassword
    (see cloud-init-scaletrigger.bicep / service-plan.bicep) - there is no case where this
    demo runs without authentication, unlike a plain ScaleTrigger deployment. -AdminPassword
    is therefore REQUIRED, same as it was at deploy time; it's used both for this script's
    own benchmark/loadconfig calls and forwarded to scaleTriggerLoad.py (--username/
    --password) so its login matches what's actually configured instead of the script's
    admin:admin fallback.

.PARAMETER Scenario
    Which scenario to run: A, B, C, VMSS, AppService, or All (in order, one at a time).
    REQUIRED - there is no default. Running the script with no parameters at all (or
    without -Scenario/-Path specifically) prints the usage block below and exits instead
    of guessing.

.PARAMETER Path
    Directory the result CSV/HTML files are saved to. REQUIRED - there is no default, so
    generated files never land somewhere unexpected.

.PARAMETER ResourceGroupPrefix
    Which deployment to auto-detect resources from - used to build every resource group
    name (e.g. "$ResourceGroupPrefix-SingleVM"). Matches -ResourceGroupPrefix in
    Deploy.ps1 and the automatic Bicep template. Default: ScaleTriggerDemo.

.PARAMETER ResourcePrefix
    Which deployment to auto-detect resources from - used to build every resource name
    (e.g. "$ResourcePrefix-vm" for the VM). Matches -ResourcePrefix in Deploy.ps1 and the
    automatic Bicep template. Default: ScaleTrigger.

.PARAMETER SubscriptionId
    Optional. If provided, switches the active Az context to this subscription before
    running. If omitted, whatever subscription is currently active in the Az context is
    used.

.PARAMETER VmApiUrl
    Overrides auto-detection for scenario A's ApiUrl (the VM's public IP or DNS name).

.PARAMETER DatabaseApiUrl
    Overrides auto-detection for scenario B's ApiUrl (the ScaleTrigger instance used for
    the SQL scenario's DB load test - the App Service by default, since the VM uses local
    SQLite and doesn't touch the shared Azure SQL database).

.PARAMETER AppServiceApiUrl
    Overrides auto-detection for scenario C's and scenario AppService's ApiUrl (both
    point at the same Web App by default, so one override covers both scenarios).

.PARAMETER VmssApiUrl
    Overrides auto-detection for scenario VMSS's ApiUrl (the Scale Set's load balancer
    public IP).

.PARAMETER AdminUsername
    Login for POST /api/auth/login - this demo always has Auth:Enabled=true, and the app's
    AdminUser:Username is set from whatever -AdminUsername was passed to Deploy.ps1/the
    automatic template at deploy time. Default: demoadmin, matching Deploy.ps1's own
    default - override this if you deployed with a different -AdminUsername.

.PARAMETER AdminPassword
    Password for the login above - whatever -AdminPassword was passed at deploy time.
    REQUIRED: Deploy.ps1 itself has no default for -AdminPassword (it always prompts),
    and every azure-demo-resources scenario always has Auth:Enabled=true, so there is no
    "no password needed" case here to fall back on. Used for this script's own benchmark/
    loadconfig calls and forwarded to scaleTriggerLoad.py's --username/--password so its
    login matches reality instead of trying the script's own admin:admin default (which
    this demo never uses).

.PARAMETER DbCpuIterationsMin
    Fixed Min for scenario B's DbCpuIterationsPerVote, pushed via /api/loadconfig before
    the load starts. Default: 5000. There's no benchmark to calibrate this from (unlike
    CpuIterationsPerVote), so it's a flat default rather than computed.

.PARAMETER DbCpuIterationsMax
    Fixed Max for scenario B's DbCpuIterationsPerVote. Default: 5000.

.PARAMETER BenchmarkTargetVotes
    Target vote count (N) for the CPU calibration formula used by scenarios A/C/VMSS/
    AppService: Max = 0.8 * (cpuNumbersPerSecond / N), Min = 0.5 * Max. Default: 100,
    same as the dashboard's own "Set recommended" button.

.PARAMETER SkipHtmlReport
    If set, skips generating the HTML report at the end.

.PARAMETER AzContextPath
    Used internally when the script runs scenario C as a background job (Import-AzContext
    instead of an interactive Connect-AzAccount). You don't need to set this manually for a
    normal run.

.PARAMETER ScenarioCTimeoutMinutes
    How long (in minutes) scenario C waits for the Logic App to be triggered manually before
    giving up. Since scenario C doesn't block the other tests (it runs in the background),
    the deadline is deliberately generous (default 240 min).

.NOTES
    SCENARIO C DOES NOT BLOCK THE OTHER TESTS

    When running multiple scenarios at once (-Scenario All), scenario C (App Service plan
    with human approval) is started as a separate background job (Start-Job) as soon as its
    turn comes up, while the foreground script immediately continues with the next scenario.
    Once the other scenarios finish, the script checks whether scenario C's job is done; if
    it's still waiting for your approval, it prints instructions and does not block - keep
    the PowerShell window open until you trigger the Logic App, then fold the result into the
    report afterward with "-Scenario Report".

    DON'T COPY THIS SCRIPT OUT OF deploy/azure-demo-resources/

    -LoadScriptPath's default is anchored to $PSScriptRoot (this file's own folder), which
    makes it independent of your current directory when you run it - but not independent of
    where the .ps1 file itself physically lives. It depends on ../../scripts/scaleTriggerLoad.py,
    a fixed relative offset that only holds true inside a clone of this repo. Run it from
    deploy/azure-demo-resources/Run-ScalingScenarios.ps1 directly; if you copy it elsewhere,
    pass -LoadScriptPath pointing at the real scaleTriggerLoad.py location explicitly.

    SELF-SIGNED CERTIFICATES

    The VM and VMSS host ScaleTrigger behind Nginx with a self-signed certificate (see main
    README). This script disables TLS certificate validation for its own HTTPS calls to
    /api/nodebenchmark/run and /api/loadconfig accordingly - same trust model
    scaleTriggerLoad.py already uses, not a new relaxation.

.EXAMPLE
    .\Run-ScalingScenarios.ps1 -Scenario All -Path .\results -AdminPassword "MyDeployPassword123!"

.EXAMPLE
    .\Run-ScalingScenarios.ps1 -Scenario A -Path .\results -AdminPassword "MyDeployPassword123!" -VmApiUrl "https://20.1.2.3"

.EXAMPLE
    .\Run-ScalingScenarios.ps1 -Scenario Report -Path .\results   # just assemble the HTML report from existing CSVs - no password needed
#>

[CmdletBinding()]
param(
    # No defaults on purpose - the script refuses to run without both explicit -Scenario
    # and -Path so generated files never land somewhere unexpected and a run never targets
    # the wrong scenario by accident. See the usage block below if either is left out.
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

    # Every azure-demo-resources scenario has Auth:Enabled=true and configures
    # AdminUser:Username/Password from these exact values at deploy time - there is no
    # "no auth needed" case here, so -AdminPassword is required (below) same as it was
    # when you ran Deploy.ps1/the automatic template.
    [string]$AdminUsername = "demoadmin",
    [string]$AdminPassword,

    [int]$DbCpuIterationsMin = 5000,
    [int]$DbCpuIterationsMax = 5000,

    # Target vote count (N) for the CPU calibration formula (main README, "CPU
    # calibration"): Max = 0.8 * (cpuNumbersPerSecond / N), Min = 0.5 * Max. Same default
    # as the dashboard's own "Set recommended" button.
    [int]$BenchmarkTargetVotes = 100,

    # Path to the Python executable and the load generator script. LoadScriptPath's default
    # is anchored to $PSScriptRoot (this script's own folder), not the caller's current
    # directory, so the script works the same regardless of where you run it from - as long
    # as the script itself hasn't been copied out of deploy/azure-demo-resources/ (see .NOTES).
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
# CONFIGURATION - resource group/name defaults, filled in further by auto-detection
# (Resolve-DeployedResources, once the Az context is established) and by the -XxxApiUrl
# overrides below.
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
        LoadArgs        = "--ramp true --votes 100 --ramp-step 25 --ramp-interval 30 --ramp-max 200 --duration 1200 --report 15"
    }

    # --- Scenario B: Azure SQL Serverless (chapter 7.3.3) ---
    ScenarioB = @{
        ResourceGroup   = "$ResourceGroupPrefix-Database"
        ServerName      = $sqlServerNamePlaceholder
        DatabaseName    = "$ResourcePrefix-sqldb"
        MetricName      = "cpu_percent"
        MetricNamespace = "Microsoft.Sql/servers/databases"
        ApiUrl          = "https://<scaletrigger-instance-url-for-db-test>"
        LoadArgs        = "--ramp true --votes 100 --ramp-step 25 --ramp-interval 30 --ramp-max 200 --duration 1200 --report 15"
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
        LoadArgs        = "--ramp true --votes 100 --ramp-step 15 --ramp-interval 60 --ramp-max 300 --duration 2400 --report 30"
    }

    # --- Horizontal scaling: VMSS (chapter 7.4) ---
    ScenarioVMSS = @{
        ResourceGroup   = "$ResourceGroupPrefix-ScaleSet"
        VMSSName        = "$ResourcePrefix-vmss"
        MetricName      = "Percentage CPU"
        MetricNamespace = "Microsoft.Compute/virtualMachineScaleSets"
        ApiUrl          = "http://<scaleset-lb-public-ip>"
        LoadArgs        = "--ramp true --votes 100 --ramp-step 30 --ramp-interval 20 --ramp-max 400 --duration 900 --report 10"
    }

    # --- Horizontal scaling: App Service plan (chapter 7.5) ---
    ScenarioAppService = @{
        ResourceGroup   = "$ResourceGroupPrefix-ServicePlan"
        AppServicePlan  = $appServicePlanNamePlaceholder
        MetricName      = "CpuPercentage"
        MetricNamespace = "Microsoft.Web/serverfarms"
        ApiUrl          = "https://<webapp-name>.azurewebsites.net"
        LoadArgs        = "--ramp true --votes 100 --ramp-step 30 --ramp-interval 20 --ramp-max 400 --duration 900 --report 10"
    }
}

# Command-line URL overrides win over auto-detection.
if ($VmApiUrl)          { $Config.ScenarioA.ApiUrl = $VmApiUrl }
if ($DatabaseApiUrl)    { $Config.ScenarioB.ApiUrl = $DatabaseApiUrl }
if ($AppServiceApiUrl)  { $Config.ScenarioC.ApiUrl = $AppServiceApiUrl; $Config.ScenarioAppService.ApiUrl = $AppServiceApiUrl }
if ($VmssApiUrl)        { $Config.ScenarioVMSS.ApiUrl = $VmssApiUrl }

# ============================================================================
# TLS setup - the VM/VMSS ScaleTrigger endpoints use a self-signed certificate (Nginx,
# see main README), so certificate validation is disabled for this script's own HTTPS
# calls, same trust model scaleTriggerLoad.py already uses. Works on both Windows
# PowerShell 5.1 and PowerShell 7+.
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
        Fills in anything still left at its placeholder value (no -XxxApiUrl override was
        given) by querying the actually-deployed resources directly - requires an
        established Az context, so this only runs for real scenario runs, never for
        -Scenario Report. Only looks up what $NeededScenarios actually requires, so
        testing one scenario doesn't require every other one to be deployed. Anything it
        can't find is left as-is and reported with Write-Warning; the scenario functions
        will then fail with a clear Azure error when they try to use it, same as before
        this existed.
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

    # Scenario B doesn't have its own compute - it drives DB load through whichever app
    # instance is actually wired to the shared Azure SQL database (the VM uses local
    # SQLite, so it's VMSS/App Service, and App Service is the simpler single-instance
    # default). So resolving the App Service also covers scenario B's ApiUrl unless
    # something already set it explicitly.
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

    # Every scenario builds its resourceId from $Config.SubscriptionId - resolve it once
    # here now that the Az context is definitely established, from -SubscriptionId if
    # given, otherwise from whatever ended up active.
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
        POSTs to a ScaleTrigger endpoint. Both /api/nodebenchmark/run and /api/loadconfig
        are optionally-authorized - if the target has Auth:Enabled, the first call gets a
        401; this logs in with -AdminUsername/-AdminPassword and retries once, caching the
        token per ApiUrl for the rest of the run. If no -AdminPassword was given, gives up
        and returns Success=$false so the caller can skip that scenario's auto-tuning step
        gracefully instead of crashing the whole run.
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
        Runs the target node's own /api/nodebenchmark/run, applies the same formula the
        dashboard's "Set recommended" button uses (main README, "CPU calibration": Max =
        0.8 * (cpuNumbersPerSecond / N), Min = 0.5 * Max, N = -BenchmarkTargetVotes), and
        POSTs the result to /api/loadconfig as CpuIterationsPerVote - so every scenario's
        load starts with a range actually calibrated to that node's real throughput,
        instead of the fixed guess baked into LoadArgs. Failures are non-fatal - warns and
        leaves whatever CpuIterationsPerVote is already configured rather than blocking
        the scenario.
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

function Set-DbCpuLoad {
    <#
        Sets a fixed DbCpuIterationsPerVote range for scenario B via /api/loadconfig -
        there's no "benchmark the database" endpoint to calibrate this from, unlike
        CpuIterationsPerVote, so it's a flat default (-DbCpuIterationsMin/-Max) rather
        than computed.
    #>
    param(
        [Parameter(Mandatory)] [string]$ApiUrl,
        [Parameter(Mandatory)] [int]$MinValue,
        [Parameter(Mandatory)] [int]$MaxValue
    )
    Write-Host "Setting DbCpuIterationsPerVote to $MinValue-$MaxValue at $ApiUrl..." -ForegroundColor Cyan
    $body = ConvertTo-Json -InputObject @(@{ settingName = 'DbCpuIterationsPerVote'; min = $MinValue; max = $MaxValue })
    $result = Invoke-ScaleTriggerApi -ApiUrl $ApiUrl -RoutePath '/api/loadconfig' -BodyJson $body
    if ($result.Success) {
        Write-Host "  DbCpuIterationsPerVote set to $MinValue-$MaxValue." -ForegroundColor Green
    }
}

function Start-LoadGenerator {
    <#
        scaleTriggerLoad.py defaults to admin:admin if --username/--password aren't given,
        which never matches this demo (Auth:Enabled=true everywhere, AdminUser set from
        the deployment's own -AdminUsername/-AdminPassword) - always pass the real ones.
    #>
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
    <#
        Polls the given metric at short intervals until the latest data point's average
        crosses the given threshold. Returns the timestamp of the threshold crossing (the
        "load threshold crossed" timestamp used in the tables).
    #>
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
    <#
        Runs a KQL query against AzureActivity (identical to the one in chapter 7.2.2) and
        returns the scaling/resize operation records for the resource within the given time
        window.
    #>
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
    <#
        Waits for a new Logic App run (used by scenario A - automatic, and scenario C -
        after the user triggers it manually). Returns the run object (StartTime, EndTime,
        Status).
    #>
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

    Set-DbCpuLoad -ApiUrl $cfg.ApiUrl -MinValue $DbCpuIterationsMin -MaxValue $DbCpuIterationsMax
    $load = Start-LoadGenerator -ApiUrl $cfg.ApiUrl -LoadArgsString $cfg.LoadArgs
    $startUtc = (Get-Date).ToUniversalTime()

    Write-Host "Collecting the cpu_percent time series for 20 minutes to plot the scale-up curve..." -ForegroundColor Cyan
    Start-Sleep -Seconds 1200

    Stop-Process -Id $load.Process.Id -Force -ErrorAction SilentlyContinue
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
    <#
        Converts a single CSV file's contents into an HTML table. If the file doesn't exist
        (the scenario hasn't been run yet), prints a note instead of an empty table, so the
        report clearly shows what's still missing.
    #>
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

    # Scenario C waits for manual human approval and takes an unpredictable amount of time,
    # so when running multiple scenarios at once it's split off and run in the background
    # (Start-Job), while the foreground script immediately continues with the rest.
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
