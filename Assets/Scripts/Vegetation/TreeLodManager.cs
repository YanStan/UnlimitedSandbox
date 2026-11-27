using UnityEngine;
namespace Unlimited.Vegetation {
 public sealed class TreeLodManager : MonoBehaviour {
  static TreeLodManager instance; Camera cam; float nextSearch; int cursor;
  internal static void Ensure(){if(instance)return;var go=new GameObject("Tree LOD Manager");instance=go.AddComponent<TreeLodManager>();}
  void LateUpdate(){if(ForestBenchmark.Running && ForestBenchmark.FreezeLod)return;using(var sample=ForestMetrics.Measure(6)){
   if(!cam && Time.unscaledTime>=nextSearch){nextSearch=Time.unscaledTime+1;cam=Camera.main;if(!cam)cam=FindAnyObjectByType<Camera>();}
   if(!cam)return;var list=TreeDistanceLod.Active;
   int budget=Mathf.Min(list.Count,Mathf.Clamp(Mathf.CeilToInt(list.Count*Time.unscaledDeltaTime*5),1,64));
   for(int i=0;i<budget && list.Count>0;i++){if(cursor>=list.Count)cursor=0;var lod=list[cursor++];if(lod)lod.Tick(cam);}
  }}
 }
}
