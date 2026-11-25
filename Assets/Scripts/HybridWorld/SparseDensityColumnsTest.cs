using System;
using UnityEngine;
namespace Unlimited.HybridWorld
{
    public static class SparseDensityColumnsTest
    {
        private static void Check(bool ok) { if(!ok) throw new Exception("Sparse column invariant failed"); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            try
            {
                var s=new SparseDensityColumns((long x,long z,out double h)=>{h=34.5;return x!=99;});
                foreach(long x in new long[]{-17,-16,-1,0,15,16,7200000})
                {
                    Check(s.TryRead(x,2,x,out var a)&&a.Length==1&&a[0].Top==2.5);
                    Check(s.TryRead(x,3,x,out a)&&a.Length==0);
                    Check(s.TryRead(x,-1,x,out a)&&a.Length==1&&a[0].Top==16);
                }
                Check(s.ChunkCount==0);
                Check(!s.TryRead(99,0,0,out _));
                s.Write(-1,2,-1,Array.Empty<SparseDensityColumns.Span>());
                Check(s.ChunkCount==1&&s.TryRead(-1,2,-1,out var b)&&b.Length==0);
                Check(s.TryRead(-2,2,-1,out b)&&b.Length==1);
                var input=new[]{new SparseDensityColumns.Span(1,2)};
                s.Write(-1,2,-1,input); input[0]=default;
                Check(s.TryRead(-1,2,-1,out b)&&b[0].Top==2); b[0]=default;
                Check(s.TryRead(-1,2,-1,out b)&&b[0].Top==2);
                Check(s.RemoveChunk(SparseDensityColumns.Key(-1,2,-1))&&s.ChunkCount==0);
                Check(s.TryRead(-1,2,-1,out b)&&b[0].Top==2.5);
                Debug.Log("[HybridSparseDensity] PASS: sparse sections, negative coordinates, missing terrain, edit isolation and release");
            }
            catch(Exception e){Debug.LogError("[HybridSparseDensity] FAIL: "+e);}
        }
    }
}
