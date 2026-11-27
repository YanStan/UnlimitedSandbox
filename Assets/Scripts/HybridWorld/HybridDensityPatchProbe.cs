using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.InputSystem;
using Unlimited.DensityTest;

namespace Unlimited.HybridWorld
{
    // Read-only integration test: existing Density mesher, real MapMagic heights.
    public sealed class HybridDensityPatchProbe : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() {
            var probe = new GameObject("Hybrid Density Patch Probe").AddComponent<HybridDensityPatchProbe>();
            var settings = UnityEngine.Object.FindFirstObjectByType<HybridWorldSettings>();
            if (settings != null) {
                probe.sphereRadius = settings.sphereRadius;
                probe.hemisphereRadius = settings.hemisphereRadius;
                probe.smoothCubeSize = settings.smoothCubeSize;
                probe.sharpCubeSize = settings.sharpCubeSize;
                probe.smoothCubeRounding = settings.smoothCubeRounding;
            } else Debug.LogWarning("[HybridBrush] No Hybrid World Settings in scene; using defaults.");
        }
        [Header("Brush sizes (metres)")]
        [Min(0.5f)] public float sphereRadius = 3f;
        [Min(0.5f)] public float hemisphereRadius = 3f;
        [Min(1f)] public float smoothCubeSize = 2f;
        [Min(1f)] public float sharpCubeSize = 2f;
        [Min(0f)] public float smoothCubeRounding = 0.5f;
        readonly List<Vector2> sizeStrikes = new List<Vector2>();
        Vector2 BrushSize() {
            switch(brushShape) {
                case DensityBrushShape.Hemisphere: return new Vector2(Mathf.Max(0.5f,hemisphereRadius),0);
                case DensityBrushShape.SmoothCube: return new Vector2(Mathf.Max(1f,smoothCubeSize)*0.5f,Mathf.Clamp(smoothCubeRounding,0,Mathf.Max(1f,smoothCubeSize)*0.5f));
                case DensityBrushShape.SharpCube: return new Vector2(Mathf.Max(1f,sharpCubeSize)*0.5f,0);
                default: return new Vector2(Mathf.Max(0.5f,sphereRadius),0);
            }
        }
        [Serializable] sealed class SavedStrike {
            public Vector3 position;
            public bool build;
            public string shape;
            public Vector2 size;
        }
        [Serializable] sealed class SavedWorld {
            public int version = 3;
            public List<SavedPatch> patches = new List<SavedPatch>();
            public string scene;
            public List<SavedStrike> operations = new List<SavedStrike>();
        }
        [Serializable] sealed class SavedColumn {
            public bool present;
            public List<SurfacePoint> points;
        }
        [Serializable] sealed class SavedPatch {
            public int x, z, baseY, topY;
            public List<SavedColumn> columns = new List<SavedColumn>();
            public Vector3Int[] sharpCells;
        }
        string savePath;
        readonly Dictionary<Vector2Int,SavedPatch> pending = new Dictionary<Vector2Int,SavedPatch>();
        readonly Dictionary<Vector2Int,SavedPatch> savedCache = new Dictionary<Vector2Int,SavedPatch>();
        readonly HashSet<HybridDensityPatchProbe> dirtyPatches = new HashSet<HybridDensityPatchProbe>();
        Task saveTask;
        bool saveDirty;
        float nextSave;
        string saveScene;
        bool shuttingDown;
        bool loadFailed;
        float nextLoad;
        SavedPatch Capture(HybridDensityPatchProbe w) {
            var origin=WorldCoordinates.DensityOrigin(w.activeKey);
            var saved=new SavedPatch { x=(int)origin.x,z=(int)origin.z,baseY=w.activeBaseY,topY=w.activeTopY,sharpCells=w.activeField.CopySharpCells() };
            foreach(var column in w.activeField.surfaceCache) saved.columns.Add(new SavedColumn { present=column!=null,points=column==null?null:new List<SurfacePoint>(column) });
            return saved;
        }
        void LoadIndex() {
            try {
                var folder=Path.Combine(Application.persistentDataPath,"HybridWorld"); Directory.CreateDirectory(folder);
                var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                saveScene=scene;
                savePath=Path.Combine(folder,"snapshot-v3-"+scene+".json");
                if(!File.Exists(savePath)) return;
                var saved=JsonUtility.FromJson<SavedWorld>(File.ReadAllText(savePath));
                if(saved==null || saved.version!=3 || saved.scene!=scene || saved.patches==null) throw new Exception("Invalid snapshot");
                foreach(var item in saved.patches) {
                    if(item.columns==null || item.columns.Count!=576 || item.topY<=item.baseY) throw new Exception("Invalid saved patch");
                    pending.Add(new Vector2Int(item.x,item.z),item);
                    savedCache.Add(new Vector2Int(item.x,item.z),item);
                }
                foreach(var op in saved.operations) {
                    if(!Enum.TryParse<DensityBrushShape>(op.shape,out var shape)) throw new Exception("Invalid brush");
                    strikes.Add(op.position); buildStrikes.Add(op.build); shapeStrikes.Add(shape); sizeStrikes.Add(op.size);
                }
                Debug.Log($"[HybridLoad] Indexed {pending.Count} snapshots");
            } catch(Exception e) { loadFailed=true; pending.Clear(); savedCache.Clear(); strikes.Clear(); buildStrikes.Clear(); shapeStrikes.Clear(); sizeStrikes.Clear(); Debug.LogError("[HybridLoad] Original save protected: "+e); }
        }
        bool TileReady(SavedPatch item) {
            foreach(var t in Terrain.activeTerrains) {
                if(!t || !t.terrainData || !t.drawHeightmap) continue;
                var a=t.GetPosition(); var d=t.terrainData;
                if(item.x>=a.x && item.z>=a.z && item.x+16<=a.x+d.size.x && item.z+16<=a.z+d.size.z) return true;
            } return false;
        }
        void LoadOne() {
            if(loadFailed || pending.Count==0 || Time.unscaledTime<nextLoad) return;
            nextLoad=Time.unscaledTime+0.15f;
            SavedPatch item=null;
            foreach(var entry in pending) if(TileReady(entry.Value)) { item=entry.Value; break; }
            if(item==null) return;
            var go=new GameObject("Hybrid Patch State"); go.transform.SetParent(transform,false);
            var w=go.AddComponent<HybridDensityPatchProbe>(); w.worker=true; w.owner=this;
            w.CreatePatch(false,new Vector3(item.x+8,item.baseY,item.z+8),item);
            if(!w.patch) { Destroy(go); nextLoad=Time.unscaledTime+2f; return; }
            patches.Add(w); pending.Remove(new Vector2Int(item.x,item.z));
            // Refresh adjacent meshes after their shared boundary becomes internal.
            foreach(var n in patches) {
                var o=WorldCoordinates.DensityOrigin(n.activeKey);
                if(Math.Abs(o.x-item.x)+Math.Abs(o.z-item.z)>16) continue;
                if(n==w) continue;
                var root=new GameObject("Hybrid Density Patch"); root.SetActive(false); root.transform.SetParent(n.transform,false); root.transform.position=n.patch.transform.position;
                var output=new List<Mesh>();
                try { n.BuildGeometry(n.activeField,n.activeKey,n.activeBaseY,n.activeTopY,root,output); n.patch.SetActive(false); Destroy(n.patch); foreach(var m in n.meshes) if(m) Destroy(m); n.meshes.Clear(); n.meshes.AddRange(output); n.patch=root; root.SetActive(true); }
                catch(Exception e) { Destroy(root); foreach(var m in output) if(m) Destroy(m); Debug.LogError("[HybridLoad] Boundary refresh failed: "+e); }
            }
            Physics.SyncTransforms(); Debug.Log($"[HybridLoad] Restored patch {item.x}, {item.z}; pending {pending.Count}");
        }
        void PollSave() {
            if(saveTask==null || !saveTask.IsCompleted) return;
            try { saveTask.GetAwaiter().GetResult(); Debug.Log("[HybridSave] Autosave completed"); }
            catch(Exception e) { saveDirty=true; Debug.LogError("[HybridSave] Save failed: "+e); }
            saveTask=null;
        }
        void CachePatch(HybridDensityPatchProbe w) {
            if(!w || w.activeField==null) return;
            var item=Capture(w);
            savedCache[new Vector2Int(item.x,item.z)]=item;
            dirtyPatches.Remove(w);
        }
        void SaveWorld(bool wait=false) {
            if(loadFailed || string.IsNullOrEmpty(savePath)) return;
            try {
                if(wait && saveTask!=null) { try { saveTask.GetAwaiter().GetResult(); } catch { saveDirty=true; } saveTask=null; }
                PollSave();
                if(saveTask!=null || !saveDirty) return;
                foreach(var w in new List<HybridDensityPatchProbe>(dirtyPatches)) CachePatch(w);
                dirtyPatches.Clear();
                var snapshot=new SavedWorld { scene=saveScene };
                snapshot.patches.AddRange(savedCache.Values);
                for(int i=0;i<strikes.Count;i++) snapshot.operations.Add(new SavedStrike { position=strikes[i],build=buildStrikes[i],shape=shapeStrikes[i].ToString(),size=sizeStrikes[i] });
                var path=savePath;
                saveDirty=false;
                saveTask=Task.Run(()=> {
                    // JsonUtility supports background serialization of detached plain data.
                    string temp=path+".tmp";
                    File.WriteAllText(temp,JsonUtility.ToJson(snapshot));
                    if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
                });
                if(wait) { saveTask.GetAwaiter().GetResult(); PollSave(); }
            } catch(Exception e) { saveDirty=true; Debug.LogError("[HybridSave] Save failed: "+e); }
        }
        void ShutdownSave() {
            if(worker || shuttingDown) return;
            shuttingDown=true;
            SaveWorld(true);
        }
        void OnApplicationQuit() { ShutdownSave(); }
        void OnApplicationPause(bool paused) { if(paused && !worker) SaveWorld(true); }
        bool worker;
        HybridDensityPatchProbe owner;
        readonly List<Vector3> strikes = new List<Vector3>();
        DensityBrushShape brushShape = DensityBrushShape.Sphere;
        readonly List<DensityBrushShape> shapeStrikes = new List<DensityBrushShape>();
        readonly List<bool> buildStrikes = new List<bool>();
        readonly List<HybridDensityPatchProbe> patches = new List<HybridDensityPatchProbe>();
        sealed class TileLease {
            public int count;
            public Material original, material;
            public float pixelError;
            public int maximumLOD;
        }
        static readonly Dictionary<Terrain,TileLease> leases = new Dictionary<Terrain,TileLease>();
        TileLease lease;
        Terrain terrain;
        TerrainData data;
        bool[,] original;
        int hx, hz, holeResolution;
        readonly List<Mesh> meshes = new List<Mesh>();
        GameObject patch;
        DensitySurfaceField activeField;
        DensityWorldConfig activeConfig;
        WorldChunkKey activeKey;
        int activeBaseY, activeTopY, digCount;
        Material material;
        Material originalTerrainMaterial, testTerrainMaterial;
        void Start() { if(!worker) LoadIndex(); }
        void Update()
        {
            if(worker || shuttingDown) return;
            PollSave();
            if(Time.unscaledTime>=nextSave) { nextSave=Time.unscaledTime+5f; SaveWorld(); }
            if(Keyboard.current!=null && Keyboard.current.f9Key.wasPressedThisFrame) { SaveWorld(); return; }
            var keyboard=Keyboard.current;
            if(keyboard!=null) {
                var nextShape=brushShape;
                if(keyboard.digit1Key.wasPressedThisFrame) nextShape=DensityBrushShape.Hemisphere;
                else if(keyboard.digit2Key.wasPressedThisFrame) nextShape=DensityBrushShape.Sphere;
                else if(keyboard.digit3Key.wasPressedThisFrame) nextShape=DensityBrushShape.SmoothCube;
                else if(keyboard.digit4Key.wasPressedThisFrame) nextShape=DensityBrushShape.SharpCube;
                if(nextShape!=brushShape) { brushShape=nextShape; Debug.Log("[HybridBrush] "+brushShape); }
            }
            for(int i=patches.Count-1;i>=0;i--) {
                var w=patches[i];
                if(!w || !w.patch || !w.terrain || !w.terrain.isActiveAndEnabled || w.terrain.terrainData!=w.data || w.data.holesResolution!=w.holeResolution || !w.terrain.drawHeightmap) {
                    if(w) { if(w.activeField!=null) { var saved=Capture(w); pending[new Vector2Int(saved.x,saved.z)]=saved; savedCache[new Vector2Int(saved.x,saved.z)]=saved; dirtyPatches.Remove(w); saveDirty=true; } w.Restore(); Destroy(w.gameObject); }
                    patches.RemoveAt(i);
                    SaveWorld();
                }
            }
            bool preview=Keyboard.current!=null && Keyboard.current.f8Key.wasPressedThisFrame;
            bool dig=Mouse.current!=null && Mouse.current.leftButton.wasPressedThisFrame;
            bool build=Mouse.current!=null && Mouse.current.rightButton.wasPressedThisFrame;
            if(preview && (patches.Count>0 || strikes.Count>0 || pending.Count>0)) { dirtyPatches.Clear(); ClearPatches(); pending.Clear(); savedCache.Clear(); saveDirty=true; SaveWorld(true); return; }
            if(!preview && !dig && !build) { LoadOne(); return; }
            var cam=FindPlayerCamera(); if(!cam) { Debug.LogWarning("[HybridDig] No player camera"); return; }
            Vector3? targetPoint=null;
            if(!preview) {
                var hits=Physics.RaycastAll(cam.transform.position,cam.transform.forward,12f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
                Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
                foreach(var hit in hits) {
                    var tree = hit.collider.GetComponentInParent<Unlimited.Vegetation.TreeDamagePreview>();
                    if (tree != null) { if (dig) tree.Hit(); return; }
                    foreach(var w in patches) if(w.patch && hit.collider.transform.IsChildOf(w.patch.transform)) { DigAcross(hit.point,build); return; }
                    if(hit.collider is TerrainCollider) { targetPoint=hit.point; break; }
                }
                if(!targetPoint.HasValue) { Debug.Log("[HybridDig] No terrain or density hit within 12 m"); return; }
                DigAcross(targetPoint.Value,build); return;
            }
            var go=new GameObject("Hybrid Patch State"); go.transform.SetParent(transform,false);
            var next=go.AddComponent<HybridDensityPatchProbe>(); next.worker=true; next.owner=this;
            next.CreatePatch(preview,targetPoint);
            if(next.patch) { patches.Add(next); Debug.Log($"[HybridWorld] Active patches: {patches.Count}"); }
            else Destroy(go);
        }
        void ClearPatches() {
            foreach(var w in patches) if(w) { w.Restore(); Destroy(w.gameObject); }
            patches.Clear(); strikes.Clear(); buildStrikes.Clear(); shapeStrikes.Clear(); sizeStrikes.Clear(); safetyFrames=0;
        }
        void CreatePatch(bool preview, Vector3? targetPoint, SavedPatch snapshot=null)
        {
            try {
                var cam = FindPlayerCamera();
                if (!cam) throw new Exception("No active camera found on PlayerController or in scene");
                Debug.Log($"[HybridTerrainHole] CAMERA: {cam.name}, position={cam.transform.position}");
                var p = cam.transform.position;
                if (!preview) {
                    if (!targetPoint.HasValue) throw new Exception("Missing terrain hit");
                    p = targetPoint.Value;
                }
                var key = WorldCoordinates.DensityAt(new double3(p.x,0,p.z));
                var o = WorldCoordinates.DensityOrigin(key);
                Terrain best = null;
                foreach (var t in Terrain.activeTerrains) {
                    if (!t || !t.terrainData || !t.drawHeightmap) continue;
                    var a=t.GetPosition(); var d=t.terrainData;
                    if (o.x<a.x || o.z<a.z || o.x+16>a.x+d.size.x || o.z+16>a.z+d.size.z) continue;
                    if (!best || d.heightmapResolution>best.terrainData.heightmapResolution) best=t;
                }
                if (!best) throw new Exception("No loaded tile contains the patch");
                terrain=best; data=best.terrainData; holeResolution=data.holesResolution;
                var pos=best.GetPosition();
                float sx=holeResolution/data.size.x, sz=holeResolution/data.size.z;
                hx=Mathf.RoundToInt((float)(o.x-pos.x)*sx); hz=Mathf.RoundToInt((float)(o.z-pos.z)*sz);
                int w=Mathf.RoundToInt(16*sx), h=Mathf.RoundToInt(16*sz);
                if (Math.Abs(hx-(o.x-pos.x)*sx)>0.001 || Math.Abs(hz-(o.z-pos.z)*sz)>0.001 || Math.Abs(w-16*sx)>0.001 || Math.Abs(h-16*sz)>0.001) throw new Exception("Terrain hole grid does not align with 16 m patch");
                Debug.Log($"[HybridTerrainHole] TILE: resolution={data.heightmapResolution}, holes={holeResolution}, patch={w} x {h}");
                if (w<=0 || h<=0 || hx<0 || hz<0 || hx+w>holeResolution || hz+h>holeResolution) throw new Exception("Invalid hole bounds");
                var saved=data.GetHoles(hx,hz,w,h);
                foreach(bool solid in saved) if(!solid) throw new Exception("Existing hole in test area");
                if (!TryBuild(key, null, snapshot)) throw new Exception("Missing heights");
                original=saved;
                if(leases.TryGetValue(terrain,out lease)) { lease.count++; }
                else {
                originalTerrainMaterial=terrain.materialTemplate;
                if(!originalTerrainMaterial) throw new Exception("Terrain has no material template");
                string shaderName=originalTerrainMaterial.shader.name;
                if(shaderName != "Unlimited/Grass Rock Slope Blend" && shaderName != "Unlimited/TriplanarGrass")
                    throw new Exception("Unsupported terrain shader: "+shaderName);
                var holeShader=originalTerrainMaterial.shader;
                if(!holeShader.isSupported || holeShader.FindPropertyIndex("_HybridHoleEnabled") < 0 || !originalTerrainMaterial.HasProperty("_HybridHoleTexture"))
                    throw new Exception("Original shader missing hybrid properties: "+shaderName+". Reimport the shader in Assets/Materials and check shader compilation errors.");
                testTerrainMaterial=new Material(originalTerrainMaterial);
                Debug.Log($"[HybridTerrainHole] MATERIAL: {shaderName}; original shader, no Resources");
                testTerrainMaterial.SetFloat("_HybridHoleEnabled",1);
                testTerrainMaterial.SetVector("_HybridHoleRect",new Vector4(pos.x,pos.z,data.size.x,data.size.z));
                testTerrainMaterial.SetFloat("_HybridHoleResolution",holeResolution);
                lease=new TileLease { count=1, original=originalTerrainMaterial, material=testTerrainMaterial, pixelError=terrain.heightmapPixelError, maximumLOD=terrain.heightmapMaximumLOD };
                leases.Add(terrain,lease);
                terrain.heightmapPixelError=1; terrain.heightmapMaximumLOD=0;
                }
                patch.SetActive(true);
                data.SetHoles(hx,hz,new bool[h,w]);
                terrain.Flush();
                lease.material.SetTexture("_HybridHoleTexture",data.holesTexture);
                terrain.materialTemplate=lease.material;
                foreach(bool solid in data.GetHoles(hx,hz,w,h)) if(solid) throw new Exception("Hole readback failed");
                Physics.SyncTransforms();
                var center=new Vector3((float)o.x+8, data.size.y+pos.y+100, (float)o.z+8);
                bool hit=false;
                foreach(var r in Physics.RaycastAll(center,Vector3.down,data.size.y+200)) {
                    if(r.collider.transform.IsChildOf(patch.transform)) hit=true;
                    if(r.collider is TerrainCollider) throw new Exception("Terrain collider still present inside hole: "+r.collider.name);
                }
                if(!hit && preview) throw new Exception("Density collider raycast failed");
                Debug.Log($"[HybridTerrainHole] PASS: {w} x {h} hole, density collider hit; F8 restores terrain");
            } catch(Exception e) { Restore(); Debug.LogError("[HybridTerrainHole] FAIL: "+e); }
        }
        HybridDensityPatchProbe FindPatch(double3 o) {
            foreach(var w in patches) if(w && w.patch) {
                var q=WorldCoordinates.DensityOrigin(w.activeKey);
                if(q.x==o.x && q.z==o.z) return w;
            }
            return null;
        }
        void DigAcross(Vector3 p, bool build) {
            var added=new List<HybridDensityPatchProbe>();
            var affected=new List<HybridDensityPatchProbe>();
            var backups=new List<List<SurfacePoint>[]>();
            var oldFields=new List<DensitySurfaceField>();
            var oldMasks=new List<Vector3Int[]>();
            var oldBases=new List<int>(); var oldTops=new List<int>();
            var roots=new List<GameObject>();
            var outputs=new List<List<Mesh>>();
            try {
                var size=BrushSize();
                float reach=size.x+1f;
                int x0=Mathf.FloorToInt((p.x-reach)/16), x1=Mathf.FloorToInt((p.x+reach)/16);
                int z0=Mathf.FloorToInt((p.z-reach)/16), z1=Mathf.FloorToInt((p.z+reach)/16);
                for(int x=x0;x<=x1;x++) for(int z=z0;z<=z1;z++) {
                    var o=new double3(x*16,0,z*16);
                    if(FindPatch(o)) continue;
                    if(pending.ContainsKey(new Vector2Int(x*16,z*16))) { nextLoad=0; throw new Exception("Saved patch awaiting restoration; retry shortly"); }
                    var go=new GameObject("Hybrid Patch State"); go.transform.SetParent(transform,false);
                    var w=go.AddComponent<HybridDensityPatchProbe>(); w.worker=true; w.owner=this;
                    w.CreatePatch(false,new Vector3(x*16+8,p.y,z*16+8));
                    if(!w.patch) { Destroy(go); throw new Exception("Neighbour patch unavailable"); }
                    patches.Add(w); added.Add(w);
                    for(int i=0;i<strikes.Count;i++) w.ApplyStrike(strikes[i],buildStrikes[i],shapeStrikes[i],sizeStrikes[i]);
                }
                foreach(var w in patches) {
                    var c=w.patch.transform.position;
                    // Rebuild neighbours too, since their boundary policy may change.
                    if(Mathf.Abs(c.x-p.x)>reach+28 || Mathf.Abs(c.z-p.z)>reach+28) continue;
                    var cache=w.activeField.surfaceCache;
                    var backup=new List<SurfacePoint>[cache.Length];
                    for(int i=0;i<cache.Length;i++) if(cache[i]!=null) backup[i]=new List<SurfacePoint>(cache[i]);
                    affected.Add(w); backups.Add(backup);
                    oldMasks.Add(w.activeField.CopySharpCells()); oldFields.Add(w.activeField); oldBases.Add(w.activeBaseY); oldTops.Add(w.activeTopY);
                    w.ApplyStrike(p,build,brushShape,size);
                    var root=new GameObject("Hybrid Density Patch"); root.SetActive(false);
                    root.transform.SetParent(w.transform,false); root.transform.position=new Vector3(c.x,w.activeBaseY,c.z);
                    roots.Add(root); var output=new List<Mesh>(); outputs.Add(output);
                    w.BuildGeometry(w.activeField,w.activeKey,w.activeBaseY,w.activeTopY,root,output);
                }
                for(int i=0;i<affected.Count;i++) {
                    var w=affected[i]; var old=w.patch; old.SetActive(false);
                    w.patch=roots[i]; w.patch.SetActive(true); Destroy(old);
                    foreach(var m in w.meshes) if(m) Destroy(m);
                    w.meshes.Clear(); w.meshes.AddRange(outputs[i]);
                }
                foreach(var w in affected) dirtyPatches.Add(w);
                saveDirty=true;
                strikes.Add(p); buildStrikes.Add(build); shapeStrikes.Add(brushShape); sizeStrikes.Add(size); Physics.SyncTransforms();
                if(build) { lastBuildBrush=p; lastBuildExtent=size.x; safetyFrames=3; LiftPlayerAboveBuild(p); }
                Debug.Log($"[HybridDig] PASS: {(build ? "build" : "dig")} strike {strikes.Count}, active patches {patches.Count}");
            } catch(Exception e) {
                for(int i=0;i<affected.Count;i++) { var w=affected[i]; w.activeField=oldFields[i]; w.activeField.RestoreSharpCells(oldMasks[i],0); w.activeBaseY=oldBases[i]; w.activeTopY=oldTops[i]; w.activeConfig.height=w.activeField.H; var cache=w.activeField.surfaceCache; for(int j=0;j<cache.Length;j++) cache[j]=backups[i][j]; }
                foreach(var root in roots) if(root) Destroy(root);
                foreach(var output in outputs) foreach(var m in output) if(m) Destroy(m);
                foreach(var w in added) { patches.Remove(w); w.Restore(); Destroy(w.gameObject); }
                Debug.LogError("[HybridDig] Cross-boundary rebuild failed: "+e);
            }
        }
        Vector3 lastBuildBrush;
        float lastBuildExtent=3f;
        int safetyFrames;
        void LateUpdate()
        {
            if(worker || safetyFrames <= 0) return;
            safetyFrames--;
            LiftPlayerAboveBuild(lastBuildBrush);
        }
        void LiftPlayerAboveBuild(Vector3 brush)
        {
            var cam=FindPlayerCamera();
            if(!cam) return;
            var controller=cam.GetComponentInParent<CharacterController>();
            if(!controller || !controller.enabled) return;
            var bounds=controller.bounds;
            float feet=bounds.min.y;
            if(brush.y>bounds.max.y+0.35f || brush.y+lastBuildExtent+0.25f<feet-0.2f) return;
            float radius=Mathf.Max(bounds.extents.x,bounds.extents.z);
            var center=bounds.center;
            if(Mathf.Abs(center.x-brush.x)>lastBuildExtent+radius || Mathf.Abs(center.z-brush.z)>lastBuildExtent+radius) return;
            float highest=feet;
            // Sample the actual cooked collision surface, not the ideal sphere.
            for(int i=0;i<33;i++) {
                float angle=(i-1)*Mathf.PI/8f;
                float r=i==0?0f:radius*(i<=16?0.5f:1f);
                var sample=new Vector3(center.x+Mathf.Cos(angle)*r,brush.y+lastBuildExtent+0.5f,center.z+Mathf.Sin(angle)*r);
                foreach(var hit in Physics.RaycastAll(sample,Vector3.down,Mathf.Max(0.1f,sample.y-feet+0.2f),Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)) {
                    if(hit.normal.y<=0.05f || hit.point.y>brush.y+lastBuildExtent+0.25f) continue;
                    foreach(var w in patches) if(w.patch && hit.collider.transform.IsChildOf(w.patch.transform)) {
                        highest=Mathf.Max(highest,hit.point.y); break;
                    }
                }
            }
            if(highest<=feet+0.01f) return;
            float lift=highest-feet+Mathf.Max(0.05f,controller.skinWidth);
            controller.enabled=false;
            controller.transform.position+=Vector3.up*lift;
            controller.enabled=true;
            var player=controller.GetComponent<global::PlayerController>();
            if(player) player.ResetVerticalVelocityAfterBuild();
            Physics.SyncTransforms();
            Debug.Log($"[HybridBuild] Player lifted {lift:F3} m above new collision surface");
        }
        void EnsureVerticalRange(float worldY, float extent)
        {
            int bottom=Mathf.Min(activeBaseY, Mathf.FloorToInt((worldY-Mathf.Max(20f,extent+2f))/16f)*16);
            int top=Mathf.Max(activeTopY, Mathf.CeilToInt((worldY+Mathf.Max(20f,extent+2f))/16f)*16);
            if(bottom==activeBaseY && top==activeTopY) return;
            int shift=activeBaseY-bottom;
            var old=activeField;
            activeConfig.height=top-bottom+1;
            var next=new DensitySurfaceField(activeConfig);
            for(int i=0;i<old.surfaceCache.Length;i++) {
                var column=old.surfaceCache[i];
                if(column==null) continue;
                var copy=new List<SurfacePoint>(column.Count);
                foreach(var point in column) {
                    var q=point;
                    // Extend the original solid substrate downward, not an artificial cavity.
                    if(!(q.kind==SurfaceKind.Ceiling && q.y==0f)) q.y+=shift;
                    copy.Add(q);
                }
                next.surfaceCache[i]=copy;
            }
            next.RestoreSharpCells(old.CopySharpCells(),shift);
            activeField=next; activeBaseY=bottom; activeTopY=top;
            Debug.Log($"[HybridDensity] Vertical range expanded: {bottom} .. {top} m");
        }
        void ApplyStrike(Vector3 p, bool build, DensityBrushShape shape, Vector2 size) {
            var center=patch.transform.position;
            if(Mathf.Abs(p.x-center.x)>12+size.x || Mathf.Abs(p.z-center.z)>12+size.x) return;
            EnsureVerticalRange(p.y,size.x*(shape==DensityBrushShape.Hemisphere?1.5f:1f));
            var local=p-new Vector3(center.x,activeBaseY,center.z);
            var operation=new DensityDigOperation(activeField,activeConfig);
            DensityDigResult result;
            if(shape==DensityBrushShape.SmoothCube || shape==DensityBrushShape.SharpCube) {
                var box=new Bounds(local,Vector3.one*(size.x*2f));
                float rounding=shape==DensityBrushShape.SmoothCube?size.y:0f;
                result=build?operation.BuildBox(box,rounding):operation.DigBox(box,rounding);
            } else {
                if(shape==DensityBrushShape.Hemisphere) local.y+=build?-size.x*0.5f:size.x*0.5f;
                result=build?operation.BuildSphere(local,size.x):operation.DigSphere(local,size.x);
            }
            activeField.MarkBrushCells(result,shape==DensityBrushShape.SharpCube);
        }
        static Camera FindPlayerCamera()
        {
            foreach (var player in UnityEngine.Object.FindObjectsByType<global::PlayerController>(FindObjectsSortMode.None))
            {
                if (!player.isActiveAndEnabled) continue;
                foreach (var camera in player.GetComponentsInChildren<Camera>())
                    if (camera.isActiveAndEnabled) return camera;
            }
            if (Camera.main && Camera.main.isActiveAndEnabled) return Camera.main;
            foreach (var camera in Camera.allCameras)
                if (camera.isActiveAndEnabled && camera.cameraType == CameraType.Game) return camera;
            return null;
        }
        void Restore()
        {
            if(original!=null && data && data.holesResolution==holeResolution) { data.SetHoles(hx,hz,original); if(terrain && terrain.terrainData==data) terrain.Flush(); }
            original=null;
            if(lease!=null) {
                if(--lease.count==0) {
                    if(terrain) {
                        terrain.heightmapPixelError=lease.pixelError; terrain.heightmapMaximumLOD=lease.maximumLOD;
                        if(terrain.materialTemplate==lease.material) terrain.materialTemplate=lease.original;
                    }
                    if(!ReferenceEquals(terrain,null)) leases.Remove(terrain);
                    if(lease.material) Destroy(lease.material);
                } else if(terrain && data && terrain.terrainData==data) {
                    terrain.Flush(); lease.material.SetTexture("_HybridHoleTexture",data.holesTexture);
                }
                lease=null;
            } else if(testTerrainMaterial) Destroy(testTerrainMaterial);
            testTerrainMaterial=null; originalTerrainMaterial=null;
            if(patch) { patch.SetActive(false); Destroy(patch); }
            patch=null;
            foreach(var m in meshes) if(m) Destroy(m);
            meshes.Clear();
            if(material) Destroy(material); material=null;
            terrain=null; data=null; activeField=null; activeConfig=null; digCount=0;
        }
        void OnDisable() {
            if(worker) {
                if(owner && owner.dirtyPatches.Contains(this)) owner.CachePatch(this);
                Restore();
            } else { ShutdownSave(); ClearPatches(); }
        }
        static List<Vector3> Clip(List<Vector3> input,int axis,float bound,bool greater)
        {
            var output=new List<Vector3>();
            if(input.Count==0) return output;
            Vector3 a=input[input.Count-1];
            foreach(var b in input) {
                bool ia=greater?a[axis]>=bound:a[axis]<=bound;
                bool ib=greater?b[axis]>=bound:b[axis]<=bound;
                if(ia!=ib) { var v=Vector3.LerpUnclamped(a,b,(bound-a[axis])/(b[axis]-a[axis])); v[axis]=bound; output.Add(v); }
                if(ib) output.Add(b);
                a=b;
            }
            return output;
        }
        Mesh ClipAndStitch(Mesh source,double3 origin,int baseY,out Mesh seam)
        {
            seam=null;
            var vertices=new List<Vector3>(); var triangles=new List<int>();
            var map=new Dictionary<Vector3,int>();
            var input=source.vertices; var indices=source.triangles;
            var pos=terrain.GetPosition();
            float dx=data.size.x/holeResolution, dz=data.size.z/holeResolution;
            int w=Mathf.RoundToInt(16/dx), h=Mathf.RoundToInt(16/dz);
            bool On(float x,float b) => Mathf.Abs(x-b)<0.00001f;
            Vector3 Ground(Vector3 q) {
                bool vertical=On(q.x,-8)||On(q.x,8);
                bool horizontal=On(q.z,-8)||On(q.z,8);
                if(!vertical&&!horizontal)return q;
                if(owner) {
                    if(vertical && owner.FindPatch(origin+new double3(q.x<0?-16:16,0,0))) return q;
                    if(horizontal && owner.FindPatch(origin+new double3(0,0,q.z<0?-16:16))) return q;
                }
                if(vertical) {
                    q.x=q.x<0?-8:8;
                    int x=hx+(q.x<0?0:w);
                    float g=Mathf.Clamp((q.z+8)/dz,0,h);
                    int k=Mathf.Min(Mathf.FloorToInt(g),h-1);
                    q.y=Mathf.Lerp(data.GetHeight(x,hz+k),data.GetHeight(x,hz+k+1),g-k)+pos.y-baseY;
                } else {
                    q.z=q.z<0?-8:8;
                    int z=hz+(q.z<0?0:h);
                    float g=Mathf.Clamp((q.x+8)/dx,0,w);
                    int k=Mathf.Min(Mathf.FloorToInt(g),w-1);
                    q.y=Mathf.Lerp(data.GetHeight(hx+k,z),data.GetHeight(hx+k+1,z),g-k)+pos.y-baseY;
                }
                return q;
            }
            int Index(Vector3 q) {
                if(!map.TryGetValue(q,out int i)){i=vertices.Count;map.Add(q,i);vertices.Add(q);}return i;
            }
            void Tri(Vector3 a,Vector3 b,Vector3 c) {
                if(Vector3.Cross(b-a,c-a).sqrMagnitude<1e-12f)return;
                triangles.Add(Index(a));triangles.Add(Index(b));triangles.Add(Index(c));
            }
            for(int i=0;i<indices.Length;i+=3) {
                var poly=new List<Vector3>{input[indices[i]],input[indices[i+1]],input[indices[i+2]]};
                poly=Clip(poly,0,-8,true);poly=Clip(poly,0,8,false);poly=Clip(poly,2,-8,true);poly=Clip(poly,2,8,false);
                if(poly.Count<3)continue;
                var ring=new List<Vector3>(); bool boundary=false;
                for(int j=0;j<poly.Count;j++) {
                    var a=poly[j];var b=poly[(j+1)%poly.Count];
                    var snapped=Ground(a);ring.Add(snapped);
                    boundary|=On(a.x,-8)||On(a.x,8)||On(a.z,-8)||On(a.z,8);
                    bool vertical=(On(a.x,-8)&&On(b.x,-8))||(On(a.x,8)&&On(b.x,8));
                    bool horizontal=(On(a.z,-8)&&On(b.z,-8))||(On(a.z,8)&&On(b.z,8));
                    if(!vertical&&!horizontal)continue;
                    if(owner && ((vertical && owner.FindPatch(origin+new double3(a.x<0?-16:16,0,0))) || (horizontal && owner.FindPatch(origin+new double3(0,0,a.z<0?-16:16))))) continue;
                    int axis=vertical?2:0;float step=vertical?dz:dx;int count=vertical?h:w;
                    float delta=b[axis]-a[axis];if(Mathf.Abs(delta)<1e-7f)continue;
                    var cuts=new List<float>();
                    for(int k=1;k<count;k++){float f=(-8+k*step-a[axis])/delta;if(f>0.000001f&&f<0.999999f)cuts.Add(f);}
                    cuts.Sort();foreach(float f in cuts)ring.Add(Ground(Vector3.LerpUnclamped(a,b,f)));
                }
                if(!boundary){for(int j=1;j+1<ring.Count;j++)Tri(ring[0],ring[j],ring[j+1]);continue;}
                Vector3 center=Vector3.zero;foreach(var q in poly)center+=q;center/=poly.Count;
                for(int j=0;j<ring.Count;j++)Tri(center,ring[j],ring[(j+1)%ring.Count]);
            }
            if(triangles.Count==0)return null;
            var mesh=new Mesh{indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        bool TryBuild(WorldChunkKey key, Vector3? digPoint, SavedPatch snapshot=null)
        {
            const int pad=4, side=24;
            var origin=WorldCoordinates.DensityOrigin(key);
            var heights=new float[side,side];
            float low=float.PositiveInfinity, high=float.NegativeInfinity;
            if(snapshot==null) for(int x=0;x<side;x++) for(int z=0;z<side;z++)
            {
                var a=terrain.GetPosition(); var size=data.size;
                float u=(float)((origin.x+x-pad-a.x)/size.x), v=(float)((origin.z+z-pad-a.z)/size.z);
                float h;
                if(u>=0 && u<=1 && v>=0 && v<=1) h=data.GetInterpolatedHeight(u,v)+a.y;
                else if(!TerrainHeightSource.TrySample(new double3(origin.x+x-pad,0,origin.z+z-pad),default,out h)) return false;
                if(float.IsNaN(h)||float.IsInfinity(h)) return false;
                heights[x,z]=h; low=Mathf.Min(low,h); high=Mathf.Max(high,h);
            }
            int baseY=snapshot!=null?snapshot.baseY:Mathf.FloorToInt(low/16)*16-16;
            int topY=snapshot!=null?snapshot.topY:Mathf.CeilToInt(high/16)*16+16;
            Debug.Log($"[HybridDensityPatch] HEIGHTS: {low:F3} .. {high:F3}, base {baseY}, top {topY}");
            var config=GetComponent<DensityWorldConfig>(); if(!config) config=gameObject.AddComponent<DensityWorldConfig>();
            config.width=side; config.depth=side; config.height=topY-baseY+1; config.noDigBorderVoxels=0;
            var field=new DensitySurfaceField(config);
            if(snapshot==null) for(int x=0;x<side;x++) for(int z=0;z<side;z++)
            {
                float wx=field.GridToWorldX(x), wz=field.GridToWorldZ(z);
                field.surfaceCache[field.IDX(x,z)]=new List<SurfacePoint>(2){
                    new SurfacePoint{y=0,kind=SurfaceKind.Ceiling,realX=wx,realZ=wz},
                    new SurfacePoint{y=heights[x,z]-baseY,kind=SurfaceKind.Top,realX=wx,realZ=wz}};
            }
            if(snapshot!=null) {
                if(snapshot.columns.Count!=field.surfaceCache.Length) throw new Exception("Snapshot column count mismatch");
                for(int i=0;i<field.surfaceCache.Length;i++) { var col=snapshot.columns[i]; field.surfaceCache[i]=col.present?new List<SurfacePoint>(col.points ?? throw new Exception("Missing saved points")):null; }
                field.RestoreSharpCells(snapshot.sharpCells ?? Array.Empty<Vector3Int>(),0);
            }
            activeField=field; activeConfig=config; activeKey=key; activeBaseY=baseY; activeTopY=topY;
            if(digPoint.HasValue) {
                var p=digPoint.Value-new Vector3((float)origin.x+8,baseY,(float)origin.z+8);
                var result=new DensityDigOperation(field,config).DigSphere(p,3f);
                if(!result.Changed) throw new Exception("Brush did not change density");
                digCount=1; Debug.Log($"[HybridDig] Changed columns: {result.TouchedColumns.Count}");
            }
            patch=new GameObject("Hybrid Density Preview");
            patch.transform.SetParent(transform,false);
            patch.transform.position=new Vector3((float)origin.x+8,baseY,(float)origin.z+8);
            patch.SetActive(false);
            var sourceMaterial=terrain.materialTemplate;
            if(!sourceMaterial || !sourceMaterial.shader || !sourceMaterial.shader.isSupported)
                throw new Exception("Terrain material unavailable for Density");
            material=new Material(sourceMaterial);
            material.name="Hybrid Density - "+sourceMaterial.name;
            // The terrain hole mask must never clip the replacement mesh.
            if(material.HasProperty("_HybridHoleEnabled")) material.SetFloat("_HybridHoleEnabled",0f);
            Debug.Log("[HybridMaterial] Density uses "+material.shader.name);
            return BuildGeometry(field,key,baseY,topY,patch,meshes);
        }
        bool BuildGeometry(DensitySurfaceField field, WorldChunkKey key, int baseY, int topY, GameObject root, List<Mesh> output)
        {
            const int pad=4;
            var origin=WorldCoordinates.DensityOrigin(key);
            int vertices=0, triangles=0, sections=0;
            using(var pipeline=new DensityMeshPipeline(field))
            {
                for(int y=16;y<topY-baseY;y+=16)
                {
                    Debug.Log($"[HybridDensityPatch] MESH: section y={y}");
                    Mesh mesh=pipeline.BuildChunkMesh(pad-2,y,pad-2,pad+18,y+16,pad+18);
                    if(!mesh) continue;
                    var clipped=ClipAndStitch(mesh,origin,baseY,out var seam);
                    Destroy(mesh);
                    mesh=clipped;
                    if(!mesh) continue;
                    output.Add(mesh);
                    {
                        foreach(var v in mesh.vertices) if(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.x)||float.IsInfinity(v.y)||float.IsInfinity(v.z)) throw new Exception("Non-finite vertex");
                        vertices+=mesh.vertexCount; triangles+=(int)mesh.GetIndexCount(0)/3; sections++;
                    var child=new GameObject("Density section "+y);
                        child.transform.SetParent(root.transform,false);
                        child.AddComponent<MeshFilter>().sharedMesh=mesh;
                        child.AddComponent<MeshRenderer>().sharedMaterial=material;
                        child.AddComponent<MeshCollider>().sharedMesh=mesh;
                        if(seam) {
                            output.Add(seam);
                            var closure=new GameObject("Visual seam closure");
                            closure.transform.SetParent(child.transform,false);
                            closure.AddComponent<MeshFilter>().sharedMesh=seam;
                            closure.AddComponent<MeshRenderer>().sharedMaterial=material;
                        }
                    }
                }
            }
            if(vertices==0||triangles==0) throw new Exception("No surface geometry generated");
            Debug.Log($"[HybridDensityPatch] PASS: 16 x 16 footprint, {sections} surface sections, {vertices} vertices, {triangles} triangles; preview mesh prepared");
            return true;
        }
    }
}
