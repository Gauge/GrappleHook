using GrappleHook.Core;
using Sandbox.ModAPI;
using SENetworkAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using static VRageRender.MyBillboard;

namespace GrappleHook
{
    /// <summary>
    /// The rope: the forces it puts on what it joins, the winch, wrapping around obstacles,
    /// and its shape on screen.
    ///
    /// Physically the rope is a polyline - the muzzle, each point it wraps around, the hook -
    /// and a length of rope paid out. Stretched past that length it pulls: a stiff, damped,
    /// one-sided constraint scaled to the mass on its ends (WinchMath.Tension). Every machine
    /// works out the same forces from the same synced length and wrap points.
    ///
    /// What the player sees is separate: on a client, a chain of points per span that swings
    /// under gravity and its own momentum (RopeChain). A dedicated server, which draws
    /// nothing, only builds the exact catenary shape (RopeMath) for its riders.
    /// </summary>
    public abstract partial class WeaponControlLayer
    {
        private static readonly MyStringId RopeTexture = MyStringId.GetOrCompute("cable");
        private static readonly MyStringId RopeTextureInteractable = MyStringId.GetOrCompute("cableRed");
        private static readonly Vector4 RopeColor = Color.DarkGray;
        private static readonly Vector4 RopeColorInteractable = Color.Red;
        private const float RopeWidth = 0.15f;

        /// <summary>A rope further than this from the camera is too thin to see and is not built.</summary>
        private const double MaxDrawDistance = 2500d;

        /// <summary>Slack in the rope while the hook is in flight, as a fraction of the span.</summary>
        private const double FlightSlack = 0.001d;

        /// <summary>How much of its swing relative to its ends the drawn rope keeps per tick.</summary>
        private const double ChainKeep = 0.985d;

        /// <summary>Speed lost to the air per tick, per unit of air density (1 at sea level).</summary>
        private const double ChainAirDrag = 0.02d;

        private const int ChainIterations = 12;

        /// <summary>Mass of what the rope joins is looked up this often, in ticks.</summary>
        private const int MassRefreshTicks = 60;

        // Wrapping. The rope is tested against the world from a little past each end: the
        // launcher's own barrel, the hook's target right at the hook, and the edge a wrap point
        // sits on would otherwise catch it at once.
        private const double GunClearance = 2.5d;
        private const double HookClearance = 0.5d;
        private const double WrapClearance = 0.3d;
        /// <summary>A wrap point sits this far off the surface it caught on.</summary>
        private const double WrapMargin = 0.1d;
        /// <summary>A new wrap point this close to a neighbour is not added.</summary>
        private const double MinWrapSpacing = 0.5d;
        private const int EdgeSearchSteps = 6;

        /// <summary>
        /// The rope, recomputed once a frame and shared by the draw, the zipline
        /// interaction check and the zipline forces.
        /// </summary>
        private readonly RopePath rope = new RopePath();

        /// <summary>The rope's corners this frame: muzzle, wrap points, hook.</summary>
        private readonly List<Vector3D> anchors = new List<Vector3D>();

        /// <summary>The wrap point behind each anchor; null for the two ends.</summary>
        private readonly List<WrapPoint> anchorWraps = new List<WrapPoint>();

        /// <summary>Last tick's anchors, to find where the rope first touched an edge.</summary>
        private readonly List<Vector3D> previousAnchors = new List<Vector3D>();

        /// <summary>The drawn rope, one chain per span between anchors.</summary>
        private readonly List<RopeChain> chains = new List<RopeChain>();
        private readonly RopePath shapeBuffer = new RopePath();

        private static readonly List<IHitInfo> HitBuffer = new List<IHitInfo>();
        private static readonly List<IMyCubeGrid> GroupBuffer = new List<IMyCubeGrid>();

        /// <summary>The rope's tension last tick, N. The winch stalls and the rope snaps by it.</summary>
        private double tension;

        private double inverseMassLauncher;
        private double inverseMassTarget;
        private int massRefreshFrame;

        private bool interactable = false;

        /// <summary>The rope length was changed by the winch and not yet sent to clients.</summary>
        private bool lengthUnsent;

