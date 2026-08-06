param(
    [string]$ResourceGroupPrefix = 'ScaleTriggerDemo'
)

Connect-AzAccount -Identity | Out-Null

$resourceGroups = @(
    "$ResourceGroupPrefix-Automation"
    "$ResourceGroupPrefix-Database"
    "$ResourceGroupPrefix-ServicePlan"
    "$ResourceGroupPrefix-ScaleSet"
    "$ResourceGroupPrefix-SingleVM"
    "$ResourceGroupPrefix-Logs"
)

foreach ($rg in $resourceGroups) {
    if (Get-AzResourceGroup -Name $rg -ErrorAction SilentlyContinue) {
        Write-Output "Deleting $rg ..."
        Remove-AzResourceGroup -Name $rg -Force -AsJob | Out-Null
    }
    else {
        Write-Output "Skipping $rg (not found)."
    }
}

Write-Output "Deletion started for all existing resource groups."
