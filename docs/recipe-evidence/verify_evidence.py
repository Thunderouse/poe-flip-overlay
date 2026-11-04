"""Offline research checks only; not an application catalog or game calibration."""
import hashlib
import html
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent

def verify():
    captures = []
    for path in sorted(ROOT.glob('*.metadata.json')):
        meta = json.loads(path.read_text())
        body = (ROOT / meta['bodyFile']).read_bytes()
        assert meta['status'] == 200, path
        assert len(body) == meta['byteLength'], path
        assert hashlib.sha256(body).hexdigest() == meta['sha256'], path
        assert meta['requestedUrl'].startswith('https://www.poewiki.net/w/api.php?'), path
        assert meta['checkedAt'], path
        captures.append(meta['bodyFile'])
    assert len(captures) == 7, captures
    pages = json.loads((ROOT / 'recipe-pages.body').read_text())['query']['pages']
    pages = {p['title']: p['revisions'][0] for p in pages.values()}
    assert len(pages) == 6
    vendor = pages['Vendor']['slots']['main']['*']
    assert vendor == json.loads((ROOT / 'vendor.body').read_text())['parse']['wikitext']['*']
    rows = [
        "|{{il|Jeweller's Orb}}||2x {{il|Orb of Alteration}}||Act 2 ([[Yeena]])",
        "|{{il|Orb of Fusing}}||4x {{il|Jeweller's Orb}}||Act 2 ([[Yeena]])",
        "|{{il|Orb of Scouring}}||4x {{il|Orb of Chance}}||Act 2 ([[Yeena]])",
        "|{{il|Orb of Regret}}||2x {{il|Orb of Scouring}}||Act 2 ([[Yeena]])",
    ]
    for row in rows:
        assert vendor.count(row) == 1, row
    assert '!Orb!!Price!!First Location' in vendor
    assert '| area1        = [[The Forest Encampment]] ([[Act 2]])' in pages['Yeena']['slots']['main']['*']
    cargo = json.loads((ROOT / 'cargo-items.body').read_text())['cargoquery']
    names = [html.unescape(row['title']['name']) for row in cargo]
    assert len(names) == len(set(names)) == 4
    assert set(names) == {"Jeweller's Orb", 'Orb of Fusing', 'Orb of Scouring', 'Orb of Regret'}
    for name in names:
        assert name in pages
    report = {
        'result': 'PASS', 'bodyIntegrityChecks': len(captures),
        'exactVendorRowsChecked': len(rows), 'uniqueCargoItemsChecked': len(names),
        'vendorParseMatchesRevision': True,
        'checkedAt': json.loads((ROOT / 'recipe-pages.metadata.json').read_text())['checkedAt'],
        'revisions': {name: {'revid': p['revid'], 'sourceRevisionTimestamp': p['timestamp']} for name, p in pages.items()},
        'limitations': 'Offline evidence integrity/structure only; not in-game calibration or patch validity.'
    }
    print(json.dumps(report, indent=2))
    return report

if __name__ == '__main__':
    verify()
