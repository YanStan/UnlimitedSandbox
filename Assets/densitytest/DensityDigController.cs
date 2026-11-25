
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unlimited.DensityTest
{
    public enum DensityBrushShape
    {
        [InspectorName("Hemisphere")] Hemisphere = 0,
        [InspectorName("Sphere")] Sphere = 2,
        [InspectorName("Smooth Cube")] SmoothCube = 1,
        [InspectorName("Sharp Cube")] SharpCube = 3
    }

    public sealed class DensityDigController : MonoBehaviour
    {
        [Header("References")]

        public DensityWorldController worldController;

        public Camera playerCamera;

        [Header("Digging")]

        public DensityBrushShape brushShape = DensityBrushShape.Hemisphere;

        [Min(0.1f)]
        public float maxDigDistance = 10f;

        [Range(0f, 1f)]
        public float digStrength = 0f;

        [Range(0f, 1f)]
        public float angleFactor = 0.25f;

        [Header("Debug")]

        public bool showDebugGizmos = true;

        public bool logDigHits = true;

        private DensityWorldConfig config;

        private DensitySurfaceField field;

        private DensityChunkTest chunkGenerator;

        private DensityChunkColliders chunkColliders;

        private DensityNormalSmoother normalSmoother;

        private DensityDigOperation digOperation;

        private DensityDirtySections dirtySections;

        private DensityBowlOperation currentBowl;
        private Bounds currentBox;
        private Bounds buildBox;
        private DensityBowlOperation buildBowl;

        private bool hasTarget;

        private bool canDig;

        private bool isDigging;

        private Vector3 hitPosition;

        private Vector3 hitNormal;

        private Vector3Int gridPosition;

        private Vector3Int chunkPosition;

        private void OnEnable()
        {
            Debug.Log("[DensityDig] Four Brushes FIX loaded: Hemisphere, Sphere, Smooth Cube, Sharp Cube", this);
        }

        private void UpdateBrushSelection()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            DensityBrushShape next = brushShape;
            if (keyboard.digit1Key.wasPressedThisFrame) next = DensityBrushShape.Hemisphere;
            else if (keyboard.digit2Key.wasPressedThisFrame) next = DensityBrushShape.Sphere;
            else if (keyboard.digit3Key.wasPressedThisFrame) next = DensityBrushShape.SmoothCube;
            else if (keyboard.digit4Key.wasPressedThisFrame) next = DensityBrushShape.SharpCube;
            if (next == brushShape) return;
            brushShape = next;
            Debug.Log($"[DensityDig] Brush: {brushShape}", this);
        }

        private void Update()
        {
            UpdateBrushSelection();
            hasTarget = false;

            canDig = false;

            if (isDigging)
                return;

            if (!InitializeReferences())
                return;

            if (!chunkGenerator.IsGenerationComplete)
                return;

            if (chunkColliders != null &&
                !chunkColliders.IsReady)
            {
                return;
            }

            UpdateDigTarget();

            if (!hasTarget || !canDig)
                return;

            if (Mouse.current == null)
                return;

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                ProcessDig(false);
            }
            else if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                ProcessDig(true);
            }
        }

        private bool InitializeReferences()
        {
            if (worldController == null)
            {
                worldController =
                    FindFirstObjectByType<
                        DensityWorldController
                    >();
            }

            if (worldController == null)
                return false;

            if (playerCamera == null)
            {
                playerCamera =
                    GetComponentInChildren<Camera>();
            }

            if (playerCamera == null)
                return false;

            if (config == null)
            {
                config = worldController
                    .GetComponent<DensityWorldConfig>();
            }

            if (chunkGenerator == null)
            {
                chunkGenerator = worldController
                    .GetComponent<DensityChunkTest>();
            }

            if (chunkColliders == null)
            {
                chunkColliders = worldController
                    .GetComponent<DensityChunkColliders>();
            }

            if (normalSmoother == null)
            {
                normalSmoother = worldController
                    .GetComponent<DensityNormalSmoother>();
            }

            DensitySurfaceField currentField =
                worldController.SurfaceField;

            if (config == null ||
                currentField == null ||
                chunkGenerator == null)
            {
                return false;
            }

            if (field != currentField ||
                digOperation == null ||
                dirtySections == null)
            {
                field = currentField;

                digOperation =
                    new DensityDigOperation(
                        field,
                        config
                    );

                dirtySections =
                    new DensityDirtySections(
                        field,
                        config
                    );
            }

            return true;
        }

        private void UpdateDigTarget()
        {
            Ray ray = new Ray(
                playerCamera.transform.position,
                playerCamera.transform.forward
            );

            if (!Physics.Raycast(
                ray,
                out RaycastHit hit,
                maxDigDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
            {
                return;
            }

            if (!hit.collider.transform.IsChildOf(
                worldController.transform))
            {
                return;
            }

            if (!hit.collider.gameObject.name.StartsWith(
                "DensityChunk_"))
            {
                return;
            }

            hasTarget = true;

            hitPosition = hit.point;

            hitNormal = hit.normal;

            Transform worldTransform =
                worldController.transform;

            Vector3 localHit =
                worldTransform.InverseTransformPoint(
                    hit.point
                );

            Vector3 localDirection =
                worldTransform.InverseTransformVector(
                    ray.direction
                ).normalized;

            int gridX =
                field.WorldToGridX(localHit.x);

            int gridY =
                Mathf.FloorToInt(localHit.y);

            int gridZ =
                field.WorldToGridZ(localHit.z);

            gridPosition = new Vector3Int(
                gridX,
                gridY,
                gridZ
            );

            chunkPosition = new Vector3Int(
                Mathf.FloorToInt(
                    gridX / (float)config.chunkSize
                ),

                Mathf.FloorToInt(
                    gridY / (float)config.sectionHeight
                ),

                Mathf.FloorToInt(
                    gridZ / (float)config.chunkSize
                )
            );

            currentBowl =
                DensityDigShape.ComputeBowl(
                    localHit,
                    localDirection,
                    config,
                    false,
                    digStrength,
                    angleFactor
                );

            if (brushShape == DensityBrushShape.Sphere)
            {
                currentBowl = new DensityBowlOperation
                {
                    center = localHit,
                    radius = 3f,
                    depth = 3f
                };
            }

            Vector3 normal = worldTransform.localToWorldMatrix.transpose
                .MultiplyVector(hit.normal).normalized;
            Vector3 axis = Vector3.up * Mathf.Sign(normal.y);
            Vector3 size = new Vector3(4f, 1f, 4f);
            if (Mathf.Abs(normal.x) >= Mathf.Abs(normal.y) &&
                Mathf.Abs(normal.x) >= Mathf.Abs(normal.z))
            {
                axis = Vector3.right * Mathf.Sign(normal.x);
                size = new Vector3(1f, 4f, 4f);
            }
            else if (Mathf.Abs(normal.z) > Mathf.Abs(normal.y))
            {
                axis = Vector3.forward * Mathf.Sign(normal.z);
                size = new Vector3(4f, 4f, 1f);
            }
            currentBox = new Bounds(localHit - axis * 0.5f, size);
            // Overlap the target to avoid a zero-width contact at the sample grid.
            buildBox = new Bounds(localHit + axis * 0.25f, size);
            buildBowl = currentBowl;
            buildBowl.center = brushShape == DensityBrushShape.Sphere
                ? localHit + normal * (currentBowl.radius * 0.5f)
                : localHit - normal * Mathf.Max(0f, currentBowl.radius - currentBowl.depth);

            if (!field.InBounds(gridX, gridZ))
                return;

            if (gridY < 0 || gridY >= field.H)
                return;

            if (field.IsNoDigBorderColumn(
                gridX,
                gridZ))
            {
                return;
            }

            canDig = true;
        }

        private void ProcessDig(bool build)
        {
            if (isDigging)
                return;

            isDigging = true;

            var totalTimer =
                System.Diagnostics.Stopwatch.StartNew();

            try
            {
                CharacterController player = build && playerCamera != null
                    ? playerCamera.GetComponentInParent<CharacterController>() : null;
                Bounds brushBounds = brushShape == DensityBrushShape.SmoothCube ||
                    brushShape == DensityBrushShape.SharpCube
                    ? buildBox : new Bounds(buildBowl.center, Vector3.one * (2f * buildBowl.radius));
                Bounds oldPlayerBounds = player != null ? player.bounds : default;
                dirtySections.Clear();

                var digTimer =
                    System.Diagnostics.Stopwatch.StartNew();

                DensityDigResult result = build
                    ? (brushShape == DensityBrushShape.SmoothCube
                        ? digOperation.BuildBox(buildBox, 0.5f)
                        : brushShape == DensityBrushShape.SharpCube
                            ? digOperation.BuildBox(buildBox)
                            : digOperation.BuildSphere(buildBowl.center, buildBowl.radius))
                    : brushShape == DensityBrushShape.SmoothCube
                        ? digOperation.DigBox(currentBox, 0.5f)
                        : brushShape == DensityBrushShape.SharpCube
                            ? digOperation.DigBox(currentBox)
                            : digOperation.DigSphere(currentBowl.center, currentBowl.radius);

                digTimer.Stop();

                if (!result.Changed)
                {
                    if (logDigHits)
                    {
                        Debug.Log(
                            "[DensityDig] No terrain changes"
                        );
                    }

                    return;
                }

                field.MarkBrushCells(result, brushShape == DensityBrushShape.SharpCube);
                dirtySections.MarkDigResult(result);

                List<Vector3Int> sections =
                    dirtySections.Consume();

                var rebuildTimer =
                    System.Diagnostics.Stopwatch.StartNew();

                chunkGenerator.RebuildSections(
                    sections, result.TouchedColumns
                );

                rebuildTimer.Stop();

                var normalsTimer =
                    System.Diagnostics.Stopwatch.StartNew();

                if (normalSmoother != null)
                {
                    normalSmoother.SmoothLocalNormals(sections);
                }

                normalsTimer.Stop();

                if (build && player != null && player.enabled)
                    LiftPlayerAboveBuild(player, oldPlayerBounds, brushBounds);

                totalTimer.Stop();

                if (logDigHits)
                {
                    Debug.Log(
                        $"[DensityDig] {(build ? "Build" : "Dig")} completed\n" +

                        $"Hit position: {hitPosition}\n" +

                        $"Grid position: {gridPosition}\n" +

                        $"Chunk position: {chunkPosition}\n" +

                        (brushShape == DensityBrushShape.SmoothCube || brushShape == DensityBrushShape.SharpCube
                            ? $"Box center: {(build ? buildBox : currentBox).center}\nBox size: {currentBox.size}\n"
                            : $"Sphere center: {(build ? buildBowl : currentBowl).center}\nBrush radius: {currentBowl.radius:F2} m\nDig depth: {currentBowl.depth:F2} m\n") +

                        $"Changed columns: {result.TouchedColumns.Count}\n" +

                        $"Dirty sections: {sections.Count}\n" +

                        $"Brush operation: {digTimer.Elapsed.TotalMilliseconds:F2} ms\n" +

                        $"Mesh and collider rebuild: {rebuildTimer.Elapsed.TotalMilliseconds:F2} ms\n" +

                        $"Normal smoothing: {normalsTimer.Elapsed.TotalMilliseconds:F2} ms\n" +

                        $"Total edit time: {totalTimer.Elapsed.TotalMilliseconds:F2} ms"
                    );
                }
            }
            finally
            {
                isDigging = false;
            }
        }

        // A newly built solid can enclose the existing character capsule.
        // MeshCollider contacts cannot reliably resolve a capsule already inside a solid.
        private void LiftPlayerAboveBuild(CharacterController player, Bounds oldBounds, Bounds brush)
        {
            Transform world = worldController.transform;
            Vector3 feet = new Vector3(oldBounds.center.x, oldBounds.min.y, oldBounds.center.z);
            Vector3 localFeet = world.InverseTransformPoint(feet);
            float margin = Mathf.Max(oldBounds.extents.x, oldBounds.extents.z);
            float localMargin = margin / Mathf.Max(0.001f,
                Mathf.Min(Mathf.Abs(world.lossyScale.x), Mathf.Abs(world.lossyScale.z)));
            if (localFeet.x < brush.min.x - localMargin || localFeet.x > brush.max.x + localMargin ||
                localFeet.z < brush.min.z - localMargin || localFeet.z > brush.max.z + localMargin ||
                hitPosition.y > oldBounds.min.y + 0.5f)
                return;

            // Bound the probe to the edited region, rather than scanning the world.
            float top = world.TransformPoint(new Vector3(localFeet.x, brush.max.y, localFeet.z)).y;
            float padding = Mathf.Max(0.25f, Mathf.Abs(world.lossyScale.y));
            if (top + padding <= feet.y) return;
            Physics.SyncTransforms();
            float originY = top + padding;
            var hits = Physics.RaycastAll(new Vector3(feet.x, originY, feet.z), Vector3.down,
                originY - feet.y + 0.05f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float surface = float.NegativeInfinity;
            foreach (var hit in hits)
            {
                if (!hit.collider.transform.IsChildOf(world) ||
                    !hit.collider.gameObject.name.StartsWith("DensityChunk_") || hit.normal.y <= 0.05f)
                    continue;
                surface = Mathf.Max(surface, hit.point.y);
            }
            if (surface <= feet.y + 0.01f || float.IsNegativeInfinity(surface)) return;

            float clearance = Mathf.Max(0.06f, player.skinWidth * 2f);
            float lift = surface - feet.y + clearance;
            float radius = Mathf.Max(0.01f, Mathf.Min(oldBounds.extents.x, oldBounds.extents.z) - 0.02f);
            Vector3 center = oldBounds.center + Vector3.up * lift;
            float halfSegment = Mathf.Max(0f, oldBounds.extents.y - radius);
            var overlaps = Physics.OverlapCapsule(center - Vector3.up * halfSegment,
                center + Vector3.up * halfSegment, radius, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            foreach (var overlap in overlaps)
                if (overlap != player && !overlap.transform.IsChildOf(player.transform))
                    return; // Do not teleport into a ceiling or another obstacle.

            player.enabled = false;
            player.transform.position += Vector3.up * lift;
            player.enabled = true;
            Physics.SyncTransforms();
            if (logDigHits) Debug.Log($"[DensityBuild] Player lifted {lift:F2} m above new surface");
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying)
                return;

            if (!showDebugGizmos || !hasTarget)
                return;

            if (config == null ||
                worldController == null)
            {
                return;
            }

            Matrix4x4 previousMatrix =
                Gizmos.matrix;

            Gizmos.matrix =
                worldController.transform
                    .localToWorldMatrix;

            Gizmos.color = canDig ? Color.green : Color.red;

            if (brushShape == DensityBrushShape.SmoothCube ||
                brushShape == DensityBrushShape.SharpCube)
            {
                Gizmos.DrawWireCube(
                    currentBox.center,
                    currentBox.size
                );
            }
            else
            {
                Gizmos.DrawWireSphere(
                    currentBowl.center,
                    currentBowl.radius
                );
            }

            Gizmos.matrix = previousMatrix;

            Gizmos.color = Color.yellow;

            Gizmos.DrawSphere(
                hitPosition,
                0.08f
            );

            Gizmos.color = Color.cyan;

            Gizmos.DrawLine(
                hitPosition,
                hitPosition + hitNormal * 1.5f
            );

            if (playerCamera != null)
            {
                Gizmos.color = Color.white;

                Gizmos.DrawLine(
                    playerCamera.transform.position,
                    hitPosition
                );
            }
        }
    }
}
