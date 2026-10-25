#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [pscredential] $Credential,
    [uri] $ServerUri = 'http://localhost:52199/MCWS/v1/',
    [string] $AlbumPath = 'Rock Albums\A\AC%2FDC\1980-07-25 - Back In Black',
    [int] $ExpectedTrackCount = 10
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\JRiver\JRiver.psd1')
function Assert($Condition, [string] $Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    Write-Host "PASS: $Message"
}

$originalLocation = Get-Location
try {
    New-PSDrive JRiverTest JRiver '\' -Credential $Credential -ServerUri $ServerUri | Out-Null
    $root = @(Get-ChildItem JRiverTest:\)
    Assert ($root.Count -gt 0 -and $root[0].PSIsContainer) 'Audio root contains views'
    Assert (-not (Test-Path 'JRiverTest:\__missing_view__')) 'Missing path returns false'
    Set-Location "JRiverTest:\$AlbumPath"
    $tracks = @(Get-ChildItem)
    Assert ($tracks.Count -eq $ExpectedTrackCount) 'Album track count matches'
    Assert (@($tracks | Where-Object PSIsContainer).Count -eq 0) 'Tracks are leaf items'
    Assert ($tracks[0].PSObject.Properties['SubGenre'] -and $tracks[0].PSObject.Properties['InMyCollection']) 'Custom metadata is preserved'
    Assert ($null -ne $tracks[0].PSObject.Properties['Album Artist (auto)']) 'Calculated metadata is returned'
    $track = Get-Item -LiteralPath $tracks[0].PSPath
    Assert ($track.Key -eq $tracks[0].Key) 'Provider-qualified PSPath resolves to the same track'
    $relative = Get-Item -LiteralPath $tracks[0].PathName
    Assert ($relative.Key -eq $track.Key) 'Relative track path resolves'
    $names = @(Get-ChildItem -Name)
    Assert ($names.Count -eq $tracks.Count -and $names[0] -eq $tracks[0].PathName) 'Name-only listing uses navigable segments'
    Assert (@(Get-ChildItem '*Hells*').Count -eq 1) 'Wildcard listing works'
    foreach ($pattern in @('Hells*', '*Bells', 'hells*', 'H?lls Bells', '[Hh]ells*')) {
        $matches = @(Get-ChildItem -Path $pattern)
        Assert ($matches.Count -eq 1 -and $matches[0].Key -eq $track.Key) "Track name pattern works: $pattern"
    }
    Assert (@(Get-ChildItem '*__no_matching_track__*').Count -eq 0) 'Unmatched wildcard returns no items'
    $pathMatches = @(Get-ChildItem -Path ($tracks[0].PathName.Substring(0, 3) + '*'))
    Assert (@($pathMatches | Where-Object Key -eq $track.Key).Count -eq 1) 'Navigable track segment patterns remain supported'
    $qualifiedMatches = @(Get-Item -Path ($tracks[0].PSParentPath + '\Hells*'))
    Assert ($qualifiedMatches.Count -eq 1 -and $qualifiedMatches[0].Key -eq $track.Key) 'Provider-qualified wildcard path resolves'
    Assert (@(Get-ChildItem -Name 'Hells*')[0] -eq $track.PathName) 'Name-only wildcard listing returns the navigable segment'
    $first = Get-ChildItem | Select-Object -First 1
    Assert ($null -ne $first) 'Stopping a pipeline early works'
    $completion = TabExpansion2 'Get-Item JRiverTest:\Rock' 25
    Assert ($completion.CompletionMatches.Count -gt 0) 'Tab completion finds browse nodes'
    Set-Location ..
    Assert ((Get-Item .).Name -eq 'AC/DC') 'Parent navigation retains the original artist name'
    $albums = @(Get-ChildItem '*Black*')
    Assert ($albums.Count -gt 0 -and @($albums | Where-Object { -not $_.PSIsContainer -or $_.Name -notlike '*Black*' }).Count -eq 0) 'Album wildcard listing returns matching albums'
    $nestedMatches = @(Get-Item 'JRiverTest:\Rock*\A\AC%2FDC\*Back In Black\Hells*')
    Assert ($nestedMatches.Count -eq 1 -and $nestedMatches[0].Key -eq $track.Key) 'Wildcards work at multiple path levels'
    Assert (@(Get-ChildItem -Depth 0).Count -gt 0) 'Depth-limited listing works'
    $writeRejected = $false
    try { Remove-Item -LiteralPath $track.PSPath -ErrorAction Stop } catch { $writeRejected = $true }
    Assert $writeRejected 'Deletion is unsupported'
    New-PSDrive JRiverFull JRiver '\' -RootId -1 -Credential $Credential -ServerUri $ServerUri | Out-Null
    Assert (@(Get-ChildItem JRiverFull:\ | Where-Object Name -eq 'Audio').Count -eq 1) 'Full root can be mounted independently'
    $authRejected = $false
    try {
        $bad = New-Object pscredential('invalid-test-user', (ConvertTo-SecureString 'invalid' -AsPlainText -Force))
        New-PSDrive JRiverInvalid JRiver '\' -Credential $bad -ServerUri $ServerUri -ErrorAction Stop | Out-Null
    } catch { $authRejected = $true }
    Assert $authRejected 'Invalid credentials produce an error'
    Write-Host "All live checks passed in PowerShell $($PSVersionTable.PSVersion)."
} finally {
    Set-Location $originalLocation
    foreach ($name in @('JRiverTest', 'JRiverFull', 'JRiverInvalid')) {
        if (Get-PSDrive $name -ErrorAction SilentlyContinue) { Remove-PSDrive $name }
    }
}
