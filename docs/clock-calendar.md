# Clock calendar formatting

Calendar arithmetic uses .NET `System.Globalization.HebrewCalendar`.
The Hebrew date follows the local civil date (midnight), not astronomical sunset.
The ordinary date and time keep the user's Windows formats and clock overrides.
Only the separate weekday line uses `Day-1` through `Day-6`, then `Shabbat`.

Month labels in `Resources/HebrewMonths.json` are extracted from Unicode CLDR's
`cldr-cal-hebrew-full/main/<locale>/ca-hebrew.json`, `months.format.wide`.
The data includes a project-local Belarusian Cyrillic translation because CLDR's
Belarusian entries inherit Latin fallback names. Roman numerals distinguish the two Adars.
The associated Unicode license is distributed in `Resources/Unicode-LICENSE.txt`.

Sources:

- https://learn.microsoft.com/dotnet/api/system.globalization.hebrewcalendar
- https://github.com/unicode-org/cldr-json/tree/main/cldr-json/cldr-cal-hebrew-full

Regression tests cover known date conversions, leap and ordinary Adar, every
month in all bundled locales, weekday labels, and Belarusian translation coverage.
