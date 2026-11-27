using Unity.Profiling;
using System.Diagnostics;
namespace Unlimited.Vegetation {
 internal static class ForestMetrics {
 static readonly string[] names={"Update","Colliders","Scan","Unload","Spawn","Ground","LOD"};
 static readonly ProfilerMarker[] markers=new ProfilerMarker[names.Length];
 static readonly double[] total=new double[names.Length], peak=new double[names.Length];
 static readonly int[] calls=new int[names.Length];
 static ForestMetrics(){for(int i=0;i<names.Length;i++)markers[i]=new ProfilerMarker("Unlimited.Forest."+names[i]);}
 internal struct Sample:System.IDisposable {readonly int i;readonly long start;internal Sample(int index){i=index;start=Stopwatch.GetTimestamp();markers[i].Begin();}public void Dispose(){markers[i].End();double ms=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;total[i]+=ms;peak[i]=System.Math.Max(peak[i],ms);calls[i]++;}}
 internal static Sample Measure(int i)=>new Sample(i);
 internal static string Report(int frames){var b=new System.Text.StringBuilder("CPU inclusive (avg ms/frame, max ms/call, calls): ");for(int i=0;i<names.Length;i++){b.Append($"{names[i]}={total[i]/System.Math.Max(1,frames):F3}/{peak[i]:F3}/{calls[i]} ");total[i]=peak[i]=0;calls[i]=0;}return b.ToString();}
 }
}
