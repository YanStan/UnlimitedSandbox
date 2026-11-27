using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unlimited.Vegetation;
public static class MixedForestSetup {
 [MenuItem("Unlimited/Vegetation/Enable Mixed Forest")]
 public static void Setup(){if(EditorApplication.isPlaying)return;var forests=Object.FindObjectsByType<ForestPreview>(FindObjectsInactive.Include);if(forests.Length==0){Debug.LogError("[MixedForest] ForestPreview not found in open scene");return;}var trees=new TreeDamagePreview[3];string[] names={"Birch","Maple","Willow"};for(int i=0;i<3;i++){var g=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Scripts/Vegetation/MixedPrefabs/"+names[i]+".prefab");if(!g||!(trees[i]=g.GetComponent<TreeDamagePreview>())){Debug.LogError("[MixedForest] Missing prefab: "+names[i]);return;}}foreach(var forest in forests){Undo.RecordObject(forest,"Enable mixed forest");forest.mixedTemplates=trees;EditorUtility.SetDirty(forest);EditorSceneManager.MarkSceneDirty(forest.gameObject.scene);}Debug.Log("[MixedForest] Four species configured. Save scene and enter Play Mode.");}
}
