param(
    [string]$Tag = "latest",
    [string]$Registry = "registry.ft-soft.ru/devops"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

$apiImage = "$Registry/api-auto-up-release:$Tag"
$webImage = "$Registry/web-auto-up-release:$Tag"

Write-Host "Building API image: $apiImage"
docker build -f Api/Dockerfile -t $apiImage Api

Write-Host "Building WEB image: $webImage"
docker build -f client/Dockerfile -t $webImage client

Write-Host "Pushing API image: $apiImage"
docker push $apiImage

Write-Host "Pushing WEB image: $webImage"
docker push $webImage

Write-Host "Done."
