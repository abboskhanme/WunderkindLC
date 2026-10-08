using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Baholash (GradingCriterion, CriterionGrade) mantig'i — mezonlar bo'yicha o'quvchilarni
/// har darsga belgilash (o'qituvchi/admin) va ularning yig'ilgan statistikasi.
/// </summary>
public static class GradingService
{
    /// <summary>
    /// Guruhning bitta oyida o'quvchi-level agregatsiya: nechta mezon biriktirilgan,
    /// har o'quvchi nechta mezonni nechta darsda bajargan, ummumiy/o'rtacha.
    /// </summary>
    /// <param name="db">Database konteksti.</param>
    /// <param name="groupId">Guruh ID.</param>
    /// <param name="month">Oy ("yyyy-MM"), bo'lmasa joriy oy.</param>
    /// <returns>O'quvchi bo'yicha: Id, FullName, CriteriaCount, TotalScore, AverageScore.</returns>
    public static async Task<List<StudentGradingTotalDto>> CalculateStudentTotalsAsync(
        IAppDbContext db, string groupId, string? month)
    {
        var cur = TuitionService.CurrentMonth();
        var resolved = !string.IsNullOrEmpty(month) ? month : cur;

        // Shu guruhga biriktirilgan mezonlar.
        var critIds = await db.GroupGradingCriteria
            .Where(g => g.GroupId == groupId)
            .Select(g => g.CriterionId)
            .ToListAsync();

        if (critIds.Count == 0)
            return new List<StudentGradingTotalDto>();

        // Mezonlarni olaylik (MaxScore har mezon uchun).
        var criteria = await db.GradingCriteria
            .Where(c => critIds.Contains(c.Id))
            .Select(c => new { c.Id, c.MaxScore })
            .ToListAsync();

        var critDict = criteria.ToDictionary(c => c.Id, c => c.MaxScore);

        // Shu oyda shu guruhdagi o'quvchilarning "bajardi" belgilari — FAQAT guruhga BIRIKTIRILGAN
        // mezonlar bo'yicha (ajratilgan mezonning eski belgilari sanalsa TotalScore CriteriaCount dan
        // oshib, o'rtacha 1 dan katta — "150%" — chiqardi).
        var grades = await db.CriterionGrades
            .Where(g => g.GroupId == groupId && g.Done && g.Date.StartsWith(resolved) && critIds.Contains(g.CriterionId))
            .Select(g => new { g.StudentId, g.CriterionId })
            .ToListAsync();

        // Har o'quvchi uchun: shu oy nechta mezon check bajargan.
        var doneByStudent = grades
            .GroupBy(g => g.StudentId)
            .ToDictionary(
                grp => grp.Key,
                grp => grp.Select(g => g.CriterionId).Distinct().Count());

        // O'quvchilar (faol a'zolar).
        var memberIds = await db.StudentGroups
            .Where(sg => sg.GroupId == groupId && sg.IsActive && sg.Status != "frozen")
            .Select(sg => sg.StudentId)
            .ToListAsync();

        var students = await db.Students
            .Where(s => memberIds.Contains(s.Id) && !s.IsArchived)
            .OrderBy(s => s.FullName)
            .ToListAsync();

        var result = new List<StudentGradingTotalDto>();

        foreach (var st in students)
        {
            var doneCount = doneByStudent.TryGetValue(st.Id, out var cnt) ? cnt : 0;
            var criteriaCount = critIds.Count;
            var avgScore = criteriaCount > 0 ? (double)doneCount / criteriaCount : 0;

            result.Add(new StudentGradingTotalDto(
                st.Id,
                st.FullName,
                criteriaCount,
                doneCount,
                Math.Round(avgScore, 2)));
        }

        return result;
    }

    /* =========================================================================================
     *  BELGILASH (o'qituvchi ham, admin ham) + JURNAL BAHOSI SINXRONI
     * ========================================================================================= */

