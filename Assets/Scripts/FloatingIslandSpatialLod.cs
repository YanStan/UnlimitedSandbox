using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

// Island geometry uses the SAME spatial sizes and cell counts as TerrainGenerator.
// Tiles are built directly from the deterministic island shape; never cut from a whole-island mesh.
public sealed class FloatingIslandSpatialLod : MonoBehaviour
{
    public Transform player;
    public Material surfaceMaterial;
    public float radiusX, radiusZ, thickness, noiseScale, topVariation, bottomVariation;
    public int seed;
    public bool showGrid = true;
    public bool createColliders = true;
    public float fineDistance = 32f, fastDistance = 100f, colliderDistance = 70f;
    public int maxBuildsPerFrame = 3;
    [Header("Async Island Jobs")]
    [Min(1)] public int maxConcurrentBuilds = 4;
    [Min(1)] public int maxMeshUploadsPerFrame = 2;
    public int maxResidentTiles = 400;
    public Color fineColor = new Color(.04f,.3f,.1f), fastColor = new Color(.55f,.43f,.02f), superColor = new Color(.9f,.025f,.025f);

    private const int FineSize = 4, FastSize = 16, SuperSize = 64;
    private struct Key : IEquatable<Key> {
        public int x,z,lod;
        public Key(int x,int z,int lod){this.x=x;this.z=z;this.lod=lod;}
        public bool Equals(Key b) => x==b.x&&z==b.z&&lod==b.lod;
        public override bool Equals(object o)=>o is Key k&&Equals(k);
        public override int GetHashCode(){unchecked{return ((x*397)^z)*397^lod;}}
    }
    private sealed class Tile {
        public GameObject go,wireGo; public Mesh mesh,grid; public MeshRenderer renderer; public MeshCollider collider;
        public float lastUsed;
    }
    private readonly Dictionary<Key,Tile> tiles=new Dictionary<Key,Tile>();
    private readonly HashSet<Key> desired=new HashSet<Key>();
    private readonly List<Key> buildQueue=new List<Key>();
    private readonly List<Key> obsolete=new List<Key>();
    // Scratch buffers are reused by the synchronous mesh builder; no per-boundary-cell List allocations.
    private readonly List<Vector2> clippingPolygon=new List<Vector2>(8);
    private readonly List<Vector2> clippedPolygon=new List<Vector2>(8);
    private int buildQueueCursor;
    private Material gridFine,gridFast,gridSuper;
    private float nextPlan;
    // One in-flight tile: native buffers remain owned until its jobs complete.
    private sealed class PendingBuild
    {
        public Key key;
        public int cells, size, gridWidth;
        public float x0, z0, step;
        public NativeArray<byte> mask;
        public NativeArray<float> noise;
        public NativeArray<float2> heights;
        public JobHandle maskHandle, heightHandle;
        public bool IsCompleted => maskHandle.IsCompleted && heightHandle.IsCompleted;
        public void Complete() { maskHandle.Complete(); heightHandle.Complete(); }
        public void Dispose()
        {
            if (mask.IsCreated) mask.Dispose();
            if (noise.IsCreated) noise.Dispose();
            if (heights.IsCreated) heights.Dispose();
        }
    }
    private readonly List<PendingBuild> pendingBuilds = new List<PendingBuild>(8);
    private readonly HashSet<Key> pendingKeys = new HashSet<Key>();

    [BurstCompile]
    private struct IslandGridHeightJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> noiseValues;
        [WriteOnly] public NativeArray<float2> heights;
        public int width;
        public float x0, z0, step, centerX, centerZ;
        public float inverseRadiusX, inverseRadiusZ;
        public float thickness, topVariation, bottomVariation;

