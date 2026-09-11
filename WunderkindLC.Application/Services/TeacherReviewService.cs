using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// O'QUVCHINING O'QITUVCHI HAQIDAGI FIKRI — yig'ish va o'qish uchun YAGONA mantiq.
///
/// <para><b>Maqsad:</b> o'qituvchini rivojlantirish. Admin o'quvchi bilan gaplashib, uning
/// o'qituvchi haqidagi fikrini o'quvchi profilidagi «Fikr-mulohazalar» bo'limiga yozib boradi.
/// Bu matnlar keyin o'qituvchining AI tahliliga MANBA bo'ladi va takrorlanuvchi naqshlar
/// (kuchli tomon / o'sish nuqtasi) ajratiladi.</para>
///
/// <para><b>Kim yozadi:</b> FAQAT admin/superadmin (o'quvchi yoki ota-ona emas) — ruxsat
/// controllerda tekshiriladi.</para>
///
/// <para><b>MAXFIYLIK — CHEGARA QAYERDA:</b> fikrlar ADMIN uchun ochiq (o'quvchi profilida ham,
/// o'qituvchi profilidagi «Fikrlar» bo'limida ham — <see cref="ForTeacherAsync"/>), lekin
/// O'QITUVCHINING O'ZIGA hech qachon berilmaydi: na o'qituvchi portalida, na Flutter ilovasida
/// bunday endpoint yo'q. AI xulosasi esa o'qituvchiga ham ko'rsatilishi mumkin — shuning uchun
/// <see cref="TextsForTeacherAsync"/> promptga O'QUVCHI ISMINI QO'SHMAYDI.</para>
/// </summary>
public static class TeacherReviewService
{
    /// <summary>Bitta fikr matnining AI promptiga tushadigan maksimal uzunligi.</summary>
    private const int MaxTextForAi = 400;

    /// <summary>Matn maydonining maksimal uzunligi (saqlashda).</summary>
    public const int MaxTextLength = 4000;

