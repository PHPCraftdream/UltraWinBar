# Changelog

## 2.5.2-alpha (2026-11-28)

### Added

- Long drop-down lists (the time zones of the clock, the countries of the place window) have a search line at the top
  of the list in the Modern Glass theme: what is typed keeps the items that contain it, in any case; the chosen item
  stays; Enter takes the first one left, Escape closes.
- A modern look, the theme "Modern Glass", now the default for new installations: slate surfaces tinted with a neon
  accent, matte glass (translucent accent layers and hairlines) and the desktop blurred behind the panel and behind
  the windows (the "blur behind" setting turns the blur off), on the panel, the settings, the calendar, the place
  chooser, the menus, the drop-downs and the tool tips. The accent is one of six Tailwind colors (cyan, violet,
  fuchsia, emerald, amber, rose), chosen in the settings under the theme and applied at once; it colors the whole
  look. The active task has a bar of the accent, the window title bars are dark. Made for speed: plain brushes
  only, with no effects, no gradients and no animations; the only layered window is the panel, which the blur needs
  (see `modern-theme.md`). The older themes remain in the list.
- A place for the Hebrew date in the clock settings, under the Hebrew date checkbox: a city of the world with
  10 000 inhabitants or more, found by its English name (typing a start of its words, in any order, accents
  ignored; the region and the country count too, "paris texas"), or coordinates typed in with their country, and
  the time zone of the place. A city brings its own zone and typed-in coordinates the zone of the nearest city of
  the country; the user may change it, and it is never taken from the computer. The cities (45 355 in 220
  countries, with 334 time zones, GeoNames, CC BY 4.0, under 1 MB) are part of the program.
- The Jewish calendar, opened by a click on the Hebrew date of the clock: a month of days with their Hebrew
  dates, the festivals, fasts, Rosh Chodesh, weekly portions and yahrzeits marked; for the chosen day the Hebrew
  date, the festival with its day number (one day less in the Land of Israel, by the country of the place), the
  weekly portion, the beginning and end of a festival or of Shabbat (candle lighting to nightfall), the times of
  the day at the place (dawn, sunrise, latest Shema and Shacharit by the Gra, midday, Mincha Gedola and Ketana,
  Plag, sunset, nightfall, candles and havdalah), the molad of the coming month, and the yahrzeits. The times are
  computed by the program from the sun's coordinates, in the time zone of the place; the halachic opinions are the
  commonly kept, more lenient ones.
- Yahrzeits of 46 well-known Torah figures in 8 categories (biblical figures, sages, Rishonim, Acharonim,
  Kabbalists, Chassidic masters, Chabad Rebbes, recent generations). The calendar's settings have a tree with a
  check box for everyone, for each category and for each figure.
- With a place chosen, the Hebrew day changes at the sunset of the place: from sunset until midnight the date
  reads "02[01]" (the new day, and the day that is still on in brackets) and the weekday "Day-2[1]"; after midnight
  only the new one. With no place the date still changes at midnight.
- A second time under the clock's own, in a time zone of your choice (in the general settings, with a third one: see Changed): "15:05
  Jerusalem", with "+1" or "-1" when it is already the next or still the previous day there. The first time stays as
  on the computer. The zone's rules (summer time and the laws that change it) are the ones Windows has, read
  again every few hours and whenever the computer's time changes.
- Coordinates are read in almost any form: a dot or a comma in the fraction; a comma, a semicolon, a colon, a slash
  or a space between the numbers, in any mixture; brackets and words around them; N, S, E, W (and С, Ю, В, З) and
  "lat", "lon", "широта", "долгота"; degrees, minutes and seconds. Which number is the latitude is found from the
  text when it says so, from the sizes when only one order is possible (a latitude is up to 90 degrees), and from
  the country otherwise: the order that lands nearer to the cities of the country wins, and a point far from the
  country is flagged.
- With the Hebrew date shown next to the clock, the pointer over it lights a
  frame of running lights around the date. It costs almost nothing: nothing is
  drawn and no timer runs until the pointer is over the date, then one timer
  redraws a few dozen dots about ten times a second (no WPF animation clock);
  with animations switched off in Windows, or in a remote session, the lights
  stand still.
- The Jewish calendar has a title bar of its own in the look of the theme (a bar of the accent, the title, a close
  button) instead of the system one, which took long to take the theme's dark color, and it opens in the corner of
  the clock it was clicked on: above a bottom panel, under a top one, beside a side one, always inside the work area.

### Changed

- Jewish calendar: the civil date is written dd.mm.yyyy everywhere, with no name of a month, in every language: in the
  grid's cells, their tooltips, the details of a day and over the calendar ("23.11.2026" instead of "23, November" and
  "23 November 2026"). The Hebrew months keep their names.
- Themes: only Modern Glass ships, in two variants; the classic themes (Windows 9x/2000/Me, XP, Vista, Longhorn,
  Watercolor) are gone. The new Modern Glass Light (slate-200 surfaces with the blur, not white; dark text; the accent
  one shade deeper) is the main theme: a new install starts with it, the dark one moves to it once, and a theme that is
  no longer there falls back to it. Its panel text is semibold (text on the layered panel has no ClearType), and the
  tray's icons lie on the glass instead of an opaque block. The deploy script removes installed themes that no longer ship.
- The clock renders its text as the task labels do, in the same weight, and its digits are all one width: the ticking
  seconds no longer move its blocks. The windows of the light theme (the calendar, the dialogs) take the panel's
  semibold weight. The clock's text is a size larger (14), its lines closer.
- Jewish calendar: the week's portion is a solid accent badge (readable on the selected, accent-tinted day too); the
  dates, the special days and the weekday numbers in the grid are darker and bolder. The details on the right start
  on the line of the first week's cards.
- A language change no longer leaves the open windows with empty labels: the new language's strings come in before
  the previous ones go (as the themes do), and the log names the dictionaries loaded.