        public void Execute(int index)
        {
            int ix = index % width;
            int iz = index / width;
            float lx = (x0 + ix * step) - centerX;
            float lz = (z0 + iz * step) - centerZ;
            float nx = lx * inverseRadiusX;
            float nz = lz * inverseRadiusZ;
            float t = math.sqrt(nx * nx + nz * nz);
            float u = math.saturate(t);
            float rim = u * u * (3f - 2f * u);
            float n = noiseValues[index] - .5f;
            float top = topVariation * n * 2f + 2f * (1f - rim);
            float bottom = -thickness * (1f - .85f * rim) + bottomVariation * n * (1f - rim);
            heights[index] = new float2(top, bottom);
        }
    }

    [BurstCompile]
    private struct IslandInteriorMaskJob : IJobParallelFor
    {
        public int cells;
        public float x0, z0, step, centerX, centerZ, inverseRadiusX, inverseRadiusZ;
        [WriteOnly] public NativeArray<byte> mask;

        public void Execute(int index)
        {
            int ix = index % cells;
            int iz = index / cells;
            float x = x0 + ix * step;
            float z = z0 + iz * step;
            float2 a = new float2((x-centerX)*inverseRadiusX,(z-centerZ)*inverseRadiusZ);
            float2 b = new float2((x+step-centerX)*inverseRadiusX,(z-centerZ)*inverseRadiusZ);
            float2 c = new float2((x+step-centerX)*inverseRadiusX,(z+step-centerZ)*inverseRadiusZ);
            float2 d = new float2((x-centerX)*inverseRadiusX,(z+step-centerZ)*inverseRadiusZ);
            mask[index] = (byte)(math.lengthsq(a)<=1f && math.lengthsq(b)<=1f &&
                                 math.lengthsq(c)<=1f && math.lengthsq(d)<=1f ? 1 : 0);
        }
    }

    private void Awake(){
        if(player==null){var p=FindFirstObjectByType<PlayerController>();if(p!=null)player=p.transform;}
    }
    private void OnEnable(){nextPlan=0f;}
    private void Update(){
        if(player==null)return;
        if(Time.time>=nextPlan){nextPlan=Time.time+.15f;Plan();}
        // Harvest finished jobs without waiting on unfinished work.
        // Limit Mesh uploads because Unity mesh creation still runs on the main thread.
        int uploads = 0;
        for (int i = pendingBuilds.Count - 1; i >= 0; i--)
        {
            PendingBuild finished = pendingBuilds[i];
            if (!finished.IsCompleted) continue;
            pendingBuilds.RemoveAt(i);
            pendingKeys.Remove(finished.key);
            try
            {
                finished.Complete();
                if (desired.Contains(finished.key) && !tiles.ContainsKey(finished.key))
                {
                    FinishBuild(finished);
                    uploads++;
                }
            }
            finally { finished.Dispose(); }
            if (uploads >= Mathf.Max(1, maxMeshUploadsPerFrame)) break;
        }

        // Multiple independent tiles may be processed concurrently.
        // Never enqueue the same tile twice, even after Plan rebuilds the queue.
        int scheduled = 0;
        int maxConcurrent = Mathf.Max(1, maxConcurrentBuilds);
        int perFrame = Mathf.Max(1, maxBuildsPerFrame);
        while (pendingBuilds.Count < maxConcurrent &&
               scheduled < perFrame && buildQueueCursor < buildQueue.Count)
        {
            Key key = buildQueue[buildQueueCursor++];
            if (!desired.Contains(key) || tiles.ContainsKey(key) || pendingKeys.Contains(key))
                continue;
            ScheduleBuild(key);
            scheduled++;
        }
        // Never hide a parent before its entire 4x4 child region is ready.
        foreach(var kv in tiles){
            Key k=kv.Key; Tile t=kv.Value;
            bool visible=desired.Contains(k);
            if(visible&&k.lod>0&&AllChildrenReady(k))visible=false;
            if(visible&&k.lod<2){Key parent=new Key(Mathf.FloorToInt(k.x/4f),Mathf.FloorToInt(k.z/4f),k.lod+1);if(tiles.ContainsKey(parent)&&!AllChildrenReady(parent))visible=false;}
            if(t.renderer==null)continue;
            t.renderer.enabled=visible;
            if(t.wireGo!=null){bool wireVisible=visible&&showGrid;if(t.wireGo.activeSelf!=wireVisible)t.wireGo.SetActive(wireVisible);}
            if(t.collider!=null){
                float d=DistanceToTile(k,player.position);
                bool colliderOn=visible&&createColliders&&d<colliderDistance;
                if(t.collider.enabled!=colliderOn)t.collider.enabled=colliderOn;
            }
        }
    }
    private void Plan(){
        desired.Clear();
        Vector3 p=player.position;
        float minX=transform.position.x-radiusX-2f,maxX=transform.position.x+radiusX+2f;
        float minZ=transform.position.z-radiusZ-2f,maxZ=transform.position.z+radiusZ+2f;
        int sx=Mathf.FloorToInt(minX/SuperSize),ex=Mathf.FloorToInt(maxX/SuperSize);
        int sz=Mathf.FloorToInt(minZ/SuperSize),ez=Mathf.FloorToInt(maxZ/SuperSize);
        for(int z=sz;z<=ez;z++)for(int x=sx;x<=ex;x++){
            Key super=new Key(x,z,2);
            if(!IntersectsIsland(super))continue;
            desired.Add(super);
            if(DistanceToTile(super,p)>fastDistance+SuperSize)continue;
            for(int dz=0;dz<4;dz++)for(int dx=0;dx<4;dx++){
                Key fast=new Key(x*4+dx,z*4+dz,1);
                if(DistanceToTile(super,p)>fastDistance)continue;
                desired.Add(fast);
                if(DistanceToTile(fast,p)>fineDistance)continue;
                for(int fz=0;fz<4;fz++)for(int fx=0;fx<4;fx++){
                    Key fine=new Key(fast.x*4+fx,fast.z*4+fz,0);
                    desired.Add(fine);
                }
            }
        }
        obsolete.Clear();
        foreach(var kv in tiles)if(!desired.Contains(kv.Key))obsolete.Add(kv.Key);
        foreach(Key k in obsolete)Remove(k);
        buildQueue.Clear();
        buildQueueCursor=0;
        foreach(Key k in desired)if(!tiles.ContainsKey(k) && !pendingKeys.Contains(k))buildQueue.Add(k);
        buildQueue.Sort((a,b)=>{
            // Coarse coverage first, then nearby refinements.
            int aGroup=a.lod==2?0:a.lod==1?1:2;
            int bGroup=b.lod==2?0:b.lod==1?1:2;
            if(aGroup!=bGroup)return aGroup.CompareTo(bGroup);
            return DistanceToTile(a,p).CompareTo(DistanceToTile(b,p));
        });
        if(tiles.Count>maxResidentTiles)Debug.LogWarning("Island LOD tile budget exceeded; reduce fine/fast distances",this);
    }
    private bool AllChildrenReady(Key k){
        int childLod=k.lod-1;
        for(int z=0;z<4;z++)for(int x=0;x<4;x++){
            Key child=new Key(k.x*4+x,k.z*4+z,childLod);
            if(!desired.Contains(child)||!tiles.ContainsKey(child))return false;
        }
        return true;
    }
    private static int Size(int lod)=>lod==0?FineSize:lod==1?FastSize:SuperSize;
    private static int Cells(int lod)=>lod==0?16:4;
    private float DistanceToTile(Key k,Vector3 p){
        float size=Size(k.lod),x=k.x*size,z=k.z*size;
        float dx=Mathf.Max(x-p.x,0f,p.x-(x+size));
        float dz=Mathf.Max(z-p.z,0f,p.z-(z+size));
        float dy=Mathf.Max(0f,Mathf.Abs(p.y-transform.position.y)-thickness*.65f);
        return Mathf.Sqrt(dx*dx+dz*dz+dy*dy);
    }
    private bool IntersectsIsland(Key k){
        float s=Size(k.lod),cx=transform.position.x,cz=transform.position.z;
        float x=Mathf.Clamp(cx,k.x*s,(k.x+1)*s),z=Mathf.Clamp(cz,k.z*s,(k.z+1)*s);
        float nx=(x-cx)/Mathf.Max(1f,radiusX),nz=(z-cz)/Mathf.Max(1f,radiusZ);
        return nx*nx+nz*nz<=1.04f;
    }
    private float Shape(float x,float z,out float top,out float bottom){
        float lx=x-transform.position.x,lz=z-transform.position.z;
        float nx=lx/Mathf.Max(1f,radiusX),nz=lz/Mathf.Max(1f,radiusZ);
        float t=Mathf.Sqrt(nx*nx+nz*nz);
        // Keep Perlin coordinates small: 32-bit seeds multiplied directly by a float
        // can reach tens of millions, losing sub-metre coordinate precision.
        float offset = 10f + (unchecked((uint)seed) % 65521u) * 0.013f;
        float n=Mathf.PerlinNoise(lx*noiseScale+offset,lz*noiseScale+offset);
        float rim=Mathf.SmoothStep(0f,1f,Mathf.Clamp01(t));
        top=topVariation*(n-.5f)*2f+2f*(1f-rim);
        bottom=-thickness*(1f-.85f*rim)+bottomVariation*(n-.5f)*(1f-rim);
        return t;
    }
    private void ScheduleBuild(Key k)
    {
        int cells = Cells(k.lod), size = Size(k.lod);
        float step = (float)size / cells;
        float x0 = k.x * size, z0 = k.z * size;
        int width = cells + 1, count = width * width;
        var work = new PendingBuild {
            key = k, cells = cells, size = size, step = step,
            x0 = x0, z0 = z0, gridWidth = width,
            mask = new NativeArray<byte>(cells*cells, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
            noise = new NativeArray<float>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
            heights = new NativeArray<float2>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory)
        };
        try
        {
            // PerlinNoise must stay on the main thread for visual compatibility.
            float offset = 10f + (unchecked((uint)seed) % 65521u) * 0.013f;
            for (int iz=0;iz<width;iz++) for (int ix=0;ix<width;ix++)
            {
                float lx = (x0+ix*step)-transform.position.x;
                float lz = (z0+iz*step)-transform.position.z;
                work.noise[iz*width+ix] = Mathf.PerlinNoise(lx*noiseScale+offset,lz*noiseScale+offset);
            }
            work.maskHandle = new IslandInteriorMaskJob {
                cells=cells, x0=x0, z0=z0, step=step,
                centerX=transform.position.x, centerZ=transform.position.z,
                inverseRadiusX=1f/Mathf.Max(1f,radiusX), inverseRadiusZ=1f/Mathf.Max(1f,radiusZ),
                mask=work.mask
            }.Schedule(cells*cells,32);
            work.heightHandle = new IslandGridHeightJob {
                noiseValues=work.noise, heights=work.heights, width=width,
                x0=x0, z0=z0, step=step,
                centerX=transform.position.x, centerZ=transform.position.z,
                inverseRadiusX=1f/Mathf.Max(1f,radiusX), inverseRadiusZ=1f/Mathf.Max(1f,radiusZ),
                thickness=thickness, topVariation=topVariation, bottomVariation=bottomVariation
            }.Schedule(count,32);
            pendingBuilds.Add(work);
            pendingKeys.Add(k);
        }
        catch
        {
            work.Complete(); work.Dispose();
            throw;
        }
    }
    private void FinishBuild(PendingBuild work)
    {
        Key k = work.key;
        int cells = work.cells, size = work.size, gridWidth = work.gridWidth;
        float x0 = work.x0, z0 = work.z0, step = work.step;
        var v = new List<Vector3>((cells+1)*(cells+1)*2);
        var tris = new List<int>(cells*cells*12);
        int[,] topIndices=new int[gridWidth,gridWidth];
        int[,] bottomIndices=new int[gridWidth,gridWidth];
        for(int iz=0;iz<gridWidth;iz++)for(int ix=0;ix<gridWidth;ix++){
            topIndices[ix,iz]=-1;
            bottomIndices[ix,iz]=-1;
        }
        for(int iz=0;iz<cells;iz++)for(int ix=0;ix<cells;ix++){
            float x=x0+ix*step,z=z0+iz*step;
            Vector2 a=new Vector2(x,z);
            Vector2 b=new Vector2(x+step,z);
            Vector2 c=new Vector2(x+step,z+step);
            Vector2 d=new Vector2(x,z+step);
            if(work.mask[iz*cells+ix]!=0){
                // Interior: identical regular indexed-grid topology to terrain.
                int ta=GridVertex(ix,iz,false,topIndices,v,x0,z0,step,gridWidth,work.heights);
                int tb=GridVertex(ix+1,iz,false,topIndices,v,x0,z0,step,gridWidth,work.heights);
                int tc=GridVertex(ix+1,iz+1,false,topIndices,v,x0,z0,step,gridWidth,work.heights);
                int td=GridVertex(ix,iz+1,false,topIndices,v,x0,z0,step,gridWidth,work.heights);
                int ba=GridVertex(ix,iz,true,bottomIndices,v,x0,z0,step,gridWidth,work.heights);
                int bb=GridVertex(ix+1,iz,true,bottomIndices,v,x0,z0,step,gridWidth,work.heights);
                int bc=GridVertex(ix+1,iz+1,true,bottomIndices,v,x0,z0,step,gridWidth,work.heights);
                int bd=GridVertex(ix,iz+1,true,bottomIndices,v,x0,z0,step,gridWidth,work.heights);
                tris.Add(ta);tris.Add(tc);tris.Add(tb);
                tris.Add(ta);tris.Add(td);tris.Add(tc);
                tris.Add(ba);tris.Add(bb);tris.Add(bc);
                tris.Add(ba);tris.Add(bc);tris.Add(bd);
            }else{
                // Only the outermost cells need the closed island rim.
                AddClippedTriangle(v,tris,a,b,c,x0,z0,k.lod);
                AddClippedTriangle(v,tris,a,c,d,x0,z0,k.lod);
            }
        }
        // Empty tiles are still tracked so their parents can transition cleanly.
        if(tris.Count==0){tiles[k]=new Tile{lastUsed=Time.time};return;}
        var mesh=new Mesh{name="Island_"+k.lod+"_"+k.x+"_"+k.z};
        if(v.Count>65535)mesh.indexFormat=IndexFormat.UInt32;
        mesh.SetVertices(v);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
        var go=new GameObject(mesh.name);go.transform.SetParent(transform,false);
        go.transform.position=new Vector3(x0,transform.position.y,z0);
        var filter=go.AddComponent<MeshFilter>();filter.sharedMesh=mesh;
        var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=surfaceMaterial;
        MeshCollider col=null;
        if(createColliders){col=go.AddComponent<MeshCollider>();col.sharedMesh=mesh;col.enabled=false;}
        var tile=new Tile{go=go,mesh=mesh,renderer=renderer,collider=col,lastUsed=Time.time};
        if(showGrid){
            var grid=BuildWire(mesh);
            var wire=new GameObject("Real_Triangle_Wireframe");wire.transform.SetParent(go.transform,false);
            wire.AddComponent<MeshFilter>().sharedMesh=grid;
            wire.AddComponent<MeshRenderer>().sharedMaterial=GridMaterial(k.lod);
            tile.grid=grid;
            tile.wireGo=wire;
        }
        tiles[k]=tile;
    }
    private int GridVertex(int ix,int iz,bool lower,int[,] cache,
        List<Vector3> vertices,float x0,float z0,float step,int gridWidth,NativeArray<float2> heights){
        int cached=cache[ix,iz];
        if(cached>=0)return cached;
        float worldX=x0+ix*step,worldZ=z0+iz*step;
        float2 height=heights[iz*gridWidth+ix];
        int index=vertices.Count;
        vertices.Add(new Vector3(worldX-x0,lower?height.y:height.x,worldZ-z0));
        cache[ix,iz]=index;
        return index;
    }

    private void AddClippedTriangle(List<Vector3> v,List<int> tris,Vector2 a,Vector2 b,Vector2 c,float x0,float z0,int lod){
        var poly=clippingPolygon;
        poly.Clear();poly.Add(a);poly.Add(b);poly.Add(c);
        // Clipping with the ellipse boundary: edge intersections plus inside points.
        var clipped=clippedPolygon;
        clipped.Clear();
        for(int i=0;i<poly.Count;i++){
            Vector2 p=poly[i],q=poly[(i+1)%poly.Count];
            bool ip=Inside(p),iq=Inside(q);
            if(ip)clipped.Add(p);
            if(ip!=iq)clipped.Add(Intersection(p,q));
            else if(!ip&&!iq){ // both outside: edge may cross the ellipse twice
                Vector2 mid=(p+q)*.5f;
                if(Inside(mid)){clipped.Add(Intersection(p,mid));clipped.Add(Intersection(mid,q));}
            }
        }
        if(clipped.Count<3)return;
        for(int i=1;i+1<clipped.Count;i++){
            Vector2 p0=clipped[0],p1=clipped[i],p2=clipped[i+1];
            int baseIndex=v.Count;
            for(int surface=0;surface<2;surface++){
                for(int j=0;j<3;j++){
                    Vector2 point=j==0?p0:j==1?p1:p2;
                    float top,bottom;Shape(point.x,point.y,out top,out bottom);
                    v.Add(new Vector3(point.x-x0,surface==0?top:bottom,point.y-z0));
                }
                int triangleStart=baseIndex+surface*3;
                if(surface==0){tris.Add(triangleStart);tris.Add(triangleStart+2);tris.Add(triangleStart+1);}
                else {tris.Add(triangleStart);tris.Add(triangleStart+1);tris.Add(triangleStart+2);}
            }
            // Vertical wall on clipped edges lying at the ellipse perimeter.
            for(int j=0;j<3;j++){
                int next=(j+1)%3;
                Vector2 point=j==0?p0:j==1?p1:p2;
                Vector2 nextPoint=next==0?p0:next==1?p1:p2;
                if(!OnRim(point)||!OnRim(nextPoint))continue;
                int u=baseIndex+j,w=baseIndex+next,du=baseIndex+3+j,dw=baseIndex+3+next;
                tris.Add(u);tris.Add(w);tris.Add(du);
                tris.Add(w);tris.Add(dw);tris.Add(du);
            }
        }
    }
    // Every vertex samples the same deterministic world-space surface.
    // Do not force fine vertices to coarse interpolation on global grid lines:
    // that creates steep walls inside otherwise continuous fine regions.
    // Mixed-LOD seam stitching must be performed only on actual LOD transitions.

    private bool Inside(Vector2 p){float x=(p.x-transform.position.x)/Mathf.Max(1f,radiusX),z=(p.y-transform.position.z)/Mathf.Max(1f,radiusZ);return x*x+z*z<=1f;}
    private bool OnRim(Vector2 p){float x=(p.x-transform.position.x)/Mathf.Max(1f,radiusX),z=(p.y-transform.position.z)/Mathf.Max(1f,radiusZ);return Mathf.Abs(x*x+z*z-1f)<.001f;}
    private Vector2 Intersection(Vector2 a,Vector2 b){
        bool inside=Inside(a);Vector2 lo=a,hi=b;
        for(int i=0;i<16;i++){Vector2 m=(lo+hi)*.5f;if(Inside(m)==inside)lo=m;else hi=m;}
        return (lo+hi)*.5f;
    }
    private Mesh BuildWire(Mesh source){
        Vector3[] verts=source.vertices,normals=source.normals;
        var lifted=new Vector3[verts.Length];for(int i=0;i<verts.Length;i++)lifted[i]=verts[i]+normals[i]*.07f;
        int[] tri=source.triangles;var lines=new List<int>(tri.Length*2);var seen=new HashSet<ulong>();
        for(int i=0;i<tri.Length;i+=3){Edge(tri[i],tri[i+1],seen,lines);Edge(tri[i+1],tri[i+2],seen,lines);Edge(tri[i+2],tri[i],seen,lines);}
        var mesh=new Mesh{name="Island_Real_Wireframe"};if(verts.Length>65535)mesh.indexFormat=IndexFormat.UInt32;
        mesh.vertices=lifted;mesh.SetIndices(lines.ToArray(),MeshTopology.Lines,0);mesh.RecalculateBounds();return mesh;
    }
    private static void Edge(int a,int b,HashSet<ulong> seen,List<int> lines){uint lo=(uint)Mathf.Min(a,b),hi=(uint)Mathf.Max(a,b);ulong key=((ulong)lo<<32)|hi;if(seen.Add(key)){lines.Add(a);lines.Add(b);}}
    private Material GridMaterial(int lod){
        Material m=lod==0?gridFine:lod==1?gridFast:gridSuper;if(m!=null)return m;
        Shader shader=Shader.Find("Sprites/Default");if(shader==null)shader=Shader.Find("Universal Render Pipeline/Unlit");
        if(shader==null)return surfaceMaterial;
        m=new Material(shader){name="Island_Grid_LOD_"+lod};m.color=lod==0?fineColor:lod==1?fastColor:superColor;
        if(lod==0)gridFine=m;else if(lod==1)gridFast=m;else gridSuper=m;return m;
    }
    private void Remove(Key k){Tile t;if(!tiles.TryGetValue(k,out t))return;tiles.Remove(k);if(t.go!=null)Destroy(t.go);if(t.mesh!=null)Destroy(t.mesh);if(t.grid!=null)Destroy(t.grid);}
    private void OnDestroy(){
        foreach (PendingBuild work in pendingBuilds)
        {
            work.Complete();
            work.Dispose();
        }
        pendingBuilds.Clear();
        pendingKeys.Clear();
        // Unity destroys children with their parent; explicitly release generated Mesh assets.
        foreach(var pair in tiles){
            Tile t=pair.Value;
            if(t.mesh!=null)Destroy(t.mesh);
            if(t.grid!=null)Destroy(t.grid);
        }
        tiles.Clear();
        if(gridFine!=null)Destroy(gridFine);
        if(gridFast!=null)Destroy(gridFast);
        if(gridSuper!=null)Destroy(gridSuper);
    }
}
