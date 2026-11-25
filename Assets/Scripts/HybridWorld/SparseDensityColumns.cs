using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Unlimited.HybridWorld
{
    // Storage only. Coordinates in each column are relative to its 16m section.
    // No synthetic surfaces at section boundaries: spans are clipped when queried.
    public sealed class SparseDensityColumns
    {
        public struct Span
        {
            public readonly double Bottom, Top;
            public Span(double bottom, double top) { Bottom = bottom; Top = top; }
        }
        public delegate bool HeightReader(long x, long z, out double height);
        private readonly HeightReader readHeight;
        private readonly Dictionary<WorldChunkKey, Dictionary<int, Span[]>> chunks = new Dictionary<WorldChunkKey, Dictionary<int, Span[]>>();
        public int ChunkCount => chunks.Count;
        public SparseDensityColumns(HeightReader reader) { readHeight = reader ?? throw new ArgumentNullException(nameof(reader)); }
        public static WorldChunkKey Key(long x, long sectionY, long z) => new WorldChunkKey(WorldCoordinates.FloorDiv(x,16), sectionY, WorldCoordinates.FloorDiv(z,16));
        private static int Slot(long x,long z) => WorldCoordinates.FloorMod(x,16)*16+WorldCoordinates.FloorMod(z,16);
        // Missing source is not air. False means retry when the terrain is ready.
        public bool TryRead(long x,long sectionY,long z,out Span[] spans)
        {
            if (chunks.TryGetValue(Key(x,sectionY,z),out var columns) && columns.TryGetValue(Slot(x,z),out var stored))
            { spans=(Span[])stored.Clone(); return true; }
            spans=null;
            if (!readHeight(x,z,out double height) || double.IsNaN(height) || double.IsInfinity(height)) return false;
            double top=Math.Max(0,Math.Min(16,height-(double)sectionY*16));
            spans=top>0 ? new[]{new Span(0,top)} : Array.Empty<Span>();
            return true;
        }
        public void Write(long x,long sectionY,long z,Span[] spans)
        {
            if (spans==null) throw new ArgumentNullException(nameof(spans));
            double last=0;
            foreach(var s in spans)
            {
                if (double.IsNaN(s.Bottom)||double.IsNaN(s.Top)||s.Bottom<last||s.Top<=s.Bottom||s.Top>16) throw new ArgumentException("Invalid section spans");
                last=s.Top;
            }
            var key=Key(x,sectionY,z);
            if(!chunks.TryGetValue(key,out var columns)) { columns=new Dictionary<int,Span[]>(); chunks.Add(key,columns); }
            columns[Slot(x,z)]=(Span[])spans.Clone();
        }
        public bool RemoveChunk(WorldChunkKey key) => chunks.Remove(key);
        public static SparseDensityColumns FromTerrain(double3 origin) => new SparseDensityColumns((long x,long z,out double h)=>
        {
            bool ok=TerrainHeightSource.TrySample(new double3(x,0,z),origin,out float localHeight);
            h=localHeight+origin.y; return ok;
        });
    }
}
