param(
    [string]$VmssResourceGroup,
    [string]$VmssName
)

Connect-AzAccount -Identity | Out-Null

Write-Output "Stopping instances $VmssName in $VmssResourceGroup ..."
Stop-AzVmss -ResourceGroupName $VmssResourceGroup -VMScaleSetName $VmssName -Force
Write-Output "Done."
