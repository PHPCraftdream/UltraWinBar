using System;
using System.Collections.Generic;
using System.Linq;

namespace UltraWinBar.Utilities
{
    /// <summary>A great Torah figure whose day of passing (yahrzeit) is kept: its day in the Hebrew calendar and its names.</summary>
    public sealed record Tzaddik(string Key, string Category, int Month, int Day, string English, string Russian, string Hebrew)
    {
        /// <summary>The name in the language of the settings ("עברית", "русский"; anything else is English).</summary>
        public string Name(string language) => language == "עברית" ? Hebrew : language == "русский" ? Russian : English;
    }

    /// <summary>
    /// The yahrzeits of well-known Torah figures, in categories. The days are the traditional ones of the Hebrew calendar,
    /// listed in the numbering of <see cref="JewishCalendar.MonthNumber"/> (Adar, kept in Adar II in a leap year, is 7).
    /// The list is compiled for this program: for the biblical figures and the sages the day is the one tradition gives,
    /// for the rest it was checked against the documented date of death (see the tests); where traditions differ, the
    /// widespread day is given. A person is chosen or not by key; the list can grow without changing what is saved.
    /// </summary>
    public static class Yahrzeits
    {
        // Categories in the order they are shown: key, then the names in English, Russian and Hebrew.
        private static readonly string[][] CategoryData =
        {
            new[] { "Biblical", "Biblical figures", "Библейские праведники", "דמויות מן המקרא" },
            new[] { "Sages", "Sages of the Mishnah and Talmud", "Мудрецы Мишны и Талмуда", "תנאים ואמוראים" },
            new[] { "Rishonim", "Rishonim", "Ришоним", "ראשונים" },
            new[] { "Acharonim", "Acharonim", "Ахароним", "אחרונים" },
            new[] { "Kabbalists", "Kabbalists", "Каббалисты", "מקובלים" },
            new[] { "Chassidic", "Chassidic masters", "Хасидские ребе", "אדמו״רים וחסידים" },
            new[] { "Chabad", "Chabad Rebbes", "Ребе Хабада", "אדמו״רי חב״ד" },
            new[] { "Recent", "Recent generations", "Недавние поколения", "דורות אחרונים" },
        };

