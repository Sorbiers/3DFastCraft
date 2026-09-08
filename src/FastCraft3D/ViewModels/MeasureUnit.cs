namespace FastCraft3D.ViewModels;

/// <summary>
/// The unit the transform boxes are read and typed in.
///
/// A display unit and nothing more. Everything is stored in millimetres and exported in
/// millimetres, because an STL carries no unit and every slicer reads one as a millimetre - so
/// working in inches changes what the boxes say and not one number in the file.
/// </summary>
/// <param name="Label">What it is called, and what the boxes are labelled with.</param>
/// <param name="Millimetres">How many millimetres one of these is.</param>
/// <param name="Step">
/// What one press of an arrow key changes, in this unit. Chosen to be a round number at the
/// scale the unit is used at rather than a fixed distance: a millimetre is the obvious step in
/// millimetres and a silly one in feet.
/// </param>
/// <param name="Real">
/// The big unit of the same system, for reading a model at its scale. A model is drawn small and
/// stands for something large, so the reading beside the millimetres wants a large unit: metres
/// for anyone working metric, feet for anyone working in inches. Reading a 1:87 model in metres
/// while its own boxes say feet is two systems at once.
/// </param>
public readonly record struct MeasureUnit(string Label, float Millimetres, float Step, MeasureUnit.Big Real)
{
    /// <summary>
    /// What the picker draws and what a screen reader reads. DisplayMemberPath governs the first
    /// and not the second, so the two come apart without this - the same trap the pattern picker
    /// fell into.
    /// </summary>
    public override string ToString() => Label;

    /// <param name="Label">What the real-world reading is called.</param>
    /// <param name="Millimetres">How many millimetres one of them is.</param>
    public readonly record struct Big(string Label, float Millimetres);

    private static readonly Big Metre = new("m", 1000f);
    private static readonly Big Foot = new("ft", 304.8f);

    public static readonly MeasureUnit[] All =
    [
        new("mm", 1f, 1f, Metre),
        new("cm", 10f, 0.5f, Metre),
        new("m", 1000f, 0.01f, Metre),
        new("in", 25.4f, 0.25f, Foot),
        new("ft", 304.8f, 0.05f, Foot),
    ];

    public static MeasureUnit Default => All[0];

    /// <summary>Millimetres to this unit.</summary>
    public float From(float millimetres) => Millimetres <= 0f ? millimetres : millimetres / Millimetres;

    /// <summary>This unit back to millimetres.</summary>
    public float To(float value) => value * Millimetres;
}
