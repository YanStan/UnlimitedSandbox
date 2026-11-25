using UnityEngine;

namespace Unlimited.HybridWorld
{
    // Read-only diagnostic: deliberately does not move MapMagic or player.
    public static class FloatingOriginPreview
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var player = Object.FindFirstObjectByType<global::PlayerController>();
            if (player == null) return;
            var go = new GameObject("Hybrid Floating Origin Preview");
            var preview = go.AddComponent<FloatingOriginPreviewBehaviour>();
            preview.player = player.transform;
        }
    }
}
