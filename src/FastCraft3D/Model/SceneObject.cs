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
    private List<Anchor> anchors = [];
    private Recipe? recipe;
    private Mesh? recipeMesh;
    private int filament = 1;
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

    /// <summary>
    /// What a tool knew about this part when it built it: where its shaft hole is, and the like.
    /// See <see cref="Anchor"/>. Empty for anything nothing was recorded about, which is most of
    /// a scene.
    /// </summary>
    public IReadOnlyList<Anchor> Anchors
    {
        get => anchors;
        set
        {
            anchors = value.ToList();
            Raise(nameof(Anchors));
        }
    }

    /// <summary>
    /// How a generator made this part, while the mesh is still the one it made. See
    /// <see cref="Geometry.Recipe"/>.
    ///
    /// Tied to the mesh itself rather than cleared by whatever changes it. A boolean, a smooth or
    /// a split puts a different mesh here and the recipe stops answering; a preview that swaps a
    /// mesh in and puts the old one back, and an undo that brings back the old object, find it
    /// answering again - with nothing in any of those places having to remember it.
    /// </summary>
    public Recipe? Recipe
    {
        get => ReferenceEquals(recipeMesh, mesh) ? recipe : null;
        set
        {
            recipe = value;
            recipeMesh = value is null ? null : mesh;
            Raise(nameof(Recipe));
        }
    }

    /// <summary>Local-space geometry, centred on its own origin.</summary>
    public Mesh Mesh
    {
        get => mesh;
        set
        {
            mesh = value;
            health = null;

            // New geometry: whatever was marked on the old was marked on a shape that is no
            // longer here, and its origin was that shape's. Centring puts both back, shifted to
            // match - see CentredOn.
            anchors.Clear();
            PivotIsOwn = false;

            var size = value.ComputeBounds().Size;
            // Guard against flat meshes so dividing by the local size stays safe.
            localSize = new Vector3(
                size.X > 1e-5f ? size.X : 1f,
                size.Y > 1e-5f ? size.Y : 1f,
                size.Z > 1e-5f ? size.Z : 1f);
            LocalCentre = value.ComputeBounds().Center;
            Raise(nameof(Mesh));
            Raise(nameof(Recipe));
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
    /// The primitive this object started as, if it did.
    ///
    /// A boolean, a split or an import produces a new object with no origin. Subtracting from a
    /// part, repairing it and aligning it keep the origin, because a cube with a hole in it still
    /// grows by a clearance the way a cube does - but the mesh is no longer the primitive, which
    /// is what <see cref="IsPristine"/> is for.
    /// </summary>
    public PrimitiveKind? Origin { get; set; }

    /// <summary>
    /// Whether the mesh is still exactly the primitive <see cref="Origin"/> generated.
    ///
    /// Rounding regenerates a shape from its parameters rather than filleting its mesh, so it
    /// can only be offered while those parameters describe the whole object. Keying it on the
    /// origin alone rebuilt a subtracted cylinder from scratch and the hole vanished. Set only
    /// where a primitive is generated - inserting, rounding, connector pins - and never copied by
    /// anything that replaces the mesh.
    /// </summary>
    public bool IsPristine { get; set; }

    /// <summary>Whether this object can be regenerated with rounded edges.</summary>
    public bool CanRound => IsPristine && Origin is { } kind && RoundedPrimitives.Supports(kind);

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value) return;

            // Hidden and locked objects are out of reach, and this is the one place that has to
            // know it: every way of selecting - a click, the list, Select all - comes through here,
            // and anything that cannot be selected cannot be moved, cut or deleted by any tool.
            if (value && !CanBeSelected) return;

            if (value) PickedAt = ++picks;

            Set(ref isSelected, value);
        }
    }

    /// <summary>
    /// Taken off the plate for now: not drawn, not picked, not selectable, and left out when a tool
    /// works on everything - Export, Drawing, Repair, Rebuild. It is still in the project, and
    /// saved with it. Hiding a selected object lets go of it.
    /// </summary>
    public bool IsHidden
    {
        get => isHidden;
        set
        {
            if (isHidden == value) return;
            if (value) IsSelected = false;

            Set(ref isHidden, value);
            Raise(nameof(CanBeSelected));
        }
    }

    /// <summary>
    /// Kept as it is: drawn, and in the way of a move that stops on contact, but it cannot be
    /// selected, so nothing can move or change it until it is unlocked. Locking a selected object
    /// lets go of it.
    /// </summary>
    public bool IsLocked
    {
        get => isLocked;
        set
        {
            if (isLocked == value) return;
            if (value) IsSelected = false;

            Set(ref isLocked, value);
            Raise(nameof(CanBeSelected));
        }
    }

    public bool CanBeSelected => !isHidden && !isLocked;

    private bool isHidden;
    private bool isLocked;

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

    /// <summary>
    /// Which filament prints this part, counted from one, on a printer that has more than one.
    ///
    /// Deliberately not the colour. The colour is how the model is looked at while it is being
    /// made, and two parts that print in the same filament are often coloured differently just to
    /// tell them apart; equally, two parts that happen to be the same shade are not therefore the
    /// same material. The slicer is told this number and nothing is read back off the colour.
    ///
    /// One for everything until it is set, so a single-filament printer never sees it at all.
    /// </summary>
    public int Filament
    {
        get => filament;
        set => Set(ref filament, Math.Clamp(value, 1, MostFilaments));
    }

    /// <summary>As many as the biggest multi-material units carry.</summary>
    public const int MostFilaments = 16;

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

    /// <summary>The marked features where they actually are on the plate.</summary>
    public IEnumerable<Anchor> WorldAnchors()
    {
        var transform = Transform;
        foreach (var a in anchors) yield return a.Through(transform);
    }

    /// <summary>
    /// The point the object is measured about: the axis a tool marked on it, or the middle of its
    /// box when nothing did.
    ///
    /// Aligning two cut-away gears by their boxes lines up the outlines of their teeth, which is
    /// not what anybody means by lining up two gears. Once the tool that made one has said where
    /// its shaft is, there is no reason to go on guessing from the silhouette.
    /// </summary>
    public Vector3 WorldCentre
    {
        get
        {
            // A pivot somebody put where they wanted it is the point the object is about, and
            // there is nothing left to work out.
            if (PivotIsOwn) return Vector3.Transform(Vector3.Zero, Transform);

            var centre = WorldBounds.Center;
            if (anchors.Count == 0) return centre;

            var axis = WorldAnchors().FirstOrDefault(a => a.Size > 0f);
            if (axis.Size <= 0f) return centre;

            // On the axis, level with the middle of the box: along the shaft it is still the
            // part's own middle that matters, and the axis says nothing about that.
            return axis.At + axis.Along * Vector3.Dot(centre - axis.At, axis.Along);
        }
    }

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
        Origin is PrimitiveKind.Cube or PrimitiveKind.Cylinder or PrimitiveKind.Sphere
        || PiecesTakeClearance;

    /// <summary>
    /// Set on a group whose every member could take a clearance on its own.
    ///
    /// Grouping throws the members away - the meshes are concatenated and the objects replaced -
    /// so by the time anyone subtracts the group, nothing is left to say it was six cylinders.
    /// This is that record, and it is all this needs to be: the pieces are found again
    /// geometrically, and each is grown about its own centre.
    ///
    /// A group is not grown as one lump. Scaling the whole thing up by the clearance would push
    /// the pins apart as well as fatten them, and the holes would come out in the wrong places -
    /// which is the failure this exists to avoid, not a hypothetical one.
    /// </summary>
    public bool PiecesTakeClearance { get; set; }

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

        // A group: each piece on its own, about its own centre, so they fatten without drifting.
        if (PiecesTakeClearance && Origin is null)
            return Mesh.Combine(MeshComponents.Split(ToWorldMesh()).Select(p => Grown(p, clearance)));

        var grown = new Vector3(
            Grow(scale.X, SizeX, clearance),
            Grow(scale.Y, SizeY, clearance),
            Grow(scale.Z, SizeZ, clearance));

        return MeshTransform.Transformed(mesh, MeshTransform.Compose(position, rotation, grown));
    }

    /// <summary>
    /// One piece of a group, grown about its own centre by the clearance on every side.
    ///
    /// The same arithmetic the whole object gets, applied to a piece that has no transform of
    /// its own: its box is all there is to go on. Exact on a box or a cylinder standing on any
    /// of its axes, which is what a group is allowed to hold.
    /// </summary>
    private static Mesh Grown(Mesh piece, float clearance)
    {
        var box = piece.ComputeBounds();
        if (box.IsEmpty) return piece;

        Vector3 size = box.Size;
        var factor = new Vector3(
            size.X <= 1e-4f ? 1f : (size.X + 2f * clearance) / size.X,
            size.Y <= 1e-4f ? 1f : (size.Y + 2f * clearance) / size.Y,
            size.Z <= 1e-4f ? 1f : (size.Z + 2f * clearance) / size.Z);

        Vector3 centre = box.Center;

        return MeshTransform.Transformed(piece,
            Matrix4x4.CreateTranslation(-centre)
            * Matrix4x4.CreateScale(factor)
            * Matrix4x4.CreateTranslation(centre));
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
    /// <summary>
    /// Whether the object's origin was put where it is on purpose rather than falling out of how
    /// the geometry was built.
    ///
    /// The origin is the pivot: it is what a position reads, what a turn goes round, and what
    /// lining two parts up by their middles compares - so nothing else in the app has to know
    /// what a pivot is. What this adds is that a pivot somebody chose is not quietly moved back
    /// to the middle of the box by the next tool that tidies up after itself.
    ///
    /// Kept in the project file; dropped when the geometry is replaced, since the point was a
    /// point on that geometry.
    /// </summary>
    public bool PivotIsOwn { get; set; }

    /// <summary>Puts the origin at the middle of the box, unless somebody has chosen one.</summary>
    public SceneObject Centred() => PivotIsOwn ? this : Shifted(mesh.ComputeBounds().Center);

    /// <summary>
    /// The same, about a point the caller names in the mesh's own coordinates rather than the
    /// middle of its box.
    ///
    /// For a part that is <em>about</em> something. A gear is about its shaft, not about the
    /// outline of its teeth - and a cut-away gear is a disc with teeth over a quarter of its rim,
    /// so the middle of its box sits two millimetres off the axis it turns on. Everything that
    /// reads a position, lines two parts up by their middles or turns one about its own centre
    /// was then out by that much, in a way nothing on screen explained.
    /// </summary>
    public SceneObject CentredOn(Vector3 origin)
    {
        Shifted(origin);
        PivotIsOwn = true;

        return this;
    }

    /// <summary>The move itself, which says nothing about whose idea the new origin was.</summary>
    private SceneObject Shifted(Vector3 origin)
    {
        if (origin.LengthSquared() < 1e-10f) return this;

        var marked = anchors.Select(a => a.Moved(-origin)).ToList();
        var made = Recipe;

        Mesh = MeshTransform.Transformed(mesh, Matrix4x4.CreateTranslation(-origin));
        Anchors = marked;

        // The same shape moved in its own coordinates, so still what the generator made.
        if (made is not null) Recipe = made with { Origin = made.Origin - origin };

        // Shifting the geometry one way and the translation the other leaves the object where it
        // was, whatever turn and scale sit between the two.
        Position += Vector3.TransformNormal(
            origin, Matrix4x4.CreateScale(scale) * MeshTransform.Rotation(rotation));

        return this;
    }

    public SceneObject Clone() => new(Name, mesh.Clone())
    {
        Origin = Origin,
        IsPristine = IsPristine,
        position = position,
        rotation = rotation,
        scale = scale,
        colour = colour,
        filament = filament,
        anchors = [.. anchors],
        PivotIsOwn = PivotIsOwn,
        Recipe = Recipe
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
