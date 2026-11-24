# Temporarily changes the live companion panel and restores it at the end.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName PresentationFramework

$release = Join-Path (Split-Path $PSScriptRoot) 'artifacts\kzlSystemCustomize-shared-backdrop'
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $release 'kzlSystemCustomize.dll'))
$backend = $assembly.GetType('StartStyleToolbox.NativeStartMenu', $true)
$instance = [Activator]::CreateInstance($backend, $true)
$style = $assembly.GetType('StartStyleToolbox.StyleOptions', $true)
$material = $assembly.GetType('StartStyleToolbox.Material', $true)
$dictionary = [type]::GetType('System.Collections.Generic.Dictionary`2').MakeGenericType([type[]]@([string], $style))
$flags = [Reflection.BindingFlags]'Instance,NonPublic'
$commit = $backend.GetMethod('Commit', $flags)
$applied = $backend.GetMethod('AppliedRegions', $flags)
$readFlags = [IO.MemoryMappedFiles.MemoryMappedFileRights]::Read
$readAccess = [IO.MemoryMappedFiles.MemoryMappedFileAccess]::Read

function Invoke-Style([string]$region, [string]$mode, [bool]$shared = $false) {
    $change = [Activator]::CreateInstance($dictionary)
    $change.Add($region, [Activator]::CreateInstance($style,
        [object[]]@([Enum]::Parse($material, $mode), '#CC2341CD', 45, 50, 28, 100, $shared)))
    $commit.Invoke($instance, [object[]]@($change.psobject.BaseObject, $true))
}

$before = Get-Process StartMenuExperienceHost
$status = $backend.GetMethod('GetCompatibility', $flags).Invoke($instance, $null)
if (!$status.CanApply -or !$status.Conflicts.ContainsKey('main') -or $status.Conflicts.ContainsKey('companion')) {
    throw 'Expected MToolBox to own the main background and the companion panel to be available.'
}
$blocked = Invoke-Style main Solid
if ($blocked.Success -or $blocked.Message -notmatch 'MToolBox') { throw 'Main background conflict was not blocked.' }
if ($applied.Invoke($instance, $null) -contains 'companion') {
    $current = [IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting(
        "Local\kzlSystemCustomize.StartMenu.4.$($before.Id)", $readFlags)
    try {
        $reader = $current.CreateViewAccessor(0, 288, $readAccess)
        try {
            if ($reader.ReadInt32(248) -ne 3 -or $reader.ReadInt32(272) -ne 1) {
                throw 'An existing companion style is active. Restore it before running the test.'
            }
            $previous = [Activator]::CreateInstance($style, [object[]]@(
                [Enum]::ToObject($material, $reader.ReadInt32(248)), ('#{0:X8}' -f $reader.ReadUInt32(252)),
                $reader.ReadInt32(256), $reader.ReadInt32(260), $reader.ReadInt32(264), $reader.ReadInt32(268), $true))
        }
        finally { $reader.Dispose() }
    }
    finally { $current.Dispose() }
}

try {
    foreach ($mode in 'Solid', 'Transparent', 'Acrylic', 'Blur', 'Mica') {
        $result = Invoke-Style companion $mode
        if (!$result.Success -or $applied.Invoke($instance, $null) -notcontains 'companion') {
            throw "Live test for $mode failed. $($result.Message)"
        }
        Write-Output "Passed $mode"
    }
    $result = Invoke-Style companion Acrylic $true
    if (!$result.Success -or $applied.Invoke($instance, $null) -notcontains 'companion') {
        throw "Live test for shared backdrop failed. $($result.Message)"
    }
    Write-Output 'Passed shared backdrop'
}
finally {
    $restored = Invoke-Style companion Original
    if (!$restored.Success -or $applied.Invoke($instance, $null) -contains 'companion') {
        throw "Live restore failed. $($restored.Message)"
    }
    if ($null -ne $previous) {
        $change = [Activator]::CreateInstance($dictionary)
        $change.Add('companion', $previous)
        $restored = $commit.Invoke($instance, [object[]]@($change.psobject.BaseObject, $true))
        if (!$restored.Success) { throw "Previous style restore failed. $($restored.Message)" }
    }
}

$after = Get-Process StartMenuExperienceHost
if ($before.Id -ne $after.Id -or $after.Modules.ModuleName -notcontains 'MToolExtend.dll') {
    throw 'The host restarted or the MToolBox module was unloaded.'
}
Write-Output 'Companion restored. The Start menu host and MToolBox module remained loaded.'
