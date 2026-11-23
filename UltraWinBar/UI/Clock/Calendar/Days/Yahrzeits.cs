using System;
using System.Collections.Generic;
using System.Linq;

namespace UltraWinBar.Utilities
{
    /// <summary>A great Torah figure whose day of passing (yahrzeit) is kept: its day in the Hebrew calendar and its names.</summary>
    public sealed record Tzaddik(string Key, string Category, int Month, int Day, string English, string Russian, string Hebrew, int Born = 0)
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
            new[] { "Breslov", "Breslov", "Бреславские хасиды", "חסידות ברסלב" },
            new[] { "Chabad", "Chabad Rebbes", "Ребе Хабада", "אדמו״רי חב״ד" },
            new[] { "Lithuanian", "Lithuanian sages and roshei yeshiva", "Литовские гаоны и главы ешив", "גדולי ליטא" },
            new[] { "Sephardic", "Sephardic and Eastern sages", "Сефардские и восточные мудрецы", "חכמי ספרד והמזרח" },
            new[] { "Yemenite", "Yemenite sages", "Йеменские мудрецы", "חכמי תימן" },
            new[] { "Recent", "Recent generations", "Недавние поколения", "דורות אחרונים" },
        };

        // key | category | month | day | English | Russian | Hebrew | year of birth (anno mundi: tradition for the biblical
        // figures, the usual date + 3760 for the rest; approximate where it is not known exactly)
        private static readonly string[] PeopleData =
        {
            "moshe|Biblical|7|7|Moshe Rabbeinu|Моше рабейну|משה רבנו|2368",
            "aharon|Biblical|12|1|Aharon HaKohen|Аарон а-Коэн|אהרן הכהן|2365",
            "miriam|Biblical|8|10|Miriam the Prophetess|Мирьям-пророчица|מרים הנביאה|2362",
            "yehoshua|Biblical|8|26|Yehoshua bin Nun|Йегошуа бин Нун|יהושע בן נון|2406",
            "shmuel|Biblical|9|28|Shmuel HaNavi|Шмуэль-пророк|שמואל הנביא|2830",
            "david|Biblical|10|6|David HaMelech|Царь Давид|דוד המלך|2854",
            "rachel|Biblical|2|11|Rachel Imeinu|Рахель-праматерь|רחל אמנו|2172",
            "rashbi|Sages|9|18|Rabbi Shimon bar Yochai (Rashbi)|Рабби Шимон бар Йохай (Рашби)|רבי שמעון בר יוחאי|3860",
            "meir|Sages|9|14|Rabbi Meir Baal HaNess|Рабби Меир Баал а-Нес|רבי מאיר בעל הנס|3870",
            "rebbi|Sages|3|15|Rabbi Yehuda HaNasi (Rebbi)|Рабби Йегуда а-Наси (Рабби)|רבי יהודה הנשיא|3895",
            "rashi|Rishonim|11|29|Rashi|Раши|רש״י|4800",
            "rambam|Rishonim|4|20|Rambam (Maimonides)|Рамбам (Маймонид)|הרמב״ם|4898",
            "ramban|Rishonim|8|11|Ramban (Nachmanides)|Рамбан (Нахманид)|הרמב״ן|4954",
            "rabbeinu_tam|Rishonim|11|4|Rabbeinu Tam|Рабейну Там|רבנו תם|4860",
            "yosef_karo|Acharonim|8|13|Rabbi Yosef Karo|Рабби Йосеф Каро|רבי יוסף קארו|5248",
            "maharal|Acharonim|13|18|Maharal of Prague|Маараль из Праги|המהר״ל מפראג|5280",
            "vilna_gaon|Acharonim|1|19|Vilna Gaon (Gra)|Виленский гаон (Гра)|הגר״א מווילנא|5480",
            "chaim_volozhin|Acharonim|10|14|Rabbi Chaim of Volozhin|Рабби Хаим из Воложина|רבי חיים מוולוז׳ין|5509",
            "akiva_eiger|Acharonim|1|13|Rabbi Akiva Eiger|Рабби Акива Эйгер|רבי עקיבא איגר|5521",
            "chasam_sofer|Acharonim|1|25|Chatam Sofer|Хатам Софер|החתם סופר|5522",
            "arizal|Kabbalists|12|5|Arizal (Rabbi Yitzchak Luria)|Аризаль (рабби Ицхак Лурия)|האר״י הקדוש|5294",
            "ramak|Kabbalists|11|23|Ramak (Rabbi Moshe Cordovero)|Рамак (рабби Моше Кордоверо)|הרמ״ק|5282",
            "ohr_hachaim|Kabbalists|11|15|Ohr HaChaim HaKadosh|Ор а-Хаим а-Кадош|האור החיים הקדוש|5456",
            "ramchal|Kabbalists|9|26|Ramchal (Rabbi Moshe Chaim Luzzatto)|Рамхаль (рабби Моше-Хаим Луццатто)|הרמח״ל|5467",
            "baba_sali|Kabbalists|5|4|Baba Sali|Баба Сали|בבא סאלי|5649",
            "ben_ish_chai|Kabbalists|13|13|Ben Ish Chai|Бен Иш Хай|הבן איש חי|5595",
            "baal_shem_tov|Chassidic|10|6|Baal Shem Tov|Баал Шем Тов|הבעל שם טוב|5458",
            "maggid|Chassidic|3|19|Maggid of Mezritch|Магид из Межирича|המגיד ממזריטש|5464",
            "elimelech|Chassidic|7|21|Rabbi Elimelech of Lizhensk|Рабби Элимелех из Лиженска|רבי אלימלך מליז׳ענסק|5477",
            "levi_yitzchak|Chassidic|1|25|Rabbi Levi Yitzchak of Berditchev|Рабби Леви-Ицхак из Бердичева|רבי לוי יצחק מברדיטשוב|5500",
            "nachman|Breslov|1|18|Rabbi Nachman of Breslov|Рабби Нахман из Бреслава|רבי נחמן מברסלב|5532",
            "chozeh|Chassidic|12|9|Chozeh of Lublin|Хозе из Люблина|החוזה מלובלין|5505",
            "kotzk|Chassidic|5|22|Kotzker Rebbe|Коцкий ребе|הרבי מקוצק|5547",
            "alter_rebbe|Chabad|4|24|Alter Rebbe (Rabbi Shneur Zalman of Liadi)|Алтер Ребе (рабби Шнеур-Залман из Ляд)|אדמו״ר הזקן|5505",
            "mittler|Chabad|3|9|Mitteler Rebbe|Мителер Ребе|האדמו״ר האמצעי|5533",
            "tzemach|Chabad|8|13|Tzemach Tzedek|Цемах Цедек|הצמח צדק|5549",
            "maharash|Chabad|1|13|Rebbe Maharash|Ребе Махараш|האדמו״ר מהר״ש|5594",
            "rashab|Chabad|8|2|Rebbe Rashab|Ребе Рашаб|האדמו״ר הרש״ב|5620",
            "rayatz|Chabad|5|10|Rebbe Rayatz|Ребе Раяц|האדמו״ר הריי״ץ|5640",
            "rebbe|Chabad|11|3|The Lubavitcher Rebbe|Любавичский Ребе|הרבי מליובאוויטש|5662",
            "salanter|Recent|5|25|Rabbi Yisrael Salanter|Рабби Исраэль Салантер|רבי ישראל סלנטר|5569",
            "chafetz_chaim|Recent|13|24|Chafetz Chaim|Хафец Хаим|החפץ חיים|5598",
            "rav_kook|Recent|13|3|Rav Kook|Рав Кук|הראי״ה קוק|5625",
            "chazon_ish|Recent|2|15|Chazon Ish|Хазон Иш|החזון איש|5638",
            "moshe_feinstein|Recent|7|13|Rabbi Moshe Feinstein|Рабби Моше Файнштейн|רבי משה פיינשטיין|5655",
            "ovadia|Recent|2|3|Rabbi Ovadia Yosef|Рав Овадия Йосеф|הרב עובדיה יוסף|5680",
            // Added with each day confirmed by two or more sources and, where the date of death is documented, by the
            // Hebrew calendar (see the tests).
            "baal_hasulam|Kabbalists|1|10|Baal HaSulam (Rabbi Yehuda Ashlag)|Бааль а-Сулам (рабби Йегуда Ашлаг)|בעל הסולם|5644",
            "brandwein|Kabbalists|8|18|Rabbi Yehuda Tzvi Brandwein|Рабби Йегуда-Цви Брандвайн|רבי יהודה צבי ברנדוויין|5663",
            "rabash|Kabbalists|1|5|Rabash (Rabbi Baruch Ashlag)|Рабаш (рабби Барух Ашлаг)|הרב״ש|5667",
            "rav_berg|Kabbalists|1|12|Rav Philip Berg|Рав Филип Берг|הרב שרגא פייבל ברג|5687",
            "chaim_vital|Kabbalists|8|30|Rabbi Chaim Vital|Рабби Хаим Виталь|רבי חיים ויטאל|5302",
            "kaduri|Kabbalists|4|29|Rabbi Yitzchak Kaduri|Рабби Ицхак Кадури|הרב יצחק כדורי|5658",
            "rashash|Yemenite|5|10|Rashash (Rabbi Shalom Sharabi)|Рашаш (рабби Шалом Шараби)|הרש״ש|5480",
            "maharitz|Yemenite|8|28|Maharitz (Rabbi Yihya Tzalach)|Махариц (рабби Йихье Цалах)|מהרי״ץ|5473",
            "kafih|Yemenite|11|18|Rabbi Yosef Kafih|Рабби Йосеф Кафих|הרב יוסף קאפח|5678",
            "nosson|Breslov|4|10|Reb Noson of Breslov|Реб Носон из Бреслова|רבי נתן מברסלב|5540",
            "odesser|Breslov|2|18|Rabbi Yisroel Ber Odesser (Saba)|Рабби Исраэль-Бер Одессер (Саба)|רבי ישראל דב אודסר|5648",
            "chaim_brisker|Lithuanian|12|21|Rabbi Chaim Soloveitchik of Brisk|Рабби Хаим Соловейчик из Бриска|רבי חיים מבריסק|5613",
            "brisker_rav|Lithuanian|1|9|Brisker Rav (Rabbi Yitzchak Zev Soloveitchik)|Брискер Рав (рабби Ицхак-Зеев Соловейчик)|הגרי״ז מבריסק|5646",
            "netziv|Lithuanian|12|28|Netziv (Rabbi Naftali Tzvi Yehuda Berlin)|Нецив (рабби Нафтали-Цви-Йегуда Берлин)|הנצי״ב מוולוז׳ין|5576",
            "yitzchak_elchanan|Lithuanian|7|21|Rabbi Yitzchak Elchanan Spektor|Рабби Ицхак-Эльханан Спектор|רבי יצחק אלחנן ספקטור|5577",
            "chaim_ozer|Lithuanian|12|5|Rabbi Chaim Ozer Grodzinski|Рабби Хаим-Озер Гродзенский|רבי חיים עוזר גרודזנסקי|5623",
            "alter_slabodka|Lithuanian|5|29|Alter of Slabodka (Rabbi Nosson Tzvi Finkel)|Альтер из Слободки (рабби Носон-Цви Финкель)|הסבא מסלבודקה|5609",
            "aharon_kotler|Lithuanian|3|2|Rabbi Aharon Kotler|Рабби Аарон Котлер|רבי אהרן קוטלר|5652",
            "steipler|Lithuanian|12|23|Steipler Gaon (Rabbi Yaakov Yisrael Kanievsky)|Стайплер (рабби Яаков-Исраэль Каниевский)|הסטייפלער|5659",
            "shach|Lithuanian|2|16|Rav Shach (Rabbi Elazar Menachem Shach)|Рав Шах (рабби Элазар-Менахем Шах)|הרב אלעזר מנחם מן שך|5659",
            "elyashiv|Lithuanian|11|28|Rabbi Yosef Shalom Elyashiv|Рабби Йосеф-Шалом Эльяшив|הרב יוסף שלום אלישיב|5670",
            "chaim_kanievsky|Lithuanian|7|15|Rabbi Chaim Kanievsky|Рабби Хаим Каниевский|הרב חיים קנייבסקי|5688",
            "chida|Sephardic|7|11|Chida (Rabbi Chaim Yosef David Azulai)|Хида (рабби Хаим-Йосеф-Давид Азулай)|החיד״א|5484",
            "abir_yaakov|Sephardic|4|20|Abir Yaakov (Rabbi Yaakov Abuhatzeira)|Авир Яаков (рабби Яаков Абу-Хацира)|האביר יעקב|5566",
            "kaf_hachaim|Sephardic|10|9|Kaf HaChaim (Rabbi Yaakov Chaim Sofer)|Каф а-Хаим (рабби Яаков-Хаим Софер)|הכף החיים|5630",
            "ben_zion_abba_shaul|Sephardic|11|19|Rabbi Ben Zion Abba Shaul|Рабби Бен-Цион Аба Шауль|הרב בן ציון אבא שאול|5684",
            "mordechai_eliyahu|Sephardic|10|25|Rabbi Mordechai Eliyahu|Рабби Мордехай Элиягу|הרב מרדכי אליהו|5689",
            "chiddushei_harim|Chassidic|7|23|Chiddushei HaRim (Rabbi Yitzchak Meir of Ger)|Хидушей а-Рим (рабби Ицхак-Меир из Гур)|החידושי הרי״ם|5560",
            "sfas_emes|Chassidic|5|5|Sfas Emes (Rabbi Yehuda Aryeh Leib of Ger)|Сфас Эмес (рабби Йегуда-Арье-Лейб из Гур)|השפת אמת|5607",
            "sar_shalom|Chassidic|13|27|Sar Shalom of Belz|Сар Шалом из Белза|השר שלום מבעלז|5539",
            "divrei_yoel|Chassidic|12|26|Satmar Rebbe (Divrei Yoel)|Сатмарский Ребе (Диврей Йоэль)|הרבי מסאטמר בעל דברי יואל|5647",
            "divrei_chaim|Chassidic|8|25|Divrei Chaim (Rabbi Chaim of Sanz)|Диврей Хаим (рабби Хаим из Санза)|הדברי חיים מצאנז|5553",
            "ruzhin|Chassidic|2|3|Rabbi Yisrael of Ruzhin|Рабби Исраэль из Ружина|רבי ישראל מרוז׳ין|5557",
            "zusha|Chassidic|5|2|Rebbe Reb Zusha of Anipoli|Ребе Реб Зуся из Аннополя|רבי זושא מאניפולי|5478",
            "bnei_yissaschar|Chassidic|4|18|Bnei Yissaschar (Rabbi Tzvi Elimelech of Dinov)|Бней Иссахар (рабби Цви-Элимелех из Динова)|הבני יששכר|5543",
            "apter_rav|Chassidic|8|5|Apter Rav (Ohev Yisrael)|Аптер Рав (Оэв Исраэль)|האוהב ישראל מאפטא|5508",
            "vitebsk|Chassidic|9|1|Rabbi Menachem Mendel of Vitebsk|Рабби Менахем-Мендл из Витебска|רבי מנחם מנדל מוויטבסק|5490",
            "koretz|Chassidic|13|10|Rabbi Pinchas of Koretz|Рабби Пинхас из Кореца|רבי פנחס מקוריץ|5486",
            "kozhnitz|Chassidic|1|14|Maggid of Kozhnitz|Магид из Козениц|המגיד מקוז׳ניץ|5493",
            "yid_hakadosh|Chassidic|1|19|Yid HaKadosh of Peshischa|Святой Еврей из Пшисхи|היהודי הקדוש מפשיסחא|5526",
            "simcha_bunim|Chassidic|13|12|Rebbe Simcha Bunim of Peshischa|Ребе Симха-Буним из Пшисхи|רבי שמחה בונם מפשיסחא|5525",
            "meor_einayim|Chassidic|2|11|Meor Einayim (Rabbi Menachem Nachum of Chernobyl)|Меор Эйнаим (рабби Менахем-Нахум из Чернобыля)|המאור עיניים|5490",
            // A third set, each day confirmed the same way (left out: the Kitzur Shulchan Aruch, 26 or 28 Tammuz; Rav Dessler,
            // 23, 24 or 25 Tevet; Rav Yaakov Kamenetsky, Adar I or II).
            "noda_biyehuda|Acharonim|9|17|Noda BiYehuda (Rabbi Yechezkel Landau)|Нода би-Йегуда (рабби Йехезкель Ландау)|הנודע ביהודה|5473",
            "shelah|Acharonim|8|11|Shelah HaKadosh (Rabbi Yeshaya Horowitz)|Шла а-Кадош (рабби Йешаяу Горовиц)|השל״ה הקדוש|5325",
            "taz|Acharonim|5|26|Taz (Rabbi David HaLevi Segal)|Таз (рабби Давид а-Леви Сегаль)|הט״ז|5346",
            "maharsha|Acharonim|3|5|Maharsha (Rabbi Shmuel Eidels)|Махарша (рабби Шмуэль Эйдельс)|המהרש״א|5315",
            "malbim|Acharonim|1|1|Malbim (Rabbi Meir Leibush Wisser)|Мальбим (рабби Меир-Лейбуш Виссер)|המלבי״ם|5569",
            "karlin|Chassidic|8|19|Rabbi Aharon of Karlin|Рабби Аарон из Карлина|רבי אהרן מקרלין|5496",
            "ropshitz|Chassidic|9|11|Rabbi Naftali of Ropshitz|Рабби Нафтали из Ропшица|רבי נפתלי מרופשיץ|5520",
            "zidichov|Chassidic|11|11|Rabbi Tzvi Hirsch of Zidichov|Рабби Цви-Гирш из Жидачова|רבי צבי הירש מזידיטשוב|5523",
            "chernobyl|Chassidic|9|20|Rabbi Mordechai of Chernobyl|Рабби Мордехай из Чернобыля|רבי מרדכי מצ׳רנוביל|5530",
            "imrei_emes|Chassidic|10|6|Imrei Emes (Rabbi Avraham Mordechai of Ger)|Имрей Эмес (рабби Авраам-Мордехай из Гуры)|האמרי אמת מגור|5626",
            "belz_aharon|Chassidic|12|21|Rabbi Aharon of Belz|Рабби Аарон из Белза|רבי אהרן מבעלז|5640",
            "klausenburg|Chassidic|11|9|Klausenburger Rebbe (Rabbi Yekusiel Yehuda Halberstam)|Клаузенбургский ребе (рабби Йекутиэль-Йегуда Хальберштам)|האדמו״ר מצאנז-קלויזנבורג|5665",
            "bobov|Chassidic|12|1|Bobover Rebbe (Rabbi Shlomo Halberstam)|Бобовский ребе (рабби Шломо Хальберштам)|האדמו״ר מבאבוב רבי שלמה הלברשטאם|5668",
            "shkop|Lithuanian|2|9|Rabbi Shimon Shkop|Рабби Шимон Шкоп|רבי שמעון שקופ|5620",
            "meltzer|Lithuanian|3|10|Rabbi Isser Zalman Meltzer|Рабби Иссер-Залман Мельцер|הרב איסר זלמן מלצר|5630",
            "shmulevitz|Lithuanian|4|3|Rabbi Chaim Shmulevitz|Рабби Хаим Шмулевич|הרב חיים שמואלביץ|5662",
            "soloveitchik_rav|Lithuanian|8|18|Rabbi Joseph B. Soloveitchik (the Rav)|Рабби Йосеф-Дов Соловейчик|הרב יוסף דוב סולובייצ׳יק|5663",
            "chaim_pinto|Sephardic|13|26|Rabbi Chaim Pinto HaGadol|Рабби Хаим Пинто а-Гадоль|רבי חיים פינטו הגדול|5509",
            "uziel|Sephardic|13|24|Rabbi Ben Zion Meir Chai Uziel|Рабби Бен-Цион Меир-Хай Узиэль|הרב בן ציון מאיר חי עוזיאל|5640",
            "aryeh_levin|Recent|8|9|Rabbi Aryeh Levin|Рабби Арье Левин|רבי אריה לוין|5645",
            "tzvi_yehuda|Recent|7|14|Rabbi Tzvi Yehuda Kook|Рабби Цви-Йегуда Кук|הרב צבי יהודה קוק|5651",
            // A fourth set, confirmed the same way (left out: the Toldos Yaakov Yosef, 21 or 24 Tishrei and the year unsure;
            // Rabbi Boruch Ber Leibowitz, 5 or 6 Kislev).
            "bach|Acharonim|7|20|Bach (Rabbi Yoel Sirkes)|Бах (рабби Йоэль Сиркис)|הב״ח|5321",
            "pnei_yehoshua|Acharonim|5|14|Pnei Yehoshua (Rabbi Yaakov Yehoshua Falk)|Пней Йегошуа (рабби Яаков-Йегошуа Фальк)|הפני יהושע|5440",
            "eybeschutz|Acharonim|13|21|Rabbi Yonasan Eybeschutz|Рабби Йонатан Эйбешюц|רבי יהונתן אייבשיץ|5450",
            "yaavetz|Acharonim|8|30|Yaavetz (Rabbi Yaakov Emden)|Яавец (рабби Яаков Эмден)|היעב״ץ|5457",
            "chayei_adam|Acharonim|1|4|Chayei Adam (Rabbi Avraham Danzig)|Хаей Адам (рабби Авраам Данциг)|החיי אדם|5508",
            "hirsch|Acharonim|4|27|Rabbi Samson Raphael Hirsch|Рабби Шимшон-Рафаэль Гирш|הרב שמשון רפאל הירש|5568",
            "ksav_sofer|Acharonim|4|19|Ksav Sofer (Rabbi Avraham Shmuel Binyamin Sofer)|Ктав Софер (рабби Авраам-Шмуэль-Биньямин Софер)|הכתב סופר|5575",
            "shmelke|Chassidic|9|1|Rabbi Shmelke of Nikolsburg|Рабби Шмелке из Никольсбурга|רבי שמעלקא מניקלשבורג|5486",
            "rimanov|Chassidic|9|19|Rabbi Menachem Mendel of Rimanov|Рабби Менахем-Мендл из Риманова|רבי מנחם מנדל מרימנוב|5505",
            "sassov|Chassidic|5|4|Rabbi Moshe Leib of Sassov|Рабби Моше-Лейб из Сасова|רבי משה לייב מסאסוב|5505",
            "degel|Chassidic|9|17|Degel Machaneh Ephraim (Rabbi Moshe Chaim Ephraim of Sudilkov)|Дегель Махане Эфраим (рабби Моше-Хаим-Эфраим из Судилкова)|הדגל מחנה אפרים|5508",
            "tzadok|Chassidic|13|9|Rabbi Tzadok HaKohen of Lublin|Рабби Цадок а-Коэн из Люблина|רבי צדוק הכהן מלובלין|5583",
            "belz_yissachar_dov|Chassidic|2|22|Rabbi Yissachar Dov of Belz|Рабби Иссахар-Дов из Белза|רבי ישכר דב מבעלז|5611",
            "beis_yisrael|Chassidic|7|2|Beis Yisrael (Rabbi Yisrael of Ger)|Бейт Исраэль (рабби Исраэль из Гуры)|הבית ישראל מגור|5655",
            "shteinman|Lithuanian|3|24|Rabbi Aharon Leib Shteinman|Рабби Аарон-Лейб Штейнман|הרב אהרן יהודה לייב שטיינמן|5674",
            "palagi|Sephardic|5|17|Rabbi Chaim Palagi|Рабби Хаим Палаччи|רבי חיים פלאג׳י|5548",
            "tzadka|Sephardic|2|12|Rabbi Yehuda Tzadka|Рабби Йегуда Цадка|הרב יהודה צדקה|5670",
            // A fifth set, confirmed the same way (left out: Rabbi Shlomo Kluger, 30 Sivan or 1 Tammuz; the Alter of Kelm,
            // whose 8 Av and 26 July 1898 do not agree; Rav Wosner and Rabbi Shalom Messas, no source of the Hebrew day).
            "chacham_tzvi|Acharonim|9|1|Chacham Tzvi (Rabbi Tzvi Hirsch Ashkenazi)|Хахам Цви (рабби Цви-Гирш Ашкенази)|החכם צבי|5416",
            "haflaah|Acharonim|11|4|Haflaah (Rabbi Pinchas Horowitz)|Афлаа (рабби Пинхас Горовиц)|בעל ההפלאה|5491",
            "nesivos|Acharonim|9|25|Nesivos (Rabbi Yaakov Lorberbaum of Lissa)|Нетивот (рабби Яаков Лорбербойм из Лиссы)|בעל הנתיבות|5520",
            "aruch_hashulchan|Acharonim|7|22|Aruch HaShulchan (Rabbi Yechiel Michel Epstein)|Арух а-Шулхан (рабби Йехиэль-Михл Эпштейн)|הערוך השולחן|5589",
            "uri_strelisk|Chassidic|13|23|Rabbi Uri of Strelisk (the Seraph)|Рабби Ури из Стрелиска (Сараф)|השרף מסטרעליסק|5517",
            "yismach_moshe|Chassidic|11|28|Yismach Moshe (Rabbi Moshe Teitelbaum)|Исмах Моше (рабби Моше Тейтельбаум)|הישמח משה|5519",
            "vorka|Chassidic|8|22|Rabbi Yitzchak of Vorka|Рабби Ицхак из Ворки|רבי יצחק מווארקא|5539",
            "tiferes_shlomo|Chassidic|7|29|Tiferes Shlomo (Rabbi Shlomo of Radomsk)|Тиферет Шломо (рабби Шломо из Радомска)|התפארת שלמה מראדומסק|5561",
            "yehoshua_belz|Chassidic|5|23|Rabbi Yehoshua of Belz|Рабби Йегошуа из Белза|רבי יהושע מבעלז|5585",
            "ahavas_yisrael|Chassidic|10|2|Ahavas Yisrael (Rabbi Yisrael of Vizhnitz)|Аават Исраэль (рабби Исраэль из Вижницы)|האהבת ישראל מוויז׳ניץ|5620",
            "minchas_elazar|Chassidic|10|2|Minchas Elazar (Rabbi Chaim Elazar of Munkatch)|Минхат Элазар (рабби Хаим-Элазар из Мукачево)|המנחת אלעזר ממונקאטש|5628",
            "meir_simcha|Lithuanian|13|4|Ohr Sameach (Rabbi Meir Simcha of Dvinsk)|Ор Самеах (рабби Меир-Симха из Двинска)|האור שמח|5603",
            "novardok|Lithuanian|3|17|Alter of Novardok (Rabbi Yosef Yozel Horowitz)|Альтер из Новардока (рабби Йосеф-Йозл Горовиц)|הסבא מנובהרדוק|5607",
            "rogatchover|Lithuanian|7|11|Rogatchover Gaon (Rabbi Yosef Rosen)|Рогачевский гаон (рабби Йосеф Розин)|הגאון מרוגצ׳וב|5618",
            "elya_lopian|Lithuanian|13|20|Rabbi Elya Lopian|Рабби Эля Лопьян|רבי אליהו לאפיאן|5636",
            "yitzchak_nissim|Sephardic|12|9|Rabbi Yitzchak Nissim|Рабби Ицхак Ниссим|הרב יצחק נסים|5656",
        };

        /// <summary>All the figures, by category in the order of <see cref="Categories"/>, within it by the time they lived.</summary>
        public static IReadOnlyList<Tzaddik> All { get; } = PeopleData.Select(line =>
        {
            string[] part = line.Split('|');
            return new Tzaddik(part[0], part[1], int.Parse(part[2]), int.Parse(part[3]), part[4], part[5], part[6], int.Parse(part[7]));
        }).OrderBy(person => Array.FindIndex(CategoryData, category => category[0] == person.Category)).ThenBy(person => person.Born).ToArray();

        /// <summary>The keys of the categories, in the order they are shown.</summary>
        public static IReadOnlyList<string> Categories { get; } = CategoryData.Select(category => category[0]).ToArray();

        public static string CategoryName(string category, string language)
        {
            string[] data = CategoryData.First(item => item[0] == category);
            return data[language == "עברית" ? 3 : language == "русский" ? 2 : 1];
        }

        public static IEnumerable<Tzaddik> OfCategory(string category) => All.Where(person => person.Category == category);

        /// <summary>
        /// The figures whose yahrzeit is on the date, except those whose keys are in hidden, the eldest (born first) first.
        /// None for a date outside the range of the Hebrew calendar.
        /// </summary>
        public static IReadOnlyList<Tzaddik> On(DateTime date, ICollection<string> hidden = null)
        {
            date = date.Date;
            var calendar = new System.Globalization.HebrewCalendar();
            if (date < calendar.MinSupportedDateTime || date > calendar.MaxSupportedDateTime) return Array.Empty<Tzaddik>();
            int month = JewishCalendar.MonthNumber(date), day = calendar.GetDayOfMonth(date);
            return All.Where(person => person.Month == month && person.Day == day && (hidden == null || !hidden.Contains(person.Key)))
                .OrderBy(person => person.Born).ToArray();
        }
    }
}
