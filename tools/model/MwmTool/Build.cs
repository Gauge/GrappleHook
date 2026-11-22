using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BulletXNA.BulletCollision;
using BulletXNA.LinearMath;
using VRage.Import;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender.Animations;
using VRageRender.Import;

/// <summary>
/// Writes .mwm models with the game's own exporter (VRageRender.Import.MyModelExporter), from the
/// mesh JSON that tools/model/build.py writes out of Blender. No MwmBuilder, no Havok tools.
///
/// Mesh JSON, in Space Engineers model space (metres, +Y up, -Z forward):
///   { "parts":   [ { "material": name, "positions": [x,y,z..], "normals": [..], "uvs": [u,v..],
///                    "indices": [..] } ],             triangles counter-clockwise seen from outside
///     "dummies": [ { "name": .., "matrix": [M11..M44], "props": { key: value } } ],
///     "havok":   optional path to an .mwm whose collision is copied in }
/// Materials come from a library JSON written by the materials command, out of real models, so
/// every descriptor (technique, texture paths) is one the game already ships.
/// </summary>
static class Build
{
    public static void DumpMaterials(string outJson, IEnumerable<string> models)
    {
        var lib = new SortedDictionary<string, object>();
        foreach (string path in models)
        {
            var imp = new MyModelImporter();
            imp.ImportData(path);
            var d = imp.GetTagData();
            if (!d.ContainsKey("MeshParts") && d.TryGetValue("GeometryDataAsset", out var g))
            {
                imp = new MyModelImporter();
                imp.ImportData(Path.Combine(Path.GetDirectoryName(path), Path.GetFileName(((string)g).Replace('\\', '/')) + ".mwm"));
                d = imp.GetTagData();
            }
            foreach (var part in (List<MyMeshPartInfo>)d["MeshParts"])
            {
                var m = part.m_MaterialDesc;
                if (m == null || lib.ContainsKey(m.MaterialName)) continue;
                lib[m.MaterialName] = new
                {
                    technique = m.Technique,
                    textures = m.Textures,
                    userData = m.UserData,
                    glassCW = m.GlassCW,
                    glassCCW = m.GlassCCW,
                    glassSmoothNormals = m.GlassSmoothNormals,
                    source = Path.GetFileName(path),
                };
            }
        }
        File.WriteAllText(outJson, JsonSerializer.Serialize(lib, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{lib.Count} materials -> {outJson}");
    }

    static MyMaterialDescriptor Material(JsonElement lib, string name)
    {
        if (!lib.TryGetProperty(name, out var e))
            throw new InvalidDataException($"material {name} is not in the library");
        var m = new MyMaterialDescriptor(name) { Technique = e.GetProperty("technique").GetString() };
        foreach (var t in e.GetProperty("textures").EnumerateObject()) m.Textures[t.Name] = t.Value.GetString();
        foreach (var t in e.GetProperty("userData").EnumerateObject()) m.UserData[t.Name] = t.Value.GetString();
        m.GlassCW = e.GetProperty("glassCW").GetString() ?? "";
        m.GlassCCW = e.GetProperty("glassCCW").GetString() ?? "";
        m.GlassSmoothNormals = e.GetProperty("glassSmoothNormals").GetBoolean();
        return m;
    }

    /// <summary>Stable 32 bit FNV-1a; the game never reads the part hash, but keep it deterministic.</summary>
    static int Fnv(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) { h ^= c; h *= 16777619; }
        return unchecked((int)h);
    }

    sealed class Triangles : IPrimitiveManagerBase
    {
        public readonly List<Vector3> P = new List<Vector3>();
        public readonly List<int> I = new List<int>();
        public void Cleanup() { }
        public bool IsTrimesh() => true;
        public int GetPrimitiveCount() => I.Count / 3;
        public void GetPrimitiveBox(int i, out AABB box)
        {
            var b = BoundingBox.CreateInvalid();
            b.Include(P[I[i * 3]]); b.Include(P[I[i * 3 + 1]]); b.Include(P[I[i * 3 + 2]]);
            box = new AABB(new IndexedVector3(b.Min.X, b.Min.Y, b.Min.Z), new IndexedVector3(b.Max.X, b.Max.Y, b.Max.Z));
        }
        public void GetPrimitiveTriangle(int i, PrimitiveTriangle t)
        {
            for (int k = 0; k < 3; k++) { var p = P[I[i * 3 + k]]; t.m_vertices[k] = new IndexedVector3(p.X, p.Y, p.Z); }
        }
    }

    public static void Run(string inJson, string materialsJson, string outMwm)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(inJson));
        using var libDoc = JsonDocument.Parse(File.ReadAllText(materialsJson));
        var root = doc.RootElement;
        var lib = libDoc.RootElement;

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var parts = new List<MyMeshPartInfo>();
        var tris = new Triangles();

