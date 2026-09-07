"""Measure the separate pieces in an STL and check each against the opening it has to fit.

The joinery is exported as one file holding several loose parts, so "is it right" is a question
about each piece's own size, not about the file's bounding box. Pieces are found by welding
vertices and walking the triangles that share them.

    python tools/fit-check.py build/house2/frames-and-door.stl
    python tools/fit-check.py build/house2/glazing.stl
"""
import os
import struct
import sys
from collections import defaultdict

# What each piece has to go into, in mm, with the gap it is meant to leave - measured across the
# whole dimension, so half of it falls on each side. Frames go into the walls' openings; panes go
# into the lights inside the frames.
WANTED = {
    "frames-and-door": (0.4, [
        ("front and side windows", 16.0, 13.0),
        ("the wide back window", 18.0, 13.0),
        ("the narrow back window", 10.0, 13.0),
        ("the front door", 11.0, 23.0),
    ]),
    "glazing": (0.2, [
        ("the 16 mm window's light", 13.2, 10.2),
        ("the 18 mm window's light", 15.2, 10.2),
        ("the 10 mm window's light", 7.2, 10.2),
    ]),
}


def triangles(path):
    data = open(path, 'rb').read()
    count = struct.unpack('<I', data[80:84])[0]
    for i in range(count):
        o = 84 + 50 * i
        yield tuple(struct.unpack('<3f', data[o + 12 + 12 * v:o + 24 + 12 * v]) for v in range(3))


def pieces(tris):
    """Connected components, keyed by welded vertex position."""
    def key(p):
        return round(p[0], 3), round(p[1], 3), round(p[2], 3)

    at = defaultdict(list)
    for i, t in enumerate(tris):
        for v in t:
            at[key(v)].append(i)

    seen = [False] * len(tris)
    for start in range(len(tris)):
        if seen[start]:
            continue
        seen[start] = True
        stack, group = [start], [start]
        while stack:
            for v in tris[stack.pop()]:
                for j in at[key(v)]:
                    if not seen[j]:
                        seen[j] = True
                        stack.append(j)
                        group.append(j)
        yield group


def size(tris, group):
    xs = [v[0] for i in group for v in tris[i]]
    ys = [v[1] for i in group for v in tris[i]]
    zs = [v[2] for i in group for v in tris[i]]
    return max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs), min(xs)


def main(path):
    name = os.path.splitext(os.path.basename(path))[0]
    if name not in WANTED:
        print('no expectations recorded for %s' % name)
        return 1
    clearance, openings = WANTED[name]

    tris = list(triangles(path))
    found = sorted((size(tris, g) for g in pieces(tris)), key=lambda s: s[3])
    print('%s: %d triangles in %d pieces' % (path, len(tris), len(found)))
    print('')

    if len(found) != len(openings):
        print('%d pieces, %d openings to fill' % (len(found), len(openings)))
        return 1

    bad = 0
    for (what, ow, oh), (w, d, _h, _x) in zip(openings, found):
        # The piece lies flat on the plate, so its depth on the plate is its height on the wall.
        gap_w, gap_h = ow - w, oh - d
        ok = abs(gap_w - clearance) < 0.05 and abs(gap_h - clearance) < 0.05
        bad += not ok
        print('  %-4s %-26s %5.1f x %4.1f into %4.1f x %4.1f  gap %.1f / %.1f mm'
              % ('ok' if ok else 'BAD', what, w, d, ow, oh, gap_w, gap_h))

    print('')
    print('All %d pieces fit.' % len(found) if not bad else '%d of %d do not fit.' % (bad, len(found)))
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else 'build/house2/frames-and-door.stl'))
