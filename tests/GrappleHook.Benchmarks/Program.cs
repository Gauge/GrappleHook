using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace GrappleHook.Benchmarks
{
    /// <summary>
    /// Times each scenario on the baseline and the current Core, after checking the two produce
    /// the same rope. Single threaded, a few seconds in all.
    ///
    ///   dotnet run -c Release --project tests/GrappleHook.Benchmarks -- [options]
    ///     --filter text   only scenarios whose name contains text
    ///     --samples n     timed samples per scenario and side (default 15)
    ///     --quick         5 samples, shorter warm up
    ///     --verify-only   check equivalence, time nothing
    ///     --out file      also write the table to a Markdown file
    /// </summary>
    public static class Program
    {
        /// <summary>Frames each scenario is run in lockstep on both sides when verifying.</summary>
        private const int VerifyFrames = 900;

        /// <summary>Largest difference in any point allowed between baseline and current, metres.</summary>
        private const double PointTolerance = 1e-6;

        private const double ResultTolerance = 1e-6;

        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            string filter = null;
            string outFile = null;
            int samples = 15;
            double warmSeconds = 0.15d;
            double sampleSeconds = 0.02d;
            bool verifyOnly = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--filter": filter = args[++i]; break;
                    case "--samples": samples = int.Parse(args[++i]); break;
                    case "--quick": samples = 5; warmSeconds = 0.05d; break;
                    case "--verify-only": verifyOnly = true; break;
                    case "--out": outFile = args[++i]; break;
                    default:
                        Console.Error.WriteLine("Usage: GrappleHook.Benchmarks [--filter text] [--samples n] [--quick] [--verify-only] [--out file]");
                        return 2;
                }
            }

            Scenario[] scenarios = Scenarios.All.Where(s => filter == null || s.Name.Contains(filter)).ToArray();
            if (scenarios.Length == 0)
            {
                Console.Error.WriteLine("No scenario matches " + filter);
                return 2;
            }

            bool verified = true;
            Console.WriteLine("Verifying current against baseline, " + VerifyFrames + " frames each");
            foreach (Scenario s in scenarios)
            {
                double worst;
                string problem = Verify(s, out worst);
                Console.WriteLine("  " + (problem == null ? "ok  " : "FAIL") + " " + s.Name.PadRight(22) + (problem ?? (s.BuildsPath ? $"largest point difference {worst:e1} m" : "same results")));
                verified &= problem == null;
            }

            if (!verified) return 1;
            if (verifyOnly) return 0;

            Console.WriteLine();
            Console.WriteLine($".NET {Environment.Version}, tiered compilation {(AppContext.TryGetSwitch("System.Runtime.TieredCompilation", out bool tc) ? tc.ToString() : "default")}, {samples} samples");

            var table = new StringBuilder();
            table.AppendLine("| Scenario | Baseline ns | Current ns | Speedup | Current B/op |");
            table.AppendLine("| --- | ---: | ---: | ---: | ---: |");

            foreach (Scenario s in scenarios)
            {
                Result baseline = Measure(s, new BaselineRope(), samples, warmSeconds, sampleSeconds);
                Result current = Measure(s, new CurrentRope(), samples, warmSeconds, sampleSeconds);

                table.AppendLine(string.Format(CultureInfo.InvariantCulture, "| {0} | {1:n0} | {2:n0} | {3:n2}x | {4:n0} |",
                    s.Name, baseline.Median, current.Median, baseline.Median / current.Median, current.BytesPerOp));
            }

            Console.WriteLine();
            Console.Write(table);

            if (outFile != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile)));
                var md = new StringBuilder();
                md.AppendLine($"Measured {DateTime.Now:yyyy-MM-dd HH:mm}, .NET {Environment.Version}, {samples} samples, median ns per op.");
                md.AppendLine();
                md.Append(table);
                md.AppendLine();
                foreach (Scenario s in scenarios) md.AppendLine($"* `{s.Name}`: {s.Description}");
                File.WriteAllText(outFile, md.ToString());
            }

            return 0;
        }

        /// <summary>Where every op's result ends up, so the JIT cannot drop the work.</summary>
        public static double Sink;

        private struct Result
        {
            public double Median;
            public double BytesPerOp;
        }

        private static Result Measure(Scenario s, IRope rope, int samples, double warmSeconds, double sampleSeconds)
        {
            Rig rig = Scenarios.RigFor(s.Name);
            s.Setup(rope, rig);
            Func<IRope, Rig, int, double> op = s.Op;
            int frame = 0;
            double sink = 0;

            // warm up, and find a batch size that takes about one sample's time
            var watch = Stopwatch.StartNew();
            long ops = 0;
            while (watch.Elapsed.TotalSeconds < warmSeconds)
            {
                sink += op(rope, rig, frame++);
                ops++;
            }
            int batch = Math.Max(1, (int)(ops * sampleSeconds / watch.Elapsed.TotalSeconds));

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var perOp = new double[samples];
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < samples; i++)
            {
                long start = Stopwatch.GetTimestamp();
                for (int k = 0; k < batch; k++) sink += op(rope, rig, frame++);
                long elapsed = Stopwatch.GetTimestamp() - start;
                perOp[i] = elapsed * 1e9d / Stopwatch.Frequency / batch;
            }
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

            Sink = sink;

            Array.Sort(perOp);
            double median = perOp[samples / 2];

            return new Result
            {
                Median = median,
                BytesPerOp = (double)allocated / ((long)batch * samples),
            };
        }

        /// <summary>Null when the two sides agree on every frame; otherwise what differed, and where.</summary>
        private static string Verify(Scenario s, out double worst)
        {
            IRope baseline = new BaselineRope();
            IRope current = new CurrentRope();
            Rig baselineRig = Scenarios.RigFor(s.Name);
            Rig currentRig = Scenarios.RigFor(s.Name);
            s.Setup(baseline, baselineRig);
            s.Setup(current, currentRig);

            worst = 0;
            for (int frame = 0; frame < VerifyFrames; frame++)
            {
                double a = s.Op(baseline, baselineRig, frame);
                double b = s.Op(current, currentRig, frame);

                if (Math.Abs(a - b) > ResultTolerance * Math.Max(1d, Math.Abs(a)) || double.IsNaN(b) != double.IsNaN(a))
                {
                    return $"frame {frame}: result {b:r}, baseline {a:r}";
                }

                if (!s.BuildsPath) continue;

                if (baseline.Count != current.Count)
                {
                    return $"frame {frame}: {current.Count} points, baseline {baseline.Count}";
                }

                for (int i = 0; i < baseline.Count; i++)
                {
                    double d = (baseline.Points[i] - current.Points[i]).Length();
                    worst = Math.Max(worst, d);
                    if (!(d <= PointTolerance))
                    {
                        return $"frame {frame}, point {i}: {d:e2} m apart";
                    }

                    double dd = Math.Abs(baseline.Distances[i] - current.Distances[i]);
                    if (!(dd <= PointTolerance))
                    {
                        return $"frame {frame}, distance {i}: {dd:e2} m apart";
                    }
                }
            }

            return null;
        }
    }
}
