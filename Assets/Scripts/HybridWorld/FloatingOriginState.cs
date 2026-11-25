using System;
using Unity.Mathematics;
using UnityEngine;

namespace Unlimited.HybridWorld
{
    // Pure coordinate state. No dependency on scene or MapMagic.
    public sealed class FloatingOriginState
    {
        public double3 Origin { get; private set; }
        public double3 Global(Vector3 local) => WorldCoordinates.ToGlobal(local, Origin);
        public Vector3 Local(double3 global) => WorldCoordinates.ToLocal(global, Origin);

        public static Vector3 RequiredShift(Vector3 local)
        {
            return new Vector3(Axis(local.x), 0f, Axis(local.z));
        }
        private static float Axis(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
            return (float)(Math.Truncate((double)value / WorldCoordinates.TileSize) * WorldCoordinates.TileSize);
        }
        public void Commit(Vector3 shift)
        {
            if (shift.y != 0 || shift.x != Axis(shift.x) || shift.z != Axis(shift.z))
                throw new ArgumentException("Shift must be aligned to the horizontal tile grid.");
            Origin += new double3(shift.x, 0, shift.z);
        }
    }
}
