using WunderkindLC.Application.Dtos;

namespace WunderkindLC.Application.Services;

/// <summary>
/// BOSH SAHIFA jadval to'ri — bir kunlik ko'rinish va uning Excel eksporti (sof funksiyalar,
/// <c>ScheduleGridExportTests</c>).
///
/// <para>⚠️ Klientdagi to'r (<c>Client/src/lib/scheduleGrid.ts</c>) AYNAN shu qoidalar bilan
/// chiziladi: ustun kaliti (<see cref="RoomKey"/>), kunlar yorlig'i (<see cref="DaysLabel"/>),
/// vaqt oralig'i filtri va to'r chegaralari (<see cref="Range"/>). Birida o'zgarsa ikkinchisini
/// ham o'zgartiring — aks holda eksport ekrandagidan boshqa narsani yozib berardi.</para>
/// </summary>
public static class ScheduleGridExport
{
    /// <summary>To'rning bitta qatori (daqiqa) — edutizim'dagi kabi yarim soat.</summary>
    public const int SlotMinutes = 30;

    /// <summary>Standart vaqt oralig'i: 08:00–22:00 (edutizim manzilidagi <c>fromHour/toHour</c>).</summary>
    public const int DefaultFromMin = 8 * 60;
    public const int DefaultToMin = 22 * 60;

    /// <summary>Kun qisqa nomi (0=Dushanba … 6=Yakshanba) — edutizim yozuvida.</summary>
    private static readonly string[] DayAbbr = ["Du", "Se", "Chor", "Pa", "Ju", "Sha", "Yak"];

    /// <summary>Bir kunlik ko'rinish filtri.</summary>
    /// <param name="Day">0=Dushanba … 6=Yakshanba (server konvensiyasi).</param>
    /// <param name="ByTeacher">Ustunlar — o'qituvchilar (aks holda xonalar).</param>
    /// <param name="FromMin">Vaqt oralig'i boshi (daqiqa) — shu oraliqqa TEGADIGAN darslar ko'rinadi.</param>
    /// <param name="Status">Guruh holati (<see cref="ScheduleGridGroupDto.Status"/>): active | full | blocked.</param>
    public sealed record Filter(
        int Day, bool ByTeacher, int FromMin, int ToMin,
        string? CourseId = null, string? TeacherId = null, string? RoomId = null,
        string? GroupId = null, string? Status = null);

    /// <summary>
    /// Xona USTUNI kaliti: xona FK bo'lsa uning id'si, bo'lmasa eski matnli nom (<c>"name:..."</c>),
    /// umuman bo'lmasa bo'sh ("Xona belgilanmagan" ustuni).
    /// </summary>
    public static string RoomKey(string? roomId, string? roomName) =>
        !string.IsNullOrEmpty(roomId) ? roomId
        : !string.IsNullOrEmpty(roomName) ? "name:" + roomName
        : "";

    /// <summary>
    /// Guruh kunlari yorlig'i: {Du, Chor, Ju} → "Toq kunlar", {Se, Pa, Sha} → "Juft kunlar",
    /// Dushanbadan Shanbagacha hammasi → "Har kuni", qolgani — qisqa nomlar ro'yxati.
    /// </summary>
    public static string DaysLabel(IEnumerable<int> days)
    {
        var set = days.Where(d => d is >= 0 and <= 6).Distinct().Order().ToList();
        if (set.Count == 0) return "";
        if (set.SequenceEqual([0, 2, 4])) return "Toq kunlar";
        if (set.SequenceEqual([1, 3, 5])) return "Juft kunlar";
        if (Enumerable.Range(0, 6).All(set.Contains)) return "Har kuni";
        return string.Join(", ", set.Select(d => DayAbbr[d]));
    }

    /// <summary>Rejadagi darslar soni hisoblanadigan eng uzun davr (kun) — buzuq sana (masalan
    /// "2026-01-01 → 2099-12-31") cheksiz sikl va ma'nosiz raqam bermasin.</summary>
    public const int MaxPlanDays = 3 * 366;

