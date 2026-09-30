param(
    [string]$Configuration = "Debug",
    [string]$OutputDirectory = "artifacts\ui-previews"
)

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $repoRoot "ELARA.csproj"
$resolvedOutput = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))

dotnet run --project $projectPath -c $Configuration -- --preview --preview-output $resolvedOutput
exit $LASTEXITCODE
