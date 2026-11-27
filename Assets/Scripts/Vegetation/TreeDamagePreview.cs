using UnityEngine;
using System.Collections;
using System.Collections.Generic;
namespace Unlimited.Vegetation
{
    public sealed class TreeDamagePreview : MonoBehaviour
    {
        public GameObject leaves;
        public GameObject wood;
        public byte State { get; private set; }
        [Min(0.01f)] public float treeScale = 2f;
        public float groundOffset = 0f;
        public float placementDistance = 5f;
        [HideInInspector] public bool forestInstance;
        [Min(0.1f)] public float fallDuration = 1.2f;
        [Min(0f)] public float fallenLifetime = 2f;
        private readonly RaycastHit[] groundHits = new RaycastHit[64];
        IEnumerator Start()
        {
            if (!wood || !leaves || forestInstance) yield break;
            transform.localScale = Vector3.one * treeScale;
            wood.SetActive(false); leaves.SetActive(false);
            var delay = new WaitForSecondsRealtime(0.25f);
            while (true)
            {
                var player = FindFirstObjectByType<PlayerController>();
                if (player)
                {
                    Vector3 p = player.transform.position + player.transform.forward * placementDistance;
                    Terrain best = null;
                    foreach (var t in Terrain.activeTerrains)
                    {
                        if (!t || !t.terrainData ) continue;
                        var a = t.GetPosition(); var s = t.terrainData.size;
                        if (p.x < a.x || p.z < a.z || p.x > a.x+s.x || p.z > a.z+s.z) continue;
                        if (!best || t.terrainData.heightmapResolution > best.terrainData.heightmapResolution) best = t;
                    }
                    if (best)
                    {
                        var a = best.GetPosition(); var s = best.terrainData.size;
                        p.y = a.y + best.terrainData.GetInterpolatedHeight((p.x-a.x)/s.x,(p.z-a.z)/s.z) + groundOffset;
                        transform.position = p;
                        wood.SetActive(true); leaves.SetActive(true);
                        Debug.Log("[Vegetation] Oak placed at " + p);
                        yield break;
                    }
                }
                yield return delay;
            }
        }
        [ContextMenu("Hit tree")]
        public void Hit()
        {
            if (State == 0) { State = 1; leaves.SetActive(false); }
            else if (State == 1)
            {
                State = 2;
                var lod = GetComponent<TreeDistanceLod>();
                if (lod) lod.RestoreAndLock();
                var player = FindFirstObjectByType<PlayerController>();
                Vector3 away = player ? transform.position - player.transform.position : transform.forward;
                away.y = 0f;
                if (away.sqrMagnitude < 0.0001f) away = Vector3.forward;
                StartCoroutine(Fall(away.normalized));
            }
        }
        // Controlled fall: retain the original mesh colliders and test the wood
        // against the actual streamed terrain/density colliders before each pose.
        IEnumerator Fall(Vector3 away)
        {
            var points = new List<Vector3>();
            var unique = new HashSet<Vector3>();
            Vector3 origin = transform.position;
            Quaternion initial = transform.rotation;
            foreach (var filter in wood.GetComponentsInChildren<MeshFilter>())
            {
                if (!filter.sharedMesh || !filter.sharedMesh.isReadable) continue;
                foreach (var vertex in filter.sharedMesh.vertices)
                {
                    Vector3 offset = filter.transform.TransformPoint(vertex) - origin;
                    if (unique.Add(offset)) points.Add(offset);
                }
            }
            if (points.Count == 0)
            {
                Debug.LogWarning("[Vegetation] Wood mesh must be readable for ground-safe falling.");
                yield return new WaitForSeconds(fallenLifetime);
                Destroy(gameObject);
                yield break;
            }
            Vector3 axis = Vector3.Cross(Vector3.up, away).normalized;
            float angle = 0f;
            float maxHeight = 0f;
            foreach (var point in points) maxHeight = Mathf.Max(maxHeight, point.magnitude);
            float duration = Mathf.Max(0.1f, fallDuration);
            // Fixed angular substeps prevent large frame jumps through the ground.
            while (angle < 90f)
            {
                float next = Mathf.Min(90f, angle + Mathf.Min(3f, 90f * Time.deltaTime / duration));
                Quaternion turn = Quaternion.AngleAxis(next, axis);
                float lift = 0f;
                float upperLift = 0f;
                foreach (var offset in points)
                {
                    Vector3 p = origin + turn * offset;
                    if (!TryGround(p, origin.y + maxHeight + 5f, out float ground)) continue;
                    float correction = ground + 0.015f - p.y;
                    lift = Mathf.Max(lift, correction);
                    if (offset.y > maxHeight * 0.25f) upperLift = Mathf.Max(upperLift, correction);
                }
                transform.SetPositionAndRotation(origin + Vector3.up * lift, turn * initial);
                Physics.SyncTransforms();
                angle = next;
                // Branches or the upper trunk have reached the surface.
                if (angle > 20f && upperLift > 0.025f && upperLift >= lift - 0.025f) break;
                yield return null;
            }
            yield return new WaitForSeconds(fallenLifetime);
            Destroy(gameObject);
        }

        bool TryGround(Vector3 point, float top, out float height)
        {
            height = float.NegativeInfinity;
            float start = Mathf.Max(top, point.y + 2f);
            int count = Physics.RaycastNonAlloc(new Vector3(point.x, start, point.z), Vector3.down,
                groundHits, start - point.y + 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (!hit.collider || hit.collider.transform.IsChildOf(transform)) continue;
                if (!(hit.collider is TerrainCollider) &&
                    !(hit.collider is MeshCollider && hit.collider.GetComponentInParent<Unlimited.HybridWorld.HybridDensityPatchProbe>())) continue;
                if (hit.normal.y <= 0.05f) continue;
                height = Mathf.Max(height, hit.point.y);
            }
            return !float.IsNegativeInfinity(height);
        }
    }
}
