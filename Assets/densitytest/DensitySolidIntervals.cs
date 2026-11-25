
using System.Collections.Generic;
using UnityEngine;

namespace Unlimited.DensityTest
{
    public struct DensitySolidInterval
    {
        public float bottom;
        public float top;

        public DensitySolidInterval(
            float bottom,
            float top)
        {
            this.bottom = bottom;
            this.top = top;
        }
    }

    public static class DensitySolidIntervals
    {
        private const float Epsilon = 0.000001f;

        public static List<DensitySolidInterval>
            ColumnToIntervals(
                List<SurfacePoint> column)
        {
            var intervals =
                new List<DensitySolidInterval>();

            if (column == null)
                return intervals;

            float currentBottom = 0f;

            bool hasBottom = false;

            foreach (SurfacePoint point in column)
            {
                if (point.kind == SurfaceKind.WallCut)
                    continue;

                if (point.kind == SurfaceKind.Ceiling)
                {
                    currentBottom = point.y;

                    hasBottom = true;
                }
                else if (point.kind == SurfaceKind.Top)
                {
                    if (hasBottom &&
                        point.y > currentBottom + Epsilon)
                    {
                        intervals.Add(
                            new DensitySolidInterval(
                                currentBottom,
                                point.y
                            )
                        );
                    }

                    hasBottom = false;
                }
            }

            return intervals;
        }

        // Exact union: do not use cleanup thresholds, which erase thin builds.
        public static List<DensitySolidInterval> UnionSpan(
            List<DensitySolidInterval> intervals, float bottom, float top)
        {
            var sorted = new List<DensitySolidInterval>(intervals);
            if (top > bottom + Epsilon)
                sorted.Add(new DensitySolidInterval(bottom, top));
            sorted.Sort((a, b) => a.bottom.CompareTo(b.bottom));
            var result = new List<DensitySolidInterval>();
            foreach (var span in sorted)
            {
                int last = result.Count - 1;
                if (last >= 0 && span.bottom <= result[last].top + Epsilon)
                {
                    var previous = result[last];
                    previous.top = Mathf.Max(previous.top, span.top);
                    result[last] = previous;
                }
                else result.Add(span);
            }
            return result;
        }

        public static List<DensitySolidInterval>
            SubtractSpanFromIntervals(
                List<DensitySolidInterval> intervals,
                float cutBottom,
                float cutTop)
        {
            var result =
                new List<DensitySolidInterval>();

            foreach (DensitySolidInterval interval
                     in intervals)
            {
                if (cutTop <= interval.bottom + Epsilon ||
                    cutBottom >= interval.top - Epsilon)
                {
                    result.Add(interval);

                    continue;
                }

                if (cutBottom > interval.bottom + Epsilon)
                {
                    result.Add(
                        new DensitySolidInterval(
                            interval.bottom,
                            cutBottom
                        )
                    );
                }

                if (cutTop < interval.top - Epsilon)
                {
                    result.Add(
                        new DensitySolidInterval(
                            cutTop,
                            interval.top
                        )
                    );
                }
            }

            return result;
        }

        public static List<DensitySolidInterval>
            SanitizeIntervals(
                List<DensitySolidInterval> intervals,
                DensitySurfaceField field,
                DensityWorldConfig config)
        {
            if (intervals.Count == 0)
                return intervals;

            var sorted =
                new List<DensitySolidInterval>();

            foreach (DensitySolidInterval interval
                     in intervals)
            {
                float bottom = field.ClampY(
                    Mathf.Min(
                        interval.bottom,
                        interval.top
                    )
                );

                float top = field.ClampY(
                    Mathf.Max(
                        interval.bottom,
                        interval.top
                    )
                );

                if (top - bottom >=
                    config.minSolidIntervalHeight)
                {
                    sorted.Add(
                        new DensitySolidInterval(
                            bottom,
                            top
                        )
                    );
                }
            }

            sorted.Sort(
                (a, b) => a.bottom.CompareTo(b.bottom)
            );

            if (sorted.Count == 0)
                return sorted;

            var merged =
                new List<DensitySolidInterval>();

            merged.Add(sorted[0]);

            for (int i = 1; i < sorted.Count; i++)
            {
                int previousIndex = merged.Count - 1;

                DensitySolidInterval previous =
                    merged[previousIndex];

                DensitySolidInterval current =
                    sorted[i];

                float gap =
                    current.bottom - previous.top;

                if (gap <= config.mergeGapEps)
                {
                    previous.top = Mathf.Max(
                        previous.top,
                        current.top
                    );

                    merged[previousIndex] = previous;
                }
                else
                {
                    merged.Add(current);
                }
            }

            merged.RemoveAll(
                interval =>
                    interval.top - interval.bottom <
                    config.minSolidIntervalHeight
            );

            return merged;
        }

        public static bool IntervalsDiffer(
            List<DensitySolidInterval> a,
            List<DensitySolidInterval> b)
        {
            if (a.Count != b.Count)
                return true;

            for (int i = 0; i < a.Count; i++)
            {
                if (Mathf.Abs(
                    a[i].bottom - b[i].bottom
                ) > Epsilon)
                {
                    return true;
                }

                if (Mathf.Abs(
                    a[i].top - b[i].top
                ) > Epsilon)
                {
                    return true;
                }
            }

            return false;
        }

        public static List<DensitySolidInterval>
            CollectCavitySlices(
                List<DensitySolidInterval> intervals,
                float cutBottom,
                float cutTop,
                DensityWorldConfig config)
        {
            var slices =
                new List<DensitySolidInterval>();

            foreach (DensitySolidInterval interval
                     in intervals)
            {
                float bottom = Mathf.Max(
                    interval.bottom,
                    cutBottom
                );

                float top = Mathf.Min(
                    interval.top,
                    cutTop
                );

                if (top - bottom >=
                    config.minCavitySliceHeight)
                {
                    slices.Add(
                        new DensitySolidInterval(
                            bottom,
                            top
                        )
                    );
                }
            }

            return slices;
        }
    }
}
