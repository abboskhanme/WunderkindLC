namespace IntellectCRM.Application.Services;

/// <summary>
/// O'quvchi a'zoligi (StudentGroup) holat jamlagichi — YAGONA MANBA. O'qituvchilar hisoboti
/// (<see cref="TeacherActivityReport"/>) va o'qituvchi "performance" (TeachersController) AYNAN shu
/// ta'rifni ishlatadi, shuning uchun raqamlar hamma joyda bir xil chiqadi.
///
/// Ta'rif (arxivlanmagan guruhlar bo'yicha, per-a'zolik):
///   Ketgan  (Left)   = !IsActive yoki LeftAt bor
///   qolganlar (a'zo) → holatiga qarab:
///     Faol   (Active) = Status=="active" (yoki noma'lum holat)
///     Sinov  (Trial)  = Status=="trial"
///     Muzlat (Frozen) = Status=="frozen"
///   Kelgan (Came)      = jami a'zolik
///   Qolgan (Remaining) = Active + Trial + Frozen = Came − Left (hozir a'zolar).
/// </summary>
public readonly record struct LifecycleTally(int Came, int Active, int Trial, int Frozen, int Left)
{
    /// <summary>Hozir a'zo (faol + sinov + muzlatilgan) = Came − Left.</summary>
    public int Remaining => Active + Trial + Frozen;
    /// <summary>Kelganlardan faol bo'lib qolganlar foizi (Active / Came * 100). Came=0 → null.</summary>
    public int? ConversionPct => Came > 0 ? (int)System.Math.Round(Active * 100.0 / Came) : null;
    /// <summary>Retention % (Faol / Came * 100), bir kasr xona bilan.</summary>
    public double Retention => Came > 0 ? System.Math.Round((double)Active / Came * 100, 1) : 0;
    /// <summary>Yo'qotish % ((Muzlatilgan + Ketgan) / Came * 100).</summary>
    public double Loss => Came > 0 ? System.Math.Round((double)(Frozen + Left) / Came * 100, 1) : 0;
}

/// <summary>A'zoliklar ro'yxatidan <see cref="LifecycleTally"/> hisoblaydi.</summary>
public static class MembershipLifecycle
{
    public static LifecycleTally Tally(IEnumerable<(string Status, bool IsActive, string? LeftAt)> memberships)
    {
        int came = 0, active = 0, trial = 0, frozen = 0, left = 0;
        foreach (var (status, isActive, leftAt) in memberships)
        {
            came++;
            if (!isActive || !string.IsNullOrEmpty(leftAt)) { left++; continue; }
            switch (status)
            {
                case "trial": trial++; break;
                case "frozen": frozen++; break;
                default: active++; break; // "active" yoki noma'lum → faol
            }
        }
        return new LifecycleTally(came, active, trial, frozen, left);
    }

    /// <summary>
    /// A'zolik SHU OYDA pullik (billable) bo'lganmi — ya'ni oylik to'lov hisoblanadigan oymi.
    ///
    /// <para><b>YAGONA TA'RIF.</b> Maosh (<c>SalaryLedger</c>) teglanmagan to'lovni guruhlar orasida
    /// shu qoida bo'yicha taqsimlaydi, ushlab turish bonusi (<c>RetentionBonusService</c>) esa
    /// "shu oyda o'quvchi haqiqatan o'qiganmi" savoliga shu bilan javob beradi. Ikki nusxa bo'lsa
    /// vaqt o'tib bir-biridan ajralib ketadi va maosh bir oyni "pullik", bonus esa "pullik emas"
    /// deb hisoblab, raqamlar bir-biriga to'g'ri kelmay qoladi.</para>
    ///
    /// <para>Qoida: sinov (trial) — hisoblanmaydi; aktivlashtirilgan oydan boshlab; muzlatish
    /// oyigacha (muzlatish oyining O'ZI kiradi — billing konvensiyasi).</para>
    /// </summary>
    /// <param name="month">"YYYY-MM"</param>
    public static bool BillableInMonth(string status, string activatedAt, string frozenAt, string month) =>
        BillableInMonth(status, activatedAt, frozenAt, month, null);

    /// <inheritdoc cref="BillableInMonth(string,string,string,string)"/>
    /// <param name="pastPeriods">YOPILGAN faol davrlar (<see cref="Domain.StudentGroup.PastPeriods"/>).
    /// ⚠️ Ular <b>"trial" tekshiruvidan OLDIN</b> ko'riladi: a'zolik BUGUN sinovga qaytarilgan bo'lsa ham
    /// o'tmishda haqiqatan pullik bo'lgan oy pullik bo'lib qolishi kerak.</param>
    public static bool BillableInMonth(string status, string activatedAt, string frozenAt, string month,
                                       IEnumerable<string>? pastPeriods)
    {
        if (pastPeriods is not null)
            foreach (var p in pastPeriods)
                if (MonthInPeriod(p, month)) return true;
        if (status == "trial") return false;
        var actOk = activatedAt.Length < 7 || string.CompareOrdinal(month, activatedAt[..7]) >= 0;
        var frzOk = frozenAt.Length < 7 || string.CompareOrdinal(month, frozenAt[..7]) <= 0;
        return actOk && frzOk;
    }

