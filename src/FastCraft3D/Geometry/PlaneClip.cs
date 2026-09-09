using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// Cuts a mesh with a plane and keeps one side, closing the opening behind it.
///
/// Not a cheaper <see cref="PlaneSplit"/>: that one is a boolean and gives back two halves that
/// will print, which is what the split itself needs. This only has to show what a split would
/// do. The boolean takes seconds on a dense model - no use at all to a plane being dragged
/// about - while this is one pass over the triangles and a ring or two to fill in.
/// </summary>
public static class PlaneClip
{
    /// <summary>
    /// How near two ends must be to count as the same corner of the cut, in millimetres.
    ///
    /// Only wide enough to catch corners a file gave twice. It used to be ten times this, to cover
    /// the two triangles either side of an edge working the crossing out to slightly different
    /// answers - and once they were made to work it out the same way round, that width was doing
    /// nothing but harm: on a sphere of forty thousand triangles the corners round a pole are two
    /// hundredths of a millimetre apart, and merging two of them pinched the ring they belonged to
    /// so that it never closed.
    /// </summary>
    private const float Weld = 1e-4f;

    /// <summary>
    /// How near the plane a corner has to be to count as lying on it.
    ///
    /// The two triangles either side of an edge work the same corner out from their own copies
    /// of it, and on an imported scan those copies are not always the same to the last bit -
    /// nor is a corner that lands exactly on the plane on any particular side of it. Without
    /// this they disagree about which side a corner is on, one of them leaves out the crossing
    /// the other put in, and the ring of edges round the cut never closes.
    /// </summary>
    private const float OnIt = 1e-3f;

    /// <summary>
    /// The part of the mesh on the side the normal points to, in world space.
    ///
    /// The transform is applied as the triangles are read rather than by transforming the whole
    /// mesh first, so a dense model is walked once instead of being copied whole and then cut.
    /// </summary>
    public static Mesh Keep(Mesh mesh, Matrix4x4 transform, Vector3 normal, float offset, bool cap = true)
    {
        normal = Vector3.Normalize(normal);

        var cuts = cap ? new List<(Vector3 From, Vector3 To)>() : null;
        var kept = Cut(mesh, transform, normal, offset, cuts);

        if (cuts is { Count: >= 3 }) Cap(kept, cuts, normal, offset);

        return kept;
    }

    /// <summary>The triangles alone, with the edges the cut left collected on the way past.</summary>
    private static Mesh Cut(Mesh mesh, Matrix4x4 transform, Vector3 normal, float offset,
                            List<(Vector3 From, Vector3 To)>? cuts)
    {
        var kept = new Mesh();
        var corner = new Vector3[4];
        var onCut = new bool[4];

        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            Vector3 a = Vector3.Transform(mesh.Positions[mesh.Indices[t]], transform);
            Vector3 b = Vector3.Transform(mesh.Positions[mesh.Indices[t + 1]], transform);
            Vector3 c = Vector3.Transform(mesh.Positions[mesh.Indices[t + 2]], transform);

            float da = Side(a), db = Side(b), dc = Side(c);

            if (da >= 0 && db >= 0 && dc >= 0)
            {
                // A triangle lying in the plane itself belongs to one side, not to both. Kept
                // where it faces, so that cutting a solid in two and putting the halves back
                // together gives what it started with: without this both halves take a copy,
                // and every edge round it is used twice over.
                if (da == 0 && db == 0 && dc == 0
                    && Vector3.Dot(Vector3.Cross(b - a, c - a), normal) <= 0)
                    continue;

                kept.AddTriangle(a, b, c);
                continue;
            }

            if (da < 0 && db < 0 && dc < 0) continue;

            // Straddling, so the triangle is walked edge by edge: a corner on the keeping side
            // is taken as it is, and every edge that crosses the plane contributes the point
            // where it crosses. That leaves three corners or four, never more, and the two
            // crossings are always next to each other - they are the new edge along the cut.
            int count = 0;
            onCut[0] = onCut[1] = onCut[2] = onCut[3] = false;

            Cross(a, da, b, db);
            Cross(b, db, c, dc);
            Cross(c, dc, a, da);

            if (count >= 3) kept.AddTriangle(corner[0], corner[1], corner[2]);
            if (count == 4) kept.AddTriangle(corner[0], corner[2], corner[3]);

            // The cut edge is the one side of this piece whose two ends both lie on the plane,
            // and it is taken in the piece's own winding order.
            //
            // Not in the order the two ends were worked out, which is what it was: where the
            // walk starts depends on which corner of the triangle happens to be over the plane,
            // so half the triangles handed back their edge the wrong way round. On a box every
            // triangle is the same way up and it went unnoticed; on a scan the ends would not
            // join up and the whole face was left open.
            if (cuts is not null && count >= 3)
            {
                for (int i = 0; i < count; i++)
                {
                    int j = (i + 1) % count;
                    if (!onCut[i] || !onCut[j]) continue;

                    cuts.Add((corner[i], corner[j]));
                    break;
                }
            }

            // A triangle with a whole edge lying along the plane keeps no area of its own, but
            // that edge is still a side of the cut. It is the edge opposite the corner that is
            // under the plane, taken in this triangle's own winding and then turned round: the
            // piece that keeps the area along that edge is the one on the other side of it.
            //
            // Not a corner case. Cut a sphere at one of its own rings of corners, or a cube
            // along a diagonal its own triangulation happens to follow, and every triangle round
            // the cut is this one. Nor can the order the walk above found them in be used: where
            // it starts depends on which corner is under the plane, so it gives the edge one way
            // round for two of the three and the other way for the third.
            if (cuts is not null && count == 2)
            {
                if (da < 0 && db == 0 && dc == 0) cuts.Add((c, b));
                else if (db < 0 && da == 0 && dc == 0) cuts.Add((a, c));
                else if (dc < 0 && da == 0 && db == 0) cuts.Add((b, a));
            }

            void Cross(Vector3 from, float fromSide, Vector3 to, float toSide)
            {
                if (fromSide >= 0) Add(from, onPlane: fromSide == 0);

                if (fromSide < 0 == toSide < 0) return;

                Add(Between(from, fromSide, to, toSide), onPlane: true);
            }

            void Add(Vector3 p, bool onPlane)
            {
                if (count >= 4) return;

                // A corner lying on the plane is both a corner of the piece and an end of the
                // cut, and would go round twice - leaving a triangle with no area at all, whose
                // normal is nothing and whose shading is worse than nothing. It is marked where
                // it already is instead.
                if (count > 0 && corner[count - 1] == p)
                {
                    onCut[count - 1] |= onPlane;
                    return;
                }

                if (count > 0 && corner[0] == p)
                {
                    onCut[0] |= onPlane;
                    return;
                }

                corner[count] = p;
                onCut[count] = onPlane;
                count++;
            }
        }

