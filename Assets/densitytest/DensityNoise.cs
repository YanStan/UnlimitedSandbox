
using System;

namespace Unlimited.DensityTest
{
    public sealed class DensityNoise
    {
        private readonly byte[] perm = new byte[256];

        private static readonly float[] grad2 =
        {
             5,  2,  2,  5,
            -5,  2, -2,  5,
             5, -2,  2, -5,
            -5, -2, -2, -5
        };

        public DensityNoise(int seed = 1337)
        {
            byte[] source = new byte[256];

            for (int i = 0; i < 256; i++)
                source[i] = (byte)i;

            uint s = unchecked((uint)seed);

            for (int i = 255; i >= 0; i--)
            {
                s = unchecked(
                    s * 1664525u + 1013904223u
                );

                int r = (int)(
                    ((ulong)s + 31UL) % (ulong)(i + 1)
                );

                perm[i] = source[r];
                source[r] = source[i];
            }
        }

        public double Noise2(double x, double y)
        {
            double s = (x + y) * 0.3660254037844386;

            double xs = x + s;
            double ys = y + s;

            int i = (int)Math.Floor(xs);
            int j = (int)Math.Floor(ys);

            double t = (i + j) * 0.21132486540518713;

            double X0 = i - t;
            double Y0 = j - t;

            double x0 = x - X0;
            double y0 = y - Y0;

            int i1 = x0 > y0 ? 1 : 0;
            int j1 = x0 > y0 ? 0 : 1;

            double x1 = x0 - i1 + 0.21132486540518713;
            double y1 = y0 - j1 + 0.21132486540518713;

            double x2 = x0 - 1 + 0.42264973081037427;
            double y2 = y0 - 1 + 0.42264973081037427;

            int ii = i & 255;
            int jj = j & 255;

            double value = 0;

            value += Corner(ii, jj, x0, y0);
            value += Corner(ii + i1, jj + j1, x1, y1);
            value += Corner(ii + 1, jj + 1, x2, y2);

            return value * 45.23065;
        }

        private double Corner(
            int ii, int jj, double x, double y)
        {
            double t = 0.5 - x * x - y * y;

            if (t <= 0)
                return 0;

            if (ii < 0 || ii >= 256)
                return double.NaN;

            int gi = perm[
                (perm[ii] + jj) & 255
            ] & 0x0e;

            double gx = grad2[gi];
            double gy = grad2[gi + 1];

            double t2 = t * t;

            return t2 * t2 * (gx * x + gy * y);
        }
    }
}
