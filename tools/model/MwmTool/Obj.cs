using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using VRage.Import;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender.Import;

/// Writes an mwm and the subparts its dummies name (subpart_*, file=...) as one OBJ, each part
/// placed at its dummy, one group per model and one usemtl per material, with UVs and normals.
/// Triangles are written counter-clockwise, as OBJ and Blender expect (the game's are clockwise).
///
/// Decals (DECAL techniques) lie on the surface they decorate; the game keeps them in front with a
/// depth bias. Here each decal triangle gets its own vertices lifted DecalLift along its normal, so
/// a renderer shows them without flicker.
///
/// With a hook model, the hook is placed where the game rests it: its tail at the muzzle and its
/// centre HookRest ahead. The muzzle is the muzzle_projectile dummy (the grapple ammo is a
/// projectile type), or for a turret's barrels without one, the barrels' own frame.
static class Obj
{
    const float DecalLift = 0.002f;
    const float HookRest = 1.1f;

    public static void Export(string mwm, string objPath, float elevationDeg, string hook = null)
    {
        var sb = new StringBuilder();
        var counts = new int[2]; // vertices, normals+uvs written so far
        Write(mwm, Matrix.Identity, sb, counts, 0, elevationDeg, hook);
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

    static void Write(string path, Matrix world, StringBuilder sb, int[] counts, int depth, float elevationDeg, string hook)
    {
        var d = Load(path);
        var verts = (HalfVector4[])d["Vertices"];
        var normals = d.TryGetValue("Normals", out var n) ? (Byte4[])n : null;
        var uvs = d.TryGetValue("TexCoords0", out var t) ? (HalfVector2[])t : null;
        string name = Path.GetFileNameWithoutExtension(path);
        Matrix rotation = Matrix.Normalize(world);
        rotation.Translation = Vector3.Zero;

        var p = new Vector3[verts.Length];
        var nn = new Vector3[verts.Length];
        var uv = new Vector2[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            var h = verts[i];
            p[i] = Vector3.Transform(PositionPacker.UnpackPosition(ref h), world);
            nn[i] = normals != null && i < normals.Length ? Vector3.Normalize(Vector3.TransformNormal(VF_Packer.UnpackNormal(normals[i]), rotation)) : Vector3.Up;
            uv[i] = uvs != null && i < uvs.Length ? uvs[i].ToVector2() : Vector2.Zero;
        }

        sb.AppendLine($"o {name}");
        int vBase = counts[0] + 1, aBase = counts[1] + 1;
        for (int i = 0; i < verts.Length; i++)
        {
            sb.AppendLine($"v {p[i].X:R} {p[i].Y:R} {p[i].Z:R}");
            sb.AppendLine($"vt {uv[i].X:R} {1f - uv[i].Y:R}");
            sb.AppendLine($"vn {nn[i].X:R} {nn[i].Y:R} {nn[i].Z:R}");
        }
        counts[0] += verts.Length;
        counts[1] += verts.Length;

        foreach (var part in (List<MyMeshPartInfo>)d["MeshParts"])
        {
            var m = part.m_MaterialDesc;
            sb.AppendLine($"usemtl {m?.MaterialName ?? "none"}");
            var ix = part.m_indices;
            bool decal = m != null && m.Technique != null && m.Technique.StartsWith("DECAL");
            for (int i = 0; i + 2 < ix.Count; i += 3)
            {
                int a = ix[i], b = ix[i + 1], c = ix[i + 2];
                if (!decal)
                {
                    sb.AppendLine($"f {F(a + vBase, a + aBase)} {F(c + vBase, c + aBase)} {F(b + vBase, b + aBase)}");
                    continue;
                }
                // the decal's own copy of the triangle, lifted off the surface
                int first = counts[0] + 1;
                foreach (int k in new[] { a, c, b })
                {
                    Vector3 lifted = p[k] + nn[k] * DecalLift;
                    sb.AppendLine($"v {lifted.X:R} {lifted.Y:R} {lifted.Z:R}");
                    sb.AppendLine($"vt {uv[k].X:R} {1f - uv[k].Y:R}");
                    sb.AppendLine($"vn {nn[k].X:R} {nn[k].Y:R} {nn[k].Z:R}");
                }
                counts[0] += 3;
                counts[1] += 3;
                sb.AppendLine($"f {F(first, first)} {F(first + 1, first + 1)} {F(first + 2, first + 2)}");
            }
        }

        var dummies = (Dictionary<string, MyModelDummy>)d["Dummies"];
        bool hooked = false;
        foreach (var kv in dummies)
        {
            if (hook != null && kv.Key.StartsWith("muzzle_projectile") && !hooked)
            {
                PlaceHook(hook, Matrix.Normalize(kv.Value.Matrix) * world, sb, counts);
                hooked = true;
            }
            if (!kv.Key.StartsWith("subpart_") || !kv.Value.CustomData.TryGetValue("file", out var file)) continue;
            Matrix sub = Matrix.Normalize(kv.Value.Matrix);
            // tilt the barrels (the second level) so the render shows elevation
            if (depth == 1) sub = Matrix.CreateRotationX(MathHelper.ToRadians(elevationDeg)) * sub;
            string subPath = Path.Combine(Path.GetDirectoryName(path), (string)file + ".mwm");
            Write(subPath, sub * world, sb, counts, depth + 1, elevationDeg, hook);
        }

        // a turret's barrels with no projectile muzzle: the game uses the barrels' frame
        if (hook != null && !hooked && depth == 2) PlaceHook(hook, world, sb, counts);
    }

    static void PlaceHook(string hook, Matrix muzzle, StringBuilder sb, int[] counts)
    {
        Matrix at = muzzle;
        at.Translation += Vector3.Normalize(muzzle.Forward) * HookRest;
        Write(hook, at, sb, counts, 99, 0f, null);
    }

    static string F(int v, int a) => $"{v}/{a}/{a}";
}
