
using UnityEngine;

namespace Unlimited.DensityTest
{
    public class DensityWorldConfig : MonoBehaviour
    {
        [Header("World Size")]
        public int width = 128;
        public int depth = 128;
        public int height = 350;

        [Header("Density Chunks")]
        public int chunkSize = 16;
        public int sectionHeight = 16;
        public float densityStep = 1f;

        [Header("Terrain")]
        public int seed = 1337;
        public int stoneLevel = 230;
        public int terrainBaseLevel = 260;
        public int topsoilDepth = 30;
        public int edgeBlendWidth = 56;
        public int edgeFlatLevel = 260;

        [Header("Digging")]
        public float digRadius = 3.2f;
        public float minDigDepth = 0.82f;
        public float maxDigDepth = 0.96f;
        public float digExpandMargin = 0.18f;

        [Header("Density Cleanup")]
        public float minSolidIntervalHeight = 1.45f;
        public float mergeGapEps = 0.75f;
        public float minCavitySliceHeight = 0.55f;
        public int noDigBorderVoxels = 8;

        [Header("Wall Cut")]
        public float wallCutEpsY = 0.35f;
        public float wallCutEpsXZ = 0.2f;
    }
}

