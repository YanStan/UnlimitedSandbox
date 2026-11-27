using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
namespace Unlimited.Vegetation
{
 public sealed class TreeDistanceLod : MonoBehaviour
 {
  [System.Serializable] public class Part { public MeshFilter filter; public Mesh[] meshes; }
  public Part[] parts;
  public float lod1Distance=20,lod2Distance=50,lod3Distance=100,hysteresis=2;
  public float impostorDistance=200,cullDistance=1000;
  public MeshRenderer impostor;
  public int CurrentLod {get;private set;}
  TreeDamagePreview damage; bool locked; bool applied;
  MaterialPropertyBlock block;
  MeshRenderer[] renderers; ShadowCastingMode[] shadows;
  internal static readonly List<TreeDistanceLod> Active=new List<TreeDistanceLod>();
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetRegistry(){Active.Clear();}
  void Awake(){damage=GetComponent<TreeDamagePreview>(); if(impostor)impostor.enabled=false;
   renderers=new MeshRenderer[parts==null?0:parts.Length]; shadows=new ShadowCastingMode[renderers.Length];
   for(int i=0;i<renderers.Length;i++){if(parts[i].filter)renderers[i]=parts[i].filter.GetComponent<MeshRenderer>();if(renderers[i])shadows[i]=renderers[i].shadowCastingMode;}
  }
  void OnEnable(){Active.Add(this);TreeLodManager.Ensure();}
  void OnDisable(){Active.Remove(this);}
  public void ConfigureForest(){lod1Distance=50;lod2Distance=100;lod3Distance=160;impostorDistance=240;hysteresis=8;}
  internal void Tick(Camera cam){
   if(locked||!cam)return;
   if(damage && damage.State>=2){RestoreAndLock();return;}
   Vector3 delta=cam.transform.position-transform.position;
   float d=delta.magnitude,h=Mathf.Max(0,hysteresis);int next=CurrentLod;
   int max=impostor && (!damage || damage.State==0)?5:3;
   next=Mathf.Min(next,max);
   while(next<max && d>Threshold(next)+h)next++;
   while(next>0 && d<Threshold(next-1)-h)next--;
   if(next!=CurrentLod || !applied)Apply(next);
   if(impostor && next==4){
    Vector3 local=transform.InverseTransformDirection(delta); local.y=0;
    if(local.sqrMagnitude>0.0001f){
     float angle=Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg;
     float frame=Mathf.Repeat(angle/45f,8);
     impostor.transform.localRotation=Quaternion.LookRotation(local.normalized,Vector3.up);
     
     if(block==null)block=new MaterialPropertyBlock();
     impostor.GetPropertyBlock(block);block.SetFloat("_Frame",frame);impostor.SetPropertyBlock(block);
    }
   }
  }
  float Threshold(int i){switch(i){case 0:return lod1Distance;case 1:return Mathf.Max(lod1Distance,lod2Distance);case 2:return Mathf.Max(lod2Distance,lod3Distance);case 3:return Mathf.Max(lod3Distance,impostorDistance);default:return Mathf.Max(impostorDistance,cullDistance);}}
  public void RestoreAndLock(){locked=true;Apply(0);}
  void Apply(int level){applied=true;CurrentLod=level;if(impostor){impostor.enabled=level==4;impostor.shadowCastingMode=ShadowCastingMode.Off;}
   if(parts==null)return;for(int i=0;i<parts.Length;i++){var p=parts[i];if(!p.filter)continue;var r=renderers[i];if(r){r.enabled=level<4;r.shadowCastingMode=level>=3?ShadowCastingMode.Off:shadows[i];}
   if(level<4 && p.meshes!=null && p.meshes.Length>level && p.meshes[level])p.filter.sharedMesh=p.meshes[level];}
  }
 }
}