    /// <summary>
    /// Shu sanaga mezon belgilash (yoki olib tashlash) MUMKINMI — jurnal qoidalarining O'ZI
    /// (ilgari baholash taxtasi ularning HECH BIRIGA bo'ysunmasdi, holbuki u jurnal bahosini va
    /// darsning "o'tildi" belgisini — ya'ni MAOSHni — o'zgartiradi). null = mumkin.
    /// <list type="bullet">
    /// <item>sana formati, kelajak sana (har doim taqiq), guruh boshlanishidan oldin;</item>
    /// <item>sana guruhning HAQIQIY dars kuni bo'lishi (ko'chirishlar bilan) — aks holda dars yo'q
    ///   kunga "o'tildi" yozilardi;</item>
    /// <item><see cref="JournalPolicy.CheckAsync"/> — tahrirlash oynasi (o'qituvchiga har doim, adminga
    ///   "Adminlarga ham qo'llash" yoqilganda).</item>
    /// </list>
    /// </summary>
    public static async Task<string?> CheckGradableDateAsync(IAppDbContext db, Group group, string date, bool isAdmin)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out _)) return "Sana noto'g'ri";
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        if (string.CompareOrdinal(date, today) > 0)
            return "Dars hali o'tilmagan — kelajakdagi sanaga baho qo'yib bo'lmaydi";
        if (group.StartDate is { Length: >= 10 } && string.CompareOrdinal(date, group.StartDate[..10]) < 0)
            return "Sana guruh yaratilishidan oldin";
        var moves = (await db.LessonReschedules.AsNoTracking().Where(r => r.ClassId == group.Id).ToListAsync())
            .Select(m => new JournalService.LessonMove(m.FromDate, m.ToDate)).ToList();
        if (!JournalService.EffectiveLessonDatesInMonth(group.Days, date[..7], moves).Contains(date))
            return "Bu kunda dars yo'q";
        return await JournalPolicy.CheckAsync(db, group.Id, group.CourseId ?? "", date, 1, isAdmin, skipConducted: true);
    }

    /// <summary>
    /// Bitta katakni belgilash: Done=true → yozuv (bajardi); Done=false → yozuvni o'chiramiz (sparse).
    /// So'ng shu (o'quvchi, sana) uchun jurnal bahosini sinxronlaydi (<see cref="SyncJournalGradeAsync"/>).
    /// <para>Belgilash (Done=true) faqat FAOL, muzlatilmagan, arxivlanmagan a'zoga va uning a'zoligi
    /// boshlangan kundan (<see cref="JournalService.MemberWriteError"/> — o'qituvchi qoidasi:
    /// muzlatilgan o'quvchi baholanmaydi). Olib tashlash a'zolikka qaramaydi (tozalash xavfsiz),
    /// lekin sana qoidalariga bo'ysunadi.</para>
    /// Xatolar — <see cref="InvalidOperationException"/> (controller 400 qaytaradi).
    /// </summary>
    public static async Task UpsertGradeAsync(IAppDbContext db, SetCriterionGradeRequest req, bool isAdmin)
    {
        var group = await db.Classes.FindAsync(req.GroupId)
            ?? throw new InvalidOperationException("Guruh topilmadi");
        var dateError = await CheckGradableDateAsync(db, group, req.Date, isAdmin);
        if (dateError is not null) throw new InvalidOperationException(dateError);

        if (req.Done)
        {
            var memberError = await JournalService.MemberWriteErrorAsync(db, req.GroupId, req.StudentId, req.Date, teacher: true);
            if (memberError == JournalService.BeforeMemberStartMessage)
                throw new InvalidOperationException(
                    "O'quvchi guruhga qo'shilgan/aktivlashtirilgan sanadan oldingi darsga mezon belgilab bo'lmaydi");
            if (memberError is not null) throw new InvalidOperationException(memberError);
        }

        var existing = await db.CriterionGrades.FirstOrDefaultAsync(
            g => g.GroupId == req.GroupId && g.StudentId == req.StudentId
                 && g.CriterionId == req.CriterionId && g.Date == req.Date);
        var now = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        if (req.Done)
        {
            if (existing is null)
                db.CriterionGrades.Add(new CriterionGrade
                {
                    GroupId = req.GroupId, StudentId = req.StudentId, CriterionId = req.CriterionId,
                    Date = req.Date, Done = true, UpdatedAt = now,
                });
            else { existing.Done = true; existing.UpdatedAt = now; }
        }
        else if (existing is not null)
        {
            db.CriterionGrades.Remove(existing);
        }

        // Mezon o'zgarishini saqlaymiz (sanoq aniq bo'lishi uchun) → so'ng jurnal bahosini sinxronlaymiz.
        await db.SaveChangesAsync();
        await SyncJournalGradeAsync(db, group, req.StudentId, req.Date, markConducted: true);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Shu sanada bitta mezon bo'yicha guruhning BARCHA faol (muzlatilmagan, arxivlanmagan) o'quvchisini
    /// belgilaydi/olib tashlaydi. A'zoligi shu sanadan KEYIN boshlanganlar va <paramref name="exclude"/>
    /// (o'qituvchi yo'lida — to'lov darvozasi yashirgan o'quvchilar) chetlab o'tiladi.
    /// Sana qoidalari — <see cref="CheckGradableDateAsync"/> (xato — <see cref="InvalidOperationException"/>).
    /// </summary>
    public static async Task BulkGradeAsync(
        IAppDbContext db, Group group, string criterionId, string date, bool done, bool isAdmin,
        IReadOnlySet<string>? exclude = null)
    {
        var dateError = await CheckGradableDateAsync(db, group, date, isAdmin);
        if (dateError is not null) throw new InvalidOperationException(dateError);

        var memberships = await db.StudentGroups.AsNoTracking()
            .Where(sg => sg.GroupId == group.Id && sg.IsActive && sg.Status != "frozen")
            .ToListAsync();
        var candidateIds = memberships.Select(m => m.StudentId).Distinct().ToList();
        var archived = (await db.Students.AsNoTracking()
                .Where(s => s.IsArchived && candidateIds.Contains(s.Id))
                .Select(s => s.Id).ToListAsync())
            .ToHashSet();
        var memberIds = memberships
            .Where(sg => JournalService.MemberWriteError(sg, archived.Contains(sg.StudentId), date, teacher: true) is null)
            .Where(sg => exclude is null || !exclude.Contains(sg.StudentId))
            .Select(sg => sg.StudentId).Distinct().ToList();

        var existing = await db.CriterionGrades
            .Where(g => g.GroupId == group.Id && g.CriterionId == criterionId && g.Date == date).ToListAsync();
        var byStudent = existing.ToDictionary(g => g.StudentId);
        var now = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        foreach (var sid in memberIds)
        {
            byStudent.TryGetValue(sid, out var e);
            if (done)
            {
                if (e is null)
                    db.CriterionGrades.Add(new CriterionGrade
                    {
                        GroupId = group.Id, StudentId = sid, CriterionId = criterionId,
                        Date = date, Done = true, UpdatedAt = now,
                    });
                else { e.Done = true; e.UpdatedAt = now; }
            }
            else if (e is not null)
            {
                db.CriterionGrades.Remove(e);
            }
        }

        // Mezon o'zgarishlarini saqlaymiz → so'ng har bir o'quvchining jurnal bahosini sinxronlaymiz.
        await db.SaveChangesAsync();
        foreach (var sid in memberIds)
            await SyncJournalGradeAsync(db, group, sid, date, markConducted: true);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// MEZON CHECKLARI SONI = JURNAL BAHOSI — lekin FAQAT sinxronning O'Z bahosi uchun
    /// (<see cref="JournalEntry.GradeFromCriteria"/>). (guruh, o'quvchi, sana) bo'yicha BELGILANGAN va
    /// guruhga BIRIKTIRILGAN mezonlar sonini hisoblab:
    /// <list type="bullet">
    /// <item>katakda QO'LDA qo'yilgan baho bo'lsa — HECH NARSA qilinmaydi (ilgari qo'lda qo'yilgan 5
    ///   bitta check bilan 1 ga aylanar, ommaviy "olib tashlash" esa kunning barcha qo'lda bahosini
    ///   o'chirib yuborardi);</item>
    /// <item>son &gt; 0 — baho = son, <c>GradeFromCriteria = true</c>;</item>
    /// <item>son = 0 — sinxronning o'z bahosi tozalanadi (sabab/uy vazifa saqlanadi; bo'm-bo'sh qolgan
    ///   yozuv o'chiriladi).</item>
    /// </list>
    /// <paramref name="markConducted"/> — belgilanganda dars "o'tildi" bo'ladi (jurnaldagi kabi;
    /// sana qoidalari <see cref="CheckGradableDateAsync"/> da allaqachon tekshirilgan). Mezon o'chirilganda
    /// qayta sinxronda (<see cref="ResyncAsync"/>) false — u darsni "o'tildi" qilmaydi.
    /// Kurssiz guruh — jurnal yo'q, hech narsa qilinmaydi.
    /// </summary>
    public static async Task SyncJournalGradeAsync(
        IAppDbContext db, Group group, string studentId, string date, bool markConducted)
    {
        var subjectId = group.CourseId ?? "";
        if (string.IsNullOrEmpty(subjectId)) return; // kurssiz guruh — jurnal yo'q
        const int quarter = 1, period = 1;

        // Guruhga biriktirilgan mezonlar (board ko'rsatadigan) bo'yicha belgilangan (Done) sonni hisoblaymiz.
        var assigned = await db.GroupGradingCriteria.Where(g => g.GroupId == group.Id)
            .Select(g => g.CriterionId).ToListAsync();
        var count = assigned.Count == 0 ? 0 : await db.CriterionGrades.CountAsync(g =>
            g.GroupId == group.Id && g.StudentId == studentId && g.Date == date && g.Done
            && assigned.Contains(g.CriterionId));

        var entry = await db.JournalEntries.FirstOrDefaultAsync(e =>
            e.ClassId == group.Id && e.SubjectId == subjectId && e.Quarter == quarter &&
            e.StudentId == studentId && e.Date == date && e.Period == period);

        // QO'LDA qo'yilgan baho — sinxron unga tegmaydi.
        if (entry is { Grade: not null, GradeFromCriteria: false }) return;

        if (count > 0)
        {
            if (entry is null)
            {
                entry = new JournalEntry
                {
                    ClassId = group.Id, SubjectId = subjectId, Quarter = quarter,
                    StudentId = studentId, Date = date, Period = period,
                };
                db.JournalEntries.Add(entry);
            }
            entry.Grade = count;
            entry.GradeFromCriteria = true;

            if (!markConducted) return;
            // Darsni "o'tildi" deb belgilash (jurnal ustuni/yashil holat uchun) — SetEntryAsync bilan izchil.
            var note = await db.LessonNotes.FirstOrDefaultAsync(n =>
                n.ClassId == group.Id && n.SubjectId == subjectId &&
                n.Quarter == quarter && n.Date == date && n.Period == period);
            if (note is null)
                db.LessonNotes.Add(new LessonNote
                {
                    ClassId = group.Id, SubjectId = subjectId, Quarter = quarter,
                    Date = date, Period = period, Conducted = true,
                });
            else if (!note.Conducted) note.Conducted = true;
        }
        else if (entry is not null && entry.GradeFromCriteria)
        {
            // Checklar olib tashlandi — FAQAT sinxronning o'z bahosini tozalaymiz.
            entry.Grade = null;
            entry.GradeFromCriteria = false;
            if (entry.ReasonId is null && !entry.Present && entry.Homework == 0 && entry.Behavior == 0
                && entry.Mastery is null)
                db.JournalEntries.Remove(entry);
        }
    }

    /// <summary>
    /// Mezon o'chirilganda yoki guruhdan AJRATILGANDA — ta'sirlangan (guruh, o'quvchi, sana) kataklarining
    /// sinxron bahosini qayta hisoblaydi (qo'lda qo'yilgan baho tegilmaydi). Darsni "o'tildi" QILMAYDI.
    /// Chaqiruvchi so'ng <c>SaveChangesAsync</c> qiladi.
    /// </summary>
    public static async Task ResyncAsync(
        IAppDbContext db, IEnumerable<(string GroupId, string StudentId, string Date)> keys)
    {
        var list = keys.Distinct().ToList();
        if (list.Count == 0) return;
        var groupIds = list.Select(k => k.GroupId).Distinct().ToList();
        var groups = await db.Classes.Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id);
        foreach (var (gid, sid, date) in list)
            if (groups.TryGetValue(gid, out var g))
                await SyncJournalGradeAsync(db, g, sid, date, markConducted: false);
    }
}
