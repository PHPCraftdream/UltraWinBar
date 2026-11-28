using System;
using System.Collections.Generic;
using System.Linq;

namespace UltraWinBar.Utilities
{
    /// <summary>The moment of an actual new moon (the conjunction of the moon with the sun) or full moon (the opposition), UTC.</summary>
    public sealed record LunarPhase(DateTime Utc, bool Full);

    /// <summary>
    /// The actual new and full moons of 1900 to 2300, by the method of Jean Meeus' "Astronomical Algorithms" (chapter 49:
    /// the mean phase and its periodic corrections, with the planetary terms), which anyone may use; the program is written
    /// from those formulas. They agree with the published moments within a minute (the tests compare them with known ones,
    /// and with the eclipses, which happen at these moments). Not the molad of the Hebrew calendar, which is a mean moment.
    /// </summary>
    public static class MoonPhases
    {
        private const double ToRad = Math.PI / 180.0;

        private static readonly Lazy<LunarPhase[]> all = new Lazy<LunarPhase[]>(Compute);

        /// <summary>Every new and full moon of the years covered, in order.</summary>
        public static IReadOnlyList<LunarPhase> All => all.Value;

        /// <summary>The new and full moons that fall on the date by the clock "toLocal" turns UTC into.</summary>
        public static IReadOnlyList<LunarPhase> On(DateTime date, Func<DateTime, DateTime> toLocal)
        {
            date = date.Date;
            if (date.Year < Eclipses.FirstYear - 1 || date.Year > Eclipses.LastYear + 1) return Array.Empty<LunarPhase>();
            return Eclipses.Within(all.Value, phase => phase.Utc, date, toLocal);
        }

        private static LunarPhase[] Compute()
        {
            var found = new List<LunarPhase>();
            int first = (int)Math.Floor((Eclipses.FirstYear - 2000) * 12.3685) - 2, last = (int)Math.Ceiling((Eclipses.LastYear + 1 - 2000) * 12.3685) + 2;
            for (int k = first; k <= last; k++)
                foreach (var phase in new[] { At(k, false), At(k + 0.5, true) })
                    if (phase.Utc.Year >= Eclipses.FirstYear && phase.Utc.Year <= Eclipses.LastYear) found.Add(phase);
            return found.OrderBy(phase => phase.Utc).ToArray();
        }

        // Planetary arguments (degrees at k = 0, and per lunation) and the sizes of their corrections.
        private static readonly double[] PlanetStart = { 299.77, 251.88, 251.83, 349.42, 84.66, 141.74, 207.14, 154.84, 34.52, 207.19, 291.34, 161.72, 239.56, 331.55 };
        private static readonly double[] PlanetStep = { 0.107408, 0.016321, 26.651886, 36.412478, 18.206239, 53.303771, 2.453732, 7.306860, 27.261239, 0.121824, 1.844379, 24.198154, 25.513099, 3.592518 };
        private static readonly double[] PlanetSize = { 0.000325, 0.000165, 0.000164, 0.000126, 0.000110, 0.000062, 0.000060, 0.000056, 0.000047, 0.000042, 0.000040, 0.000037, 0.000035, 0.000023 };

        // The new moon (k whole) or full moon (k + 0.5) numbered from the new moon of January 2000.
        internal static LunarPhase At(double k, bool full)
        {
            double t = k / 1236.85, t2 = t * t, t3 = t2 * t, t4 = t3 * t;
            double jde = 2451550.09766 + 29.530588861 * k + 0.00015437 * t2 - 0.000000150 * t3 + 0.00000000073 * t4;
            double e = 1 - 0.002516 * t - 0.0000074 * t2;
            double m = (2.5534 + 29.10535670 * k - 0.0000014 * t2 - 0.00000011 * t3) * ToRad;
            double mp = (201.5643 + 385.81693528 * k + 0.0107582 * t2 + 0.00001238 * t3 - 0.000000058 * t4) * ToRad;
            double f = (160.7108 + 390.67050284 * k - 0.0016118 * t2 - 0.00000227 * t3 + 0.000000011 * t4) * ToRad;
            double omega = (124.7746 - 1.56375588 * k + 0.0020672 * t2 + 0.00000215 * t3) * ToRad;
            double correction = full
                ? -0.40614 * Math.Sin(mp) + 0.17302 * e * Math.Sin(m) + 0.01614 * Math.Sin(2 * mp) + 0.01043 * Math.Sin(2 * f)
                  + 0.00734 * e * Math.Sin(mp - m) - 0.00515 * e * Math.Sin(mp + m) + 0.00209 * e * e * Math.Sin(2 * m)
                : -0.40720 * Math.Sin(mp) + 0.17241 * e * Math.Sin(m) + 0.01608 * Math.Sin(2 * mp) + 0.01039 * Math.Sin(2 * f)
                  + 0.00739 * e * Math.Sin(mp - m) - 0.00514 * e * Math.Sin(mp + m) + 0.00208 * e * e * Math.Sin(2 * m);
            // The terms the new and the full moon share.
            correction += -0.00111 * Math.Sin(mp - 2 * f) - 0.00057 * Math.Sin(mp + 2 * f) + 0.00056 * e * Math.Sin(2 * mp + m)
                - 0.00042 * Math.Sin(3 * mp) + 0.00042 * e * Math.Sin(m + 2 * f) + 0.00038 * e * Math.Sin(m - 2 * f)
                - 0.00024 * e * Math.Sin(2 * mp - m) - 0.00017 * Math.Sin(omega) - 0.00007 * Math.Sin(mp + 2 * m)
                + 0.00004 * Math.Sin(2 * mp - 2 * f) + 0.00004 * Math.Sin(3 * m) + 0.00003 * Math.Sin(mp + m - 2 * f)
                + 0.00003 * Math.Sin(2 * mp + 2 * f) - 0.00003 * Math.Sin(mp + m + 2 * f) + 0.00003 * Math.Sin(mp - m + 2 * f)
                - 0.00002 * Math.Sin(mp - m - 2 * f) - 0.00002 * Math.Sin(3 * mp + m) + 0.00002 * Math.Sin(4 * mp);
            for (int i = 0; i < PlanetStart.Length; i++)
            {
                double argument = PlanetStart[i] + PlanetStep[i] * k - (i == 0 ? 0.009173 * t2 : 0);
                correction += PlanetSize[i] * Math.Sin(argument * ToRad);
            }
            double seconds = Eclipses.DeltaT(2000 + k / 12.3685);
            DateTime utc = DateTime.FromOADate(jde + correction - 2415018.5 - seconds / 86400.0);
            return new LunarPhase(DateTime.SpecifyKind(utc, DateTimeKind.Utc), full);
        }
    }
}
