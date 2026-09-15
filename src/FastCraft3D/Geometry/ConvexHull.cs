using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// The convex hull of a set of points: the shape a sheet of cling film takes pulled tight round
/// them.
///
/// Quickhull. Each face keeps the points still outside it, and the farthest of those is added
/// next, so most points are looked at a handful of times and then dropped as inside. Testing every
/// point against every face instead is quadratic, and a scan has a hundred thousand vertices.
///
/// Worked in doubles: the test that decides whether a point is outside a face is a difference of
/// nearly equal products, and in floats a flat face of a cube came back as a fan of slivers
/// tilted either way.
/// </summary>
public static class ConvexHull
{
    private sealed class Face(int a, int b, int c)
    {
        public readonly int A = a, B = b, C = c;
        public double Nx, Ny, Nz, D;
        public List<int>? Outside;
        public bool Dead;
        public int Seen;
    }

    /// <summary>
    /// The hull's triangles, as indices into <paramref name="points"/>, wound anticlockwise seen
    /// from outside. Empty when the points do not enclose any volume - all in one plane, or fewer
    /// than four.
    /// </summary>
    public static List<(int A, int B, int C)> Build(IReadOnlyList<Vector3> points)
    {
        int n = points.Count;
        if (n < 4) return [];

        var x = new double[n];
        var y = new double[n];
        var z = new double[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = points[i].X;
            y[i] = points[i].Y;
            z[i] = points[i].Z;
        }

        // Extremes along each axis, for the starting tetrahedron and for the tolerance.
        var extremes = new int[6];
        for (int i = 1; i < n; i++)
        {
            if (x[i] < x[extremes[0]]) extremes[0] = i;
            if (x[i] > x[extremes[1]]) extremes[1] = i;
            if (y[i] < y[extremes[2]]) extremes[2] = i;
            if (y[i] > y[extremes[3]]) extremes[3] = i;
            if (z[i] < z[extremes[4]]) extremes[4] = i;
            if (z[i] > z[extremes[5]]) extremes[5] = i;
        }

        double span = Math.Max(x[extremes[1]] - x[extremes[0]],
                      Math.Max(y[extremes[3]] - y[extremes[2]], z[extremes[5]] - z[extremes[4]]));
        if (span <= 0) return [];
        double eps = span * 1e-7;

        // The two extremes farthest apart, the point farthest off the line through them, and the
        // point farthest off the plane through those three.
        int p0 = 0, p1 = 0;
        double far = -1;
        foreach (int i in extremes)
            foreach (int j in extremes)
            {
                double d = Square(i, j);
                if (d > far) { far = d; p0 = i; p1 = j; }
            }

        double dx = x[p1] - x[p0], dy = y[p1] - y[p0], dz = z[p1] - z[p0];
        int p2 = -1;
        far = eps * eps;
        for (int i = 0; i < n; i++)
        {
            double ex = x[i] - x[p0], ey = y[i] - y[p0], ez = z[i] - z[p0];
            double cx = dy * ez - dz * ey, cy = dz * ex - dx * ez, cz = dx * ey - dy * ex;
            double d = (cx * cx + cy * cy + cz * cz) / (dx * dx + dy * dy + dz * dz);
            if (d > far) { far = d; p2 = i; }
        }
        if (p2 < 0) return [];

        var basis = MakeFace(p0, p1, p2);
        int p3 = -1;
        far = eps;
        for (int i = 0; i < n; i++)
        {
            double d = Math.Abs(Distance(basis, i));
            if (d > far) { far = d; p3 = i; }
        }
        if (p3 < 0) return [];

        double mx = (x[p0] + x[p1] + x[p2] + x[p3]) / 4;
        double my = (y[p0] + y[p1] + y[p2] + y[p3]) / 4;
        double mz = (z[p0] + z[p1] + z[p2] + z[p3]) / 4;

        var faces = new List<Face>();
        var edges = new Dictionary<long, Face>();

        foreach (var (a, b, c) in new[] { (p0, p1, p2), (p0, p1, p3), (p0, p2, p3), (p1, p2, p3) })
        {
            var face = MakeFace(a, b, c);
            // Wound so its normal points away from the middle of the tetrahedron.
            if (face.Nx * mx + face.Ny * my + face.Nz * mz - face.D > 0) face = MakeFace(a, c, b);
            Add(face);
        }

        var pending = new Stack<Face>();
        var starting = faces.ToList();
        for (int i = 0; i < n; i++)
        {
            if (i == p0 || i == p1 || i == p2 || i == p3) continue;
            Assign(i, starting);
        }
        foreach (var face in starting)
            if (face.Outside is { Count: > 0 }) pending.Push(face);

        int stamp = 0;
        var visible = new List<Face>();
        var horizon = new List<(int U, int V)>();
        var search = new Stack<Face>();

        while (pending.Count > 0)
        {
            var face = pending.Pop();
            if (face.Dead || face.Outside is not { Count: > 0 }) continue;

            int eye = face.Outside[0];
            double best = Distance(face, eye);
            foreach (int i in face.Outside)
            {
                double d = Distance(face, i);
                if (d > best) { best = d; eye = i; }
            }

            // Every face the new point can see, found by walking out from this one, and the ring of
            // edges where the seen faces meet the unseen ones.
            stamp++;
            visible.Clear();
            horizon.Clear();
            face.Seen = stamp;
            search.Push(face);
            while (search.Count > 0)
            {
                var seen = search.Pop();
                visible.Add(seen);

                foreach (var (u, v) in Edges(seen))
                {
                    if (!edges.TryGetValue(Key(v, u), out var across)) continue;
                    if (across.Seen == stamp) continue;

                    if (Distance(across, eye) > eps)
                    {
                        across.Seen = stamp;
                        search.Push(across);
                    }
                    else
                    {
                        horizon.Add((u, v));
                    }
                }
            }

            var orphans = new List<int>();
            foreach (var gone in visible)
            {
                gone.Dead = true;
                if (gone.Outside is not null)
                    foreach (int i in gone.Outside)
                        if (i != eye) orphans.Add(i);
                gone.Outside = null;

                foreach (var (u, v) in Edges(gone))
                    if (edges.TryGetValue(Key(u, v), out var owner) && owner == gone)
                        edges.Remove(Key(u, v));
            }

            // Each horizon edge keeps the direction it had in the face it came from, which is what
            // winds the new face outward.
            var made = new List<Face>(horizon.Count);
            foreach (var (u, v) in horizon)
            {
                var fresh = MakeFace(u, v, eye);
                Add(fresh);
                made.Add(fresh);
            }

            foreach (int i in orphans) Assign(i, made);
            foreach (var fresh in made)
                if (fresh.Outside is { Count: > 0 }) pending.Push(fresh);
        }

        return faces.Where(f => !f.Dead).Select(f => (f.A, f.B, f.C)).ToList();

        double Square(int i, int j)
        {
            double ax = x[i] - x[j], ay = y[i] - y[j], az = z[i] - z[j];
            return ax * ax + ay * ay + az * az;
        }

        Face MakeFace(int a, int b, int c)
        {
            double ux = x[b] - x[a], uy = y[b] - y[a], uz = z[b] - z[a];
            double vx = x[c] - x[a], vy = y[c] - y[a], vz = z[c] - z[a];
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);

            var face = new Face(a, b, c);
            // A sliver with no area has no direction; it sees nothing and is never outside-tested.
            if (length > 0)
            {
                face.Nx = nx / length;
                face.Ny = ny / length;
                face.Nz = nz / length;
                face.D = face.Nx * x[a] + face.Ny * y[a] + face.Nz * z[a];
            }

            return face;
        }

        double Distance(Face face, int i) => face.Nx * x[i] + face.Ny * y[i] + face.Nz * z[i] - face.D;

        void Add(Face face)
        {
            faces.Add(face);
            foreach (var (u, v) in Edges(face)) edges[Key(u, v)] = face;
        }

        void Assign(int i, List<Face> candidates)
        {
            Face? best = null;
            double farthest = eps;
            foreach (var face in candidates)
            {
                double d = Distance(face, i);
                if (d > farthest) { farthest = d; best = face; }
            }

            if (best is not null) (best.Outside ??= []).Add(i);
        }

        long Key(int u, int v) => (long)u * n + v;
    }

    private static (int, int)[] Edges(Face face) => [(face.A, face.B), (face.B, face.C), (face.C, face.A)];
}
