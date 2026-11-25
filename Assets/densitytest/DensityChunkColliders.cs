
using System.Collections;
using UnityEngine;

namespace Unlimited.DensityTest
{
    [RequireComponent(typeof(DensityChunkTest))]
    public sealed class DensityChunkColliders : MonoBehaviour
    {
        [Header("Colliders")]

        public bool generateColliders = true;

        [Min(1)]
        public int collidersPerFrame = 4;

        public bool useAdaptiveFrameBudget = true;
        public bool generateImmediatelyOnStartup = true;
        [Min(1f)] public float generationBudgetMs = 12f;

        private DensityChunkTest chunkGenerator;

        public bool IsReady { get; private set; }

        private void Awake()
        {
            var spawner = GetComponent<DensityPlayerSpawner>();
            if (spawner != null && spawner.isActiveAndEnabled && spawner.player != null && !generateColliders)
            {
                generateColliders = true;
                Debug.LogWarning("[DensityColliders] Enabled because player spawning requires collision.");
            }
        }

        private IEnumerator Start()
        {
            IsReady = false;

            var spawner = GetComponent<DensityPlayerSpawner>();
            if (spawner != null && spawner.isActiveAndEnabled && spawner.player != null)
                generateColliders = true;
            if (!generateColliders)
                yield break;

            chunkGenerator =
                GetComponent<DensityChunkTest>();

            while (!chunkGenerator.IsGenerationComplete)
            {
                yield return null;
            }

            Debug.Log("[DensityStartup] Collider generation started");
            yield return GenerateColliders();

            IsReady = true;
        }

        private IEnumerator GenerateColliders()
        {
            var timer =
                System.Diagnostics.Stopwatch.StartNew();

            MeshFilter[] filters =
                GetComponentsInChildren<MeshFilter>();

            int totalColliders = 0;
            int totalTriangles = 0;

            int generatedThisFrame = 0;
            int yieldedFrames = 0;
            var frameTimer = System.Diagnostics.Stopwatch.StartNew();

            double colliderBuildTime = 0.0;

            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh == null)
                    continue;

                if (!filter.gameObject.name.StartsWith(
                    "DensityChunk_"))
                {
                    continue;
                }

                var buildTimer =
                    System.Diagnostics.Stopwatch.StartNew();

                UpdateChunkCollider(filter);

                buildTimer.Stop();

                colliderBuildTime +=
                    buildTimer.Elapsed.TotalMilliseconds;

                totalColliders++;

                totalTriangles +=
                    (int)(filter.sharedMesh.GetIndexCount(0) / 3);

                generatedThisFrame++;


            }

            timer.Stop();

            Debug.Log(
                "[DensityColliders] Generation completed\n" +

                $"Colliders: {totalColliders}\n" +

                $"Collision triangles: {totalTriangles}\n" +

                $"Collider build time: {colliderBuildTime:F2} ms\n" +

                $"Yielded frames: {yieldedFrames}\n" +
                $"Total time: {timer.Elapsed.TotalMilliseconds:F2} ms"
            );
            yield break;
        }

        public void UpdateChunkCollider(
            MeshFilter filter)
        {
            if (filter == null)
                return;

            MeshCollider meshCollider =
                filter.GetComponent<MeshCollider>();

            if (filter.sharedMesh == null)
            {
                if (meshCollider != null)
                    meshCollider.sharedMesh = null;

                return;
            }

            if (meshCollider == null)
            {
                meshCollider =
                    filter.gameObject.AddComponent<MeshCollider>();
            }

            meshCollider.sharedMesh = null;

            meshCollider.convex = false;

            meshCollider.isTrigger = false;

            meshCollider.cookingOptions =
                MeshColliderCookingOptions.CookForFasterSimulation |

                MeshColliderCookingOptions.EnableMeshCleaning |

                MeshColliderCookingOptions.WeldColocatedVertices |

                MeshColliderCookingOptions.UseFastMidphase;

            meshCollider.sharedMesh =
                filter.sharedMesh;
        }

        public void RemoveChunkCollider(
            GameObject chunkObject)
        {
            if (chunkObject == null)
                return;

            MeshCollider meshCollider =
                chunkObject.GetComponent<MeshCollider>();

            if (meshCollider == null)
                return;

            meshCollider.sharedMesh = null;

            Destroy(meshCollider);
        }
    }
}
