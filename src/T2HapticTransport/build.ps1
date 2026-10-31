param([switch]$Analyze)
$ErrorActionPreference = 'Stop'
$kit = 'C:\Program Files (x86)\Windows Kits\10'
$kitVersion = '10.0.26100.0'
$compilerRoot = Get-ChildItem 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC' -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1 -ExpandProperty FullName
if (!$compilerRoot) { throw 'Visual Studio 2022 C++ Build Tools are required.' }
$buildOutput = Join-Path $PSScriptRoot 'out'
New-Item -ItemType Directory -Force -Path $buildOutput | Out-Null
$includes = @("$kit\Include\$kitVersion\km", "$kit\Include\$kitVersion\shared", "$kit\Include\$kitVersion\ucrt", "$kit\Include\wdf\kmdf\1.15", "$compilerRoot\include")
$compileArgs = @('/nologo','/c','/kernel','/W4','/WX','/GS','/Zp8','/D_AMD64_','/DAMD64','/D_WIN64','/D_WIN32_WINNT=0x0A00','/DNTDDI_VERSION=0x0A000000')
$compileArgs += '/external:W0'
if ($Analyze) { $compileArgs += '/analyze'; $compileArgs += '/analyze:external-' }
foreach ($include in $includes) { $compileArgs += '/external:I' + $include }
$compileArgs += '/Fo' + (Join-Path $buildOutput 'Driver.obj')
$compileArgs += Join-Path $PSScriptRoot 'Driver.c'
& "$compilerRoot\bin\Hostx64\x64\cl.exe" @compileArgs
if ($LASTEXITCODE) { throw "Compiler failed: $LASTEXITCODE" }
& "$compilerRoot\bin\Hostx64\x64\link.exe" /nologo /driver /subsystem:native,10.00 /entry:FxDriverEntry /nodefaultlib /machine:x64 /integritycheck /dynamicbase /nxcompat ("/out:" + (Join-Path $buildOutput 'T2Haptic.sys')) (Join-Path $buildOutput 'Driver.obj') "/libpath:$kit\Lib\$kitVersion\km\x64" "/libpath:$kit\Lib\wdf\kmdf\x64\1.15" ntoskrnl.lib hal.lib wmilib.lib BufferOverflowFastFailK.lib WdfDriverEntry.lib WdfLdr.lib
if ($LASTEXITCODE) { throw "Linker failed: $LASTEXITCODE" }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'T2Haptic.inf') -Destination $buildOutput
