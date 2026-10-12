using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Jurnal o'qish/yozish mantig'i (baho, davomat, dars mavzusi/uyga vazifa). Admin jurnali ham,
/// o'qituvchi ilovasi ham shu yagona mantiqdan foydalanadi (faqat ruxsat tekshiruvi farq qiladi).
/// </summary>
public static class JournalService
{
    /// <summary>
    /// Fanning darslari (sana + dars raqami). Dars jadvali olib tashlandi — ustunlar endi QO'LDA
    /// qo'shilgan darslardan (LessonNote) keladi: o'qituvchi/admin dars qo'shsa yoki birinchi baho/davomat
    /// kiritilsa LessonNote yaratiladi (Conducted=true) va ustun paydo bo'ladi. Bir kunda bir fan bir
    /// necha marta bo'lishi mumkin (Period — oddiy tartib raqami).
    /// </summary>
    public static async Task<List<JournalColumnDto>> ComputeColumnsAsync(
        IAppDbContext db, string classId, string subjectId, int quarter) =>
        await db.LessonNotes
            .Where(n => n.ClassId == classId && n.SubjectId == subjectId && n.Quarter == quarter)
            .OrderBy(n => n.Date).ThenBy(n => n.Period)
            .Select(n => new JournalColumnDto(n.Date, n.Period))
            .ToListAsync();

    /// <summary>Chiqqan a'zolik shu oy jurnalida ko'rinadimi: chiqish oyi shu oy yoki keyin VA shu oyga
    /// qadar guruhda boshlagan bo'lsa (o'qimagan oylarida ko'rinmaydi).</summary>
    private static bool EndedVisibleIn(StudentGroup m, string month)
    {
        if (m.IsActive) return true;
        var end = EndOf(m);
        if (end.Length < 7 || string.CompareOrdinal(end[..7], month) < 0) return false;
        var start = MemberStart(m) ?? m.JoinedAt;
        return start is not { Length: >= 7 } || string.CompareOrdinal(start[..7], month) <= 0;
    }

    /// <summary>Chiqqan a'zolikning oxirgi kuni: muzlatilgan bo'lib chiqqan bo'lsa muzlatish sanasi (undan
    /// keyin o'qimagan), aks holda chiqish sanasi.</summary>
    private static string EndOf(StudentGroup m)
    {
        var left = m.LeftAt ?? "";
        var frozen = m.FrozenAt ?? "";
        if (frozen.Length >= 10 && (left.Length < 10 || string.CompareOrdinal(frozen, left) < 0)) return frozen[..10];
        return left.Length >= 10 ? left[..10] : left;
    }

