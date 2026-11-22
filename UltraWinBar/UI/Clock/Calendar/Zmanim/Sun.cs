using System;

namespace UltraWinBar.Utilities
{
    /// <summary>
    /// The moments the sun is at a given angle below or above the horizon, from the sun's coordinates by the series of
    /// Jean Meeus' "Astronomical Algorithms" (the mean longitude and anomaly of the sun, the equation of the centre, the
    /// obliquity of the ecliptic, the equation of time), which anyone may use; the program is written from those
    /// formulas, not from any other program. The tests compare it with an independent implementation: they agree to
    /// within a minute (usually a few seconds; the far north and deep twilight are the least certain). Times are UTC; the
    /// day is the solar day of the place, that is the date of its local mean time (longitude / 15 hours from UTC), so no
    /// time zone is needed to ask for "that day".
    /// </summary>
    public static class Sun
    {
        /// <summary>Sunrise and sunset: the centre of the sun 50 arc minutes under the horizon (refraction and the sun's radius).</summary>
        public const double SunriseZenith = 90.0 + 50.0 / 60.0;

        private const double ToRad = Math.PI / 180.0, ToDeg = 180.0 / Math.PI;

        // The sun at an instant: the declination and the equation of time (minutes, apparent minus mean solar time).
        private static (double Declination, double EquationOfTime) Position(DateTime utc)
        {
            double julianDay = utc.ToOADate() + 2415018.5;
            double t = (julianDay - 2451545.0) / 36525.0;
            double meanLongitude = Normalize(280.46646 + t * (36000.76983 + 0.0003032 * t));
            double meanAnomaly = 357.52911 + t * (35999.05029 - 0.0001537 * t);
            double eccentricity = 0.016708634 - t * (0.000042037 + 0.0000001267 * t);
            double m = meanAnomaly * ToRad;
            double center = Math.Sin(m) * (1.914602 - t * (0.004817 + 0.000014 * t))
                + Math.Sin(2 * m) * (0.019993 - 0.000101 * t) + Math.Sin(3 * m) * 0.000289;
            double omega = (125.04 - 1934.136 * t) * ToRad;
            double apparentLongitude = (meanLongitude + center - 0.00569 - 0.00478 * Math.Sin(omega)) * ToRad;
            double meanObliquity = 23.0 + (26.0 + (21.448 - t * (46.815 + t * (0.00059 - t * 0.001813))) / 60.0) / 60.0;
            double obliquity = (meanObliquity + 0.00256 * Math.Cos(omega)) * ToRad;
            double declination = Math.Asin(Math.Sin(obliquity) * Math.Sin(apparentLongitude));
            double y = Math.Tan(obliquity / 2) * Math.Tan(obliquity / 2);
            double l0 = meanLongitude * ToRad;
            double equation = y * Math.Sin(2 * l0) - 2 * eccentricity * Math.Sin(m)
                + 4 * eccentricity * y * Math.Sin(m) * Math.Cos(2 * l0) - 0.5 * y * y * Math.Sin(4 * l0)
                - 1.25 * eccentricity * eccentricity * Math.Sin(2 * m);
            return (declination, 4.0 * equation * ToDeg);
        }

        private static double Normalize(double degrees) => degrees - 360.0 * Math.Floor(degrees / 360.0);

        private static DateTime Midnight(DateTime date) => new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>The moment of solar noon on the solar day of the date at the longitude (east is positive).</summary>
        public static DateTime SolarNoon(DateTime date, double longitude)
        {
            DateTime midnight = Midnight(date);
            double minutes = 720.0 - 4.0 * longitude;
            for (int i = 0; i < 3; i++) minutes = 720.0 - 4.0 * longitude - Position(midnight.AddMinutes(minutes)).EquationOfTime;
            return midnight.AddMinutes(minutes);
        }

        /// <summary>
        /// The moment on the solar day of the date at which the centre of the sun is at the zenith distance (90° is the
        /// horizon, more is below it), rising in the morning or setting in the evening; null where the sun does not
        /// reach it that day (the polar day and the polar night).
        /// </summary>
        public static DateTime? At(DateTime date, double latitude, double longitude, double zenith, bool rising)
        {
            DateTime midnight = Midnight(date);
            double minutes = (SolarNoon(date, longitude) - midnight).TotalMinutes;
            for (int i = 0; i < 4; i++)
            {
                var (declination, equation) = Position(midnight.AddMinutes(minutes));
                double cosine = (Math.Cos(zenith * ToRad) - Math.Sin(latitude * ToRad) * Math.Sin(declination)) / (Math.Cos(latitude * ToRad) * Math.Cos(declination));
                if (cosine < -1.0 || cosine > 1.0 || double.IsNaN(cosine)) return null;
                double hourAngle = Math.Acos(cosine) * ToDeg;
                minutes = 720.0 - 4.0 * longitude - equation + (rising ? -4.0 : 4.0) * hourAngle;
            }
            return midnight.AddMinutes(minutes);
        }
    }
}
