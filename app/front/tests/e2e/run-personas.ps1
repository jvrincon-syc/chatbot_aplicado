$ErrorActionPreference = "Continue"
$CLI = 'C:\Users\jvrincon\AppData\Roaming\npm\playwright-cli.cmd'
$S = 'sst-test'

function Pc($cmd) {
    $proc = Start-Process -FilePath 'cmd.exe' -ArgumentList "/c `"$CLI`" -s=$S $cmd" -NoNewWindow -Wait -PassThru -RedirectStandardOutput "$env:TEMP\pw_out.txt" -RedirectStandardError "$env:TEMP\pw_err.txt"
    $out = Get-Content "$env:TEMP\pw_out.txt" -Raw -ErrorAction SilentlyContinue
    return $out
}

$personas = @(
    @{ Name="Carlos Mendoza";     Role="Ingeniero de Seguridad";    Q="¿Qué es el Anexo Técnico de Seguridad y por qué es obligatorio?" }
    @{ Name="Maria F. Lopez";     Role="Operaria de manufactura";   Q="¿Cuáles son los EPP obligatorios en una planta industrial?" }
    @{ Name="Andres F. Ramirez";  Role="Gerente de RRHH";           Q="¿Cada cuánto se debe realizar la capacitación de SST?" }
    @{ Name="Diana C. Munoz";     Role="Supervisora de planta";     Q="¿Qué protocolo de emergencia se sigue ante un incendio?" }
    @{ Name="Juan E. Gutierrez";  Role="Aprendiz nuevo";            Q="¿Qué es la inducción de SST y qué temas debe cubrir?" }
    @{ Name="Patricia V. Solano"; Role="Presidenta COPASST";        Q="¿Cuáles son las funciones principales del COPASST?" }
)

Write-Host ""
Write-Host "SST Chatbot — 6 Persona Simulation"
Write-Host "===================================="
Write-Host ""

$results = @()

foreach ($p in $personas) {
    Write-Host "--- $($p.Name) ($($p.Role)) ---"
    Write-Host "P: $($p.Q)"

    # Click Nuevo chat to reset
    Pc "click ' Nuevo chat'" | Out-Null
    Start-Sleep -Seconds 1

    # Fill question
    Pc "fill '#chat-input' '$($p.Q)'" | Out-Null
    Start-Sleep -Seconds 1

    # Click send - measure time
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Pc "click 'Enviar'" | Out-Null
    Write-Host -NoNewline "  Waiting"

    # Poll: wait up to 120s for response
    $answered = $false
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Seconds 3
        $snap = Pc "snapshot"
        if ($snap -match "bubble--assistant" -and $snap -notmatch "bubble--typing") {
            $answered = $true
            break
        }
        Write-Host -NoNewline "."
    }
    $sw.Stop()

    if ($answered) {
        # Extract answer text
        $lines = $snap -split "`n"
        $capture = $false
        $answerLines = @()
        $foundAssistant = $false
        foreach ($line in $lines) {
            if ($line -match "bubble--assistant" -and $line -notmatch "bubble--typing" -and -not $foundAssistant) {
                $foundAssistant = $true
            }
            if ($foundAssistant -and $line -match "bubble__text") {
                $capture = $true
                continue
            }
            if ($capture) {
                $t = $line.Trim()
                if ($t -and $t -ne "" -and $t -notmatch "^(generic|paragraph|article|button|toolbar|img)" -and $answerLines.Count -lt 5) {
                    $answerLines += $t
                }
                if ($t -match "^  toolbar" -or $t -match "^  - button") { break }
            }
        }
        $answer = ($answerLines -join " ").Substring(0, [Math]::Min(160, ($answerLines -join " ").Length))
        $ms = $sw.ElapsedMilliseconds
        Write-Host ""
        Write-Host "  ✓ Answer (${ms}ms): $answer"
        $results += [PSCustomObject]@{ Name=$p.Name; Role=$p.Role; MS=$ms; OK=$true; Snippet=$answer }
    } else {
        $ms = $sw.ElapsedMilliseconds
        Write-Host ""
        Write-Host "  ✗ TIMEOUT (${ms}ms)"
        $results += [PSCustomObject]@{ Name=$p.Name; Role=$p.Role; MS=$ms; OK=$false; Snippet="TIMEOUT" }
    }
    Write-Host ""
}

Write-Host ""
Write-Host "===================================="
Write-Host "           RESULTS"
Write-Host "===================================="
foreach ($r in $results) {
    $mark = if ($r.OK) { "✓" } else { "✗" }
    Write-Host ("  {0} {1,-26} {2,7}ms  {3}" -f $mark, $r.Name, $r.MS, $r.Snippet.Substring(0, [Math]::Min(60, $r.Snippet.Length)))
}
$passed = ($results | Where-Object { $_.OK }).Count
Write-Host "------------------------------------"
Write-Host "  Passed: $passed / $($results.Count)"
Write-Host "===================================="
