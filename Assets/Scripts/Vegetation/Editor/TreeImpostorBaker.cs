using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using Unlimited.Vegetation;
using System.IO;
public static class TreeImpostorBaker {
 [MenuItem("Unlimited/Vegetation/Bake Selected Tree Impostor")]
 public static void Bake(){
 var root=Selection.activeGameObject;var lod=root?root.GetComponent<TreeDistanceLod>():null;
 if(EditorApplication.isPlaying || !lod || lod.parts==null || lod.parts.Length==0){Debug.LogError("Select baked Oak Preview outside Play Mode.");return;}
 var shader=Shader.Find("Unlimited/Vegetation/TreeImpostor");if(!shader){Debug.LogError("Impostor shader missing");return;}
 const int size=256;const string folder="Assets/Scripts/Vegetation/GeneratedLOD";
 if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/Scripts/Vegetation","GeneratedLOD");
 var stage=new GameObject("Impostor bake temporary");stage.hideFlags=HideFlags.HideAndDontSave;stage.transform.position=new Vector3(0,10000,0);
 var atlas=new Texture2D(size*4,size*2,TextureFormat.RGBA32,false);var rt=new RenderTexture(size,size,24,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;
 var materials=new System.Collections.Generic.List<Material>();
 try{
 Bounds bounds=new Bounds();bool first=true;
 foreach(var p in lod.parts){if(!p.filter || p.meshes==null || p.meshes.Length==0 || !p.meshes[0])continue;
 var src=p.filter.GetComponent<MeshRenderer>();if(!src)continue;
 var g=new GameObject("Bake part");g.layer=31;g.transform.SetParent(stage.transform,false);
 g.transform.localPosition=root.transform.InverseTransformPoint(p.filter.transform.position);g.transform.localRotation=Quaternion.Inverse(root.transform.rotation)*p.filter.transform.rotation;
 var s=p.filter.transform.lossyScale;var rs=root.transform.lossyScale;g.transform.localScale=new Vector3(s.x/rs.x,s.y/rs.y,s.z/rs.z);
 g.AddComponent<MeshFilter>().sharedMesh=p.meshes[0];var r=g.AddComponent<MeshRenderer>();var mats=src.sharedMaterials;
 for(int j=0;j<mats.Length;j++){if(!mats[j])continue;var m=new Material(mats[j]);if(m.HasProperty("_Wind"))m.SetFloat("_Wind",0);materials.Add(m);mats[j]=m;}r.sharedMaterials=mats;
 if(first){bounds=r.bounds;first=false;}else bounds.Encapsulate(r.bounds);
 }
 if(first)throw new System.Exception("No renderable tree parts");
 float extent=Mathf.Max(bounds.extents.y,new Vector2(bounds.extents.x,bounds.extents.z).magnitude)*1.08f;
 var cg=new GameObject("Bake camera");cg.transform.SetParent(stage.transform);var cam=cg.AddComponent<Camera>();cam.enabled=false;cam.orthographic=true;cam.orthographicSize=extent;cam.aspect=1;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=Color.clear;cam.cullingMask=1<<31;cam.targetTexture=rt;cam.allowHDR=false;cam.allowMSAA=false;cam.nearClipPlane=.01f;cam.farClipPlane=extent*8+10;
 for(int i=0;i<8;i++){var dir=Quaternion.Euler(0,i*45,0)*Vector3.forward;cam.transform.position=bounds.center+dir*(extent*3+1);cam.transform.rotation=Quaternion.LookRotation(-dir,Vector3.up);cam.Render();RenderTexture.active=rt;atlas.ReadPixels(new Rect(0,0,size,size),(i%4)*size,(i/4)*size,false);}
 atlas.Apply();string path=AssetDatabase.GenerateUniqueAssetPath(folder+"/TreeImpostor.png");File.WriteAllBytes(path,atlas.EncodeToPNG());AssetDatabase.ImportAsset(path);var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.alphaIsTransparency=true;importer.mipmapEnabled=true;importer.mipMapsPreserveCoverage=true;importer.alphaTestReferenceValue=.45f;importer.wrapMode=TextureWrapMode.Clamp;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
 var mat=new Material(shader);mat.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(path));AssetDatabase.CreateAsset(mat,AssetDatabase.GenerateUniqueAssetPath(folder+"/TreeImpostor.mat"));
 var mesh=new Mesh();mesh.vertices=new[]{new Vector3(extent,-extent,0),new Vector3(-extent,-extent,0),new Vector3(-extent,extent,0),new Vector3(extent,extent,0)};mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,AssetDatabase.GenerateUniqueAssetPath(folder+"/TreeImpostor.asset"));
 Undo.RecordObject(lod,"Bake impostor");if(lod.impostor)Undo.DestroyObjectImmediate(lod.impostor.gameObject);
 var quad=new GameObject("LOD4 Impostor");Undo.RegisterCreatedObjectUndo(quad,"Create impostor");quad.transform.SetParent(root.transform,false);quad.transform.localPosition=bounds.center-stage.transform.position;quad.AddComponent<MeshFilter>().sharedMesh=mesh;lod.impostor=quad.AddComponent<MeshRenderer>();lod.impostor.sharedMaterial=mat;lod.impostor.shadowCastingMode=ShadowCastingMode.Off;lod.impostor.enabled=false;
 EditorUtility.SetDirty(lod);UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);AssetDatabase.SaveAssets();Debug.Log("[Vegetation Impostor] Baked 8 views, 2 triangles.");
 }finally{RenderTexture.active=previous;Object.DestroyImmediate(stage);foreach(var m in materials)Object.DestroyImmediate(m);rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(atlas);}
 }
}
