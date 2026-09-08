using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using FastCraft3D.Geometry;

namespace FastCraft3D.Model;

/// <summary>
/// One editable object: a local-space mesh plus a position / rotation / size transform.
///
/// Transform components are exposed as individual scalars rather than as Vector3 properties
/// because WPF cannot bind to a field of a struct property - binding to "Position.X" would
/// read once and never update. Keeping the struct internal and the scalars public gives the
/// properties panel something it can two-way bind to.
///
/// Size is expressed in millimetres rather than as a scale factor: for a print you care that
/// a part is 12 mm wide, not that it is 0.6x its original size.
/// </summary>
public sealed class SceneObject : INotifyPropertyChanged
{
    private Mesh mesh = new();
    private Vector3 localSize = Vector3.One;
    private Vector3 position;
    private Vector3 rotation;
    private Vector3 scale = Vector3.One;
    private Vector3 colour = new(0.30f, 0.55f, 0.85f);
    private string name = "Object";
    private bool isSelected;
    private Bounds? worldBounds;
    private MeshHealth? health;

    public SceneObject(string name, Mesh mesh)
    {
        this.name = name;
        Mesh = mesh;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Local-space geometry, centred on its own origin.</summary>
    public Mesh Mesh
    {
        get => mesh;
        set
        {
            mesh = value;
            health = null;

            var size = value.ComputeBounds().Size;
            // Guard against flat meshes so dividing by the local size stays safe.
            localSize = new Vector3(
                size.X > 1e-5f ? size.X : 1f,
                size.Y > 1e-5f ? size.Y : 1f,
                size.Z > 1e-5f ? size.Z : 1f);
            LocalCentre = value.ComputeBounds().Center;
            Raise(nameof(Mesh));
            RaiseDerived();
        }
    }

    /// <summary>
    /// The middle of the geometry in its own coordinates. Usually the origin, since primitives
    /// are built centred, but not after a boolean - and the resize handles need the middle of
    /// the object's own box, not of the world-aligned one round it.
    /// </summary>
    public Vector3 LocalCentre { get; private set; }

    public string Name
    {
        get => name;
        set => Set(ref name, value);
    }

    /// <summary>
    /// The primitive this object still is, if it is one.
    ///
    /// Rounding regenerates a shape from its parameters rather than filleting its mesh, so it
    /// can only be offered while those parameters still describe the object. A boolean, a split
    /// or an import produces a new object with no origin, and rounding is refused there - which
    /// is the honest answer, not a limitation to work around.
    /// </summary>
    public PrimitiveKind? Origin { get; set; }

    /// <summary>Whether this object can be regenerated with rounded edges.</summary>
    public bool CanRound => Origin is { } kind && RoundedPrimitives.Supports(kind);

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value) return;
            if (value) PickedAt = ++picks;

