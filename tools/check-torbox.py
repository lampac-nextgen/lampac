#!/usr/bin/env python3
"""Compile manifest sources unchanged and run offline checks in a disposable CWD.

No test project or test framework is added to the solution. The temporary console
project references the real Shared project and compiles the dynamic manifest tree.
"""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
from xml.sax.saxutils import quoteattr

root = Path(__file__).resolve().parents[1]
module = root / 'Modules/TorBox'
sources = json.loads((module / 'manifest.json').read_text())['tree']
assert set(sources) == {p.name for p in module.glob('*.cs')}, 'manifest/source mismatch'
dotnet = os.environ.get('DOTNET', 'dotnet')
with tempfile.TemporaryDirectory(prefix='lampac-torbox-checks-') as directory:
    work = Path(directory)
    # A runner may also have a newer SDK installed. Keep the temporary project
    # on the same SDK policy as the repository, despite its separate CWD.
    shutil.copy2(root / 'global.json', work / 'global.json')
    items = ''.join(f'<Compile Include={quoteattr(str(module / name))} />' for name in sources)
    items += f'<Compile Include={quoteattr(str(root / "tools/torbox/Checks.cs"))} />'
    shared = quoteattr(str(root / 'Shared/Shared.csproj'))
    (work / 'Checks.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk.Web">
      <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType>
      <EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
      <ItemGroup><ProjectReference Include={shared} />{items}</ItemGroup>
    </Project>''')
    subprocess.run([dotnet, 'build', str(work / 'Checks.csproj'), '--nologo',
                    '-p:PlaywrightPlatform=linux', '-v:minimal'], cwd=work, check=True)
    subprocess.run([dotnet, str(work / 'bin/Debug/net10.0/Checks.dll')], cwd=work, check=True)
