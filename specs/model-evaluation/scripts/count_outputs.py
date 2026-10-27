import glob
import os

ROOT = r'D:\src\DreamGenClone\artifacts\prompt-test'
for suffix, label in (('', '800-token pass'), ('_2k', '2000-token sample A'), ('_2kb', '2000-token sample B')):
    files = sorted(glob.glob(os.path.join(ROOT, f'results{suffix}', '*.md')))
    empties, partial = [], []
    for f in files:
        raw = open(f, encoding='utf-8').read()
        body = raw.split('## Content', 1)[1].split('## Reasoning')[0].strip()
        if body in ('(empty)', ''):
            empties.append(os.path.basename(f)[:-3])
        if 'stream_error: None' not in raw and 'stream_error: ' in raw:
            line = [l for l in raw.splitlines() if l.startswith('- stream_error:')][0]
            if line.split(':', 1)[1].strip() not in ('None', ''):
                partial.append(os.path.basename(f)[:-3])
        elif '- finish_reason: length' in raw:
            partial.append(os.path.basename(f)[:-3] + ' (hit token cap)')
    print(f'{label:22} {len(files):2} files | empty: {empties or "none"} | incomplete: {partial or "none"}')
