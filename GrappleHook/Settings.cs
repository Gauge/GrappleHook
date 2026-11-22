using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Utils;

namespace GrappleHook
{
    /// <summary>
    /// The server's settings, in the world's storage as GrappleHook.cfg and synced to every
    /// client. The file is grouped by what each value affects and carries a comment on each
    /// one (see <see cref="Comments"/>). Every value has its default in its field initialiser,
    /// so a value missing from the file - deleted by hand, or added by a newer version - takes
    /// its default and the rest of the file is kept.
    /// </summary>
    [ProtoContract]
    [XmlRoot("GrappleHookSettings")]
    public class Settings
    {
        public static Settings Instance;

        public const string Filename = "GrappleHook.cfg";
        public const string BackupFilename = "GrappleHook.unreadable.cfg";

        /// <summary>Bumped when the file's layout changes. 12 introduced the grouped layout.</summary>
        public const double CurrentVersion = 12;

        [ProtoMember(1)]
        public double Version = CurrentVersion;

        [ProtoMember(2)]
        public RopeSettings Rope = new RopeSettings();

        [ProtoMember(3)]
        public WinchSettings Winch = new WinchSettings();

        [ProtoMember(4)]
        public HookSettings Hook = new HookSettings();

        [ProtoMember(5)]
        public ZiplineSettings Zipline = new ZiplineSettings();

        private static readonly object FileLock = new object();

        public static void Init()
        {
            lock (FileLock)
            {
                if (Instance == null)
                {
                    Instance = Load();
                }
            }
        }

        public static Settings GetDefaults()
        {
            return new Settings();
        }

        /// <summary>
        /// The grid size dependent values: a large grid's launcher has the stronger winch and
        /// rope.
        /// </summary>
        public double WinchForceFor(bool largeGrid)
        {
            return largeGrid ? Winch.ForceLargeGrid : Winch.ForceSmallGrid;
        }

        public double BreakingStrengthFor(bool largeGrid)
        {
            return largeGrid ? Rope.Breaking.StrengthLargeGrid : Rope.Breaking.StrengthSmallGrid;
        }

        /// <summary>
        /// Reads the file, keeps every value it has, fills in the rest with defaults, clamps
        /// anything out of range, and writes it back so it shows every setting with its comment.
        /// A file that cannot be read is kept as <see cref="BackupFilename"/> rather than lost.
        /// </summary>
        public static Settings Load()
        {
            Settings settings = null;
            string text = null;

            try
            {
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage(Filename, typeof(Settings)))
                {
                    MyLog.Default.Info("[GrappleHook] Loading saved settings");
                    TextReader reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(Filename, typeof(Settings));
                    text = reader.ReadToEnd();
                    reader.Close();

                    settings = MyAPIGateway.Utilities.SerializeFromXML<Settings>(text);
                }
                else
                {
                    MyLog.Default.Info("[GrappleHook] Config file not found. Writing the defaults");
                }
            }
            catch (Exception e)
            {
                settings = null;
                MyLog.Default.Info($"[GrappleHook] Could not read {Filename}; it is kept as {BackupFilename} and the defaults are used.\n{e.Message}");
                WriteText(BackupFilename, text);
            }

            if (settings == null)
            {
                settings = GetDefaults();
            }
            else if (settings.Version != CurrentVersion)
            {
                MyLog.Default.Info($"[GrappleHook] Updating {Filename} from version {settings.Version} to {CurrentVersion}; values with the same name are kept");
            }

            settings.Version = CurrentVersion;
            settings.Validate();
            Save(settings);

            return settings;
        }