    /// <summary>
    /// O'QUVCHI PROFILI uchun: har GURUH bo'yicha bitta blok (guruh + o'qituvchi + shu o'qituvchi
    /// haqida yozilgan fikrlar). O'quvchining BARCHA a'zoliklari olinadi — chiqib ketgan/tugatgan
    /// guruhlar ham, chunki o'sha davrdagi fikr ham o'qituvchi uchun qimmatli.
    /// Bloklar: avval faol a'zoliklar, keyin guruh nomi bo'yicha.
    /// </summary>
    public static async Task<List<StudentTeacherReviewGroupDto>> ForStudentAsync(
        IAppDbContext db, string studentId)
    {
        var memberships = await db.StudentGroups.AsNoTracking()
            .Where(sg => sg.StudentId == studentId)
            .Select(sg => new { sg.GroupId, sg.IsActive, sg.Status })
            .ToListAsync();
        if (memberships.Count == 0) return new List<StudentTeacherReviewGroupDto>();

        var groupIds = memberships.Select(m => m.GroupId).Distinct().ToList();
        var groups = (await db.Classes.AsNoTracking()
                .Where(g => groupIds.Contains(g.Id))
                .Select(g => new { g.Id, g.Name, g.CourseId, g.TeacherId }).ToListAsync())
            .ToDictionary(g => g.Id);

        var teacherIds = groups.Values.Select(g => g.TeacherId)
            .Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
        var teacherNames = teacherIds.Count == 0
            ? new Dictionary<string, string>()
            : await db.Teachers.AsNoTracking()
                .Where(t => teacherIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.FullName);

        var courseIds = groups.Values.Select(g => g.CourseId)
            .Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
        var courseNames = courseIds.Count == 0
            ? new Dictionary<string, string>()
            : await db.Subjects.AsNoTracking()
                .Where(s => courseIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name);

        // Shu o'quvchining BARCHA fikrlari (guruh bo'yicha guruhlanadi).
        var reviews = (await db.TeacherReviews.AsNoTracking()
                .Where(r => r.StudentId == studentId).ToListAsync())
            .OrderByDescending(r => r.CreatedAt, StringComparer.Ordinal)
            .ToList();

        var result = new List<StudentTeacherReviewGroupDto>();
        foreach (var m in memberships.DistinctBy(x => x.GroupId))
        {
            if (!groups.TryGetValue(m.GroupId, out var g)) continue;
            // O'qituvchisi biriktirilmagan guruh uchun fikr yozib bo'lmaydi — blok ham chiqmaydi.
            if (string.IsNullOrEmpty(g.TeacherId)) continue;

            result.Add(new StudentTeacherReviewGroupDto(
                g.Id, g.Name,
                string.IsNullOrEmpty(g.CourseId) ? "" : courseNames.GetValueOrDefault(g.CourseId, ""),
                g.TeacherId, teacherNames.GetValueOrDefault(g.TeacherId, ""),
                m.IsActive, m.Status ?? "",
                reviews.Where(r => r.GroupId == g.Id)
                    .Select(r => new TeacherReviewDto(
                        r.Id, r.TeacherId, r.GroupId, r.Text, r.CreatedAt, r.CreatedBy))
                    .ToList()));
        }

        return result
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.GroupName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// O'QITUVCHI PROFILIDAGI «Fikrlar» bo'limi: shu o'qituvchi haqida yozilgan BARCHA fikrlar,
    /// eng yangisi tepada, o'quvchi va guruh nomi bilan. Yozuvlar vaqt o'tgani sayin YIG'ILIB boradi.
    /// <para>Bu ADMIN ko'rinishi — o'quvchi ismi ko'rsatiladi. O'qituvchining o'ziga berilmaydi
    /// (o'qituvchi portalida/ilovasida bunday endpoint yo'q).</para>
    /// </summary>
    /// <param name="max">Ko'pi bilan nechta qator qaytadi (jami soni alohida sanaladi).</param>
    public static async Task<TeacherReviewFeedDto> ForTeacherAsync(
        IAppDbContext db, string teacherId, int max = 300)
    {
        var rows = await db.TeacherReviews.AsNoTracking()
            .Where(r => r.TeacherId == teacherId)
            .ToListAsync();
        if (rows.Count == 0) return new TeacherReviewFeedDto(0, new List<TeacherReviewFeedItemDto>());

        var ordered = rows
            .OrderByDescending(r => r.CreatedAt, StringComparer.Ordinal)
            .Take(max)
            .ToList();

        var studentIds = ordered.Select(r => r.StudentId).Distinct().ToList();
        var studentNames = await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName);

        var groupIds = ordered.Select(r => r.GroupId).Distinct().ToList();
        var groupNames = await db.Classes.AsNoTracking()
            .Where(g => groupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name);

        return new TeacherReviewFeedDto(
            rows.Count,
            ordered.Select(r => new TeacherReviewFeedItemDto(
                r.Id, r.StudentId, studentNames.GetValueOrDefault(r.StudentId, ""),
                r.GroupId, groupNames.GetValueOrDefault(r.GroupId, ""),
                r.Text, r.CreatedAt, r.CreatedBy)).ToList());
    }

