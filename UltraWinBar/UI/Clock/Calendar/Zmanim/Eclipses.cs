using System;
using System.Collections.Generic;
using System.Linq;

namespace UltraWinBar.Utilities
{
    /// <summary>What an eclipse is at its greatest: the sun or the moon wholly or partly darkened.</summary>
    public enum EclipseKind { Total, Annular, Hybrid, Partial, Penumbral }

    /// <summary>
    /// An eclipse: of the sun (likui chama) or of the moon (likui levana), its kind and the moment of its greatest phase
    /// (UTC). The Gemara (Sukkah 29a) counts them as signs: the sun's for the nations, the moon's for Israel, who count by it.
    /// </summary>
    public sealed record Eclipse(DateTime Utc, bool Solar, EclipseKind Kind);

    /// <summary>
    /// The eclipses of 1900 to 2300, by the method of Jean Meeus' "Astronomical Algorithms" (chapter 54: the new and full
    /// moons near a node, the moment of the greatest eclipse, gamma and u), which anyone may use; the program is written
    /// from those formulas. Times agree with NASA's catalogue within about two minutes; the kinds agree except for a few
    /// marginal penumbral eclipses of the moon. Terrestrial time becomes UTC by the polynomials of Espenak and Meeus for ΔT.
    /// </summary>
    public static class Eclipses
    {
        public const int FirstYear = 1900, LastYear = 2300;
        private const double ToRad = Math.PI / 180.0;

        private static readonly Lazy<Eclipse[]> all = new Lazy<Eclipse[]>(Compute);

        /// <summary>Every eclipse of the years covered, in order.</summary>
        public static IReadOnlyList<Eclipse> All => all.Value;

        /// <summary>The eclipses whose greatest phase falls on the date by the clock "toLocal" turns UTC into.</summary>
        public static IReadOnlyList<Eclipse> On(DateTime date, Func<DateTime, DateTime> toLocal)
        {
            date = date.Date;
            if (date.Year < FirstYear - 1 || date.Year > LastYear + 1) return Array.Empty<Eclipse>();
            return Within(all.Value, eclipse => eclipse.Utc, date, toLocal);
        }

        /// <summary>The items of a list in order of their UTC moments that fall on the date by the clock "toLocal".</summary>
        internal static IReadOnlyList<T> Within<T>(T[] sorted, Func<T, DateTime> utc, DateTime date, Func<DateTime, DateTime> toLocal)
        {
            // The first one a day before the date (UTC), then those within a day after it.
            int low = 0, high = sorted.Length;
            DateTime from = date.AddDays(-1);
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (utc(sorted[middle]) < from) low = middle + 1; else high = middle;
            }
            var found = new List<T>();
            for (int i = low; i < sorted.Length && utc(sorted[i]) < date.AddDays(2); i++)
                if (toLocal(utc(sorted[i])).Date == date) found.Add(sorted[i]);
            return found;
        }

        /// <summary>
        /// Whether an eclipse of the moon can be seen at the place at its greatest: the moon, opposite the sun, is over the
        /// horizon when the sun is under it.
        /// </summary>
        public static bool MoonUp(Eclipse eclipse, double latitude, double longitude) =>
            Sun.Altitude(eclipse.Utc, latitude, longitude) < 0;

        private static Eclipse[] Compute()
        {
            var found = new List<Eclipse>();
            int first = (int)Math.Floor((FirstYear - 2000) * 12.3685) - 2, last = (int)Math.Ceiling((LastYear + 1 - 2000) * 12.3685) + 2;
            for (int k = first; k <= last; k++)
            {
                foreach (var eclipse in new[] { At(k, true), At(k + 0.5, false) })
                    if (eclipse != null && eclipse.Utc.Year >= FirstYear && eclipse.Utc.Year <= LastYear) found.Add(eclipse);
            }
            return found.OrderBy(eclipse => eclipse.Utc).ToArray();
        }