        // key | category | month | day | English | Russian | Hebrew
        private static readonly string[] PeopleData =
        {
            "moshe|Biblical|7|7|Moshe Rabbeinu|Моше рабейну|משה רבנו",
            "aharon|Biblical|12|1|Aharon HaKohen|Аарон а-Коэн|אהרן הכהן",
            "miriam|Biblical|8|10|Miriam the Prophetess|Мирьям-пророчица|מרים הנביאה",
            "yehoshua|Biblical|8|26|Yehoshua bin Nun|Йегошуа бин Нун|יהושע בן נון",
            "shmuel|Biblical|9|28|Shmuel HaNavi|Шмуэль-пророк|שמואל הנביא",
            "david|Biblical|10|6|David HaMelech|Царь Давид|דוד המלך",
            "rachel|Biblical|2|11|Rachel Imeinu|Рахель-праматерь|רחל אמנו",
            "rashbi|Sages|9|18|Rabbi Shimon bar Yochai (Rashbi)|Рабби Шимон бар Йохай (Рашби)|רבי שמעון בר יוחאי",
            "meir|Sages|9|14|Rabbi Meir Baal HaNess|Рабби Меир Баал а-Несс|רבי מאיר בעל הנס",
            "rebbi|Sages|3|15|Rabbi Yehuda HaNasi (Rebbi)|Рабби Йегуда а-Наси (Рабби)|רבי יהודה הנשיא",
            "rashi|Rishonim|11|29|Rashi|Раши|רש״י",
            "rambam|Rishonim|4|20|Rambam (Maimonides)|Рамбам (Маймонид)|הרמב״ם",
            "ramban|Rishonim|8|11|Ramban (Nachmanides)|Рамбан (Нахманид)|הרמב״ן",
            "rabbeinu_tam|Rishonim|11|4|Rabbeinu Tam|Рабейну Там|רבנו תם",
            "yosef_karo|Acharonim|8|13|Rabbi Yosef Karo|Рабби Йосеф Каро|רבי יוסף קארו",
            "maharal|Acharonim|13|18|Maharal of Prague|Маараль из Праги|המהר״ל מפראג",
            "vilna_gaon|Acharonim|1|19|Vilna Gaon (Gra)|Виленский гаон (Гра)|הגר״א מווילנא",
            "chaim_volozhin|Acharonim|10|14|Rabbi Chaim of Volozhin|Рабби Хаим из Воложина|רבי חיים מוולוז׳ין",
            "akiva_eiger|Acharonim|1|13|Rabbi Akiva Eiger|Рабби Акива Эйгер|רבי עקיבא איגר",
            "chasam_sofer|Acharonim|1|25|Chasam Sofer|Хатам Софер|החתם סופר",
            "arizal|Kabbalists|12|5|Arizal (Rabbi Yitzchak Luria)|Аризаль (рабби Ицхак Лурия)|האר״י הקדוש",
            "ramak|Kabbalists|11|23|Ramak (Rabbi Moshe Cordovero)|Рамак (рабби Моше Кордоверо)|הרמ״ק",
            "ohr_hachaim|Kabbalists|11|15|Ohr HaChaim HaKadosh|Ор а-Хаим а-Кадош|האור החיים הקדוש",
            "ramchal|Kabbalists|9|26|Ramchal (Rabbi Moshe Chaim Luzzatto)|Рамхаль (рабби Моше-Хаим Луццатто)|הרמח״ל",
            "baba_sali|Kabbalists|5|4|Baba Sali|Баба Сали|בבא סאלי",
            "ben_ish_chai|Kabbalists|13|13|Ben Ish Chai|Бен Иш Хай|הבן איש חי",
            "baal_shem_tov|Chassidic|10|6|Baal Shem Tov|Баал Шем Тов|הבעל שם טוב",
            "maggid|Chassidic|3|19|Maggid of Mezritch|Магид из Межирича|המגיד ממזריטש",
            "elimelech|Chassidic|7|21|Rabbi Elimelech of Lizhensk|Рабби Элимелех из Лиженска|רבי אלימלך מליז׳ענסק",
            "levi_yitzchak|Chassidic|1|25|Rabbi Levi Yitzchak of Berditchev|Рабби Леви-Ицхак из Бердичева|רבי לוי יצחק מברדיטשוב",
            "nachman|Chassidic|1|18|Rabbi Nachman of Breslov|Рабби Нахман из Бреслава|רבי נחמן מברסלב",
            "chozeh|Chassidic|12|9|Chozeh of Lublin|Хозе из Люблина|החוזה מלובלין",
            "kotzk|Chassidic|5|22|Rebbe of Kotzk|Коцкий ребе|הרבי מקוצק",
            "alter_rebbe|Chabad|4|24|Alter Rebbe (Rabbi Shneur Zalman of Liadi)|Алтер Ребе (рабби Шнеур-Залман из Ляд)|אדמו״ר הזקן",
            "mittler|Chabad|3|9|Mittler Rebbe|Миттлер Ребе|האדמו״ר האמצעי",
            "tzemach|Chabad|8|13|Tzemach Tzedek|Цемах Цедек|הצמח צדק",
            "maharash|Chabad|1|13|Rebbe Maharash|Ребе Махараш|האדמו״ר מהר״ש",
            "rashab|Chabad|8|2|Rebbe Rashab|Ребе Рашаб|האדמו״ר הרש״ב",
            "rayatz|Chabad|5|10|Rebbe Rayatz|Ребе Раяц|האדמו״ר הריי״ץ",
            "rebbe|Chabad|11|3|The Lubavitcher Rebbe|Любавичский Ребе|הרבי מליובאוויטש",
            "salanter|Recent|5|25|Rabbi Yisrael Salanter|Рабби Исраэль Салантер|רבי ישראל סלנטר",
            "chafetz_chaim|Recent|13|24|Chafetz Chaim|Хафец Хаим|החפץ חיים",
            "rav_kook|Recent|13|3|Rav Kook|Рав Кук|הראי״ה קוק",
            "chazon_ish|Recent|2|15|Chazon Ish|Хазон Иш|החזון איש",
            "moshe_feinstein|Recent|7|13|Rabbi Moshe Feinstein|Рабби Моше Файнштейн|רבי משה פיינשטיין",
            "ovadia|Recent|2|3|Rabbi Ovadia Yosef|Рав Овадия Йосеф|הרב עובדיה יוסף",
        };

        /// <summary>All the figures, by category in the order of <see cref="Categories"/>, within it by the time they lived.</summary>
        public static IReadOnlyList<Tzaddik> All { get; } = PeopleData.Select(line =>
        {
            string[] part = line.Split('|');
            return new Tzaddik(part[0], part[1], int.Parse(part[2]), int.Parse(part[3]), part[4], part[5], part[6]);
        }).ToArray();

        /// <summary>The keys of the categories, in the order they are shown.</summary>
        public static IReadOnlyList<string> Categories { get; } = CategoryData.Select(category => category[0]).ToArray();

        public static string CategoryName(string category, string language)
        {
            string[] data = CategoryData.First(item => item[0] == category);
            return data[language == "עברית" ? 3 : language == "русский" ? 2 : 1];
        }

        public static IEnumerable<Tzaddik> OfCategory(string category) => All.Where(person => person.Category == category);

        /// <summary>
        /// The figures whose yahrzeit is on the date, except those whose keys are in hidden. None for a date outside the
        /// range of the Hebrew calendar.
        /// </summary>
        public static IReadOnlyList<Tzaddik> On(DateTime date, ICollection<string> hidden = null)
        {
            date = date.Date;
            var calendar = new System.Globalization.HebrewCalendar();
            if (date < calendar.MinSupportedDateTime || date > calendar.MaxSupportedDateTime) return Array.Empty<Tzaddik>();
            int month = JewishCalendar.MonthNumber(date), day = calendar.GetDayOfMonth(date);
            return All.Where(person => person.Month == month && person.Day == day && (hidden == null || !hidden.Contains(person.Key))).ToArray();
        }
    }
}
