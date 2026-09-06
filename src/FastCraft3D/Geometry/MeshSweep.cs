using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// How far something can slide along one axis before its actual surface meets another's.
///
/// The same job as <see cref="CollisionSweep"/> and the same answer for boxes, but measured
/// against the triangles rather than the bounding box. A box stops a sphere short of a cone by
/// the width of the gap between the cone's base and its side - which is exactly the gap you were
/// trying to close. Anything rotated is worse, because a rotated box is a great deal bigger than
/// the shape inside it.
///
/// Sliding along one axis is the easy case of this, and worth taking advantage of. Looked at
/// down the axis of travel, the question is only: where do the two shapes cover the same ground,
/// and how far apart are they there? So both are flattened onto the plane across the axis, and
/// wherever a triangle of one covers the same ground as a triangle of the other, the distance
/// between them over that shared ground is a limit on the travel. The smallest such limit wins.
///
/// Over any one pair of triangles that distance is a plane function, so its smallest value sits
/// at a corner of the shared ground: a corner of one triangle inside the other, or a crossing of
/// their edges. That makes it a handful of exact point evaluations per pair rather than a search,
/// and no tolerance to tune.
/// </summary>
public static class MeshSweep
{
    /// <summary>
    /// A hair of clearance at the contact, so two parts meet rather than share a face. The same
    /// figure the box sweep uses, and for the same reason: coplanar faces are a nuisance for
    /// every boolean that might follow.
    /// </summary>
    public const float ClearanceMm = CollisionSweep.ClearanceMm;

    /// <summary>
    /// Past this many triangles standing in the way, the boxes are used instead. Nothing here is
    /// per-frame - a drag works its answer out once and then only clamps to it - but a dense
    /// import would still stall the moment the mouse went down, and a stall at the start of every
    /// drag is worse than stopping a fraction of a millimetre early. Counted after the obstacles
    /// that cannot be reached have been dropped, so an import off to one side does not cost the
    /// exact answer against the part you are actually pushing against.
    /// </summary>
    public const int TriangleBudget = 40_000;

    /// <summary>Below this a triangle is edge-on to the sweep and has no area to stand on.</summary>
    private const float FlatEpsilon = 1e-9f;

    /// <summary>Corners are counted as inside, so shapes that already touch are seen to touch.</summary>
    private const float InsideEpsilon = 1e-6f;

    /// <summary>
    /// The largest part of <paramref name="travel"/> that can be made along
    /// <paramref name="axis"/> without <paramref name="moving"/> running into
    /// <paramref name="obstacles"/>. All meshes are in the same space.
    ///
    /// Returns the full travel when nothing is in the way, and never more than was asked for or
    /// a value of the opposite sign - a blocked drag stops, it does not reverse.
    /// </summary>
    public static float Limit(
        IReadOnlyList<Mesh> moving, IReadOnlyList<Mesh> obstacles, Axis axis, float travel)
    {
        if (travel == 0) return 0;

        int direction = travel > 0 ? 1 : -1;
        float allowed = Distance(moving, obstacles, axis, direction);

        return Math.Min(Math.Abs(travel), allowed) * direction;
    }

    /// <summary>
    /// How far it may go in the given direction before contact, or infinity when nothing is in
    /// the way. Never negative.
    /// </summary>
    public static float Distance(
        IReadOnlyList<Mesh> moving, IReadOnlyList<Mesh> obstacles, Axis axis, int direction)
    {
        if (moving.Count == 0 || obstacles.Count == 0) return float.PositiveInfinity;

        int h = (int)axis;
        int u = (h + 1) % 3;
        int v = (h + 2) % 3;
        int sign = direction >= 0 ? 1 : -1;

        // Most of a scene is not in the way at all, and its boxes say so for nothing. Only what
        // survives this is worth flattening - which is also what the budget is measured on, so a
        // large import standing well to one side costs nothing and does not spoil the answer for
        // whatever is genuinely in front.
        var inTheWay = InTheWay(moving, obstacles, u, v, h, sign);
        if (inTheWay.Count == 0) return float.PositiveInfinity;

        if (!WithinBudget(moving, inTheWay))
        {
            var movingBoxes = moving.Select(m => m.ComputeBounds()).ToList();
            var obstacleBoxes = inTheWay.Select(m => m.ComputeBounds()).ToList();

            return Math.Abs(CollisionSweep.Limit(
                movingBoxes, obstacleBoxes, axis, sign * float.MaxValue));
        }

        obstacles = inTheWay;

        // Only the surfaces that can see each other take part: the front of the mover, and the
        // back of whatever it is heading for. Without that, the mover's own back face reports a
        // gap to the far side of an obstacle it has already passed through, and two parts left
        // deliberately overlapping would lock together instead of staying free.
        var movers = Faces(moving, u, v, h, sign);
        if (movers.Count == 0) return float.PositiveInfinity;

        var blockers = Faces(obstacles, u, v, h, -sign);
        if (blockers.Count == 0) return float.PositiveInfinity;

        var grid = new FaceGrid(blockers);
        var nearby = new List<int>();
        float nearest = float.PositiveInfinity;

        foreach (var mover in movers)
        {
            grid.Near(mover, nearby);

            foreach (int index in nearby)
            {
                float gap = PairGap(mover, blockers[index], sign);
                if (gap < nearest) nearest = gap;
            }
        }

        if (float.IsPositiveInfinity(nearest)) return float.PositiveInfinity;

        return Math.Max(nearest - ClearanceMm, 0f);
    }