    /// <summary>
    /// Rejadagi JAMI darslar: <paramref name="startDate"/>–<paramref name="endDate"/> (ikkalasi ham
    /// kiradi) oralig'idagi guruh dars kunlari. Sanalardan biri bo'sh/buzuq, teskari yoki
    /// <see cref="MaxPlanDays"/> dan uzun bo'lsa — 0 ("noma'lum"). Bir martalik ko'chirishlar
    /// (<c>LessonReschedule</c>) hisobga olinmaydi: ular darslar SONINI o'zgartirmaydi.
    /// </summary>
    public static int PlannedLessons(IEnumerable<int> days, string? startDate, string? endDate)
    {
        if (!DateOnly.TryParseExact(startDate, "yyyy-MM-dd", out var from)
            || !DateOnly.TryParseExact(endDate, "yyyy-MM-dd", out var to)
            || to < from || to.DayNumber - from.DayNumber > MaxPlanDays)
            return 0;
        var set = days.Where(d => d is >= 0 and <= 6).ToHashSet();
        if (set.Count == 0) return 0;
        var n = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
            if (set.Contains(((int)d.DayOfWeek + 6) % 7)) n++;
        return n;
    }

    /// <summary>So'rov parametrlaridan filtr: noto'g'ri qiymatlar JIM standartga tushadi
    /// (eskirgan/qo'lda yozilgan havola eksportni buzmasin).</summary>
    public static Filter MakeFilter(
        int? day, string? groupBy, string? fromHour, string? toHour,
        string? courseId, string? teacherId, string? roomId, int todayDay,
        string? groupId = null, string? status = null)
    {
        var d = day is >= 0 and <= 6 ? day.Value : todayDay;
        var from = ScheduleRules.ParseTime(fromHour) ?? DefaultFromMin;
        var to = ScheduleRules.ParseTime(toHour) ?? DefaultToMin;
        if (to <= from) (from, to) = (DefaultFromMin, DefaultToMin);
        return new Filter(d, groupBy == "teacher", from, to,
            Blank(courseId), Blank(teacherId), Blank(roomId), Blank(groupId), Blank(status));
    }

    /// <summary>Tanlangan kundagi darslar (filtrlar bilan), boshlanish vaqti bo'yicha tartiblangan.</summary>
    public static List<ScheduleGridGroupDto> LessonsOfDay(IEnumerable<ScheduleGridGroupDto> groups, Filter f) =>
        groups
            .Where(g => g.Days.Contains(f.Day))
            .Where(g => f.CourseId is null || g.CourseId == f.CourseId)
            .Where(g => f.TeacherId is null || g.TeacherId == f.TeacherId)
            .Where(g => f.RoomId is null || RoomKey(g.RoomId, g.RoomName) == f.RoomId)
            .Where(g => f.GroupId is null || g.GroupId == f.GroupId)
            .Where(g => f.Status is null || g.Status == f.Status)
            .Where(g =>
            {
                var s = ScheduleRules.ParseTime(g.Start);
                var e = ScheduleRules.ParseTime(g.End);
                return s is not null && e is not null
                       && ScheduleRules.Overlaps(s.Value, e.Value, f.FromMin, f.ToMin);
            })
            .OrderBy(g => g.Start, StringComparer.Ordinal).ThenBy(g => g.GroupName)
            .ToList();

    /// <summary>
    /// To'r chegaralari: tanlangan oraliq, lekin undan chiqib ketgan dars bo'lsa u TO'LIQ sig'ishi
    /// uchun kengaytiriladi (07:00 da boshlanadigan dars 08:00 filtrida ham qirqilmay chiziladi).
    /// Yarim soatga yaxlitlanadi.
    /// </summary>
    public static (int From, int To) Range(IReadOnlyList<ScheduleGridGroupDto> lessons, Filter f)
    {
        var from = f.FromMin;
        var to = f.ToMin;
        foreach (var g in lessons)
        {
            if (ScheduleRules.ParseTime(g.Start) is { } s && s < from) from = s;
            if (ScheduleRules.ParseTime(g.End) is { } e && e > to) to = e;
        }
        from = from / SlotMinutes * SlotMinutes;
        to = (to + SlotMinutes - 1) / SlotMinutes * SlotMinutes;
        return (from, to);
    }

