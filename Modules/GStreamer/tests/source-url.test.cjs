const { mkdtempSync, writeFileSync, readFileSync, rmSync } = require('node:fs');
const { tmpdir } = require('node:os');
const path = require('node:path');
const { execFileSync } = require('node:child_process');

// Compile the real service and URL guard; replace only media/native/network dependencies.
const root = path.resolve(__dirname, '../../..');
const temp = mkdtempSync(path.join(tmpdir(), 'gst-source-url-'));
try {
  writeFileSync(path.join(temp, 'check.csproj'), `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup><Compile Include="${root}/Modules/GStreamer/Services/GService.cs"/><Compile Include="${root}/Shared/Services/Utilities/SafeHttpUrl.cs"/></ItemGroup></Project>`);
  writeFileSync(path.join(temp, 'Program.cs'), readFileSync(path.join(__dirname, 'source-url.fixture'), 'utf8'));
  const command = ['run', '--project', path.join(temp, 'check.csproj'), '--nologo'];
  if (process.argv.includes('--docker')) {
    execFileSync('docker', ['run', '--rm', '-v', `${root}:${root}:ro`, '-v', `${temp}:${temp}`,
      'mcr.microsoft.com/dotnet/sdk:10.0', 'dotnet', ...command], { stdio: 'inherit' });
  } else {
    execFileSync('dotnet', command, { stdio: 'inherit' });
  }
} finally {
  rmSync(temp, { recursive: true, force: true });
}
