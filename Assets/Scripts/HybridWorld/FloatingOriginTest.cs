using System;
using Unity.Mathematics;
using UnityEngine;

namespace Unlimited.HybridWorld
{
    public static class FloatingOriginTest
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            try
            {
                var state = new FloatingOriginState();
                int count = 0;
                foreach (float x in new[] {-3073f,-2048f,-1024f,-1023f,0f,1023f,1024f,2048f,3073f})
                foreach (float z in new[] {-2049f,-1024f,-1f,0f,1024f,2049f})
                {
                    var p = new Vector3(x, 73.25f, z);
                    var global = state.Global(p);
                    var key = WorldCoordinates.DensityAt(global);
                    var shift = FloatingOriginState.RequiredShift(p);
                    state.Commit(shift);
                    var next = p - shift;
                    if (math.any(state.Global(next) != global) || !WorldCoordinates.DensityAt(state.Global(next)).Equals(key) || state.Local(global) != next || Mathf.Abs(next.x) >= 1024 || Mathf.Abs(next.z) >= 1024)
                        throw new Exception("Rebase invariant failed: " + p);
                    count++;
                }
                for (int i=0;i<7200;i++) state.Commit(new Vector3(1024,0,-1024));
                var local = new Vector3(0.125f, 73.25f, -0.25f);
                if (state.Local(state.Global(local)) != local) throw new Exception("Long distance precision failed");
                Debug.Log($"[HybridFloatingOrigin] PASS: {count} rebase cases, long-distance precision; live MapMagic rebase disabled");
            }
            catch (Exception e) { Debug.LogError("[HybridFloatingOrigin] FAIL: " + e); }
        }
    }
}
