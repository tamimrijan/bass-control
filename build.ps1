# Builds Bass Control and its installer using the C# compiler that ships with Windows.
# Output: dist\BassControl-Setup.exe
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Add-Type -AssemblyName System.Drawing

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$build = Join-Path $PSScriptRoot 'build'
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force $build, $dist | Out-Null

# --- icon.png -> icon.ico (classic BMP frames, readable by every Windows API) ---
function New-Ico([string]$png, [string]$ico, [int[]]$sizes) {
    $src = [System.Drawing.Image]::FromFile($png)
    $frames = foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
        $g.DrawImage($src, 0, 0, $s, $s); $g.Dispose()
        $ms = New-Object System.IO.MemoryStream
        $w = New-Object System.IO.BinaryWriter $ms
        $w.Write([int]40); $w.Write([int]$s); $w.Write([int]($s * 2)); $w.Write([int16]1); $w.Write([int16]32)
        $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
        for ($y = $s - 1; $y -ge 0; $y--) { for ($x = 0; $x -lt $s; $x++) { $c = $bmp.GetPixel($x, $y); $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A) } }
        $maskRow = [int]([math]::Ceiling($s / 32) * 4)
        $w.Write((New-Object byte[] ($maskRow * $s)))
        $bmp.Dispose()
        , @($s, $ms.ToArray())
    }
    $src.Dispose()
    $out = New-Object System.IO.BinaryWriter ([System.IO.File]::Create($ico))
    $out.Write([int16]0); $out.Write([int16]1); $out.Write([int16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($f in $frames) {
        $s = $f[0]; $data = $f[1]
        $out.Write([byte]($s % 256)); $out.Write([byte]($s % 256)); $out.Write([byte]0); $out.Write([byte]0)
        $out.Write([int16]1); $out.Write([int16]32); $out.Write([int]$data.Length); $out.Write([int]$offset)
        $offset += $data.Length
    }
    foreach ($f in $frames) { $out.Write([byte[]]$f[1]) }
    $out.Close()
}
New-Ico 'assets\icon.png' "$build\icon.ico" @(16, 20, 24, 32, 40, 48, 64, 128, 256)

# --- manifests ---
function New-Manifest([string]$path, [string]$level) {
@"
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="2.0.0.0" name="BassControl.app"/>
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security><requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
      <requestedExecutionLevel level="$level" uiAccess="false"/>
    </requestedPrivileges></security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1"><application>
    <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}"/>
  </application></compatibility>
  <application xmlns="urn:schemas-microsoft-com:asm.v3"><windowsSettings>
    <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true</dpiAware>
  </windowsSettings></application>
  <dependency><dependentAssembly>
    <assemblyIdentity type="win32" name="Microsoft.Windows.Common-Controls" version="6.0.0.0" processorArchitecture="*" publicKeyToken="6595b64144ccf1df" language="*"/>
  </dependentAssembly></dependency>
</assembly>
"@ | Set-Content -Encoding utf8 $path
}
New-Manifest "$build\app.manifest" 'asInvoker'
New-Manifest "$build\setup.manifest" 'requireAdministrator'

$common = @('/nologo', '/target:winexe', '/optimize+', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll',
    "/win32icon:$build\icon.ico", "/resource:assets\icon.png,icon.png", "/resource:$build\icon.ico,icon.ico")

# --- the app ---
& $csc @common "/win32manifest:$build\app.manifest" "/out:$build\BassControl.exe" src\Common.cs src\BassApp.cs
if ($LASTEXITCODE) { throw 'App build failed' }

# --- the installer (embeds the app and the Equalizer APO installer) ---
# Equalizer APO is not stored in the repo; it is downloaded once into build\.
$eq = Get-ChildItem 'EqualizerAPO*.exe', "$build\EqualizerAPO*.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $eq) {
    $eqFile = "$build\EqualizerAPO-x64-1.4.2.exe"
    Write-Host 'Downloading Equalizer APO 1.4.2...'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    try { Invoke-WebRequest 'https://sourceforge.net/projects/equalizerapo/files/1.4.2/EqualizerAPO-x64-1.4.2.exe/download' -OutFile $eqFile -UserAgent 'Wget' -UseBasicParsing } catch { }
    if (-not (Test-Path $eqFile) -or (Get-Item $eqFile).Length -lt 1MB) {
        Remove-Item $eqFile -ErrorAction SilentlyContinue
        throw 'Could not download Equalizer APO. Download EqualizerAPO-x64-1.4.2.exe from https://sourceforge.net/projects/equalizerapo/ and put it next to build.ps1'
    }
    $eq = Get-Item $eqFile
}
& $csc @common "/win32manifest:$build\setup.manifest" "/out:$dist\BassControl-Setup.exe" `
    "/resource:$build\BassControl.exe,BassControl.exe" "/resource:$($eq.FullName),EqualizerAPO.exe" src\Common.cs src\Setup.cs
if ($LASTEXITCODE) { throw 'Setup build failed' }

Get-Item "$build\BassControl.exe", "$dist\BassControl-Setup.exe" | Select-Object Name, @{n = 'Size (KB)'; e = { [int]($_.Length / 1KB) } }
