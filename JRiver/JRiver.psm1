$ErrorActionPreference = 'Stop'
if (-not ('PSJRiver.JRiverProvider' -as [type])) {
    $source = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'JRiverProvider.cs') -Raw
    if ($PSVersionTable.PSVersion.Major -ge 7) {
        $source = "#pragma warning disable SYSLIB0014`n" + $source
    }
    if ($PSVersionTable.PSVersion.Major -lt 7) {
        $types = Add-Type -TypeDefinition $source -ReferencedAssemblies @('System.Xml.dll', 'System.Core.dll', [System.Management.Automation.PSObject].Assembly.Location) -PassThru
    } else {
        $types = Add-Type -TypeDefinition $source -PassThru
    }
    $assembly = ($types | Where-Object FullName -eq 'PSJRiver.JRiverProvider').Assembly
} else {
    $assembly = [PSJRiver.JRiverProvider].Assembly
}
Import-Module -Assembly $assembly
