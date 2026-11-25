using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using VRageRender.Import;

static class Program
{
    static int Main(string[] args)
    {
        // the game's file system reads relative paths from its own Content folder
        args = args.Select((a, i) => i > 0 && (a.EndsWith(".mwm") || a.EndsWith(".json") || a.EndsWith(".obj")) ? System.IO.Path.GetFullPath(a) : a).ToArray();
        // the game's Content folder sits beside the Bin64 its DLLs are loaded from
        string bin = System.IO.Path.GetDirectoryName(typeof(MyModelImporter).Assembly.Location);
        VRage.FileSystem.MyFileSystem.Init(System.IO.Path.GetFullPath(System.IO.Path.Combine(bin, "..", "Content")), System.IO.Path.GetTempPath());
        if (args[0] == "dump")
        {
            var imp = new MyModelImporter();
            imp.ImportData(args[1]);
            Console.WriteLine($"version {imp.DataVersion}");
            foreach (var kv in imp.GetTagData())
            {
                object v = kv.Value;
                string desc = v == null ? "null" : v is Array a ? $"{v.GetType().Name}[{a.Length}]" : v is ICollection c ? $"{v.GetType().Name} count {c.Count}" : $"{v}";
                Console.WriteLine($"{kv.Key}: {desc}");
            }
            var data = imp.GetTagData();
            if (data.TryGetValue("Dummies", out var d))
                foreach (var kv in (Dictionary<string, MyModelDummy>)d)
                {
                    Console.WriteLine($"  dummy {kv.Key} t={kv.Value.Matrix.Translation} f={kv.Value.Matrix.Forward} up={kv.Value.Matrix.Up} scale=({kv.Value.Matrix.Right.Length():F3},{kv.Value.Matrix.Up.Length():F3},{kv.Value.Matrix.Forward.Length():F3})");
                    foreach (var cp in kv.Value.CustomData) Console.WriteLine($"     {cp.Key}={cp.Value}");
                }
            if (data.TryGetValue("MeshParts", out var mp))
                foreach (var p in (List<MyMeshPartInfo>)mp)
                {
                    var m = p.m_MaterialDesc;
                    Console.WriteLine($"  part tris={p.m_indices.Count/3} mat={m?.MaterialName} tech={m?.Technique} facing={m?.Facing}");
                    if (m != null) foreach (var t in m.Textures) Console.WriteLine($"     tex {t.Key}={t.Value}");
                }
            if (data.TryGetValue("LODs", out var l))
                foreach (var lod in (MyLODDescriptor[])l) Console.WriteLine($"  lod {lod.Distance} {lod.Model} {lod.RenderQuality}");
            if (data.ContainsKey("BoundingBox")) Console.WriteLine($"  bbox {data["BoundingBox"]}");
            var hk = data.TryGetValue("HavokCollisionGeometry", out var h) ? (byte[])h : null;
            Console.WriteLine($"  havok bytes {hk?.Length}");
            if (args.Length > 2 && hk != null) System.IO.File.WriteAllBytes(args[2], hk);
            return 0;
        }
        if (args[0] == "materials") { Build.DumpMaterials(args[1], args.Skip(2)); return 0; }
        if (args[0] == "build") { Build.Run(args[1], args[2], args[3]); return 0; }
        if (args[0] == "probe") { Probe.Run(args[1]); return 0; }
        if (args[0] == "obj") { Obj.Export(args[1], args[2], args.Length > 3 ? float.Parse(args[3]) : 0f, args.Length > 4 ? args[4] : null); return 0; }
        return 2;
    }
}
