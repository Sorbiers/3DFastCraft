using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Geometry.Engraving;
using FastCraft3D.Model;
using FastCraft3D.View;
using FastCraft3D.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FastCraft3D.Tests;

/// <summary>
/// Makes the pictures for the website's Cladding and Texture pages: every pattern flat and in 3D, from
/// the app's own preview, and an example scene - a barrel, a knob, a board - built with the tools
/// themselves and drawn by a small software renderer, so no window is needed. (The Cladding page uses
/// real screenshots.)
///
/// Does nothing unless FASTCRAFT_SITE_IMG names the folder to write to (the site's web\img), which
/// keeps it out of an ordinary test run. To remake the pictures after a change:
/// set FASTCRAFT_SITE_IMG to the web/img folder of the site and run the tests of this class.
/// </summary>
public class SiteAssets(ITestOutputHelper log)
{
    private static string? Folder => Environment.GetEnvironmentVariable("FASTCRAFT_SITE_IMG") is { Length: > 0 } folder
        ? folder.TrimEnd('\\', '/') + Path.DirectorySeparatorChar
        : null;

    private static string Out => Folder ?? throw new InvalidOperationException("FASTCRAFT_SITE_IMG is not set");

    // --- Pictures of every pattern ----------------------------------------------------------

    private static readonly (TextureKind Kind, string File, float Pitch, float Groove, float Depth, float Aspect)[] Patterns =
    [
        (TextureKind.Knurl, "knurl", 3f, 0.6f, 0.8f, 0f),
        (TextureKind.Ribs, "ribs", 3f, 0.8f, 0.8f, 0f),
        (TextureKind.Hex, "hex", 4f, 0.8f, 0.8f, 0f),
        (TextureKind.Dots, "dots", 3f, 0.8f, 0.8f, 0f),
        (TextureKind.Tread, "tread", 4f, 0.8f, 0.8f, 0f),
        (TextureKind.Brick, "brick", 6f, 0.8f, 0.8f, 0f),
        (TextureKind.RoofTiles, "roof-tiles", 6f, 0.8f, 0.8f, 0f),
        (TextureKind.Tiles, "tiles", 6f, 0.8f, 0.8f, 0f),
        (TextureKind.Planks, "planks", 30f, 0.6f, 0.6f, 0f),
        (TextureKind.Siding, "siding", 4f, 0f, 0.8f, 0f),
        (TextureKind.Rubble, "rubble", 8f, 0.6f, 1f, 0f),
        (TextureKind.CoursedStone, "coursed-stone", 10f, 0.6f, 1f, 0f),
        (TextureKind.Bark, "bark", 6f, 1f, 1.2f, 0f),
        (TextureKind.Grain, "grain", 3f, 0.6f, 0.6f, 0f),
        (TextureKind.Logs, "logs", 6f, 0.6f, 2.5f, 0f)
    ];