- A theme change no longer fails with "'ShowDesktopIconImageLarge' resource not found": the new base dictionary is
  added before the old themes are removed (no moment without the theme's resources), and the show-desktop icon is a
  resource reference instead of a lookup.
- Jewish calendar: the window fills the work area of the clock's screen and can be neither resized nor maximized.
- Clock on a vertical panel: a zone's name goes under its time, so one long line no longer shrinks the whole clock,
  and each zone (its time with its name) has a frame of its own, so several zones do not run together.
- Yahrzeits: 21 more figures (109 in all): the Noda BiYehuda, the Shelah, the Taz, the Maharsha, the Malbim; Rabbi
  Aharon of Karlin, of Ropshitz, of Zidichov, of Chernobyl, the Imrei Emes, Rabbi Aharon of Belz, the Klausenburger and
  the Bobover Rebbes; Rabbi Shimon Shkop, Rabbi Isser Zalman Meltzer, Rabbi Chaim Shmulevitz, Rav Soloveitchik; Rabbi
  Chaim Pinto, Rabbi Uziel; Rabbi Aryeh Levin, Rabbi Tzvi Yehuda Kook. Each day was confirmed by two or more sources
  and, for 18 of them, by the Hebrew date of the documented day of death (a test); left out where the sources disagree
  (the Kitzur Shulchan Aruch, Rav Dessler, Rav Yaakov Kamenetsky).
- Yahrzeits: 17 more figures (126 in all): the Bach, the Pnei Yehoshua, Rabbi Yonasan Eybeschutz, the Yaavetz, the
  Chayei Adam, Rabbi Samson Raphael Hirsch, the Ksav Sofer; Rabbi Shmelke of Nikolsburg, Rabbi Menachem Mendel of
  Rimanov, Rabbi Moshe Leib of Sassov, the Degel Machaneh Ephraim, Rabbi Tzadok HaKohen, Rabbi Yissachar Dov of Belz,
  the Beis Yisrael; Rav Aharon Leib Shteinman; Rabbi Chaim Palagi, Rabbi Yehuda Tzadka. Confirmed the same way (16 by
  the documented day of death); left out: the Toldos Yaakov Yosef and Rabbi Boruch Ber Leibowitz (sources disagree).
- Yahrzeits: 16 more figures (142 in all): the Chacham Tzvi, the Haflaah, the Nesivos, the Aruch HaShulchan; the
  Seraph of Strelisk, the Yismach Moshe, Rabbi Yitzchak of Vorka, the Tiferes Shlomo, Rabbi Yehoshua of Belz, the
  Ahavas Yisrael, the Minchas Elazar; the Ohr Sameach, the Alter of Novardok, the Rogatchover Gaon, Rabbi Elya Lopian;
  Rabbi Yitzchak Nissim. Confirmed the same way (13 by the documented day of death); left out: Rabbi Shlomo Kluger and
  the Alter of Kelm (sources disagree), Rav Wosner and Rabbi Shalom Messas (no source of the Hebrew day).
- Jewish calendar: its settings set the place of the zone the calendar goes by (they always set the main zone's, so a
  calendar by the second or third zone showed no times of the day). Each time zone in the clock settings has a Remove
  button (the main one goes back to the computer's zone); Clear beside the place clears only the place.
- On a vertical panel the clock's blocks are framed in the accent, with 6 px between them: each time zone, the weekday
  with the Hebrew date, and the civil date. There the whole Hebrew
  block (weekday and date) opens the calendar, shows the hand and runs the lights around it. Shorter labels of the place
  in the settings ("Place:", "Place of the zone:").
- The times of the day in the calendar end with midnight (chatzot halayla): halfway from sunset to the next sunrise,
  after the evening's candles or havdalah.
- During Hanukkah the clock's weekday has a hanukkiah after it with the number of candles to light this evening besides
  the shamash (1 from the evening of 24 Kislev, so on a Friday before the Shabbat; 8 on the evening of 1 or 2 Tevet).
  Each cell of the calendar shows the hanukkiah and the candles of its evening at its top left, and its tooltip too.
  The times of the day have a highlighted line with the hanukkiah, "Hanukkah candles: 3 + shamash", at nightfall; on a
  Friday before the Shabbat candles and at their time, after the Shabbat after the havdalah. In all 46 languages.
  The heading of the times shows the hanukkiah and the number too.
- Jewish calendar: the eclipses of 1900 to 2300 by their Hebrew names, Likui Chama (of the sun) and Likui Levana (of the
  moon), computed by Meeus' method (within about two minutes of NASA's catalogue). A day of an eclipse has its sign in
  its cell and its name in the tooltip; the details give its kind (total, annular, hybrid, partial, penumbral) and its
  greatest moment by the place's clocks, and for the moon's whether it is up at the place then. In all 46 languages.
- Jewish calendar: the actual (astronomical) new moon and full moon, not the molad's mean moment, by Meeus' method
  (within a minute of the published moments), for 1900 to 2300. A day of one has a new-moon or full-moon sign in its
  cell and the moment in its tooltip; the times of the day have a line for it, with the sign, where it falls among
  them, by the place's clocks. They are named in Hebrew: the new moon Molad Emet (the true molad) and the full moon
  Milui HaLevana, with the plain word beside the name. The signs of the moons and of the eclipses stand at the bottom
  right of the cell, beside the civil date, so they never run into the Hebrew month at the top. In all 46 languages.
- Jewish calendar: the changes between the summer and the winter prayers: Mashiv HaRuach UMorid HaGeshem (22 Tishrei,
  from Musaf), V'ten Tal U'Matar (in Israel from the evening before 7 Cheshvan; elsewhere from the evening of 4 December,
  5 December before a leap year, as the 60th day after the autumn equinox of Shmuel), V'ten Bracha (from the first
  weekday Ma'ariv after the first day of Pesach: the evening of 15 Nisan in Israel, of 16 Nisan elsewhere; the last
  Mincha that asks for rain is the eve of Pesach's) and Morid HaTal (15 Nisan, from Musaf; the Ashkenazi rite outside
  Israel says nothing, and the sentence says so). When the first Ma'ariv would be a Friday evening's, with no blessing of
  the years, the change is told on Saturday, for Motzei Shabbat (4 December 2026 is a Friday, so V'ten Tal U'Matar is
  5 December). A block in the cell (blue for the winter, orange for the summer), the sentence in the tooltip and in the
  day's card. In all 46 languages.