    /// <summary>
    /// The obstacles that could conceivably be run into: those covering some of the same ground
    /// as the mover, and not entirely behind it.
    /// </summary>
    private static List<Mesh> InTheWay(
        IReadOnlyList<Mesh> moving, IReadOnlyList<Mesh> obstacles, int u, int v, int h, int sign)
    {
        var reach = Bounds.Empty;
        foreach (var mesh in moving) reach = reach.Union(mesh.ComputeBounds());

        var kept = new List<Mesh>();
        if (reach.IsEmpty) return kept;

        foreach (var mesh in obstacles)
        {
            var box = mesh.ComputeBounds();
            if (box.IsEmpty) continue;

            if (Component(box.Max, u) < Component(reach.Min, u)) continue;
            if (Component(box.Min, u) > Component(reach.Max, u)) continue;
            if (Component(box.Max, v) < Component(reach.Min, v)) continue;
            if (Component(box.Min, v) > Component(reach.Max, v)) continue;

            // Entirely behind: it cannot be run into by going this way.
            if (sign > 0 ? Component(box.Max, h) < Component(reach.Min, h)
                         : Component(box.Min, h) > Component(reach.Max, h)) continue;

            kept.Add(mesh);
        }

        return kept;
    }

    /// <summary>Whether a sweep of this size is worth doing at all - see <see cref="TriangleBudget"/>.</summary>
    private static bool WithinBudget(IReadOnlyList<Mesh> moving, IReadOnlyList<Mesh> obstacles)
    {
        long total = 0;
        foreach (var mesh in moving) total += mesh.TriangleCount;
        foreach (var mesh in obstacles) total += mesh.TriangleCount;

        return total <= TriangleBudget;
    }

    // --- One pair of triangles ---------------------------------------------------------

    /// <summary>
    /// How far the mover may travel before meeting this one triangle, or infinity when the two
    /// never cover the same ground, or when the obstacle is behind.
    /// </summary>
    private static float PairGap(in Face mover, in Face blocker, int sign)
    {
        if (mover.MinU > blocker.MaxU || blocker.MinU > mover.MaxU) return float.PositiveInfinity;
        if (mover.MinV > blocker.MaxV || blocker.MinV > mover.MaxV) return float.PositiveInfinity;

        // Behind us: it cannot be run into by going this way. Strictly behind - two faces that
        // meet exactly are touching, and touching is a gap of nothing rather than no gap at all.
        if (sign > 0 ? blocker.MaxH < mover.MinH : blocker.MinH > mover.MaxH)
            return float.PositiveInfinity;

        float worst = float.PositiveInfinity;
        bool met = false;

        // The corners of the ground the two share: a corner of one inside the other, or a
        // crossing of their edges. The distance between them is a plane function over that
        // ground, so its smallest value is at one of these and nowhere else.
        for (int i = 0; i < 3; i++)
        {
            var corner = mover.Corner(i);
            if (blocker.Covers(corner))
            {
                met = true;
                worst = MathF.Min(worst, GapAt(mover, blocker, corner, sign));
            }

            corner = blocker.Corner(i);
            if (mover.Covers(corner))
            {
                met = true;
                worst = MathF.Min(worst, GapAt(mover, blocker, corner, sign));
            }
        }

        for (int i = 0; i < 3; i++)
        {
            var from = mover.Corner(i);
            var to = mover.Corner(i == 2 ? 0 : i + 1);

            for (int j = 0; j < 3; j++)
                if (Crossing(from, to, blocker.Corner(j), blocker.Corner(j == 2 ? 0 : j + 1))
                    is { } point)
                {
                    met = true;
                    worst = MathF.Min(worst, GapAt(mover, blocker, point, sign));
                }
        }

        if (!met) return float.PositiveInfinity;

        // Already through each other here. Parts get overlapped deliberately on the way to a
        // boolean, and holding them fast until the toggle goes off again would be obstruction
        // rather than help, so this pair imposes no limit - as the box sweep does for the same
        // situation, only now decided on the shapes themselves rather than their boxes.
        return worst < 0 ? float.PositiveInfinity : worst;
    }

