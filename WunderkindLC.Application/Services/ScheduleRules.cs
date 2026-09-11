namespace WunderkindLC.Application.Services;

/// <summary>
/// DARS JADVALI qoidalari — sof funksiyalar (`ScheduleRulesTests` bilan qoplangan).
///
/// <para>⚠️ Loyihada QAT'IY "soat/para" tushunchasi YO'Q: guruh vaqti erkin matn
/// (<c>Group.StartTime</c>/<c>EndTime</c>, "HH:mm") va har guruh o'z uzunligiga ega
/// (bazada 60, 90, 120 va hatto 210 daqiqalik darslar bor). Shuning uchun jadval
/// "1-soat, 2-soat" kataklariga emas, HAQIQIY VAQT O'QIGA quriladi.</para>
///
/// <para>⚠️ Bazada BUZUQ qatorlar bor (masalan tugash sanasi boshlanishdan oldin:
/// "13:30 → 03:00", yoki tugash vaqti umuman bo'sh). Ular JIM tashlanadi — bitta
/// buzuq guruh tufayli butun jadval yiqilmasligi kerak.</para>
/// </summary>
public static class ScheduleRules
{
    /// <summary>Bo'sh oraliq shundan qisqa bo'lsa "teshik" hisoblanmaydi (daqiqa).
    /// 60 daqiqa: bazadagi eng qisqa dars 60 daqiqa, ya'ni undan kalta oraliqqa
    /// baribir hech narsa sig'maydi va u faqat ro'yxatni shovqin bilan to'ldirardi.</summary>
    public const int DefaultMinGapMinutes = 60;

    /// <summary>Bo'sh oraliq uchun so'raladigan eng kichik/eng katta chegara (himoya).</summary>
    public const int MinGapMinutesLimit = 15;
    public const int MaxGapMinutesLimit = 8 * 60;

    /// <summary>Bitta dars oralig'i. <paramref name="Day"/>: 0=Dushanba … 6=Yakshanba.</summary>
    public readonly record struct Slot(int Day, int StartMin, int EndMin);

    /// <summary>Ikki dars orasidagi BO'SH oraliq.</summary>
    public readonly record struct Gap(int Day, int StartMin, int EndMin)
    {
        public int Minutes => EndMin - StartMin;
    }

    /// <summary>"HH:mm" → yarim tundan boshlab daqiqa. Bo'sh/noto'g'ri qiymatda <c>null</c>.</summary>
    public static int? ParseTime(string? hhmm)
    {
        if (string.IsNullOrWhiteSpace(hhmm)) return null;
        var s = hhmm.Trim();
        var i = s.IndexOf(':');
        if (i <= 0 || i == s.Length - 1) return null;
        if (!int.TryParse(s[..i], out var h) || !int.TryParse(s[(i + 1)..], out var m)) return null;
        if (h < 0 || h > 23 || m < 0 || m > 59) return null;
        return h * 60 + m;
    }

    /// <summary>Daqiqa → "HH:mm".</summary>
    public static string FormatTime(int minutes)
    {
        var m = ((minutes % 1440) + 1440) % 1440;
        return $"{m / 60:00}:{m % 60:00}";
    }

    /// <summary>Guruh maydonlaridan oraliq yasaydi. Vaqt buzuq yoki tugash boshlanishdan
    /// keyin EMAS bo'lsa — <c>null</c> (bunday qator jadvalga umuman kirmaydi).</summary>
    public static Slot? MakeSlot(int day, string? startTime, string? endTime)
    {
        if (day < 0 || day > 6) return null;
        var s = ParseTime(startTime);
        var e = ParseTime(endTime);
        if (s is null || e is null || e <= s) return null;
        return new Slot(day, s.Value, e.Value);
    }

    /// <summary>Ikki oraliq ustma-ust tushadimi (yarim-ochiq: [start, end)).</summary>
    public static bool Overlaps(int aStart, int aEnd, int bStart, int bEnd) =>
        !(aEnd <= bStart || bEnd <= aStart);

    /// <summary>
    /// Berilgan oraliqlar orasidagi BO'SH oraliqlar (kun bo'yicha alohida).
    ///
    /// <para>⚠️ Faqat IKKI DARS ORASIDAGI bo'shliq qaytadi — kun boshidagi va oxiridagi
    /// bo'sh vaqt EMAS. Sabab: "kunning boshi bo'sh" har xonada har doim rost bo'lardi va
    /// ro'yxat foydasiz uzun chiqardi; muammo esa aynan ORADA qolib ketgan teshik
    /// ("4-soatda dars bor, 5-soat bo'sh, 6-soatda yana dars").</para>
    ///
    /// <para>⚠️ Ustma-ust tushgan darslar avval BIRLASHTIRILADI: xona/o'qituvchi ikki marta
    /// band qilingan bo'lishi mumkin (to'qnashuv saqlashda faqat OGOHLANTIRISH), birlashtirmasak
    /// bunday joyda soxta "manfiy" teshik chiqardi.</para>
    /// </summary>
    public static List<Gap> FindGaps(IEnumerable<Slot> slots, int minMinutes = DefaultMinGapMinutes)
    {
        var gaps = new List<Gap>();
        foreach (var byDay in slots.GroupBy(s => s.Day).OrderBy(g => g.Key))
        {
            var merged = MergeSlots(byDay);
            for (var i = 1; i < merged.Count; i++)
            {
                var from = merged[i - 1].EndMin;
                var to = merged[i].StartMin;
                if (to - from >= minMinutes) gaps.Add(new Gap(byDay.Key, from, to));
            }
        }
        return gaps;
    }

