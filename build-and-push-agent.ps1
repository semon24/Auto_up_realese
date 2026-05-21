param(
    [string]$Tag = "latest",
    [string]$Registry = "registry.ft-soft.ru/devops"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

$agentImage = "$Registry/agent-auto-up-release:$Tag"

Write-Host "Building AGENT image: $agentImage"
docker build -f agent/Dockerfile -t $agentImage agent

Write-Host "Pushing AGENT image: $agentImage"
docker push $agentImage

Write-Host "Done."
