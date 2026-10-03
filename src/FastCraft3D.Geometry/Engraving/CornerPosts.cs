using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

/// <summary>
/// The trim at the corners of sided walls: a post standing the full height of each outside
/// corner, square to both walls, and a smaller one in each inside corner.
///
/// Siding laid round a corner bends round it, and nothing in a sided wall does that: the boards
/// stop either side of the corner and a post covers their ends. So the posts stand prouder than
/// the boards and are joined over them, and the boards running into them are the boards tucked
/// into the post's channel. Where the walls picked stop at an outside corner the post is the one
/// face of it on the wall picked, leaving the wall round the corner as it was.
/// </summary>
public static class CornerPosts
{
    /// <summary>How much prouder than the boards a post stands, as a share of their relief.</summary>
    private const float Prouder = 1.3f;

    /// <summary>How wide a post is on each face, as a share of a board's exposure.</summary>
    private const float Wide = 0.7f;

    /// <summary>The inside post, as a share of the outside one's width.</summary>
    private const float Nook = 0.6f;

    /// <summary>How far a post stands off the walls for boards standing <paramref name="reliefMm"/> off them.</summary>
    public static float ProudFor(float reliefMm) => reliefMm * Prouder;

    /// <summary>The posts for siding of <paramref name="courseMm"/> exposure and <paramref name="reliefMm"/> relief.</summary>
    public static Mesh Build(WallRun run, float courseMm, float reliefMm, out int posts)
    {
        posts = 0;
        var loop = run.Loop;
        int n = loop.Count;
        float proud = ProudFor(reliefMm);
        float wide = MathF.Max(courseMm * Wide, proud * 1.5f);
        float bottom = run.Low + WallBlocks.HairMm, top = run.High - WallBlocks.HairMm;

        var positions = new List<Vector3>();
        var indices = new List<int>();
        var walls = run.Walls.ToList();

        for (int p = 0; p < walls.Count; p++)
        {
            int wall = walls[p];
            float length = loop.LengthOf(wall);
            float post = MathF.Min(wide, length * 0.45f);

            // Where the run starts at an outside corner, the one face of its post.
            if (!run.Closed && p == 0 && WallBlocks.Outside(loop, (wall + n - 1) % n, wall))
            {
                WallBlocks.Straight(positions, indices, loop, wall, WallBlocks.HairMm, post, proud, bottom, top);
                posts++;
            }

            bool last = !run.Closed && p == walls.Count - 1;
            int next = (wall + 1) % n;
            bool outside = WallBlocks.Outside(loop, wall, next);

            if (last)
            {
                if (outside)
                {
                    WallBlocks.Straight(positions, indices, loop, wall, length - post, length - WallBlocks.HairMm, proud, bottom, top);
                    posts++;
                }

                continue;
            }

            float onward = MathF.Min(wide, loop.LengthOf(next) * 0.45f);
            if (outside) WallBlocks.Turning(positions, indices, loop, wall, post, onward, proud, bottom, top);
            else WallBlocks.Nook(positions, indices, loop, wall, MathF.Max(wide * Nook, proud * 1.2f), bottom, top);
            posts++;
        }

        return new Mesh(positions, indices);
    }
}
