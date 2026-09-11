using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Bitta o'quvchining o'zlashtirish va qatnashish hisoboti: har fan bo'yicha chorak baholari +
/// chorak bo'yicha qoldirilgan kunlar/darslar va kech qolishlar. Admin hisobotlari ham,
/// o'quvchi (oila) ilovasi ham shu yagona mantiqdan foydalanadi.
/// </summary>
public static class StudentReportBuilder
{
    public static async Task<StudentReportDto> BuildAsync(IAppDbContext db, Student st)
    {
        // O'quvchining FAOL guruh(lar)i (M2M) bo'yicha — yo'q bo'lsa ClassName bo'yicha (orqaga moslik).
        // Faqat-o'qish hisobot generatori — barcha ro'yxatlar AsNoTracking (tracking overhead va
        // lug'atlarni (Subjects/AbsenceReasons) har chaqiruvda identity-map'ga yig'ishni oldini oladi).
        var memberships = await StudentMembershipView.LiveMembershipsAsync(db, st.Id);
        var memberGroupIds = memberships.Select(sg => sg.GroupId).Distinct().ToList();
        List<Group> groups;
        if (memberGroupIds.Count > 0)
            groups = await db.Classes.AsNoTracking().Where(c => memberGroupIds.Contains(c.Id)).ToListAsync();
        else
        {
            var byName = await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Name == st.ClassName);
            groups = byName is null ? new List<Group>() : new List<Group> { byName };
        }
        var classIds = groups.Select(g => g.Id).ToHashSet();
        var allSubjects = await db.Subjects.AsNoTracking().ToListAsync();
        var assignedSubjectIds = groups
            .Where(g => !string.IsNullOrEmpty(g.CourseId)).Select(g => g.CourseId).Distinct().ToList();
        var entries = (await db.JournalEntries.AsNoTracking().Where(e => e.StudentId == st.Id).ToListAsync())
            .Where(e => classIds.Count == 0 || classIds.Contains(e.ClassId)).ToList();
        var reasonRows = await db.AbsenceReasons.AsNoTracking().ToListAsync();
        var lateIds = reasonRows.Where(r => r.IsLate).Select(r => r.Id).ToHashSet();
        var reasons = reasonRows.ToDictionary(r => r.Id, r => r.Name.ToLowerInvariant());

        var subjectIds = assignedSubjectIds.Count > 0 ? assignedSubjectIds : allSubjects.Select(s => s.Id).ToList();
        var subjects = subjectIds
            .Select(id => allSubjects.FirstOrDefault(s => s.Id == id))
            .Where(s => s is not null)
            .Select(s => new SubjectDto(s!.Id, s.Name, s.Price))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var grades = new Dictionary<string, Dictionary<int, double>>();
        foreach (var subj in subjects)
        {
            var byQ = entries.Where(e => e.SubjectId == subj.Id && e.Grade != null)
                .GroupBy(e => e.Quarter)
                .ToDictionary(g => g.Key, g => Math.Round(g.Average(e => (double)e.Grade!.Value), 2));
            if (byQ.Count > 0) grades[subj.Id] = byQ;
        }

        var absences = entries.Where(e => e.ReasonId != null).ToList();
        bool IsLate(JournalEntry e) => lateIds.Contains(e.ReasonId!);
        bool IsIll(JournalEntry e) => reasons.TryGetValue(e.ReasonId!, out var n) && n.Contains("kasal");
        Dictionary<int, int> PerQ(Func<JournalEntry, bool> pred) =>
            absences.Where(pred).GroupBy(e => e.Quarter).ToDictionary(g => g.Key, g => g.Count());
        Dictionary<int, int> PerQDays(Func<JournalEntry, bool> pred) =>
            absences.Where(pred).GroupBy(e => e.Quarter)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Date).Distinct().Count());

        var attendance = new StudentAttendanceDto(
            PerQDays(e => !IsLate(e)), PerQDays(IsIll),
            PerQ(e => !IsLate(e)), PerQ(IsIll), PerQ(IsLate));

        // ASOSIY GURUH = A'ZOLIK bo'yicha tanlanadi (faol → sinov → muzlatilgan, keyin eng yangi
        // qo'shilgani) — `MembershipLifecycle.PrimaryMembership`.
        // ⚠️ Ilgari bu yerda `groups.FirstOrDefault(g => g.Name == st.ClassName)` turardi, ya'ni
        // BIRINCHI qo'shilgan guruh ("asosiy guruh" yorlig'i) g'olib chiqardi: o'quvchi eski
        // guruhida MUZLATILIB yangisida o'qiyotgan bo'lsa, hisobotda eski guruh nomi va eski
        // guruh RAHBARI ko'rinardi. `ClassName` endi faqat a'zolik UMUMAN bo'lmagan (juda eski)
        // yozuvlar uchun zaxira.
        var primaryMembership = MembershipLifecycle.PrimaryMembership(memberships);
        var primary = (primaryMembership is null
                          ? null
                          : groups.FirstOrDefault(g => g.Id == primaryMembership.GroupId))
                      ?? groups.FirstOrDefault();
        var homeroom = primary is null || string.IsNullOrEmpty(primary.TeacherId)
            ? ""
            : (await db.Teachers.FindAsync(primary.TeacherId))?.FullName ?? "";

        return new StudentReportDto(
            st.Id, st.FullName, primary?.Name ?? st.ClassName, homeroom,
            st.ParentFullName, subjects, grades, attendance);
    }
}
