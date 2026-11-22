using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UltraWinBar.Utilities;

// The times of the sun and the halachic times that follow from them, and the evenings of the days of rest.
internal static class ZmanimChecks
{
    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        CheckSun();
        CheckZmanim();
        CheckHebrewDay();
        CheckRest();
    }

    private static DateTime Day(string text) => DateTime.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTime Utc(string text) => DateTime.SpecifyKind(DateTime.ParseExact(text, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), DateTimeKind.Utc);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void CheckSun()
    {
        // Moments of the sun at 40 places on all continents and in the far north, as an independent program (astral,
        // Apache-2.0, used only as a reference for the expected values) gives them: R sunrise, S sunset, D dawn at 16.1°,
        // T nightfall at 8.5°, N solar noon, all in UTC.
        var references = new (double Latitude, double Longitude, string Date, char Event, string Expected)[]
        {
            (31.778, 35.235, "2026-03-09", 'R', "2026-03-09T03:57:07"),
            (31.778, 35.235, "2026-03-09", 'S', "2026-03-09T15:42:30"),
            (31.778, 35.235, "2026-03-09", 'D', "2026-03-09T02:44:57"),
            (31.778, 35.235, "2026-03-09", 'T', "2026-03-09T16:19:01"),
            (31.778, 35.235, "2026-03-09", 'N', "2026-03-09T09:49:40"),
            (40.713, -74.006, "2025-11-29", 'R', "2025-11-29T11:59:11"),
            (40.713, -74.006, "2025-11-29", 'S', "2025-11-29T21:29:29"),
            (40.713, -74.006, "2025-11-29", 'D', "2025-11-29T10:32:01"),
            (40.713, -74.006, "2025-11-29", 'T', "2025-11-29T22:14:33"),
            (40.713, -74.006, "2025-11-29", 'N', "2025-11-29T16:44:13"),
            (51.507, -0.128, "2026-08-05", 'R', "2026-08-05T04:30:32"),
            (51.507, -0.128, "2026-08-05", 'S', "2026-08-05T19:41:29"),
            (51.507, -0.128, "2026-08-05", 'D', "2026-08-05T02:13:23"),
            (51.507, -0.128, "2026-08-05", 'T', "2026-08-05T20:42:14"),
            (51.507, -0.128, "2026-08-05", 'N', "2026-08-05T12:06:35"),
            (55.756, 37.617, "2025-04-21", 'R', "2025-04-21T02:10:02"),
            (55.756, 37.617, "2025-04-21", 'S', "2025-04-21T16:47:42"),
            (55.756, 37.617, "2025-04-21", 'T', "2025-04-21T17:51:29"),
            (55.756, 37.617, "2025-04-21", 'N', "2025-04-21T09:28:17"),
            (-33.869, 151.209, "2025-08-14", 'S', "2025-08-14T07:24:34"),
            (-33.869, 151.209, "2025-08-14", 'T', "2025-08-14T08:03:05"),
            (-33.869, 151.209, "2025-08-14", 'N', "2025-08-14T01:59:54"),
            (-34.604, -58.382, "2025-06-19", 'R', "2025-06-19T11:00:10"),
            (-34.604, -58.382, "2025-06-19", 'S', "2025-06-19T20:49:48"),
            (-34.604, -58.382, "2025-06-19", 'D', "2025-06-19T09:39:11"),
            (-34.604, -58.382, "2025-06-19", 'T', "2025-06-19T21:31:31"),
            (-34.604, -58.382, "2025-06-19", 'N', "2025-06-19T15:54:51"),
            (69.649, 18.956, "2025-04-21", 'R', "2025-04-21T02:12:52"),
            (69.649, 18.956, "2025-04-21", 'S', "2025-04-21T19:16:13"),
            (69.649, 18.956, "2025-04-21", 'D', null),
            (69.649, 18.956, "2025-04-21", 'T', null),
            (69.649, 18.956, "2025-04-21", 'N', "2025-04-21T10:42:55"),
            (64.147, -21.942, "2026-09-30", 'R', "2026-09-30T07:34:05"),
            (64.147, -21.942, "2026-09-30", 'S', "2026-09-30T18:59:52"),
            (64.147, -21.942, "2026-09-30", 'D', "2026-09-30T05:08:06"),
            (64.147, -21.942, "2026-09-30", 'T', "2026-09-30T20:11:05"),
            (64.147, -21.942, "2026-09-30", 'N', "2026-09-30T13:17:55"),
            (1.352, 103.82, "2026-02-22", 'S', "2026-02-22T11:20:26"),
            (1.352, 103.82, "2026-02-22", 'T', "2026-02-22T11:51:55"),
            (1.352, 103.82, "2026-02-22", 'N', "2026-02-22T05:18:15"),
            (21.307, -157.858, "2026-06-05", 'R', "2026-06-05T15:48:54"),
            (21.307, -157.858, "2026-06-05", 'D', "2026-06-05T14:33:55"),
            (21.307, -157.858, "2026-06-05", 'T', "2026-06-06T05:48:30"),
            (21.307, -157.858, "2026-06-05", 'N', "2026-06-05T22:29:52"),
            (-36.849, 174.763, "2025-09-03", 'S', "2025-09-03T06:01:51"),
            (-36.849, 174.763, "2025-09-03", 'T', "2025-09-03T06:40:49"),
            (-36.849, 174.763, "2025-09-03", 'N', "2025-09-03T00:20:24"),
            (32.085, 34.782, "2025-07-06", 'R', "2025-07-06T02:40:43"),
            (32.085, 34.782, "2025-07-06", 'S', "2025-07-06T16:50:28"),
            (32.085, 34.782, "2025-07-06", 'D', "2025-07-06T01:13:13"),
            (32.085, 34.782, "2025-07-06", 'T', "2025-07-06T17:32:56"),
            (32.085, 34.782, "2025-07-06", 'N', "2025-07-06T09:45:37"),
            (25.276, 55.296, "2026-11-14", 'R', "2026-11-14T02:35:08"),
            (25.276, 55.296, "2026-11-14", 'S', "2026-11-14T13:30:57"),
            (25.276, 55.296, "2026-11-14", 'D', "2026-11-14T01:24:25"),
            (25.276, 55.296, "2026-11-14", 'T', "2026-11-14T14:07:01"),
            (25.276, 55.296, "2026-11-14", 'N', "2026-11-14T08:03:07"),
            (41.009, 28.978, "2025-01-24", 'R', "2025-01-24T05:21:48"),
            (41.009, 28.978, "2025-01-24", 'S', "2025-01-24T15:11:01"),
            (41.009, 28.978, "2025-01-24", 'D', "2025-01-24T03:56:05"),
            (41.009, 28.978, "2025-01-24", 'T', "2025-01-24T15:55:11"),
            (41.009, 28.978, "2025-01-24", 'N', "2025-01-24T10:16:06"),
            (48.857, 2.352, "2026-10-28", 'R', "2026-10-28T06:31:14"),
            (48.857, 2.352, "2026-10-28", 'S', "2026-10-28T16:36:43"),
            (48.857, 2.352, "2026-10-28", 'D', "2026-10-28T04:55:59"),
            (48.857, 2.352, "2026-10-28", 'T', "2026-10-28T17:25:29"),
            (48.857, 2.352, "2026-10-28", 'N', "2026-10-28T11:34:22"),
            (34.052, -118.244, "2026-12-18", 'R', "2026-12-18T14:53:18"),
            (34.052, -118.244, "2026-12-18", 'D', "2026-12-18T13:32:55"),
            (34.052, -118.244, "2026-12-18", 'T', "2026-12-19T01:27:31"),
            (34.052, -118.244, "2026-12-18", 'N', "2026-12-18T19:49:19"),
            (-26.204, 28.047, "2026-02-17", 'R', "2026-02-17T03:54:11"),
            (-26.204, 28.047, "2026-02-17", 'S', "2026-02-17T16:49:01"),
            (-26.204, 28.047, "2026-02-17", 'D', "2026-02-17T02:42:12"),
            (-26.204, 28.047, "2026-02-17", 'T', "2026-02-17T17:24:53"),
            (-26.204, 28.047, "2026-02-17", 'N', "2026-02-17T10:21:50"),
            (35.69, 139.692, "2026-06-01", 'S', "2026-06-01T09:51:15"),
            (35.69, 139.692, "2026-06-01", 'T', "2026-06-01T10:36:07"),
            (35.69, 139.692, "2026-06-01", 'N', "2026-06-01T02:39:01"),
            (19.433, -99.133, "2026-06-23", 'R', "2026-06-23T11:59:55"),
            (19.433, -99.133, "2026-06-23", 'D', "2026-06-23T10:45:40"),
            (19.433, -99.133, "2026-06-23", 'T', "2026-06-24T01:54:36"),
            (19.433, -99.133, "2026-06-23", 'N', "2026-06-23T18:38:40"),
            (59.329, 18.069, "2025-09-02", 'R', "2025-09-02T03:47:18"),
            (59.329, 18.069, "2025-09-02", 'S', "2025-09-02T17:45:53"),
            (59.329, 18.069, "2025-09-02", 'D', "2025-09-02T01:21:26"),
            (59.329, 18.069, "2025-09-02", 'T', "2025-09-02T18:52:17"),
            (59.329, 18.069, "2025-09-02", 'N', "2025-09-02T10:47:30"),
            (50.45, 30.523, "2025-02-02", 'R', "2025-02-02T05:31:29"),
            (50.45, 30.523, "2025-02-02", 'S', "2025-02-02T14:52:24"),
            (50.45, 30.523, "2025-02-02", 'D', "2025-02-02T03:50:46"),
            (50.45, 30.523, "2025-02-02", 'T', "2025-02-02T15:44:34"),
            (50.45, 30.523, "2025-02-02", 'N', "2025-02-02T10:11:35"),
            (43.652, -79.383, "2025-04-30", 'R', "2025-04-30T10:11:09"),
            (43.652, -79.383, "2025-04-30", 'D', "2025-04-30T08:32:19"),
            (43.652, -79.383, "2025-04-30", 'T', "2025-05-01T01:06:48"),
            (43.652, -79.383, "2025-04-30", 'N', "2025-04-30T17:14:47"),
            (-22.907, -43.173, "2025-02-05", 'R', "2025-02-05T08:35:38"),
            (-22.907, -43.173, "2025-02-05", 'S', "2025-02-05T21:37:29"),
            (-22.907, -43.173, "2025-02-05", 'D', "2025-02-05T07:24:03"),
            (-22.907, -43.173, "2025-02-05", 'T', "2025-02-05T22:13:07"),
            (-22.907, -43.173, "2025-02-05", 'N', "2025-02-05T15:06:40"),
            (6.524, 3.379, "2025-05-09", 'R', "2025-05-09T05:31:22"),
            (6.524, 3.379, "2025-05-09", 'S', "2025-05-09T17:54:33"),
            (6.524, 3.379, "2025-05-09", 'D', "2025-05-09T04:26:09"),
            (6.524, 3.379, "2025-05-09", 'T', "2025-05-09T18:27:25"),
            (6.524, 3.379, "2025-05-09", 'N', "2025-05-09T11:42:57"),
            (28.614, 77.209, "2026-04-11", 'R', "2026-04-11T00:30:31"),
            (28.614, 77.209, "2026-04-11", 'S', "2026-04-11T13:14:24"),
            (28.614, 77.209, "2026-04-11", 'T', "2026-04-11T13:50:34"),
            (28.614, 77.209, "2026-04-11", 'N', "2026-04-11T06:52:19"),
            (13.756, 100.502, "2025-04-25", 'S', "2025-04-25T11:32:35"),
            (13.756, 100.502, "2025-04-25", 'T', "2025-04-25T12:05:38"),
            (13.756, 100.502, "2025-04-25", 'N', "2025-04-25T05:15:59"),
            (45.508, -73.588, "2025-09-14", 'R', "2025-09-14T10:32:12"),
            (45.508, -73.588, "2025-09-14", 'S', "2025-09-14T23:06:31"),
            (45.508, -73.588, "2025-09-14", 'D', "2025-09-14T09:01:44"),
            (45.508, -73.588, "2025-09-14", 'T', "2025-09-14T23:51:18"),
            (45.508, -73.588, "2025-09-14", 'N', "2025-09-14T16:50:01"),
            (52.52, 13.405, "2025-04-10", 'R', "2025-04-10T04:19:56"),
            (52.52, 13.405, "2025-04-10", 'S', "2025-04-10T17:56:25"),
            (52.52, 13.405, "2025-04-10", 'D', "2025-04-10T02:27:19"),
            (52.52, 13.405, "2025-04-10", 'T', "2025-04-10T18:50:50"),
            (52.52, 13.405, "2025-04-10", 'N', "2025-04-10T11:07:44"),
            (38.907, -77.037, "2026-01-23", 'R', "2026-01-23T12:21:27"),
            (38.907, -77.037, "2026-01-23", 'S', "2026-01-23T22:18:59"),
            (38.907, -77.037, "2026-01-23", 'D', "2026-01-23T10:58:20"),
            (38.907, -77.037, "2026-01-23", 'T', "2026-01-23T23:01:45"),
            (38.907, -77.037, "2026-01-23", 'N', "2026-01-23T17:19:51"),
            (53.348, -6.26, "2025-01-12", 'R', "2025-01-12T08:35:10"),
            (53.348, -6.26, "2025-01-12", 'S', "2025-01-12T16:32:07"),
            (53.348, -6.26, "2025-01-12", 'D', "2025-01-12T06:40:36"),
            (53.348, -6.26, "2025-01-12", 'T', "2025-01-12T17:32:39"),
            (53.348, -6.26, "2025-01-12", 'N', "2025-01-12T12:33:14"),
            (-1.286, 36.818, "2025-07-24", 'R', "2025-07-24T03:37:46"),
            (-1.286, 36.818, "2025-07-24", 'S', "2025-07-24T15:40:47"),
            (-1.286, 36.818, "2025-07-24", 'D', "2025-07-24T02:32:31"),
            (-1.286, 36.818, "2025-07-24", 'T', "2025-07-24T16:13:43"),
            (-1.286, 36.818, "2025-07-24", 'N', "2025-07-24T09:39:16"),
            (60.17, 24.938, "2026-09-06", 'R', "2026-09-06T03:26:40"),
            (60.17, 24.938, "2026-09-06", 'S', "2026-09-06T17:09:04"),
            (60.17, 24.938, "2026-09-06", 'D', "2026-09-06T01:00:48"),
            (60.17, 24.938, "2026-09-06", 'T', "2026-09-06T18:16:02"),
            (60.17, 24.938, "2026-09-06", 'N', "2026-09-06T10:18:47"),
            (31.63, -8.008, "2026-07-11", 'R', "2026-07-11T05:35:28"),
            (31.63, -8.008, "2026-07-11", 'S', "2026-07-11T19:39:27"),
            (31.63, -8.008, "2026-07-11", 'D', "2026-07-11T04:09:27"),
            (31.63, -8.008, "2026-07-11", 'T', "2026-07-11T20:21:18"),
            (31.63, -8.008, "2026-07-11", 'N', "2026-07-11T12:37:30"),
            (47.376, 8.542, "2025-11-06", 'R', "2025-11-06T06:17:16"),
            (47.376, 8.542, "2025-11-06", 'S', "2025-11-06T16:00:58"),
            (47.376, 8.542, "2025-11-06", 'D', "2025-11-06T04:43:12"),
            (47.376, 8.542, "2025-11-06", 'T', "2025-11-06T16:49:23"),
            (47.376, 8.542, "2025-11-06", 'N', "2025-11-06T11:09:23"),
            (-41.286, 174.776, "2026-02-02", 'S', "2026-02-02T07:40:33"),
            (-41.286, 174.776, "2026-02-02", 'T', "2026-02-02T08:26:58"),
            (-41.286, 174.776, "2026-02-02", 'N', "2026-02-02T00:34:32"),
            (64.835, -147.716, "2025-09-13", 'R', "2025-09-13T15:08:56"),
            (64.835, -147.716, "2025-09-13", 'D', "2025-09-13T12:18:56"),
            (64.835, -147.716, "2025-09-13", 'T', "2025-09-14T05:39:15"),
            (64.835, -147.716, "2025-09-13", 'N', "2025-09-13T21:46:53"),
            (9.145, 40.49, "2025-08-28", 'R', "2025-08-28T03:09:48"),
            (9.145, 40.49, "2025-08-28", 'S', "2025-08-28T15:28:33"),
            (9.145, 40.49, "2025-08-28", 'D', "2025-08-28T02:06:27"),
            (9.145, 40.49, "2025-08-28", 'T', "2025-08-28T16:00:28"),
            (9.145, 40.49, "2025-08-28", 'N', "2025-08-28T09:19:21"),
            (-12.046, -77.043, "2026-03-07", 'R', "2026-03-07T11:11:29"),
            (-12.046, -77.043, "2026-03-07", 'S', "2026-03-07T23:26:32"),
            (-12.046, -77.043, "2026-03-07", 'D', "2026-03-07T10:08:16"),
            (-12.046, -77.043, "2026-03-07", 'T', "2026-03-07T23:58:24"),
            (-12.046, -77.043, "2026-03-07", 'N', "2026-03-07T17:19:16"),
            (23.129, 113.264, "2026-08-27", 'S', "2026-08-27T10:49:13"),
            (23.129, 113.264, "2026-08-27", 'T', "2026-08-27T11:23:47"),
            (23.129, 113.264, "2026-08-27", 'N', "2026-08-27T04:28:37"),
            (36.717, 3.088, "2025-08-08", 'R', "2025-08-08T04:59:30"),
            (36.717, 3.088, "2025-08-08", 'S', "2025-08-08T18:46:30"),
            (36.717, 3.088, "2025-08-08", 'D', "2025-08-08T03:32:47"),
            (36.717, 3.088, "2025-08-08", 'T', "2025-08-08T19:28:46"),
            (36.717, 3.088, "2025-08-08", 'N', "2025-08-08T11:53:20"),
        };
        double worst = 0;
        foreach (var (latitude, longitude, date, kind, expected) in references)
        {
            DateTime day = Day(date);
            DateTime? actual = kind switch
            {
                'R' => Sun.At(day, latitude, longitude, Sun.SunriseZenith, true),
                'S' => Sun.At(day, latitude, longitude, Sun.SunriseZenith, false),
                'D' => Sun.At(day, latitude, longitude, 106.1, true),
                'T' => Sun.At(day, latitude, longitude, 98.5, false),
                _ => Sun.SolarNoon(day, longitude),
            };
            if (expected == null)
            {
                if (actual != null) throw new Exception($"{latitude},{longitude} {date} {kind}: the sun does not reach this angle, got {actual}.");
                continue;
            }
            if (actual == null) throw new Exception($"{latitude},{longitude} {date} {kind}: expected {expected}, got none.");
            double seconds = Math.Abs((actual.Value - Utc(expected)).TotalSeconds);
            worst = Math.Max(worst, seconds);
            if (seconds > 120) throw new Exception($"{latitude},{longitude} {date} {kind}: expected {expected}, got {actual:s}, {seconds:0} seconds apart.");
        }
        Console.WriteLine($"PASS: {references.Length} moments of the sun agree with an independent program, the worst {worst:0} seconds apart.");

        // The polar day and night give no sunset and no sunrise.
        if (Sun.At(Day("2026-06-21"), 69.649, 18.956, Sun.SunriseZenith, false) != null || Sun.At(Day("2026-12-21"), 69.649, 18.956, Sun.SunriseZenith, true) != null ||
            Sun.At(Day("2026-06-21"), 89.9, 0, Sun.SunriseZenith, true) != null)
            throw new Exception("The sun does not set at 69.6°N at the summer solstice and does not rise at the winter one.");

        // What the numbers must satisfy anywhere: sunrise before noon before sunset, noon half way between them, dawn before
        // sunrise, the moments on the solar day that was asked for, and noon a minute from the date's meridian.
        int checkedDays = 0;
        for (double latitude = -60; latitude <= 60; latitude += 15)
        for (double longitude = -170; longitude <= 170; longitude += 20)
        for (int dayOfYear = 3; dayOfYear < 365; dayOfYear += 29)
        {
            DateTime day = new DateTime(2026, 1, 1).AddDays(dayOfYear);
            DateTime sunrise = Sun.At(day, latitude, longitude, Sun.SunriseZenith, true).Value, sunset = Sun.At(day, latitude, longitude, Sun.SunriseZenith, false).Value;
            DateTime noon = Sun.SolarNoon(day, longitude);
            string place = $"{latitude},{longitude} {day:yyyy-MM-dd}";
            Check(sunrise < noon && noon < sunset, $"{place}: sunrise, noon and sunset are not in order");
            Check(Math.Abs(((sunrise - noon) + (sunset - noon)).TotalMinutes) < 6, $"{place}: noon is not half way between sunrise and sunset");
            // The solar day: the local mean time (UTC plus longitude / 15 hours) of sunrise, noon and sunset has the date asked for.
            foreach (DateTime moment in new[] { sunrise, noon, sunset })
                Check(moment.AddHours(longitude / 15).Date == day, $"{place}: {moment:s} is not on the solar day");
            Check(Math.Abs((noon.AddHours(longitude / 15) - day).TotalHours - 12) < 0.35, $"{place}: noon is not at noon of the local mean time");
            DateTime? dawn = Sun.At(day, latitude, longitude, 106.1, true);
            if (dawn != null) Check(dawn.Value < sunrise && (sunrise - dawn.Value).TotalMinutes < 400, $"{place}: dawn is not before sunrise");
            Check(Sun.At(day, latitude, longitude, 95, true).Value < sunrise && Sun.At(day, latitude, longitude, 95, false).Value > sunset, $"{place}: the sun at 5° below is not outside sunrise and sunset");
            checkedDays++;
        }
        Console.WriteLine($"PASS: {checkedDays} days at the latitudes from 60° south to 60° north and every 20° of longitude: sunrise, noon and sunset in order, on the solar day asked for, noon half way between them.");
    }

    private static void CheckZmanim()
    {
        DateTime day = Day("2026-03-09");
        const double latitude = 31.778, longitude = 35.235;
        var times = Zmanim.Of(day, latitude, longitude);
        Check(times.Count == Enum.GetValues(typeof(Zman)).Length && times.Values.All(time => time.HasValue), "Jerusalem has every time on an ordinary day.");

        // They come one after the other in the order of the day.
        var order = new[]
        {
            Zman.Alot, Zman.Misheyakir, Zman.Sunrise, Zman.ShemaMagenAvraham, Zman.ShemaGra, Zman.TefillaMagenAvraham, Zman.TefillaGra,
            Zman.Chatzot, Zman.MinchaGedola, Zman.MinchaKetana, Zman.PlagHaMincha, Zman.CandleLighting, Zman.Sunset, Zman.Tzeit, Zman.RabbeinuTam,
        };
        for (int i = 1; i < order.Length; i++)
            Check(times[order[i - 1]].Value < times[order[i]].Value, $"{order[i - 1]} is not before {order[i]}");

        // The hours of the day are twelfths of the time from sunrise to sunset (the Magen Avraham: with 72 minutes more on each side).
        DateTime sunrise = times[Zman.Sunrise].Value, sunset = times[Zman.Sunset].Value;
        double hour = (sunset - sunrise).TotalSeconds / 12, longHour = (sunset - sunrise).TotalSeconds / 12 + 2 * 72 * 60 / 12.0;
        Check(Math.Abs((times[Zman.ShemaGra].Value - sunrise).TotalSeconds - 3 * hour) < 1, "The Gra's Shema is three hours after sunrise.");
        Check(Math.Abs((times[Zman.TefillaGra].Value - sunrise).TotalSeconds - 4 * hour) < 1, "The Gra's Shacharit is four hours after sunrise.");
        Check(Math.Abs((times[Zman.MinchaGedola].Value - sunrise).TotalSeconds - 6.5 * hour) < 1 && Math.Abs((times[Zman.MinchaKetana].Value - sunrise).TotalSeconds - 9.5 * hour) < 1 &&
            Math.Abs((times[Zman.PlagHaMincha].Value - sunrise).TotalSeconds - 10.75 * hour) < 1, "Mincha Gedola, Mincha Ketana and Plag are 6.5, 9.5 and 10.75 hours after sunrise.");
        Check(Math.Abs((times[Zman.ShemaMagenAvraham].Value - sunrise).TotalSeconds + 72 * 60 - 3 * longHour) < 1, "The Magen Avraham's Shema is three of his hours after dawn 72 minutes before sunrise.");
        Check(Math.Abs((times[Zman.TefillaMagenAvraham].Value - sunrise).TotalSeconds + 72 * 60 - 4 * longHour) < 1, "The Magen Avraham's Shacharit is four of his hours after that dawn.");
        Check(Math.Abs((times[Zman.Chatzot].Value - Sun.SolarNoon(day, longitude)).TotalMinutes) < 3, "Midday is at solar noon.");
        Check(Math.Abs((sunset - times[Zman.CandleLighting].Value).TotalMinutes - 18) < 1e-6 && Math.Abs((times[Zman.RabbeinuTam].Value - sunset).TotalMinutes - 72) < 1e-6, "Candles 18 minutes before sunset, Rabbeinu Tam 72 minutes after.");
        Check(Math.Abs((sunset - Zmanim.Of(day, latitude, longitude, 40)[Zman.CandleLighting].Value).TotalMinutes - 40) < 1e-6, "Candles can be lit 40 minutes before sunset, as in Jerusalem.");
        // Nightfall at 8.5° under the horizon in Jerusalem in March is about 30 minutes after sunset.
        double dusk = (times[Zman.Tzeit].Value - sunset).TotalMinutes;
        Check(dusk > 25 && dusk < 40, $"Nightfall in Jerusalem is {dusk:0} minutes after sunset.");

        // The far north in summer: no sunset, so nothing that depends on it, and no exception.
        var polar = Zmanim.Of(Day("2026-06-21"), 69.649, 18.956);
        Check(polar[Zman.Sunrise] == null && polar[Zman.Sunset] == null && polar[Zman.ShemaGra] == null && polar[Zman.CandleLighting] == null && polar[Zman.MinchaKetana] == null,
            "At the polar day there is no sunrise, sunset or the hours between them.");

        // The Hebrew day begins at sunset.
        DateTime date = Day("2026-03-09");
        Check(Zmanim.HebrewDay(date, sunset.AddSeconds(-1), latitude, longitude) == date && Zmanim.HebrewDay(date.AddHours(15), sunset.AddSeconds(-1), latitude, longitude) == date &&
            Zmanim.HebrewDay(date, sunset, latitude, longitude) == date.AddDays(1) && Zmanim.HebrewDay(date, sunset.AddHours(3), latitude, longitude) == date.AddDays(1),
            "The Hebrew day changes at sunset.");
        Check(Zmanim.HebrewDay(Day("2026-06-21"), new DateTime(2026, 6, 21, 23, 0, 0, DateTimeKind.Utc), 69.649, 18.956) == Day("2026-06-21"), "With no sunset the day does not change.");

        // Names in three languages, all different.
        foreach (Zman zman in Enum.GetValues(typeof(Zman)))
        {
            var names = new[] { "English", "русский", "עברית" }.Select(language => Zmanim.NameOf(zman, language)).ToArray();
            Check(names.All(name => !string.IsNullOrWhiteSpace(name)) && names.Distinct().Count() == 3, $"{zman} needs three different names.");
            Check(Zmanim.NameOf(zman, "Deutsch") == names[0], $"{zman}: a language without names falls back to English.");
        }
        Console.WriteLine("PASS: the halachic times come in the order of the day, are the twelfths of the day (Gra and Magen Avraham), candles and Rabbeinu Tam are at their minutes, the polar day gives none, the Hebrew day turns at sunset, and all have three names.");
    }

    // The Hebrew day of the clock: it turns at the sunset of the place chosen, in the time zone chosen with it.
    private static void CheckHebrewDay()
    {
        var jerusalem = new HebrewPlace { Name = "Jerusalem", Latitude = 31.778, Longitude = 35.235, TimeZoneId = "Asia/Jerusalem", CountryCode = "IL" };
        DateTime sunset = Zmanim.Of(Day("2026-03-09"), jerusalem.Latitude, jerusalem.Longitude)[Zman.Sunset].Value;
        DateTime On(string text) => DateTime.SpecifyKind(DateTime.ParseExact(text, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture), DateTimeKind.Utc);
        // Jerusalem is two hours ahead of UTC in March, until the clocks go forward on 27 March.
        var cases = new (DateTime Utc, string Day, string Civil)[]
        {
            (On("2026-03-09T00:30"), "2026-03-09", null),                 // 02:30, after midnight
            (sunset.AddMinutes(-1), "2026-03-09", null),                  // a minute before sunset
            (sunset.AddMinutes(1), "2026-03-10", "2026-03-09"),           // a minute after: the next Hebrew day, the civil day still on
            (On("2026-03-09T21:30"), "2026-03-10", "2026-03-09"),         // 23:30, before midnight
            (On("2026-03-09T22:30"), "2026-03-10", null),                 // 00:30: the civil day has turned too
            (On("2026-03-10T11:00"), "2026-03-10", null),
        };
        foreach (var (utc, day, civil) in cases)
        {
            var now = HebrewDayClock.Now(utc, jerusalem);
            if (now.Day != Day(day) || now.Civil != (civil == null ? (DateTime?)null : Day(civil)) || now.AfterSunset != (civil != null))
                throw new Exception($"{utc:s}Z in Jerusalem: expected {day} [{civil}], got {now.Day:yyyy-MM-dd} [{now.Civil:yyyy-MM-dd}].");
        }
        // No place: the computer's clock and the civil day; a place with a zone nobody knows: the computer's clock, the sunset of the place.
        var anyMoment = new DateTime(2026, 3, 9, 12, 0, 0, DateTimeKind.Utc);
        var withoutPlace = HebrewDayClock.Now(anyMoment, null);
        if (withoutPlace.Day != TimeZoneInfo.ConvertTimeFromUtc(anyMoment, TimeZoneInfo.Local).Date || withoutPlace.Civil != null)
            throw new Exception("Without a place the Hebrew day is the computer's civil day.");
        var unknownZone = HebrewDayClock.Now(anyMoment, jerusalem with { TimeZoneId = "Nowhere/Land" });
        if (Math.Abs((unknownZone.Day - withoutPlace.Day).TotalDays) > 1) throw new Exception("An unknown zone falls back to the computer's clock.");
        var polar = HebrewDayClock.Now(new DateTime(2026, 6, 21, 23, 0, 0, DateTimeKind.Utc), new HebrewPlace { Latitude = 69.649, Longitude = 18.956, TimeZoneId = "Europe/Oslo" });
        if (polar.Civil != null || polar.Day != Day("2026-06-22")) throw new Exception("With no sunset the Hebrew day turns with the civil day.");

        // The date and the weekday as the clock shows them in the evening: the new day, with the day still on in brackets.
        var calendar = new System.Globalization.HebrewCalendar();
        string previous = calendar.GetDayOfMonth(new DateTime(2016, 11, 30)).ToString("00");
        if (HebrewClockFormatter.FormatDate(new DateTime(2016, 12, 1), "English", null, new DateTime(2016, 11, 30)) != $"01[{previous}] Kislev 5777" ||
            HebrewClockFormatter.FormatDate(new DateTime(2016, 12, 1), "русский", null, new DateTime(2016, 11, 30)) != $"01[{previous}] кислев 5777" ||
            HebrewClockFormatter.FormatDate(new DateTime(2016, 12, 2), "English", null, new DateTime(2016, 12, 1)) != "02[01] Kislev 5777" ||
            HebrewClockFormatter.FormatDate(new DateTime(2016, 12, 2), "English") != "02 Kislev 5777" ||
            HebrewClockFormatter.FormatDate(new DateTime(2016, 12, 2), "English", null, null) != "02 Kislev 5777")
            throw new Exception("The Hebrew date in the evening has the day still on in brackets.");
        if (HebrewClockFormatter.DayName(DayOfWeek.Monday, DayOfWeek.Sunday) != "Day-2[1]" || HebrewClockFormatter.DayName(DayOfWeek.Saturday, DayOfWeek.Friday) != "Shabbat[6]" ||
            HebrewClockFormatter.DayName(DayOfWeek.Sunday, DayOfWeek.Saturday) != "Day-1[Shabbat]" || HebrewClockFormatter.DayName(DayOfWeek.Monday, null) != "Day-2" ||
            HebrewClockFormatter.DayName(DayOfWeek.Saturday) != "Shabbat")
            throw new Exception("The weekday in the evening has the weekday still on in brackets.");
        Console.WriteLine("PASS: with a place the Hebrew day turns at its sunset in its time zone (new day, civil day in brackets until midnight, then alone); without one at midnight; the date and weekday read \"02[01]\" and \"2[1]\" in the evening.");
    }

    private static void CheckRest()
    {
        var evenings = new (string Date, bool Israel, RestEvent Expected)[]
        {
            ("2026-11-19", false, RestEvent.None), ("2026-11-20", false, RestEvent.CandleLighting), ("2026-11-21", false, RestEvent.Havdalah),
            // Rosh Hashana 5787 is Shabbat and Sunday: the eve, the first night into the second day, and the end.
            ("2026-09-11", false, RestEvent.CandleLighting), ("2026-09-12", false, RestEvent.CandleLightingAfterNightfall), ("2026-09-13", false, RestEvent.Havdalah),
            ("2026-09-11", true, RestEvent.CandleLighting), ("2026-09-12", true, RestEvent.CandleLightingAfterNightfall), ("2026-09-13", true, RestEvent.Havdalah),
            // Pesach 5786 begins on Thursday 2 April: two days in the diaspora, one in Israel.
            ("2026-04-01", false, RestEvent.CandleLighting), ("2026-04-02", false, RestEvent.CandleLightingAfterNightfall), ("2026-04-03", false, RestEvent.CandleLighting),
            ("2026-04-04", false, RestEvent.Havdalah), ("2026-04-01", true, RestEvent.CandleLighting), ("2026-04-02", true, RestEvent.Havdalah),
            ("2026-04-03", true, RestEvent.CandleLighting), ("2026-04-04", true, RestEvent.Havdalah),
            // The last days of Pesach: Wednesday 8 April is the seventh day, Thursday 9 April the eighth, kept only outside Israel.
            ("2026-04-07", false, RestEvent.CandleLighting), ("2026-04-08", false, RestEvent.CandleLightingAfterNightfall), ("2026-04-09", false, RestEvent.Havdalah),
            ("2026-04-07", true, RestEvent.CandleLighting), ("2026-04-08", true, RestEvent.Havdalah), ("2026-04-09", true, RestEvent.None),
        };
        foreach (var (date, israel, expected) in evenings)
        {
            var actual = Zmanim.EveningOf(Day(date), israel);
            Check(actual == expected, $"{date} ({(israel ? "Israel" : "diaspora")}): expected {expected}, got {actual}.");
        }
        Check(Zmanim.IsRestDay(Day("2026-11-21"), false) && Zmanim.IsRestDay(Day("2026-09-12"), true) && !Zmanim.IsRestDay(Day("2026-11-19"), false) &&
            !Zmanim.IsRestDay(Day("2026-04-09"), true) && Zmanim.IsRestDay(Day("2026-04-09"), false), "Days of rest are Shabbat and festivals, with the second days only outside Israel.");

        // In a century every Friday evening has candles before sunset, whatever festival is on that Friday.
        int shabbatot = 0;
        for (DateTime day = Day("2000-01-01"); day < Day("2100-01-01"); day = day.AddDays(1))
            if (day.DayOfWeek == DayOfWeek.Friday)
            {
                shabbatot++;
                var friday = Zmanim.EveningOf(day, false);
                Check(friday == RestEvent.CandleLighting, $"{day:yyyy-MM-dd}: the evening of Friday has candles before sunset, got {friday}.");
            }
        Console.WriteLine($"PASS: {evenings.Length} evenings of Shabbat and festivals, in Israel and the diaspora, and all {shabbatot} Fridays in a century have candles before sunset.");
    }
}
