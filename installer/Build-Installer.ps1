param([switch]$Ci)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$repoUrl = 'https://github.com/Rhahee/aycd-with-your-thermal.git'
$branch = 'main'

Push-Location $root
try {
    if (-not $Ci -and (git status --porcelain).Length -ne 0) {
        throw 'Commit semua perubahan sumber sebelum membangun installer.'
    }
    if (-not $Ci -and ((git remote get-url origin).Trim() -ne $repoUrl -or
        (git branch --show-current).Trim() -ne $branch)) {
        throw 'Remote dan branch Git tidak cocok dengan sumber update bawaan.'
    }
    $model = Get-Content -LiteralPath (Join-Path $root 'Models.cs') -Raw
    if (-not $model.Contains("DefaultRepositoryUrl = `"$repoUrl`"") -or
        -not $model.Contains("DefaultBranch = `"$branch`"")) {
        throw 'Sumber update bawaan di Models.cs tidak cocok dengan installer.'
    }
    $commit = (git rev-parse HEAD).Trim()
    $version = ([xml](Get-Content -LiteralPath (Join-Path $root 'PrintToDo.csproj') -Raw)).Project.PropertyGroup.Version
    $iss = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'PrintToDo.iss') -Raw
    if (-not $iss.Contains("#define MyAppVersion `"$version`"")) {
        throw 'Versi installer dan proyek tidak sama.'
    }
    if ($Ci) {
        if ($env:GITHUB_REF_NAME -ne "v$version" -or $env:GITHUB_SHA -ne $commit) {
            throw 'Tag rilis atau commit Actions tidak cocok dengan versi aplikasi.'
        }
    }
    else {
        $remoteLine = git ls-remote origin "refs/heads/$branch"
        if ($LASTEXITCODE -ne 0 -or -not $remoteLine -or
            ($remoteLine -split '\s+')[0] -ne $commit) {
            throw 'Push commit terbaru ke repo sebelum membangun installer.'
        }
    }

    $dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnet)) {
        $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    }
    & $dotnet publish 'PrintToDo.csproj' -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -o 'dist' --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Build rilis gagal.' }

    $hash = [System.Security.Cryptography.SHA256]::HashData(
        [System.Text.Encoding]::UTF8.GetBytes($repoUrl + "`n" + $branch))
    $key = [System.Convert]::ToHexString($hash).Substring(0, 16).ToLowerInvariant()
    [System.IO.File]::WriteAllText((Join-Path $root 'dist\update-commit.txt'),
        $key + "`n" + $commit + "`n")

    $prerequisites = Join-Path $PSScriptRoot 'prerequisites'
    New-Item -ItemType Directory -Path $prerequisites -Force | Out-Null
    $bootstrapper = Join-Path $prerequisites 'MicrosoftEdgeWebView2Setup.exe'
    if (-not (Test-Path -LiteralPath $bootstrapper)) {
        Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' `
            -OutFile $bootstrapper
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $bootstrapper
    if ($signature.Status -ne 'Valid' -or
        $signature.SignerCertificate.Subject -notmatch 'Microsoft') {
        throw 'Tanda tangan pemasang WebView2 tidak valid.'
    }

    $compiler = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $compiler) {
        throw 'ISCC.exe tidak ditemukan. Pasang Inno Setup 6 terlebih dahulu.'
    }
    & $compiler (Join-Path $PSScriptRoot 'PrintToDo.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Pembuatan installer gagal.' }

    $archive = Join-Path $PSScriptRoot "output\AYCD-Printer-$version-win-x64.zip"
    Compress-Archive -LiteralPath @(
        (Join-Path $root 'dist\PrintToDo.exe'),
        (Join-Path $root 'dist\assets'),
        (Join-Path $root 'dist\update-commit.txt')
    ) -DestinationPath $archive -Force
}
finally {
    Pop-Location
}
