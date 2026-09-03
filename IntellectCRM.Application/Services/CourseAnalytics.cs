namespace IntellectCRM.Application.Services;

/// <summary>
/// KURSLAR ANALITIKASI hisob-kitobining <b>YAGONA MANBASI</b> — sof funksiyalar (bazaga bog'liq
/// emas, testlangan: <c>CourseAnalyticsTests</c>). Controller faqat ma'lumot yuklaydi.
///
/// <para><b>Asosiy qiyinchilik — "ketdi" nima?</b> O'quvchi bir kursda bir necha guruhda bo'lishi
/// mumkin (parallel yoki ketma-ket: guruh almashtirish, tugatib keyingi darajaga o'tish). Har bir
/// a'zolikni alohida sanasak, GURUH ALMASHTIRISH "ketdi + keldi" bo'lib ko'rinardi va hisobot
/// qo'rqinchli, ammo yolg'on churn ko'rsatardi.</para>
///
/// <para>Shuning uchun har (o'quvchi, kurs) juftligi uchun a'zoliklar <b>ORALIQLARGA</b> aylantiriladi
/// va ustma-ust/ketma-ket tushganlari BIRLASHTIRILADI (<see cref="MergeGapDays"/>). "Keldi" = oraliq
/// BOSHLANDI, "ketdi" = oraliq TUGADI — ya'ni o'quvchi kursdan haqiqatan chiqib ketdi.</para>
/// </summary>
public static class CourseAnalytics
{
    /// <summary>
    /// Ikki a'zolik orasidagi shu KUNGACHA bo'lgan tanaffus "chiqib ketish" deb hisoblanmaydi.
    ///
    /// <para>Guruh almashtirish odatda o'sha kuni yoki ertasiga bo'ladi (eski a'zolik muzlatiladi,
    /// yangisi aktivlashtiriladi), lekin qo'lda qilinganda bir necha kun cho'zilishi mumkin. 7 kun —
    /// "bir hafta ichida boshqa guruhda paydo bo'ldi = ketmagan" degan amaliy chegara.</para>
    /// </summary>
    public const int MergeGapDays = 7;

    /// <summary>Hisob uchun kerak bo'lgan a'zolik ma'lumoti (StudentGroup + guruhning kursi).</summary>
    /// <param name="JoinedAt">Guruhga qo'shilgan sana ("yyyy-MM-dd").</param>
    /// <param name="ActivatedAt">Aktivlashtirilgan sana; bo'sh — hali sinovda.</param>
    /// <param name="LeftAt">Guruhdan chiqqan sana; bo'sh/null — hali a'zo.</param>
    /// <param name="FrozenAt">Muzlatilgan sana; bo'sh — muzlatilmagan.</param>
    /// <param name="Status">trial | active | frozen | completed.</param>
    /// <param name="MonthlyFee">Guruh oyligi — joriy oylik tushum bahosi uchun.</param>
    /// <param name="PastPeriods">YOPILGAN faol davrlar (<see cref="Domain.StudentGroup.PastPeriods"/>).
    /// ⚠️ Ularsiz muzlatib qayta aktivlashtirilgan a'zolikda <c>ActivatedAt</c> yangi sanaga o'tgani
    /// uchun "oy oxirida faol" chizig'i IKKI xil xato berardi: o'tmishdagi haqiqatan faol oylar
    /// tushib qolar, muzlab yotgan oylar esa (<c>FrozenAt</c> tozalangani uchun) faol ko'rinardi.</param>
    public readonly record struct MembershipRow(
        string StudentId, string CourseId,
        string JoinedAt, string ActivatedAt, string? LeftAt, string FrozenAt,
        string Status, bool IsActive, decimal MonthlyFee,
        IReadOnlyList<string>? PastPeriods = null);

    /// <summary>Kurs haqidagi ma'lumotnoma (nom/narx) va unga bog'langan guruh/o'qituvchi sanog'i.</summary>
    public readonly record struct CourseRow(
        string CourseId, string Name, decimal Price, int Groups, int Teachers);

    /* =========================================================================================
     *  ORALIQLAR
     * ====================================================================================== */

    /// <summary>Bitta (o'quvchi, kurs) uchun kursda bo'lgan davr.</summary>
    /// <param name="End">Chiqqan sana; <c>null</c> — hali kursda.</param>
    /// <param name="Completed">Oxirgi a'zolik "tugatgan" (sertifikat bilan) bo'lganmi — bu CHURN EMAS.</param>
    public readonly record struct Interval(string Start, string? End, bool Completed);