        /// <summary>
        /// Clamps every value into a range the mod can work with, so a typo in the file cannot
        /// break the physics. Logs each value it changes.
        /// </summary>
        public void Validate()
        {
            if (Rope == null) Rope = new RopeSettings();
            if (Rope.Breaking == null) Rope.Breaking = new BreakingSettings();
            if (Rope.Wrapping == null) Rope.Wrapping = new WrappingSettings();
            if (Winch == null) Winch = new WinchSettings();
            if (Hook == null) Hook = new HookSettings();
            if (Zipline == null) Zipline = new ZiplineSettings();

            Rope.SpringFrequency = Clamp("Rope.SpringFrequency", Rope.SpringFrequency, 0.1f, 4f);
            Rope.DampingRatio = Clamp("Rope.DampingRatio", Rope.DampingRatio, 0f, 2f);
            Rope.MinLength = Clamp("Rope.MinLength", Rope.MinLength, 1f, 100000f);
            Rope.MaxLength = Clamp("Rope.MaxLength", Rope.MaxLength, Rope.MinLength, 100000f);
            Rope.DrawPoints = (int)Clamp("Rope.DrawPoints", Rope.DrawPoints, 2, 100);
            Rope.Breaking.StrengthLargeGrid = Clamp("Rope.Breaking.StrengthLargeGrid", Rope.Breaking.StrengthLargeGrid, 1d, double.MaxValue);
            Rope.Breaking.StrengthSmallGrid = Clamp("Rope.Breaking.StrengthSmallGrid", Rope.Breaking.StrengthSmallGrid, 1d, double.MaxValue);
            Rope.Wrapping.MaxWraps = (int)Clamp("Rope.Wrapping.MaxWraps", Rope.Wrapping.MaxWraps, 1, 32);

            Winch.TightenSpeed = Clamp("Winch.TightenSpeed", Winch.TightenSpeed, 0f, 1000f);
            Winch.LoosenSpeed = Clamp("Winch.LoosenSpeed", Winch.LoosenSpeed, 0f, 1000f);
            Winch.ForceLargeGrid = Clamp("Winch.ForceLargeGrid", Winch.ForceLargeGrid, 0d, double.MaxValue);
            Winch.ForceSmallGrid = Clamp("Winch.ForceSmallGrid", Winch.ForceSmallGrid, 0d, double.MaxValue);

            Hook.Speed = Clamp("Hook.Speed", Hook.Speed, 1f, 3000f);
            Hook.Range = Clamp("Hook.Range", Hook.Range, 1f, 100000f);
            Hook.RetractSpeed = Clamp("Hook.RetractSpeed", Hook.RetractSpeed, 1f, 3000f);

            Zipline.TetherLength = Clamp("Zipline.TetherLength", Zipline.TetherLength, 0f, 100f);
            Zipline.TetherForce = Clamp("Zipline.TetherForce", Zipline.TetherForce, 0f, float.MaxValue);
            Zipline.TetherDamping = Clamp("Zipline.TetherDamping", Zipline.TetherDamping, 0f, float.MaxValue);
            Zipline.MinSpeed = Clamp("Zipline.MinSpeed", Zipline.MinSpeed, 0.1f, 1000f);
        }

        private static float Clamp(string name, float value, float min, float max)
        {
            float clamped = float.IsNaN(value) ? min : Math.Max(min, Math.Min(max, value));
            if (clamped != value)
            {
                MyLog.Default.Info($"[GrappleHook] Setting {name} = {value} is out of range ({min} to {max}); using {clamped}");
            }
            return clamped;
        }

        private static double Clamp(string name, double value, double min, double max)
        {
            double clamped = double.IsNaN(value) ? min : Math.Max(min, Math.Min(max, value));
            if (clamped != value)
            {
                MyLog.Default.Info($"[GrappleHook] Setting {name} = {value} is out of range ({min} to {max}); using {clamped}");
            }
            return clamped;
        }

        public static void Save(Settings settings)
        {
            WriteText(Filename, Annotate(MyAPIGateway.Utilities.SerializeToXML(settings)));
        }

