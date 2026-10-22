<#
.SYNOPSIS
    Build, installation et captures d'écran de MatrixVision sur un téléphone Android (ARCore) ou un Meta Quest.

.DESCRIPTION
    1. Applique la configuration de la cible (menu MatrixVision > Build Target) : loader XR, API graphique,
       qualité, API minimale. Téléphone et Quest ciblent tous deux Android et partagent ces réglages.
    2. Build de la scène de la cible via l'éditeur Unity ouvert sur le projet (CLI Unity, paquet com.unity.pipeline).
    3. Installation de l'APK (adb install -r) sur l'appareil de la cible, puis lancement de l'application.
    4. Captures de l'écran de l'appareil aux délais demandés, dans Builds/<cible>/captures.
    5. Avertissements et erreurs Unity du journal Android (logcat).

.EXAMPLE
    ./Tools/deploy-android.ps1
    ./Tools/deploy-android.ps1 -Target Quest
    ./Tools/deploy-android.ps1 -Target Phone -SkipBuild -CaptureDelays 5,10,20
    ./Tools/deploy-android.ps1 -Target Quest -BuildOnly
    ./Tools/deploy-android.ps1 -Target HandsDemo   # exemple XRI Hands en réalité mixte sur Quest
#>
param(
    [ValidateSet('Phone', 'Quest', 'HandsDemo')]
    [string]$Target = 'Phone',
    [switch]$SkipBuild,
    [switch]$BuildOnly,
    [int[]]$CaptureDelays = @(3, 8, 15)
)

# Continue : adb et monkey écrivent des messages normaux sur la sortie d'erreur, que PowerShell 5
# prendrait sinon pour des échecs. Les vraies erreurs sont vérifiées explicitement (throw).
$ErrorActionPreference = 'Continue'
$project = Split-Path $PSScriptRoot -Parent
$headset = $Target -ne 'Phone'
$scene = switch ($Target) {
    'Quest' { 'Assets/Scenes/MatrixQuest.unity' }
    'HandsDemo' { 'Assets/Samples/XR Interaction Toolkit/3.3.2/Hands Interaction Demo/HandsDemoScene.unity' }
    default { 'Assets/Scenes/MatrixAR.unity' }
}
$configureType = if ($Target -eq 'HandsDemo') { 'HandsDemoMixedReality' } else { 'MatrixBuildTargets' }
$configure = switch ($Target) { 'Quest' { 'ConfigureQuest' } 'HandsDemo' { 'Configure' } default { 'ConfigurePhone' } }
$outputDir = "Builds/$Target"
$apkRelative = "$outputDir/MatrixVision.apk"
$apk = Join-Path $project $apkRelative
$captures = Join-Path $project "$outputDir/captures"
$package = 'com.ziglou.matrixvision'
$adb = Join-Path $env:LOCALAPPDATA 'Android/Sdk/platform-tools/adb.exe'
if (-not (Test-Path $adb)) { $adb = 'adb' }
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'User') + ';' + [Environment]::GetEnvironmentVariable('Path', 'Machine')
$unityArgs = @('--project-path', $project, '--no-banner', '--result-only')

# Appareil de la cible : un casque Meta (fabricant Oculus/Meta) pour Quest, un autre appareil pour Phone.
$serials = & $adb devices | Select-String "^(\S+)\tdevice$" | ForEach-Object { $_.Matches[0].Groups[1].Value }
$device = $serials | Where-Object {
    $maker = (& $adb -s $_ shell getprop ro.product.manufacturer).Trim()
    $isHeadset = $maker -match 'Oculus|Meta'
    if ($headset) { $isHeadset } else { -not $isHeadset }
} | Select-Object -First 1
if (-not $device -and -not $BuildOnly) {
    throw "Aucun appareil '$Target' détecté : branchez-le en USB et autorisez le débogage USB."
}
if ($device) { Write-Host "Appareil : $device" }

if (-not $SkipBuild) {
    # Configuration de la cible, appliquée dans l'éditeur ouvert.
    $script = Join-Path $env:TEMP 'matrixvision-configure.cs'
    @"
var editorAssembly = System.AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp-Editor");
return (string)editorAssembly.GetType("MatrixVision.Editor.$configureType").GetMethod("$configure").Invoke(null, null);
"@ | Set-Content $script -Encoding utf8
    $result = unity command eval_file --file $script @unityArgs --timeout 60 | ConvertFrom-Json
    if (-not $result.success) { throw "Configuration '$Target' en échec : $($result | ConvertTo-Json -Depth 5)" }
    Write-Host $result.result

    $previous = if (Test-Path $apk) { (Get-Item $apk).LastWriteTime } else { [datetime]::MinValue }
    Write-Host "Build Android de $scene…"
    unity command build --target Android --outputPath $apkRelative --scenes "[\`"$scene\`"]" --confirm true @unityArgs | Out-Null
    do {
        Start-Sleep 15
        $status = unity command build_status @unityArgs 2>$null | ConvertFrom-Json -ErrorAction SilentlyContinue
        # Le résultat peut arriver encodé deux fois (une chaîne JSON) : on le décode une seconde fois.
        if ($status -is [string]) { $status = $status | ConvertFrom-Json -ErrorAction SilentlyContinue }
    } while (-not $status -or $status.status -ne 'completed')
    if ($status.result -ne 'Succeeded') {
        throw "Build en échec : $($status.result) ($($status.totalErrors) erreur(s)) : voir la console Unity."
    }
    if (-not (Test-Path $apk) -or (Get-Item $apk).LastWriteTime -le $previous) {
        throw 'Le build n''a pas produit de nouvel APK : voir la console Unity.'
    }
}

if ($BuildOnly) {
    Write-Host "APK : $apk"
    return
}

Write-Host 'Installation…'
& $adb -s $device install -r $apk
& $adb -s $device logcat -c
& $adb -s $device shell monkey -p $package -c android.intent.category.LAUNCHER 1 | Out-Null

New-Item -ItemType Directory -Force $captures | Out-Null
$elapsed = 0
foreach ($delay in $CaptureDelays) {
    Start-Sleep ([Math]::Max(0, $delay - $elapsed))
    $elapsed = $delay
    $file = Join-Path $captures ('capture_{0:00}s.png' -f $delay)
    # cmd /c : la redirection de PowerShell 5 corromprait l'image binaire.
    cmd /c "`"$adb`" -s $device exec-out screencap -p > `"$file`""
    Write-Host "Capture : $file"
}

Write-Host 'Journal Unity (avertissements et erreurs) :'
& $adb -s $device logcat -d -s 'Unity:W' 'AndroidRuntime:E' | Select-Object -Last 40
