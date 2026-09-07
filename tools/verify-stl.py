"""Checks an exported STL against what it is supposed to contain.

The first house model was verified by looking at renders, and the renders hid five missing
openings and four walls bored end to end. Ray-testing the same file found all of it in four
minutes, which is the whole argument for this script.

    python tools/verify-stl.py part.stl checks.json

Every check is a point and what should be there:

    [
      {"at": [-35, -43.25, 17], "expect": "open",  "what": "living room window"},
      {"at": [-20, -43.25, 28], "expect": "solid", "what": "wall above it"}
    ]

A point is "solid" when a ray cast upward from it crosses the surface an odd number of times.
That is exact for a closed mesh and needs nothing but the triangle list.
"""

import json
import struct
import sys


def load(path):
    """Triangles from a binary STL, as three (x, y, z) tuples each."""
    with open(path, 'rb') as f:
        header = f.read(80)
        if header.lstrip()[:5] == b'solid':
            raise SystemExit(f'{path}: ASCII STL - export as binary')

        count = struct.unpack('<I', f.read(4))[0]
        tris = []

        for _ in range(count):
            d = struct.unpack('<12fH', f.read(50))
            tris.append(((d[3], d[4], d[5]), (d[6], d[7], d[8]), (d[9], d[10], d[11])))

    return tris


# Offsets far below any printable feature, and deliberately not round numbers. A ray sent from a
# point that lands exactly on the edge between two triangles is counted by both of them and the
# parity comes out backwards - which is how a dowel read as thin air and an open doorway read as
# solid wall. Three rays from three nowhere-near-round places, and the majority wins.
JITTER = [(0.0, 0.0), (0.000731, 0.001117), (-0.001033, 0.000619)]


def inside(tris, point):
    """Whether the point is within the solid, by parity of crossings straight up."""
    votes = sum(1 for dx, dy in JITTER
                if crossings(tris, (point[0] + dx, point[1] + dy, point[2])) % 2 == 1)

    return votes >= 2


def crossings(tris, point):
    """How many times a ray straight up from the point meets the surface."""
    px, py, pz = point
    hits = 0

    for a, b, c in tris:
        x1, y1, z1 = a
        x2, y2, z2 = b
        x3, y3, z3 = c

        # Barycentric containment in plan, then the height of the plane over the point.
        d = (y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3)
        if abs(d) < 1e-12:
            continue

        l1 = ((y2 - y3) * (px - x3) + (x3 - x2) * (py - y3)) / d
        l2 = ((y3 - y1) * (px - x3) + (x1 - x3) * (py - y3)) / d
        l3 = 1 - l1 - l2
        if l1 < 0 or l2 < 0 or l3 < 0:
            continue

        if l1 * z1 + l2 * z2 + l3 * z3 > pz:
            hits += 1

    return hits


def bounds(tris):
    xs = [v[0] for t in tris for v in t]
    ys = [v[1] for t in tris for v in t]
    zs = [v[2] for t in tris for v in t]
    return (min(xs), min(ys), min(zs)), (max(xs), max(ys), max(zs))


def main(argv):
    if len(argv) < 3:
        raise SystemExit(__doc__)

    tris = load(argv[1])
    checks = json.load(open(argv[2], encoding='utf-8'))

    low, high = bounds(tris)
    print(f'{argv[1]}: {len(tris):,} triangles')
    print(f'  x {low[0]:8.2f} .. {high[0]:8.2f}    '
          f'y {low[1]:8.2f} .. {high[1]:8.2f}    '
          f'z {low[2]:8.2f} .. {high[2]:8.2f}')
    print()

    failed = 0

    for check in checks:
        want = check['expect']
        got = 'solid' if inside(tris, check['at']) else 'open'
        ok = got == want

        if not ok:
            failed += 1

        mark = '  ok ' if ok else 'WRONG'
        print(f'  {mark}  {check.get("what", ""):<34} wanted {want:<5} got {got}')

    print()
    if failed:
        print(f'{failed} of {len(checks)} checks failed.')
        return 1

    print(f'All {len(checks)} checks passed.')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
