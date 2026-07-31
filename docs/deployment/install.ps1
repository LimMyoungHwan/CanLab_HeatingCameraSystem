#Requires -RunAsAdministrator
<#
.SYNOPSIS
    HeatingCameraSystem Agent Manager 설치 스크립트 (운영자 세션 앱).
.DESCRIPTION
    Manager 는 session-0 Windows Service 가 아니라 운영자 로그인 세션에서 도는 콘솔 앱이다.
    이 스크립트는 디렉터리·설정·방화벽만 준비하고, 자동 시작(로그온)은 install-manager-task.ps1 이 담당한다.
    1. 설치 디렉터리 생성 (C:\HeatingCameraSystem\Manager, Agent, logs)
    2. manager-settings.json 생성 (NATS URL 대화형 입력)
    3. 방화벽 아웃바운드 규칙 추가 (NATS 4222/tcp)
    4. Manager 자동 시작: install-manager-task.ps1 로 로그온 예약작업 등록 (AgentUI 와 동일 방식)
#>

param(
    [string]$InstallRoot = "C:\HeatingCameraSystem",
    [string]$NatsUrl
)

$ErrorActionPreference = "Stop"

$managerDir = Join-Path $InstallRoot "Manager"
$agentDir   = Join-Path $InstallRoot "Agent"
$agentUiDir = Join-Path $InstallRoot "AgentUI"
$logsDir    = Join-Path $InstallRoot "logs"

Write-Host "=== HeatingCameraSystem Manager Installer ===" -ForegroundColor Cyan
Write-Host "Install root: $InstallRoot"

# 1. 디렉터리 생성
foreach ($dir in @($managerDir, $agentDir, $agentUiDir, $logsDir)) {
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
    PCId                = $env:COMPUTERNAME
    NatsUrl             = $NatsUrl
    SimulateEnumeration = $false
    SimulateAgentMode   = $false
    LogRetentionDays    = 7
    WarnAlertEnabled    = $false
    InstallRoot         = $InstallRoot
    AgentExePath        = Join-Path $agentDir "HeatingCameraSystem.Agent.exe"
    AgentUiExePath      = Join-Path $agentUiDir "HeatingCameraSystem.AgentUI.exe"
} | ConvertTo-Json -Depth 3

Set-Content -Path $settingsPath -Value $settings -Encoding UTF8
Write-Host "  Settings: $settingsPath" -ForegroundColor Green

# 4. Manager 자동 시작 — 로그온 예약작업 (install-manager-task.ps1 이 단일 소스)
# session-0 Windows Service 제거됨. Manager 는 운영자 로그인 세션의 콘솔 앱으로 로그온 시 기동한다.
Write-Host "  Manager autostart: run install-manager-task.ps1 to register the HCS-Manager logon task." -ForegroundColor Yellow

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
Write-Host "  1. Copy Manager build output to:  $managerDir"
Write-Host "  2. Copy AgentUI build output to:  $agentUiDir  (primary camera app)"
Write-Host "  3. Register AgentUI logon task:   ./docs/deployment/install-agentui-task.ps1 -InstallRoot $InstallRoot"
Write-Host "  4. Register Manager logon task:   ./docs/deployment/install-manager-task.ps1 -InstallRoot $InstallRoot"
Write-Host "  5. Verify: Get-ScheduledTask HCS-Manager"
Write-Host "  (console Agent at $agentDir is optional - diagnostic/fallback only)"
