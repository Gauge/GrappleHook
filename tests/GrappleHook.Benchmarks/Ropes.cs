using System.Collections.Generic;
using VRageMath;
using B = GrappleHook.Baseline;
using C = GrappleHook.Core;

namespace GrappleHook.Benchmarks
{
    /// <summary>
    /// One rope as a block owns it: its shape builder and its path. Each scenario drives this
    /// interface, so the same calls run on the baseline and the current code.
    /// </summary>
    public interface IRope
    {
        void StepChains(List<Vector3D> anchors, double ropeLength, Vector3D gravity, double airDrag, Vector3D fallbackDown, int drawPoints);
        void BuildCatenary(List<Vector3D> anchors, double ropeLength, Vector3D gravity, int drawPoints);
        void Deactivate();
        bool RayHits(ref RayD ray, double range);
        double ClosestDistance(Vector3D position);
        Vector3D PointAt(double distance, out Vector3D tangent);

        int Count { get; }
        Vector3D[] Points { get; }
        double[] Distances { get; }
        double Length { get; }
    }

    /// <summary>The mod's own constants for the drawn rope (WeaponControlLayer.Rope).</summary>
    public static class ModConstants
    {
        public const double ChainKeep = 0.985d;
        public const double ChainAirDrag = 0.02d;
        public const int ChainIterations = 12;
        public const double InteractRange = 3.5d;
    }

    public sealed class CurrentRope : IRope
    {
        private readonly C.RopeShape shape = new C.RopeShape();
        private readonly C.RopePath path = new C.RopePath();

        public void StepChains(List<Vector3D> anchors, double ropeLength, Vector3D gravity, double airDrag, Vector3D fallbackDown, int drawPoints)
        {
            shape.StepChains(path, anchors, ropeLength, gravity, airDrag, fallbackDown, drawPoints, ModConstants.ChainKeep, ModConstants.ChainIterations);
        }

        public void BuildCatenary(List<Vector3D> anchors, double ropeLength, Vector3D gravity, int drawPoints)
        {
            C.RopeShape.BuildCatenary(path, anchors, ropeLength, gravity, drawPoints);
        }

        public void Deactivate() { shape.Deactivate(); }
        public bool RayHits(ref RayD ray, double range) { return C.RopeMath.RayHitsPath(path, ref ray, range); }
        public double ClosestDistance(Vector3D position) { return C.ZiplineMath.ClosestDistance(path, position); }

        public Vector3D PointAt(double distance, out Vector3D tangent)
        {
            Vector3D point;
            C.ZiplineMath.PointAt(path, distance, out point, out tangent);
            return point;
        }

        public int Count { get { return path.Count; } }
        public Vector3D[] Points { get { return path.Points; } }
        public double[] Distances { get { return path.Distances; } }
        public double Length { get { return path.Length; } }
    }

    public sealed class BaselineRope : IRope
    {
        private readonly B.RopeShape shape = new B.RopeShape();
        private readonly B.RopePath path = new B.RopePath();

        public void StepChains(List<Vector3D> anchors, double ropeLength, Vector3D gravity, double airDrag, Vector3D fallbackDown, int drawPoints)
        {
            shape.StepChains(path, anchors, ropeLength, gravity, airDrag, fallbackDown, drawPoints, ModConstants.ChainKeep, ModConstants.ChainIterations);
        }

        public void BuildCatenary(List<Vector3D> anchors, double ropeLength, Vector3D gravity, int drawPoints)
        {
            B.RopeShape.BuildCatenary(path, anchors, ropeLength, gravity, drawPoints);
        }

        public void Deactivate() { shape.Deactivate(); }
        public bool RayHits(ref RayD ray, double range) { return B.RopeMath.RayHitsPath(path, ref ray, range); }
        public double ClosestDistance(Vector3D position) { return B.ZiplineMath.ClosestDistance(path, position); }

        public Vector3D PointAt(double distance, out Vector3D tangent)
        {
            Vector3D point;
            B.ZiplineMath.PointAt(path, distance, out point, out tangent);
            return point;
        }

        public int Count { get { return path.Count; } }
        public Vector3D[] Points { get { return path.Points; } }
        public double[] Distances { get { return path.Distances; } }
        public double Length { get { return path.Length; } }
    }
}
