param(
  [string]$AndroidSdk = "$env:LOCALAPPDATA\Android\Sdk",
  [string]$JavaHome = 'C:\Program Files\Android\Android Studio\jbr'
)
$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$buildDir = Join-Path $projectDir 'build'
$payloadDir = Join-Path $buildDir 'payload'
$releaseDir = Join-Path $projectDir 'release'
New-Item -ItemType Directory -Force -Path $buildDir,$payloadDir,$releaseDir | Out-Null
foreach ($name in @('adb.exe','AdbWinApi.dll','AdbWinUsbApi.dll')) {
  Copy-Item -LiteralPath (Join-Path "$AndroidSdk\platform-tools" $name) -Destination $payloadDir -Force
}
Copy-Item -LiteralPath "$AndroidSdk\platform-tools\NOTICE.txt" -Destination "$payloadDir\NOTICE-platform-tools.txt" -Force
Copy-Item -LiteralPath "$AndroidSdk\build-tools\36.1.0\aapt.exe" -Destination $payloadDir -Force
Copy-Item -LiteralPath "$AndroidSdk\build-tools\36.1.0\lib\apksigner.jar" -Destination $payloadDir -Force
Copy-Item -LiteralPath "$AndroidSdk\build-tools\36.1.0\NOTICE.txt" -Destination "$payloadDir\NOTICE-build-tools.txt" -Force
Copy-Item -LiteralPath "$projectDir\aurora-helper\app\build\outputs\apk\debug\app-debug.apk" -Destination "$payloadDir\aurora-helper.apk" -Force
Copy-Item -LiteralPath "$AndroidSdk\platforms\android-36\android.jar" -Destination "$payloadDir\android.jar" -Force
if (-not (Test-Path -LiteralPath "$projectDir\wrapper-menu-key.p12")) {
  Write-Output 'Generating a local beta-only menu-entry key. This key is embedded in the beta and is not a security credential.'
  & "$JavaHome\bin\keytool.exe" -genkeypair -keystore "$projectDir\wrapper-menu-key.p12" -storetype PKCS12 -storepass menuentry -keypass menuentry -alias blindshell-menu -dname 'CN=BlindShell Updater Menu Entry' -keyalg RSA -keysize 2048 -validity 10000
  if ($LASTEXITCODE -ne 0) { throw 'Could not generate the local beta menu-entry key' }
}
Copy-Item -LiteralPath "$projectDir\wrapper-menu-key.p12" -Destination "$payloadDir\wrapper-menu-key.p12" -Force
if (-not (Test-Path -LiteralPath "$payloadDir\java\bin\java.exe")) {
  Copy-Item -LiteralPath $JavaHome -Destination "$payloadDir\java" -Recurse -Force
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipPath = Join-Path $buildDir 'tools.zip'
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath }
[System.IO.Compression.ZipFile]::CreateFromDirectory($payloadDir,$zipPath,[System.IO.Compression.CompressionLevel]::Optimal,$false)
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outputExe = Join-Path $releaseDir 'BlindShellUpdater-Accessible.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /debug:pdbonly "/out:$outputExe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.Linq.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:System.Web.Extensions.dll "/reference:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\UIAutomationProvider\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationProvider.dll" "/reference:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\UIAutomationTypes\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationTypes.dll" "/reference:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll" "/resource:$zipPath,tools.zip" "/resource:$projectDir\README.md,readme.txt" "$projectDir\Updater.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
Copy-Item -LiteralPath "$projectDir\README.md" -Destination $releaseDir -Force
Copy-Item -LiteralPath "$projectDir\LICENSE.txt" -Destination $releaseDir -Force
$hash = Get-FileHash -LiteralPath $outputExe -Algorithm SHA256
"$($hash.Hash.ToLower())  BlindShellUpdater-Accessible.exe" | Set-Content -LiteralPath "$releaseDir\SHA256SUMS.txt" -Encoding ascii
Write-Output "Built $outputExe"
