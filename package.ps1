#!/usr/bin/env pwsh
# Copyright (c) 2025 Roger Brown.
# Licensed under the MIT License.

param($ProjectName, $IntermediateOutputPath, $OutDir, $PublishDir, $TargetFramework)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

trap
{
	throw $PSItem
}

function Get-SingleNodeValue([System.Xml.XmlDocument]$doc,[string]$path)
{
	return $doc.SelectSingleNode($path).FirstChild.Value
}

$xmlDoc = [System.Xml.XmlDocument](Get-Content "$ProjectName.csproj")

$ModuleId = Get-SingleNodeValue $xmlDoc '/Project/PropertyGroup/PackageId'
$Version = Get-SingleNodeValue $xmlDoc '/Project/PropertyGroup/Version'
$ProjectUri = Get-SingleNodeValue $xmlDoc '/Project/PropertyGroup/PackageProjectUrl'
$Description = Get-SingleNodeValue $xmlDoc '/Project/PropertyGroup/Description'
$Author = Get-SingleNodeValue $xmlDoc '/Project/PropertyGroup/Authors'
$Copyright = Get-SingleNodeValue $xmlDoc '/Project/PropertyGroup/Copyright'
$AssemblyName = Get-SingleNodeValue $xmlDoc '/Project/PropertyGroup/AssemblyName'
$CompanyName = Get-SingleNodeValue $xmlDoc '/Project/PropertyGroup/Company'
$CompatiblePSEditions = 'Core'

switch ( $TargetFramework )
{
	'net6.0' {
			$PowerShellVersion = '7.2'
		}
	'net8.0' {
			$PowerShellVersion = '7.4'
		}
	'net9.0' {
			$PowerShellVersion = '7.5'
		}
	'netstandard2.0' {
		$PowerShellVersion = '5.1'
		$CompatiblePSEditions = 'Desktop'
		$ModuleId += '.Desktop'
	}
}

$moduleSettings = @{
	Path = "$OutDir$ModuleId.psd1"
	RootModule = "$AssemblyName.dll"
	ModuleVersion = $Version
	Guid = '9e1459b0-baab-481c-89e2-52d8b4eed335'
	Author = $Author
	CompanyName = $CompanyName
	Copyright = $Copyright
	Description = $Description
	FunctionsToExport = @()
	CmdletsToExport = @('Get-CompressedFileSize','Copy-File')
	VariablesToExport = '*'
	AliasesToExport = @()
	ProjectUri = $ProjectUri
	CompatiblePSEditions = $CompatiblePSEditions
	PowerShellVersion = $PowerShellVersion
}

New-ModuleManifest @moduleSettings

Import-PowerShellDataFile -LiteralPath "$OutDir$ModuleId.psd1" | Export-PowerShellDataFile | Set-Content -LiteralPath "$PublishDir$ModuleId.psd1" -Encoding utf8BOM
