using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Import;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender.Import;

/// Reads conventions out of a real model: which triangle winding faces outward, how many UV
/// units the tiling materials get per metre, and each material's descriptor fields.
static class Probe
{
    public static void Run(string path)
    {
        var imp = new MyModelImporter();
        imp.ImportData(path);
        var d = imp.GetTagData();
        var v = (HalfVector4[])d["Vertices"];
        var n = (Byte4[])d["Normals"];
        var uv = (HalfVector2[])d["TexCoords0"];
        Vector3 P(int i) { var h = v[i]; return PositionPacker.UnpackPosition(ref h); }
        foreach (var part in (List<MyMeshPartInfo>)d["MeshParts"])
        {
            int agree = 0, disagree = 0; double area = 0, uvArea = 0;
            var ix = part.m_indices;
            for (int t = 0; t + 2 < ix.Count; t += 3)
            {
                Vector3 a = P(ix[t]), b = P(ix[t + 1]), c = P(ix[t + 2]);
                Vector3 g = Vector3.Cross(b - a, c - a);
                Vector3 vn = VF_Packer.UnpackNormal(n[ix[t]]) + VF_Packer.UnpackNormal(n[ix[t + 1]]) + VF_Packer.UnpackNormal(n[ix[t + 2]]);
                if (g.Length() < 1e-9f) continue;
                if (Vector3.Dot(g, vn) > 0) agree++; else disagree++;
                area += g.Length() / 2;
                Vector2 ua = uv[ix[t]].ToVector2(), ub = uv[ix[t + 1]].ToVector2(), uc = uv[ix[t + 2]].ToVector2();
                uvArea += Math.Abs((ub.X - ua.X) * (uc.Y - ua.Y) - (uc.X - ua.X) * (ub.Y - ua.Y)) / 2;
            }
            var m = part.m_MaterialDesc;
            Console.WriteLine($"{m?.MaterialName,-28} hash={part.m_MaterialHash} nameHash={m?.MaterialName.GetHashCode()} tech={m?.Technique} ccwOut={agree} cwOut={disagree} uvPerMetre={Math.Sqrt(uvArea / Math.Max(area, 1e-9)):F3} glass=({m?.GlassCW},{m?.GlassCCW},{m?.GlassSmoothNormals}) user=[{string.Join(",", m?.UserData.Select(kv => kv.Key + "=" + kv.Value) ?? new string[0])}]");
        }
        foreach (var k in new[] { "RescaleFactor", "UseChannelTextures", "SwapWindingOrder", "PatternScale" }) Console.WriteLine($"{k}={d[k]}");
        var bones = (MyModelBone[])d["Bones"];
        foreach (var b in bones) Console.WriteLine($"bone {b.Name} parent={b.Parent}");
    }
}
