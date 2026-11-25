using UnityEngine;
using Unity.Mathematics;
using System.Collections;

namespace Unlimited.HybridWorld
{
    public sealed class TerrainHeightProbe : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { new GameObject("Hybrid Terrain Height Probe").AddComponent<TerrainHeightProbe>(); }
        IEnumerator Start()
        {
            for (int attempt=0; attempt<120; attempt++)
            {
                foreach (Terrain t in Terrain.activeTerrains)
                {
                    if (!t || !t.terrainData || !t.drawHeightmap) continue;
                    var d=t.terrainData;
                    if (Mathf.Abs(d.size.x-1024)>0.01f || Mathf.Abs(d.size.z-1024)>0.01f) continue;
                    Vector3 p=t.GetPosition()+new Vector3(d.size.x*0.5f,0,d.size.z*0.5f);
                    float expected=d.GetInterpolatedHeight(0.5f,0.5f)+t.GetPosition().y;
                    if (TerrainHeightSource.TrySample(new double3(p.x,p.y,p.z),default,out float actual) && Mathf.Abs(expected-actual)<0.01f)
                    { Debug.Log($"[HybridTerrainHeight] PASS: {d.size.x} x {d.size.z}, resolution {d.heightmapResolution}, height {actual:F3}"); Destroy(gameObject); yield break; }
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Debug.LogWarning("[HybridTerrainHeight] No matching active 1024m terrain sampled within 60 seconds."); Destroy(gameObject);
        }
    }
}
