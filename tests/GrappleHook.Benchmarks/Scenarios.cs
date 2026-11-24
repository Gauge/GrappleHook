using System;
using System.Collections.Generic;
using VRageMath;

namespace GrappleHook.Benchmarks
{
    /// <summary>
    /// One measured piece of per-frame work. <see cref="Setup"/> brings a fresh rope to the
    /// state the scenario starts from; <see cref="Op"/> is one frame (or one call) of the work,
    /// and returns a number that depends on its result, so nothing is optimised away and the
    /// baseline and current code can be compared.
    /// </summary>
    public sealed class Scenario
    {
        public string Name;
        public string Description;

        /// <summary>True when the op rebuilds the rope's path, so verification compares every point.</summary>
        public bool BuildsPath;

        public Action<IRope, Rig> Setup = (rope, rig) => { };
        public Func<IRope, Rig, int, double> Op;
    }

    /// <summary>
    /// The moving ends and wrap points of one rope. Positions are kilometres from the world
    /// origin, as on a planet's surface, so the doubles carry the precision they do in game.
    /// </summary>
    public sealed class Rig
    {
        public static readonly Vector3D Origin = new Vector3D(52000, 31000, -18000);
        public static readonly Vector3D Up = Vector3D.Up;
        public static readonly Vector3D Gravity = Up * -9.81d;

        public readonly List<Vector3D> Anchors = new List<Vector3D>();
        public readonly Vector3D[] Wraps;
        public readonly Vector3D Hook;
        public readonly Vector3D Velocity;
        public readonly double Slack;
        public readonly double JumpEvery;

        public Rig(double span, int wraps, double slack, Vector3D velocity, double jump = 0)
        {
            Slack = slack;
            Velocity = velocity;
            JumpEvery = jump;
            Hook = Origin + new Vector3D(span, -span * 0.2d, span * 0.1d);
            Wraps = new Vector3D[wraps];

            // corners zigzag above the straight line, the way a rope pulled over a ridge of blocks bends
            for (int i = 0; i < wraps; i++)
            {
                double t = (i + 1d) / (wraps + 1d);
                Wraps[i] = Vector3D.Lerp(Origin, Hook, t) + Up * (2d + 1.5d * (i % 2));
            }
        }

        /// <summary>The anchors at a frame: the launcher bobs on its ship, the rest move with the ship's velocity.</summary>
        public List<Vector3D> At(int frame)
        {
            double t = frame / 60d;
            Vector3D drift = Velocity * t;
            if (JumpEvery > 0) drift += Vector3D.Right * (JumpEvery * (frame % 2));
            Vector3D bob = Up * (0.5d * Math.Sin(t * 2d * Math.PI * 0.3d)) + Vector3D.Forward * (0.3d * Math.Cos(t * 2d * Math.PI * 0.17d));

            Anchors.Clear();
            Anchors.Add(Origin + drift + bob);
            for (int i = 0; i < Wraps.Length; i++) Anchors.Add(Wraps[i] + drift);
            Anchors.Add(Hook + drift);
            return Anchors;
        }

        public double RopeLength(List<Vector3D> anchors)
        {
            double length = 0;
            for (int i = 1; i < anchors.Count; i++) length += Vector3D.Distance(anchors[i - 1], anchors[i]);
            return length * (1d + Slack);
        }
    }

    public static class Scenarios
    {
        private static readonly Vector3D FallbackDown = Vector3D.Down;

        /// <summary>The rigs, built fresh for each run so no state carries over.</summary>
        public static Rig RigFor(string name)
        {
            switch (name)
            {
                case "client/taut": return new Rig(40, 0, -0.02d, Vector3D.Zero);
                case "client/zero-g-tow": return new Rig(40, 0, 0.05d, new Vector3D(80, 0, 60));
                case "client/wrapped-8":
                case "dedicated/wrapped-8": return new Rig(80, 8, 0.05d, Vector3D.Zero);
                case "client/restart": return new Rig(40, 0, 0.10d, Vector3D.Zero, 60);
                default: return new Rig(40, 0, 0.10d, Vector3D.Zero);
            }
        }

        private static double Client(IRope rope, Rig rig, int frame, Vector3D gravity, double air, int points)
        {
            List<Vector3D> anchors = rig.At(frame);
            rope.StepChains(anchors, rig.RopeLength(anchors), gravity, air * ModConstants.ChainAirDrag, FallbackDown, points);
            return rope.Length;
        }

        private static double Dedicated(IRope rope, Rig rig, int frame, int points)
        {
            List<Vector3D> anchors = rig.At(frame);
            rope.BuildCatenary(anchors, rig.RopeLength(anchors), Rig.Gravity, points);
            return rope.Length;
        }

        /// <summary>A rope hanging still for a few seconds, as the ray and zipline scenarios find it.</summary>
        private static Action<IRope, Rig> Settled(int points)
        {
            return (rope, rig) =>
            {
                for (int f = 0; f < 300; f++) Client(rope, rig, 0, Rig.Gravity, 1, points);
            };
        }

