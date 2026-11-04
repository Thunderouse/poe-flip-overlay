"""One-shot research retrieval, not application runtime. Public documented MediaWiki only."""
import datetime
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parent
LEDGER = ROOT / 'sources-ledger.json'
SCRIPT = Path.home() / '.hermes/skills/research/grounded-citations/scripts/sources.py'

def fetch(name, url):
    target = ROOT / (name + '.body')
    if target.exists():
        raise RuntimeError('Refusing to overwrite evidence: ' + name)
    checked = datetime.datetime.now(datetime.timezone.utc).isoformat()
    req = urllib.request.Request(url, headers={'User-Agent': 'PoEFlipOverlayResearch/0.1 (manual community-recipe verification; no game access)', 'Accept': 'application/json,text/html'})
    try:
        response = urllib.request.urlopen(req, timeout=60)
    except urllib.error.HTTPError as e:
        response = e
    with response:
        body = response.read()
        meta = {'requestedUrl': url, 'responseUrl': response.url, 'checkedAt': checked, 'status': response.status, 'headers': dict(response.headers), 'bodyFile': target.name, 'sha256': hashlib.sha256(body).hexdigest(), 'byteLength': len(body)}
    target.write_bytes(body)
    (ROOT / (name + '.metadata.json')).write_text(json.dumps(meta, indent=2) + '\n')
    print(json.dumps(meta, indent=2))
    subprocess.run([sys.executable, str(SCRIPT), '--ledger', str(LEDGER), 'add', url, '--title', name], check=True)
    print(body.decode('utf-8', errors='replace')[:18000])

if __name__ == '__main__':
    fetch(sys.argv[1], sys.argv[2])