        // The new moon (k whole) or full moon (k + 0.5) numbered from that of January 2000, if there is an eclipse at it.
        internal static Eclipse At(double k, bool solar)
        {
            double t = k / 1236.85, t2 = t * t, t3 = t2 * t, t4 = t3 * t;
            double jde = 2451550.09766 + 29.530588861 * k + 0.00015437 * t2 - 0.000000150 * t3 + 0.00000000073 * t4;
            double m = (2.5534 + 29.10535670 * k - 0.0000014 * t2 - 0.00000011 * t3) * ToRad;
            double mp = (201.5643 + 385.81693528 * k + 0.0107582 * t2 + 0.00001238 * t3 - 0.000000058 * t4) * ToRad;
            double f = (160.7108 + 390.67050284 * k - 0.0016118 * t2 - 0.00000227 * t3 + 0.000000011 * t4) * ToRad;
            double omega = (124.7746 - 1.56375588 * k + 0.0020672 * t2 + 0.00000215 * t3) * ToRad;
            double e = 1 - 0.002516 * t - 0.0000074 * t2;
            // Too far from a node: no eclipse.
            if (Math.Abs(Math.Sin(f)) > 0.36) return null;
            double f1 = f - 0.02665 * ToRad * Math.Sin(omega);
            double a1 = (299.77 + 0.107408 * k - 0.009173 * t2) * ToRad;
            jde += solar ? -0.4075 * Math.Sin(mp) + 0.1721 * e * Math.Sin(m) : -0.4065 * Math.Sin(mp) + 0.1727 * e * Math.Sin(m);
            jde += 0.0161 * Math.Sin(2 * mp) - 0.0097 * Math.Sin(2 * f1) + 0.0073 * e * Math.Sin(mp - m) - 0.0050 * e * Math.Sin(mp + m)
                - 0.0023 * Math.Sin(mp - 2 * f1) + 0.0021 * e * Math.Sin(2 * m) + 0.0012 * Math.Sin(mp + 2 * f1) + 0.0006 * e * Math.Sin(2 * mp + m)
                - 0.0004 * Math.Sin(3 * mp) - 0.0003 * e * Math.Sin(m + 2 * f1) + 0.0003 * Math.Sin(a1) - 0.0002 * e * Math.Sin(m - 2 * f1)
                - 0.0002 * e * Math.Sin(2 * mp - m) - 0.0002 * Math.Sin(omega);
            double p = 0.2070 * e * Math.Sin(m) + 0.0024 * e * Math.Sin(2 * m) - 0.0392 * Math.Sin(mp) + 0.0116 * Math.Sin(2 * mp)
                - 0.0073 * e * Math.Sin(mp + m) + 0.0067 * e * Math.Sin(mp - m) + 0.0118 * Math.Sin(2 * f1);
            double q = 5.2207 - 0.0048 * e * Math.Cos(m) + 0.0020 * e * Math.Cos(2 * m) - 0.3299 * Math.Cos(mp)
                - 0.0060 * e * Math.Cos(mp + m) + 0.0041 * e * Math.Cos(mp - m);
            double gamma = (p * Math.Cos(f1) + q * Math.Sin(f1)) * (1 - 0.0048 * Math.Abs(Math.Cos(f1)));
            double u = 0.0059 + 0.0046 * e * Math.Cos(m) - 0.0182 * Math.Cos(mp) + 0.0004 * Math.Cos(2 * mp) - 0.0005 * Math.Cos(m + mp);
            double g = Math.Abs(gamma);
            EclipseKind kind;
            if (solar)
            {
                if (g > 1.5433 + u) return null;
                if (g < 0.9972)
                    kind = u < 0 ? EclipseKind.Total : u > 0.0047 ? EclipseKind.Annular
                        : u < 0.00464 * Math.Sqrt(1 - gamma * gamma) ? EclipseKind.Hybrid : EclipseKind.Annular;
                else if (g < 0.9972 + Math.Abs(u)) kind = u < 0 ? EclipseKind.Total : EclipseKind.Annular;
                else kind = EclipseKind.Partial;
            }
            else
            {
                double penumbral = (1.5573 + u - g) / 0.5450, umbral = (1.0128 - u - g) / 0.5450;
                if (penumbral <= 0) return null;
                kind = umbral <= 0 ? EclipseKind.Penumbral : umbral < 1 ? EclipseKind.Partial : EclipseKind.Total;
            }
            double seconds = DeltaT(2000 + k / 12.3685);
            DateTime utc = DateTime.FromOADate(jde - 2415018.5 - seconds / 86400.0);
            return new Eclipse(DateTime.SpecifyKind(utc, DateTimeKind.Utc), solar, kind);
        }

        // Terrestrial time minus universal time, in seconds (Espenak and Meeus).
        internal static double DeltaT(double year)
        {
            double t;
            if (year < 1920) { t = year - 1900; return -2.79 + 1.494119 * t - 0.0598939 * t * t + 0.0061966 * t * t * t - 0.000197 * t * t * t * t; }
            if (year < 1941) { t = year - 1920; return 21.20 + 0.84493 * t - 0.076100 * t * t + 0.0020936 * t * t * t; }
            if (year < 1961) { t = year - 1950; return 29.07 + 0.407 * t - t * t / 233 + t * t * t / 2547; }
            if (year < 1986) { t = year - 1975; return 45.45 + 1.067 * t - t * t / 260 - t * t * t / 718; }
            if (year < 2005)
            {
                t = year - 2000;
                return 63.86 + 0.3345 * t - 0.060374 * t * t + 0.0017275 * t * t * t + 0.000651814 * t * t * t * t + 0.00002373599 * t * t * t * t * t;
            }
            if (year < 2050) { t = year - 2000; return 62.92 + 0.32217 * t + 0.005589 * t * t; }
            double c = (year - 1820) / 100;
            return year < 2150 ? -20 + 32 * c * c - 0.5628 * (2150 - year) : -20 + 32 * c * c;
        }
    }
}