        foreach (var pe in root.GetProperty("parts").EnumerateArray())
        {
            float[] p = pe.GetProperty("positions").EnumerateArray().Select(x => x.GetSingle()).ToArray();
            float[] n = pe.GetProperty("normals").EnumerateArray().Select(x => x.GetSingle()).ToArray();
            float[] t = pe.GetProperty("uvs").EnumerateArray().Select(x => x.GetSingle()).ToArray();
            int[] ix = pe.GetProperty("indices").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            int start = positions.Count;
            for (int i = 0; i < p.Length / 3; i++)
            {
                positions.Add(new Vector3(p[i * 3], p[i * 3 + 1], p[i * 3 + 2]));
                normals.Add(Vector3.Normalize(new Vector3(n[i * 3], n[i * 3 + 1], n[i * 3 + 2])));
                uvs.Add(new Vector2(t[i * 2], 1f - t[i * 2 + 1]));  // Blender V runs up, DirectX V runs down
            }
            string name = pe.GetProperty("material").GetString();
            var part = new MyMeshPartInfo { m_MaterialDesc = Material(lib, name), m_MaterialHash = Fnv(name) };
            part.Technique = part.m_MaterialDesc.TechniqueEnum;
            // the game draws clockwise-from-outside triangles (checked against shipped models)
            for (int i = 0; i < ix.Length; i += 3)
            {
                part.m_indices.Add(start + ix[i]);
                part.m_indices.Add(start + ix[i + 2]);
                part.m_indices.Add(start + ix[i + 1]);
            }
            if (part.m_indices.Count == 0) continue;
            parts.Add(part);
            tris.I.AddRange(part.m_indices);
        }
        tris.P.AddRange(positions);

        // per vertex tangent frames from the UVs (Lengyel), for the normal maps
        var tan = new Vector3[positions.Count];
        var bit = new Vector3[positions.Count];
        foreach (var part in parts)
            for (int i = 0; i < part.m_indices.Count; i += 3)
            {
                int a = part.m_indices[i], b = part.m_indices[i + 1], c = part.m_indices[i + 2];
                Vector3 e1 = positions[b] - positions[a], e2 = positions[c] - positions[a];
                Vector2 d1 = uvs[b] - uvs[a], d2 = uvs[c] - uvs[a];
                float r = d1.X * d2.Y - d2.X * d1.Y;
                if (Math.Abs(r) < 1e-12f) continue;
                r = 1f / r;
                Vector3 sdir = (e1 * d2.Y - e2 * d1.Y) * r, tdir = (e2 * d1.X - e1 * d2.X) * r;
                foreach (int k in new[] { a, b, c }) { tan[k] += sdir; bit[k] += tdir; }
            }