    /// <inheritdoc cref="BillableInMonth(string,string,string,string)"/>
    public static bool BillableInMonth(Domain.StudentGroup m, string month) =>
        BillableInMonth(m.Status, m.ActivatedAt, m.FrozenAt, month, m.PastPeriods);

    // ==================== YOPILGAN FAOL DAVRLAR (StudentGroup.PastPeriods) ====================

    /// <summary>Davr qatoridagi ajratkich: "boshlanish|tugash".</summary>
    public const char PeriodSep = '|';

    /// <summary>Bitta a'zolikda saqlanadigan yopilgan davrlar CHEGARASI. Oshib ketsa eng ESKISI
    /// tashlanadi — ro'yxat cheksiz o'smasin (u har so'rovda entity bilan birga yuklanadi).
    /// 50 ta davr amalda erishib bo'lmaydigan son (yiliga bir-ikki muzlatish).</summary>
    public const int MaxPastPeriods = 50;

    /// <summary>Haqiqiy ISO sana ("YYYY-MM-DD")mi. Bo'sh/qisqa/mavjud bo'lmagan sana — <c>false</c>.</summary>
    private static bool IsDate(string? iso) =>
        iso is { Length: >= 10 } && DateOnly.TryParse(iso[..10], System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _);

    /// <summary>"boshlanish|tugash" qatorini ajratadi. Yaroqsiz/bo'sh qator — <c>false</c>
    /// (buzuq yozuv butun hisobni yiqitmasin).</summary>
    public static bool TryParsePeriod(string? raw, out string from, out string to)
    {
        from = to = string.Empty;
        if (string.IsNullOrEmpty(raw)) return false;
        var i = raw.IndexOf(PeriodSep);
        if (i < 0) return false;
        from = raw[..i];
        to = raw[(i + 1)..];
        return from.Length >= 7;
    }

    /// <summary>Oy shu YOPILGAN davr ichidami — chegaralar KIRADI (aktivlashtirish oyi ham,
    /// muzlatish oyi ham), ya'ni <see cref="BillableInMonth"/> konvensiyasi bilan bir xil.
    /// Tugashi bo'sh davr — ochiq (bunday yozuv bo'lmasligi kerak, lekin yo'qotmaymiz).</summary>
    public static bool MonthInPeriod(string? raw, string month)
    {
        if (!TryParsePeriod(raw, out var from, out var to)) return false;
        if (string.CompareOrdinal(month, from[..7]) < 0) return false;
        return to.Length < 7 || string.CompareOrdinal(month, to[..7]) <= 0;
    }

    /// <summary>Oy shu yopilgan davr uchun oylik hisob YOZILADIGAN oymi — chegaralar KIRMAYDI.
    /// <para>⚠️ <see cref="MonthInPeriod"/> dan ATAYIN farq qiladi: aktivlashtirish oyi va muzlatish oyi
    /// QISMAN hisob bilan o'sha paytning O'ZIDA yozilgan (<c>TuitionService.ChargeActivationProrateAsync</c> /
    /// <c>ChargeFreezeProrateAsync</c>) — ularni qayta yozsak ikki marta hisoblangan bo'lardi. Bu ayni
    /// <c>TuitionService.AccrueMonth</c> dagi joriy davr sharti (qat'iy &gt; va &lt;).</para></summary>
    public static bool AccruableInPeriod(string? raw, string month)
    {
        if (!TryParsePeriod(raw, out var from, out var to)) return false;
        if (string.CompareOrdinal(month, from[..7]) <= 0) return false;
        return to.Length >= 7 && string.CompareOrdinal(month, to[..7]) < 0;
    }

    /// <summary>A'zolikning ENG ERTA aktivlashtirilgan sanasi (ISO) — yopilgan davrlar ham hisobga olinadi.
    /// <para>"Shu oyda aktivlashdi" tipidagi HAR QANDAY ko'rsatkich shundan foydalanishi kerak: joriy
    /// <see cref="Domain.StudentGroup.ActivatedAt"/> qayta aktivlashtirishda ustidan yozilgani uchun
    /// hodisa noto'g'ri oyga ko'chib ketardi.</para>
    /// <returns>Sana topilmasa bo'sh satr.</returns></summary>
    public static string FirstActivatedAt(string activatedAt, IEnumerable<string>? pastPeriods)
    {
        var best = activatedAt.Length >= 7 ? activatedAt : string.Empty;
        if (pastPeriods is not null)
            foreach (var p in pastPeriods)
                if (TryParsePeriod(p, out var from, out _)
                    && (best.Length < 7 || string.CompareOrdinal(from, best) < 0))
                    best = from;
        return best;
    }

    /// <inheritdoc cref="FirstActivatedAt(string,System.Collections.Generic.IEnumerable{string})"/>
    public static string FirstActivatedAt(Domain.StudentGroup m) =>
        FirstActivatedAt(m.ActivatedAt, m.PastPeriods);

