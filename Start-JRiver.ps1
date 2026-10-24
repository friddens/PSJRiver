#Requires -Version 5.1
[CmdletBinding()]
param(
    [pscredential] $Credential = (Get-Credential -Message 'JRiver MCWS credentials'),
    [string] $Name = 'Music',
    [uri] $ServerUri = 'http://localhost:52199/MCWS/v1/',
    [string] $RootId = '1'
)

Import-Module (Join-Path $PSScriptRoot 'JRiver\JRiver.psd1') -ErrorAction Stop
New-PSDrive -Name $Name -PSProvider JRiver -Root '\' -Credential $Credential -ServerUri $ServerUri -RootId $RootId -Scope Global -ErrorAction Stop