- Jewish calendar: the fast of the firstborn (14 Nisan, the Thursday 12 Nisan when the eve of Pesach is Shabbat), and the
  Shabbatot with names of their own, as a block beside the portion: the four before Pesach (Shekalim, Zachor, Parah,
  HaChodesh), HaGadol, Shuva, Chazon (before Tisha B'Av), Nachamu (after it) and Shirah (Beshalach); in English, Russian
  and Hebrew, the other languages have the English name, as the festivals do. The dates agree with an independent
  calendar (pyluach) over four centuries.
- Jewish calendar: for today (by the place's clocks) the times of the day have a line "Now" with the time, between the
  times it falls among; it moves by itself when the minute changes, while the calendar is in sight (a timer to the next
  minute; the window is not rebuilt). In all 46 languages.
- Telegram's media viewer is full screen again for the panels: it is one pixel wider than the monitor, and only a window
  of exactly a monitor's bounds was taken for full screen, so the panels stayed over its top. A window covering a
  monitor up to 8 pixels past its edges now counts.
- A window that draws its own title bar (Telegram's media viewer and chats, and other apps without the system's title
  bar) is brought into reach like the others when its top strip opens under a panel or off the screen; tool windows,
  popups and small windows are left alone. The log line of a move names the window's styles.
- Calendar: a year of months before and after the one shown is made ahead in hidden grids, nearest first, one at a time
  when the program is idle (the two next ones already when the calendar is prepared), so an arrow (or PageUp/PageDown)
  only brings one into sight; as the months go by, the grids that leave the year are reused for the ones that enter
  it. A month took 140–220 ms to make, nearly all of it the cells' templates and layout.
- Calendar: no names of the nations' days of the week. The start and end of a day of rest are one line,
  "16:16 (candle lighting) – 17:14 (nightfall)"; days of rest in a row name their days by number ("Day 4 … – Shabbat
  …"), as the molad does ("Molad of Tevet: Day 4, 11:11"); the tooltip and heading dates have no weekday. The rows of
  the times are a little closer.
- The clock's weekday has a memorial candle before it when the Hebrew day has a yahrzeit that is shown, and during the
  Omer the tree of the sefirot with the day of the count after it. The calendar shows the counting of the Omer as a card of its
  own (the tree of the sefirot, the day, from the seventh the weeks and days, and the day's sefira within the week's,
  "Hod she'b'Hod"); each cell of the Omer has the tree and the day at its top left (after the candle of a yahrzeit, or
  in its place), and its tooltip the day with its sefira. The candle and the tree are in a dictionary the calendar and the clock share. The place in the
  calendar says whether its festivals, Rosh Chodesh and portions are those of the Land of Israel or of the diaspora
  (by the place's country, as before).
- Jewish calendar: the memorial candle is brighter: a saturated amber glow, a deeper flame with an outline and an
  outlined candle, so it stands out on the light theme's glass.
- Jewish calendar: today stands out with a thick accent frame and the accent's soft fill.
- Settings: the Advanced tab has sub-tabs (General, Behavior, Multiple displays), as the notification area tab has.
- On a horizontal panel the task buttons fill its height (the list's height shared by its rows), not only their
  content's.
- Jewish calendar: the window takes the whole width of the work area; the grid has five weeks, and a sixth only for a
  month that reaches into it (the days always in order); each day is a card with its own
  fill and border.
- Jewish calendar: the candle lighting, the havdalah and the start and end of a Shabbat or festival are highlighted
  lines with two candles (the accent's soft fill and edge, the time in bold) among the times of the day.
- Modern Glass dialogs: the glass is denser and a little darker (buttons stay readable over a white page), and the
  windows draw their own title bar: dark and solid, its text light with a dark shadow and a close button (the system's
  caption showed its text on a black box over the glass).
- Yahrzeits: 42 more figures (88 in all) in four new categories (Breslov, Lithuanian, Sephardic, Yemenite) and the
  existing ones: Baal HaSulam, the Rabash, Rabbi Yehuda Tzvi Brandwein, Rav Philip Berg, Reb Noson and the Saba of
  Breslov, the Brisker Rav and Reb Chaim, the Netziv, Rav Shach, Rav Elyashiv, the Steipler, Rav Chaim Kanievsky, the
  Chida, the Kaf HaChaim, the Maharitz, Rav Kafih, the Rebbes of Ger, Belz, Satmar, Sanz, Ruzhin, Peshischa and more.
  Every day was confirmed by two or more sources and, where the date of death is documented, by the Hebrew calendar
  (a test); figures whose sources disagree (Rabbi Elchanan Wasserman) or whose day is not known (Rabbi Shalom Shabazi)
  are left out. The list is ordered by category and, within it, by birth.
- Jewish calendar: nothing in a cell is cut any more: the weeks are all as high as the fullest day, and only when
  the month does not fit on the screen do the weeks with less in them give way; once grown the window keeps its height
  (another month or day no longer makes it shrink and flicker), the window takes the height of its content up to the work area and stays by the clock. The details
  are cards: the day's special days as colored blocks with the start and end of the rest, the yahrzeits, the times of
  the day, the molad. Names: Mitteler Rebbe, Chatam Sofer, Kotzker Rebbe; a search leaves out what is in brackets.
- Jewish calendar: from the sunset of the place until midnight the day that is ending (its lower right half, its
  frame a little darker) and the day whose Hebrew date has begun (its upper left half) share a diagonal shade, a
  parallelogram across the two cells. A yahrzeit name shows, on hover, buttons to copy it and to look it up with Google
  in the system's browser (a one-color Chrome mark). The special days in the cells are in a larger font.
- Jewish calendar: a button beside Today switches the grid between civil months and Hebrew ones (1 Kislev to the end
  of Kislev); the arrows, PageUp and PageDown and the arrow keys move by the kind shown, and the choice is kept.
- Jewish calendar: the days of the week over the grid are numbered 1 (Sunday) to 6 (Friday), then Shabbat, with no
  day names of a language. The yahrzeits of the chosen day, above the times of the day, each follow a memorial candle,
  the eldest (born first) first; every figure has its year of birth.
- Jewish calendar cells: the Hebrew date stands out, the civil date is muted, the week's portion is an accent badge,
  the special days are blocks colored by kind (festival, Chol HaMoed, fast, minor day, Rosh Chodesh) and a memorial candle
  with its flame and glow, at the top left, marks a yahrzeit. The place's popup stands over the globe inside the window, shows on hover and a click pins or hides it.
  The Hebrew date of the clock shows the hand cursor.
- A double click on the Hebrew date is a click: it shows the Jewish calendar (its second click no longer closes what
  the first opened) and never opens the system's date and time settings.
- The Jewish calendar opens at once: it is made ahead, without being shown, when the pointer comes to the clock and
  once while the program is idle after its start; a click only shows it (one made on another day is made anew).
- Jewish calendar: the row above the month spans the window: the chosen day's civil date at the left, its week's
  portion in the middle and its Hebrew date at the right; the details no longer repeat them, and the place of the times
  of the day is in a popup under a globe beside their heading; the settings are a gear in the title bar and
  the Close button is gone (the title bar's cross and Escape close it). A click on the Hebrew date brings a calendar that
  lies under other windows up over them; only one in sight is closed by it.
- A restart on purpose (the Restart menu, the deploy script) no longer makes maximized windows jump: the new process
  holds the panels' strips of the last run as hidden shell AppBars from its first moment until its own panels have
  published theirs (Explorer recomputed the work area several times while it dropped the dead panels), and panels
  closing for such a restart keep their strips instead of releasing them one by one.
  The deploy script holds the strips itself (Hold-WorkAreaStrips.ps1) while the old instance dies, and the Restart menu's
  new instance takes them before it waits for the old one to exit; each lets go once the new instance holds them.
- Tray icons no longer turn into black or dashed squares after a start. Explorer's own copy of the icons was never
  updated while the program's tray ran, so the icons read from it at the next start held handles the applications had
  since destroyed (or that were reused for other icons). Each notify icon message the tray takes is now also sent on to
  Explorer (without the balloon, which it would show again), from the forward queue; and an icon that already has its
  application's own image keeps it when Explorer's copy is read.
- A faster start (panels shown in about 6 s instead of 9-11): Explorer's taskbar state is read from the registry
  instead of asked from a busy Explorer (and other ABM_GETSTATE queries about it are answered the same way); the
  program's own ABM_SETSTATE no longer waits for Explorer; the mouse hook installs without walking the process's
  modules, and mouse events no longer wait on the lock the installing thread holds; the SystemEvents thread is started
  off the UI thread; a window's process start time is read with one OpenProcess.
- The brand dictionaries (ModernControls, ModernMenus, FlatControls) are compiled to BAML instead of being parsed as
  XAML text at every start (the theme loads in a fraction of a second instead of 1-2 s). The windows' virtual desktops
  are asked from Explorer on a thread of their own while the theme and panels load.
- Panels of the manual layout are shown at their final size at once, not first across their whole screen side.
- Modern Glass: horizontal panels are taller: 40 px at the panel scale 1.2.
- The main time zone of the clock can be chosen too (the computer's by default), with its place, which is the place of
  the Hebrew date. The notification area of the settings is split into sub-tabs: General, Clock, Time zones (the three
  zones with their names and places) and Hebrew date.
- The second and the third time zone each have a place, asked for under the zone in the settings (a place chosen for a
  zone that is not chosen yet brings its own zone along). The calendar goes by the zone it is told to with the place of
  that zone: the date on its clock, the Hebrew day turning at the sunset of that place, the times of the day of that place
  on that zone's clock, and Israel or the diaspora by its country, not the main place's times shifted by an offset. A zone
  with no place shows no times. The main zone's place is the place of the Hebrew date, as before.
- The settings are easier on the eyes: the text of the settings window is a sixth larger, and the tabs of the Modern Glass
  theme are bright (not gray) and larger, the chosen one bold.
- The label of the active window's button on the panel is no longer bold, in any theme.
- The time zones of the clock are in the general settings (the clock group), not in the calendar's: the computer's own
  is the main one, and a second and a third can be chosen. Each has a check box for its name, the main one too: the
  clock then shows "8:05 Amsterdam", "10:05 Moscow", "15:05 Tokyo +1" (the name of the computer's zone is the first place
  of Windows' name for it, in the language of Windows). The calendar's settings choose which of the three the date, the
  change of the day and the times of the day go by (the computer's by default); the place window no longer has a time
  zone, only the place.
- The Jewish calendar is bigger and easier on the eyes: the window is 1240 by 800, the text 15 points (the headings
  19 to 20), the grid and the details column wider. A day's cell shows the Hebrew date at its very top right ("Kislev,
  13"), the festival or the portion in the middle and the civil date at its very bottom left ("23, November"; in Russian
  the month is in the genitive, "23, ноября"). The times of the day light up the whole row under the pointer, in the
  accent of the Modern Glass theme or in gray under the others.
- A newly opened window now always goes to the end of its panel's task list,
  and one of a pinned application goes right after that application's windows
  (the pin's own place when it has none), not to the end of the whole list. A
  window opened after start-up used to take over the first stale legacy slot
  (`exe#N`, kept from before windows had keys) of the saved order, and so
  appeared in the middle of the list; only the windows that existed at start-up
  take such a slot now.
- Restored windows are no longer moved or resized to fit the panel work area
  after they are shown, activated or dragged, so a window can be dragged past
  the panels or off the work area. Maximized windows are still fitted to the
  panels, and a window whose title bar cannot be reached is brought back (see
  Fixed).

### Fixed

- Jewish calendar: Yom HaAtzmaut and Yom HaZikaron are shown from 5709 (1949), Yom HaShoah from 5711 (1951) and Yom
  Yerushalayim from 5727 (1967), not in the years before the State of Israel; the move of Yom HaAtzmaut from a Monday to
  the Tuesday is of 5764 (2004), before it the day stayed on the Monday.
- Jewish calendar: candles are lit 40 minutes before sunset in Jerusalem and its neighbourhood (within 10 km of its
  centre), as the custom of the city has it, in the day's times and in the start of the rest; 18 minutes elsewhere.
- Hebrew months: in Japanese Heshvan is written with the katakana ヘ (it began with the hiragana へ); in Korean Tammuz is
  담무스 and Av is 아브, as the Korean Wikipedia has them. Russian: Rabbeinu Tam's nightfall is spelled as his yahrzeit
  is, "рабейну Там".
- A restart no longer lets the windows grow over the whole screen and shrink again when the panels are ready: the work
  area of the last layout is remembered with its screen and put back at the start of the next run, before the panels are
  made (with the watchdog that gives the original one back already running).
- A second click on the Hebrew date closes the calendar. A calendar that is open on another virtual desktop is brought
  to the desktop the user is on instead of taking the user to it (and so is the settings window, when it is open
  elsewhere): the program never switches desktops to follow a window.
- The clock no longer shows a tool tip with the date when the pointer is over it.
- Starting the program, or changing the theme, no longer freezes the panels for seconds: the panels' reservation of
  their strips of the screen is told to Explorer from a background queue, in order, instead of the UI thread waiting for
  Explorer's answer (it waited up to half a second for each of about ten messages, and more on a busy desktop; the
  answer is never used). Explorer is still told, and what is queued is sent before the program ends.

- A window whose title bar the mouse cannot reach is moved into the work area.
  Proxifier's main window once stayed shown at (-32000,-32000) and its rules
  dialog opened at (0,0), with the title bar under the top panel, so neither
  could be seen or dragged. When such a window is activated, or its taskbar
  button is clicked (the click moves it instead of minimizing it), it is moved,
  size kept, wholly into the work area (a window off every monitor goes to the
  middle). A window that is only shown is moved too when it is partly on a
  monitor; one shown off every monitor may be parked there on purpose and waits
  until it is activated. Full-screen windows stay. The icon and the caption
  buttons do not count as a handle: a window is left alone while at least
  48 x 12 px of the rest of its title bar are in reach, even when part of the
  title bar is under a panel or off the screen.
- Panels that are not shown after a logon are put right. Once, right after a
  Windows logon (the shell starts together with Explorer), the left and bottom
  panels were not there at all while the other two were, until the virtual
  desktops were switched back and forth; the log held nothing about it. For the
  first three minutes after start-up the panels are now checked: shown, not
  cloaked, opaque, topmost, and the window under some probed point of the panel
  is the panel itself. A panel that fails is named in the log with the reason
  (and the window covering it); it is put right at once, and if it still fails
  at the next check the panels are opened again, once. A panel that gave way to
  a full-screen app is left alone, and so is everything while auto-hide is on.
- A pinned application, such as Task Manager on the right panel, can be dragged
  to another panel: its pin and the application's rule move together (they did
  not move at all for a pin without a window, and for a running one only the
  window moved while the pin stayed and sent it back on the next click). Ctrl+drag
  still moves just the window.
- The panel a window shown on all desktops (Task Manager, a pinned Telegram)
  belongs to no longer depends on which code path looked it up. Windows reports
  an id for these that no desktop has, so a rule made by dragging was kept under
  that id but one made by launching a pin under the real desktop: the window went
  back to its old panel, or moved to another by itself seconds after it opened.
  All lookups now key such a window by the current desktop.

- Pinned applications are shown right after start-up again when Explorer has
  not recorded the current virtual desktop. After a display-driver reset or a
  resume Explorer recreates its session key without `CurrentVirtualDesktop` and
  writes it only on the first desktop switch; the panels then started on an
  unknown desktop and, as pins belong to a desktop, showed none until the
  desktops were switched back and forth. Without a record, the desktop is now
  taken from the open windows: the desktop most of them report to Explorer,
  skipping UltraWinBar's own windows, with windows shown on all desktops
  outvoted. The record still wins as soon as Explorer writes it.

## 2.5.1 (2026-11-01)

### Added

- The window journal names who is behind a window: an `opened` line ends with a
  sixth column, the owning process and the processes that started it, as
  `name(pid)[command line] <- parent(pid)[command line] <- ...` (up to four
  levels, command lines cut at 160 characters). `?(pid)` stands for a process
  that is already gone (a console window that lives for milliseconds) or that
  cannot be read (an elevated one), and a parent id that was reused by a newer
  process is not named. With it, a flashing console window such as `tzutil.exe`
  can be traced to what started it. The columns before it are unchanged, and
  `closed` lines have no sixth column. Command lines can hold secrets, so the
  journal stays opt-in and local.

## 2.5.0 (2026-10-20)

First release since 2.4.0. It also carries everything listed under 2.4.7-alpha
below, which was never published. The version is pinned at 2.5.0.0 in
`version.json`.

### Changed

- Start-up no longer leaves the screen without a taskbar: with the manual work
  area, Explorer's taskbar is hidden only after the panels are shown (it used
  to go a few seconds before they appeared). Panels that register as AppBars
  still hide it first.
- Explorer's notification icons are read beside start-up instead of before the
  panels exist (the read waits on Explorer and took 2-3 s); they are added in
  Explorer's order and the tray is announced to applications after them, or
  after 5 s if Explorer does not answer.
- The log gets `Startup: <stage> at N ms (+N ms)` lines, counted from the
  moment the process was created, so a slow start can be pinned on a stage.
- With debug logging on (the setting or the flag file), the log records every
  change of the stored task order (`Task order: edge=...; written <desktop>:
  +added -removed moved=N [...]; aligned <desktop>: ...`) and every dead window
  key pruned from it, and the task order snapshot is written for the flag file
  too (it used to need the setting), so a reordering can be traced to a desktop.

### Fixed

- After switching to a desktop for the first time (or back to one whose panel
  had not been laid out yet), task buttons could stay at half width on a nearly
  empty panel: the width was computed from the new desktop's list before it had
  any size. It is now taken from the panel's scroll viewer, and left unchanged
  while the panel has no width yet.
- Tasks shown on every desktop (pinned applications, windows shown on all
  desktops) keep one order across desktops instead of changing places when the
  desktop changes. Each desktop still stores its own order, but every write now
  rearranges the keys it shares with the other desktops into the written order
  (keys of one desktop only keep their slots), a window that first appears on a
  desktop takes the place another desktop keeps for it instead of the end, and
  dragging a button only swaps displayed buttons, leaving other desktops' keys
  where they were. Orders that had already diverged are aligned on the first
  update after the upgrade.
- The Win+K and Win+P flyouts were left about 64 px short of the right panel
  again: the move against the panel only recognised a flyout docked against
  Explorer's hidden taskbar while that taskbar sat at its shown position, and
  Explorer parks it past the screen edge once it is auto-hidden. The flyout is
  now matched against where the taskbar sits when shown. The log says why a
  flyout was left in place (`shell flyout ... left in place`).

## 2.4.7-alpha (2026-10-13)

### Added

- Opt-in window journal: while the separate "Log windows opening and closing"
  setting (Advanced, next to debug logging) is checked or
  `%LOCALAPPDATA%\UltraWinBar\window-log.enabled` exists, every taskbar window opening and closing is appended to
  `Logs\windows-YYYY-MM-DD.log` as `time, opened|closed, hwnd, executable, title`
  (tab-separated; the closing line keeps the last known title). Toggling the
  flag file takes effect immediately; files follow the 7-day log retention.
- Current-desktop launch policy for pinned applications and desktop icons:
  reuse windows available here (including applications shown on all desktops);
  otherwise open new browser, Explorer-folder and Notepad++ windows without
  moving their existing windows from other desktops. Other applications retain
  the existing transfer behavior.
- Desktop folder matching uses the requested folder path; shortcut launch
  arguments, working directory and administrator flags are preserved.

### Changed

- The version is pinned in `version.json` (all four components) and changes
  only by hand; commits no longer raise it.

### Fixed

- Resizing a panel by dragging its edge follows the pointer: moves are
  coalesced and jump straight to the size under the pointer (one layout per
  frame instead of one row per queued mouse event), the layout uses the new
  size instead of the previous one, and the shell reservation (10-370 ms per
  panel per step) is published once the size settles. Maximized windows then
  follow the new work area, keeping their frame.
- A pinned application whose process reports its image in 8.3 form (Process
  Explorer runs from `C:\PROGRA~1\Tool\tool.exe`) takes its pinned button
  instead of appearing as a separate task: the identifier uses the long path,
  and pins, assignments and order saved under the short spelling are merged into
  it on load.
- Folder launches from desktop icons and pinned shortcuts run through the desktop
  shell with foreground permission and normal-window presentation, rather than
  leaving new Explorer windows minimized and flashing for attention.
- Explorer task buttons no longer disappear/reappear on live-window replacement
  notifications. The original task object, order and progress/attention state
  survive replacement and return; actual window destruction still removes it.
- Windows ghost surfaces map to their original task during initial discovery,
  replacement and recovery, preventing duplicate task entries. Visibility and
  activation use the ghost surface while present.
- Desktop icon activation no longer fails while the C# COM binder scans Shell
  type libraries with duplicate GUIDs; desktop/folder/shortcut automation now
  uses CLR COM dispatch instead.
- The Connect (Win+K) and Project (Win+P) flyouts open flush against the panel
  instead of 64 px away: Windows docks them to Explorer's hidden taskbar, which
  keeps its own width, so they are moved to the work-area edge when they open.
- Task buttons appear instantly when switching virtual desktops: every desktop
  keeps its own cached task list, so a switch only makes the new desktop's list
  visible instead of clearing and rebuilding the shared list (which recreated
  every button and cost 100-250 ms). Window desktop ids stay cached across
  switches (only a move or close forgets one), and pinned-appearance updates
  run after the switch frame, so the cached list shows ~60-90 ms after
  Explorer records the switch instead of ~450 ms after the keypress.
- On a single monitor the panels are also registered with the shell as AppBars
  carrying their exact rectangles (positioning stays manual), so Explorer's own
  work-area recomputation on unlock reserves them instead of clearing the work
  area and stretching maximized windows over the panels.
- Maximized windows no longer stay stretched over the panels after unlocking the
  screen. Windows clears the reserved work area on unlock and re-maximizes
  windows to the full monitor; after the work area is restored (and on every
  unlock) those windows are refitted once to the panel work area, keeping
  their maximized state and restore bounds.
- The reserved work area is restored as soon as a display change clears it
  (a monitor waking while the screen is locked), and before refitting on
  unlock, so windows are usually already in place when the desktop appears.
- While the screen is locked the work area is checked every 250 ms and a
  cleared one is restored (and maximized windows refitted) before the desktop
  is shown. Windows of elevated processes cannot be moved by the panels and
  are reported as refused in the log.

## 2.4.0 (2026-09-30)

### Fixed

- Pinned applications no longer silently fail when their launch file has been
  removed: clicking opens a permanent-launcher selection dialog and saves the
  chosen target. Pinning a process from the current temporary directory also
  requires a permanent launcher, rather than retaining a disposable executable.
- A failed pinned launch is reported to the user as well as written to the log.

### Added

- Executable-path and executable-directory clipboard commands for running tasks
  and closed pinned applications; shortcut pins resolve their target.
- Live detailed-log switching via
  `%LOCALAPPDATA%\UltraWinBar\debug-logging.enabled`, without restarting.
  Creating/removing the flag toggles debug severity unless the `DebugLogging`
  setting itself keeps debug enabled; normal logs and configured masks remain.

## 2.3.0 (2026-09-28)

The Nerdbank.GitVersioning prefix moves from `2.2` to `2.3`; the version height
is offset so this release is exactly 2.3.0.

### Changed

- Every taskbar panel's task list is now driven by `TaskModelHost`, one
  coordinator that runs `TaskModel`'s pure computation once per dispatcher
  pass for every registered panel (including several panels sharing an edge
  under multi-monitor mode) instead of each panel separately recomputing
  order, pruning TaskOrder/TaskbarAssignments and rewriting pin
  PrimaryWindowKeys on its own; Settings is written once per pass, only for
  what actually changed.
- The taskbar list's per-window, per-panel edge filter no longer rebuilds the
  enabled-edges list or rescans every saved taskbar assignment on each refresh;
  both are cached and only invalidated when the settings they depend on change.
- Application and legacy window identifiers used for task pins, order and panel
  assignments are cached per window instead of rebuilt on every call; the cache
  refreshes when the source values change (for example a UWP window's
  AppUserModelID arriving late).
- The taskbar list's per-panel rebuild no longer rescans every window per pin,
  recomputes each window's legacy order key with its own full pass over the
  task source, or diffs the displayed list with repeated `Contains`/`IndexOf`
  scans; all three are now near-linear, with identical results.
- ManagedShell is vendored from its 0.0.358 source (`Support/vendor/ManagedShell`)
  and built with the app, so its defects are fixed at the source; the upstream
  version is unchanged.
- Every WinEvent hook goes through one primitive that keeps its callback rooted,
  removes it on the installing thread and never lets a handler exception reach
  user32; every Explorer-hosted COM object goes through one proxy with a single
  disconnect check, 2/5/15/60 s backoff and recreation on Explorer restart.
- UI controls and windows subscribe to Settings and virtual desktop changes
  weakly, so a missed unsubscribe can no longer keep a closed panel alive.
- The log gets a resource snapshot 5 minutes after start and then every 30
  minutes (handles, GDI/USER objects, threads, memory, hooks, subscribers), and
  exceptions on background threads and unobserved tasks are logged.
- Closed taskbar panels (settings/display changes, Explorer restart) are tracked
  with a bounded list of `WeakReference`s; the health snapshot forces a GC pass
  and logs how many are still alive, with their edge and screen, so a missed
  unsubscribe anywhere shows up without a profiler. The health line also reports
  the worst UI-thread delay and how often it crossed 250 ms/1 s over the
  interval, total `CallbackGuard` failures, how many `ShellComProxy` instances
  are currently unavailable, and log lines lost or dropped by the file logger.
  A panel counts as leaked only a minute after it closed.
- The native callback primitives (exception barrier, WinEvent hook, Explorer COM
  proxy) live in ManagedShell and are shared by the app and ManagedShell; the
  tray window procedure and window enumeration go through them too. Foreground
  and window-show events are delivered once per event instead of once per
  interested component (4 system hooks instead of 8 or more). A test rejects
  hooks, window classes and registry watches created outside these primitives.
- All low-level mouse hooks (panel and element drag, toolbar drag, desktop
  activation) share one hook on a dedicated thread, so a busy UI thread no
  longer delays every mouse event in the system or gets the hook removed by
  Windows; moving a panel to another edge no longer runs inside the hook.
- One registry watch implementation for virtual desktops and the Japanese IME
  mode; the thread-agnostic flag is used only on Windows 8 and later.
- The log is written on a background thread in batches; errors are written
  immediately together with everything queued before them. Debug and info
  messages are not formatted when their level is off. Debug logging is off by
  default for new installations.
- Window, tray and Explorer helper services have explicit start/stop/dispose
  states; stopping releases every hook, window and class, a failed start rolls
  back, and repeated stops are harmless.
- Faster window tracking: lookups by window handle use a dictionary; a title
  change redraws only that window (other windows of the same program only when
  it flashes); taskbar progress and overlay messages no longer allocate a
  temporary window object. The Win+number hotkey table is built in one pass
  over explorer.exe instead of copying it; tray messages no longer re-parse
  constant GUIDs or allocate an icon before checking that it is new.
- Windows that vanished without a notification, or whose handle was reused by
  another process, are removed from the task list by a sweep every 30 minutes
  and counted in the health line.
- Cross-process data (tray and AppBar messages, AppBar shared memory, shell
  hook pointers, overlay descriptions) is parsed in one place with tests on
  garbage input; tests use typed access to internals instead of reflection.
- Large files are split by responsibility (Start menu monitor, taskbar window,
  desktop activation, Japanese IME, settings, tests) without behavior changes.
- Fewer background wake-ups of the UI thread: keeping Explorer's taskbar hidden
  and keeping the app's tray window ahead of Explorer's now react to window
  events instead of 100 ms polls (a 2 s check remains as a safety net); the
  keyboard layout indicator reacts to foreground changes and polls every 750 ms
  instead of 200 ms; the Japanese IME kana/romaji mode is cached and re-read
  only when its registry value changes.

### Fixed

- Fix memory-unsafe COM declarations: desktop pin and move queries returned a
  4-byte `BOOL` into a 2-byte `VARIANT_BOOL`, and the immersive launcher
  interfaces lacked `PreserveSig`. A test now rejects any COM method with a hidden
  `[out, retval]`, unmarked `bool`/VARIANT, or a pointer-sized value declared as
  a 32-bit integer, in the app and in ManagedShell (shell item, image factory and
  service provider interfaces now return their real HRESULT).
- ManagedShell: ignore tray `WM_COPYDATA` payloads shorter than their structure
  instead of reading past the sender's buffer; destroy only icons the app owns;
  retry a failed window path lookup at most every 30 s; use pointer-sized
  handles and message parameters throughout.
- Exceptions in the Start menu, window placement, task recovery and registry
  watch callbacks are logged instead of terminating the process.
- Task buttons sort by cached ranks instead of rebuilding and scanning the saved
  order on every comparison; tray icons cache their order position.
- Only the live settings object is saved; a stray `Settings` instance can no
  longer overwrite the user's settings file.
- Fix a crash (access violation) after hours of uptime: ManagedShell's Start
  launcher visibility sink declared its COM methods without `PreserveSig`, so
  each Start open/close notification wrote a phantom return value through a
  garbage pointer. Start monitoring now uses its own correctly declared sink.
- Keep installed low-level mouse hooks rooted until they are removed, and remove
  drag hooks when a panel closes or a quick-launch button unloads. Moving a panel
  to another edge reopens the panels mid-drag; the old panel's hook callback
  could be freed and crash the next mouse event with an access violation.
- Build the notification-area view and icon controls only on the visible panel
  that hosts the tray; other panels no longer keep a live view or show balloons
  a second time after the tray moves.
- Query the foreground IME state with a bounded, hang-aware call so a hung
  application can no longer block the taskbar.
- Treat a foreground CoreWindow as the Start menu only when Start was opened
  from a taskbar or the launcher reports itself visible; Search, Action Center
  and the emoji panel no longer mark Start as open.
- Remove the previous language dictionaries before merging the next language,
  so switching languages no longer stacks resource dictionaries.
- Wait (up to 3 s) for the work-area change broadcast when the watchdog
  restores the work area and on shutdown, so other applications see it.
- Drop cached Start launcher COM objects after an Explorer restart.
- Stop restoring all-desktop pins through COM on every virtual desktop switch;
  restore them when the virtual desktop service reconnects. The initial pin
  import gives up on persistently failing windows after 5 attempts.
- Remove per-event diagnostic logging from the foreground, Start poller and task
  drag paths, and attach the console log only when a console exists.
- The tray window procedure no longer lets an exception reach user32, and AppBar
  requests from a process whose shared memory cannot be mapped (elevated or
  already exited) fail instead of crashing; AppBar forwarding and the Explorer
  tray icon import no longer leak native memory.
- Messages forwarded to Explorer (tray, AppBar, Win+number hotkeys, Show
  desktop) and IME control messages to the foreground application wait at most
  500 ms, so a hung Explorer or application can no longer freeze the taskbar.
- Action Center, the clock calendar and other Explorer flyouts, and the Start
  launcher, work again after an Explorer restart without restarting UltraWinBar;
  their COM references are released instead of leaked.
- Window tracking reinstalls its cloak and move hooks after an Explorer restart,
  rolls back a partial start, validates the minimize-rectangle pointer and
  bounds the overlay-icon description sent by other processes, and a failed
  icon load no longer stops the window's icon from ever updating.
- WinEvent hooks disposed from another thread are removed on their own thread;
  an Explorer COM object can no longer be created twice when activation
  re-enters; exceptions in hidden message windows are logged instead of
  silently dropped.
- The log never throws into its caller when a new log file cannot be created,
  and keeps at most 500 messages while no log is attached.
- Tray icon settings (Always show, Hide, Remove) are keyed by the icon's GUID or
  executable and ID, not its tooltip, so they survive tooltip changes such as
  unread counters.
- Shutdown on sign-out runs once and signals the work-area watchdog; settings
  are saved before an automatic restart after a crash.
- The Start menu opens beside the panels, not over them, also when opened with
  the Windows key or when its visibility notification arrives before it takes
  focus. With Open Shell, about half of the Start button presses left the menu
  where Open Shell put it: the menu takes focus as a hidden placeholder and is
  shown ~50 ms later, and a poll in that gap dropped the placement as "closed".
- The Open Shell menu no longer flashes at Open Shell's own position for
  20–50 ms before moving beside the panels: it stays transparent (layered alpha;
  cloaking another process's window is denied) until it is placed, then fades
  in over 150 ms, or at once with client-area animations off. A menu not placed
  within 2 s is shown where it is; its style is restored on close and on exit.
- Open Shell's avatar no longer flashes beside the menu's first position before
  jumping into place: moving it waits for Explorer's thread, so a hook thread
  keeps it transparent (per-pixel layered alpha, which takes effect at once)
  until it is placed, then it fades in with the menu. It is shown at the latest
  when the menu is fully opaque, and made opaque again if the menu closes first.
  An event for a moved or newly shown avatar is never taken for the echo of our
  own alpha change, which let it show for a frame when Open Shell was slow.
- The first Start press after launch no longer shows the menu and avatar where
  Open Shell puts them: the placement path is warmed up (JIT, P/Invoke stubs,
  hook thread) once when the app is idle after start.

## 2.2.0 (2026-09-28)

First published release of the UltraWinBar fork. The Nerdbank.GitVersioning
prefix moves from `2.1` to `2.2` (2.1 was never released); the version height is
offset so this release is exactly 2.2.0.

### Added

- The clock context menu offers Restart UltraWinBar next to Exit; like Exit,
  it follows the "show Exit menu item" setting.
- Tray-icon activation now records the owning process before its callback is
  sent. A double-click pre-moves its sole foreign window; other clicks wait for
  the exact window activation before attempting a move.

### Fixed

- Prune task-order entries and per-window taskbar assignments whose window no
  longer exists; the saved order previously grew with every window ever opened.
  Entries of windows that still exist but are not yet listed are kept.
- Stop an endless task-list rebuild loop when a pinned application's primary
  window could not be verified through its process; liveness now comes from the
  current window list.
- Stop rebuilding every task list and re-checking all-desktop application pins
  on each window activation; pins are restored only when windows are added or
  the task list is reset.
- Skip windows that can never become task buttons before creating task objects
  during desktop-switch recovery, and refresh panels only when something changed.
- Serialize settings once per burst of changes on the UI thread instead of on
  every property change; shutdown still saves the latest state synchronously.
- Keep logs for 7 days instead of 7 hours and roll to a new file at 20 MB.
- Run the desktop-activation mouse hook on its own thread so a busy UI thread no
  longer delays mouse input system-wide or lets Windows silently remove the hook.
- Install the Start menu placement event hook only while a Start menu is being
  positioned instead of listening to all system object events permanently.
- Update the clock at the next second or minute boundary instead of every 200 ms.
- Re-attach the virtual-desktop registry watch when Explorer creates or recreates
  its desktop key, so desktop switches keep being detected after an Explorer restart.
- Ignore repeated Loaded events on task buttons to avoid duplicate subscriptions.
- Recreate the virtual-desktop manager and Start menu visibility COM objects
  after an Explorer restart or a disconnected-proxy error instead of querying
  dead proxies for the rest of the session.
- Detect Start menu visibility from shell events; the fallback poll runs every
  1.5 s and speeds up to 100 ms only while a Start menu is open or positioned.
- Filter only the added, removed or changed window instead of refreshing every
  task list, skip monitor changes that cannot affect a panel, and share desktop
  lookups between panels. Windows moved between desktops are re-checked from
  their cloak events.
- Drop expired missed tray notifications and keep at most 10 per icon; remove
  tray icons whose owner window no longer exists when the tray is hovered.
- Post desktop-activation cancels from the mouse hook only while something is
  armed, and log from the UI thread instead of the hook thread.
- Recompute the saved original work area and restart its crash watchdog when
  the primary monitor's bounds change, so exit and crash restore the right size.
- Keep retrying the virtual-desktop manager and Start visibility objects with
  backoff when Explorer has not registered them yet after a restart, instead of
  losing desktop filtering or Start detection until UltraWinBar restarts.
- Re-read the current desktop before re-checking windows from cloak events.
- Release closed panels: tray and task views are detached from ManagedShell's
  app-lifetime collections, which kept every panel ever created alive (about
  1 MB each) across theme, language, edge and display changes. The hidden
  Quick Launch toolbar no longer keeps a folder watcher per panel.
- Reopen panels once per settings change instead of up to four times.
- Recreate shell COM objects before reopening panels after an Explorer
  restart, and re-filter panels when the desktop manager becomes available.
- Cache window desktops per dispatcher pass for task-assignment filtering and
  skip all-desktop pin restores for windows of apps that were not remembered.
- Stop leaking inverted tray-icon effects (and the panels holding them) through
  a static shader event, and guard repeated Loaded events on the Start button
  and task thumbnails.
- Tick clocks only in the panel that shows them, read the keyboard layout
  without per-poll allocations or exceptions, reload the System theme once per
  color broadcast, and leave balloon promotion to the panel hosting the tray.
- Coalesce work-area recovery events and pause recovery after repeated resets
  to prevent conflicts from continuously resizing desktop windows. Pauses grow
  from 1 to 5 to 15 minutes while a conflict persists, a single probe runs after
  each pause, and reopening the panels restores full recovery.
- Retry a deferred work-area recovery when its cooldown ends instead of waiting
  for the next window activation.
- Stop correcting window bounds on every location-change event, and suppress
  repeated placement attempts when applications refuse the requested bounds.
  Windows moved programmatically or with Win+Arrow are now constrained on
  their next show or activation rather than immediately.
- Report work-area notification enumeration failures only when enumeration fails.
- Restore the reserved screen work area if Windows or Explorer clears it while
  the panels remain open, so maximized windows continue to avoid the panels.
- Keep maximized window geometry under Windows control instead of resizing it
  on every activation; restored windows still move clear of panel edges.
- Desktop executable shortcuts with one matching window on another desktop can
  bring that window forward before Explorer launches the shortcut, avoiding a
  visible round trip between desktops. Ambiguous shortcuts keep the existing
  activation behavior.
- The experimental desktop guard attempts a bounded return when Windows
  switches after a move or before the target window's activation event. Later
  user input cancels the return.

## 2.0 development (unreleased)

The Nerdbank.GitVersioning prefix is raised from `1.23` to `2.0`. No 2.0 release
was published.

### Added

- Taskbar context-menu shortcuts to Windows Settings and Computer Management,
  plus a submenu for This PC and individual drives in File Explorer.
- An opt-in experimental guard that tries to move an already open window to the
  current virtual desktop after a desktop-icon double-click or pinned-launcher
  activation. Diagnostic logs record when Windows switches desktops first.

### Changed

- Exit is available only from the clock's right-click menu.
- The first Taskbar settings page is split into five localized sub-tabs.
- Vertical-panel clock bottom spacing is set to 22.5 physical pixels and scaled
  for DPI and taskbar scale.

### Fixed

- Settings writes are coalesced and atomic; unreadable settings are preserved
  before defaults replace them.
- Work-area notifications no longer block the UI while waiting on other windows.
- Window-specific taskbar assignments use window-lifetime IDs, and saved order
  validation checks process start time to avoid reused PID/HWNDs.
- Low-level mouse hook callbacks follow the Win32 contract and recover when a
  drag hook cannot be installed.
- Incomplete desktop-pin imports remain retryable instead of saving partial data.
- Task lists now refresh from shell window changes without WPF live shaping,
  preventing collection changes during its deferred filtering pass.

### Previously documented changes

The following sections retain the history recorded for the preceding 1.23.x
development work.

### Committed on 2026-09-22

#### Added

- Up to four taskbars per enabled screen, one per edge, with configurable edge
  priority through **Stretch**. Applications can be assigned to a panel by dragging;
  Ctrl+drag assigns only the selected window, with commands to reset assignments.
  ([f5609b6](https://github.com/PHPCraftdream/UltraWinBar/commit/f5609b6))
- **Make main** selects the default panel for unassigned windows. The clock,
  notification area, Start button, and language indicator have independent panel
  locations; clock, tray, and language blocks can be dragged between panels.
  ([7829ebf](https://github.com/PHPCraftdream/UltraWinBar/commit/7829ebf),
  [0b960b3](https://github.com/PHPCraftdream/UltraWinBar/commit/0b960b3))
- Independent row counts and widths for each edge, retaining the previous global
  size as a fallback for edges without an override.
  ([e33ac5a](https://github.com/PHPCraftdream/UltraWinBar/commit/e33ac5a))
- Saved task-button order per panel and drag-to-reorder within the same panel.
  ([3041e71](https://github.com/PHPCraftdream/UltraWinBar/commit/3041e71),
  [d667152](https://github.com/PHPCraftdream/UltraWinBar/commit/d667152))
- Separate Quick Launch shortcut sets per panel, context-menu reassignment, and
  cross-panel dragging. The initial Ctrl requirement was removed: plain dragging
  both reorders shortcuts and moves them between panels. Reordering one panel
  preserves the other panels' saved entries. Quick Launch is hidden by the later
  pinned-launcher changes documented below.
  ([501ce09](https://github.com/PHPCraftdream/UltraWinBar/commit/501ce09),
  [7a43a19](https://github.com/PHPCraftdream/UltraWinBar/commit/7a43a19),
  [0cce5c8](https://github.com/PHPCraftdream/UltraWinBar/commit/0cce5c8))

#### Changed

- The Start button became icon-only, without the reserved space for its old label.
  ([07bdd46](https://github.com/PHPCraftdream/UltraWinBar/commit/07bdd46))
- Flattened the Zune theme's vertical-panel background and task-button side borders
  and background to remove glare and visible stripes. The later all-theme cleanup
  is listed in the follow-up changes section.
  ([52f333d](https://github.com/PHPCraftdream/UltraWinBar/commit/52f333d),
  [9e13577](https://github.com/PHPCraftdream/UltraWinBar/commit/9e13577),
  [96e20f0](https://github.com/PHPCraftdream/UltraWinBar/commit/96e20f0))
- Improved Start-menu placement around the invoking panel: cached the modern
  launcher per monitor, added foreground-event positioning for modern Start and
  Open-Shell, retried position drift, and added related user-picture positioning.
  Further Open-Shell corrections are listed in the follow-up changes below.
  ([1c3d0d4](https://github.com/PHPCraftdream/UltraWinBar/commit/1c3d0d4),
  [8a3c860](https://github.com/PHPCraftdream/UltraWinBar/commit/8a3c860),
  [a1e868f](https://github.com/PHPCraftdream/UltraWinBar/commit/a1e868f),
  [43cdd40](https://github.com/PHPCraftdream/UltraWinBar/commit/43cdd40),
  [834f0b4](https://github.com/PHPCraftdream/UltraWinBar/commit/834f0b4))

#### Fixed

- Panel freezes caused by the task-order save/refresh feedback loop: unchanged
  orders no longer trigger another write, and reordering refreshes its owning
  panel instead of broadcasting a recursive refresh to all panels.
  ([2cb28ec](https://github.com/PHPCraftdream/UltraWinBar/commit/2cb28ec),
  [2f11dc0](https://github.com/PHPCraftdream/UltraWinBar/commit/2f11dc0))
- Blank task lists and interrupted assignment updates during collection changes
  or transient window-enumeration failures.
  ([0b8c591](https://github.com/PHPCraftdream/UltraWinBar/commit/0b8c591),
  [ea94587](https://github.com/PHPCraftdream/UltraWinBar/commit/ea94587))
- Reordering that did not immediately update the visible list, competing drag
  handlers, and dragging being unavailable with only one panel enabled. The final
  implementation uses mouse capture, explicit completion, cursor feedback, and
  insertion markers based on actual screen bounds, including RTL layouts.
  ([f0733c5](https://github.com/PHPCraftdream/UltraWinBar/commit/f0733c5),
  [1de7776](https://github.com/PHPCraftdream/UltraWinBar/commit/1de7776),
  [3cd02f0](https://github.com/PHPCraftdream/UltraWinBar/commit/3cd02f0))
- Changing window titles no longer directly invalidate order keys. This commit
  introduced executable/ordinal keys; their remaining restart ambiguity is
  addressed by the window-lifetime identities in the follow-up changes below.
  ([5b07df5](https://github.com/PHPCraftdream/UltraWinBar/commit/5b07df5))
- Task-list and tray-toggle orientation now follows the hosting panel rather
  than the primary panel. Language-indicator dragging receives the mouse-down
  event before its inner button consumes it.
  ([5ac6b28](https://github.com/PHPCraftdream/UltraWinBar/commit/5ac6b28),
  [729b4ad](https://github.com/PHPCraftdream/UltraWinBar/commit/729b4ad))
- Notification icons with invalid owner windows are hidden, and icons without a
  bitmap wait for it to arrive instead of briefly displaying an empty image.
  ([7e7063e](https://github.com/PHPCraftdream/UltraWinBar/commit/7e7063e),
  [7dda9e2](https://github.com/PHPCraftdream/UltraWinBar/commit/7dda9e2))
- Clock actions run on a completed click rather than mouse-down, so dragging does
  not open the calendar. Clicking an already-open notification center no longer
  immediately reopens it after dismissal.
  ([9921de2](https://github.com/PHPCraftdream/UltraWinBar/commit/9921de2),
  [74282f7](https://github.com/PHPCraftdream/UltraWinBar/commit/74282f7))
- Computed primary-panel size properties are excluded from JSON serialization;
  per-edge size records remain the persisted source of truth.
  ([eca0c84](https://github.com/PHPCraftdream/UltraWinBar/commit/eca0c84))

### Previously documented follow-up changes

#### Added

- Per-panel pinned applications: closed applications retain a launcher, while
  running applications show a titled task button for each window.
- Virtual-desktop-specific panel assignments, pins, and task ordering.
- Task-menu commands to show an application's windows on all desktops, move one
  window to another desktop, or move all windows of an application.
- Persistent all-desktop pin preferences, restored at startup and when windows
  are activated, added, or desktops change. Existing system pins are imported once.
- Configurable log masks with wildcard inclusion and exclusion rules.
- Drag cursors and destination markers for moving the clock, notification area,
  and language indicator between panels.
- Optional Hebrew date above the ordinary date, with `Day-1` through `Day-6` and
  `Shabbat` weekday labels. Calendar arithmetic uses .NET `HebrewCalendar`;
  month labels use bundled Unicode CLDR data for the interface languages, with
  a Belarusian Cyrillic override.
- Regression checks for desktop rules, settings persistence, task identities,
  calendar formatting, localization coverage, and off-screen theme/layout rendering.

#### Changed

- Unified UltraWinBar naming across source files, namespaces, binaries,
  installer, settings storage, startup registration, localization, and documentation.
- New flat, two-tone application icon with nine sizes from 16 to 256 pixels,
  shared by the executable, installer, and settings window.
- Update checks and download links target the project's GitHub releases.
- All 19 built-in theme resource files use solid fills instead of gradients.
  Shared flat templates replace layered bevels and glossy taskbar controls,
  with distinct hover, active, pressed, and flashing states.
- Modern vector Start icons with contrast appropriate to each theme.
- The Start button and closed pinned launchers are clickable across the full
  width of vertical panels; launcher icons remain centered. Horizontal launchers
  remain compact.
- Task-button separation is 2 DIPs. Vertical-panel clock spacing is 22.5
  physical pixels, adjusted for DPI and taskbar scale, without changing
  horizontal panels.
- Larger task, clock, and language-indicator text; larger task and notification
  icons, with notification-icon padding and a rounded notification-area outline.
- Quick Launch is hidden in favor of per-panel pinned launchers. Inactive taskbar
  drag grips are hidden; the language-indicator grip remains available.
- Settings use a resizable, bounded window, independently scrollable tabs, aligned
  form columns, and system UI fonts independent of the selected taskbar theme.
- Expanded Belarusian Cyrillic translations, including new desktop and calendar
  controls, with checks for missing translations and unintended Latin text.

#### Fixed

- Task order no longer depends on the discovery ordinal of same-application
  windows. Window/process-lifetime identities preserve positions when UltraWinBar
  restarts while those windows remain open. Pinned applications also remember
  which window occupies their launcher position.
- Windows omitted during startup because they were on another virtual desktop
  are recovered when they become available, instead of requiring individual activation.
- Multiple windows of a pinned application retain separate task buttons rather
  than disappearing into a single launcher.
- Centering and icon clipping in pinned task buttons.
- Open-Shell requests now target Explorer's taskbar window rather than the
  application's same-class helper window. Removed the delayed second request.
- Start-menu placement corrections respond to window events, skip the temporary
  placeholder window, and avoid stealing focus. Menu bounds and the separate
  user-picture position are adjusted for the initiating panel.
- Improved single-monitor startup layout and work-area reservation, including
  top/left edge priority, window placement guards, and work-area restoration on exit.
- Settings no longer retain obsolete theme dictionaries after switching themes.
  Removed the manual screen-width measurement that could lay out settings fields
  outside the actual window.
- Enabling the Hebrew date no longer changes the user's time or ordinary-date
  format. Month names follow the selected interface language independently.

### Current limitations

- System-wide desktop pinning and window-move commands currently support Windows
  10 builds 19041–19045. They are disabled on unsupported builds.
- Exact task-order restoration applies to windows that remain open during an
  UltraWinBar restart. It does not recreate application windows after a reboot;
  older ordinal-based order records can only be migrated on a best-effort basis.
- The Hebrew date changes at local midnight, not sunset. No location-based
  astronomical calculation is performed.
- Update checks require a published GitHub release; making the repository public
  alone does not publish an update.
