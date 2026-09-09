using Microsoft.EntityFrameworkCore;
using IntellectCRM.Application.Abstractions;
using IntellectCRM.Application.Dtos;
using IntellectCRM.Domain;

namespace IntellectCRM.Application.Services;

/// <summary>
/// Markaz bo'yicha o'quvchilar reytingi — admin "Reyting" sahifasi va o'quvchi/parent portali uchun
/// umumiy manba. Ball manbasi — <see cref="StudentBallService"/>
/// (<c>Σ jurnal baholari + Σ bajarilgan mezonlar + Σ qo'lda tuzatish</c>, PER-GURUH hisoblanadi).
///
/// <para><b>MARKAZ O'RNI — GURUHLAR BO'YICHA O'RTACHA BALL</b> (<c>AvgBall</c>), yig'indi EMAS:
/// ikki-uch guruhda o'qiydigan o'quvchi ballari qo'shilib, u markazda nohaq birinchi chiqardi.
/// Teng bo'lsa o'rtacha baho hal qiladi. Bitta guruhli o'quvchida <c>AvgBall == Ball</c>.</para>
///
/// <para><b>GURUH REYTINGI — FAQAT O'SHA GURUH BALI</b> (<see cref="PortalAsync"/>): qatorlar
/// <c>StudentRatingRowDto.GroupBalls[guruh]</c> dan quriladi va shu guruh bali bo'yicha QAYTA
/// saralanadi. Ilgari guruh ro'yxati markaz JAMI bali bilan chizilar va markaz tartibida qolardi.</para>
///
/// HAR O'QUVCHI BITTA QATOR (markaz ro'yxatida): bir nechta guruhda bo'lsa ham, baho/davomat barcha
/// FAOL guruhlari bo'yicha yig'iladi — aks holda ko'p guruhli o'quvchi reytingda DUBLIKAT bo'lardi.
/// </summary>
public static class RatingService
{
    /// <summary>Barcha o'quvchilarning reyting qatori (har biri bir marta: ball + o'rtacha baho + davomat).</summary>
    public static async Task<List<StudentRatingRowDto>> SchoolAsync(IAppDbContext db)
    {
        // Ball PER-GURUH hisoblanadi, keyin o'quvchi bo'yicha jamlanadi: `Ball` — yig'indi,
        // `AvgBall` — markaz reytingi saralanadigan o'rtacha, `GroupBalls` — guruh kesimi.
        var balls = StudentBallService.AggregateByStudent(await StudentBallService.ComputeByGroupAsync(db));
        // Arxivlanganlar reytingda qatnashmaydi.
        var students = await db.Students.Where(s => !s.IsArchived).ToListAsync();
        var classes = await db.Classes.ToListAsync();
        var classById = classes.ToDictionary(c => c.Id);
        var lateIds = (await db.AbsenceReasons.Where(r => r.IsLate).Select(r => r.Id).ToListAsync()).ToHashSet();

        // Faol a'zoliklar (M2M) — har o'quvchining guruhlari. Tartib USTUNLIK bo'yicha
        // (faol → sinov → muzlatilgan, keyin eng yangi qo'shilgani): birinchi element "vakil guruh"
        // sifatida ishlatiladi, ya'ni u muzlatilgan (eski) guruh bo'lib qolmasin.
        var groupsByStudent = (await db.StudentGroups.Where(m => m.IsActive).ToListAsync())
            .GroupBy(m => m.StudentId)
            .ToDictionary(g => g.Key, g => g
                .OrderBy(m => MembershipLifecycle.StateRank(m.Status))
                .ThenByDescending(m => m.JoinedAt ?? string.Empty, StringComparer.Ordinal)
                .Select(m => m.GroupId).Distinct().ToList());

        // Jurnal yozuvlari va o'tilgan darslar — bir marta yuklab, guruh bo'yicha guruhlaymiz.
        var entriesByClass = (await db.JournalEntries.ToListAsync())
            .GroupBy(e => e.ClassId).ToDictionary(g => g.Key, g => g.ToList());
        var conductedByClass = (await db.LessonNotes.Where(n => n.Conducted).ToListAsync())
            .GroupBy(n => n.ClassId)
            .ToDictionary(g => g.Key, g => g.Select(n => (n.SubjectId, n.Date, n.Period)).ToHashSet());

        var result = new List<StudentRatingRowDto>();
        foreach (var st in students)
        {
            // O'quvchining guruhlari: faol a'zolik (M2M); yo'q bo'lsa ClassName yorlig'i bo'yicha (orqaga moslik).
            var groupIds = groupsByStudent.TryGetValue(st.Id, out var gs) && gs.Count > 0
                ? gs
                : classes.Where(c => !string.IsNullOrEmpty(st.ClassName) && c.Name == st.ClassName)
                    .Select(c => c.Id).ToList();
            if (groupIds.Count == 0) continue; // guruhsiz o'quvchi reytingda yo'q

            var grades = new List<double>();
            int conducted = 0, absent = 0;
            foreach (var gid in groupIds)
            {
                var mine = entriesByClass.TryGetValue(gid, out var ents)
                    ? ents.Where(e => e.StudentId == st.Id).ToList()
                    : new List<JournalEntry>();
                grades.AddRange(mine.Where(e => e.Grade.HasValue).Select(e => (double)e.Grade!.Value));
                if (conductedByClass.TryGetValue(gid, out var cond))
                {
                    conducted += cond.Count;
                    absent += mine.Count(e => e.ReasonId != null && !lateIds.Contains(e.ReasonId)
                        && cond.Contains((e.SubjectId, e.Date, e.Period)));
                }
            }

            var average = grades.Count > 0 ? Math.Round(grades.Average(), 1) : 0;
            double? attendance = conducted > 0 ? Math.Round((double)(conducted - absent) / conducted * 100) : null;

            // VAKIL GURUH — a'zolik bo'yicha birinchisi (yuqorida ustunlik tartibida saralangan).
            // ⚠️ Ilgari `ClassName` (BIRINCHI qo'shilgan guruh yorlig'i) ustun edi: eski guruhida
            // muzlatilib yangisida o'qiyotgan o'quvchi reytingda ESKI guruh nomi bilan chiqardi.
            var firstCls = classById.TryGetValue(groupIds[0], out var c0) ? c0 : null;
            var className = firstCls?.Name ?? st.ClassName;
            var gradeLevel = firstCls?.Grade ?? 0;

            var b = balls.GetValueOrDefault(st.Id);
            // GroupIds — guruh reytingi SHU bo'yicha ajratiladi (ClassName matni emas);
            // GroupBalls — o'sha guruhdagi ball (guruh ro'yxati markaz jamisini ko'rsatmasin).
            result.Add(new StudentRatingRowDto(
                Map(st), className, gradeLevel, average, attendance, b?.Ball ?? 0, groupIds,
                AvgBall: b?.AvgBall ?? 0,
                GroupCount: b?.GroupCount ?? 0,
                GroupBalls: b is null
                    ? new Dictionary<string, int>()
                    : new Dictionary<string, int>(b.GroupBalls)));
        }
        return result;
    }

