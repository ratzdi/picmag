# MIT License
#
# Copyright (c) 2025 Dimitri Ratz
#
# Permission is hereby granted, free of charge, to any person obtaining a copy
# of this software and associated documentation files (the "Software"), to deal
# in the Software without restriction, including without limitation the rights
# to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
# copies of the Software, and to permit persons to whom the Software is
# furnished to do so, subject to the following conditions:
#
# The above copyright notice and this permission notice shall be included in all
# copies or substantial portions of the Software.
#
# THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
# IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
# FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
# AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
# LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
# OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
# SOFTWARE.

import json
import sys
import urllib.parse
import urllib.request
from pathlib import Path

root = Path(__file__).resolve().parent.parent / 'picmag' / 'tests' / 'in' / '5-person'
train = root / 'train'
probe = root / 'probe'
probe_negative = root / 'probe-negative'
train.mkdir(parents=True, exist_ok=True)
probe.mkdir(parents=True, exist_ok=True)
probe_negative.mkdir(parents=True, exist_ok=True)

target_name = 'train'
if '--probe' in sys.argv:
    target_name = 'probe'
if '--probe-negative' in sys.argv:
    target_name = 'probe-negative'

if target_name == 'train':
    target_dir = train
    target_prefix = 'obama_'
    search_queries = ['Barack Obama portrait filetype:bitmap']
elif target_name == 'probe':
    target_dir = probe
    target_prefix = 'obama_probe_'
    search_queries = ['Barack Obama portrait filetype:bitmap']
else:
    target_dir = probe_negative
    target_prefix = 'other_probe_'
    search_queries = [
        'Angela Merkel portrait filetype:bitmap',
        'Joe Biden portrait filetype:bitmap',
        'Emmanuel Macron portrait filetype:bitmap',
        'Justin Trudeau portrait filetype:bitmap',
    ]

required_count = 5

existing_target = sorted(target_dir.glob(f'{target_prefix}*.jpg'))
existing_train = sorted(train.glob('obama_*.jpg'))

license_file = root / 'LICENSES.tsv'
used_urls = set()
if license_file.exists():
    for line in license_file.read_text(encoding='utf-8').splitlines():
        parts = line.split('\t')
        if len(parts) >= 3 and parts[2].startswith('http'):
            used_urls.add(parts[2])

params = {
    'action': 'query',
    'generator': 'search',
    'gsrnamespace': '6',
    'gsrlimit': '100',
    'prop': 'imageinfo',
    'iiprop': 'url|extmetadata',
    'format': 'json',
}
items = []
for search_query in search_queries:
    query_params = dict(params)
    query_params['gsrsearch'] = search_query
    api = 'https://commons.wikimedia.org/w/api.php?' + urllib.parse.urlencode(query_params)
    req = urllib.request.Request(api, headers={'User-Agent': 'picmag-test-dataset/1.0'})

    with urllib.request.urlopen(req, timeout=25) as response:
        data = json.load(response)

    pages = data.get('query', {}).get('pages', {})
    for page in pages.values():
        title = page.get('title', '')
        if not title.lower().endswith(('.jpg', '.jpeg')):
            continue

        imageinfo = (page.get('imageinfo') or [{}])[0]
        url = imageinfo.get('url')
        if not url:
            continue

        meta = imageinfo.get('extmetadata') or {}
        license_name = (meta.get('LicenseShortName') or {}).get('value', 'Unknown')
        license_url = (meta.get('LicenseUrl') or {}).get('value', '')
        artist = (meta.get('Artist') or {}).get('value', 'Unknown')

        items.append((title, url, license_name, license_url, artist))

items.sort(key=lambda x: x[0].lower())

if not license_file.exists():
    license_file.write_text(
        'Dataset source: Wikimedia Commons\n'
        'Person queries: Barack Obama portrait, Angela Merkel portrait\n'
        'Format: local_filename\tcommons_title\tfile_url\tlicense\tlicense_url\tartist\n\n',
        encoding='utf-8',
    )

lines = []
count = len(existing_target)

for title, url, license_name, license_url, artist in items:
    if count >= required_count:
        break

    if url in used_urls:
        continue
    used_urls.add(url)

    lower_title = title.lower()
    if target_name == 'probe' and any(candidate.name.lower() in lower_title for candidate in existing_train):
        continue

    target = target_dir / f'{target_prefix}{count + 1:02d}.jpg'
    try:
        image_req = urllib.request.Request(url, headers={'User-Agent': 'picmag-test-dataset/1.0'})
        with urllib.request.urlopen(image_req, timeout=25) as image_response:
            payload = image_response.read()

        if len(payload) < 20_000:
            continue

        target.write_bytes(payload)
    except Exception:
        continue

    lines.append(
        f'{target.name}\t{title}\t{url}\t{license_name}\t{license_url}\t{artist}'
    )
    count += 1

if lines:
    with license_file.open('a', encoding='utf-8') as handle:
        handle.write('\n'.join(lines) + '\n')

print(f'Train images now: {len(list(train.glob("obama_*.jpg")))}')
print(f'Probe images now: {len(list(probe.glob("obama_probe_*.jpg")))}')
print(f'Negative probe images now: {len(list(probe_negative.glob("other_probe_*.jpg")))}')
