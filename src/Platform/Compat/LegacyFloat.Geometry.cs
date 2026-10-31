using System.Drawing;
using System.Runtime.InteropServices;

namespace SlimDX.Compat;

public static partial class LegacyFloat
{
    [DllImport(Kernel, EntryPoint = "legacy_distance", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int DistanceRaw(float px1, float px2, float py1, float py2, float tx, float ty, int bits, out double result);

    [DllImport(Kernel, EntryPoint = "legacy_circle_hit", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int CircleHitRaw(float px1, float px2, float py1, float py2, float tx, float ty,
        float radius, float scale, int targetRegion, float targetScale, int bits, out int result);

    [DllImport(Kernel, EntryPoint = "legacy_ellipse_hit", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int EllipseHitRaw(double direction, double angle, int angleWithDirection,
        float px1, float px2, float py1, float py2, float distanceTargetX, float distanceTargetY,
        float directionOwnY, float directionOwnX, float directionTargetX, float directionTargetY,
        int targetRegion, float targetScale, float radius, float width, float length, int bits, out int result);

    [DllImport(Kernel, EntryPoint = "legacy_motion_step", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int MotionStepRaw(float ox1, int left1, float oy1, int top1, float velocityX, double directionX, float scaleX,
        float ox2, int left2, float oy2, int top2, float velocityY, double directionY, float scaleY,
        int setterLeft, int setterTop, int bits, out float originalX, out float originalY);

    /// <summary>One arithmetic block, preserving four ordered position reads and the CLR2 double spill before sqrt.</summary>
    public static double Distance(PointF xFirst, PointF xSecond, PointF yFirst, PointF ySecond, PointF target)
    {
        Check(DistanceRaw(xFirst.X, xSecond.X, yFirst.Y, ySecond.Y, target.X, target.Y, Precision, out double result));
        return result;
    }

    /// <summary>The original ordered distance-minus-extent comparison, without reassociation or squared-distance substitution.</summary>
    public static bool CircleHit(PointF target, PointF xFirst, PointF xSecond, PointF yFirst, PointF ySecond,
        int targetRegion, float targetScale, float radius, float scale)
    {
        Check(CircleHitRaw(xFirst.X, xSecond.X, yFirst.Y, ySecond.Y, target.X, target.Y,
            radius, scale, targetRegion, targetScale, Precision, out int result));
        return result != 0;
    }

    /// <summary>Preserves ellipse arithmetic, including the extended angle sum and all explicit storage boundaries.</summary>
    public static bool EllipseHit(bool angleWithDirection, double direction, double angle,
        PointF distanceTarget, PointF xFirst, PointF xSecond, PointF yFirst, PointF ySecond,
        int targetRegion, float targetScale, PointF directionTarget, PointF directionY, PointF directionX,
        float radius, float width, float length)
    {
        Check(EllipseHitRaw(direction, angle, angleWithDirection ? 1 : 0,
            xFirst.X, xSecond.X, yFirst.Y, ySecond.Y, distanceTarget.X, distanceTarget.Y,
            directionY.Y, directionX.X, directionTarget.X, directionTarget.Y,
            targetRegion, targetScale, radius, width, length, Precision, out int result));
        return result != 0;
    }

    /// <summary>Preserves both translated Position getters, motion and the final setter's binary32 boundaries.</summary>
    public static PointF MotionStep(PointF originX1, int left1, PointF originY1, int top1, float velocityX, double directionX, float scaleX,
        PointF originX2, int left2, PointF originY2, int top2, float velocityY, double directionY, float scaleY,
        int setterLeft, int setterTop)
    {
        Check(MotionStepRaw(originX1.X, left1, originY1.Y, top1, velocityX, directionX, scaleX,
            originX2.X, left2, originY2.Y, top2, velocityY, directionY, scaleY,
            setterLeft, setterTop, Precision, out float x, out float y));
        return new PointF(x, y);
    }
}
