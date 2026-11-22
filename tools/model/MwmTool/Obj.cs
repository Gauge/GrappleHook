using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using VRage.Import;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender.Import;

/// Writes an mwm and the subparts its dummies name (subpart_*, file=...) as one OBJ, each part
/// placed at its dummy, one group per model and one usemtl per material.
static class Obj
{
    public static void Export(string mwm, string objPath, float elevationDeg)
    {
        var sb = new StringBuilder();
        int baseIndex = 1;
        Write(mwm, Matrix.Identity, sb, ref baseIndex, 0, elevationDeg);
        File.WriteAllText(objPath, sb.ToString());
    }

    static Dictionary<string, object> Load(string path)
    {
        var imp = new MyModelImporter();
        imp.ImportData(path);
        var d = imp.GetTagData();
        if (!d.ContainsKey("Vertices") && d.TryGetValue("GeometryDataAsset", out var g))
        {
            string geo = Path.Combine(Path.GetDirectoryName(path), Path.GetFileName(((string)g).Replace('\\', '/')) + ".mwm");
            var imp2 = new MyModelImporter();
            imp2.ImportData(geo);
            foreach (var kv in imp2.GetTagData()) if (!d.ContainsKey(kv.Key)) d[kv.Key] = kv.Value;
        }
        return d;
    }

    static void Write(string path, Matrix world, StringBuilder sb, ref int baseIndex, int depth, float elevationDeg)
    {
        var d = Load(path);
        var verts = (HalfVector4[])d["Vertices"];
        string name = Path.GetFileNameWithoutExtension(path);
        sb.AppendLine($"o {name}");
        foreach (var hv in verts)
        {
            var h = hv;
            Vector3 p = Vector3.Transform(PositionPacker.UnpackPosition(ref h), world);
            sb.AppendLine($"v {p.X:R} {p.Y:R} {p.Z:R}");
        }
        foreach (var part in (List<MyMeshPartInfo>)d["MeshParts"])
        {
            sb.AppendLine($"usemtl {part.m_MaterialDesc?.MaterialName ?? "none"}");
            var ix = part.m_indices;
            for (int i = 0; i + 2 < ix.Count; i += 3)
                sb.AppendLine($"f {ix[i] + baseIndex} {ix[i + 1] + baseIndex} {ix[i + 2] + baseIndex}");
        }
        baseIndex += verts.Length;
        foreach (var kv in (Dictionary<string, MyModelDummy>)d["Dummies"])
        {
            if (!kv.Key.StartsWith("subpart_") || !kv.Value.CustomData.TryGetValue("file", out var file)) continue;
            Matrix m = Matrix.Normalize(kv.Value.Matrix);
            // tilt the barrels (the second level) so the render shows elevation
            if (depth == 1) m = Matrix.CreateRotationX(MathHelper.ToRadians(elevationDeg)) * m;
            string sub = Path.Combine(Path.GetDirectoryName(path), (string)file + ".mwm");
            Write(sub, m * world, sb, ref baseIndex, depth + 1, elevationDeg);
        }
    }
}
