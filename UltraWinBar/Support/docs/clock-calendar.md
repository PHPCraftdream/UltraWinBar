# Clock calendar formatting

Calendar arithmetic uses .NET `System.Globalization.HebrewCalendar`.
The Hebrew date follows the local civil date (midnight), not astronomical sunset.
The ordinary date and time keep the user's Windows formats and clock overrides.
Only the separate weekday line uses `Day-1` through `Day-6`, then `Shabbat`.

Month labels in `../../Assets/Resources/Calendar/HebrewMonths.json` are extracted from Unicode CLDR's
`cldr-cal-hebrew-full/main/<locale>/ca-hebrew.json`, `months.format.wide`.
The data includes a project-local Belarusian Cyrillic translation because CLDR's
Belarusian entries inherit Latin fallback names. Roman numerals distinguish the two Adars.
The associated Unicode license is distributed in `../../Assets/Resources/Calendar/Unicode-LICENSE.txt`.

Sources:

- https://learn.microsoft.com/dotnet/api/system.globalization.hebrewcalendar
- https://github.com/unicode-org/cldr-json/tree/main/cldr-json/cldr-cal-hebrew-full

## Place for the Hebrew date

The settings keep a place for the Hebrew date (`Settings.HebrewPlace`): a city of the place data, or coordinates
typed in with their country, and the IANA time zone chosen with it (`HebrewPlace.TimeZoneId`). The place and its zone
are chosen by the user only. With a place chosen the Hebrew day turns at its sunset (`HebrewDayClock`, in
`UI/Clock/Calendar/Days/HebrewDay.cs`): from sunset to midnight of the place the date reads `02[01]` and the weekday
`Day-2[1]`, after midnight `02`; with no place it changes at midnight of the computer's clock.

The place data, `../../Assets/Resources/Places/places.tsv.gz`, holds the 45 355 cities of the world with at least
10 000 inhabitants: English name, region, country, latitude, longitude, population and time zone. It comes from
GeoNames (CC BY 4.0, see `../../Assets/Resources/Places/GeoNames-NOTICE.txt`) by way of a normalized SQLite database,
`CompactPlacesExporter` of the AstroPort project writing the text (format `places v2`). `PlaceDatabase` reads it once
from the embedded resource.

`CoordinateParser` reads typed coordinates (decimal marks, separators, N/S/E/W and С/Ю/В/З, labels, degrees-minutes-
seconds) and finds which number is the latitude: from the text, from the sizes, or from the country, whichever of the
two orders lands nearer to the cities of the country.

## Time zones

`ZoneClock` (`UI/Clock/Place/ZoneClock.cs`) reads time zones by IANA id through .NET's `TimeZoneInfo`, so the rules are
the ones Windows has: it brings them up to date with its updates, and a zone applies its changes (summer time, a country
moving its clocks) on the date asked about. The program forgets what it read (`ZoneClock.Refresh`) when the computer's
time changes and every six hours, so a changed rule is taken up without a restart. A zone that this computer does not
know is not offered and shows nothing. The clock has three time zones (`ClockZones`): the computer's own, `Settings.SecondTimeZone` and `Settings.ThirdTimeZone`; each shows its time
under the clock's own and, with its check box set (`ShowMainZoneName`, `ShowSecondZoneName`, `ShowThirdZoneName`), its name: the one given in the settings
(`MainZoneLabel`, `SecondZoneLabel`, `ThirdZoneLabel`, e.g. "Heart" for "Jerusalem"), else its place's.
`Settings.CalendarZone` (0, 1 or 2) is the zone the calendar goes by: its clock decides the date, where the Hebrew day turns
and the times of the day are shown; the place gives only the coordinates and the country.

Regression tests cover known date conversions, leap and ordinary Adar, every
month in all bundled locales, weekday labels, and Belarusian translation coverage.

## Jewish calendar (engine and window)