            Set(ref isSelected, value);
        }
    }

    /// <summary>
    /// When this was last picked, for putting a selection back into the order it was made in.
    ///
    /// The scene keeps its objects in the order they were created, which is the right order for
    /// the list and the wrong one for a boolean: subtracting needs to know which one you meant to
    /// keep, and the only thing that says so is which you clicked first. Without this the order
    /// of the two clicks made no difference at all, because the answer was read off the list.
    /// </summary>
    public long PickedAt { get; private set; }

    private static long picks;

    /// <summary>Diffuse colour, components in 0..1.</summary>
    public Vector3 Colour
    {
        get => colour;
        set => Set(ref colour, value);
    }

    public Vector3 Position
    {
        get => position;
        set
        {
            position = value;
            RaiseTransform();
        }
    }

    public Vector3 Rotation
    {
        get => rotation;
        set
        {
            rotation = value;
            RaiseTransform();
        }
    }

    public Vector3 Scale
    {
        get => scale;
        set
        {
            scale = value;
            RaiseTransform();
        }
    }

    public float PositionX { get => position.X; set { position.X = value; RaiseTransform(); } }
    public float PositionY { get => position.Y; set { position.Y = value; RaiseTransform(); } }
    public float PositionZ { get => position.Z; set { position.Z = value; RaiseTransform(); } }

    public float RotationX { get => rotation.X; set { rotation.X = value; RaiseTransform(); } }
    public float RotationY { get => rotation.Y; set { rotation.Y = value; RaiseTransform(); } }
    public float RotationZ { get => rotation.Z; set { rotation.Z = value; RaiseTransform(); } }

    public float SizeX
    {
        get => MathF.Abs(localSize.X * scale.X);
        set { scale.X = SafeScale(value, localSize.X, scale.X); RaiseTransform(); }
    }

    public float SizeY
    {
        get => MathF.Abs(localSize.Y * scale.Y);
        set { scale.Y = SafeScale(value, localSize.Y, scale.Y); RaiseTransform(); }
    }

    public float SizeZ
    {
        get => MathF.Abs(localSize.Z * scale.Z);
        set { scale.Z = SafeScale(value, localSize.Z, scale.Z); RaiseTransform(); }
    }

    /// <summary>
    /// Size reads as a magnitude - a part is 5 mm wide whether or not it is mirrored, and a
    /// negative number in the properties box would only confuse. The mirror lives in the sign
    /// of the scale, which this preserves so resizing a mirrored object keeps it mirrored.
    /// </summary>
    private static float SafeScale(float requestedSize, float localExtent, float currentScale)
    {
        if (MathF.Abs(requestedSize) < 1e-4f) return currentScale;
        float magnitude = MathF.Abs(requestedSize) / localExtent;
        return currentScale < 0 ? -magnitude : magnitude;
    }

    public Matrix4x4 Transform => MeshTransform.Compose(position, rotation, scale);

    /// <summary>Geometry in build-plate coordinates, ready for export.</summary>
    public Mesh ToWorldMesh() => MeshTransform.Transformed(mesh, Transform);

    /// <summary>
    /// Whether this can be grown by a clearance and still mean it.
    ///
    /// Growing each dimension by twice the clearance is the true offset for these three and
    /// nothing else. On a cone or a pyramid the sloped face ends up nearer than asked - the
    /// clearance comes out as c times the cosine of the slope - and a clearance that is quietly
    /// smaller than the number typed is the one direction that jams a printed part.
    /// </summary>
    public bool CanTakeClearance =>
        Origin is PrimitiveKind.Cube or PrimitiveKind.Cylinder or PrimitiveKind.Sphere;

    /// <summary>
    /// The same geometry in build-plate coordinates, grown by <paramref name="clearance"/> on
    /// every side - the cutter for a hole that a part this size will actually go into.
    ///
    /// Grown in the object's own frame, so a turned part grows along its own axes rather than
    /// the plate's. A cylinder gains the clearance on its radius and on each end; a cube on each
    /// of its six faces.
    /// </summary>
    public Mesh ToWorldMeshGrown(float clearance)
    {
        if (clearance <= 0f) return ToWorldMesh();

        var grown = new Vector3(
            Grow(scale.X, SizeX, clearance),
            Grow(scale.Y, SizeY, clearance),
            Grow(scale.Z, SizeZ, clearance));

        return MeshTransform.Transformed(mesh, MeshTransform.Compose(position, rotation, grown));
    }

    /// <summary>
    /// The scale that adds a clearance to each side. The sign is kept: a mirrored object has a
    /// negative scale, and flipping it here would turn the cutter inside out.
    /// </summary>
    private static float Grow(float scale, float size, float clearance) =>
        size <= 1e-4f ? scale : scale * ((size + 2f * clearance) / size);

    /// <summary>
    /// Bounds in build-plate coordinates.
    ///
    /// Cached because working this out means transforming every vertex, and the manipulator
    /// asks for it on every rendered frame to decide whether its handles are still in the right
    /// place. On a dense imported mesh, recomputing it sixty times a second would be crippling.
    /// The cache is dropped whenever the transform or the mesh changes.
    /// </summary>
    public Bounds WorldBounds => worldBounds ??= MeasureWorldBounds();

    /// <summary>
    /// The box round the transformed geometry, walked rather than built.
    ///
    /// This used to transform the whole mesh and take the bounds of the result, which meant a new
    /// copy of every vertex and every index each time the cache was dropped - and it is dropped on
    /// every transform, while the renderer asks for it each frame. On a three million triangle
    /// mould that is a hundred and fifty megabytes allocated per frame of a drag. The corners of
    /// the local box will not do instead: turned, its transformed corners bound a larger box than
    /// the geometry does, and Drop to plate and Align would both land in the wrong place.
    /// </summary>
    private Bounds MeasureWorldBounds()
    {
        if (mesh.Positions.Count == 0) return Bounds.Empty;

        var transform = Transform;
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);

        foreach (var p in mesh.Positions)
        {
            var at = Vector3.Transform(p, transform);
            min = Vector3.Min(min, at);
            max = Vector3.Max(max, at);
        }

        return new Bounds(min, max);
    }

    /// <summary>
    /// What is wrong with the geometry, if anything, kept until the mesh is replaced.
    ///
    /// Measured on the local mesh and not the transformed one, because a transform cannot change
    /// the topology: the same edges meet the same edges wherever the object is put. That is what
    /// makes it worth keeping - the status bar asks for this every time anything is raised, and on
    /// a three million triangle mould the check runs for four seconds. It was being run a dozen
    /// times per nudge, on the UI thread, which is what "Not Responding" was.
    /// </summary>
    public MeshHealth Health => health ??= mesh.CheckHealth();

    /// <summary>
    /// How much material this is on the plate, in cm3.
    ///
    /// Volume is the one part of the health that a transform does change, and it changes by the
    /// determinant of the scale - so it is scaled here rather than measured again.
    /// </summary>
    public double VolumeCm3 =>
        Math.Abs(Health.SignedVolume * scale.X * scale.Y * scale.Z) / 1000.0;

    /// <summary>
    /// Moves the geometry onto the object's own origin and takes the position with it, so the
    /// object stays exactly where it was drawn.
    ///
    /// Anything made from geometry that is already in build-plate coordinates - a boolean, a
    /// group, a split, an import - arrives with its shape out at whatever corner of the bed it
    /// belongs to and its position still reading nothing at all. It looks right and it is right,
    /// but the position boxes then describe somewhere else entirely, and typing a coordinate
    /// into one measures from the wrong place. This puts the two back in step.
    /// </summary>
    public SceneObject Centred()
    {
        var centre = mesh.ComputeBounds().Center;
        if (centre.LengthSquared() < 1e-10f) return this;

        Mesh = MeshTransform.Transformed(mesh, Matrix4x4.CreateTranslation(-centre));

        // Shifting the geometry one way and the translation the other leaves the object where it
        // was, whatever turn and scale sit between the two.
        Position += Vector3.TransformNormal(
            centre, Matrix4x4.CreateScale(scale) * MeshTransform.Rotation(rotation));

        return this;
    }

    public SceneObject Clone() => new(Name, mesh.Clone())
    {
        Origin = Origin,
        position = position,
        rotation = rotation,
        scale = scale,
        colour = colour
    };

    private void RaiseTransform()
    {
        Raise(nameof(Transform));
        RaiseDerived();
    }

    private void RaiseDerived()
    {
        // Both the transform and the mesh reach here, and either invalidates the cached bounds.
        worldBounds = null;

        foreach (var property in new[]
        {
            nameof(PositionX), nameof(PositionY), nameof(PositionZ),
            nameof(RotationX), nameof(RotationY), nameof(RotationZ),
            nameof(SizeX), nameof(SizeY), nameof(SizeZ),
            nameof(Position), nameof(Rotation), nameof(Scale)
        })
        {
            Raise(property);
        }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Raise(property);
    }

    private void Raise(string? property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    /// <summary>
    /// The object list uses a DataTemplate, but accessibility tools fall back to ToString()
    /// for the item name - without this they announce the class name instead of the object.
    /// </summary>
    public override string ToString() => Name;
}
