# GrappleHook.Benchmarks

Offline timing of the rope math the mod runs every frame. It never starts the game: it links
`GrappleHook/Core` the way the test project does, runs single threaded, and takes about ten seconds.

```bash
dotnet run -c Release --project tests/GrappleHook.Benchmarks                 # verify, then time everything
dotnet run -c Release --project tests/GrappleHook.Benchmarks -- --quick      # 5 samples instead of 15
dotnet run -c Release --project tests/GrappleHook.Benchmarks -- --filter client
dotnet run -c Release --project tests/GrappleHook.Benchmarks -- --verify-only
dotnet run -c Release --project tests/GrappleHook.Benchmarks -- --out tests/GrappleHook.Benchmarks/RESULTS.md
```

## How it works

[Baseline/](Baseline) is a frozen copy of `Core` from before the optimisation passes, in the
`GrappleHook.Baseline` namespace. Every scenario ([Scenarios.cs](Scenarios.cs)) runs on both
through the same interface ([Ropes.cs](Ropes.cs)):

1. **Verify.** Both sides run 900 frames in lockstep with the same inputs. Every op's result,
   and for path-building scenarios every point and arc distance, must agree within 1e-6 m;
   the largest point difference is printed. A failure exits 1 before anything is timed.
2. **Time.** Warm up, then 15 batches of about 20 ms each; the median ns per op is reported,
   with the bytes allocated per op.

The scenarios use the mod's own constants (12 constraint sweeps, 0.985 keep, 0.02 air drag)
and positions tens of kilometres from the world origin, as on a planet. Ops are per rope per
frame, or per call for the ray and zipline lookups.

## Reading the numbers

* Tiered compilation and dynamic PGO are off, so methods are compiled fully optimised on first
  call, closer to the .NET Framework 4.8 JIT the game uses. The game's absolute timings will
  still differ: read the **speedup** column, not the nanoseconds.
* Below about 200 ns, run-to-run differences of up to ~20% come from where the JIT happens to
  lay code out, not from the code. Treat a speedup between 0.85x and 1.15x as no change.
* Only the pure math is covered. Raycasts for wrapping, gravity lookups, forces and the
  billboard draw are game calls and are not measured here.

## When the baseline should move

The baseline is what "the same rope" means. A deliberate change to how the rope behaves makes
the verify step fail by design: copy `Core` into `Baseline/` again (changing the namespace)
once the change is accepted, so later passes are measured from there.

## Passes so far (2026-11-23)

See [RESULTS.md](RESULTS.md) for the full table.

| Pass | Change | Effect |
| --- | --- | --- |
| 1 | `RopeChain` stops its constraint sweeps once a sweep corrects nothing; later sweeps would see the same points. | Slack rope in space 2.8x. Bit-identical. |
| 2 | `RopeMath.RayHitsPath` skips a segment whose box is further from the eye than the grab range. | Zipline grab check 3.5-4x on a miss, 2x mixed. Same answers. |
| 3 | `RopeMath.AppendCurvePoints` gets each point from q = sinh(u) algebraically: no sinh per point, one asinh. | Dedicated server catenary 1.9x; chain restart 1.8x. Within 1e-10 m. |

Not done: a slack rope in gravity (`client/single`, about 2.9 µs) is twelve dependent
sqrt-and-divide sweeps per frame, and stays as it is unless its behaviour changes: fewer sweeps,
or an approximate square root. That is a call on how the rope should look, not an optimisation.
