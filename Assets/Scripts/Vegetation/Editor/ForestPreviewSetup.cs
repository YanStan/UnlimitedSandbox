using UnityEditor;
using UnityEngine;
using Unlimited.Vegetation;
public static class ForestPreviewSetup {
 [MenuItem("Unlimited/Vegetation/Create Forest Preview")]
 static void Create(){
 if(EditorApplication.isPlayingOrWillChangePlaymode){Debug.LogError("Exit Play Mode first.");return;}
 var selected=Selection.activeGameObject;
 var tree=selected?selected.GetComponentInParent<TreeDamagePreview>(true):null;
 if(!Valid(tree)){
 tree=null;
 foreach(var candidate in Object.FindObjectsByType<TreeDamagePreview>(FindObjectsInactive.Include)){
 if(!Valid(candidate))continue;
 if(tree){Debug.LogError("Multiple baked trees found. Select the desired tree in Hierarchy.");return;}tree=candidate;
 }}
 if(!tree){Debug.LogError("No baked tree found. Bake Selected Tree LODs first.");return;}
 var forest=Object.FindAnyObjectByType<ForestPreview>(FindObjectsInactive.Include);
 if(!forest){var go=new GameObject("Forest Preview");Undo.RegisterCreatedObjectUndo(go,"Create Forest Preview");forest=Undo.AddComponent<ForestPreview>(go);}
 Undo.RecordObject(forest,"Assign forest template");forest.template=tree;
 Undo.RecordObject(tree.gameObject,"Enable tree template");tree.gameObject.SetActive(true);
 EditorUtility.SetDirty(forest);UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(forest.gameObject.scene);UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(tree.gameObject.scene);
 Selection.activeGameObject=forest.gameObject;Debug.Log("[Vegetation] Forest template assigned: "+tree.name);
 }
 static bool Valid(TreeDamagePreview t){return t && t.gameObject.scene.IsValid() && !t.forestInstance && t.wood && t.leaves && t.GetComponent<TreeDistanceLod>();}
}
