using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Xml;
using FastCraft3D.Geometry;

namespace FastCraft3D.Io;

/// <summary>
/// 3MF, the 3D Manufacturing Format: 3D Builder's own format, and the one every current slicer
/// prefers because it carries units, separate parts and colours, all of which STL throws away.
///
/// A 3MF is a zip. The model is XML inside it - by default at 3D/3dmodel.model - holding objects
/// made of vertices and triangles, a build list saying which objects are on the plate and where,
/// and optionally colours. Slicers that save assemblies (Bambu Studio, PrusaSlicer) put the
/// meshes in further .model files and point at them from components, so those are followed too.
/// </summary>
public static class ThreeMf
{
    private const string CoreNamespace = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";
    private const string ProductionNamespace = "http://schemas.microsoft.com/3dmanufacturing/production/2015/06";
    private const string DefaultModelPath = "3D/3dmodel.model";

    // --- reading ---------------------------------------------------------------------------------

    /// <summary>One part per item on the build plate, in millimetres, named and coloured if the file says.</summary>
    public static List<(string Name, Mesh Mesh, Vector3? Colour)> Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);

        string root = RootModelPath(zip);
        var models = new Dictionary<string, ModelPart>(StringComparer.OrdinalIgnoreCase);

        ModelPart Load(string part)
        {
            string key = part.TrimStart('/');
            if (models.TryGetValue(key, out var loaded)) return loaded;

            var entry = zip.GetEntry(key)
                ?? throw new InvalidDataException($"The 3MF points at {key}, which is not in the file.");

            using var stream = entry.Open();
            loaded = ModelPart.Parse(stream);
            models[key] = loaded;
            return loaded;
        }

        var main = Load(root);
        var parts = new List<(string, Mesh, Vector3?)>();

        // What is on the plate is the build list. A file with no build list is unusual, but every
        // object with a mesh is then the most useful reading of it.
        var items = main.Build.Count > 0
            ? main.Build
            : main.Objects.Values.Where(o => o.Mesh is not null)
                .Select(o => new BuildItem(o.Id, Matrix4x4.Identity, null)).ToList();

        int unnamed = 0;
        foreach (var item in items)
        {
            var model = item.Path is null ? main : Load(item.Path);
            if (!model.Objects.TryGetValue(item.ObjectId, out var obj)) continue;

            var mesh = new Mesh();
            Vector3? colour = null;
            Flatten(model, obj, item.Transform, mesh, ref colour, Load, depth: 0);

            if (mesh.TriangleCount == 0) continue;

            // Everything is converted to millimetres here and nowhere else.
            float toMillimetres = main.MillimetresPerUnit;
            if (toMillimetres != 1f)
                mesh = MeshTransform.Transformed(mesh, Matrix4x4.CreateScale(toMillimetres));

            string name = string.IsNullOrWhiteSpace(obj.Name) ? $"Object {++unnamed}" : obj.Name!;
            parts.Add((name, mesh, colour));
        }

        return parts;
    }

    /// <summary>
    /// An object's triangles in plate coordinates, following components into whatever they point at.
    ///
    /// Components nest, and a malformed file could make them point back at themselves, so the
    /// depth is capped rather than trusted.
    /// </summary>
    private static void Flatten(
        ModelPart model, ModelObject obj, Matrix4x4 transform, Mesh into, ref Vector3? colour,
        Func<string, ModelPart> load, int depth)
    {
        if (depth > 16) return;

        colour ??= model.ColourOf(obj);

        if (obj.Mesh is { } mesh)
        {
            int start = into.Positions.Count;
            foreach (var p in mesh.Positions) into.Positions.Add(Vector3.Transform(p, transform));
            foreach (int i in mesh.Indices) into.Indices.Add(start + i);
        }

        foreach (var component in obj.Components)
        {
            var target = component.Path is null ? model : load(component.Path);
            if (!target.Objects.TryGetValue(component.ObjectId, out var child)) continue;

            // Row vectors, as in System.Numerics: the component's own transform applies first.
            Flatten(target, child, component.Transform * transform, into, ref colour, load, depth + 1);
        }
    }

    private static string RootModelPath(ZipArchive zip)
    {
        var rels = zip.GetEntry("_rels/.rels");
        if (rels is null) return DefaultModelPath;

        using var stream = rels.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { IgnoreComments = true });

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Relationship") continue;

            string? type = reader.GetAttribute("Type");
            string? target = reader.GetAttribute("Target");
            if (target is not null && type is not null && type.EndsWith("/3dmodel", StringComparison.Ordinal))
                return target.TrimStart('/');
        }

        return DefaultModelPath;
    }

    private sealed record BuildItem(string ObjectId, Matrix4x4 Transform, string? Path);

    private sealed class ModelObject(string id)
    {
        public string Id { get; } = id;
        public string? Name { get; set; }
        public string? MaterialId { get; set; }
        public int MaterialIndex { get; set; }
        public Mesh? Mesh { get; set; }
        public List<BuildItem> Components { get; } = [];
    }

    /// <summary>One .model part of the package, read as a stream rather than loaded as a document.</summary>
    private sealed class ModelPart
    {
        public float MillimetresPerUnit { get; private set; } = 1f;
        public Dictionary<string, ModelObject> Objects { get; } = new(StringComparer.Ordinal);
        public List<BuildItem> Build { get; } = [];
        private readonly Dictionary<string, List<Vector3>> materials = new(StringComparer.Ordinal);

        public Vector3? ColourOf(ModelObject obj) =>
            obj.MaterialId is { } id && materials.TryGetValue(id, out var colours)
                && obj.MaterialIndex >= 0 && obj.MaterialIndex < colours.Count
                ? colours[obj.MaterialIndex]
                : null;

        /// <summary>
        /// Streamed, because a scan saved as 3MF is tens of megabytes of XML, and loading that as a
        /// document holds several times its size in memory for no gain.
        /// </summary>
        public static ModelPart Parse(Stream stream)
        {
            var part = new ModelPart();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = true,
                DtdProcessing = DtdProcessing.Prohibit
            });

            ModelObject? current = null;
            string? currentMaterials = null;
            bool inBuild = false;

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement)
                {
                    switch (reader.LocalName)
                    {
                        case "object": current = null; break;
                        case "basematerials": currentMaterials = null; break;
                        case "build": inBuild = false; break;
                    }
                    continue;
                }

                if (reader.NodeType != XmlNodeType.Element) continue;

                switch (reader.LocalName)
                {
                    case "model":
                        part.MillimetresPerUnit = UnitScale(reader.GetAttribute("unit"));
                        break;

                    case "basematerials":
                        currentMaterials = reader.GetAttribute("id");
                        if (currentMaterials is not null) part.materials[currentMaterials] = [];
                        break;

                    case "base" when currentMaterials is not null:
                        part.materials[currentMaterials].Add(ParseColour(reader.GetAttribute("displaycolor")));
                        break;

                    case "object":
                        current = new ModelObject(reader.GetAttribute("id") ?? "")
                        {
                            Name = reader.GetAttribute("name"),
                            MaterialId = reader.GetAttribute("pid"),
                            MaterialIndex = int.TryParse(reader.GetAttribute("pindex"), out int pi) ? pi : 0
                        };
                        part.Objects[current.Id] = current;
                        if (reader.IsEmptyElement) current = null;
                        break;

                    case "mesh" when current is not null:
                        current.Mesh = ReadMesh(reader, current);
                        break;

                    case "component" when current is not null:
                        current.Components.Add(new BuildItem(
                            reader.GetAttribute("objectid") ?? "",
                            ParseTransform(reader.GetAttribute("transform")),
                            reader.GetAttribute("path", ProductionNamespace)));
                        break;

                    case "build":
                        inBuild = !reader.IsEmptyElement;
                        break;

                    case "item" when inBuild:
                        part.Build.Add(new BuildItem(
                            reader.GetAttribute("objectid") ?? "",
                            ParseTransform(reader.GetAttribute("transform")),
                            reader.GetAttribute("path", ProductionNamespace)));
                        break;
                }
            }

            return part;
        }

        /// <summary>The vertices and triangles of one mesh, leaving the reader just past it.</summary>
        private static Mesh ReadMesh(XmlReader reader, ModelObject owner)
        {
            var mesh = new Mesh();
            if (reader.IsEmptyElement) return mesh;

            int depth = reader.Depth;
            bool colourTaken = owner.MaterialId is not null;

            while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
            {
                if (reader.NodeType != XmlNodeType.Element) continue;

                if (reader.LocalName == "vertex")
                {
                    mesh.Positions.Add(new Vector3(
                        Number(reader.GetAttribute("x")),
                        Number(reader.GetAttribute("y")),
                        Number(reader.GetAttribute("z"))));
                }
                else if (reader.LocalName == "triangle")
                {
                    if (!int.TryParse(reader.GetAttribute("v1"), out int a)
                        || !int.TryParse(reader.GetAttribute("v2"), out int b)
                        || !int.TryParse(reader.GetAttribute("v3"), out int c))
                        continue;

                    int count = mesh.Positions.Count;
                    if (a < 0 || b < 0 || c < 0 || a >= count || b >= count || c >= count) continue;

                    mesh.Indices.Add(a);
                    mesh.Indices.Add(b);
                    mesh.Indices.Add(c);

                    // A colour given on the triangles rather than the object: the first one is
                    // taken for the whole part, since a part here has one colour.
                    if (!colourTaken && reader.GetAttribute("pid") is { } pid)
                    {
                        owner.MaterialId = pid;
                        owner.MaterialIndex = int.TryParse(reader.GetAttribute("p1"), out int p1) ? p1 : 0;
                        colourTaken = true;
                    }
                }
            }

            return mesh;
        }

        private static float UnitScale(string? unit) => unit?.ToLowerInvariant() switch
        {
            "micron" => 0.001f,
            "centimeter" => 10f,
            "inch" => 25.4f,
            "foot" => 304.8f,
            "meter" => 1000f,
            _ => 1f // millimeter, and the spec's default when the attribute is missing
        };
    }

    private static float Number(string? text) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && float.IsFinite(v) ? v : 0f;

    /// <summary>"m00 m01 m02 m10 m11 m12 m20 m21 m22 m30 m31 m32" - row vectors, last column implied.</summary>
    private static Matrix4x4 ParseTransform(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Matrix4x4.Identity;

        var v = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Number).ToArray();
        if (v.Length != 12) return Matrix4x4.Identity;

        return new Matrix4x4(
            v[0], v[1], v[2], 0f,
            v[3], v[4], v[5], 0f,
            v[6], v[7], v[8], 0f,
            v[9], v[10], v[11], 1f);
    }

    /// <summary>"#RRGGBB" or "#RRGGBBAA", into 0..1 components. Grey if it cannot be read.</summary>
    private static Vector3 ParseColour(string? text)
    {
        if (text is { Length: >= 7 } && text[0] == '#'
            && int.TryParse(text.AsSpan(1, 2), NumberStyles.HexNumber, null, out int r)
            && int.TryParse(text.AsSpan(3, 2), NumberStyles.HexNumber, null, out int g)
            && int.TryParse(text.AsSpan(5, 2), NumberStyles.HexNumber, null, out int b))
            return new Vector3(r / 255f, g / 255f, b / 255f);

        return new Vector3(0.6f, 0.6f, 0.6f);
    }

    // --- writing ---------------------------------------------------------------------------------

    /// <summary>
    /// One object per part, in millimetres, each with its own colour, all on the build plate
    /// where they stand.
    /// </summary>
    public static void Write(string path, IReadOnlyList<ObjObject> parts)
    {
        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);

        WriteEntry(zip, "[Content_Types].xml", w =>
        {
            w.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
            w.WriteStartElement("Default");
            w.WriteAttributeString("Extension", "rels");
            w.WriteAttributeString("ContentType", "application/vnd.openxmlformats-package.relationships+xml");
            w.WriteEndElement();
            w.WriteStartElement("Default");
            w.WriteAttributeString("Extension", "model");
            w.WriteAttributeString("ContentType", "application/vnd.ms-package.3dmanufacturing-3dmodel+xml");
            w.WriteEndElement();
            w.WriteEndElement();
        });

        WriteEntry(zip, "_rels/.rels", w =>
        {
            w.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
            w.WriteStartElement("Relationship");
            w.WriteAttributeString("Target", "/" + DefaultModelPath);
            w.WriteAttributeString("Id", "rel0");
            w.WriteAttributeString("Type", "http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel");
            w.WriteEndElement();
            w.WriteEndElement();
        });

        WriteEntry(zip, DefaultModelPath, w =>
        {
            w.WriteStartElement("model", CoreNamespace);
            w.WriteAttributeString("unit", "millimeter");
            w.WriteAttributeString("xml", "lang", null, "en-GB");

            w.WriteStartElement("resources", CoreNamespace);

            // One colour per part, all in one table: id 1, indexed in part order.
            w.WriteStartElement("basematerials", CoreNamespace);
            w.WriteAttributeString("id", "1");
            foreach (var part in parts)
            {
                w.WriteStartElement("base", CoreNamespace);
                w.WriteAttributeString("name", part.Name);
                w.WriteAttributeString("displaycolor", Hex(part.Colour));
                w.WriteEndElement();
            }
            w.WriteEndElement();

            for (int i = 0; i < parts.Count; i++)
            {
                var mesh = parts[i].Mesh;

                w.WriteStartElement("object", CoreNamespace);
                w.WriteAttributeString("id", (i + 2).ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("type", "model");
                w.WriteAttributeString("name", parts[i].Name);
                w.WriteAttributeString("pid", "1");
                w.WriteAttributeString("pindex", i.ToString(CultureInfo.InvariantCulture));

                w.WriteStartElement("mesh", CoreNamespace);

                w.WriteStartElement("vertices", CoreNamespace);
                foreach (var p in mesh.Positions)
                {
                    w.WriteStartElement("vertex", CoreNamespace);
                    w.WriteAttributeString("x", Text(p.X));
                    w.WriteAttributeString("y", Text(p.Y));
                    w.WriteAttributeString("z", Text(p.Z));
                    w.WriteEndElement();
                }
                w.WriteEndElement();

                w.WriteStartElement("triangles", CoreNamespace);
                for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
                {
                    w.WriteStartElement("triangle", CoreNamespace);
                    w.WriteAttributeString("v1", mesh.Indices[t].ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("v2", mesh.Indices[t + 1].ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("v3", mesh.Indices[t + 2].ToString(CultureInfo.InvariantCulture));
                    w.WriteEndElement();
                }
                w.WriteEndElement();

                w.WriteEndElement(); // mesh
                w.WriteEndElement(); // object
            }

            w.WriteEndElement(); // resources

            w.WriteStartElement("build", CoreNamespace);
            for (int i = 0; i < parts.Count; i++)
            {
                w.WriteStartElement("item", CoreNamespace);
                w.WriteAttributeString("objectid", (i + 2).ToString(CultureInfo.InvariantCulture));
                w.WriteEndElement();
            }
            w.WriteEndElement();

            w.WriteEndElement(); // model
        });
    }

    private static void WriteEntry(ZipArchive zip, string name, Action<XmlWriter> body)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new System.Text.UTF8Encoding(false),
            Indent = false
        });

        writer.WriteStartDocument();
        body(writer);
        writer.WriteEndDocument();
    }

    private static string Text(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Hex(Vector3 colour)
    {
        static int Byte(float c) => (int)MathF.Round(Math.Clamp(c, 0f, 1f) * 255f);
        return $"#{Byte(colour.X):X2}{Byte(colour.Y):X2}{Byte(colour.Z):X2}";
    }
}