    /// <summary>
    /// O'QUVCHI/PARENT PORTALI reytingi: markaz TOP 15 + o'quvchining HAR BIR faol guruhi alohida.
    ///
    /// <para><b>Nega guruh a'zoligi bo'yicha:</b> ilgari guruh ro'yxati <c>ClassName</c> matn
    /// yorlig'i tengligi bilan qurilardi. M2M a'zolikka o'tilgandan keyin bu yorliq ko'p
    /// o'quvchida BO'SH yoki ESKIRGAN — bo'sh bo'lsa ro'yxat umuman bo'sh chiqar
    /// ("Reyting yo'q"), eskirgan bo'lsa butunlay boshqa guruh odamlari ko'rinardi.</para>
    ///
    /// <para>Har guruh ichida o'rin QAYTA raqamlanadi (1,2,3...) — podium (1/2/3) shunga tayanadi.
    /// Faol a'zoligi yo'q o'quvchida eski <c>ClassName</c> tengligiga qaytiladi (fallback) —
    /// eski/guruhsiz yozuvlar yo'qolmasin.</para>
    ///
    /// <para><b>QAYSI SON KO'RSATILADI:</b> guruh ro'yxatlarida (<c>Groups[].Rows</c>,
    /// <c>ClassRows</c>) — <b>SHU GURUHDAGI</b> ball va saralash ham shu bo'yicha; markaz
    /// ro'yxatida (<c>SchoolRows</c>) — guruhlar bo'yicha <b>O'RTACHA</b>. Ya'ni har ro'yxatda
    /// "shu reyting nima bo'yicha tuzilgan" soni turadi va klientga qo'shimcha mantiq kerak emas.
    /// Har qatorda `TotalBall`/`GroupCount`/`AvgBall` ham qaytadi (shaffoflik uchun).</para>
    /// </summary>
    /// <param name="school">Keshdan olingan markaz reytingi (<see cref="SchoolAsync"/>) — bu yerda
    /// saralanadi, chaqiruvchi oldindan saralashi shart emas.</param>
    public static async Task<PortalRatingDto> PortalAsync(
        IAppDbContext db, Student s, IEnumerable<StudentRatingRowDto> school)
    {
        // MARKAZ: guruhlar bo'yicha O'RTACHA ball kamayish tartibida (teng bo'lsa o'rtacha baho).
        var ordered = school.OrderByDescending(r => r.AvgBall).ThenByDescending(r => r.Average).ToList();

        // Markaz qatori: ko'rsatiladigan `Ball` = O'RTACHA (butunlashtirilgan).
        static PortalRatingRowDto MapSchool(StudentRatingRowDto r, int i) =>
            new(i + 1, r.Student.Id, r.Student.FullName, r.ClassName, r.Average, r.Attendance,
                (int)Math.Round(r.AvgBall), r.Ball, r.GroupCount, r.AvgBall);

        // Guruh qatori: ko'rsatiladigan `Ball` = FAQAT shu guruh bali.
        static PortalRatingRowDto MapGroup(StudentRatingRowDto r, string groupId, int i) =>
            new(i + 1, r.Student.Id, r.Student.FullName, r.ClassName, r.Average, r.Attendance,
                r.GroupBalls is not null && r.GroupBalls.TryGetValue(groupId, out var gb) ? gb : 0,
                r.Ball, r.GroupCount, r.AvgBall);

        // Guruh ro'yxati — SHU GURUH bali bo'yicha qayta saralanadi (markaz tartibi emas).
        static List<PortalRatingRowDto> GroupRows(IEnumerable<StudentRatingRowDto> src, string groupId) =>
            src.Select(r => (Row: r,
                    Ball: r.GroupBalls is not null && r.GroupBalls.TryGetValue(groupId, out var gb) ? gb : 0))
                .OrderByDescending(x => x.Ball)
                .ThenByDescending(x => x.Row.Average)
                .ThenBy(x => x.Row.Student.FullName, StringComparer.OrdinalIgnoreCase)
                .Select((x, i) => MapGroup(x.Row, groupId, i))
                .ToList();

        // O'quvchining FAOL guruhlari — bitta so'rov (N+1 yo'q).
        var myGroupIds = await db.StudentGroups.AsNoTracking()
            .Where(sg => sg.StudentId == s.Id && sg.IsActive)
            .Select(sg => sg.GroupId).Distinct().ToListAsync();

        var groups = new List<PortalRatingGroupDto>();
        if (myGroupIds.Count > 0)
        {
            // Guruh nomlari — yana bitta so'rov (har guruh uchun alohida emas).
            var names = await db.Classes.AsNoTracking()
                .Where(c => myGroupIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name })
                .ToListAsync();
            foreach (var g in names.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var members = ordered.Where(r => r.GroupIds != null && r.GroupIds.Contains(g.Id));
                var rows = GroupRows(members, g.Id);   // o'rin guruh ICHIDA qayta raqamlanadi
                if (rows.Count == 0) continue;
                var me = rows.FindIndex(r => r.StudentId == s.Id);
                groups.Add(new PortalRatingGroupDto(g.Id, g.Name, rows, me >= 0 ? me + 1 : 0, rows.Count));
            }
        }
        else if (!string.IsNullOrEmpty(s.ClassName))
        {
            // FALLBACK: faol a'zolik yo'q — eski "asosiy guruh" yorlig'i bo'yicha. Guruh id'si yo'q,
            // shuning uchun ball ham yig'indi bo'lib qoladi (kesim ajratib bo'lmaydi).
            var rows = ordered.Where(r => r.ClassName == s.ClassName)
                .OrderByDescending(r => r.Ball).ThenByDescending(r => r.Average)
                .Select((r, i) => new PortalRatingRowDto(
                    i + 1, r.Student.Id, r.Student.FullName, r.ClassName, r.Average, r.Attendance,
                    r.Ball, r.Ball, r.GroupCount, r.AvgBall))
                .ToList();
            if (rows.Count > 0)
            {
                var me = rows.FindIndex(r => r.StudentId == s.Id);
                groups.Add(new PortalRatingGroupDto("", s.ClassName, rows, me >= 0 ? me + 1 : 0, rows.Count));
            }
        }

        // ClassRows — ESKI maydon (web klient va ilovaning eski versiyalari o'qiydi): birinchi guruh.
        var classRows = groups.Count > 0 ? groups[0].Rows : new List<PortalRatingRowDto>();
        var schoolRows = ordered.Take(15).Select(MapSchool).ToList();

        var meIdx = ordered.FindIndex(r => r.Student.Id == s.Id);
        int? meSchoolRank = meIdx >= 0 ? meIdx + 1 : null;

        return new PortalRatingDto(s.Id, classRows, schoolRows, meSchoolRank, ordered.Count, groups);
    }

    private static StudentDto Map(Student s) => new(
        s.Id, s.FullName, s.BirthDate, s.Address, s.Gender,
        s.ParentFullName, s.ParentPhone, s.ClassName, s.EnrollmentDate, s.Balance,
        LoginBlocked: s.LoginBlocked);
}
