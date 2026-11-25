using UnityEngine;

namespace Unlimited.HybridWorld
{
    public sealed class FloatingOriginPreviewBehaviour : MonoBehaviour
    {
        internal Transform player;
        private Vector3 lastShift;
        private void LateUpdate()
        {
            if (!player) return;
            var shift = FloatingOriginState.RequiredShift(player.position);
            if (shift == lastShift) return;
            lastShift = shift;
            Debug.Log($"[HybridFloatingOrigin] Preview: player={player.position}, candidate shift={shift}; no transforms moved");
        }
    }
}