        int count = positions.Count;
        var hv = new HalfVector4[count];
        var hn = new Byte4[count];
        var huv = new HalfVector2[count];
        var hb = new Byte4[count];
        var ht = new Byte4[count];
        var box = BoundingBox.CreateInvalid();
        for (int i = 0; i < count; i++)
        {
            Vector3 p = positions[i], n = normals[i];
            box.Include(p);
            hv[i] = VF_Packer.PackPosition(ref p);
            hn[i] = VF_Packer.PackNormalB4(ref n);
            huv[i] = new HalfVector2(uvs[i]);
            Vector3 t = tan[i] - n * Vector3.Dot(n, tan[i]);
            if (t.LengthSquared() < 1e-12f) t = Vector3.CalculatePerpendicularVector(n);
            t = Vector3.Normalize(t);
            float w = Vector3.Dot(Vector3.Cross(n, t), bit[i]) < 0 ? -1f : 1f;
            Vector3 bn = Vector3.Cross(n, t) * w;
            var tw = new Vector4(t, w);
            ht[i] = VF_Packer.PackTangentSignB4(ref tw);
            hb[i] = VF_Packer.PackNormalB4(ref bn);
        }

        var dummies = new Dictionary<string, MyModelDummy>();
        if (root.TryGetProperty("dummies", out var de))
            foreach (var e in de.EnumerateArray())
            {
                float[] m = e.GetProperty("matrix").EnumerateArray().Select(x => x.GetSingle()).ToArray();
                var props = new Dictionary<string, object>();
                if (e.TryGetProperty("props", out var pr))
                    foreach (var kv in pr.EnumerateObject()) props[kv.Name] = kv.Value.GetString();
                string name = e.GetProperty("name").GetString();
                dummies[name] = new MyModelDummy
                {
                    Name = name,
                    Matrix = new Matrix(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]),
                    CustomData = props,
                };
            }

        byte[] havok = new byte[0];
        if (root.TryGetProperty("havok", out var hk) && hk.ValueKind == JsonValueKind.String)
        {
            var imp = new MyModelImporter();
            imp.ImportData(hk.GetString());
            havok = (byte[])imp.GetTagData()["HavokCollisionGeometry"];
        }

        var bvh = new GImpactQuantizedBvh(tris);
        bvh.BuildSet();

        if (count == 0) box = new BoundingBox(Vector3.Zero, Vector3.Zero);
        var tags = new Dictionary<string, object>
        {
            // empty: the exporter appends "Version:...", and the importer only reads the version from the first entry
            ["Debug"] = new string[0],
            ["Dummies"] = dummies,
            ["Vertices"] = hv,
            ["Normals"] = hn,
            ["TexCoords0"] = huv,
            ["Binormals"] = hb,
            ["Tangents"] = ht,
            ["TexCoords1"] = new HalfVector2[0],
            ["BlendIndices"] = new Vector4I[0],
            ["BlendWeights"] = new Vector4[0],
            ["ModelBvh"] = bvh,
            ["MeshParts"] = parts,
            ["RescaleFactor"] = 1f,
            ["UseChannelTextures"] = false,
            ["BoundingBox"] = box,
            ["BoundingSphere"] = BoundingSphere.CreateFromBoundingBox(box),
            ["SwapWindingOrder"] = false,
            ["Sections"] = new List<MyMeshSectionInfo>(),
            ["ModelInfo"] = new MyModelInfo(tris.I.Count / 3, count, box.Size),
            ["Animations"] = new ModelAnimations(),
            ["Bones"] = new[] { new MyModelBone { Index = 0, Name = "RootNode", Parent = -1, Transform = Matrix.Identity } },
            ["BoneMapping"] = new Vector3I[0],
            ["HavokCollisionGeometry"] = havok,
            ["PatternScale"] = 1f,
            ["LODs"] = new MyLODDescriptor[0],
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outMwm)));
        MyModelExporter.ExportModelData(outMwm, tags);
        Console.WriteLine($"{outMwm}: {count} vertices, {tris.I.Count / 3} triangles, {parts.Count} parts, {dummies.Count} dummies, havok {havok.Length} bytes, box {box.Min} .. {box.Max}");
    }
}
