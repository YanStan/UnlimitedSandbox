using UnityEditor;
using UnityEngine;
using Unlimited.Vegetation;

public static class OakPreviewBuilder
{
    const string Root = "Assets/Scripts/Vegetation/";
    [MenuItem("Unlimited/Vegetation/Create Oak Preview")]
    public static void Build()
    {
        var shader = Shader.Find("Unlimited/Vegetation/ProceduralTree");
        var trunk = AssetDatabase.LoadAssetAtPath<GameObject>(Root+"Models/Trunk.obj");
        var leaves = AssetDatabase.LoadAssetAtPath<GameObject>(Root+"Models/Leaves.obj");
        if (!shader || !trunk || !leaves) { Debug.LogError("Oak assets missing"); return; }
        var root = new GameObject("Oak Preview");
        var wood = Part(trunk,root.transform,"Wood",shader,new Color(.34f,.19f,.09f),false);
        var foliage = Part(leaves,root.transform,"Leaves",shader,new Color(.22f,.48f,.08f),true);
        var damage = root.AddComponent<TreeDamagePreview>(); damage.wood=wood; damage.leaves=foliage;
        foreach(var filter in wood.GetComponentsInChildren<MeshFilter>()) { var c=filter.gameObject.AddComponent<MeshCollider>(); c.sharedMesh=filter.sharedMesh; }
        Undo.RegisterCreatedObjectUndo(root,"Create Oak Preview"); Selection.activeGameObject=root;
        SceneView.lastActiveSceneView?.FrameSelected();
    }
    static GameObject Part(GameObject source,Transform parent,string name,Shader shader,Color color,bool foliage)
    {
        var part=(GameObject)PrefabUtility.InstantiatePrefab(source); part.name=name;part.transform.SetParent(parent,false);
        string path=Root+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!mat){mat=new Material(shader);mat.SetColor("_Color",color);mat.SetFloat("_Foliage",foliage?1:0);mat.enableInstancing=true;AssetDatabase.CreateAsset(mat,path);}
        foreach(var r in part.GetComponentsInChildren<Renderer>()) {var mats=r.sharedMaterials;for(int i=0;i<mats.Length;i++)mats[i]=mat;r.sharedMaterials=mats;}
        return part;
    }
}
