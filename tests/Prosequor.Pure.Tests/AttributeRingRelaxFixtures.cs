using Prosequor.Client;
using Xunit;

namespace Prosequor.Progress;

/// <summary>Ellipse ring angle relaxation (no GUI).</summary>
public static class AttributeRingRelaxFixtures
{
    public static void VerifyAll()
    {
        VerifyTinyClustersStayAtIdeals();
        VerifyLargeClustersClearOverlapAndKeepMirror();
    }

    static void VerifyTinyClustersStayAtIdeals()
    {
        const int n = 5;
        const double cx = 100;
        const double cy = 100;
        const double baseRx = 80;
        const double baseRy = 70;
        const double clusterW = 8;
        const double clusterH = 10;

        AttributeRingRelax.Resolve(
            n, cx, cy, baseRx, baseRy, clusterW, clusterH,
            out double[] angles, out double rx, out double ry);

        double[] ideals = AttributeRingRelax.Ideals(n);
        for (int i = 0; i < n; i++)
        {
            if (Math.Abs(Normalize(angles[i] - ideals[i])) > 1e-6)
            {
                Assert.Fail(
                    $"[prosequor] Ring relax tiny fixture: angle[{i}] drifted " +
                    $"({angles[i]} vs ideal {ideals[i]}).");
            }
        }

        if (Math.Abs(rx - baseRx) > 1e-9 || Math.Abs(ry - baseRy) > 1e-9)
        {
            Assert.Fail(
                $"[prosequor] Ring relax tiny fixture: expected scale 1 " +
                $"(rx={rx}, ry={ry}; base {baseRx},{baseRy}).");
        }

        if (HasAdjacentOverlap(angles, cx, cy, rx, ry, clusterW, clusterH))
        {
            Assert.Fail("[prosequor] Ring relax tiny fixture: unexpected adjacent overlap.");
        }
    }

    static void VerifyLargeClustersClearOverlapAndKeepMirror()
    {
        const int n = 5;
        const double cx = 200;
        const double cy = 200;
        const double baseRx = 60;
        const double baseRy = 50;
        const double clusterW = 48;
        const double clusterH = 56;

        double[] ideals = AttributeRingRelax.Ideals(n);
        if (!HasAdjacentOverlap(ideals, cx, cy, baseRx, baseRy, clusterW, clusterH))
        {
            Assert.Fail(
                "[prosequor] Ring relax large fixture: ideals should overlap before resolve.");
        }

        AttributeRingRelax.Resolve(
            n, cx, cy, baseRx, baseRy, clusterW, clusterH,
            out double[] angles, out double rx, out double ry);

        if (HasAdjacentOverlap(angles, cx, cy, rx, ry, clusterW, clusterH))
        {
            Assert.Fail(
                "[prosequor] Ring relax large fixture: adjacent overlap remains after resolve.");
        }

        if (Math.Abs(Normalize(angles[0] - AttributeRingRelax.Phase)) > 1e-6)
        {
            Assert.Fail("[prosequor] Ring relax large fixture: crown left 12 o'clock.");
        }

        // Vertical mirror: angle[k] and angle[n-k] are mirrors across Phase.
        for (int k = 1; k <= (n - 1) / 2; k++)
        {
            double expectedMirror = AttributeRingRelax.Phase
                - (angles[k] - AttributeRingRelax.Phase);
            if (Math.Abs(Normalize(angles[n - k] - expectedMirror)) > 1e-5)
            {
                Assert.Fail(
                    $"[prosequor] Ring relax large fixture: pair {k}/{n - k} not mirrored " +
                    $"({angles[k]}, {angles[n - k]}).");
            }
        }

        if (rx < baseRx - 1e-9 || ry < baseRy - 1e-9)
        {
            Assert.Fail("[prosequor] Ring relax large fixture: ellipse shrank.");
        }

        if (rx > baseRx * AttributeRingRelax.ScaleMax + 1e-6
            || ry > baseRy * AttributeRingRelax.ScaleMax + 1e-6)
        {
            Assert.Fail("[prosequor] Ring relax large fixture: ellipse exceeded ScaleMax.");
        }
    }

    static bool HasAdjacentOverlap(
        double[] angles,
        double cx,
        double cy,
        double rx,
        double ry,
        double clusterW,
        double clusterH)
    {
        int n = angles.Length;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            AttributeRingRelax.ClusterRect(
                angles[i], cx, cy, rx, ry, clusterW, clusterH,
                out double l0, out double t0, out double r0, out double b0);
            AttributeRingRelax.ClusterRect(
                angles[j], cx, cy, rx, ry, clusterW, clusterH,
                out double l1, out double t1, out double r1, out double b1);
            if (AttributeRingRelax.RectsOverlap(l0, t0, r0, b0, l1, t1, r1, b1))
            {
                return true;
            }
        }

        return false;
    }

    static double Normalize(double d)
    {
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
}