        private void BuildAnchors()
        {
            anchors.Clear();
            anchorWraps.Clear();

            anchors.Add(MuzzlePosition());
            anchorWraps.Add(null);

            if (State.Value == States.attached)
            {
                List<WrapPoint> wraps = Wraps.Value;
                for (int i = 0; i < wraps.Count; i++)
                {
                    // a client may not have the entity a wrap point is on; the rope skips it
                    if (!wraps[i].Resolve()) continue;
                    anchors.Add(wraps[i].World);
                    anchorWraps.Add(wraps[i]);
                }

                anchors.Add(attachPoint);
            }
            else
            {
                anchors.Add(GrappleMatrix.Translation);
            }

            anchorWraps.Add(null);
        }

        /// <summary>How much rope is out: paid out while attached, just over the span in flight.</summary>
        private double CurrentRopeLength(double span)
        {
            switch (State.Value)
            {
                case States.attached:
                    return GrappleLength.Value;
                case States.projectile:
                    return span * (1d + FlightSlack);
                default:
                    return span;
            }
        }

        private void UpdateMasses()
        {
            if (frame < massRefreshFrame) return;
            massRefreshFrame = frame + MassRefreshTicks;

            inverseMassLauncher = InverseMass(Weapon.CubeGrid);
            inverseMassTarget = InverseMass(ConnectedEntity);
        }

        /// <summary>
        /// 1 / mass of what the rope pulls on. A grid counts everything joined to it by rotors,
        /// pistons, hinges and connectors; anything static, or joined to something static,
        /// is an anchor (0).
        /// </summary>
        private static double InverseMass(IMyEntity entity)
        {
            if (entity == null) return 0;

            IMyCubeGrid grid = entity as IMyCubeGrid;
            if (grid != null)
            {
                GroupBuffer.Clear();
                MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Physical, GroupBuffer);
                if (GroupBuffer.Count == 0) GroupBuffer.Add(grid);

                double mass = 0;
                for (int i = 0; i < GroupBuffer.Count; i++)
                {
                    IMyCubeGrid member = GroupBuffer[i];
                    if (member.Physics == null) continue;
                    if (member.IsStatic || member.Physics.IsStatic)
                    {
                        GroupBuffer.Clear();
                        return 0;
                    }
                    mass += member.Physics.Mass;
                }

                GroupBuffer.Clear();
                return mass > 0 ? 1d / mass : 0;
            }

            if (entity.Physics == null || entity.Physics.IsStatic || entity.Physics.Mass <= 0) return 0;
            return 1d / entity.Physics.Mass;
        }

        /// <summary>
        /// The winch reels in or pays out, but stops reeling in when the rope already pulls as
        /// hard as the winch can (Winch.ForceLargeGrid / ForceSmallGrid). Every machine steps
        /// the length itself; the server sends its value once a second while it changes, and
        /// once when it stops, so they cannot drift apart.
        /// </summary>
        private void UpdateGrappleLength()
        {
            float winch = Winch.Value;
            if (WinchMath.WinchStalled(winch, tension, Config.WinchForceFor(IsLargeGrid)))
            {
                winch = 0;
            }

            double length = WinchMath.StepLength(GrappleLength.Value, winch, Config.Rope.MinLength, Config.Rope.MaxLength);

            if (length != GrappleLength.Value)
            {
                bool send = isServer && frame % 60 == 0;
                GrappleLength.SetValue(length, send ? SyncType.Broadcast : SyncType.None);
                lengthUnsent = isServer && !send;
            }
            else if (lengthUnsent)
            {
                lengthUnsent = false;
                GrappleLength.Push();
            }
        }