        private static void WriteText(string filename, string text)
        {
            if (text == null) return;

            lock (FileLock)
            {
                try
                {
                    MyLog.Default.Info($"[GrappleHook] Saving {filename}");
                    TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(filename, typeof(Settings));
                    writer.Write(text);
                    writer.Close();
                }
                catch (Exception e)
                {
                    MyLog.Default.Info($"[GrappleHook] Failed to save {filename}\n{e}");
                }
            }
        }

        private const string Header =
            "GrappleHook settings, read by the server when the world loads. Edit them with the world closed.\n" +
            "     Delete a value to get its default back. Values out of range are clamped, and the game log says so.\n" +
            "     Forces are in newtons (1 MN = 1000000), lengths in metres, speeds in metres per second.";

        /// <summary>The comment written above each value, by its path in the file.</summary>
        public static readonly Dictionary<string, string> Comments = new Dictionary<string, string>
        {
            { "Version", "Layout version of this file. Leave it alone." },

            { "Rope", "How the rope behaves once the hook has caught on something." },
            { "Rope/SpringFrequency", "How stiff the rope is, as how many times a second a stretched rope springs back (0.1 to 4). The pull scales with the mass on its ends, so it feels the same on any grid. Higher is stiffer; above about 2 heavy grids can start to shake." },
            { "Rope/DampingRatio", "How quickly the rope stops bouncing: 0 bounces forever, 1 settles without bouncing, 0.7 gives one small bounce." },
            { "Rope/MinLength", "The shortest the winch can reel the rope in to." },
            { "Rope/MaxLength", "The longest the winch can pay the rope out to." },
            { "Rope/DrawPoints", "How many points the rope is drawn with. More is smoother and costs a little more to draw." },
            { "Rope/Breaking", "Whether the rope snaps when pulled too hard. A snapped rope loses its hook." },
            { "Rope/Breaking/Enabled", "true or false." },
            { "Rope/Breaking/StrengthLargeGrid", "Tension that snaps the rope of a launcher on a large grid. The default holds 65% of workshop large ships at full thrust; the rest can break free." },
            { "Rope/Breaking/StrengthSmallGrid", "Tension that snaps the rope of a launcher on a small grid. The default holds 65% of workshop small ships at full thrust." },
            { "Rope/Wrapping", "Whether the rope catches on and bends around things between the launcher and the hook." },
            { "Rope/Wrapping/Enabled", "true or false." },
            { "Rope/Wrapping/MaxWraps", "The most edges one rope can bend around at once." },

            { "Winch", "The winch on the launcher that reels the rope in and pays it out." },
            { "Winch/TightenSpeed", "Fastest reel-in speed." },
            { "Winch/LoosenSpeed", "Fastest pay-out speed." },
            { "Winch/ForceLargeGrid", "Most a launcher on a large grid can pull with while reeling in. Past it the winch stalls; its brake still holds any load. The default lifts 40% of workshop large ships against 1 g; in space it moves anything, heavier ships slower." },
            { "Winch/ForceSmallGrid", "Most a launcher on a small grid can pull with while reeling in. The default lifts 40% of workshop small ships (up to 38 t) against 1 g." },

            { "Hook", "The hook in flight." },
            { "Hook/Speed", "Launch speed, on top of the launcher's own speed." },
            { "Hook/Range", "How much rope the launcher holds: how far the hook flies before it gives up and reels back." },
            { "Hook/Gravity", "true: the hook falls under gravity in flight, so it has to be aimed above far targets. false: it flies straight." },
            { "Hook/RetractSpeed", "How fast the hook is reeled back to the launcher after a detach or a miss." },

            { "Zipline", "Riding the rope (F while looking at it)." },
            { "Zipline/TetherLength", "How far below the rope the rider hangs." },
            { "Zipline/TetherForce", "How hard the rider is held to the rope, per metre they are pulled away from it." },
            { "Zipline/TetherDamping", "How quickly the rider stops swinging under the rope." },
            { "Zipline/MinSpeed", "The slowest a rider travels, so they always reach the end. Downhill, gravity speeds them up." },
        };

