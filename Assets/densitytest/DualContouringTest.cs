using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
namespace Unlimited.DensityTest
{
    public static class DualContouringTest
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            var samples = new NativeArray<float>(343, Allocator.TempJob);
            var vertices = default(NativeArray<float3>);
            var active = default(NativeArray<byte>);
            var indices = default(NativeList<int>);
            try
            {
                vertices = new NativeArray<float3>(64, Allocator.TempJob);
                active = new NativeArray<byte>(64, Allocator.TempJob);
                indices = new NativeList<int>(128, Allocator.TempJob);
                int3 origin = new int3(-8,12,20);
                for (int axis=0; axis<3; axis++)
                for (int sign=-1; sign<=1; sign+=2)
                {
                    for(int x=0;x<7;x++) for(int y=0;y<7;y++) for(int z=0;z<7;z++)
                        samples[(x*7+y)*7+z] = sign*((axis==0?x:axis==1?y:z)-2.25f);
                    new DualContouringJob { Samples=samples, SampleOrigin=origin, SampleSize=new int3(7), CellOrigin=origin+1, CellSize=new int3(4), Vertices=vertices, Active=active }.Schedule(64,8).Complete();
                    new DualContouringFacesJob { Samples=samples, SampleOrigin=origin, SampleSize=new int3(7), CellOrigin=origin+1, CellSize=new int3(4), Active=active, Indices=indices }.Schedule().Complete();
                    int count=0; bool ok=true;
                    for(int i=0;i<64;i++) if(active[i]!=0) { count++; ok &= math.abs(vertices[i][axis]-1.25f)<0.001f; }
                    ok &= count==16 && indices.Length==54;
                    for(int i=0;i<indices.Length;i+=3)
                    {
                        int a=indices[i],b=indices[i+1],c=indices[i+2];
                        if(a<0||a>=64||b<0||b>=64||c<0||c>=64) {ok=false; break;}
                        float3 n=math.cross(vertices[b]-vertices[a],vertices[c]-vertices[a]);
                        ok &= n[axis]*sign>0 && active[a]!=0 && active[b]!=0 && active[c]!=0;
                    }
                    if(!ok) { Debug.LogError("[DualContouring] FAIL: axis="+axis+" sign="+sign); return; }
                }
                Debug.Log("[DualContouring] PASS: vertices, faces and winding on 6 oriented planes");
            }
            finally
            {
                if(indices.IsCreated) indices.Dispose();
                if(active.IsCreated) active.Dispose();
                if(vertices.IsCreated) vertices.Dispose();
                if(samples.IsCreated) samples.Dispose();
            }
        }
    }
}
