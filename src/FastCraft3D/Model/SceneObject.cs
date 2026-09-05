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
            var size = value.ComputeBounds().Size;
            // Guard against flat meshes so dividing by the local size stays safe.
            localSize = new Vector3(
                size.X > 1e-5f ? size.X : 1f,
                size.Y > 1e-5f ? size.Y : 1f,
                size.Z > 1e-5f ? size.Z : 1f);
            Raise(nameof(Mesh));
            RaiseDerived();
        }
    }

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
        set => Set(ref isSelected, value);
    }

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
    /// Bounds in build-plate coordinates.
    ///
    /// Cached because working this out means transforming every vertex, and the manipulator
    /// asks for it on every rendered frame to decide whether its handles are still in the right
    /// place. On a dense imported mesh, recomputing it sixty times a second would be crippling.
    /// The cache is dropped whenever the transform or the mesh changes.
    /// </summary>
    public Bounds WorldBounds => worldBounds ??= ToWorldMesh().ComputeBounds();

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