    /// <summary>
    /// Bir (o'quvchi, kurs) juftligining a'zoliklarini oraliqlarga aylantiradi va
    /// ustma-ust/ketma-ket tushganlarini birlashtiradi.
    /// </summary>
    public static List<Interval> MergeIntervals(IEnumerable<MembershipRow> rows)
    {
        var ordered = rows
            .Where(r => r.JoinedAt.Length >= 10)
            .OrderBy(r => r.JoinedAt, StringComparer.Ordinal)
            .ToList();

        var result = new List<Interval>();
        foreach (var r in ordered)
        {
            var end = string.IsNullOrEmpty(r.LeftAt) ? null : r.LeftAt;
            var completed = r.Status == "completed";

            if (result.Count == 0)
            {
                result.Add(new Interval(r.JoinedAt, end, completed));
                continue;
            }

            var prev = result[^1];
            // Oldingi oraliq HALI OCHIQ bo'lsa (End == null) — yangisi ham shu davrga tushadi
            // (parallel guruh). Ochiq oraliq yopilmaydi.
            if (prev.End is null)
            {
                // Oldingi a'zolik hali OCHIQ (parallel guruh) — o'quvchi kursda qolaveradi.
                // `Completed` ochiq oraliqda ma'noga ega emas: u faqat oraliq YOPILGANDA
                // "tugatdi/ketdi" ni ajratish uchun kerak.
                result[^1] = new Interval(prev.Start, null, false);
                continue;
            }

            // Tanaffus qancha kun? Chegara ichida bo'lsa — bu guruh almashtirish, ketish EMAS.
            if (DaysBetween(prev.End, r.JoinedAt) <= MergeGapDays)
            {
                if (end is null)
                {
                    result[^1] = new Interval(prev.Start, null, false);
                    continue;
                }
                // `Completed` AYNAN oraliqni yopgan a'zolikdan olinadi. Ilgari u har doim
                // oxirgi qayta ishlangan qatordan olinardi — parallel guruhda erta tugagan
                // a'zolik butun oraliqni "tugatgan" deb belgilab qo'yishi mumkin edi.
                var later = string.CompareOrdinal(end, prev.End) > 0;
                result[^1] = new Interval(prev.Start, later ? end : prev.End,
                    later ? completed : prev.Completed);
                continue;
            }

            result.Add(new Interval(r.JoinedAt, end, completed));
        }

        return result;
    }

    /// <summary>Ikki ISO sana orasidagi kunlar (buzuq sana — juda katta son, ya'ni "birlashtirilmaydi").</summary>
    private static int DaysBetween(string from, string to) =>
        DateOnly.TryParse(from, out var a) && DateOnly.TryParse(to, out var b)
            ? b.DayNumber - a.DayNumber
            : int.MaxValue;

    /* =========================================================================================
     *  OYLIK OQIM
     * ====================================================================================== */

    /// <summary>Bir kursning bir oydagi oqimi.</summary>
    /// <param name="Joined">Kursga KELGAN o'quvchilar (oraliq boshlandi) — sinovdagilar ham.</param>
    /// <param name="Activated">Shu oyda BIRINCHI marta aktivlashgan (to'lov boshlangan) o'quvchilar.</param>
    /// <param name="Left">KETGAN — oraliq tugadi va bu "tugatgan" emas (haqiqiy churn).</param>
    /// <param name="Completed">Kursni TUGATGAN (sertifikat bilan yopilgan) — churn emas.</param>
    /// <param name="ActiveEnd">Oy OXIRIDA faol (aktivlashgan, ketmagan, muzlatilmagan) o'quvchilar.</param>
    public readonly record struct MonthFlow(
        string Month, int Joined, int Activated, int Left, int Completed, int ActiveEnd);

    /// <summary>Oy oxirgi kuni ("yyyy-MM" → "yyyy-MM-dd").</summary>
    public static string MonthEnd(string month)
    {
        if (month.Length < 7 || !int.TryParse(month[..4], out var y) || !int.TryParse(month[5..7], out var m))
            return month;
        return new DateOnly(y, m, DateTime.DaysInMonth(y, m)).ToString("yyyy-MM-dd");
    }

    /// <summary>
    /// Bitta kursning oylik oqimi. <paramref name="byStudent"/> — shu kursdagi a'zoliklar
    /// o'quvchi bo'yicha guruhlangan holda.
    /// </summary>
    public static List<MonthFlow> MonthlyFlow(
        Dictionary<string, List<MembershipRow>> byStudent, IReadOnlyList<string> months)
    {
        // Har o'quvchi uchun birlashtirilgan oraliqlar va birinchi aktivlashish oyi — bir marta.
        var intervals = byStudent.ToDictionary(kv => kv.Key, kv => MergeIntervals(kv.Value));
        var firstActivation = byStudent.ToDictionary(
            kv => kv.Key,
            // ⚠️ Joriy ActivatedAt EMAS: qayta aktivlashtirishda u ustidan yoziladi va "birinchi marta
            // aktivlashdi" hodisasi noto'g'ri oyga ko'chib ketardi. MembershipLifecycle yopilgan
            // davrlarning boshlanishini ham ko'radi.
            kv => kv.Value.Select(r => MembershipLifecycle.FirstActivatedAt(r.ActivatedAt, r.PastPeriods))
                .Where(d => d.Length >= 7).OrderBy(d => d, StringComparer.Ordinal).FirstOrDefault());

        var flows = new List<MonthFlow>(months.Count);
        foreach (var month in months)
        {
            var end = MonthEnd(month);
            int joined = 0, activated = 0, left = 0, completed = 0, activeEnd = 0;

            foreach (var (studentId, list) in intervals)
            {
                // KELDI / KETDI — oraliqlar bo'yicha (bitta o'quvchi bir oyda bir marta sanaladi).
                if (list.Any(i => i.Start.Length >= 7 && i.Start[..7] == month)) joined++;
                var closed = list.FirstOrDefault(i => i.End is { Length: >= 7 } e && e[..7] == month);
                if (closed.End is not null)
                {
                    if (closed.Completed) completed++;
                    else left++;
                }

                var act = firstActivation.GetValueOrDefault(studentId);
                if (!string.IsNullOrEmpty(act) && act.Length >= 7 && act[..7] == month) activated++;

                // OY OXIRIDA FAOL — holat maydonlariga emas, SANALARGA qarab tiklanadi
                // (`Status` joriy holatni bildiradi, o'tmishdagini emas).
                if (byStudent[studentId].Any(r => WasActiveAt(r, end))) activeEnd++;
            }

            flows.Add(new MonthFlow(month, joined, activated, left, completed, activeEnd));
        }
        return flows;
    }

