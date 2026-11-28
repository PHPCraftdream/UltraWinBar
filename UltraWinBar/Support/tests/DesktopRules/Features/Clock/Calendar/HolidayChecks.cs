using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UltraWinBar.Utilities;

// The festivals, fasts and Rosh Chodesh by date, and the molad of every month.
internal static class HolidayChecks
{
    private static readonly HebrewCalendar Calendar = new HebrewCalendar();

    // The days an independent reference calendar does not know; they are checked by date below.
    private static readonly string[] Modern = { "YomHashoah", "YomHazikaron", "YomHaatzmaut", "YomYerushalayim" };

    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        CheckWholeYears();
        CheckModernDays();
        CheckFirstborn();
        CheckSpecialShabbatot();
        CheckNames();
        CheckMolad();
    }

    private static string Group(string key) => key switch
    {
        "CholHamoedSukkot" or "HoshanaRabba" => "Sukkot",
        "CholHamoedPesach" => "Pesach",
        _ => key,
    };

    // The days of a year as "Group@first day+days", the first day counted from Rosh Hashana.
    private static string Tokens(Dictionary<string, SortedSet<int>> offsets)
    {
        var tokens = new List<(int Start, string Group, int Length)>();
        foreach (var (group, set) in offsets)
        {
            int start = -1, previous = -2;
            foreach (int offset in set)
            {
                if (offset != previous + 1)
                {
                    if (start >= 0) tokens.Add((start, group, previous - start + 1));
                    start = offset;
                }
                previous = offset;
            }
            tokens.Add((start, group, previous - start + 1));
        }
        return string.Join(" ", tokens.OrderBy(token => token.Start).ThenBy(token => token.Group, StringComparer.Ordinal)
            .Select(token => $"{token.Group}@{token.Start}+{token.Length}"));
    }

    private static void CheckWholeYears()
    {
        // One whole year of each of the 14 kinds, in the diaspora and in Israel, as an independent calendar (pyluach,
        // MIT, used only as a reference for the expected values) knows its festivals, fasts and Rosh Chodesh. A festival
        // of several days is one entry whatever the day of it is (Sukkot with its Chol HaMoed and Hoshana Rabba).
        var wholeYears = new (int Year, bool Israel, string Expected)[]
        {
            (5780, false, "RoshHashana@0+2 FastOfGedaliah@2+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 SimchatTorah@22+1 RoshChodesh@29+2 RoshChodesh@59+2 Hanukkah@84+8 RoshChodesh@89+2 FastOfTevet@99+1 RoshChodesh@119+1 TuBishvat@133+1 RoshChodesh@148+2 FastOfEsther@161+1 Purim@162+1 ShushanPurim@163+1 RoshChodesh@178+1 Pesach@192+8 RoshChodesh@207+2 PesachSheni@221+1 LagBaomer@225+1 RoshChodesh@237+1 Shavuot@242+2 RoshChodesh@266+2 FastOfTammuz@283+1 RoshChodesh@296+1 TishaBeav@304+1 TuBeav@310+1 RoshChodesh@325+2"),
            (5780, true, "RoshHashana@0+2 FastOfGedaliah@2+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 RoshChodesh@29+2 RoshChodesh@59+2 Hanukkah@84+8 RoshChodesh@89+2 FastOfTevet@99+1 RoshChodesh@119+1 TuBishvat@133+1 RoshChodesh@148+2 FastOfEsther@161+1 Purim@162+1 ShushanPurim@163+1 RoshChodesh@178+1 Pesach@192+7 RoshChodesh@207+2 PesachSheni@221+1 LagBaomer@225+1 RoshChodesh@237+1 Shavuot@242+1 RoshChodesh@266+2 FastOfTammuz@283+1 RoshChodesh@296+1 TishaBeav@304+1 TuBeav@310+1 RoshChodesh@325+2"),
            (5781, false, "RoshHashana@0+2 FastOfGedaliah@2+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 SimchatTorah@22+1 RoshChodesh@29+2 RoshChodesh@59+1 Hanukkah@83+8 RoshChodesh@88+1 FastOfTevet@97+1 RoshChodesh@117+1 TuBishvat@131+1 RoshChodesh@146+2 FastOfEsther@159+1 Purim@160+1 ShushanPurim@161+1 RoshChodesh@176+1 Pesach@190+8 RoshChodesh@205+2 PesachSheni@219+1 LagBaomer@223+1 RoshChodesh@235+1 Shavuot@240+2 RoshChodesh@264+2 FastOfTammuz@281+1 RoshChodesh@294+1 TishaBeav@302+1 TuBeav@308+1 RoshChodesh@323+2"),
            (5781, true, "RoshHashana@0+2 FastOfGedaliah@2+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 RoshChodesh@29+2 RoshChodesh@59+1 Hanukkah@83+8 RoshChodesh@88+1 FastOfTevet@97+1 RoshChodesh@117+1 TuBishvat@131+1 RoshChodesh@146+2 FastOfEsther@159+1 Purim@160+1 ShushanPurim@161+1 RoshChodesh@176+1 Pesach@190+7 RoshChodesh@205+2 PesachSheni@219+1 LagBaomer@223+1 RoshChodesh@235+1 Shavuot@240+1 RoshChodesh@264+2 FastOfTammuz@281+1 RoshChodesh@294+1 TishaBeav@302+1 TuBeav@308+1 RoshChodesh@323+2"),
            (5782, false, "RoshHashana@0+2 FastOfGedaliah@2+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 SimchatTorah@22+1 RoshChodesh@29+2 RoshChodesh@59+1 Hanukkah@83+8 RoshChodesh@88+2 FastOfTevet@98+1 RoshChodesh@118+1 TuBishvat@132+1 RoshChodesh@147+2 PurimKatan@161+1 RoshChodesh@177+2 FastOfEsther@190+1 Purim@191+1 ShushanPurim@192+1 RoshChodesh@207+1 Pesach@221+8 RoshChodesh@236+2 PesachSheni@250+1 LagBaomer@254+1 RoshChodesh@266+1 Shavuot@271+2 RoshChodesh@295+2 FastOfTammuz@313+1 RoshChodesh@325+1 TishaBeav@334+1 TuBeav@339+1 RoshChodesh@354+2"),
            (5782, true, "RoshHashana@0+2 FastOfGedaliah@2+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 RoshChodesh@29+2 RoshChodesh@59+1 Hanukkah@83+8 RoshChodesh@88+2 FastOfTevet@98+1 RoshChodesh@118+1 TuBishvat@132+1 RoshChodesh@147+2 PurimKatan@161+1 RoshChodesh@177+2 FastOfEsther@190+1 Purim@191+1 ShushanPurim@192+1 RoshChodesh@207+1 Pesach@221+7 RoshChodesh@236+2 PesachSheni@250+1 LagBaomer@254+1 RoshChodesh@266+1 Shavuot@271+1 RoshChodesh@295+2 FastOfTammuz@313+1 RoshChodesh@325+1 TishaBeav@334+1 TuBeav@339+1 RoshChodesh@354+2"),
            (5784, false, "RoshHashana@0+2 FastOfGedaliah@2+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 SimchatTorah@22+1 RoshChodesh@29+2 RoshChodesh@59+1 Hanukkah@83+8 RoshChodesh@88+1 FastOfTevet@97+1 RoshChodesh@117+1 TuBishvat@131+1 RoshChodesh@146+2 PurimKatan@160+1 RoshChodesh@176+2 FastOfEsther@187+1 Purim@190+1 ShushanPurim@191+1 RoshChodesh@206+1 Pesach@220+8 RoshChodesh@235+2 PesachSheni@249+1 LagBaomer@253+1 RoshChodesh@265+1 Shavuot@270+2 RoshChodesh@294+2 FastOfTammuz@311+1 RoshChodesh@324+1 TishaBeav@332+1 TuBeav@338+1 RoshChodesh@353+2"),
            (5784, true, "RoshHashana@0+2 FastOfGedaliah@2+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 RoshChodesh@29+2 RoshChodesh@59+1 Hanukkah@83+8 RoshChodesh@88+1 FastOfTevet@97+1 RoshChodesh@117+1 TuBishvat@131+1 RoshChodesh@146+2 PurimKatan@160+1 RoshChodesh@176+2 FastOfEsther@187+1 Purim@190+1 ShushanPurim@191+1 RoshChodesh@206+1 Pesach@220+7 RoshChodesh@235+2 PesachSheni@249+1 LagBaomer@253+1 RoshChodesh@265+1 Shavuot@270+1 RoshChodesh@294+2 FastOfTammuz@311+1 RoshChodesh@324+1 TishaBeav@332+1 TuBeav@338+1 RoshChodesh@353+2"),
            (5785, false, "RoshHashana@0+2 FastOfGedaliah@3+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 SimchatTorah@22+1 RoshChodesh@29+2 RoshChodesh@59+2 Hanukkah@84+8 RoshChodesh@89+2 FastOfTevet@99+1 RoshChodesh@119+1 TuBishvat@133+1 RoshChodesh@148+2 FastOfEsther@161+1 Purim@162+1 ShushanPurim@163+1 RoshChodesh@178+1 Pesach@192+8 RoshChodesh@207+2 PesachSheni@221+1 LagBaomer@225+1 RoshChodesh@237+1 Shavuot@242+2 RoshChodesh@266+2 FastOfTammuz@283+1 RoshChodesh@296+1 TishaBeav@304+1 TuBeav@310+1 RoshChodesh@325+2"),
            (5785, true, "RoshHashana@0+2 FastOfGedaliah@3+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 RoshChodesh@29+2 RoshChodesh@59+2 Hanukkah@84+8 RoshChodesh@89+2 FastOfTevet@99+1 RoshChodesh@119+1 TuBishvat@133+1 RoshChodesh@148+2 FastOfEsther@161+1 Purim@162+1 ShushanPurim@163+1 RoshChodesh@178+1 Pesach@192+7 RoshChodesh@207+2 PesachSheni@221+1 LagBaomer@225+1 RoshChodesh@237+1 Shavuot@242+1 RoshChodesh@266+2 FastOfTammuz@283+1 RoshChodesh@296+1 TishaBeav@304+1 TuBeav@310+1 RoshChodesh@325+2"),
            (5786, false, "RoshHashana@0+2 FastOfGedaliah@2+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 SimchatTorah@22+1 RoshChodesh@29+2 RoshChodesh@59+1 Hanukkah@83+8 RoshChodesh@88+2 FastOfTevet@98+1 RoshChodesh@118+1 TuBishvat@132+1 RoshChodesh@147+2 FastOfEsther@160+1 Purim@161+1 ShushanPurim@162+1 RoshChodesh@177+1 Pesach@191+8 RoshChodesh@206+2 PesachSheni@220+1 LagBaomer@224+1 RoshChodesh@236+1 Shavuot@241+2 RoshChodesh@265+2 FastOfTammuz@282+1 RoshChodesh@295+1 TishaBeav@303+1 TuBeav@309+1 RoshChodesh@324+2"),
            (5786, true, "RoshHashana@0+2 FastOfGedaliah@2+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 RoshChodesh@29+2 RoshChodesh@59+1 Hanukkah@83+8 RoshChodesh@88+2 FastOfTevet@98+1 RoshChodesh@118+1 TuBishvat@132+1 RoshChodesh@147+2 FastOfEsther@160+1 Purim@161+1 ShushanPurim@162+1 RoshChodesh@177+1 Pesach@191+7 RoshChodesh@206+2 PesachSheni@220+1 LagBaomer@224+1 RoshChodesh@236+1 Shavuot@241+1 RoshChodesh@265+2 FastOfTammuz@282+1 RoshChodesh@295+1 TishaBeav@303+1 TuBeav@309+1 RoshChodesh@324+2"),
            (5787, false, "RoshHashana@0+2 FastOfGedaliah@2+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 SimchatTorah@22+1 RoshChodesh@29+2 RoshChodesh@59+2 Hanukkah@84+8 RoshChodesh@89+2 FastOfTevet@99+1 RoshChodesh@119+1 TuBishvat@133+1 RoshChodesh@148+2 PurimKatan@162+1 RoshChodesh@178+2 FastOfEsther@191+1 Purim@192+1 ShushanPurim@193+1 RoshChodesh@208+1 Pesach@222+8 RoshChodesh@237+2 PesachSheni@251+1 LagBaomer@255+1 RoshChodesh@267+1 Shavuot@272+2 RoshChodesh@296+2 FastOfTammuz@313+1 RoshChodesh@326+1 TishaBeav@334+1 TuBeav@340+1 RoshChodesh@355+2"),
            (5787, true, "RoshHashana@0+2 FastOfGedaliah@2+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 RoshChodesh@29+2 RoshChodesh@59+2 Hanukkah@84+8 RoshChodesh@89+2 FastOfTevet@99+1 RoshChodesh@119+1 TuBishvat@133+1 RoshChodesh@148+2 PurimKatan@162+1 RoshChodesh@178+2 FastOfEsther@191+1 Purim@192+1 ShushanPurim@193+1 RoshChodesh@208+1 Pesach@222+7 RoshChodesh@237+2 PesachSheni@251+1 LagBaomer@255+1 RoshChodesh@267+1 Shavuot@272+1 RoshChodesh@296+2 FastOfTammuz@313+1 RoshChodesh@326+1 TishaBeav@334+1 TuBeav@340+1 RoshChodesh@355+2"),
            (5788, false, "RoshHashana@0+2 FastOfGedaliah@2+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 SimchatTorah@22+1 RoshChodesh@29+2 RoshChodesh@59+2 Hanukkah@84+8 RoshChodesh@89+2 FastOfTevet@99+1 RoshChodesh@119+1 TuBishvat@133+1 RoshChodesh@148+2 FastOfEsther@159+1 Purim@162+1 ShushanPurim@163+1 RoshChodesh@178+1 Pesach@192+8 RoshChodesh@207+2 PesachSheni@221+1 LagBaomer@225+1 RoshChodesh@237+1 Shavuot@242+2 RoshChodesh@266+2 FastOfTammuz@283+1 RoshChodesh@296+1 TishaBeav@304+1 TuBeav@310+1 RoshChodesh@325+2"),
            (5788, true, "RoshHashana@0+2 FastOfGedaliah@2+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 RoshChodesh@29+2 RoshChodesh@59+2 Hanukkah@84+8 RoshChodesh@89+2 FastOfTevet@99+1 RoshChodesh@119+1 TuBishvat@133+1 RoshChodesh@148+2 FastOfEsther@159+1 Purim@162+1 ShushanPurim@163+1 RoshChodesh@178+1 Pesach@192+7 RoshChodesh@207+2 PesachSheni@221+1 LagBaomer@225+1 RoshChodesh@237+1 Shavuot@242+1 RoshChodesh@266+2 FastOfTammuz@283+1 RoshChodesh@296+1 TishaBeav@304+1 TuBeav@310+1 RoshChodesh@325+2"),
            (5789, false, "RoshHashana@0+2 FastOfGedaliah@3+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 SimchatTorah@22+1 RoshChodesh@29+2 RoshChodesh@59+1 Hanukkah@83+8 RoshChodesh@88+2 FastOfTevet@98+1 RoshChodesh@118+1 TuBishvat@132+1 RoshChodesh@147+2 FastOfEsther@160+1 Purim@161+1 ShushanPurim@162+1 RoshChodesh@177+1 Pesach@191+8 RoshChodesh@206+2 PesachSheni@220+1 LagBaomer@224+1 RoshChodesh@236+1 Shavuot@241+2 RoshChodesh@265+2 FastOfTammuz@283+1 RoshChodesh@295+1 TishaBeav@304+1 TuBeav@309+1 RoshChodesh@324+2"),
            (5789, true, "RoshHashana@0+2 FastOfGedaliah@3+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 RoshChodesh@29+2 RoshChodesh@59+1 Hanukkah@83+8 RoshChodesh@88+2 FastOfTevet@98+1 RoshChodesh@118+1 TuBishvat@132+1 RoshChodesh@147+2 FastOfEsther@160+1 Purim@161+1 ShushanPurim@162+1 RoshChodesh@177+1 Pesach@191+7 RoshChodesh@206+2 PesachSheni@220+1 LagBaomer@224+1 RoshChodesh@236+1 Shavuot@241+1 RoshChodesh@265+2 FastOfTammuz@283+1 RoshChodesh@295+1 TishaBeav@304+1 TuBeav@309+1 RoshChodesh@324+2"),
            (5790, false, "RoshHashana@0+2 FastOfGedaliah@2+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 SimchatTorah@22+1 RoshChodesh@29+2 RoshChodesh@59+1 Hanukkah@83+8 RoshChodesh@88+1 FastOfTevet@97+1 RoshChodesh@117+1 TuBishvat@131+1 RoshChodesh@146+2 PurimKatan@160+1 RoshChodesh@176+2 FastOfEsther@189+1 Purim@190+1 ShushanPurim@191+1 RoshChodesh@206+1 Pesach@220+8 RoshChodesh@235+2 PesachSheni@249+1 LagBaomer@253+1 RoshChodesh@265+1 Shavuot@270+2 RoshChodesh@294+2 FastOfTammuz@311+1 RoshChodesh@324+1 TishaBeav@332+1 TuBeav@338+1 RoshChodesh@353+2"),
            (5790, true, "RoshHashana@0+2 FastOfGedaliah@2+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 RoshChodesh@29+2 RoshChodesh@59+1 Hanukkah@83+8 RoshChodesh@88+1 FastOfTevet@97+1 RoshChodesh@117+1 TuBishvat@131+1 RoshChodesh@146+2 PurimKatan@160+1 RoshChodesh@176+2 FastOfEsther@189+1 Purim@190+1 ShushanPurim@191+1 RoshChodesh@206+1 Pesach@220+7 RoshChodesh@235+2 PesachSheni@249+1 LagBaomer@253+1 RoshChodesh@265+1 Shavuot@270+1 RoshChodesh@294+2 FastOfTammuz@311+1 RoshChodesh@324+1 TishaBeav@332+1 TuBeav@338+1 RoshChodesh@353+2"),
            (5795, false, "RoshHashana@0+2 FastOfGedaliah@3+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 SimchatTorah@22+1 RoshChodesh@29+2 RoshChodesh@59+2 Hanukkah@84+8 RoshChodesh@89+2 FastOfTevet@99+1 RoshChodesh@119+1 TuBishvat@133+1 RoshChodesh@148+2 PurimKatan@162+1 RoshChodesh@178+2 FastOfEsther@189+1 Purim@192+1 ShushanPurim@193+1 RoshChodesh@208+1 Pesach@222+8 RoshChodesh@237+2 PesachSheni@251+1 LagBaomer@255+1 RoshChodesh@267+1 Shavuot@272+2 RoshChodesh@296+2 FastOfTammuz@313+1 RoshChodesh@326+1 TishaBeav@334+1 TuBeav@340+1 RoshChodesh@355+2"),
            (5795, true, "RoshHashana@0+2 FastOfGedaliah@3+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 RoshChodesh@29+2 RoshChodesh@59+2 Hanukkah@84+8 RoshChodesh@89+2 FastOfTevet@99+1 RoshChodesh@119+1 TuBishvat@133+1 RoshChodesh@148+2 PurimKatan@162+1 RoshChodesh@178+2 FastOfEsther@189+1 Purim@192+1 ShushanPurim@193+1 RoshChodesh@208+1 Pesach@222+7 RoshChodesh@237+2 PesachSheni@251+1 LagBaomer@255+1 RoshChodesh@267+1 Shavuot@272+1 RoshChodesh@296+2 FastOfTammuz@313+1 RoshChodesh@326+1 TishaBeav@334+1 TuBeav@340+1 RoshChodesh@355+2"),
            (5797, false, "RoshHashana@0+2 FastOfGedaliah@2+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 SimchatTorah@22+1 RoshChodesh@29+2 RoshChodesh@59+1 Hanukkah@83+8 RoshChodesh@88+1 FastOfTevet@97+1 RoshChodesh@117+1 TuBishvat@131+1 RoshChodesh@146+2 FastOfEsther@157+1 Purim@160+1 ShushanPurim@161+1 RoshChodesh@176+1 Pesach@190+8 RoshChodesh@205+2 PesachSheni@219+1 LagBaomer@223+1 RoshChodesh@235+1 Shavuot@240+2 RoshChodesh@264+2 FastOfTammuz@281+1 RoshChodesh@294+1 TishaBeav@302+1 TuBeav@308+1 RoshChodesh@323+2"),
            (5797, true, "RoshHashana@0+2 FastOfGedaliah@2+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 RoshChodesh@29+2 RoshChodesh@59+1 Hanukkah@83+8 RoshChodesh@88+1 FastOfTevet@97+1 RoshChodesh@117+1 TuBishvat@131+1 RoshChodesh@146+2 FastOfEsther@157+1 Purim@160+1 ShushanPurim@161+1 RoshChodesh@176+1 Pesach@190+7 RoshChodesh@205+2 PesachSheni@219+1 LagBaomer@223+1 RoshChodesh@235+1 Shavuot@240+1 RoshChodesh@264+2 FastOfTammuz@281+1 RoshChodesh@294+1 TishaBeav@302+1 TuBeav@308+1 RoshChodesh@323+2"),
            (5803, false, "RoshHashana@0+2 FastOfGedaliah@2+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 SimchatTorah@22+1 RoshChodesh@29+2 RoshChodesh@59+2 Hanukkah@84+8 RoshChodesh@89+2 FastOfTevet@99+1 RoshChodesh@119+1 TuBishvat@133+1 RoshChodesh@148+2 PurimKatan@162+1 RoshChodesh@178+2 FastOfEsther@191+1 Purim@192+1 ShushanPurim@193+1 RoshChodesh@208+1 Pesach@222+8 RoshChodesh@237+2 PesachSheni@251+1 LagBaomer@255+1 RoshChodesh@267+1 Shavuot@272+2 RoshChodesh@296+2 FastOfTammuz@314+1 RoshChodesh@326+1 TishaBeav@335+1 TuBeav@340+1 RoshChodesh@355+2"),
            (5803, true, "RoshHashana@0+2 FastOfGedaliah@2+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 RoshChodesh@29+2 RoshChodesh@59+2 Hanukkah@84+8 RoshChodesh@89+2 FastOfTevet@99+1 RoshChodesh@119+1 TuBishvat@133+1 RoshChodesh@148+2 PurimKatan@162+1 RoshChodesh@178+2 FastOfEsther@191+1 Purim@192+1 ShushanPurim@193+1 RoshChodesh@208+1 Pesach@222+7 RoshChodesh@237+2 PesachSheni@251+1 LagBaomer@255+1 RoshChodesh@267+1 Shavuot@272+1 RoshChodesh@296+2 FastOfTammuz@314+1 RoshChodesh@326+1 TishaBeav@335+1 TuBeav@340+1 RoshChodesh@355+2"),
            (5812, false, "RoshHashana@0+2 FastOfGedaliah@3+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 SimchatTorah@22+1 RoshChodesh@29+2 RoshChodesh@59+1 Hanukkah@83+8 RoshChodesh@88+1 FastOfTevet@97+1 RoshChodesh@117+1 TuBishvat@131+1 RoshChodesh@146+2 PurimKatan@160+1 RoshChodesh@176+2 FastOfEsther@189+1 Purim@190+1 ShushanPurim@191+1 RoshChodesh@206+1 Pesach@220+8 RoshChodesh@235+2 PesachSheni@249+1 LagBaomer@253+1 RoshChodesh@265+1 Shavuot@270+2 RoshChodesh@294+2 FastOfTammuz@311+1 RoshChodesh@324+1 TishaBeav@332+1 TuBeav@338+1 RoshChodesh@353+2"),
            (5812, true, "RoshHashana@0+2 FastOfGedaliah@3+1 YomKippur@9+1 Sukkot@14+7 SheminiAtzeret@21+1 RoshChodesh@29+2 RoshChodesh@59+1 Hanukkah@83+8 RoshChodesh@88+1 FastOfTevet@97+1 RoshChodesh@117+1 TuBishvat@131+1 RoshChodesh@146+2 PurimKatan@160+1 RoshChodesh@176+2 FastOfEsther@189+1 Purim@190+1 ShushanPurim@191+1 RoshChodesh@206+1 Pesach@220+7 RoshChodesh@235+2 PesachSheni@249+1 LagBaomer@253+1 RoshChodesh@265+1 Shavuot@270+1 RoshChodesh@294+2 FastOfTammuz@311+1 RoshChodesh@324+1 TishaBeav@332+1 TuBeav@338+1 RoshChodesh@353+2"),
        };
        foreach (var (year, israel, expected) in wholeYears)
        {
            DateTime start = Calendar.ToDateTime(year, 1, 1, 0, 0, 0, 0), end = Calendar.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0);
            var offsets = new Dictionary<string, SortedSet<int>>();
            for (DateTime day = start; day < end; day = day.AddDays(1))
            {
                foreach (var item in JewishCalendar.Days(day, israel))
                {
                    string group = Group(item.Key);
                    if (Modern.Contains(group) || group == "FastOfFirstborn" || israel && group == "SimchatTorah") continue;
                    if (!offsets.TryGetValue(group, out var set)) offsets[group] = set = new SortedSet<int>();
                    set.Add((day - start).Days);
                }
            }
            string actual = Tokens(offsets);
            if (actual != expected) throw new Exception($"{year} ({(israel ? "Israel" : "diaspora")}): expected\n{expected}\ngot\n{actual}");
        }
        Console.WriteLine($"PASS: {wholeYears.Length} whole years (every kind of year, diaspora and Israel) have the festivals, fasts and Rosh Chodesh the reference calendar has.");

        // The days one day's list must carry: day numbers, the end of the festivals and the Israeli second days.
        DateTime Gregorian(string text) => DateTime.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var pesachDiaspora = JewishCalendar.Days(Gregorian("2026-04-09"), false).Single();
        var pesachIsrael = JewishCalendar.Days(Gregorian("2026-04-09"), true);
        if (pesachDiaspora.Key != "Pesach" || pesachDiaspora.Number != 8 || pesachDiaspora.Count != 8 || !pesachDiaspora.IsYomTov || !pesachDiaspora.IsLast)
            throw new Exception("The eighth day of Pesach, 22 Nisan 5786, is a festival in the diaspora.");
        if (pesachIsrael.Count != 0) throw new Exception("22 Nisan 5786 is an ordinary day in Israel.");
        var middle = JewishCalendar.Days(Gregorian("2026-04-05"), true).Single();
        if (middle.Key != "CholHamoedPesach" || middle.Kind != DayKind.CholHamoed || middle.Number != 4 || middle.Count != 7 || middle.IsYomTov)
            throw new Exception("18 Nisan 5786 is Chol HaMoed, day 4 of 7 in Israel.");
        var simchat = JewishCalendar.Days(Gregorian("2025-10-14"), true).Select(item => item.Key).ToArray();
        if (!simchat.SequenceEqual(new[] { "SheminiAtzeret", "SimchatTorah" }) || JewishCalendar.Days(Gregorian("2025-10-15"), false).Single().Key != "SimchatTorah")
            throw new Exception("Shemini Atzeret and Simchat Torah are one day in Israel, two in the diaspora.");
        var hanukkah = JewishCalendar.Days(Gregorian("2025-12-22"), false).Single(item => item.Key == "Hanukkah");
        if (hanukkah.Number != 8 || !hanukkah.IsLast || hanukkah.Kind != DayKind.Minor) throw new Exception("22 December 2025 is the eighth day of Hanukkah.");
        // 22 December 2025 is 2 Tevet 5786, the eighth day because Kislev has 30 days that year; Rosh Chodesh runs on beside it.
        var roshChodesh = JewishCalendar.Days(Gregorian("2025-12-21"), false).Single(item => item.Key == "RoshChodesh");
        if (roshChodesh.Number != 2 || roshChodesh.Count != 2 || roshChodesh.Month != 4 || roshChodesh.LeapYear) throw new Exception("21 December 2025 is the second day of Rosh Chodesh Tevet.");
        // Fasts moved off the Shabbat: 9 Av 5785 was Shabbat, 2 August 2025, so the fast is the Sunday after.
        if (JewishCalendar.Days(Gregorian("2025-08-02"), false).Any(item => item.Key == "TishaBeav") ||
            JewishCalendar.Days(Gregorian("2025-08-03"), false).Single(item => item.Key == "TishaBeav").Kind != DayKind.Fast)
            throw new Exception("Tisha B'Av that falls on Shabbat is fasted on Sunday.");
        // No day of an out of range date.
        if (JewishCalendar.Days(new DateTime(1500, 1, 1), false).Count != 0 || JewishCalendar.Days(new DateTime(2300, 1, 1), false).Count != 0)
            throw new Exception("A date outside the Hebrew calendar has no special days.");
        // The time of day does not matter.
        if (JewishCalendar.Days(Gregorian("2025-12-22").AddHours(23), false).Single().Key != "Hanukkah")
            throw new Exception("The time of day must not matter.");
        Console.WriteLine("PASS: day numbers, Israeli and diaspora second days, Chol HaMoed, Hanukkah across the turn of the month, a fast moved off Shabbat, Rosh Chodesh of two days, and the range are right.");
    }

    // The modern days and their moves, from the law of Israel: Yom HaShoah is on the Thursday before when the 27th of Nisan is
    // a Friday and on the Monday after when it is a Sunday; Yom HaAtzmaut is the 5th of Iyar, on the Thursday before when
    // that is Friday or Shabbat and (from 5764) on the Tuesday after when that is a Monday; Yom HaZikaron is the day before it.
    private static void CheckModernDays()
    {
        var modern = new (string Date, string Key)[]
        {
            ("2014-04-28", "YomHashoah"), ("2018-04-12", "YomHashoah"), ("2024-05-06", "YomHashoah"), ("2025-04-24", "YomHashoah"), ("2026-04-14", "YomHashoah"),
            ("2018-04-18", "YomHazikaron"), ("2018-04-19", "YomHaatzmaut"), ("2021-04-14", "YomHazikaron"), ("2021-04-15", "YomHaatzmaut"),
            ("2022-05-04", "YomHazikaron"), ("2022-05-05", "YomHaatzmaut"), ("2024-05-13", "YomHazikaron"), ("2024-05-14", "YomHaatzmaut"),
            ("2025-04-30", "YomHazikaron"), ("2025-05-01", "YomHaatzmaut"), ("2026-04-21", "YomHazikaron"), ("2026-04-22", "YomHaatzmaut"),
            ("2024-06-05", "YomYerushalayim"), ("2025-05-26", "YomYerushalayim"),
        };
        foreach (var (date, key) in modern)
        {
            var found = JewishCalendar.Days(DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture), true).Where(item => item.Key == key).ToArray();
            if (found.Length != 1 || found[0].Kind != DayKind.Minor) throw new Exception($"{date} is {key}.");
        }
        // They begin in the years of the law: Yom HaAtzmaut and Yom HaZikaron in 5709 (1949; 5 Iyar 5708 was the Declaration itself,
        // Friday 14 May 1948), Yom HaShoah in 5711 (1951), Yom Yerushalayim in 5727 (7 June 1967); and the move of Yom HaAtzmaut from a
        // Monday to the Tuesday is of 5764 (2004), before it the day stayed on the Monday (12 May 1997).
        bool Has(string date, string key) =>
            JewishCalendar.Days(DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture), true).Any(item => item.Key == key);
        if (Has("1948-05-13", "YomHazikaron") || Has("1948-05-13", "YomHaatzmaut") || Has("1948-05-14", "YomHaatzmaut") ||
            !Has("1949-05-03", "YomHazikaron") || !Has("1949-05-04", "YomHaatzmaut") ||
            Has("1950-04-14", "YomHashoah") || Has("1950-04-13", "YomHashoah") || !Has("1951-05-03", "YomHashoah") ||
            Has("1966-05-18", "YomYerushalayim") || !Has("1967-06-07", "YomYerushalayim") ||
            !Has("1997-05-11", "YomHazikaron") || !Has("1997-05-12", "YomHaatzmaut") || Has("1997-05-13", "YomHaatzmaut") ||
            !Has("2004-04-26", "YomHazikaron") || !Has("2004-04-27", "YomHaatzmaut") || Has("2004-04-26", "YomHaatzmaut"))
            throw new Exception("The modern days begin in the years of the law, and the Monday move of Yom HaAtzmaut in 5764.");
        // Each of them comes once a year, from 5727 to 5900. Yom HaShoah is never on a Friday, Shabbat or Sunday, Yom
        // HaAtzmaut never on a Friday or Shabbat (nor on a Monday from 5764), Yom HaZikaron never on a Friday or Shabbat.
        for (int year = 5727; year <= 5900; year++)
        {
            var counts = new Dictionary<string, List<DateTime>>();
            for (DateTime day = Calendar.ToDateTime(year, 1, 1, 0, 0, 0, 0); day < Calendar.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0); day = day.AddDays(1))
                foreach (var item in JewishCalendar.Days(day, true).Where(item => Modern.Contains(item.Key)))
                {
                    if (!counts.TryGetValue(item.Key, out var list)) counts[item.Key] = list = new List<DateTime>();
                    list.Add(day);
                }
            foreach (string key in Modern)
            {
                int times = counts.TryGetValue(key, out var found) ? found.Count : 0;
                if (times != 1) throw new Exception($"{year}: {key} must come once, came {times} times.");
            }
            DayOfWeek Of(string key) => counts[key][0].DayOfWeek;
            if (Of("YomHashoah") == DayOfWeek.Friday || Of("YomHashoah") == DayOfWeek.Saturday || Of("YomHashoah") == DayOfWeek.Sunday ||
                Of("YomHaatzmaut") == DayOfWeek.Friday || Of("YomHaatzmaut") == DayOfWeek.Saturday || Of("YomHaatzmaut") == DayOfWeek.Monday && year >= 5764 ||
                Of("YomHazikaron") == DayOfWeek.Friday || Of("YomHazikaron") == DayOfWeek.Saturday ||
                (counts["YomHaatzmaut"][0] - counts["YomHazikaron"][0]).Days != 1)
                throw new Exception($"{year}: the modern days are on days the law does not allow.");
        }
        Console.WriteLine($"PASS: {modern.Length} modern days by date, the years they begin in, and in 174 years each comes once, on a weekday the law allows, Yom HaZikaron the day before Yom HaAtzmaut.");
    }

    // The fast of the firstborn (the reference calendar does not know it): 14 Nisan, the Thursday before (12 Nisan) when that is Shabbat.
    private static void CheckFirstborn()
    {
        DateTime Gregorian(string text) => DateTime.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        bool Fasts(string date, bool israel) => JewishCalendar.Days(Gregorian(date), israel).Any(item => item.Key == "FastOfFirstborn" && item.Kind == DayKind.Fast);
        // Pesach 5786 began on Thursday 2 April 2026 (the eve, Wednesday 1 April); 5785 on Sunday 13 April 2025 (the eve was Shabbat).
        if (!Fasts("2026-04-01", false) || !Fasts("2026-04-01", true) || Fasts("2026-03-31", false) || Fasts("2025-04-12", false) || !Fasts("2025-04-10", false) || Fasts("2025-04-11", false))
            throw new Exception("The fast of the firstborn is on the eve of Pesach, on the Thursday before when the eve is Shabbat.");
        for (int year = 5700; year <= 5900; year++)
        {
            var fasts = new List<DateTime>();
            for (DateTime day = Calendar.ToDateTime(year, 1, 1, 0, 0, 0, 0); day < Calendar.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0); day = day.AddDays(1))
                if (JewishCalendar.Days(day, false).Any(item => item.Key == "FastOfFirstborn")) fasts.Add(day);
            // Nisan is the 7th month of a plain year of the .NET calendar and the 8th of a leap one.
            DateTime eve = Calendar.ToDateTime(year, Calendar.IsLeapYear(year) ? 8 : 7, 14, 0, 0, 0, 0);
            DateTime expected = eve.DayOfWeek == DayOfWeek.Saturday ? eve.AddDays(-2) : eve;
            if (fasts.Count != 1 || fasts[0] != expected || expected.DayOfWeek == DayOfWeek.Saturday)
                throw new Exception($"{year}: the fast of the firstborn must come once, on {expected:yyyy-MM-dd}, not {string.Join(", ", fasts.Select(day => day.ToString("yyyy-MM-dd")))}.");
        }
        Console.WriteLine("PASS: the fast of the firstborn comes once in each of 201 years, on the eve of Pesach or the Thursday before it.");
    }

    // The Shabbatot with names of their own. The reference values are what an independent calendar (pyluach) gives for the four
    // before Pesach and for Beshalach, and the dates of the others follow from the rules (the one before Tisha B'Av, and so on).
    private static void CheckSpecialShabbatot()
    {
        DateTime Gregorian(string text) => DateTime.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var expected = new (int Year, string Expected)[]
        {
            (5786, "2025-09-27 ShabbatShuva 2026-01-31 ShabbatShirah 2026-02-14 ShabbatShekalim 2026-02-28 ShabbatZachor 2026-03-07 ShabbatParah 2026-03-14 ShabbatHachodesh 2026-03-28 ShabbatHagadol 2026-07-18 ShabbatChazon 2026-07-25 ShabbatNachamu"),
            (5787, "2026-09-19 ShabbatShuva 2027-01-23 ShabbatShirah 2027-03-06 ShabbatShekalim 2027-03-20 ShabbatZachor 2027-03-27 ShabbatParah 2027-04-03 ShabbatHachodesh 2027-04-17 ShabbatHagadol 2027-08-07 ShabbatChazon 2027-08-14 ShabbatNachamu"),
        };
        foreach (var (year, text) in expected)
        {
            foreach (bool israel in new[] { false, true })
            {
                var found = new List<string>();
                for (DateTime day = Calendar.ToDateTime(year, 1, 1, 0, 0, 0, 0); day < Calendar.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0); day = day.AddDays(1))
                {
                    string key = JewishCalendar.SpecialShabbat(day, israel);
                    if (key != null) found.Add($"{day:yyyy-MM-dd} {key}");
                }
                if (string.Join(" ", found) != text) throw new Exception($"{year} ({(israel ? "Israel" : "diaspora")}): expected\n{text}\ngot\n{string.Join(" ", found)}");
            }
        }
        // In 201 years each of the nine comes once a year, on a Shabbat, and only a Shabbat has one.
        for (int year = 5700; year <= 5900; year++)
        {
            var keys = new List<string>();
            for (DateTime day = Calendar.ToDateTime(year, 1, 1, 0, 0, 0, 0); day < Calendar.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0); day = day.AddDays(1))
            {
                string key = JewishCalendar.SpecialShabbat(day, false);
                if (key == null) continue;
                if (day.DayOfWeek != DayOfWeek.Saturday) throw new Exception($"{day:yyyy-MM-dd}: {key} is not on a Shabbat.");
                keys.Add(key);
            }
            if (keys.Count != 9 || keys.Distinct().Count() != 9 || !keys.OrderBy(key => key).SequenceEqual(JewishCalendar.SpecialShabbatKeys.OrderBy(key => key)))
                throw new Exception($"{year}: the nine special Shabbatot must come once each, came {string.Join(", ", keys)}.");
        }
        if (JewishCalendar.SpecialShabbat(Gregorian("2026-03-27"), false) != null || JewishCalendar.SpecialShabbat(Gregorian("2300-03-27"), false) != null)
            throw new Exception("Only a Shabbat in the range of the Hebrew calendar is a special one.");
        foreach (string key in JewishCalendar.SpecialShabbatKeys)
        {
            var names = new[] { "English", "русский", "עברית" }.Select(language => JewishCalendar.SpecialShabbatName(key, language)).ToArray();
            if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct().Count() != 3 || JewishCalendar.SpecialShabbatName(key, "Deutsch") != names[0])
                throw new Exception($"{key} needs three different names, English for the other languages.");
        }
        Console.WriteLine("PASS: the nine special Shabbatot come once a year, on a Shabbat, in Israel and the diaspora, as the reference calendar has the four before Pesach and Beshalach, and have names in three languages.");
    }

    private static void CheckNames()
    {
        foreach (string key in JewishCalendar.Keys)
        {
            var names = new[] { "English", "русский", "עברית" }.Select(language => JewishCalendar.NameOf(key, language)).ToArray();
            if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct().Count() != 3) throw new Exception($"{key} needs three different names.");
            if (JewishCalendar.NameOf(key, "Deutsch") != names[0]) throw new Exception($"{key}: a language without names falls back to English.");
        }
        // Every key the calendar knows comes up somewhere in ten years, in Israel or in the diaspora.
        var seen = new HashSet<string>();
        for (DateTime day = new DateTime(2024, 9, 1); day < new DateTime(2034, 9, 1); day = day.AddDays(1))
            foreach (bool israel in new[] { false, true })
                foreach (var item in JewishCalendar.Days(day, israel)) seen.Add(item.Key);
        var missing = JewishCalendar.Keys.Except(seen).ToArray();
        if (missing.Length != 0) throw new Exception("Never comes up in ten years: " + string.Join(", ", missing));

        if (new JewishDay("RoshChodesh", DayKind.RoshChodesh, 1, 1, 3, false).Name("English") != "Rosh Chodesh Kislev" ||
            new JewishDay("RoshChodesh", DayKind.RoshChodesh, 1, 1, 3, false).Name("русский") != "Рош ходеш кислев" ||
            new JewishDay("RoshChodesh", DayKind.RoshChodesh, 1, 2, 7, true).Name("עברית") != "ראש חודש אדר ב׳" ||
            new JewishDay("RoshChodesh", DayKind.RoshChodesh, 1, 2, 7, false).Name("English") != "Rosh Chodesh Adar" ||
            new JewishDay("Purim", DayKind.Minor, 1, 1).Name("русский") != "Пурим")
            throw new Exception("Names of the days are wrong.");
        Console.WriteLine($"PASS: all {JewishCalendar.Keys.Count} days have names in English, Russian and Hebrew and all of them come up in ten years.");
    }

    // The molad of each month. The reference values are what an independent calendar announces: the weekday (1 is Sunday),
    // the hour, the minutes and the chalakim.
    private static void CheckMolad()
    {
        var molads = new (int Year, int Month, int Weekday, int Hour, int Minute, int Parts)[]
        {
            (5703, 5, 4, 17, 1, 10),
            (5703, 11, 6, 21, 25, 16),
            (5704, 7, 5, 3, 18, 6),
            (5707, 3, 1, 4, 16, 4),
            (5710, 13, 1, 14, 2, 14),
            (5711, 2, 4, 15, 30, 16),
            (5713, 5, 5, 12, 4, 8),
            (5716, 7, 7, 15, 58, 10),
            (5717, 1, 4, 9, 6, 17),
            (5719, 5, 6, 18, 24, 10),
            (5721, 8, 6, 2, 14, 1),
            (5730, 13, 3, 4, 8, 10),
            (5732, 9, 5, 18, 49, 12),
            (5733, 3, 2, 11, 58, 1),
            (5735, 3, 5, 18, 19, 8),
            (5736, 10, 6, 20, 16, 9),
            (5737, 9, 2, 16, 21, 2),
            (5738, 4, 7, 22, 13, 10),
            (5741, 1, 3, 23, 11, 8),
            (5747, 10, 5, 0, 8, 1),
            (5747, 13, 2, 14, 20, 4),
            (5750, 9, 4, 2, 34, 1),
            (5754, 4, 2, 23, 36, 10),
            (5758, 13, 7, 18, 11, 14),
            (5760, 7, 1, 20, 8, 15),
            (5760, 11, 7, 23, 5, 1),
            (5764, 4, 3, 18, 39, 8),
            (5766, 8, 4, 15, 13, 0),
            (5769, 4, 7, 16, 10, 16),
            (5770, 5, 6, 13, 43, 11),
            (5770, 7, 1, 2, 27, 12),
            (5773, 7, 1, 17, 37, 13),
            (5773, 8, 3, 6, 21, 14),
            (5775, 8, 6, 12, 43, 3),
            (5777, 11, 7, 9, 16, 13),
            (5779, 8, 6, 1, 25, 17),
            (5779, 12, 5, 4, 22, 3),
            (5781, 8, 7, 19, 3, 5),
            (5782, 4, 7, 13, 39, 14),
            (5782, 7, 5, 3, 51, 17),
            (5784, 12, 2, 1, 53, 11),
            (5789, 3, 6, 4, 48, 10),
            (5790, 13, 4, 20, 57, 14),
            (5800, 8, 3, 11, 36, 6),
            (5803, 1, 1, 22, 22, 1),
            (5804, 3, 3, 21, 22, 16),
            (5805, 1, 5, 4, 43, 8),
            (5807, 13, 4, 7, 9, 8),
            (5815, 5, 5, 2, 42, 10),
            (5818, 13, 2, 11, 1, 0),
            (5820, 13, 5, 17, 22, 7),
            (5824, 11, 7, 15, 53, 0),
            (5825, 13, 2, 14, 53, 15),
            (5828, 2, 7, 9, 59, 5),
            (5829, 10, 3, 0, 40, 7),
            (5831, 6, 7, 4, 5, 10),
            (5833, 13, 7, 3, 35, 6),
            (5835, 9, 2, 18, 16, 8),
            (5838, 10, 4, 22, 10, 10),
            (5839, 7, 6, 5, 31, 2),
            (5839, 10, 3, 19, 43, 5),
            (5840, 12, 4, 6, 0, 1),
            (5850, 1, 2, 4, 58, 6),
            (5860, 3, 6, 1, 29, 6),
            (5864, 8, 4, 17, 8, 6),
            (5866, 7, 6, 10, 45, 12),
            (5868, 12, 1, 20, 3, 5),
            (5873, 10, 2, 16, 6, 11),
            (5874, 3, 4, 20, 30, 17),
            (5874, 8, 5, 12, 11, 4),
            (5879, 4, 3, 6, 46, 8),
            (5879, 7, 7, 20, 58, 11),
            (5880, 9, 1, 7, 15, 7),
            (5883, 12, 6, 12, 37, 11),
            (5888, 6, 1, 5, 44, 13),
            (5888, 8, 4, 7, 12, 15),
            (5889, 4, 4, 1, 49, 6),
            (5895, 10, 5, 23, 49, 13),
            (5898, 12, 4, 5, 11, 17),
            (5899, 12, 1, 14, 0, 11),
        };
        foreach (var (year, month, weekday, hour, minute, parts) in molads)
        {
            var molad = Molads.Of(year, month);
            if ((int)molad.MeanTime.DayOfWeek + 1 != weekday || molad.MeanTime.Hour != hour || molad.MeanTime.Minute != minute || molad.Parts != parts)
                throw new Exception($"Molad of {year} month {month}: expected day {weekday} {hour}:{minute:00} and {parts}, got {molad}.");
        }
        Console.WriteLine($"PASS: {molads.Length} molads are announced the way the reference calendar announces them.");

        // The molad of the coming month, from a date, across the turn of the year and of the Adars.
        var next = new (string Date, int Year, int Month, int Weekday, int Hour, int Minute, int Parts)[]
        {
            ("2026-11-22", 5787, 4, 4, 11, 11, 4), ("2026-09-01", 5787, 1, 6, 20, 59, 1), ("2026-03-05", 5786, 8, 4, 16, 34, 13),
            ("2027-01-15", 5787, 6, 7, 12, 39, 6), ("2027-02-20", 5787, 7, 2, 1, 23, 7), ("2027-03-10", 5787, 8, 3, 14, 7, 8),
            ("2025-12-30", 5786, 5, 1, 15, 6, 11), ("2026-10-12", 5787, 3, 2, 22, 27, 3),
        };
        foreach (var (date, year, month, weekday, hour, minute, parts) in next)
        {
            var molad = Molads.Next(DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (molad.Year != year || molad.Month != month || (int)molad.MeanTime.DayOfWeek + 1 != weekday || molad.MeanTime.Hour != hour || molad.MeanTime.Minute != minute || molad.Parts != parts)
                throw new Exception($"The molad after {date}: expected {year}/{month} day {weekday} {hour}:{minute:00} and {parts}, got {molad}.");
        }
        var tevet = Molads.Next(new DateTime(2026, 11, 22));
        if (tevet.MonthName("English") != "Tevet" || Molads.Next(new DateTime(2027, 2, 20)).MonthName("English") != "Adar II" || Molads.Next(new DateTime(2027, 2, 20)).MonthName("עברית") != "אדר ב׳" ||
            Molads.Next(new DateTime(2026, 3, 5)).MonthName("русский") != "нисан")
            throw new Exception("The month of the molad is named wrong.");
        Console.WriteLine("PASS: the molad of the next month is found from any date, across the turn of the year and in a leap year.");

        // What does not depend on the reference: from 5700 to 5900 the months follow at 29 days 12 hours 793 chalakim
        // (also from the last month of a year to the first of the next, and in a leap year across both Adars), and Rosh
        // Hashana is the day of the molad or one or two days after it, and never on a Sunday, Wednesday or Friday.
        const long monthParts = 29 * 25920 + 12 * 1080 + 793;
        long Parts(Molad molad) => molad.MeanTime.Ticks / TimeSpan.TicksPerMinute * 18 + molad.Parts;
        Molad previous = null;
        for (int year = 5700; year <= 5900; year++)
        {
            bool leap = Calendar.IsLeapYear(year);
            foreach (int month in Enumerable.Range(1, 13).Where(number => leap || number != 6))
            {
                var molad = Molads.Of(year, month);
                if (previous != null && Parts(molad) - Parts(previous) != monthParts) throw new Exception($"{year} month {month}: the months are not one month apart.");
                previous = molad;
                if (month == 1)
                {
                    DateTime day = molad.MeanTime.AddHours(6).Date, roshHashana = Calendar.ToDateTime(year, 1, 1, 0, 0, 0, 0);
                    if ((roshHashana - day).Days < 0 || (roshHashana - day).Days > 2) throw new Exception($"{year}: Rosh Hashana is {(roshHashana - day).Days} days after the molad.");
                    if (roshHashana.DayOfWeek is DayOfWeek.Sunday or DayOfWeek.Wednesday or DayOfWeek.Friday) throw new Exception($"{year}: Rosh Hashana on {roshHashana.DayOfWeek}.");
                }
            }
        }
        foreach (int month in new[] { 0, 14, -1 })
            try { Molads.Of(5787, month); throw new Exception("A month " + month + " does not exist."); } catch (ArgumentOutOfRangeException) { }
        try { Molads.Of(5786, 6); throw new Exception("There is no Adar I in a year that is not a leap year."); } catch (ArgumentOutOfRangeException) { }
        Console.WriteLine("PASS: in 201 years the months of the molad follow at a constant length across year boundaries and Adars, and Rosh Hashana is within two days after its molad, never on Sunday, Wednesday or Friday.");
    }
}
