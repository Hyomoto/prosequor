namespace Prosequor.Client;

/// <summary>
/// Even, vertically mirrored ellipse slots with adjacent overlap relaxation and slight scale-up.
/// </summary>
public static class AttributeRingRelax
{
    public const double Phase = -Math.PI / 2;
    public const double Spring = 0.05;
    public const int MaxIters = 40;
    public const double ScaleMin = 1.0;
    public const double ScaleMax = 1.15;
    public const double ScaleStep = 0.01;
    const double EpsilonAngle = 1e-4;
    const double Probe = 1e-3;

    /// <summary>
    /// Resolves ring angles and ellipse radii. Catalog index 0 stays at 12 o'clock.
    /// </summary>
    public static void Resolve(
        int n,
        double cx,
        double cy,
        double baseRx,
        double baseRy,
        double clusterW,
        double clusterH,
        out double[] angles,
        out double rx,
        out double ry)
    {
        if (n <= 0)
        {
            angles = [];
            rx = baseRx;
            ry = baseRy;
            return;
        }

        if (n == 1)
        {
            angles = [Phase];
            rx = baseRx;
            ry = baseRy;
            return;
        }

        double[] bestAngles = Ideals(n);
        double bestRx = baseRx;
        double bestRy = baseRy;

        for (double scale = ScaleMin; scale <= ScaleMax + 1e-9; scale += ScaleStep)
        {
            double sRx = baseRx * scale;
            double sRy = baseRy * scale;
            double[] free = IdealFree(n);
            double[] work = new double[n];

            for (int iter = 0; iter < MaxIters; iter++)
            {
                WriteAngles(n, free, work);
                BuildRects(work, cx, cy, sRx, sRy, clusterW, clusterH, out RingRect[] rects);
                bool any = false;
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    if (!OverlapPixels(rects[i], rects[j], out double pixels) || pixels <= 0)
                    {
                        continue;
                    }

                    any = true;
                    double mid = MidAngle(work[i], work[j]);
                    double ppr = PixelsPerRadian(cx, cy, sRx, sRy, mid);
                    double dTheta = pixels / Math.Max(ppr, 1e-3);
                    ApplyAngleDelta(n, free, i, -dTheta * 0.5);
                    ApplyAngleDelta(n, free, j, dTheta * 0.5);
                    ClampFree(n, free);
                }

                SpringFree(n, free);
                ClampFree(n, free);

                if (!any)
                {
                    WriteAngles(n, free, work);
                    BuildRects(work, cx, cy, sRx, sRy, clusterW, clusterH, out RingRect[] check);
                    if (!AnyAdjacentOverlap(check))
                    {
                        angles = work;
                        rx = sRx;
                        ry = sRy;
                        return;
                    }
                }
            }

            WriteAngles(n, free, work);
            bestAngles = work;
            bestRx = sRx;
            bestRy = sRy;
            BuildRects(work, cx, cy, sRx, sRy, clusterW, clusterH, out RingRect[] finalRects);
            if (!AnyAdjacentOverlap(finalRects))
            {
                angles = bestAngles;
                rx = bestRx;
                ry = bestRy;
                return;
            }
        }

