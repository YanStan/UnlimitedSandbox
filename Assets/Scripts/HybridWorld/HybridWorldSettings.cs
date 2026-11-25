using UnityEngine;

namespace Unlimited.HybridWorld
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Unlimited/Hybrid World Settings")]
    public sealed class HybridWorldSettings : MonoBehaviour
    {
        [Header("Brush sizes (metres)")]
        [Min(0.5f)] public float sphereRadius = 3f;
        [Min(0.5f)] public float hemisphereRadius = 3f;
        [Min(1f)] public float smoothCubeSize = 2f;
        [Min(1f)] public float sharpCubeSize = 2f;
        [Min(0f)] public float smoothCubeRounding = 0.5f;
    }
}
