param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root
try {
    $publishDir = Join-Path $root "publish"
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

    dotnet publish "App\Mwrites.App.csproj" -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "No se pudo publicar Mwrites." }

    Get-ChildItem $publishDir -File -Filter "*.pdb" | Remove-Item -Force

    $exe = Join-Path $publishDir "Mwrites.exe"
    $sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host ""
    Write-Host ("Mwrites.exe  {0} MB" -f $sizeMb) -ForegroundColor Green
    Write-Host "Publicado en publish\" -ForegroundColor Green
}
finally {
    Pop-Location
}
