<#
.SYNOPSIS
    Lance les tests automatiques de MatrixVision dans l'éditeur Unity ouvert (CLI Unity + com.unity.pipeline).

.DESCRIPTION
    - Tests d'édition (quelques secondes) : shaders, câblage du rendu, scènes AR et Quest, bascule téléphone/Quest.
    - Tests en mode Play avec XR Simulation (environ 15 s) : dans la pièce de test virtuelle, la scène téléphone
      affiche du code sur les plans détectés et la scène Quest sur le maillage de la pièce ; captures d'écran
      dans Temp/MatrixVisionTests.
    XR Simulation est activé le temps des tests seulement (les autres scènes du projet ne sont pas touchées).

.EXAMPLE
    ./Tools/run-tests.ps1
    ./Tools/run-tests.ps1 -EditModeOnly
#>
param([switch]$EditModeOnly)

$ErrorActionPreference = 'Continue'
$project = Split-Path $PSScriptRoot -Parent
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'User') + ';' + [Environment]::GetEnvironmentVariable('Path', 'Machine')
$unityArgs = @('--project-path', $project, '--no-banner', '--result-only')
$failed = 0

Write-Host 'Tests d''édition…'
$edit = unity command run_tests --mode editor --filter MatrixVision.Tests.EditMode --filter_type assembly @unityArgs --timeout 300 | ConvertFrom-Json
foreach ($result in $edit.Results) {
    Write-Host ("  {0,-7} {1}" -f $result.Status, $result.FullName)
    if ($result.Status -ne 'Passed') { $failed++; Write-Host "          $($result.Message)" }
}

if (-not $EditModeOnly) {
    Write-Host 'Tests en mode Play (XR Simulation)…'
    unity command run_tests --mode playmode --filter MatrixVision.Tests.PlayMode --filter_type assembly --async_tests true @unityArgs | Out-Null
    Start-Sleep 30
    $start = Get-Date
    do {
        Start-Sleep 10
        $status = unity command test_status @unityArgs 2>$null | ConvertFrom-Json -ErrorAction SilentlyContinue
        # Le résultat peut arriver encodé deux fois (une chaîne JSON) : on le décode une seconde fois.
        if ($status -is [string]) { $status = $status | ConvertFrom-Json -ErrorAction SilentlyContinue }
    } while (((Get-Date) - $start).TotalSeconds -lt 480 -and (-not $status -or $status.status -eq 'running'))

    foreach ($result in $status.results) {
        Write-Host ("  {0,-7} {1} ({2:0.0} s)" -f $result.status, $result.fullName, [double]$result.duration)
        if ($result.status -ne 'Passed') { $failed++; Write-Host "          $($result.message)" }
    }
    Write-Host "Captures : $(Join-Path $project 'Temp/MatrixVisionTests')"
}

if ($failed -gt 0) {
    Write-Host "$failed test(s) en échec." -ForegroundColor Red
    exit 1
}
Write-Host 'Tous les tests passent.' -ForegroundColor Green