        public void ApplyForce()
        {
            tension = 0;

            if (Weapon.CubeGrid.Physics == null || ConnectedEntity == null || Entity.MarkedForClose || ConnectedEntity.MarkedForClose || anchors.Count < 2)
            {
                return;
            }

            double stretch = RopeWrap.Length(anchors) - GrappleLength.Value;
            if (stretch <= 0) return;

            double effectiveMass = WinchMath.EffectiveMass(inverseMassLauncher, inverseMassTarget);
            if (effectiveMass <= 0) return;

            int last = anchors.Count - 1;
            Vector3D launcherEnd = anchors[0];
            Vector3D hookEnd = anchors[last];
            Vector3D launcherPull = RopeWrap.Direction(launcherEnd, anchors[1]);
            Vector3D hookPull = RopeWrap.Direction(hookEnd, anchors[last - 1]);

            MyPhysicsComponentBase launcher = Weapon.CubeGrid.Physics;
            MyPhysicsComponentBase target = ConnectedEntity.Physics;

            // how fast the rope is being stretched: each end and each wrap point moving away
            // from its neighbours lengthens it
            double stretchRate = -Vector3D.Dot(launcher.GetVelocityAtPoint(launcherEnd), launcherPull);
            if (target != null)
            {
                stretchRate -= Vector3D.Dot(target.GetVelocityAtPoint(hookEnd), hookPull);
            }

            for (int i = 1; i < last; i++)
            {
                MyPhysicsComponentBase obstacle = anchorWraps[i].Entity.Physics;
                if (obstacle == null || obstacle.IsStatic) continue;
                stretchRate -= Vector3D.Dot(obstacle.GetVelocityAtPoint(anchors[i]), RopeWrap.PressDirection(anchors[i - 1], anchors[i], anchors[i + 1]));
            }

            tension = WinchMath.Tension(stretch, stretchRate, effectiveMass, Config.Rope.SpringFrequency, Config.Rope.DampingRatio);
            if (tension <= 0) return;

            if (isServer && Config.Rope.Breaking.Enabled && tension > Config.BreakingStrengthFor(IsLargeGrid))
            {
                Tools.Debug($"Rope snapped at {tension:n0} N: stretched {stretch:n3} m at {stretchRate:n2} m/s, {anchors.Count - 2} wrap points, mass {effectiveMass:n0} kg");
                Reset(false);
                return;
            }

            if (inverseMassLauncher > 0)
            {
                launcher.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, launcherPull * tension, launcherEnd, null, null, true);
            }