The code is in `../../UI/Clock/Calendar/`; the tests are in `../tests/DesktopRules/Features/Clock/Calendar/`.

- `Days/TorahReading.cs`: the weekly portion of a Shabbat, in Israel or the diaspora. The portions are read one after the
  other from the Shabbat after Simchat Torah; which are read together and which Shabbatot a festival takes follows from
  the kind of the year (leap or not, the weekday of Rosh Hashana, the length: 14 kinds), written down as 14 schedules.
- `Days/JewishDays.cs`: festivals and their days, fasts (moved off Shabbat), Rosh Chodesh, Hanukkah, Purim, and the modern
  days (Yom HaShoah, Yom HaZikaron, Yom HaAtzmaut, Yom Yerushalayim, by the law of Israel), in Israel or the diaspora.
- `Days/Molad.cs`: the molad of any month, by the arithmetic of the calendar (29 days 12 hours 793 chalakim a month).
- `Days/Yahrzeits.cs`: 365 figures in 13 categories, from the biblical figures, the sages of the Mishnah and Talmud, the
  Geonim and the Rishonim to the recent generations, with their traditional days. The list is compiled for this program;
  the days of those who lived after 1582 were checked against documented dates of death (see `YahrzeitChecks`), the older
  ones are the days the sources give (Shulchan Aruch OC 580, the Iggeret of Rav Sherira Gaon, the Sefer HaKabbalah, the
  chronicles), each confirmed by a second source. A death in Adar I (month 6) is kept in Adar I of a leap year and in
  Adar of a common year; a death on 30 Cheshvan or 30 Kislev is kept on the 1st of the next month in a year without that day.
  When the list grows, go through it era by era, not only by community.
- `Days/HebrewDay.cs`: the Hebrew day of a moment, with the day change at sunset; `Days/DayDetails.cs`: what the calendar
  window says about a day and a month.
- `Zmanim/Sun.cs`: the moment the sun is at a given angle, from the solar coordinates (Meeus' series), UTC, for the solar
  day of the place: no time zone is needed to ask for "that day".
- `Zmanim/Zmanim.cs`: the halachic times of a day at a place with fixed opinions (hours of the Gra and of the Magen Avraham
  with 72 minutes, dawn 16.1 degrees, earliest tallit 11.5 degrees, nightfall 8.5 degrees, Rabbeinu Tam 72 minutes after
  sunset, candles 18 minutes before sunset), the Hebrew day that begins at sunset, and the evening of a day of rest (candle
  lighting before sunset or after nightfall, havdalah). The window shows the Gra's hours, nightfall at 8.5 degrees and the
  candles: the commonly kept, more lenient opinions.
- `Window/CalendarWindow.xaml`: the window a click on the Hebrew date opens; `Window/CalendarSettingsWindow.xaml`: its
  settings (place, second time zone, the tree of yahrzeits, `Window/YahrzeitNode.cs`).

The Land of Israel is the country codes IL and PS of the place data: the festivals last a day less there. Names are in
English, Russian and Hebrew; other languages get English. The Hebrew date itself is still counted by .NET.

### Where it comes from, and the licenses

Everything is written for this project from public rules and formulas: the rules of the Hebrew calendar, the series of
Jean Meeus' "Astronomical Algorithms", the schedules of Torah readings (facts of the calendar), the laws about the modern
days. The widespread libraries Hebcal (GPL-2.0) and KosherJava Zmanim and its .NET ports (LGPL-2.1) are not used, linked,
copied or ported: their licenses do not fit a program under Apache-2.0 that is handed out as a whole.

The expected values in the tests were taken from two programs run as black boxes, only their printed numbers kept:
pyluach (MIT) for Torah portions, festivals, fasts, Rosh Chodesh and the molad, astral (Apache-2.0) for the moments of
the sun. Neither is part of the program or of its build. The solar fixtures drop the 21 of 200 values on which astral
and this code name the day differently (astral takes the UTC date of the event, this code the solar date of the place).
