[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$IsccPath =
        "C:\Program Files (x86)\MySoftware\Inno Setup 7\ISCC.exe",
    [switch]$SkipRestore
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path `
    $projectRoot `
    "src\Reminder.App\Reminder.App.csproj"
$installerScript = Join-Path `
    $projectRoot `
    "installer\Reminder.iss"
$artifactsRoot = Join-Path $projectRoot "artifacts"
$publishDirectory = Join-Path `
    $artifactsRoot `
    "publish\$RuntimeIdentifier"
$installerDirectory = Join-Path `
    $artifactsRoot `
    "installer"

function Assert-ChildPath {
    param(
        [Parameter(Mandatory)]
        [string]$Parent,
        [Parameter(Mandatory)]
        [string]$Child
    )

    $normalizedParent = (
        [System.IO.Path]::GetFullPath($Parent)).TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar)
    $normalizedChild = [System.IO.Path]::GetFullPath($Child)

    if (!$normalizedChild.StartsWith(
            "$normalizedParent$([System.IO.Path]::DirectorySeparatorChar)",
            [System.StringComparison]::OrdinalIgnoreCase))
    {
        throw "Refusing to clean a path outside the project: $normalizedChild"
    }
}

if ($RuntimeIdentifier -ne "win-x64")
{
    throw "The initial release only supports RuntimeIdentifier=win-x64."
}

if (!(Test-Path -LiteralPath $projectPath -PathType Leaf))
{
    throw "Project file not found: $projectPath"
}

if (!(Test-Path -LiteralPath $installerScript -PathType Leaf))
{
    throw "Inno Setup script not found: $installerScript"
}

if (!(Test-Path -LiteralPath $IsccPath -PathType Leaf))
{
    throw "Inno Setup compiler not found: $IsccPath"
}

[xml]$projectXml = Get-Content `
    -LiteralPath $projectPath `
    -Raw `
    -Encoding UTF8
$version = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version))
{
    throw "The project version could not be read."
}

Assert-ChildPath -Parent $projectRoot -Child $publishDirectory
Assert-ChildPath -Parent $projectRoot -Child $installerDirectory

foreach ($directory in @($publishDirectory, $installerDirectory))
{
    if (Test-Path -LiteralPath $directory)
    {
        Remove-Item `
            -LiteralPath $directory `
            -Recurse `
            -Force
    }

    New-Item `
        -ItemType Directory `
        -Path $directory `
        -Force | Out-Null
}

if (!$SkipRestore)
{
    Write-Host "Restoring packages for $RuntimeIdentifier..."
    & dotnet restore `
        $projectPath `
        --runtime $RuntimeIdentifier

    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet restore failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Publishing Reminder $version ($RuntimeIdentifier)..."
& dotnet publish `
    $projectPath `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $publishDirectory `
    --no-restore `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=false `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$publishedExecutable = Join-Path `
    $publishDirectory `
    "Reminder.App.exe"
if (!(Test-Path -LiteralPath $publishedExecutable -PathType Leaf))
{
    throw "Reminder.App.exe was not found in the publish directory."
}

Write-Host "Compiling the Inno Setup installer..."
$isccArguments = @(
    "/DMyAppVersion=$version",
    "/DPublishDir=$publishDirectory",
    "/DOutputDir=$installerDirectory",
    $installerScript
)
& $IsccPath @isccArguments

if ($LASTEXITCODE -ne 0)
{
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path `
    $installerDirectory `
    "Reminder-Setup-$version-$RuntimeIdentifier.exe"
if (!(Test-Path -LiteralPath $installerPath -PathType Leaf))
{
    throw "The expected installer was not found: $installerPath"
}

$installerFile = Get-Item -LiteralPath $installerPath
$installerHash = Get-FileHash `
    -LiteralPath $installerPath `
    -Algorithm SHA256
$sizeMiB = [Math]::Round(
    $installerFile.Length / 1MB,
    2)

Write-Host ""
Write-Host "Installer generation completed."
Write-Host "Path: $installerPath"
Write-Host "Size: $sizeMiB MiB"
Write-Host "SHA-256: $($installerHash.Hash)"
