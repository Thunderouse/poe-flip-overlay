from pathlib import Path
import subprocess, os, sys, shutil
root = Path(__file__).resolve().parents[2]
env = dict(os.environ)
dotnet = os.environ.get('DOTNET_HOST_PATH') or shutil.which('dotnet')
if not dotnet: raise SystemExit('The .NET SDK pinned in global.json must be on PATH.')
cmd = [dotnet, 'test', str(Path(__file__).with_name('FlipOverlay.Core.Tests.csproj')), '-c', 'Release', '--nologo']
if '--no-restore' in sys.argv: cmd.append('--no-restore')
p = subprocess.run(cmd, cwd=root, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
with (root/'docs/core-test-evidence.txt').open('a') as f:
    f.write('\n=== '+sys.argv[1]+' ===\n'+' '.join(cmd)+'\n'+p.stdout+'\nExit: '+str(p.returncode)+'\n')
print(p.stdout)
print('Exit:', p.returncode)
sys.exit(p.returncode)