        /// <summary>
        /// Writes <see cref="Comments"/> into the serialised file above the values they describe.
        /// The serialiser cannot write comments, and the reader skips them.
        /// </summary>
        public static string Annotate(string xml)
        {
            StringBuilder sb = new StringBuilder(xml.Length * 3);
            List<string> path = new List<string>();
            bool headerWritten = false;

            foreach (string raw in xml.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                string trimmed = line.TrimStart();

                if (trimmed.Length == 0 || trimmed.StartsWith("<?") || trimmed.StartsWith("<!--"))
                {
                    sb.Append(line).Append('\n');
                    continue;
                }

                if (trimmed.StartsWith("</"))
                {
                    if (path.Count > 0) path.RemoveAt(path.Count - 1);
                    sb.Append(line).Append('\n');
                    continue;
                }

                if (!headerWritten)
                {
                    sb.Append("<!-- ").Append(Header).Append(" -->\n");
                    headerWritten = true;
                }

                int end = trimmed.IndexOfAny(new char[] { ' ', '>', '/' }, 1);
                string name = end > 1 ? trimmed.Substring(1, end - 1) : trimmed.Substring(1);
                bool leaf = trimmed.Contains("</") || trimmed.EndsWith("/>");

                // the root element is not part of a value's path
                if (path.Count > 0)
                {
                    string key = path.Count > 1 ? string.Join("/", path.GetRange(1, path.Count - 1)) + "/" + name : name;
                    string comment;
                    if (Comments.TryGetValue(key, out comment))
                    {
                        string indent = line.Substring(0, line.Length - trimmed.Length);
                        if (!leaf) sb.Append('\n');
                        sb.Append(indent).Append("<!-- ").Append(comment).Append(" -->\n");
                    }
                }

                sb.Append(line).Append('\n');
                if (!leaf) path.Add(name);
            }

            return sb.ToString();
        }
    }

    [ProtoContract]
    public class RopeSettings
    {
        [ProtoMember(1)] public float SpringFrequency = 1.5f;
        [ProtoMember(2)] public float DampingRatio = 0.7f;
        [ProtoMember(3)] public float MinLength = 3f;
        [ProtoMember(4)] public float MaxLength = 1000f;
        [ProtoMember(5)] public int DrawPoints = 20;
        [ProtoMember(6)] public BreakingSettings Breaking = new BreakingSettings();
        [ProtoMember(7)] public WrappingSettings Wrapping = new WrappingSettings();
    }

    [ProtoContract]
    public class BreakingSettings
    {
        [ProtoMember(1)] public bool Enabled = false;
        [ProtoMember(2)] public double StrengthLargeGrid = 113000000d;
        [ProtoMember(3)] public double StrengthSmallGrid = 4300000d;
    }

    [ProtoContract]
    public class WrappingSettings
    {
        [ProtoMember(1)] public bool Enabled = false;
        [ProtoMember(2)] public int MaxWraps = 8;
    }

    [ProtoContract]
    public class WinchSettings
    {
        [ProtoMember(1)] public float TightenSpeed = 3f;
        [ProtoMember(2)] public float LoosenSpeed = 10f;
        [ProtoMember(3)] public double ForceLargeGrid = 11500000d;
        [ProtoMember(4)] public double ForceSmallGrid = 370000d;
    }

    [ProtoContract]
    public class HookSettings
    {
        [ProtoMember(1)] public float Speed = 90f;
        [ProtoMember(2)] public float Range = 300f;
        [ProtoMember(3)] public bool Gravity = false;
        [ProtoMember(4)] public float RetractSpeed = 40f;
    }

    [ProtoContract]
    public class ZiplineSettings
    {
        [ProtoMember(1)] public float TetherLength = 0f;
        [ProtoMember(2)] public float TetherForce = 2600f;
        [ProtoMember(3)] public float TetherDamping = 140f;
        [ProtoMember(4)] public float MinSpeed = 5f;
    }
}
