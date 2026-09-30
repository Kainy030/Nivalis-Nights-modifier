# Compatibility entry: all plugins are validated and packaged against the same shared runtime.
param([switch]$Restore)
& (Join-Path $PSScriptRoot 'Build-Hooks.ps1') -Restore:$Restore