    /// <summary>
    /// Ustunlar: BARCHA xonalar / o'qituvchilar (darsi yo'qlari ham — kim/qaysi xona bo'shligi
    /// ko'rinishi kerak) va egasiz darslar uchun bitta qo'shimcha ustun. Xona yoki o'qituvchi
    /// filtri tanlansa — faqat o'sha ustun.
    /// </summary>
    public static List<(string Key, string Name)> Columns(
        ScheduleGridDto grid, IReadOnlyList<ScheduleGridGroupDto> lessons, Filter f)
    {
        var cols = new List<(string Key, string Name)>();
        if (f.ByTeacher)
        {
            cols.AddRange(grid.Teachers.Select(t => (t.Id, t.Name)));
            if (lessons.Any(l => l.TeacherId.Length == 0)) cols.Add(("", "O'qituvchi belgilanmagan"));
            if (f.TeacherId is not null) cols = cols.Where(c => c.Key == f.TeacherId).ToList();
            return cols;
        }

        cols.AddRange(grid.Rooms.Select(r => (r.Id, r.Name)));
        if (lessons.Any(l => RoomKey(l.RoomId, l.RoomName).Length == 0)) cols.Add(("", "Xona belgilanmagan"));
        if (f.RoomId is not null) cols = cols.Where(c => c.Key == f.RoomId).ToList();
        return cols;
    }

    /// <summary>Excel kitobi: "Jadval" (vaqt × ustun to'ri) va "Ro'yxat" (darslar vaqt bo'yicha).</summary>
    public static IReadOnlyList<ExcelExport.SheetSpec> Sheets(ScheduleGridDto grid, Filter f)
    {
        var lessons = LessonsOfDay(grid.Groups, f);
        var cols = Columns(grid, lessons, f);
        var (from, to) = Range(lessons, f);

        string ColumnOf(ScheduleGridGroupDto g) => f.ByTeacher ? g.TeacherId : RoomKey(g.RoomId, g.RoomName);

        var gridRows = new List<IReadOnlyList<string>>();
        for (var t = from; t < to; t += SlotMinutes)
        {
            var row = new List<string> { $"{ScheduleRules.FormatTime(t)} - {ScheduleRules.FormatTime(t + SlotMinutes)}" };
            foreach (var (key, _) in cols)
            {
                var here = lessons.Where(l => ColumnOf(l) == key
                    && ScheduleRules.Overlaps(ScheduleRules.ParseTime(l.Start)!.Value,
                        ScheduleRules.ParseTime(l.End)!.Value, t, t + SlotMinutes));
                row.Add(string.Join("\n", here.Select(l =>
                {
                    var s = ScheduleRules.ParseTime(l.Start)!.Value;
                    // Dars boshlangan katakda — to'liq ma'lumot, davomida — faqat kurs nomi.
                    return s >= t && s < t + SlotMinutes
                        ? $"{l.Start} - {l.End} · {Title(l)} · {l.GroupName} · " +
                          (f.ByTeacher ? l.RoomName : l.TeacherName)
                        : $"↳ {Title(l)}";
                })));
            }
            gridRows.Add(row);
        }

        var listRows = lessons.Select(l => (IReadOnlyList<string>)new[]
        {
            $"{l.Start} - {l.End}", Title(l), l.GroupName, l.TeacherName, l.RoomName,
            DaysLabel(l.Days), l.Capacity > 0 ? $"{l.Members}/{l.Capacity}" : l.Members.ToString(),
            l.LessonsTotal > 0 ? $"{l.LessonsDone}/{l.LessonsTotal}" : l.LessonsDone.ToString(),
        }).ToList();

        return
        [
            new ExcelExport.SheetSpec("Jadval", ["Vaqt", .. cols.Select(c => c.Name)], gridRows),
            new ExcelExport.SheetSpec("Ro'yxat",
                ["Vaqt", "Kurs", "Guruh", "O'qituvchi", "Xona", "Kunlar", "O'quvchilar", "Darslar"], listRows),
        ];
    }

    /// <summary>"1-bino 2-xona" &lt; "1-bino 10-xona" — raqamlar SON sifatida solishtiriladi
    /// (klientdagi <c>localeCompare(..., { numeric: true })</c> bilan bir xil tartib).</summary>
    public static IComparer<string> NaturalComparer { get; } = Comparer<string>.Create(CompareNatural);

    private static int CompareNatural(string? a, string? b)
    {
        a ??= "";
        b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                var si = i;
                var sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = a[si..i].TrimStart('0');
                var nb = b[sj..j].TrimStart('0');
                var c = na.Length != nb.Length ? na.Length.CompareTo(nb.Length) : string.CompareOrdinal(na, nb);
                if (c != 0) return c;
                continue;
            }
            var cc = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
            if (cc != 0) return cc;
            i++;
            j++;
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }

    private static string Title(ScheduleGridGroupDto g) => g.CourseName.Length > 0 ? g.CourseName : g.GroupName;

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
