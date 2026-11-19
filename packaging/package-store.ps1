# Builds the Microsoft Store package with this app's Partner Center identity (Store ID 9P4ZP4DHZ59J).
# Bump -Version (last part must stay 0) for every new submission, e.g. .\packaging\package-store.ps1 -Version 1.0.1.0
param([string]$Version = "1.0.0.0")
& (Join-Path $PSScriptRoot 'package.ps1') `
    -IdentityName "HisHandiwork.NeonDiskManagement" `
    -Publisher "CN=9F716D34-43EE-4F0B-918B-8A025B55F943" `
    -PublisherDisplayName "His Handiwork" `
    -Version $Version