    /// <summary>
    /// A'zolik <paramref name="date"/> sanasida FAOL bo'lganmi.
    ///
    /// <para>`Status` ustuni JORIY holatni bildiradi (bugun muzlatilgan a'zolik o'tgan oyda faol
    /// bo'lgan bo'lishi mumkin), shuning uchun tarixni SANALAR bo'yicha tiklaymiz:
    /// aktivlashtirilgan ✓, hali ketmagan ✓, hali muzlatilmagan ✓.</para>
    /// </summary>
    public static bool WasActiveAt(MembershipRow r, string date)
    {
        // YOPILGAN davrlar avval: o'sha paytda a'zolik haqiqatan faol edi, garchi bugungi
        // ActivatedAt/FrozenAt boshqa (keyingi) davrni ko'rsatayotgan bo'lsa ham.
        if (r.PastPeriods is not null)
            foreach (var p in r.PastPeriods)
                if (MembershipLifecycle.TryParsePeriod(p, out var pFrom, out var pTo)
                    && string.CompareOrdinal(pFrom, date) <= 0
                    && (pTo.Length < 10 || string.CompareOrdinal(date, pTo) < 0))
                    return true;
        if (r.ActivatedAt.Length < 10 || string.CompareOrdinal(r.ActivatedAt, date) > 0) return false;
        if (!string.IsNullOrEmpty(r.LeftAt) && string.CompareOrdinal(r.LeftAt, date) <= 0) return false;
        if (r.FrozenAt.Length >= 10 && string.CompareOrdinal(r.FrozenAt, date) <= 0) return false;
        return true;
    }

    /* =========================================================================================
     *  KURSLAR KESISHUVI
     * ====================================================================================== */

    /// <summary>Nechta kursga qatnashadigan o'quvchilar taqsimoti.</summary>
    public readonly record struct OverlapBucket(int Courses, int Students);

    /// <summary>Ikki kursning birga o'qilishi.</summary>
    public readonly record struct CoursePair(string AId, string BId, int Students);

    /// <summary>
    /// Kurslar kesishuvi: har o'quvchining FAOL kurslari to'plamidan taqsimot va juftliklar.
    /// </summary>
    /// <param name="activeByStudent">O'quvchi → u FAOL o'qiyotgan kurs id'lari (takrorsiz).</param>
    public static (List<OverlapBucket> Buckets, List<CoursePair> Pairs) Overlap(
        Dictionary<string, HashSet<string>> activeByStudent)
    {
        var buckets = activeByStudent.Values
            .Where(s => s.Count > 0)
            .GroupBy(s => s.Count)
            .Select(g => new OverlapBucket(g.Key, g.Count()))
            .OrderBy(b => b.Courses)
            .ToList();

        var pairCounts = new Dictionary<(string, string), int>();
        foreach (var set in activeByStudent.Values.Where(s => s.Count > 1))
        {
            // Juftlik kaliti TARTIBLANGAN — (A,B) va (B,A) bitta qator bo'lsin.
            var sorted = set.OrderBy(c => c, StringComparer.Ordinal).ToList();
            for (var i = 0; i < sorted.Count; i++)
                for (var j = i + 1; j < sorted.Count; j++)
                {
                    var key = (sorted[i], sorted[j]);
                    pairCounts[key] = pairCounts.GetValueOrDefault(key) + 1;
                }
        }

        var pairs = pairCounts
            .Select(kv => new CoursePair(kv.Key.Item1, kv.Key.Item2, kv.Value))
            .OrderByDescending(p => p.Students)
            .ToList();

        return (buckets, pairs);
    }

    /* =========================================================================================
     *  Yordamchi
     * ====================================================================================== */

    /// <summary>Oxirgi <paramref name="count"/> oy ("yyyy-MM"), eng eskisidan boshlab.</summary>
    public static List<string> LastMonths(DateOnly today, int count)
    {
        var months = new List<string>(count);
        var start = new DateOnly(today.Year, today.Month, 1).AddMonths(-(count - 1));
        for (var i = 0; i < count; i++) months.Add(start.AddMonths(i).ToString("yyyy-MM"));
        return months;
    }
}
