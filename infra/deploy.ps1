<#
.SYNOPSIS
    Deploys the Service Bus namespace used by the notebook walkthrough.

.EXAMPLE
    ./deploy.ps1 -ResourceGroup rg-sbdemo -Location eastus -NamespaceName sbdemo12345
#>
param(
    [Parameter(Mandatory = $true)] [string] $ResourceGroup,
    [Parameter(Mandatory = $true)] [string] $NamespaceName,
    [string] $Location = "eastus",
    [string] $Sku = "Standard",
    [string] $PrincipalId = ""
)

$ErrorActionPreference = "Stop"

Write-Host "Ensuring resource group '$ResourceGroup' exists in $Location..." -ForegroundColor Cyan
az group create -n $ResourceGroup -l $Location --output none

Write-Host "Deploying Bicep template..." -ForegroundColor Cyan
$deployment = az deployment group create `
    -g $ResourceGroup `
    -f (Join-Path $PSScriptRoot "main.bicep") `
    -p namespaceName=$NamespaceName location=$Location skuName=$Sku principalId=$PrincipalId `
    --query "properties.outputs" `
    -o json | ConvertFrom-Json

if (-not $deployment) {
    throw "Deployment failed."
}

$connString = $deployment.primaryConnectionString.value
$hostname   = $deployment.namespaceHostname.value

Write-Host ""
Write-Host "Deployment complete." -ForegroundColor Green
Write-Host "Namespace : $($deployment.namespaceName.value)"
Write-Host "Hostname  : $hostname"
Write-Host ""
Write-Host "To use the notebooks, set these environment variables:" -ForegroundColor Yellow
Write-Host "  `$env:SERVICEBUS_CONNECTION_STRING = '$connString'"
Write-Host "  `$env:SERVICEBUS_NAMESPACE         = '$hostname'"