        return kept;

        float Side(Vector3 p)
        {
            float d = Vector3.Dot(normal, p) - offset;
            return MathF.Abs(d) <= OnIt ? 0f : d;
        }
    }

    /// <summary>
    /// Where an edge crosses the plane, worked out the same way from either end.
    ///
    /// The two triangles either side of an edge each work this out for themselves, and they meet
    /// it from opposite ends: one interpolates from P towards Q, the other from Q towards P. In
    /// exact arithmetic those are the same point; in floating point they are a ten-thousandth of
    /// a millimetre apart, which is enough for the cut face and the shell around it not to join
    /// up. Always going from the same end of the edge makes the two answers identical, bit for
    /// bit, so there is nothing left to join.
    /// </summary>
    private static Vector3 Between(Vector3 from, float fromSide, Vector3 to, float toSide) =>
        First(from, to)
            ? Vector3.Lerp(from, to, fromSide / (fromSide - toSide))
            : Vector3.Lerp(to, from, toSide / (toSide - fromSide));

    /// <summary>An order on points that does not depend on which triangle is asking.</summary>
    private static bool First(Vector3 a, Vector3 b) =>
        a.X != b.X ? a.X < b.X :
        a.Y != b.Y ? a.Y < b.Y : a.Z < b.Z;

    /// <summary>
    /// Closes the opening, so the piece reads as solid material rather than as a shell you can
    /// see the inside of.
    ///
    /// The edges left by the cut are chained into rings and filled in by the same triangulator
    /// the lettering uses, holes and all - a section through a tube is a ring inside a ring. If
    /// they will not chain, which a mesh with holes of its own can easily manage, the opening is
    /// left alone: an open cut looks unfinished, a wrongly filled one looks broken.
    /// </summary>
    private static void Cap(Mesh kept, List<(Vector3 From, Vector3 To)> cuts, Vector3 normal, float offset)
    {
        // A frame on the plane, turned so that anticlockwise in it faces out of the piece: the
        // face a cut exposes looks away from the side being kept.
        Vector3 u = Across(normal);
        Vector3 v = Vector3.Cross(-normal, u);
        Vector3 origin = normal * offset;

        var rings = Rings(cuts, u, v, origin);
        if (rings.Count == 0) return;

        // Flattened to lay them out, but put back exactly as they were rather than worked out
        // again from the flat ones. Coming back through the frame lands a few millionths off,
        // which is nothing at the origin and more than the shell will weld to a metre and a half
        // up - and then the face and the shell it should be joined to are two separate surfaces.
        var solid = new Dictionary<Vector2, Vector3>();
        var flat = new List<List<Vector2>>(rings.Count);

        foreach (var ring in rings)
        {
            var loop = new List<Vector2>(ring.Count);

            foreach (var p in ring)
            {
                var here = Flatten(p);
                solid[here] = p;
                loop.Add(here);
            }

            flat.Add(loop);
        }

        foreach (var (outline, holes) in Polygon2.Nest(flat))
        {
            var (points, triangles) = Polygon2.Triangulate(outline, holes);

            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                kept.AddTriangle(
                    Raise(points[triangles[t]]),
                    Raise(points[triangles[t + 1]]),
                    Raise(points[triangles[t + 2]]));
            }
        }

        Vector2 Flatten(Vector3 p) => new(Vector3.Dot(p - origin, u), Vector3.Dot(p - origin, v));

        Vector3 Raise(Vector2 p) =>
            solid.TryGetValue(p, out var kept) ? kept : origin + u * p.X + v * p.Y;
    }

    /// <summary>
    /// Chains the cut edges into closed rings, keeping whichever ones come full circle.
    ///
    /// The ends are welded rather than matched exactly: the two triangles either side of an edge
    /// meet the plane at the same point, but they work it out from their own ends of that edge
    /// and the last bit or two of the answer disagree.
    ///
    /// Awkward corners are walked round rather than given up on. A corner with two ways on -
    /// which a scan manages easily enough, wherever its surface pinches or doubles back - used
    /// to abandon the whole cut face, so a 200,000 triangle import was cut open and left that
    /// way. Now only the chain that will not close is dropped, and the rest of the face is
    /// filled. A chain that runs out is dropped rather than closed by force: an opening the cut
    /// did not go all the way round is a hole in the model, not a face waiting to be filled.
    /// </summary>
    private static List<List<Vector3>> Rings(
        List<(Vector3 From, Vector3 To)> cuts, Vector3 u, Vector3 v, Vector3 origin)
    {
        var points = new List<Vector3>();
        var buckets = new Dictionary<(int X, int Y, int Z), List<int>>();
        var leaving = new Dictionary<int, List<int>>();

        foreach (var (from, to) in cuts)
        {
            int a = Index(from), b = Index(to);
            if (a == b) continue; // the plane grazed a corner

            if (!leaving.TryGetValue(a, out var ways)) leaving[a] = ways = [];
            ways.Add(b);
        }

        var rings = new List<List<Vector3>>();

        foreach (int begin in leaving.Keys.ToList())
        {
            while (Take(begin, -1) is int onward)
            {
                var ring = new List<Vector3> { points[begin] };
                int from = begin, at = onward;

                while (at != begin && ring.Count <= cuts.Count)
                {
                    ring.Add(points[at]);

                    if (Take(at, from) is not int step) break;
                    from = at;
                    at = step;
                }

                if (at == begin && ring.Count >= 3) rings.Add(ring);
            }
        }

        return rings;

        /// <summary>
        /// The next edge round the cut, taking the sharpest turn back the way we came.
        ///
        /// Where several edges meet at one corner - which a plane passing within a whisker of a
        /// corner of a dense mesh produces routinely - any of them continues a chain, but only
        /// one of them continues *this* chain. Taking whichever came last closed some rings and
        /// left others as chains that ran out, and a chain that runs out is dropped: a hole the
        /// size of one triangle in the middle of a face that was supposed to be closed over.
        ///
        /// Turning as sharply as possible is the rule that traces one region's boundary and
        /// stays on it, the same rule a planar graph is walked face by face with.
        /// </summary>
        int? Take(int from, int cameFrom)
        {
            if (!leaving.TryGetValue(from, out var ways) || ways.Count == 0) return null;

            int pick = ways.Count - 1;

            if (ways.Count > 1 && cameFrom >= 0)
            {
                double back = Bearing(from, cameFrom);
                double best = double.MaxValue;

                for (int i = 0; i < ways.Count; i++)
                {
                    // Anticlockwise from the way we came in: the smallest such turn is the
                    // sharpest one back, and it is the edge that keeps to this boundary.
                    double turn = Bearing(from, ways[i]) - back;
                    while (turn <= 0) turn += Math.Tau;
                    while (turn > Math.Tau) turn -= Math.Tau;

                    if (turn < best) { best = turn; pick = i; }
                }
            }

            int to = ways[pick];
            ways.RemoveAt(pick);
            return to;
        }

        /// <summary>Which way one corner lies from another, in the plane's own frame.</summary>
        double Bearing(int at, int towards)
        {
            Vector3 along = points[towards] - points[at];
            return Math.Atan2(Vector3.Dot(along, v), Vector3.Dot(along, u));
        }

        int Index(Vector3 p)
        {
            var cell = Cell(p);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!buckets.TryGetValue((cell.X + dx, cell.Y + dy, cell.Z + dz), out var near))
                            continue;

                        foreach (int i in near)
                            if (Vector3.DistanceSquared(points[i], p) <= Weld * Weld)
                                return i;
                    }
                }
            }

            points.Add(p);

            if (!buckets.TryGetValue(cell, out var bucket)) buckets[cell] = bucket = [];
            bucket.Add(points.Count - 1);

            return points.Count - 1;
        }

        static (int X, int Y, int Z) Cell(Vector3 p) => (
            (int)MathF.Floor(p.X / Weld), (int)MathF.Floor(p.Y / Weld), (int)MathF.Floor(p.Z / Weld));
    }

    private static Vector3 Across(Vector3 v) =>
        Vector3.Normalize(Vector3.Cross(v, MathF.Abs(v.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY));
}