    /// <summary>Bir kundagi oraliqlarni tartiblab, ustma-ust/tegib turganlarini birlashtiradi.</summary>
    public static List<Slot> MergeSlots(IEnumerable<Slot> daySlots)
    {
        var sorted = daySlots.OrderBy(s => s.StartMin).ThenBy(s => s.EndMin).ToList();
        var merged = new List<Slot>();
        foreach (var s in sorted)
        {
            if (merged.Count > 0 && s.StartMin <= merged[^1].EndMin)
            {
                var last = merged[^1];
                if (s.EndMin > last.EndMin) merged[^1] = last with { EndMin = s.EndMin };
                continue;
            }
            merged.Add(s);
        }
        return merged;
    }

    /// <summary>Oraliq shu kunda band vaqtga tegmaydimi (ya'ni bo'shmi).</summary>
    public static bool IsFree(IEnumerable<Slot> busy, int day, int startMin, int endMin) =>
        !busy.Any(b => b.Day == day && Overlaps(b.StartMin, b.EndMin, startMin, endMin));

    /// <summary>
    /// Oraliq shu kundagi darslardan biriga TEGIB turadimi (oldidan yoki ketidan).
    ///
    /// <para>Tavsiyada eng kuchli belgi shu: bo'sh oraliqni allaqachon shu yerda darsi bor
    /// o'qituvchi to'ldirsa, uning kuni UZLUKSIZ bo'ladi — u markazda baribir turibdi,
    /// ya'ni qo'shimcha qatnov ham, kutish ham yo'q.</para>
    /// </summary>
    public static bool TouchesLesson(IEnumerable<Slot> busy, int day, int startMin, int endMin) =>
        busy.Any(b => b.Day == day && (b.EndMin == startMin || b.StartMin == endMin));

    /// <summary>
    /// TIG'IZLIK gistogrammasi: (kun, oraliq boshi) → o'sha payt DAVOM ETAYOTGAN darslar soni.
    ///
    /// <para>Jadval tuzishda savol "qachon bo'sh xona bor" emas, "odam qachon KELADI":
    /// o'qituvchining darsi avval eng tig'iz vaqtga qo'yiladi, chunki o'quvchi aynan o'shanda
    /// kela oladi. Shuning uchun tig'izlikni butun markaz bo'yicha sanaymiz.</para>
    /// </summary>
    /// <param name="bucketMinutes">Katak kengligi (odatda 30 daqiqa).</param>
    public static Dictionary<(int Day, int Bucket), int> BusyHistogram(
        IEnumerable<Slot> slots, int bucketMinutes = 30)
    {
        if (bucketMinutes <= 0) bucketMinutes = 30;
        var map = new Dictionary<(int, int), int>();
        foreach (var s in slots)
        {
            var first = s.StartMin / bucketMinutes * bucketMinutes;
            for (var b = first; b < s.EndMin; b += bucketMinutes)
            {
                // Birinchi katak dars boshlanishidan OLDIN boshlanishi mumkin — u baribir
                // shu katakda dars borligini bildiradi.
                map[(s.Day, b)] = map.GetValueOrDefault((s.Day, b)) + 1;
            }
        }
        return map;
    }

    /// <summary>Oraliq davomidagi ENG YUQORI tig'izlik — tavsiyalarni saralashda ishlatiladi.</summary>
    public static int PeakInRange(
        Dictionary<(int Day, int Bucket), int> histogram,
        int day, int startMin, int endMin, int bucketMinutes = 30)
    {
        if (bucketMinutes <= 0) bucketMinutes = 30;
        var best = 0;
        var first = startMin / bucketMinutes * bucketMinutes;
        for (var b = first; b < endMin; b += bucketMinutes)
            best = Math.Max(best, histogram.GetValueOrDefault((day, b)));
        return best;
    }

    /// <summary>So'ralgan chegarani ruxsat etilgan oraliqqa siqadi.</summary>
    public static int ClampGapMinutes(int? requested) =>
        Math.Clamp(requested ?? DefaultMinGapMinutes, MinGapMinutesLimit, MaxGapMinutesLimit);

    /// <summary>Kun nomi (qisqa) — 0=Dushanba.</summary>
    public static string DayShort(int day) => day switch
    {
        0 => "Du", 1 => "Se", 2 => "Ch", 3 => "Pa", 4 => "Ju", 5 => "Sh", 6 => "Ya",
        _ => "?",
    };
}