    private static void Save(BitmapSource picture, string path, bool jpeg)
    {
        BitmapEncoder encoder = jpeg ? new JpegBitmapEncoder { QualityLevel = 90 } : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    [Fact]
    public void PatternPictures()
    {
        if (Folder is null) return;

        Directory.CreateDirectory(Out + "pat");
        foreach (var (kind, file, pitch, groove, depth, aspect) in Patterns)
        {
            var options = new TextureOptions(kind, pitch, groove, 45f, false, aspect);
            var pictures = TexturePreview.Render(options, depth, 300);
            Assert.NotNull(pictures);

            Save(pictures!.Flat, Out + $"pat\\{file}-flat.png", jpeg: false);
            Save(pictures.Relief, Out + $"pat\\{file}-relief.jpg", jpeg: true);
        }
    }

    // --- A little software renderer ---------------------------------------------------------

    private sealed record Part(Mesh Mesh, Vector3 Colour);

    private static byte[] Draw(IReadOnlyList<Part> parts, Vector3 eye, Vector3 target, float fovDegrees, int width, int height, int ss,
                               Vector3 skyTop, Vector3 skyBottom)
    {
        int W = width * ss, H = height * ss;
        var view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitZ);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(fovDegrees * MathF.PI / 180f, (float)W / H, 5f, 4000f);
        var clip = view * proj;

        var colour = new float[W * H * 3];
        var depth = new float[W * H];
        Array.Fill(depth, float.MaxValue);
        for (int y = 0; y < H; y++)
        {
            float t = (float)y / H;
            var sky = Vector3.Lerp(skyTop, skyBottom, t);
            for (int x = 0; x < W; x++)
            {
                colour[(y * W + x) * 3] = sky.X;
                colour[(y * W + x) * 3 + 1] = sky.Y;
                colour[(y * W + x) * 3 + 2] = sky.Z;
            }
        }

        var key = Vector3.Normalize(new Vector3(-0.55f, -0.65f, 0.95f));
        var fill = Vector3.Normalize(eye - target);

        foreach (var part in parts)
        {
            var mesh = part.Mesh;
            int triangles = mesh.TriangleCount;
            var positions = mesh.Positions;
            var indices = mesh.Indices;

            var faceNormal = new Vector3[triangles];
            for (int t = 0; t < triangles; t++)
            {
                var a = positions[indices[t * 3]];
                var n = Vector3.Cross(positions[indices[t * 3 + 1]] - a, positions[indices[t * 3 + 2]] - a);
                faceNormal[t] = n; // area weighted
            }

            // faces round each vertex, to smooth the normals across gentle creases only
            var count = new int[positions.Count + 1];
            for (int i = 0; i < indices.Count; i++) count[indices[i] + 1]++;
            for (int v = 0; v < positions.Count; v++) count[v + 1] += count[v];
            var fill2 = new int[indices.Count];
            var cursor = (int[])count.Clone();
            for (int t = 0; t < triangles; t++)
                for (int c = 0; c < 3; c++) fill2[cursor[indices[t * 3 + c]]++] = t;

            var screen = new Vector3[3];
            var shade = new float[3];

            for (int t = 0; t < triangles; t++)
            {
                var own = faceNormal[t];
                float ownLength = own.Length();
                if (ownLength < 1e-9f) continue;
                var ownUnit = own / ownLength;

                bool skip = false;
                for (int c = 0; c < 3; c++)
                {
                    int v = indices[t * 3 + c];
                    var p = Vector4.Transform(new Vector4(positions[v], 1f), clip);
                    if (p.W < 5f) { skip = true; break; }

                    screen[c] = new Vector3((p.X / p.W * 0.5f + 0.5f) * W, (1f - (p.Y / p.W * 0.5f + 0.5f)) * H, p.Z / p.W);

                    var sum = Vector3.Zero;
                    for (int k = count[v]; k < count[v + 1]; k++)
                    {
                        var other = faceNormal[fill2[k]];
                        float len = other.Length();
                        if (len > 1e-9f && Vector3.Dot(other / len, ownUnit) > 0.77f) sum += other;
                    }

                    var normal = sum.LengthSquared() > 1e-12f ? Vector3.Normalize(sum) : ownUnit;
                    var toEye = Vector3.Normalize(eye - positions[v]);
                    if (Vector3.Dot(normal, toEye) < 0f) normal = -normal;

                    shade[c] = 0.34f + 0.66f * MathF.Max(Vector3.Dot(normal, key), 0f) + 0.16f * MathF.Max(Vector3.Dot(normal, fill), 0f);
                }
                if (skip) continue;

                Vector3 a2 = screen[0], b2 = screen[1], c2 = screen[2];
                float area = (b2.X - a2.X) * (c2.Y - a2.Y) - (c2.X - a2.X) * (b2.Y - a2.Y);
                if (MathF.Abs(area) < 1e-9f) continue;

                int x0 = Math.Max((int)MathF.Floor(MathF.Min(a2.X, MathF.Min(b2.X, c2.X))), 0);
                int x1 = Math.Min((int)MathF.Ceiling(MathF.Max(a2.X, MathF.Max(b2.X, c2.X))), W - 1);
                int y0 = Math.Max((int)MathF.Floor(MathF.Min(a2.Y, MathF.Min(b2.Y, c2.Y))), 0);
                int y1 = Math.Min((int)MathF.Ceiling(MathF.Max(a2.Y, MathF.Max(b2.Y, c2.Y))), H - 1);

                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float l0 = ((b2.X - px) * (c2.Y - py) - (c2.X - px) * (b2.Y - py)) / area;
                        float l1 = ((c2.X - px) * (a2.Y - py) - (a2.X - px) * (c2.Y - py)) / area;
                        float l2 = 1f - l0 - l1;
                        if (l0 < -1e-5f || l1 < -1e-5f || l2 < -1e-5f) continue;

                        float z = l0 * a2.Z + l1 * b2.Z + l2 * c2.Z;
                        int at = y * W + x;
                        if (z >= depth[at]) continue;

                        depth[at] = z;
                        float s = l0 * shade[0] + l1 * shade[1] + l2 * shade[2];
                        colour[at * 3] = part.Colour.X * s;
                        colour[at * 3 + 1] = part.Colour.Y * s;
                        colour[at * 3 + 2] = part.Colour.Z * s;
                    }
            }
        }

