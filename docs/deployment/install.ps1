#Requires -RunAsAdministrator
<#
.SYNOPSIS
    HeatingCameraSystem Agent Manager Windows Service 설치 스크립트.
.DESCRIPTION
    1. 설치 디렉터리 생성 (C:\HeatingCameraSystem\Manager, Agent, logs)
    2. sc.exe 로 Windows Service 등록 (HCS-Manager)
    3. 서비스 실패 복구 정책 설정 (3회 재시작, 5초 간격)
    4. 방화벽 아웃바운드 규칙 추가 (NATS 4222/tcp)
    5. manager-settings.json 생성 (NATS URL 대화형 입력)
#>

param(
    [string]$InstallRoot = "C:\HeatingCameraSystem",
    [string]$NatsUrl
)

$ErrorActionPreference = "Stop"

$managerDir = Join-Path $InstallRoot "Manager"
$agentDir   = Join-Path $InstallRoot "Agent"
$logsDir    = Join-Path $InstallRoot "logs"

Write-Host "=== HeatingCameraSystem Manager Installer ===" -ForegroundColor Cyan
Write-Host "Install root: $InstallRoot"

# 1. 디렉터리 생성
foreach ($dir in @($managerDir, $agentDir, $logsDir)) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-Host "  Created: $dir"
    }
}

# 2. NATS URL 입력
if (-not $NatsUrl) {
    $NatsUrl = Read-Host "NATS Server URL (default: nats://127.0.0.1:4222)"
    if ([string]::IsNullOrWhiteSpace($NatsUrl)) { $NatsUrl = "nats://127.0.0.1:4222" }
}

# 3. manager-settings.json 생성
$settingsPath = Join-Path $managerDir "manager-settings.json"
$settings = @{
    PCId             = $env:COMPUTERNAME
    NatsUrl          = $NatsUrl
    SimulationMode   = $false
    LogRetentionDays = 7
    WarnAlertEnabled = $false
    InstallRoot      = $InstallRoot
    AgentExePath     = Join-Path $agentDir "HeatingCameraSystem.Agent.exe"
} | ConvertTo-Json -Depth 3

Set-Content -Path $settingsPath -Value $settings -Encoding UTF8
Write-Host "  Settings: $settingsPath" -ForegroundColor Green

# 4. 서비스 등록
$exePath = Join-Path $managerDir "HeatingCameraSystem.AgentManager.exe"
$serviceName = "HCS-Manager"

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "  Service '$serviceName' already exists. Stopping..." -ForegroundColor Yellow
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

& sc.exe create $serviceName binPath= "`"$exePath`" `"$InstallRoot`"" start= auto obj= LocalSystem displayname= "HCS Agent Manager"
& sc.exe description $serviceName "HeatingCameraSystem Agent Manager - camera auto-discovery and Agent process supervisor"
& sc.exe failure $serviceName reset= 0 actions= restart/5000/restart/5000/restart/5000

Write-Host "  Service '$serviceName' registered." -ForegroundColor Green

# 5. 방화벽 규칙
$ruleName = "HCS-NATS-Outbound"
$existingRule = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
if (-not $existingRule) {
    New-NetFirewallRule -DisplayName $ruleName `
        -Direction Outbound -Protocol TCP -RemotePort 4222 `
        -Action Allow -Profile Any | Out-Null
    Write-Host "  Firewall rule '$ruleName' created." -ForegroundColor Green
} else {
    Write-Host "  Firewall rule '$ruleName' already exists." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== Installation Complete ===" -ForegroundColor Cyan
Write-Host "Next steps:"
Write-Host "  1. Copy Manager build output to: $managerDir"
Write-Host "  2. Copy Agent build output to:   $agentDir"
Write-Host "  3. Start service: Start-Service $serviceName"
Write-Host "  4. Verify: Get-Service $serviceName"
