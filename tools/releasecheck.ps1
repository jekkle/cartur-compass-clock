# Resolves every member the built DLL references against the INSTALLED game.
#
#   powershell -ExecutionPolicy Bypass -File tools\releasecheck.ps1
#
# A mod compiles against the game's assemblies, so the compiler already agreed the code is
# valid - against whatever was on disk at build time. This asks a different question: does
# every type, method and field the DLL reaches for still exist in the game as installed right
# now? That is what actually fails at load, and it fails silently as a missing patch rather
# than a crash.
#
# Exits non-zero if anything is unresolved, so it can gate a release.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$dll = Join-Path $root "src\bin\Release\net472\CarturCompassAndClock.dll"
if (-not (Test-Path $dll)) { throw "No Release build at $dll - run tools\pack.ps1 first" }

$game = "C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed"
$bep = "$env:APPDATA\r2modmanPlus-local\Valheim\profiles\Default\BepInEx\core"
foreach ($d in @($game, $bep)) {
    if (-not (Test-Path $d)) { throw "Not found: $d" }
}

Add-Type -Path (Join-Path $bep "Mono.Cecil.dll")

$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory($game)
$resolver.AddSearchDirectory($bep)
$resolver.AddSearchDirectory((Split-Path $dll))

$params = New-Object Mono.Cecil.ReaderParameters
$params.AssemblyResolver = $resolver
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll, $params)
$module = $asm.MainModule

"Cartur's Compass and Clock - release check"
"  DLL   $dll"
"  game  $game"
""

$bad = New-Object System.Collections.ArrayList

function Check($kind, $name, $block) {
    try {
        $resolved = & $block
        if ($null -eq $resolved) { [void]$bad.Add("$kind  $name") }
    }
    catch { [void]$bad.Add("$kind  $name   ($($_.Exception.Message))") }
}

# Only members from the GAME's assemblies matter. References into mscorlib, Unity or the mod's
# own types resolve trivially and would bury the interesting failures.
$watch = @("assembly_valheim", "assembly_utils", "assembly_guiutils", "assembly_postprocessing")

function FromGame($scope) {
    if ($null -eq $scope) { return $false }
    $n = $scope.Name
    foreach ($w in $watch) { if ($n -eq $w) { return $true } }
    return $false
}

$types = 0; $methods = 0; $fields = 0

foreach ($t in $module.GetTypeReferences()) {
    if (-not (FromGame $t.Scope)) { continue }
    $types++
    Check "TYPE  " $t.FullName { $t.Resolve() }
}

foreach ($m in $module.GetMemberReferences()) {
    $scope = $m.DeclaringType.Scope
    if (-not (FromGame $scope)) { continue }
    if ($m -is [Mono.Cecil.MethodReference]) {
        $methods++
        Check "METHOD" $m.FullName { $m.Resolve() }
    }
    elseif ($m -is [Mono.Cecil.FieldReference]) {
        $fields++
        Check "FIELD " $m.FullName { $m.Resolve() }
    }
}

"checked against the installed game:"
"  types    $types"
"  methods  $methods"
"  fields   $fields"
""

if ($bad.Count -eq 0) {
    "PASS - every game member the DLL references resolves."
    exit 0
}

"FAIL - $($bad.Count) unresolved:"
foreach ($b in $bad) { "  $b" }
exit 1