    /// <summary>
    /// Guruhning bitta OYLIK jurnali. Dars jadvali yo'q — ustunlar guruh <see cref="Group.Days"/> (hafta
    /// kunlari) bo'yicha SHU OYDAGI sanalardan avtomatik quriladi (Period=1). Qatorlar — a'zolar (+ shu oyda o'qigan, keyin chiqqanlar — faqat ko'rish).
    /// Mavjud oylar: guruh boshlanish sanasidan (yoki eng erta a'zolik) joriy oygacha. Fan = guruh kursi.
    /// </summary>
    public static async Task<GroupJournalDto?> GroupMonthAsync(IAppDbContext db, string classId, string? month)
    {
        var cls = await db.Classes.FindAsync(classId);
        if (cls is null) return null;
        var subjectId = cls.CourseId ?? "";
        var courseName = string.IsNullOrEmpty(subjectId) ? "" : (await db.Subjects.FindAsync(subjectId))?.Name ?? "";
        var teacherName = string.IsNullOrEmpty(cls.TeacherId) ? "" : (await db.Teachers.FindAsync(cls.TeacherId))?.FullName ?? "";

        // Faol a'zolar (IsActive, status: trial/active/frozen) + GURUHDAN CHIQQANLAR — faqat o'qigan
        // oylarida (chiqish oyigacha), muzlatilganlar kabi FAQAT KO'RISH qatorida (pastda `shown`).
        // ⚠️ Ilgari faqat `IsActive` olinardi: chiqarilgan/boshqa guruhga o'tkazilgan o'quvchi o'qigan
        // oylaridan ham yo'qolar, sertifikat bilan tugatilgan guruh jurnali esa bo'm-bo'sh ochilardi.
        var allMemberships = await db.StudentGroups.Where(sg => sg.GroupId == classId).ToListAsync();

        // Mavjud oylar: guruh StartDate (yoki eng erta a'zolik JoinedAt) oyidan joriy oygacha.
        var starts = new List<string>();
        if (!string.IsNullOrEmpty(cls.StartDate) && cls.StartDate.Length >= 7) starts.Add(cls.StartDate[..7]);
        foreach (var m in allMemberships)
            if (!string.IsNullOrEmpty(m.JoinedAt) && m.JoinedAt.Length >= 7) starts.Add(m.JoinedAt[..7]);
        var cur = TuitionService.CurrentMonth();
        // TUGATILGAN (arxivlangan) guruhda oylar ARXIV OYIDA to'xtaydi — aks holda o'qituvchi profilidan
        // ochilgan tugatilgan guruh jurnali bo'm-bo'sh joriy oyda ochilardi (oxirgi haqiqiy oy ko'rinsin).
        if (cls.IsArchived && cls.ArchivedAt is { Length: >= 7 } archMonth
            && string.CompareOrdinal(archMonth[..7], cur) < 0)
            cur = archMonth[..7];
        var from = starts.Count > 0 ? starts.Min()! : cur;
        if (string.CompareOrdinal(from, cur) > 0) from = cur;
        var months = TuitionService.MonthRange(from, cur).ToList();
        if (months.Count == 0) months.Add(cur);
        var resolved = !string.IsNullOrEmpty(month) && months.Contains(month) ? month! : months[^1];

        // ARXIVLANGAN o'quvchi (eski yozuvlarda a'zoligi hali TIRIK bo'lishi mumkin) JORIY oy
        // jurnalida ko'rinmaydi: aks holda o'qituvchi uni har darsda belgilashga majbur bo'lar,
        // tezkor davomat "hammasi belgilanmaguncha saqlanmaydi" qoidasi bilan to'xtab qolar va ota-onaga
        // "darsga kelmadi" ketardi. O'TGAN oylarda — o'qigan joyida ko'rinadi (tarix yo'qolmasin).
        var curMonthNow = TuitionService.CurrentMonth();
        var hideArchivedLive = string.CompareOrdinal(resolved, curMonthNow) >= 0;
        var archivedIds = !hideArchivedLive ? new HashSet<string>()
            : (await db.Students.AsNoTracking()
                .Where(s => s.IsArchived && db.StudentGroups.Any(sg => sg.GroupId == classId && sg.IsActive && sg.StudentId == s.Id))
                .Select(s => s.Id).ToListAsync()).ToHashSet();
        var shown = allMemberships
            .Where(m => m.IsActive ? !archivedIds.Contains(m.StudentId) : EndedVisibleIn(m, resolved))
            .ToList();
        var ids = shown.Select(m => m.StudentId).ToList();
        var studentById = (await db.Students.Where(s => ids.Contains(s.Id)).ToListAsync())
            .ToDictionary(s => s.Id);
        // Balans — SHU GURUH bo'yicha (umumiy Student.Balance emas): o'quvchi bir nechta guruhda o'qib
        // faqat bittasiga to'lasa, to'lagan guruhida "qarzi yo'q", to'lamaganida qarzdor ko'rinadi.
        // Balans bilan birga QARZDOR OYLAR soni ham keladi (2+ oy → jurnalda alohida rang).
        var balanceByStudent = await GroupBalanceService.DetailedForGroupAsync(db, classId, ids);
        // TO'LOV "DARVOZASI": to'lamagan o'quvchi O'QITUVCHI jurnalida ko'rinmasin (muzlatish EMAS —
        // hisob-kitob davom etadi). Bu yerda faqat BAYROQ qo'yiladi: admin jurnali hammani ko'radi,
        // o'qituvchi controlleri esa (TeacherPortalController) bayroqli qatorlarni javobdan olib tashlaydi.
        // DIQQAT: darvoza BUGUNGI holatga qarab ishlaydi (ko'rilayotgan `resolved` oyga emas).
        var policy = await JournalPolicy.GetAsync(db);
        var dayNow = AppClock.Today.Day;
        var students = shown
            .Where(m => studentById.ContainsKey(m.StudentId))
            .Select(m =>
            {
                var st = studentById[m.StudentId];
                var bal = balanceByStudent.GetValueOrDefault(m.StudentId);
                var (payHidden, payReason) = JournalPolicy.PaymentGate(policy, bal, curMonthNow, dayNow);
                // Chiqqan a'zolik — klient uni muzlatilganlar blokida (faqat ko'rish, chiqishdan keyingi
                // darslar avto-"keldi" emas) ko'rsatadi: holat "frozen", tugash sanasi — chiqish sanasi.
                var ended = m.IsActive ? "" : m.Status == "completed" ? "completed" : "left";
                return new GroupJournalStudentDto(
                    m.StudentId, st.FullName, ended.Length > 0 ? "frozen" : m.Status ?? "trial", m.ActivatedAt ?? "",
                    bal.Balance,
                    // ⚠️ `RecordedAt` KUN sifatida solishtiriladi (klientda `autoPresent`), shuning
                    // uchun 10 belgigacha qirqiladi: ko'chirilgan ma'lumotda u to'liq ISO vaqt
                    // ("2026-09-18T17:39:16Z") bo'lib tushgan va "2026-09-18" >= o'sha satr FALSE
                    // chiqib, o'sha kungi dars hech qachon avtomatik "keldi" bo'lmasdi.
                    MemberStart(m) ?? "", DayOnly(m.RecordedAt), ended.Length > 0 ? EndOf(m) : m.FrozenAt ?? "", bal.DebtMonths,
                    payHidden, payReason, st.BirthCertificateUrl ?? "", ended, Debtor: bal.Balance < 0);
            })
            .OrderBy(s => s.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();


        // Bir martalik ko'chirishlar (dars boshqa kunga): asl kun olib tashlanadi, yangi kun qo'shiladi.
        var moves = await db.LessonReschedules.Where(r => r.ClassId == classId).ToListAsync();
        // Ustunlar: shu oyda guruh kunlariga to'g'ri keladigan sanalar (+ ko'chirishlar), Period=1.
        var columns = EffectiveLessonDatesInMonth(
                cls.Days, resolved, moves.Select(m => new LessonMove(m.FromDate, m.ToDate)))
            .Select(d => new JournalColumnDto(d, 1)).ToList();
        // Shu oyga tegishli ko'chirishlar (yangi yoki asl sana shu oyda) — frontend belgi + bekor qilish uchun.
        var reschedules = moves
            .Where(m => (m.ToDate.Length >= 7 && m.ToDate[..7] == resolved)
                        || (m.FromDate.Length >= 7 && m.FromDate[..7] == resolved))
            .Select(m => new LessonRescheduleDto(m.Id, m.FromDate, m.ToDate, m.Time))
            .ToList();

        // Yozuvlar: shu guruh + kurs + oy (sana prefiksi bo'yicha).
        var entries = string.IsNullOrEmpty(subjectId)
            ? new List<JournalEntryDto>()
            : await db.JournalEntries
                .Where(e => e.ClassId == classId && e.SubjectId == subjectId && e.Date.StartsWith(resolved))
                .Select(e => new JournalEntryDto(e.StudentId, e.Date, e.Period, e.Grade, e.ReasonId, e.Homework, e.Behavior, e.Mastery, e.Present, e.GradeFromCriteria))
                .ToListAsync();

        // "O'tildi" deb belgilangan dars sanalari — sababsiz o'quvchi shu kunda KELDI (yashil) deb ko'rsatiladi.
        var conductedDates = string.IsNullOrEmpty(subjectId)
            ? new List<string>()
            : await db.LessonNotes
                .Where(n => n.ClassId == classId && n.SubjectId == subjectId && n.Conducted && n.Date.StartsWith(resolved))
                .Select(n => n.Date).Distinct().ToListAsync();

        var info = new GroupJournalInfoDto(
            cls.Id, cls.Name, subjectId, courseName, teacherName,
            cls.Days, cls.StartTime, cls.EndTime, cls.Room ?? "", cls.StartDate ?? "", cls.MonthlyFee);
        return new GroupJournalDto(info, months, resolved, columns, students, entries, conductedDates, reschedules);
    }

    /// <summary>O'quvchining guruhdagi a'zoligi boshlangan sana ("yyyy-MM-dd"): aktivlashtirilgan bo'lsa
    /// ActivatedAt, aks holda JoinedAt. Noma'lum/formatsiz bo'lsa null (cheklov qo'llanmaydi). Undan
    /// oldingi darslarga davomat/baho (jurnal ham, baholash mezonlari ham) kiritib bo'lmaydi.</summary>
    /// <summary>ISO sana/vaqtdan faqat KUN ("yyyy-MM-dd"). Bo'sh/qisqa bo'lsa o'zgarmaydi.</summary>
    private static string DayOnly(string? iso) => iso is { Length: >= 10 } ? iso[..10] : iso ?? "";

    public static string? MemberStart(StudentGroup? m)
    {
        if (m is null) return null;
        // ENG ERTA aktivlashtirish — yopilgan davrlar ham. Joriy ActivatedAt ga qarasak, muzlatib
        // qayta aktivlashtirilgan o'quvchida O'TGAN oylardagi (allaqachon kiritilgan) davomat va
        // baholar "a'zolikdan oldingi" deb bloklangan ko'rinardi.
        var first = MembershipLifecycle.FirstActivatedAt(m);
        if (first.Length >= 10) return first[..10];
        if (!string.IsNullOrEmpty(m.JoinedAt) && m.JoinedAt.Length >= 10) return m.JoinedAt[..10];
        return null;
    }

    /// <summary>"yyyy-MM" oyidagi, berilgan hafta kunlariga (0=Du..6=Yak) to'g'ri keladigan sanalar ("yyyy-MM-dd").</summary>
    public static IEnumerable<string> LessonDatesInMonth(IReadOnlyCollection<int> days, string month)
    {
        if (days.Count == 0 || month.Length < 7) yield break;
        var y = int.Parse(month[..4]);
        var m = int.Parse(month[5..7]);
        var set = days.ToHashSet();
        for (var day = 1; day <= DateTime.DaysInMonth(y, m); day++)
        {
            var d = new DateOnly(y, m, day);
            if (set.Contains(((int)d.DayOfWeek + 6) % 7)) yield return d.ToString("yyyy-MM-dd");
        }
    }

    /// <summary>Bitta darsning bir martalik ko'chirilishi (asl sana → yangi sana).</summary>
    public sealed record LessonMove(string FromDate, string ToDate);

    /// <summary>
    /// Shu oyning AMALDAGI dars sanalari: guruh kunlaridan (<see cref="LessonDatesInMonth"/>) chiqarilgan
    /// sanalar + bir martalik ko'chirishlar qo'llanadi — ko'chirilgan darsning ASL sanasi (shu oyda bo'lsa)
    /// olib tashlanadi, YANGI sanasi (shu oyda bo'lsa) qo'shiladi (guruh kuni bo'lmasa ham). Tartiblangan.
    /// </summary>
    public static List<string> EffectiveLessonDatesInMonth(
        IReadOnlyCollection<int> days, string month, IEnumerable<LessonMove> moves)
    {
        var set = LessonDatesInMonth(days, month).ToHashSet();
        foreach (var mv in moves)
        {
            if (mv.FromDate.Length >= 7 && mv.FromDate[..7] == month) set.Remove(mv.FromDate);
            if (mv.ToDate.Length >= 7 && mv.ToDate[..7] == month) set.Add(mv.ToDate);
        }
        return set.OrderBy(d => d, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Bitta darsni BIR MARTALIK boshqa kunga ko'chiradi (<paramref name="fromDate"/> → <paramref name="toDate"/>).
    /// Asl kundagi jurnal ma'lumotlari (baho/davomat/mavzu/MEZON belgilari) yangi kunga KO'CHIRILADI. Avval ko'chirilgan
    /// dars yana ko'chirilsa — zanjir yig'iladi (asl kun → yangi kun). Xatolarni InvalidOperationException bilan qaytaradi.
    ///
    /// <para>⚠️ Ko'chirish oyning dars sonini (maosh maxraji) va JURNAL JARIMASINI o'zgartiradi
    /// (<see cref="SalaryJournalStats"/>: asl kun rejadan chiqadi). Shuning uchun u jurnal
    /// siyosatiga bo'ysunadi (<see cref="CheckMoveAsync"/>): ilgari o'qituvchi dushanbadagi
    /// belgilamagan darsini keyingi haftaga ko'chirib, jarimadan jimgina qutulardi.</para>
    /// <para><paramref name="isAdmin"/> — admin jurnali (siyosat faqat "Adminlarga ham qo'llash"
    /// yoqilganda; maosh muhlati va "o'tgan darsni kelajakka" taqig'i adminga QO'LLANMAYDI —
    /// kechikib rasmiylashtirilgan "qo'shimcha dars" admin qarori, u tarixga yoziladi).</para>
    /// </summary>
    /// <param name="forceConflict">Save even when the target room/teacher is busy (<see cref="RescheduleConflicts"/>).
    /// ONLY the admin journal endpoint passes <c>true</c>, and only for a superadmin with a reason
    /// (<paramref name="forceReason"/>, audited); the teacher endpoint never forces.</param>
    public static async Task<LessonReschedule> RescheduleLessonAsync(
        IAppDbContext db, string classId, string fromDate, string toDate, string? time, string? actor,
        bool isAdmin, AuditService? audit = null, bool forceConflict = false, string? forceReason = null)
    {
        var cls = await db.Classes.FindAsync(classId)
            ?? throw new InvalidOperationException("Guruh topilmadi");
        if (!DateOnly.TryParse(fromDate, out var fd) || !DateOnly.TryParse(toDate, out var td))
            throw new InvalidOperationException("Sana noto'g'ri");
        fromDate = fd.ToString("yyyy-MM-dd");
        toDate = td.ToString("yyyy-MM-dd");
        if (fromDate == toDate)
            throw new InvalidOperationException("Yangi sana asl sanadan farq qilishi kerak");
        if (!string.IsNullOrEmpty(cls.StartDate) && string.CompareOrdinal(toDate, cls.StartDate[..Math.Min(10, cls.StartDate.Length)]) < 0)
            throw new InvalidOperationException("Yangi sana guruh boshlanishidan oldin bo'lishi mumkin emas");

        var moves = await db.LessonReschedules.Where(r => r.ClassId == classId).ToListAsync();
        var moveRecords = moves.Select(m => new LessonMove(m.FromDate, m.ToDate)).ToList();

        // Asl kunda haqiqatan dars bo'lishi kerak (guruh kuni yoki oldin ko'chirilgan kun).
        var fromEffective = EffectiveLessonDatesInMonth(cls.Days, fromDate[..7], moveRecords);
        if (!fromEffective.Contains(fromDate))
            throw new InvalidOperationException("Bu kunda dars yo'q");
        // Yangi kunda allaqachon dars bo'lmasin (ikki dars bir kunga tushmasin).
        var toEffective = EffectiveLessonDatesInMonth(cls.Days, toDate[..7], moveRecords);
        if (toEffective.Contains(toDate))
            throw new InvalidOperationException("Bu kunda allaqachon dars bor");

        var deny = await CheckMoveAsync(db, cls, fromDate, toDate, isAdmin);
        if (deny is not null) throw new InvalidOperationException(deny);

        // DOUBLE BOOKING (CRM Phase 5): the room / the teacher must be free at the new date and time
        // (given time or the group's start; same duration). Hard refusal; only a forced admin save passes.
        var clash = await RescheduleConflicts.FindAsync(db, cls, toDate, time);
        if (clash is not null)
        {
            if (!forceConflict)
                throw new ScheduleConflictException(ScheduleConflicts.LessonClashMessage(clash), clash);
            audit?.Record(AuditService.EntityGroup, classId, "update",
                $"Jadval to'qnashuvi bilan saqlandi: «{cls.Name}» darsi {UzDay(toDate)} ga ko'chirildi, " +
                $"band: «{clash.GroupName}» {clash.Start}–{clash.End} — sabab: {(forceReason ?? "").Trim()}");
        }

        // Zanjirni yig'ish: agar shu darsning o'zi allaqachon ko'chirilgan bo'lsa (ToDate == fromDate) — o'sha
        // yozuvni yangilaymiz (asl kun saqlanadi). Aks holda yangi yozuv.
        var existing = moves.FirstOrDefault(m => m.ToDate == fromDate)
                       ?? moves.FirstOrDefault(m => m.FromDate == fromDate);
        LessonReschedule rec;
        if (existing is not null)
        {
            existing.ToDate = toDate;
            existing.Time = time;
            existing.CreatedBy = actor;
            existing.CreatedAt = DateTime.UtcNow;
            rec = existing;
        }
        else
        {
            rec = new LessonReschedule
            {
                ClassId = classId, FromDate = fromDate, ToDate = toDate, Time = time, CreatedBy = actor,
            };
            db.LessonReschedules.Add(rec);
        }

        // Mavjud jurnal ma'lumotlarini asl kundan yangi kunga ko'chiramiz (yo'qolmasin).
        await MoveJournalDataAsync(db, classId, fromDate, toDate);
        audit?.Record(AuditService.EntityGroup, classId, "update",
            $"Dars ko'chirildi: «{cls.Name}» guruhi — {UzDay(fromDate)} → {UzDay(toDate)}"
            + (string.IsNullOrWhiteSpace(time) ? "" : $" (soat {time})"),
            before: new { Dars = fromDate }, after: new { Dars = toDate, Vaqt = time });
        await db.SaveChangesAsync();
        return rec;
    }

    /// <summary>Ko'chirishni bekor qiladi — dars asl kuniga qaytadi (ma'lumotlar ham qaytariladi).
    /// Ko'chirish bilan BIR XIL siyosat (<see cref="CheckMoveAsync"/>): bu ham darsni boshqa kunga olib o'tadi.</summary>
    public static async Task CancelRescheduleAsync(IAppDbContext db, string id, bool isAdmin, AuditService? audit = null)
    {
        var rec = await db.LessonReschedules.FirstOrDefaultAsync(r => r.Id == id);
        if (rec is null) return;
        var cls = await db.Classes.FindAsync(rec.ClassId);
        if (cls is not null)
        {
            var deny = await CheckMoveAsync(db, cls, rec.ToDate, rec.FromDate, isAdmin);
            if (deny is not null) throw new InvalidOperationException(deny);
        }
        await MoveJournalDataAsync(db, rec.ClassId, rec.ToDate, rec.FromDate);
        db.LessonReschedules.Remove(rec);
        audit?.Record(AuditService.EntityGroup, rec.ClassId, "update",
            $"Dars ko'chirish bekor qilindi: «{cls?.Name}» guruhi — dars {UzDay(rec.ToDate)} dan asl kuniga ({UzDay(rec.FromDate)}) qaytdi",
            before: new { Dars = rec.ToDate }, after: new { Dars = rec.FromDate });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Darsni <paramref name="from"/> kunidan <paramref name="to"/> kuniga olib o'tish MUMKINMI.
    /// null = ruxsat; aks holda foydalanuvchiga ko'rsatiladigan sabab.
    /// <list type="number">
    /// <item><b>O'rinbosar</b> qamragan kun ko'chirilmaydi (hech kimga): tayinlov o'sha SANAGA
    ///   bog'langan — dars ketsa, o'rinbosarning haqi va kirish huquqi dars yo'q kunda qolib ketardi.</item>
    /// <item><b>Jurnal siyosati</b> (<see cref="JournalPolicy.CheckAsync"/>) — ikkala sana ham (o'tgan
    ///   bo'lsa): yopiq davrdagi darsni ko'chirish ham o'sha davrni tahrirlash.</item>
    /// <item>O'qituvchi uchun: o'tgan darsni KELAJAKKA ko'chirib bo'lmaydi va maosh muhlati
    ///   (<see cref="CenterMeta.SalaryGraceDays"/>) o'tgan darsni ham — aks holda belgilanmagan dars
    ///   jarimasi (<see cref="SalaryJournalStats"/>) jimgina yo'qolardi.</item>
    /// </list>
    /// </summary>
    public static async Task<string?> CheckMoveAsync(IAppDbContext db, Group cls, string from, string to, bool isAdmin)
    {
        var subs = await db.SubstituteTeacherAssignments.AsNoTracking()
            .Where(a => a.GroupId == cls.Id && a.IsActive).ToListAsync();
        if (subs.Any(a => SubstituteTeacherService.CoversDate(a, from)))
            return "Bu kunga o'rinbosar o'qituvchi biriktirilgan — avval o'rinbosar tayinlovini o'zgartiring (O'qituvchilar → O'rinbosarlar), so'ng darsni ko'chiring";

        var today = AppClock.Today.ToString("yyyy-MM-dd");
        var subject = cls.CourseId ?? "";
        foreach (var d in new[] { from, to })
        {
            if (string.CompareOrdinal(d, today) >= 0) continue;
            var deny = await JournalPolicy.CheckAsync(db, cls.Id, subject, d, 1, isAdmin, skipConducted: true);
            if (deny is not null) return deny;
        }
        if (isAdmin) return null;

        if (string.CompareOrdinal(from, today) < 0 && string.CompareOrdinal(to, today) > 0)
            return "O'tgan darsni kelajakka ko'chirib bo'lmaydi — o'tkazib yuborilgan darsni qayta o'tish kerak bo'lsa admin bilan bog'laning";

        var p = await JournalPolicy.GetAsync(db);
        if (p.SalaryRequireJournal && string.CompareOrdinal(from, today) < 0)
        {
            var cutoff = AppClock.Today.AddDays(-Math.Max(0, p.SalaryGraceDays)).ToString("yyyy-MM-dd");
            if (string.CompareOrdinal(from, cutoff) <= 0)
                return "Bu darsni belgilash muhlati o'tgan (maosh hisobida yopilgan) — ko'chirish uchun admin bilan bog'laning";
        }
        return null;
    }

    /// <summary>"2026-10-07" → "7-oktabr" (audit matni uchun).</summary>
    private static string UzDay(string iso) =>
        iso is { Length: >= 10 } && int.TryParse(iso[5..7], out var mm) && int.TryParse(iso[8..10], out var dd)
            ? $"{dd}-{MessageTokenizer.MonthNameUz(mm)}"
            : iso;

    /// <summary>Guruhning bir kundagi jurnal yozuvlarini (JournalEntry + LessonNote + baholash MEZONI belgilari)
    /// boshqa kunga ko'chiradi. ⚠️ Ilgari mezon belgilari (<see cref="CriterionGrade"/>) eski kunda qolib
    /// ketardi — baholash taxtasida dars yo'q kunda osilib turar, yangi kunda esa bo'sh ko'rinardi.</summary>
    private static async Task MoveJournalDataAsync(IAppDbContext db, string classId, string fromDate, string toDate)
    {
        var entries = await db.JournalEntries.Where(e => e.ClassId == classId && e.Date == fromDate).ToListAsync();
        foreach (var e in entries) e.Date = toDate;
        var notes = await db.LessonNotes.Where(n => n.ClassId == classId && n.Date == fromDate).ToListAsync();
        foreach (var n in notes) n.Date = toDate;

        // Mezon belgilari: (guruh, o'quvchi, mezon, sana) UNIKAL — yangi kunda xuddi shunday qator
        // bo'lsa (eski qoldiq) ikkalasi birlashtiriladi, aks holda sana shunchaki almashadi.
        var grades = await db.CriterionGrades.Where(g => g.GroupId == classId && g.Date == fromDate).ToListAsync();
        if (grades.Count > 0)
        {
            var atTarget = (await db.CriterionGrades.Where(g => g.GroupId == classId && g.Date == toDate).ToListAsync())
                .ToDictionary(g => (g.StudentId, g.CriterionId));
            foreach (var g in grades)
            {
                if (atTarget.TryGetValue((g.StudentId, g.CriterionId), out var dst))
                {
                    dst.Done = dst.Done || g.Done;
                    db.CriterionGrades.Remove(g);
                }
                else g.Date = toDate;
            }
        }
    }

    public static async Task<List<JournalEntryDto>> GetEntriesAsync(
        IAppDbContext db, string classId, string subjectId, int quarter) =>
        await db.JournalEntries
            .Where(e => e.ClassId == classId && e.SubjectId == subjectId && e.Quarter == quarter)
            .Select(e => new JournalEntryDto(e.StudentId, e.Date, e.Period, e.Grade, e.ReasonId, e.Homework, e.Behavior, e.Mastery, e.Present, e.GradeFromCriteria))
            .ToListAsync();

    // ---------- A'ZOLIK DARVOZASI (kimga yozish mumkin) ----------

    public const string NotMemberMessage = "O'quvchi bu guruhning a'zosi emas — jurnalga yozib bo'lmaydi";
    public const string NotActiveMemberMessage =
        "O'quvchi guruhda faol emas (muzlatilgan yoki chiqqan) — jurnalga yozib bo'lmaydi";
    public const string ArchivedStudentMessage = "O'quvchi arxivlangan — jurnalga yozib bo'lmaydi";
    public const string OutsideMembershipMessage =
        "O'quvchi bu sanada guruhda bo'lmagan (muzlatilgan yoki chiqqan) — jurnalga yozib bo'lmaydi";
    public const string BeforeMemberStartMessage =
        "O'quvchi guruhga qo'shilgan/aktivlashtirilgan sanadan oldingi darsga ma'lumot kiritib bo'lmaydi";

    /// <summary>
    /// Shu o'quvchiga shu SANADAGI darsga jurnalda yozish mumkinmi (sof funksiya, testlangan).
    /// null = mumkin, aks holda sabab.
    /// <list type="bullet">
    /// <item><b>a'zolik yo'q</b> — hech kim yoza olmaydi (ilgari "a'zolik topilmadi" = "cheklov yo'q"
    ///   deb talqin qilinardi va API orqali begona o'quvchiga baho/davomat qo'yib bo'lardi);</item>
    /// <item>a'zolik boshlanishidan (<see cref="MemberStart"/>) oldingi dars — hech kim;</item>
    /// <item><b>o'qituvchi</b> (<paramref name="teacher"/>) — faqat FAOL, muzlatilmagan, arxivlanmagan
    ///   a'zo (u jurnalida faqat shularni ko'radi);</item>
    /// <item><b>admin</b> — muzlatilgan/chiqqan o'quvchini ham tuzata oladi, lekin faqat u guruhda
    ///   BO'LGAN kunga (<see cref="StudentJournalBuilder.BoundsOf"/>: muzlatish/chiqish sanasigacha).
    ///   Tugash sanasi noma'lum muzlatilgan/yopiq a'zolik — rad etiladi (qamrovni tekshirib bo'lmaydi).</item>
    /// </list>
    /// </summary>
    public static string? MemberWriteError(StudentGroup? m, bool studentArchived, string date, bool teacher)
    {
        if (m is null) return NotMemberMessage;
        var start = MemberStart(m);
        if (start is not null && string.CompareOrdinal(date, start) < 0) return BeforeMemberStartMessage;
        var frozen = string.Equals(m.Status, "frozen", StringComparison.Ordinal);
        if (teacher)
        {
            if (!m.IsActive || frozen) return NotActiveMemberMessage;
            if (studentArchived) return ArchivedStudentMessage;
            return null;
        }
        if (m.IsActive && !frozen) return null;
        // Muzlatilgan/chiqqan: faqat tugash sanasigacha (shu kun ham). Sana noma'lum — qamrovni bilib bo'lmaydi.
        var end = StudentJournalBuilder.BoundsOf(m).MemberEnd;
        if (end is null || string.CompareOrdinal(date, end) > 0) return OutsideMembershipMessage;
        return null;
    }

    /// <summary>O'quvchining shu guruhdagi a'zoligi (bir nechta qator bo'lsa — FAOLI birinchi).</summary>
    private static StudentGroup? PickMembership(IEnumerable<StudentGroup> rows) =>
        rows.OrderByDescending(sg => sg.IsActive).ThenByDescending(sg => sg.LeftAt ?? "").FirstOrDefault();

    /// <summary><see cref="MemberWriteError"/> ning bazadan o'qiydigan shakli (bitta o'quvchi).</summary>
    public static async Task<string?> MemberWriteErrorAsync(
        IAppDbContext db, string classId, string studentId, string date, bool teacher)
    {
        var m = PickMembership(await db.StudentGroups.AsNoTracking()
            .Where(sg => sg.GroupId == classId && sg.StudentId == studentId).ToListAsync());
        var archived = await db.Students.AsNoTracking().AnyAsync(s => s.Id == studentId && s.IsArchived);
        return MemberWriteError(m, archived, date, teacher);
    }

    /// <summary>
    /// Bitta katakni belgilaydi (baho/davomat). Qaytaradi: bu YANGI "darsga kelmadi" hodisasimi
    /// (<see cref="AttendanceNotify.IsNewAbsence"/>: kech bo'lmagan sabab qo'yildi VA oldin "kelmadi"
    /// emas edi) — chaqiruvchi faqat shunda <c>attendance_absent</c> avto-xabarini yuboradi.
    /// <para><paramref name="teacher"/> — o'qituvchi yo'li: faqat faol, muzlatilmagan a'zoga
    /// (<see cref="MemberWriteError"/>).</para>
    /// </summary>
    public static async Task<bool> SetEntryAsync(
        IAppDbContext db, SetJournalEntryRequest req, AutoMessageService? autoMsg = null,
        bool teacher = false)
    {
        // Guruhning StartDate'i tekshirish — undan oldin dars bo'lmaydi.
        var cls = await db.Classes.FindAsync(req.ClassId);
        if (cls is null)
            throw new InvalidOperationException("Guruh topilmadi");
        if (!string.IsNullOrEmpty(cls.StartDate) && string.Compare(req.Date, cls.StartDate) < 0)
            throw new InvalidOperationException("Sana guruh yaratilishidan oldin");

        // Tutkun: kelasi kunlarga baho/davomat kirita olmassin.
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        if (string.Compare(req.Date, today) > 0)
            throw new InvalidOperationException("Kelasi kunlarga o'quvchi ma'lumoti kirita olmassin");

        // A'ZOLIK: faqat shu guruh a'zosiga va u guruhda bo'lgan kunga (a'zolik boshlanishidan oldingi
        // darsga ham emas). Qoida — MemberWriteError (o'qituvchi uchun qat'iyroq).
        var memberError = await MemberWriteErrorAsync(db, req.ClassId, req.StudentId, req.Date, teacher);
        if (memberError is not null)
            throw new InvalidOperationException(memberError);

        var entry = await db.JournalEntries.FirstOrDefaultAsync(e =>
            e.ClassId == req.ClassId && e.SubjectId == req.SubjectId && e.Quarter == req.Quarter &&
            e.StudentId == req.StudentId && e.Date == req.Date && e.Period == req.Period);
        // Bildirishnoma uchun — yangi/o'zgargan baho yoki sababnigina xabar qilamiz.
        var oldGrade = entry?.Grade;
        var oldReason = entry?.ReasonId;

        var student = await db.Students.FindAsync(req.StudentId);

        if (entry is null)
        {
            entry = new JournalEntry
            {
                ClassId = req.ClassId,
                SubjectId = req.SubjectId,
                Quarter = req.Quarter,
                StudentId = req.StudentId,
                Date = req.Date,
                Period = req.Period,
            };
            db.JournalEntries.Add(entry);
        }
        // BAHO EGASI: qo'lda O'ZGARTIRILGAN baho mezon sinxronining mulki bo'lishdan chiqadi (keyin mezon
        // belgilari unga tegmaydi). Baho o'zgarmagan bo'lsa (masalan katakda faqat uy vazifa tahrirlandi)
        // egasi o'zgarmaydi — aks holda mezon bahosi "qo'lda" bo'lib, ballda IKKI marta sanalardi.
        if (req.Grade != entry.Grade || req.Grade is null) entry.GradeFromCriteria = false;
        entry.Grade = req.Grade;
        entry.ReasonId = req.ReasonId;
        // "Keldi" va "kelmadi (sabab)" bir vaqtda bo'lmaydi — sabab tanlangan bo'lsa Present o'chadi.
        entry.Present = req.Present && req.ReasonId is null;
        entry.Homework = req.Homework;
        entry.Behavior = req.Behavior;
        entry.Mastery = req.Mastery;

        // Baho/davomat/uyga vazifa/xulq/o'zlashtirish kiritilsa — shu darsni "o'tildi" deb avtomatik belgilaymiz.
        if (req.Grade.HasValue || req.ReasonId is not null || entry.Present || req.Homework != 0 || req.Behavior != 0 || req.Mastery.HasValue)
        {
            var note = await db.LessonNotes.FirstOrDefaultAsync(n =>
                n.ClassId == req.ClassId && n.SubjectId == req.SubjectId &&
                n.Quarter == req.Quarter && n.Date == req.Date && n.Period == req.Period);
            if (note is null)
                db.LessonNotes.Add(new LessonNote
                {
                    ClassId = req.ClassId,
                    SubjectId = req.SubjectId,
                    Quarter = req.Quarter,
                    Date = req.Date,
                    Period = req.Period,
                    Conducted = true,
                });
            else if (!note.Conducted)
                note.Conducted = true;
        }

        await db.SaveChangesAsync();

        // YANGI "kelmadi" hodisasimi: "kech" — kelmadi EMAS; sababni almashtirish — yangi hodisa EMAS.
        var newAbsence = req.ReasonId is not null
            && AttendanceNotify.IsNewAbsence(oldReason, req.ReasonId, await AttendanceNotify.LateReasonIdsAsync(db));

        // Avtomatik bildirishnoma: farzandi baho olsa yoki davomatda belgilansa — oila kabinetiga xabar.
        await NotifyEntryAsync(db, autoMsg, req, student, cls, oldGrade, newAbsence);

        return newAbsence;
    }

    /// <summary>Baho/davomat yozuvi o'zgarganda oila kabinetiga bildirishnoma yozadi.
    /// BAHO: agar "grade_entered" avto-xabar qoidasi MAVJUD bo'lsa (yoqilgan-o'chirilganidan qat'i nazar) —
    /// yuborish to'liq <see cref="AutoMessageService"/>ga o'tadi (qoida o'chiq bo'lsa hech narsa yuborilmaydi);
    /// qoida UMUMAN yo'q bo'lsa — standart kabinet bildirishnomasi yoziladi (default-on). (Push 2026-10 da
    /// olib tashlandi — ilgari shu yerda FCM ham yuborilardi.)
    /// <para>DAVOMAT — AYNAN shu naqsh: faqat YANGI "kelmadi" (<paramref name="newAbsence"/>; "kech" HECH
    /// QACHON) va faqat "attendance_absent" qoidasi UMUMAN yo'q bo'lsa. Qoida bor bo'lsa xabarni chaqiruvchi
    /// (controller) dispatcher orqali yuboradi — ilgari ikkalasi BIRGA ketar va ota-ona bitta hodisa uchun
    /// IKKI xabar olardi. Arxivlangan o'quvchiga hech narsa ketmaydi.</para></summary>
    private static async Task NotifyEntryAsync(
        IAppDbContext db, AutoMessageService? autoMsg, SetJournalEntryRequest req,
        Student? student, Group? group, int? oldGrade, bool newAbsence)
    {
        // Xabar KONTEKSTI — aynan shu dars guruhi ({guruh}, {oqituvchi}, {dars_*} tokenlari shundan).
        var groupName = group?.Name ?? "";
        if (student is null || student.IsArchived) return;
        var notifyGrade = req.Grade.HasValue && req.Grade != oldGrade;
        var notifyAbsence = !notifyGrade && newAbsence;
        if (!notifyGrade && !notifyAbsence) return;

        // Baho — yangi yagona avto-xabar tizimi orqali (qoida mavjud bo'lsa).
        if (notifyGrade && autoMsg is not null &&
            await db.AutoMessageRules.AnyAsync(r => r.Trigger == AutoMessageTriggers.GradeEntered))
        {
            var d = req.Date;
            var sana = d.Length >= 10 ? $"{d[8..10]}.{d[5..7]}.{d[..4]}" : d;
            await autoMsg.DispatchStudentAsync(db, AutoMessageTriggers.GradeEntered, student,
                new Dictionary<string, string>
                {
                    ["{baho}"] = req.Grade!.Value.ToString(),
                    ["{sana}"] = sana,
                    ["{guruh}"] = groupName,
                }, group: group);
            return;
        }

        // Davomat — "attendance_absent" qoidasi bor bo'lsa uni controller yuboradi (yuqoriga qarang).
        if (notifyAbsence &&
            await db.AutoMessageRules.AnyAsync(r => r.Trigger == AutoMessageTriggers.AttendanceAbsent))
            return;

        if (student.UserId is null) return;

        var subject = (await db.Subjects.FindAsync(req.SubjectId))?.Name ?? "";
        string title, body, type;
        if (notifyGrade)
        {
            title = "Yangi baho";
            body = $"{student.FullName}: {subject} fanidan {req.Grade} baho ({req.Date})";
            type = "grade";
        }
        else
        {
            var reason = (await db.AbsenceReasons.FindAsync(req.ReasonId!))?.Name ?? "Davomat";
            title = "Davomat";
            body = $"{student.FullName}: {subject} darsida — {reason} ({req.Date})";
            type = "attendance";
        }

        // Kabinet tarixiga yozamiz (o'quvchi/ota-ona kabinetidagi qo'ng'iroqcha).
        NotificationStore.Add(db, student.UserId, title, body, type);
        await db.SaveChangesAsync();
    }

    /// <summary>Ommaviy davomat natijasi.</summary>
    /// <param name="AbsentReasonId">Haqiqatan ishlatilgan sabab (Absent=true bo'lganda), aks holda null.</param>
    /// <param name="WrittenIds">Yozuv HAQIQATAN yozilgan o'quvchilar.</param>
    /// <param name="NewlyAbsentIds">Shu so'rovda YANGI "kelmadi" bo'lganlar — "darsga kelmadi" xabari
    /// FAQAT shularga (allaqachon kelmagan, chetlab o'tilgan, begona va "kech"lar KIRMAYDI).</param>
    /// <param name="SkippedIds">Chetlab o'tilganlar (a'zo emas / a'zolik boshqa sanada / o'qituvchiga yopiq).</param>
    public sealed record BulkAttendanceResult(
        string? AbsentReasonId, IReadOnlyList<string> WrittenIds,
        IReadOnlyList<string> NewlyAbsentIds, IReadOnlyList<string> SkippedIds);

    /// <summary>
    /// Bitta dars (sana+dars raqami) uchun BARCHA berilgan o'quvchiga birdan davomat. <c>Absent == false</c> →
    /// "hammasi keldi": mavjud davomat sabablari tozalanadi (baho saqlanadi). <c>Absent == true</c> →
    /// "hammasi kelmadi": har bir o'quvchiga shu sabab yoziladi. Ikkala holatda ham dars "o'tildi" (Conducted) bo'ladi.
    /// <para>Shu guruhga yozib bo'lmaydigan o'quvchilar (<see cref="MemberWriteError"/>: a'zo emas, a'zoligi
    /// keyin boshlangan, o'qituvchi yo'lida — muzlatilgan/chiqqan/arxivlangan) JIMGINA chetlab o'tiladi —
    /// butun amal to'xtamaydi va ularga xabar ham ketmaydi.</para>
    /// <para><paramref name="beforeSave"/> — natija tayyor bo'lgach, SaveChanges'dan OLDIN chaqiriladi:
    /// davomat xabarlari navbati (<see cref="AttendanceNotify.EnqueueBulkAsync"/>) jurnal yozuvi bilan
    /// BITTA tranzaksiyada saqlansin (jurnal yozildi = xabar navbatda; restart uni yo'qotmaydi).</para>
    /// </summary>
    public static async Task<BulkAttendanceResult> BulkAttendanceAsync(
        IAppDbContext db, BulkAttendanceRequest req, bool teacher = false,
        Func<BulkAttendanceResult, Task>? beforeSave = null)
    {
        const int quarter = 1;
        var empty = new BulkAttendanceResult(null, [], [], []);
        if (req.StudentIds.Count == 0) return empty;

        // Guruhning StartDate'i tekshirish — undan oldin dars bo'lmaydi.
        var cls = await db.Classes.FindAsync(req.ClassId);
        if (cls is null)
            throw new InvalidOperationException("Guruh topilmadi");
        if (!string.IsNullOrEmpty(cls.StartDate) && string.Compare(req.Date, cls.StartDate) < 0)
            throw new InvalidOperationException("Sana guruh yaratilishidan oldin");

        // Tutkun: kelasi kunlarga davomat kirita olmassin.
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        if (string.Compare(req.Date, today) > 0)
            throw new InvalidOperationException("Kelasi kunlarga o'quvchi ma'lumoti kirita olmassin");

        // A'ZOLIK DARVOZASI har o'quvchi uchun (MemberWriteError). Ilgari faqat SHU guruhning faol
        // a'zolari tekshirilar, lug'atda YO'Q id esa "cheklov yo'q" deb o'tib ketardi — so'rovdagi har
        // qanday id (boshqa guruh o'quvchisi) uchun yozuv yaratilib, ota-onasiga SMS ketardi.
        var requested = req.StudentIds.Distinct().ToList();
        var memberships = (await db.StudentGroups.AsNoTracking()
                .Where(sg => sg.GroupId == req.ClassId && requested.Contains(sg.StudentId))
                .ToListAsync())
            .GroupBy(sg => sg.StudentId)
            .ToDictionary(g => g.Key, g => PickMembership(g));
        var archived = (await db.Students.AsNoTracking()
                .Where(s => requested.Contains(s.Id) && s.IsArchived).Select(s => s.Id).ToListAsync())
            .ToHashSet();
        var studentIds = new List<string>();
        var skipped = new List<string>();
        foreach (var sid in requested)
        {
            var err = MemberWriteError(memberships.GetValueOrDefault(sid), archived.Contains(sid), req.Date, teacher);
            if (err is null) studentIds.Add(sid); else skipped.Add(sid);
        }
        if (studentIds.Count == 0) return empty with { SkippedIds = skipped };

        // Darsni "o'tildi" deb belgilash (LessonNote) + RASSMIY davomat olindi deb belgilash
        // (o'quvchi shaxsiy jurnalida standart "keldi" taxmini shu bayroqqa bog'liq).
        var note = await db.LessonNotes.FirstOrDefaultAsync(n =>
            n.ClassId == req.ClassId && n.SubjectId == req.SubjectId &&
            n.Quarter == quarter && n.Date == req.Date && n.Period == req.Period);
        if (note is null)
            db.LessonNotes.Add(new LessonNote
            {
                ClassId = req.ClassId, SubjectId = req.SubjectId, Quarter = quarter,
                Date = req.Date, Period = req.Period, Conducted = true, AttendanceTaken = true,
            });
        else
        {
            note.Conducted = true;
            note.AttendanceTaken = true;
        }

        // Hammasi KELMADI bo'lsa — sabab: berilgani, yo'q bo'lsa birinchi (kech bo'lmagan) sabab,
        // umuman sabab bo'lmasa standart "Sababsiz" avtomatik yaratiladi (yo'q tugmasi har doim ishlasin).
        string? absentReasonId = null;
        if (req.Absent)
        {
            absentReasonId = req.ReasonId;
            if (string.IsNullOrEmpty(absentReasonId))
            {
                var def = await db.AbsenceReasons.FirstOrDefaultAsync(r => !r.IsLate);
                if (def is null)
                {
                    def = new AbsenceReason { Name = "Sababsiz", Short = "S", IsLate = false };
                    db.AbsenceReasons.Add(def);
                }
                absentReasonId = def.Id;
            }
        }
        var lateIds = req.Absent ? await AttendanceNotify.LateReasonIdsAsync(db) : new HashSet<string>();

        var existing = await db.JournalEntries.Where(e =>
            e.ClassId == req.ClassId && e.SubjectId == req.SubjectId && e.Quarter == quarter &&
            e.Date == req.Date && e.Period == req.Period && studentIds.Contains(e.StudentId)).ToListAsync();
        var byStudent = existing.ToDictionary(e => e.StudentId);

        var newlyAbsent = new List<string>();
        foreach (var sid in studentIds)
        {
            byStudent.TryGetValue(sid, out var entry);
            if (req.Absent)
            {
                // YANGI "kelmadi" — saqlashdan OLDINGI holatga qarab (allaqachon kelmagan bo'lsa qayta xabar yo'q).
                if (AttendanceNotify.IsNewAbsence(entry?.ReasonId, absentReasonId, lateIds)) newlyAbsent.Add(sid);
                // Hammasi kelmadi — har bir o'quvchiga sabab yoziladi (mavjud baho saqlanadi).
                if (entry is null)
                {
                    entry = new JournalEntry
                    {
                        ClassId = req.ClassId, SubjectId = req.SubjectId, Quarter = quarter,
                        StudentId = sid, Date = req.Date, Period = req.Period,
                    };
                    db.JournalEntries.Add(entry);
                }
                entry.ReasonId = absentReasonId;
                entry.Present = false;
            }
            else
            {
                // Hammasi keldi — har bir o'quvchiga ANIQ Present=true yoziladi (sabab tozalanadi,
                // baho/uy vazifa/xulq tegilmaydi). Yozuvsiz o'quvchiga ham yozuv yaratiladi — shunda
                // orqaga sanalgan a'zolikda (PresentDefaultFrom dan oldingi darslarda) ham yashil ✓
                // ko'rinadi (aks holda "dars o'tildi + yozuv yo'q = keldi" konventsiyasi bosilib qolardi).
                if (entry is null)
                {
                    entry = new JournalEntry
                    {
                        ClassId = req.ClassId, SubjectId = req.SubjectId, Quarter = quarter,
                        StudentId = sid, Date = req.Date, Period = req.Period,
                    };
                    db.JournalEntries.Add(entry);
                }
                entry.ReasonId = null;
                entry.Present = true;
            }
        }

        var result = new BulkAttendanceResult(req.Absent ? absentReasonId : null, studentIds, newlyAbsent, skipped);
        if (beforeSave is not null) await beforeSave(result);
        await db.SaveChangesAsync();
        return result;
    }

    /// <summary>Shu darsda (guruh, fan, sana, dars raqami) ANIQ «keldi» (<c>Present</c>) deb yozilgan
    /// o'quvchilar — «keldi» xabari faqat YANGI kelganlarga ketishi uchun (saqlashdan oldin va keyin).</summary>
    public static async Task<HashSet<string>> PresentStudentIdsAsync(
        IAppDbContext db, string classId, string subjectId, string date, int period, IReadOnlyCollection<string> studentIds) =>
        (await db.JournalEntries.AsNoTracking()
            .Where(e => e.ClassId == classId && e.SubjectId == subjectId && e.Date == date && e.Period == period
                        && e.Present && studentIds.Contains(e.StudentId))
            .Select(e => e.StudentId)
            .ToListAsync()).ToHashSet();

    public static async Task ClearEntryAsync(
        IAppDbContext db, string classId, string subjectId, int quarter,
        string studentId, string date, int period)
    {
        var entries = db.JournalEntries.Where(e =>
            e.ClassId == classId && e.SubjectId == subjectId && e.Quarter == quarter &&
            e.StudentId == studentId && e.Date == date && e.Period == period);
        db.JournalEntries.RemoveRange(entries);
        await db.SaveChangesAsync();
    }

    public static async Task<List<JournalTopicDto>> GetNotesAsync(
        IAppDbContext db, string classId, string subjectId, int quarter) =>
        await db.LessonNotes
            .Where(n => n.ClassId == classId && n.SubjectId == subjectId && n.Quarter == quarter)
            .Select(n => new JournalTopicDto(n.Date, n.Period, n.Topic, n.Homework, n.Conducted))
            .ToListAsync();

    public static async Task SetNoteAsync(IAppDbContext db, SetLessonNoteRequest req)
    {
        var note = await db.LessonNotes.FirstOrDefaultAsync(n =>
            n.ClassId == req.ClassId && n.SubjectId == req.SubjectId &&
            n.Quarter == req.Quarter && n.Date == req.Date && n.Period == req.Period);

        // Mavzu, uyga vazifa va "dars o'tildi" — uchchovi ham bo'sh bo'lsa yozuvni o'chiramiz.
        var empty = string.IsNullOrWhiteSpace(req.Topic) && string.IsNullOrWhiteSpace(req.Homework) && !req.Conducted;
        if (empty)
        {
            if (note is not null) db.LessonNotes.Remove(note);
        }
        else if (note is null)
        {
            db.LessonNotes.Add(new LessonNote
            {
                ClassId = req.ClassId,
                SubjectId = req.SubjectId,
                Quarter = req.Quarter,
                Date = req.Date,
                Period = req.Period,
                Topic = req.Topic,
                Homework = req.Homework,
                Conducted = req.Conducted,
            });
        }
        else
        {
            note.Topic = req.Topic;
            note.Homework = req.Homework;
            note.Conducted = req.Conducted;
        }
        await db.SaveChangesAsync();
    }

    // ---------- Mavzular Excel shablon / import (mavzu + uy vazifa; darsni "o'tilgan" QILMAYDI) ----------

    public static readonly string[] TopicHeaders =
        { "Dars raqami", "Mavzu", "Uy vazifa" };

    /// <summary>Tanlangan guruh+fan+chorak uchun mavzular shabloni (.xlsx): "Dars raqami" jadval tartibida
    /// (1, 2, 3, ...) oldindan to'ldirilgan — sana va guruh shu raqamdan avtomatik aniqlanadi;
    /// "Mavzu"/"Uy vazifa" ustunlari foydalanuvchi to'ldirishi uchun (mavjudi ham ko'rsatiladi).</summary>
    public static async Task<byte[]> TopicTemplateXlsxAsync(IAppDbContext db, string classId, string subjectId, int quarter)
    {
        var cols = await ComputeColumnsAsync(db, classId, subjectId, quarter);
        var notes = (await db.LessonNotes
                .Where(n => n.ClassId == classId && n.SubjectId == subjectId && n.Quarter == quarter).ToListAsync())
            .GroupBy(n => (n.Date, n.Period)).ToDictionary(g => g.Key, g => g.First());

        // Har bir dars slotiga jadval tartibidagi raqam beriladi (1-asosli); sana shu raqamdan kelib chiqadi.
        var rows = cols.Select((c, i) =>
        {
            notes.TryGetValue((c.Date, c.Period), out var n);
            return (IReadOnlyList<string>)new[]
            {
                (i + 1).ToString(), n?.Topic ?? "", n?.Homework ?? "",
            };
        }).ToList();

        var info = new List<IReadOnlyList<string>>
        {
            new[] { "Dars raqami", "O'zgartirmang — jadval tartibidagi dars raqami (sana va guruh avtomatik aniqlanadi)" },
            new[] { "Mavzu", "Dars mavzusini yozing" },
            new[] { "Uy vazifa", "Uyga vazifani yozing (ixtiyoriy)" },
            new[] { "", "" },
            new[] { "Eslatma:", "Import faqat mavzu/uy vazifani to'ldiradi — darsni \"o'tilgan\" QILMAYDI (buni jurnalda o'zingiz belgilaysiz)." },
        };

        return ExcelExport.Build(new[]
        {
            new ExcelExport.SheetSpec("Mavzular", TopicHeaders, rows),
            new ExcelExport.SheetSpec("Yo'riqnoma", new[] { "Ustun", "Izoh" }, info),
        });
    }

    /// <summary>Excel qatorlaridan mavzu+uy vazifani jurnalga import qiladi. MUHIM: darsni "o'tilgan"
    /// QILMAYDI — yangi yozuvda Conducted=false, mavjud yozuvda Conducted o'zgarmaydi. Faqat "Dars raqami"
    /// (jadval tartibidagi 1-asosli raqam) o'qiladi; mos sana/dars/guruh shu raqamdan avtomatik aniqlanadi.</summary>
    public static async Task<TopicImportResultDto> ImportTopicsAsync(
        IAppDbContext db, string classId, string subjectId, int quarter, List<string[]> rows)
    {
        // Tartiblangan dars ketma-ketligi — "Dars raqami" shu ro'yxatga 1-asosli indeks.
        var cols = await ComputeColumnsAsync(db, classId, subjectId, quarter);
        var notes = (await db.LessonNotes
                .Where(n => n.ClassId == classId && n.SubjectId == subjectId && n.Quarter == quarter).ToListAsync())
            .GroupBy(n => (n.Date, n.Period)).ToDictionary(g => g.Key, g => g.First());

        var errors = new List<TopicImportRowErrorDto>();
        int imported = 0, skipped = 0;
        for (var i = 1; i < rows.Count; i++) // 0-qator = sarlavha
        {
            var r = rows[i];
            var excelRow = i + 1;
            if (r.All(string.IsNullOrWhiteSpace)) { skipped++; continue; }

            var lessonOk = int.TryParse((r.ElementAtOrDefault(0) ?? "").Trim(), out var lessonNo);
            var topic = (r.ElementAtOrDefault(1) ?? "").Trim();
            var homework = (r.ElementAtOrDefault(2) ?? "").Trim();

            if (!lessonOk || lessonNo <= 0)
            { errors.Add(new TopicImportRowErrorDto(excelRow, "Dars raqami noto'g'ri")); continue; }
            if (topic.Length == 0 && homework.Length == 0) { skipped++; continue; }
            if (lessonNo > cols.Count)
            { errors.Add(new TopicImportRowErrorDto(excelRow, $"Dars raqami {lessonNo} jadvalda yo'q (jami {cols.Count} ta dars)")); continue; }

            var slot = cols[lessonNo - 1];
            var key = (slot.Date, slot.Period);

            if (notes.TryGetValue(key, out var n))
            {
                n.Topic = topic;
                n.Homework = homework;
                // Conducted O'ZGARMAYDI — import darsni o'tilgan qilmaydi.
            }
            else
            {
                var fresh = new LessonNote
                {
                    ClassId = classId, SubjectId = subjectId, Quarter = quarter,
                    Date = slot.Date, Period = slot.Period,
                    Topic = topic, Homework = homework, Conducted = false,
                };
                db.LessonNotes.Add(fresh);
                notes[key] = fresh; // bir slot ikki marta kelsa qayta qo'shmaymiz
            }
            imported++;
        }
        if (imported > 0) await db.SaveChangesAsync();
        return new TopicImportResultDto(imported, skipped, errors.Count, errors);
    }
}
