using System.Linq;
using System.Text.Json.Serialization;

namespace UltraWinBar.Utilities
{
    /// <summary>
    /// The place the Hebrew date and the halachic times are kept for: a city of the place data, or coordinates the user
    /// typed in with their country, and the time zone the user chose. Stored in the settings as it is, so it stays
    /// whatever the place data later becomes.
    /// </summary>
    public sealed record HebrewPlace
    {
        /// <summary>The name of the city; empty when the place is coordinates typed in.</summary>
        public string Name { get; init; }
        public string Region { get; init; }
        public string Country { get; init; }
        public string CountryCode { get; init; }
        public double Latitude { get; init; }
        public double Longitude { get; init; }
        /// <summary>True for coordinates typed in, false for a city chosen from the place data.</summary>
        public bool IsCustom { get; init; }
        /// <summary>The IANA time zone the user chose for the place ("Asia/Jerusalem"); empty or null when none was chosen.</summary>
        public string TimeZoneId { get; init; }

        /// <summary>
        /// "Jerusalem, Jerusalem District, Israel" for a city; "31.7683° N, 35.2137° E, Israel" for coordinates.
        /// </summary>
        [JsonIgnore]
        public string Display => IsCustom
            ? string.Join(", ", new[] { CoordinateParser.Format(Latitude, Longitude), Country }.Where(part => !string.IsNullOrEmpty(part)))
            : string.Join(", ", new[] { Name, Region, Country }.Where(part => !string.IsNullOrEmpty(part)));

        public static HebrewPlace FromPlace(Place place) => new HebrewPlace
        {
            Name = place.Name,
            Region = place.Region,
            Country = place.Country,
            CountryCode = place.CountryCode,
            Latitude = place.Latitude,
            Longitude = place.Longitude,
            TimeZoneId = place.TimeZoneId,
        };

        public static HebrewPlace FromCoordinates(double latitude, double longitude, CountryInfo country, string timeZoneId = null) => new HebrewPlace
        {
            TimeZoneId = timeZoneId,
            Country = country?.Name,
            CountryCode = country?.Code,
            Latitude = latitude,
            Longitude = longitude,
            IsCustom = true,
        };
    }
}
