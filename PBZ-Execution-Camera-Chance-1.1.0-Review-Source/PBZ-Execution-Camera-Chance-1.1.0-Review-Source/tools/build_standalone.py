"""Build a portable Windows manager with a curated, embedded UE4SS + mod payload."""
from pathlib import Path
import hashlib
import os
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
VERSION = '1.1.0'
NAME = f'PBZ-Execution-Camera-Chance-{VERSION}-Standalone'
RUNTIME = Path(os.environ.get('PBZ_RUNTIME_SOURCE', ROOT / 'deps' / 'ue4ss'))
CSC = Path(os.environ.get('WINDIR', r'C:\Windows')) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
BUILD = ROOT / 'build' / VERSION
DIST = ROOT / 'dist' / NAME
BUILD.mkdir(parents=True, exist_ok=True)
DIST.mkdir(parents=True, exist_ok=True)
LOADER = (
    'dwmapi.dll', 'ue4ss/UE4SS.dll', 'ue4ss/MemberVariableLayout.ini',
    'ue4ss/UE4SS-settings.ini', 'ue4ss/LICENSE',
    'ue4ss/UE4SS_Signatures/GUObjectArray.lua', 'ue4ss/UE4SS_Signatures/GNatives.lua',
)
FILES = {rel: (RUNTIME / rel).read_bytes() for rel in LOADER}
# Verify the same two dependency identities tested with the game.
EXPECTED = {
    'ue4ss/UE4SS.dll': 'C273E7228687B647226FD63D7CA65888D313ECE6BE18996EC1320D00A5EFE1F8',
    'ue4ss/MemberVariableLayout.ini': '8C5017D84C4553632B2FFBA320FF62D3998B273640E302A28B60D4BE98703FD0',
}
for rel, digest in EXPECTED.items():
    assert hashlib.sha256(FILES[rel]).hexdigest().upper() == digest, f'Untested runtime: {rel}'
settings = FILES['ue4ss/UE4SS-settings.ini'].decode('utf-8-sig')
for field in ('ConsoleEnabled', 'GuiConsoleEnabled'):
    settings, count = re.subn(rf'(?m)^({field}\s*=\s*)\d+', r'\g<1>0', settings)
    assert count == 1, field
FILES['ue4ss/UE4SS-settings.ini'] = settings.encode('utf-8')
for rel in ('enabled.txt', 'Scripts/main.lua', 'Scripts/policy.lua', 'Scripts/sessions.lua', 'Scripts/config.lua'):
    FILES['mod/' + rel] = (ROOT / 'mod/PBZExecutionCameraChance' / rel).read_bytes()

manifest = ET.Element('Payload', Version=VERSION)
payload = BUILD / 'payload.zip'
with zipfile.ZipFile(payload, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for rel, data in sorted(FILES.items()):
        info = zipfile.ZipInfo(rel, date_time=(2026, 10, 30, 0, 0, 0))
        info.compress_type = zipfile.ZIP_DEFLATED
        archive.writestr(info, data, compresslevel=9)
        ET.SubElement(manifest, 'File', Path=rel, SHA256=hashlib.sha256(data).hexdigest().upper())
ET.ElementTree(manifest).write(BUILD / 'payload-manifest.xml', encoding='utf-8', xml_declaration=True)

exe = DIST / 'PBZ-Execution-Camera-Manager.exe'
references = ['System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll',
              'System.Xml.dll', 'System.Xml.Linq.dll', 'System.IO.Compression.dll']
command = [str(CSC), '/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output',
           '/out:' + str(exe), '/win32manifest:' + str(ROOT / 'manager/app.manifest'),
           '/resource:' + str(payload) + ',PBZ.Payload',
           '/resource:' + str(BUILD / 'payload-manifest.xml') + ',PBZ.Manifest']
command += ['/reference:' + reference for reference in references]
command += [str(ROOT / 'manager/Manager.cs')]
subprocess.run(command, check=True)
release_files = {'PBZ-Execution-Camera-Manager.exe', '安装与卸载说明.txt', 'README.md',
                 'CHANGELOG.md', 'LICENSE.txt', 'THIRD-PARTY-NOTICES.txt', 'UE4SS-LICENSE.txt', 'SHA256SUMS.txt'}
for name in ('README.md', 'CHANGELOG.md', 'LICENSE.txt', 'THIRD-PARTY-NOTICES.txt', '安装与卸载说明.txt'):
    shutil.copyfile(ROOT / name, DIST / name)
(DIST / 'UE4SS-LICENSE.txt').write_bytes(FILES['ue4ss/LICENSE'])
actual = {p.name for p in DIST.iterdir() if p.is_file()}
assert actual - release_files == set(), f'Unexpected release contents: {actual - release_files}'
assert not any(p.is_dir() for p in DIST.iterdir()), 'Unexpected directory in distribution'
(DIST / 'SHA256SUMS.txt').write_text(''.join(
    hashlib.sha256((DIST / name).read_bytes()).hexdigest().upper() + '  ' + name + '\n'
    for name in sorted(release_files - {'SHA256SUMS.txt'})
), encoding='utf-8-sig')
output = DIST.parent / (NAME + '.zip')
with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for name in sorted(release_files):
        archive.write(DIST / name, NAME + '/' + name)
output.with_name(NAME + '-SHA256.txt').write_text(
    hashlib.sha256(output.read_bytes()).hexdigest().upper() + '  ' + output.name + '\n', encoding='ascii')
print(f'{output}\n{output.stat().st_size:,} bytes; {len(release_files)} release files; {len(FILES)} embedded payload files.')