    /// <summary>Yangi fikr yozadi. Xato bo'lsa (matn bo'sh / a'zolik yo'q / guruh o'qituvchisi
    /// boshqa) — xabar qaytadi va hech narsa saqlanmaydi.</summary>
    public static async Task<(TeacherReviewDto? Dto, string? Error)> AddAsync(
        IAppDbContext db, string studentId, string teacherId, string groupId, string? text,
        string createdBy, string? createdById)
    {
        var body = (text ?? "").Trim();
        if (body.Length == 0) return (null, "Fikr matnini yozing");
        if (body.Length > MaxTextLength)
            return (null, $"Matn juda uzun ({body.Length}) — {MaxTextLength} belgidan oshmasin");

        if (!await db.Students.AsNoTracking().AnyAsync(s => s.Id == studentId))
            return (null, "O'quvchi topilmadi");

        // Guruh va uning o'qituvchisi — fikr AYNAN shu guruh o'qituvchisi haqida bo'lishi kerak
        // (klientdan kelgan teacherId'ga ishonmaymiz).
        var group = await db.Classes.AsNoTracking()
            .Where(g => g.Id == groupId).Select(g => new { g.Id, g.TeacherId }).FirstOrDefaultAsync();
        if (group is null) return (null, "Guruh topilmadi");
        if (string.IsNullOrEmpty(group.TeacherId))
            return (null, "Bu guruhga o'qituvchi biriktirilmagan");
        if (!string.IsNullOrEmpty(teacherId) && teacherId != group.TeacherId)
            return (null, "Guruh o'qituvchisi o'zgargan — sahifani yangilang");

        // O'quvchi shu guruhda bo'lgan bo'lishi kerak (hozir bo'lmasa ham — tarix qoladi).
        if (!await db.StudentGroups.AsNoTracking()
                .AnyAsync(sg => sg.StudentId == studentId && sg.GroupId == groupId))
            return (null, "O'quvchi bu guruhda o'qimagan");

        var review = new TeacherReview
        {
            StudentId = studentId,
            TeacherId = group.TeacherId,
            GroupId = groupId,
            Text = body,
            CreatedAt = AppClock.Iso(),
            CreatedBy = createdBy,
            CreatedById = createdById,
        };
        db.TeacherReviews.Add(review);
        await db.SaveChangesAsync();

        return (new TeacherReviewDto(
            review.Id, review.TeacherId, review.GroupId, review.Text,
            review.CreatedAt, review.CreatedBy), null);
    }

    /// <summary>Fikrni o'chiradi (xato yozilgan bo'lsa). Qaytadi: topildimi.</summary>
    public static async Task<bool> DeleteAsync(IAppDbContext db, string id)
    {
        var r = await db.TeacherReviews.FirstOrDefaultAsync(x => x.Id == id);
        if (r is null) return false;
        db.TeacherReviews.Remove(r);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// AI TAHLIL uchun: o'qituvchi haqida yozilgan oxirgi fikr MATNLARI (eng yangisidan).
    /// <para>O'QUVCHI ISMI ATAYIN QO'SHILMAYDI — AI xulosasi o'qituvchiga ko'rsatiladi va u
    /// yerda "falonchi shunday dedi" chiqsa, o'quvchi bilan munosabat buzilardi. Faqat guruh
    /// nomi beriladi (qaysi guruhda muammo borligi ko'rinsin).</para>
    /// </summary>
    /// <param name="sinceIso">Shu vaqtdan keyingi fikrlar (ISO). Bo'sh — hammasi.</param>
    /// <param name="max">Ko'pi bilan nechta matn (prompt shishib ketmasin).</param>
    public static async Task<(int Count, List<string> Texts)> TextsForTeacherAsync(
        IAppDbContext db, string teacherId, string sinceIso, int max = 25,
        CancellationToken ct = default)
    {
        var rows = await db.TeacherReviews.AsNoTracking()
            .Where(r => r.TeacherId == teacherId
                        && (sinceIso.Length == 0 || string.Compare(r.CreatedAt, sinceIso) >= 0))
            .Select(r => new { r.GroupId, r.Text, r.CreatedAt })
            .ToListAsync(ct);
        if (rows.Count == 0) return (0, new List<string>());

        var groupIds = rows.Select(r => r.GroupId).Distinct().ToList();
        var groupNames = await db.Classes.AsNoTracking()
            .Where(g => groupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name, ct);

        var texts = rows
            .OrderByDescending(r => r.CreatedAt, StringComparer.Ordinal)
            .Take(max)
            .Select(r =>
            {
                var name = groupNames.GetValueOrDefault(r.GroupId, "");
                var when = r.CreatedAt.Length >= 10 ? r.CreatedAt[..10] : r.CreatedAt;
                var body = r.Text.Length > MaxTextForAi ? r.Text[..MaxTextForAi] + "…" : r.Text;
                return $"[{when}{(name.Length > 0 ? " · " + name : "")}] {body}";
            })
            .ToList();

        return (rows.Count, texts);
    }
}