        var pixels = new byte[width * height * 3];
        float scale = 1f / (ss * ss);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float r = 0, g = 0, b = 0;
                for (int dy = 0; dy < ss; dy++)
                    for (int dx = 0; dx < ss; dx++)
                    {
                        int at = ((y * ss + dy) * W + x * ss + dx) * 3;
                        r += colour[at]; g += colour[at + 1]; b += colour[at + 2];
                    }

                int k = (y * width + x) * 3;
                pixels[k] = (byte)(255 * Math.Clamp(r * scale, 0f, 1f));
                pixels[k + 1] = (byte)(255 * Math.Clamp(g * scale, 0f, 1f));
                pixels[k + 2] = (byte)(255 * Math.Clamp(b * scale, 0f, 1f));
            }

        return pixels;
    }

    private static List<Part> Checker(float halfX, float halfY, float square)
    {
        var light = new Mesh(new List<Vector3>(), new List<int>());
        var dark = new Mesh(new List<Vector3>(), new List<int>());

        int nx = (int)(2 * halfX / square), ny = (int)(2 * halfY / square);
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                var target = (i + j) % 2 == 0 ? light : dark;
                float x0 = -halfX + i * square, y0 = -halfY + j * square;
                int b = target.Positions.Count;
                target.Positions.Add(new Vector3(x0, y0, 0));
                target.Positions.Add(new Vector3(x0 + square, y0, 0));
                target.Positions.Add(new Vector3(x0 + square, y0 + square, 0));
                target.Positions.Add(new Vector3(x0, y0 + square, 0));
                target.Indices.AddRange([b, b + 1, b + 2, b, b + 2, b + 3]);
            }

        return [new Part(light, new Vector3(0.80f, 0.82f, 0.85f)), new Part(dark, new Vector3(0.70f, 0.72f, 0.76f))];
    }

    private static void SaveScene(byte[] pixels, int width, int height, string file)
    {
        var picture = BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, pixels, width * 3);
        picture.Freeze();
        Save(picture, Out + file, jpeg: true);
    }

    // --- The scenes -------------------------------------------------------------------------

    private static void RunSta(Action body)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    private static void PumpUntil(Func<bool> done, int milliseconds = 120000)
    {
        var watch = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Background,
            (_, _) => { if (done() || watch.ElapsedMilliseconds > milliseconds) frame.Continue = false; },
            Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static Mesh Moved(Mesh mesh, float x, float y, float z) =>
        MeshTransform.Transformed(mesh, Matrix4x4.CreateTranslation(x, y, z));

    [Fact]
    public void TextureScene() => RunSta(() =>
    {
        if (Folder is null) return;

        var model = new MainViewModel();
        var parts = new List<Part>();

        // kind, pitch, groove, depth, shape, colour, wrap round the side (else the top, or the front)
        var items = new (TextureKind Kind, float Pitch, float Groove, float Depth, Func<SceneObject> Make, Vector3 Colour, string How)[]
        {
            (TextureKind.Bark, 6f, 1f, 1.2f, () => new SceneObject("Barrel", Primitives.Prism(20f, 34f, 96)), new Vector3(0.62f, 0.45f, 0.32f), "side"),
            (TextureKind.Knurl, 2.4f, 0.7f, 0.7f, () => new SceneObject("Knob", Primitives.Prism(13f, 22f, 96)), new Vector3(0.40f, 0.46f, 0.58f), "side"),
            (TextureKind.Grain, 3f, 0.6f, 0.5f, () => new SceneObject("Board", Primitives.Box(66f, 38f, 12f)), new Vector3(0.84f, 0.66f, 0.42f), "top"),
            (TextureKind.Hex, 5f, 1f, 1.2f, () => new SceneObject("Panel", Primitives.Box(46f, 10f, 40f)), new Vector3(0.32f, 0.64f, 0.64f), "front")
        };

        float[] xs = [-92f, -40f, 24f, 96f];
        for (int i = 0; i < items.Length; i++)
        {
            var (kind, pitch, groove, depth, make, colour, how) = items[i];
            var part = make().Centred();
            part.Position = new Vector3(0, 0, part.WorldBounds.Size.Z / 2f);
            model.Scene.Objects.Clear();
            model.Scene.Objects.Add(part);
            part.IsSelected = true;
            model.RefreshSelection();

            model.BeginTextureCommand.Execute(null);
            model.EmbossTexture = kind;
            var b = part.WorldBounds;
            bool picked = how switch
            {
                "side" => model.PickEmbossFace(part, new Vector3(b.Center.X + b.Size.X / 2f, b.Center.Y, b.Center.Z), Vector3.UnitX),
                "top" => model.PickEmbossFace(part, new Vector3(b.Center.X, b.Center.Y, b.Max.Z), Vector3.UnitZ),
                _ => model.PickEmbossFace(part, new Vector3(b.Center.X, b.Min.Y, b.Center.Z), -Vector3.UnitY)
            };
            Assert.True(picked, model.Status);

            model.EmbossProjection = how == "side" ? TextProjection.Cylindrical : TextProjection.Planar;
            model.EmbossRaised = true;
            model.EmbossTexturePitch = pitch;
            model.EmbossTextureLine = groove;
            model.EmbossDepth = depth;

            var watch = Stopwatch.StartNew();
            model.ApplyEmbossCommand.Execute(null);
            PumpUntil(() => model.Scene.Objects.Count == 1 && !ReferenceEquals(model.Scene.Objects[0], part));
            var made = model.Scene.Objects[0].ToWorldMesh();
            log.WriteLine($"{kind}: {watch.Elapsed.TotalSeconds:0.0} s, {made.TriangleCount:N0} triangles; {model.Status}");

            parts.Add(new Part(Moved(made, xs[i], 0, 0), colour));
        }

        var scene = new List<Part>(Checker(190f, 100f, 10f));
        scene.AddRange(parts);

        var sky = (new Vector3(0.80f, 0.82f, 0.85f), new Vector3(0.66f, 0.69f, 0.74f));
        SaveScene(Draw(scene, new Vector3(45f, -245f, 150f), new Vector3(4f, 0f, 14f), 28f, 1500, 720, 2, sky.Item1, sky.Item2),
            1500, 720, "texture-examples.jpg");

    });
}
