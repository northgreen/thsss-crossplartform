using System.Runtime.InteropServices;
using System.Drawing;

namespace SlimDX.Compat;

public static partial class LegacyFloat
{
    [DllImport(Kernel, EntryPoint = "legacy_script_acceleration", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int ScriptAccelerationRaw(double direction, float accelerationX, float accelerationY,
        int bits, out float x, out float y);

    /// <summary>Preserves the extended script angle and each trigonometric binary32 boundary.</summary>
    public static PointF ScriptAcceleration(double direction, float accelerationX, float accelerationY)
    {
        Check(ScriptAccelerationRaw(direction, accelerationX, accelerationY, Precision, out float x, out float y));
        return new PointF(x, y);
    }

    [DllImport(Kernel, EntryPoint = "legacy_degree_increment", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int DegreeIncrementRaw(double radians, float delta, int bits, out double result);

    /// <summary>Preserves the degree getter, addition and setter boundaries; radian normalization remains with the caller.</summary>
    public static double DegreeIncrement(double radians, float delta)
    {
        Check(DegreeIncrementRaw(radians, delta, Precision, out double result));
        return result;
    }
}