    /// <summary>How far apart the two are over one point of shared ground.</summary>
    private static float GapAt(in Face mover, in Face blocker, Vector2 at, int sign) =>
        sign * (blocker.HeightAt(at) - mover.HeightAt(at));

    /// <summary>Where two flat segments cross, or nothing when they are parallel or miss.</summary>
    private static Vector2? Crossing(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
    {
        var da = a1 - a0;
        var db = b1 - b0;

        float denominator = da.X * db.Y - da.Y * db.X;
        if (MathF.Abs(denominator) < 1e-12f) return null;

        var between = b0 - a0;
        float t = (between.X * db.Y - between.Y * db.X) / denominator;
        float s = (between.X * da.Y - between.Y * da.X) / denominator;

        if (t < 0 || t > 1 || s < 0 || s > 1) return null;

        return a0 + da * t;
    }

    // --- Flattening --------------------------------------------------------------------

    /// <summary>
    /// The triangles of these meshes, flattened, keeping only those facing
    /// <paramref name="towards"/> along the axis.
    ///
    /// Which way a triangle faces falls straight out of flattening it: the area it covers comes
    /// out signed, and the sign is which side of it you are looking at. So the filter is free,
    /// and it halves the work into the bargain.
    /// </summary>
    private static List<Face> Faces(IReadOnlyList<Mesh> meshes, int u, int v, int h, int towards)
    {
        var faces = new List<Face>();

        foreach (var mesh in meshes)
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                var face = new Face(
                    Flatten(mesh.Positions[mesh.Indices[t]], u, v, h),
                    Flatten(mesh.Positions[mesh.Indices[t + 1]], u, v, h),
                    Flatten(mesh.Positions[mesh.Indices[t + 2]], u, v, h));

                if (face.FacesAlong(towards)) faces.Add(face);
            }

        return faces;
    }

    /// <summary>A point in sweep coordinates: x and y across the axis, z along it.</summary>
    private static Vector3 Flatten(Vector3 p, int u, int v, int h) =>
        new(Component(p, u), Component(p, v), Component(p, h));

    private static float Component(Vector3 v, int i) => i switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    /// <summary>One triangle, seen down the axis of travel.</summary>
    private readonly struct Face
    {
        private readonly Vector3 a, b, c;

        /// <summary>Twice the flattened area. Zero when the triangle is edge-on to the sweep.</summary>
        private readonly float spread;

        public readonly float MinU, MaxU, MinV, MaxV, MinH, MaxH;

        public Face(Vector3 a, Vector3 b, Vector3 c)
        {
            this.a = a;
            this.b = b;
            this.c = c;

            spread = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);

            MinU = MathF.Min(a.X, MathF.Min(b.X, c.X));
            MaxU = MathF.Max(a.X, MathF.Max(b.X, c.X));
            MinV = MathF.Min(a.Y, MathF.Min(b.Y, c.Y));
            MaxV = MathF.Max(a.Y, MathF.Max(b.Y, c.Y));
            MinH = MathF.Min(a.Z, MathF.Min(b.Z, c.Z));
            MaxH = MathF.Max(a.Z, MathF.Max(b.Z, c.Z));
        }

        /// <summary>
        /// Whether this triangle is turned towards the given direction along the axis. Edge-on
        /// triangles face neither way and take no part: on a closed shape the leading point is
        /// always on a triangle that is turned to meet you.
        /// </summary>
        public bool FacesAlong(int towards) =>
            towards > 0 ? spread > FlatEpsilon : spread < -FlatEpsilon;