        /// <summary>
        /// The player looking along the rope from beside it, just missing it: a miss is the
        /// worst case, every segment is tested.
        /// </summary>
        private static double LookPast(IRope rope, int frame)
        {
            Vector3D eye = Rig.Origin + new Vector3D(-2, 12, 6) + Vector3D.Right * (0.001d * (frame % 100));
            Vector3D target = Rig.Origin + new Vector3D(40, 4, 6);
            RayD ray = new RayD(eye, Vector3D.Normalize(target - eye));
            return rope.RayHits(ref ray, ModConstants.InteractRange) ? 1 : 0;
        }

        public static readonly Scenario[] All =
        {
            new Scenario
            {
                Name = "client/single", BuildsPath = true,
                Description = "One frame of a slack rope in 1 g air: 40 m, 10% slack, 20 points (the default), launcher bobbing.",
                Op = (rope, rig, f) => Client(rope, rig, f, Rig.Gravity, 1, 20),
            },
            new Scenario
            {
                Name = "client/taut", BuildsPath = true,
                Description = "One frame of a rope pulled tight: drawn straight.",
                Op = (rope, rig, f) => Client(rope, rig, f, Rig.Gravity, 1, 20),
            },
            new Scenario
            {
                Name = "client/zero-g-tow", BuildsPath = true,
                Description = "One frame of a slack rope in space, both ends travelling at 100 m/s.",
                Op = (rope, rig, f) => Client(rope, rig, f, Vector3D.Zero, 0, 20),
            },
            new Scenario
            {
                Name = "client/wrapped-8", BuildsPath = true,
                Description = "One frame of a rope bent over 8 edges (Rope.Wrapping.MaxWraps): 9 spans sharing 20 points.",
                Op = (rope, rig, f) => Client(rope, rig, f, Rig.Gravity, 1, 20),
            },
            new Scenario
            {
                Name = "client/100-points", BuildsPath = true,
                Description = "As client/single at Rope.DrawPoints = 100, the maximum.",
                Op = (rope, rig, f) => Client(rope, rig, f, Rig.Gravity, 1, 100),
            },
            new Scenario
            {
                Name = "client/restart", BuildsPath = true,
                Description = "A chain started from its hanging shape (the rope coming into view, or its ends jumping): catenary solve plus reset.",
                Op = (rope, rig, f) => Client(rope, rig, f, Rig.Gravity, 1, 20),
            },
            new Scenario
            {
                Name = "dedicated/single", BuildsPath = true,
                Description = "One frame of the exact catenary a dedicated server builds while someone rides: 20 points.",
                Op = (rope, rig, f) => Dedicated(rope, rig, f, 20),
            },
            new Scenario
            {
                Name = "dedicated/wrapped-8", BuildsPath = true,
                Description = "The dedicated server's catenary over 8 wrap points: 9 spans.",
                Op = (rope, rig, f) => Dedicated(rope, rig, f, 20),
            },
            new Scenario
            {
                Name = "interact/ray-20",
                Description = "The per-frame check whether the player is looking at the rope (zipline grab), 20 points, a miss.",
                Setup = Settled(20),
                Op = (rope, rig, f) => LookPast(rope, f),
            },
            new Scenario
            {
                Name = "interact/ray-100",
                Description = "As interact/ray-20 on 100 points.",
                Setup = Settled(100),
                Op = (rope, rig, f) => LookPast(rope, f),
            },
            new Scenario
            {
                Name = "interact/ray-sweep",
                Description = "The player standing 1-5 m from the rope sweeping their view across it: hits and misses mixed, 20 points.",
                Setup = Settled(20),
                Op = (rope, rig, f) =>
                {
                    Vector3D onRope = rope.Points[(f / 7) % rope.Count];
                    Vector3D eye = onRope + new Vector3D(0.5d, 1d + 4d * ((f % 13) / 13d), 1d);
                    Vector3D look = onRope + Vector3D.Right * (2d * Math.Sin(f * 0.37d)) - eye;
                    RayD ray = new RayD(eye, Vector3D.Normalize(look));
                    return rope.RayHits(ref ray, ModConstants.InteractRange) ? 1 : 0;
                },
            },
            new Scenario
            {
                Name = "zipline/4-riders",
                Description = "Each rider's pulley found on the rope every frame: 4 riders on 20 points.",
                Setup = Settled(20),
                Op = (rope, rig, f) =>
                {
                    double sum = 0;
                    for (int r = 0; r < 4; r++)
                    {
                        double distance = (rope.Length * (0.2d * r + 0.001d * (f % 200))) % rope.Length;
                        Vector3D tangent;
                        Vector3D point = rope.PointAt(distance, out tangent);
                        sum += point.X + tangent.Y;
                    }
                    return sum;
                },
            },
            new Scenario
            {
                Name = "zipline/clip-on",
                Description = "Finding where a rider clips on (closest point on the rope), 100 points.",
                Setup = Settled(100),
                Op = (rope, rig, f) => rope.ClosestDistance(Rig.Origin + new Vector3D(10 + 0.1d * (f % 200), -5, 3)),
            },
        };
    }
}