    /// <summary>A'zolik SHU OYDA aktivlashtirilganmi — yopilgan davrlarning boshlanishi ham hisobga olinadi.
    /// <para>"Shu oyda nechta o'quvchi aktivlashdi" tipidagi OQIM grafiklari uchun: joriy
    /// <see cref="Domain.StudentGroup.ActivatedAt"/> ga qarasak, qayta aktivlashtirilgan a'zolikda
    /// eski hodisa yo'qolib, yangi oyga ko'chib ketardi.</para>
    /// <para>Bir a'zolik bir oyda ikki marta aktivlashtirilsa ham BIR marta sanaladi (funksiya
    /// bool qaytaradi) — sanoq A'ZOLIKLAR bo'yicha, hodisalar bo'yicha emas.</para></summary>
    public static bool ActivatedInMonth(Domain.StudentGroup m, string month)
    {
        if (m.ActivatedAt.Length >= 7 && m.ActivatedAt[..7] == month) return true;
        foreach (var p in m.PastPeriods)
            if (TryParsePeriod(p, out var from, out _) && from[..7] == month) return true;
        return false;
    }

    /// <summary>YOPILGAN davrlarni <paramref name="dateIso"/> sanasiga QIRQADI: undan keyin cho'zilgan
    /// davr shu sanada tugatiladi, butunlay keyin boshlangani esa O'CHIRILADI.
    ///
    /// <para>NEGA KERAK: ORQAGA sanalgan muzlatishda <c>TuitionService.PurgeChargesAfterMonthAsync</c>
    /// shu (o'quvchi, guruh) ning muzlatish oyidan KEYINGI BARCHA hisoblarini o'chiradi va pulni
    /// balansga qaytaradi — shu jumladan ESKI yopilgan davr ichidagi oylarni ham. Davrlarni
    /// qirqmasak, keyingi accrual sikli o'sha o'chirilgan oylarni QAYTA yozib qo'yardi va
    /// muzlatishning ma'nosi yo'qolardi.</para>
    ///
    /// <para>Ya'ni tarix hisob bilan IZCHIL qoladi: "hisobi bekor qilingan oy" hech qachon
    /// "pullik davr ichida" bo'lib qolmaydi.</para></summary>
    public static void TruncatePastPeriodsAfter(Domain.StudentGroup m, string dateIso)
    {
        if (!IsDate(dateIso) || m.PastPeriods.Count == 0) return;
        var cut = dateIso[..10];
        for (var i = m.PastPeriods.Count - 1; i >= 0; i--)
        {
            if (!TryParsePeriod(m.PastPeriods[i], out var from, out var to)) continue;
            if (string.CompareOrdinal(from, cut) > 0) { m.PastPeriods.RemoveAt(i); continue; }
            if (to.Length >= 10 && string.CompareOrdinal(to, cut) <= 0) continue; // tegilmaydi
            m.PastPeriods[i] = from + PeriodSep + cut;
        }
    }

    /// <summary>JORIY faol davrni YOPILGANLAR ro'yxatiga ko'chiradi — chaqiruvchi
    /// <see cref="Domain.StudentGroup.ActivatedAt"/> ni ustidan yozishdan (yoki tozalashdan) OLDIN.
    /// <para>Tugash sanasi: <c>FrozenAt</c> → <c>LeftAt</c> → <paramref name="fallbackEndIso"/>.
    /// Davr boshlanmagan bo'lsa (<c>ActivatedAt</c> bo'sh) hech narsa qilmaydi.</para>
    /// <para>IDEMPOTENT — bir xil qator ikki marta qo'shilmaydi (ikki marta bosilgan tugma
    /// tarixni ikkilantirmasin). SaveChanges QILMAYDI.</para></summary>
    public static void ClosePeriod(Domain.StudentGroup m, string fallbackEndIso)
    {
        if (!IsDate(m.ActivatedAt)) return;
        var end = IsDate(m.FrozenAt) ? m.FrozenAt
            : IsDate(m.LeftAt) ? m.LeftAt!
            : fallbackEndIso ?? string.Empty;
        // ⚠️ FAQAT HAQIQIY sanalar yoziladi. Aktivlashtirish endpointi `date` ni validatsiya
        // qilmaydi, ya'ni bazaga "2026-13-99" tushishi mumkin; bunday qator keyin oylar
        // oralig'ini hisoblaganda CHEKSIZ siklga olib borardi (TuitionService.MonthRange).
        if (!IsDate(end)) return;
        // Teskari oraliq bo'lmasin (orqaga sanalgan amal): tugash boshlanishdan oldin bo'lsa —
        // davr bir kunlik deb yopiladi, aks holda hech bir oy unga tushmasdi.
        if (string.CompareOrdinal(end, m.ActivatedAt) < 0) end = m.ActivatedAt;
        var row = m.ActivatedAt + PeriodSep + end;
        if (m.PastPeriods.Contains(row)) return;
        m.PastPeriods.Add(row);
        while (m.PastPeriods.Count > MaxPastPeriods) m.PastPeriods.RemoveAt(0);
    }
}