        public Vector2 Corner(int i) => i switch
        {
            0 => new Vector2(a.X, a.Y),
            1 => new Vector2(b.X, b.Y),
            _ => new Vector2(c.X, c.Y)
        };

        /// <summary>Whether this triangle covers the given ground.</summary>
        public bool Covers(Vector2 at)
        {
            var (first, second) = Weights(at);
            float third = 1f - first - second;

            return first >= -InsideEpsilon && second >= -InsideEpsilon && third >= -InsideEpsilon;
        }

        /// <summary>
        /// How far along the axis the triangle sits over a point of ground. Only ever asked of
        /// triangles that face the sweep, so there is always exactly one answer.
        /// </summary>
        public float HeightAt(Vector2 at)
        {
            var (first, second) = Weights(at);
            return first * a.Z + second * b.Z + (1f - first - second) * c.Z;
        }

        private (float First, float Second) Weights(Vector2 at) => (
            ((b.Y - c.Y) * (at.X - c.X) + (c.X - b.X) * (at.Y - c.Y)) / spread,
            ((c.Y - a.Y) * (at.X - c.X) + (a.X - c.X) * (at.Y - c.Y)) / spread);
    }

    // --- Narrowing it down -------------------------------------------------------------

    /// <summary>
    /// Buckets the obstacle triangles by where they sit on the flattened plane, so a mover
    /// triangle only ever meets the few that could possibly be under it. Without this the work
    /// is every triangle against every triangle, which two ordinary imports would never finish.
    /// </summary>
    private sealed class FaceGrid
    {
        private readonly List<int>[] cells;
        private readonly int columns, rows;
        private readonly float originU, originV, cell;
        private readonly int[] seen;
        private int visit;

        public FaceGrid(List<Face> faces)
        {
            float minU = float.MaxValue, minV = float.MaxValue;
            float maxU = float.MinValue, maxV = float.MinValue;

            foreach (var face in faces)
            {
                minU = MathF.Min(minU, face.MinU);
                minV = MathF.Min(minV, face.MinV);
                maxU = MathF.Max(maxU, face.MaxU);
                maxV = MathF.Max(maxV, face.MaxV);
            }

            // About one triangle a cell, which keeps both the bucketing and the queries cheap.
            int side = Math.Clamp((int)MathF.Sqrt(faces.Count), 1, 256);

            originU = minU;
            originV = minV;
            columns = side;
            rows = side;
            cell = MathF.Max(MathF.Max(maxU - minU, maxV - minV) / side, 1e-6f);

            cells = new List<int>[columns * rows];
            seen = new int[faces.Count];

            for (int i = 0; i < faces.Count; i++)
            {
                var face = faces[i];
                int fromColumn = Column(face.MinU), toColumn = Column(face.MaxU);
                int fromRow = Row(face.MinV), toRow = Row(face.MaxV);

                for (int row = fromRow; row <= toRow; row++)
                    for (int column = fromColumn; column <= toColumn; column++)
                        (cells[row * columns + column] ??= new List<int>()).Add(i);
            }
        }

        /// <summary>
        /// Every obstacle triangle sharing a cell with this one, each given once, into a list the
        /// caller keeps and reuses. This is the innermost loop of the whole sweep, so it hands
        /// back a filled list rather than something to walk: an enumerator per triangle was four
        /// fifths of the time it took.
        /// </summary>
        public void Near(in Face face, List<int> into)
        {
            into.Clear();
            visit++;

            int fromColumn = Column(face.MinU), toColumn = Column(face.MaxU);
            int fromRow = Row(face.MinV), toRow = Row(face.MaxV);

            for (int row = fromRow; row <= toRow; row++)
                for (int column = fromColumn; column <= toColumn; column++)
                {
                    var bucket = cells[row * columns + column];
                    if (bucket is null) continue;

                    foreach (int i in bucket)
                    {
                        if (seen[i] == visit) continue;

                        seen[i] = visit;
                        into.Add(i);
                    }
                }
        }

        private int Column(float u) => Math.Clamp((int)((u - originU) / cell), 0, columns - 1);
        private int Row(float v) => Math.Clamp((int)((v - originV) / cell), 0, rows - 1);
    }
}