            if (inverseMassTarget > 0 && target != null)
            {
                target.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, hookPull * tension, hookEnd, null, null, true);
            }

            // the rope presses into each edge it bends around
            for (int i = 1; i < last; i++)
            {
                MyPhysicsComponentBase obstacle = anchorWraps[i].Entity.Physics;
                if (obstacle == null || obstacle.IsStatic) continue;
                obstacle.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, RopeWrap.PressDirection(anchors[i - 1], anchors[i], anchors[i + 1]) * tension, anchors[i], null, null, true);
            }
        }

        /// <summary>
        /// Rope.Wrapping: the server tests each span of the rope against the world. A span that
        /// has swung into something catches on the edge it first touched - found by sweeping
        /// between last tick's span and this one - and bends there. A wrap point lets go once
        /// the rope swings back past straight. Changes are sent to clients.
        /// </summary>
        private void UpdateWrapping()
        {
            if (!isServer) return;

            List<WrapPoint> wraps = Wraps.Value;
            bool changed = false;

            if (!Config.Rope.Wrapping.Enabled)
            {
                if (wraps.Count > 0)
                {
                    wraps.Clear();
                    Wraps.Push();
                    BuildAnchors();
                }
                previousAnchors.Clear();
                return;
            }

            for (int i = wraps.Count - 1; i >= 0; i--)
            {
                if (!wraps[i].Resolve())
                {
                    wraps.RemoveAt(i);
                    changed = true;
                }
            }
            if (changed) BuildAnchors();

            for (int i = wraps.Count - 1; i >= 0; i--)
            {
                if (RopeWrap.ShouldUnwrap(anchors[i], anchors[i + 1], anchors[i + 2], wraps[i].WorldBend))
                {
                    wraps.RemoveAt(i);
                    anchors.RemoveAt(i + 1);
                    anchorWraps.RemoveAt(i + 1);
                    changed = true;
                }
            }

            // one new wrap a tick, and only when last tick's spans line up with this tick's
            if (!changed && wraps.Count < Config.Rope.Wrapping.MaxWraps && previousAnchors.Count == anchors.Count)
            {
                for (int j = 0; j < anchors.Count - 1; j++)
                {
                    Vector3D a = anchors[j];
                    Vector3D b = anchors[j + 1];

                    IHitInfo hit;
                    if (!SpanBlocked(j, a, b, out hit)) continue;

                    IHitInfo edge = hit;
                    Vector3D pa = previousAnchors[j];
                    Vector3D pb = previousAnchors[j + 1];
                    IHitInfo probe;
                    if (!SpanBlocked(j, pa, pb, out probe))
                    {
                        double clear = 0, blocked = 1;
                        for (int k = 0; k < EdgeSearchSteps; k++)
                        {
                            double mid = (clear + blocked) * 0.5d;
                            if (SpanBlocked(j, Vector3D.Lerp(pa, a, mid), Vector3D.Lerp(pb, b, mid), out probe))
                            {
                                blocked = mid;
                                edge = probe;
                            }
                            else
                            {
                                clear = mid;
                            }
                        }
                    }

                    Vector3D point = edge.Position + (Vector3D)edge.Normal * WrapMargin;
                    if (Vector3D.DistanceSquared(point, a) < MinWrapSpacing * MinWrapSpacing || Vector3D.DistanceSquared(point, b) < MinWrapSpacing * MinWrapSpacing) continue;

                    Vector3D bend = RopeWrap.BendNormal(a, point, b);
                    if (bend.LengthSquared() < 1e-12) continue;

                    WrapPoint wrap = new WrapPoint(edge.HitEntity, point, bend);
                    wraps.Insert(j, wrap);
                    anchors.Insert(j + 1, point);
                    anchorWraps.Insert(j + 1, wrap);
                    changed = true;
                    if (Tools.DebugMode)
                    {
                        MatrixD muzzle = gun.GunBase.GetMuzzleWorldMatrix();
                        Tools.Debug($"Rope wrapped on {edge.HitEntity.DisplayName} at {Vector3D.Dot(point - muzzle.Translation, muzzle.Forward):n2} ahead {Vector3D.Dot(muzzle.Translation - point, muzzle.Up):n2} below; {wraps.Count} wrap points, rope {RopeWrap.Length(anchors):n2} m of {GrappleLength.Value:n2} m");
                    }
                    break;
                }
            }

            if (changed) Wraps.Push();

            previousAnchors.Clear();
            previousAnchors.AddRange(anchors);
        }

        /// <summary>
        /// True when span <paramref name="index"/>, from a to b, runs through a grid or voxel.
        /// Characters and loose items do not catch the rope.
        /// </summary>
        private bool SpanBlocked(int index, Vector3D a, Vector3D b, out IHitInfo hit)
        {
            hit = null;

            Vector3D d = b - a;
            double length = d.Length();
            double startClear = index == 0 ? GunClearance : WrapClearance;
            double endClear = index + 1 == anchors.Count - 1 ? HookClearance : WrapClearance;
            if (length <= startClear + endClear) return false;

            Vector3D direction = d / length;

            HitBuffer.Clear();
            MyAPIGateway.Physics.CastRay(a + direction * startClear, b - direction * endClear, HitBuffer);

            float nearest = float.MaxValue;
            for (int i = 0; i < HitBuffer.Count; i++)
            {
                IHitInfo h = HitBuffer[i];
                if (h.HitEntity == null || !(h.HitEntity is IMyCubeGrid || h.HitEntity is IMyVoxelBase)) continue;
                if (h.Fraction < nearest)
                {
                    nearest = h.Fraction;
                    hit = h;
                }
            }

            HitBuffer.Clear();
            return hit != null;
        }

        /// <summary>
        /// Rebuilds the rope's shape for this frame: on a client the swinging chains, on a
        /// dedicated server the exact catenary. Everything that needs the rope - the draw, the
        /// zipline interaction check, the zipline forces - reads the one buffer.
        /// </summary>
        private void UpdateRopePath()
        {
            rope.Clear();
            if (anchors.Count < 2) return;

            double total = RopeWrap.Length(anchors);
            double scale = total > 1e-9 ? CurrentRopeLength(total) / total : 1d;
            Vector3D middle = (anchors[0] + anchors[anchors.Count - 1]) * 0.5d;
            Vector3D gravity = WorldPlanets.GetGravity(middle);
            int spans = anchors.Count - 1;

            if (isDedicated)
            {
                for (int s = 0; s < spans; s++)
                {
                    double span = Vector3D.Distance(anchors[s], anchors[s + 1]);
                    RopeMath.AppendCurvePoints(rope, anchors[s], anchors[s + 1], gravity, span * scale, PointsFor(span, total), s > 0);
                }
            }
            else
            {
                if (chains.Count != spans)
                {
                    while (chains.Count < spans) chains.Add(new RopeChain());
                    while (chains.Count > spans) chains.RemoveAt(chains.Count - 1);
                    for (int s = 0; s < spans; s++) chains[s].Active = false;
                }

                double airDrag = WorldPlanets.GetAirDensity(middle) * ChainAirDrag;
                Vector3D gravityStep = gravity * (WinchMath.TickSeconds * WinchMath.TickSeconds);

                for (int s = 0; s < spans; s++)
                {
                    RopeChain chain = chains[s];
                    double span = Vector3D.Distance(anchors[s], anchors[s + 1]);
                    double restLength = span * scale;

                    if (!chain.Step(anchors[s], anchors[s + 1], restLength, gravityStep, ChainKeep, airDrag, ChainIterations))
                    {
                        StartChain(chain, s, restLength, PointsFor(span, total), gravity);
                    }

                    for (int i = s > 0 ? 1 : 0; i < chain.Count; i++)
                    {
                        rope.Add(chain.Points[i]);
                    }
                }
            }

            rope.UpdateDistances();
        }

        /// <summary>
        /// A chain starts from the shape the rope would hang in. In space there is no hang, so
        /// it starts bowed away from the launcher's underside, to show its slack.
        /// </summary>
        private void StartChain(RopeChain chain, int span, double restLength, int points, Vector3D gravity)
        {
            Vector3D down = gravity.LengthSquared() > 1e-6 ? gravity : (Vector3D)Weapon.WorldMatrix.Down;
            RopeMath.ComputeCurvePoints(shapeBuffer, anchors[span], anchors[span + 1], down, restLength, points);
            chain.Reset(shapeBuffer.Points, 0, shapeBuffer.Count);
        }

        /// <summary>The rope's points shared among its spans by length.</summary>
        private int PointsFor(double span, double total)
        {
            int points = Config.Rope.DrawPoints;
            if (anchors.Count <= 2 || total <= 0) return points;
            return Math.Max(RopeMath.MinSegmentPoints, (int)Math.Round(points * span / total) + 1);
        }

        private void HideRope()
        {
            rope.Clear();
            for (int i = 0; i < chains.Count; i++) chains[i].Active = false;
        }

        /// <summary>
        /// Whether the rope can be on screen: the region it can reach is inside the camera's
        /// view and close enough to see. A rope off screen is not built at all; it starts again
        /// from its hanging shape when it comes back into view.
        /// </summary>
        private bool RopeInView()
        {
            IMyCamera camera = MyAPIGateway.Session.Camera;
            if (camera == null || anchors.Count < 2) return true;

            BoundingBoxD bounds = BoundingBoxD.CreateInvalid();
            for (int i = 0; i < anchors.Count; i++) bounds.Include(anchors[i]);

            double total = RopeWrap.Length(anchors);
            bounds.Inflate(RopeMath.ReachAcross(total, CurrentRopeLength(total)) + 1d);

            Vector3D eye = camera.WorldMatrix.Translation;
            Vector3D nearest = Vector3D.Clamp(eye, bounds.Min, bounds.Max);
            if (Vector3D.DistanceSquared(eye, nearest) > MaxDrawDistance * MaxDrawDistance) return false;

            return camera.IsInFrustum(ref bounds);
        }

        private void Draw()
        {
            try
            {
                List<ZiplineEntity> riders = ZiplinePlayers.Value;
                if (rope.SegmentCount == 0 && riders.Count == 0) return;

                Vector4 color = interactable ? RopeColorInteractable : RopeColor;
                MyStringId texture = interactable ? RopeTextureInteractable : RopeTexture;

                for (int i = 0; i < rope.SegmentCount; i++)
                {
                    MySimpleObjectDraw.DrawLine(rope.Points[i], rope.Points[i + 1], texture, ref color, RopeWidth, BlendTypeEnum.Standard);
                }

                for (int i = 0; i < riders.Count; i++)
                {
                    ZiplineEntity zipEntity = riders[i];
                    ZiplineEntity.Populate(ref zipEntity);

                    if (!zipEntity.hasLastPulley || zipEntity.player == null || !(zipEntity.player.Controller.ControlledEntity is IMyCharacter)) continue;

                    IMyCharacter character = zipEntity.player.Character;
                    if (character == null) continue;

                    MySimpleObjectDraw.DrawLine(character.WorldMatrix.Translation + character.WorldMatrix.Up * 1.85f, zipEntity.pulley, texture, ref color, 0.05f, BlendTypeEnum.Standard);
                }

            }
            catch (Exception e)
            {
                Tools.Debug(e.ToString());
            }
        }
    }
}
