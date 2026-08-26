<#
.SYNOPSIS
    Starts the local Qwen3-1.7B model via llama-server. All tunables come from model.json
    (this module's single source of config). Bind is loopback-only.

.DESCRIPTION
    Model path resolution order: -ModelPath arg > $env:<pathEnvVar> > model.json defaultPath.
    The .gguf is external infrastructure — never committed. Do NOT raise resource limits here;
    change model.json (and know why) — this is an 8 GB workstation baseline.

.EXAMPLE
    pwsh llm/start-llm.ps1
#>
[CmdletBinding()]
param([string]$ModelPath)

$ErrorActionPreference = 'Stop'

$cfg = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'model.json') -Raw | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($ModelPath)) {
    $ModelPath = [Environment]::GetEnvironmentVariable($cfg.model.pathEnvVar)
}
if ([string]::IsNullOrWhiteSpace($ModelPath)) {
    $ModelPath = $cfg.model.defaultPath
    Write-Warning "$($cfg.model.pathEnvVar) not set; using model.json defaultPath: $ModelPath"
}
if (-not (Test-Path -LiteralPath $ModelPath)) {
    throw "GGUF model not found at '$ModelPath'. Set $($cfg.model.pathEnvVar) or pass -ModelPath."
}
if (-not (Get-Command llama-server -ErrorAction SilentlyContinue)) {
    throw "'llama-server' not found on PATH. Install llama.cpp and ensure llama-server is available."
}

# Disable model 'thinking' output (Qwen chat template flag).
$env:LLAMA_ARG_CHAT_TEMPLATE_KWARGS = '{"enable_thinking":' + ($cfg.generation.enableThinking.ToString().ToLower()) + '}'

$r = $cfg.runtime
Write-Host "Starting llama-server on $($cfg.server.host):$($cfg.server.port) (loopback only)..." -ForegroundColor Green

llama-server `
    -m "$ModelPath" `
    --device $r.device `
    --gpu-layers $r.gpuLayers `
    -t $r.threads `
    -tb $r.threadsBatch `
    --poll $r.poll `
    --prio $r.priority `
    -c $r.contextSize `
    -n $cfg.generation.maxOutputTokens `
    -b $r.batchSize `
    -ub $r.ubatchSize `
    -ctk $r.cacheTypeK `
    -ctv $r.cacheTypeV `
    -np $r.parallelSlots `
    --cache-ram $r.cacheRam `
    --cache-reuse $r.cacheReuse `
    --temp $cfg.generation.temperature `
    --reasoning $cfg.generation.reasoning `
    --host $cfg.server.host `
    --port $cfg.server.port `
    --cors-origins $cfg.server.corsOrigins `
    --perf

# Stop: Ctrl+C in this window, or close it. Do not expose beyond 127.0.0.1.
