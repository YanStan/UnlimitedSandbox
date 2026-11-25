
using System.Collections;
using UnityEngine;

namespace Unlimited.DensityTest
{
    [RequireComponent(typeof(DensityChunkColliders))]
    public sealed class DensityPlayerSpawner : MonoBehaviour
    {
        [Header("Player")]

        public GameObject player;

        [Header("Spawn Position")]

        public float spawnX = 0f;
        public float spawnZ = 0f;

        [Min(0.1f)]
        public float spawnOffset = 2f;

        public bool IsSpawned { get; private set; }

        private DensityChunkColliders chunkColliders;
        private DensityWorldConfig config;

        private void Awake()
        {
            IsSpawned = false;

            if (player != null &&
                player != gameObject &&
                !transform.IsChildOf(player.transform))
            {
                player.SetActive(false);
            }
        }

        private IEnumerator Start()
        {
            if (player == null)
            {
                Debug.LogError(
                    "[DensitySpawn] Player is not assigned."
                );

                yield break;
            }

            if (player == gameObject ||
                transform.IsChildOf(player.transform))
            {
                Debug.LogError(
                    "[DensitySpawn] Invalid player object."
                );

                yield break;
            }

            chunkColliders =
                GetComponent<DensityChunkColliders>();

            config =
                GetComponent<DensityWorldConfig>();

            if (!chunkColliders.isActiveAndEnabled)
            {
                Debug.LogError("[DensitySpawn] Collider component is inactive.");
                yield break;
            }
            chunkColliders.generateColliders = true;

            while (!chunkColliders.IsReady)
            {
                yield return null;
            }

            Physics.SyncTransforms();

            SpawnPlayer();
        }

        public void SpawnPlayer()
        {
            if (player == null || config == null)
                return;

            if (chunkColliders == null ||
                !chunkColliders.IsReady)
            {
                return;
            }

            Vector3 rayOrigin =
                transform.TransformPoint(
                    new Vector3(
                        spawnX,
                        config.height + 10f,
                        spawnZ
                    )
                );

            Vector3 rayDirection =
                -transform.up;

            float rayDistance =
                (config.height + 20f) *
                Mathf.Abs(transform.lossyScale.y);

            RaycastHit[] hits = Physics.RaycastAll(
                rayOrigin,
                rayDirection,
                rayDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore
            );

            bool foundGround = false;

            RaycastHit groundHit = default;

            float nearestDistance =
                float.PositiveInfinity;

            foreach (RaycastHit hit in hits)
            {
                if (!hit.collider.transform.IsChildOf(
                    transform))
                {
                    continue;
                }

                if (!hit.collider.gameObject.name.StartsWith(
                    "DensityChunk_"))
                {
                    continue;
                }

                if (hit.distance >= nearestDistance)
                    continue;

                nearestDistance = hit.distance;

                groundHit = hit;

                foundGround = true;
            }

            if (!foundGround)
            {
                Debug.LogError(
                    "[DensitySpawn] Ground was not found."
                );

                return;
            }

            Vector3 spawnPosition =
                groundHit.point +
                transform.up * spawnOffset;

            CharacterController characterController =
                player.GetComponent<CharacterController>();

            bool controllerWasEnabled =
                characterController != null &&
                characterController.enabled;

            if (characterController != null)
                characterController.enabled = false;

            player.transform.position =
                spawnPosition;

            if (characterController != null)
            {
                characterController.enabled =
                    controllerWasEnabled;
            }

            player.SetActive(true);

            Physics.SyncTransforms();

            IsSpawned = true;

            Debug.Log(
                "[DensitySpawn] Player spawned\n" +

                $"Ground position: {groundHit.point}\n" +

                $"Player position: {spawnPosition}\n" +

                $"Ground normal: {groundHit.normal}\n" +

                $"Chunk: {groundHit.collider.gameObject.name}"
            );
        }
    }
}