        angles = bestAngles;
        rx = bestRx;
        ry = bestRy;
    }

    public static double[] Ideals(int n)
    {
        double[] angles = new double[n];
        double step = 2 * Math.PI / n;
        for (int i = 0; i < n; i++)
        {
            angles[i] = Phase + i * step;
        }

        return angles;
    }

    public static void ClusterCenter(
        double theta,
        double cx,
        double cy,
        double rx,
        double ry,
        out double x,
        out double y)
    {
        x = cx + rx * Math.Cos(theta);
        y = cy + ry * Math.Sin(theta);
    }

    public static void ClusterRect(
        double theta,
        double cx,
        double cy,
        double rx,
        double ry,
        double clusterW,
        double clusterH,
        out double left,
        out double top,
        out double right,
        out double bottom)
    {
        ClusterCenter(theta, cx, cy, rx, ry, out double mx, out double my);
        left = mx - clusterW / 2;
        top = my - clusterH / 2;
        right = left + clusterW;
        bottom = top + clusterH;
    }

    public static bool RectsOverlap(
        double l0, double t0, double r0, double b0,
        double l1, double t1, double r1, double b1) =>
        l0 < r1 && l1 < r0 && t0 < b1 && t1 < b0;

    static double[] IdealFree(int n)
    {
        int m = FreeCount(n);
        double[] free = new double[m];
        double step = 2 * Math.PI / n;
        for (int k = 0; k < m; k++)
        {
            free[k] = (k + 1) * step;
        }

        return free;
    }

    static int FreeCount(int n) => n <= 1 ? 0 : (n - 1) / 2;

    static void WriteAngles(int n, double[] free, double[] angles)
    {
        angles[0] = Phase;
        int m = FreeCount(n);
        for (int k = 0; k < m; k++)
        {
            int i = k + 1;
            angles[i] = Phase + free[k];
            angles[n - i] = Phase - free[k];
        }

        if ((n & 1) == 0)
        {
            angles[n / 2] = Phase + Math.PI;
        }
    }

    static void ApplyAngleDelta(int n, double[] free, int idx, double delta)
    {
        if (idx == 0)
        {
            return;
        }

        if ((n & 1) == 0 && idx == n / 2)
        {
            return;
        }

        int m = FreeCount(n);
        if (idx >= 1 && idx <= m)
        {
            free[idx - 1] += delta;
            return;
        }

        int partner = n - idx;
        if (partner >= 1 && partner <= m)
        {
            // angles[idx] = Phase - free[partner-1]; +delta on angle ⇒ -delta on free
            free[partner - 1] -= delta;
        }
    }

    static void SpringFree(int n, double[] free)
    {
        double[] ideal = IdealFree(n);
        for (int k = 0; k < free.Length; k++)
        {
            free[k] += (ideal[k] - free[k]) * Spring;
        }
    }

    static void ClampFree(int n, double[] free)
    {
        double step = 2 * Math.PI / n;
        int m = free.Length;
        for (int k = 0; k < m; k++)
        {
            double low = (k + 0.5) * step;
            double high = (k + 1.5) * step;
            if (k == 0)
            {
                low = EpsilonAngle;
            }

            if ((n & 1) == 0)
            {
                high = Math.Min(high, Math.PI - EpsilonAngle);
            }
            else
            {
                high = Math.Min(high, Math.PI - EpsilonAngle);
            }

            if (k > 0)
            {
                low = Math.Max(low, free[k - 1] + EpsilonAngle);
            }

            free[k] = Math.Clamp(free[k], low, high);
        }

        // Enforce strict order after individual clamps.
        for (int k = 1; k < m; k++)
        {
            if (free[k] <= free[k - 1])
            {
                free[k] = free[k - 1] + EpsilonAngle;
            }
        }
    }

    static void BuildRects(
        double[] angles,
        double cx,
        double cy,
        double rx,
        double ry,
        double clusterW,
        double clusterH,
        out RingRect[] rects)
    {
        rects = new RingRect[angles.Length];
        for (int i = 0; i < angles.Length; i++)
        {
            ClusterRect(
                angles[i], cx, cy, rx, ry, clusterW, clusterH,
                out double l, out double t, out double r, out double b);
            rects[i] = new RingRect(l, t, r, b);
        }
    }

    static bool AnyAdjacentOverlap(RingRect[] rects)
    {
        int n = rects.Length;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            if (OverlapPixels(rects[i], rects[j], out _))
            {
                return true;
            }
        }

        return false;
    }

    static bool OverlapPixels(RingRect a, RingRect b, out double pixels)
    {
        double ox = (a.W + b.W) / 2 - Math.Abs(a.Cx - b.Cx);
        double oy = (a.H + b.H) / 2 - Math.Abs(a.Cy - b.Cy);
        if (ox <= 0 || oy <= 0)
        {
            pixels = 0;
            return false;
        }

        pixels = Math.Min(ox, oy);
        return true;
    }

    static double MidAngle(double a, double b)
    {
        double d = ShortestDelta(a, b);
        return a + d * 0.5;
    }

    static double ShortestDelta(double from, double to)
    {
        double d = to - from;
        while (d > Math.PI)
        {
            d -= 2 * Math.PI;
        }

        while (d < -Math.PI)
        {
            d += 2 * Math.PI;
        }

        return d;
    }

    static double PixelsPerRadian(double cx, double cy, double rx, double ry, double theta)
    {
        ClusterCenter(theta, cx, cy, rx, ry, out double x0, out double y0);
        ClusterCenter(theta + Probe, cx, cy, rx, ry, out double x1, out double y1);
        double dist = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        return dist / Probe;
    }

    readonly struct RingRect
    {
        public RingRect(double left, double top, double right, double bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public double Left { get; }
        public double Top { get; }
        public double Right { get; }
        public double Bottom { get; }
        public double Cx => (Left + Right) * 0.5;
        public double Cy => (Top + Bottom) * 0.5;
        public double W => Right - Left;
        public double H => Bottom - Top;
    }
}
