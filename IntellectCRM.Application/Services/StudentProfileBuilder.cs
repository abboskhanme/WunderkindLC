using Microsoft.EntityFrameworkCore;
using IntellectCRM.Application.Abstractions;
using IntellectCRM.Application.Dtos;
using IntellectCRM.Domain;

namespace IntellectCRM.Application.Services;

/// <summary>
/// O'quvchi shaxsiy daftari — bitta o'quvchi haqida BARCHA ma'lumotni jamlaydi:
/// profil, o'zlashtirish (<see cref="StudentReportBuilder"/>), qatnashish (<see cref="Analytics"/>
/// mantig'i), davomat sabablari va jurnaldagi uy vazifa/xulq belgilari.
/// </summary>
public static class StudentProfileBuilder
{
    public static async Task<StudentNotebookDto> BuildAsync(IAppDbContext db, Student st)
    {
        // O'quvchining TIRIK guruh a'zoliklari (M2M) — a'zolik umuman bo'lmasa ClassName zaxirasi
        // (StudentReportBuilder bilan bir xil mantiq).
        var live = await StudentMembershipView.LiveMembershipsAsync(db, st.Id);
        // ⚠️ Tirik a'zolik qolmagan (hamma guruhdan chiqib ketgan) o'quvchida O'TGAN a'zoliklar
        // olinadi — ular bilan CHEGARA (`LeftAt`) ham keladi. Ilgari bunday holatda ClassName
        // guruhiga tushilar va a'zolik oynasi UMUMAN bo'lmasdi: o'quvchi ketgandan KEYINGI barcha
        // darslar ham uning "o'tilgan darslari" bo'lib sanalib, davomat foizi cheksiz pasayardi.
        var memberships = live.Count > 0
            ? live
            : await db.StudentGroups.AsNoTracking().Where(sg => sg.StudentId == st.Id).ToListAsync();
        var cls = memberships.Count > 0
            ? null
            : await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Name == st.ClassName);
        var classIds = memberships.Count > 0
            ? memberships.Select(m => m.GroupId).ToHashSet()
            : (cls is null ? new HashSet<string>() : new HashSet<string> { cls.Id });
        // Har guruh uchun a'zolik oynasi (memberStart..frozenAt) — jurnal (JournalService.GroupMonthAsync)
        // va o'quvchi portali (StudentAttendanceController) bilan BIR XIL chegara: guruhga qo'shilishidan
        // oldingi va muzlatilgandan keyingi o'tilgan darslar bu o'quvchiga "davomat" hisobiga kirmaydi.
        // (Bir o'quvchiga bir guruhda IKKINCHI a'zolik qatori BO'LMAYDI — bazada unikal indeks
        // (StudentId, GroupId); qayta qo'shilganda mavjud qator tiklanadi. Shu sabab bu yerda
        // guruh bo'yicha to'g'ridan-to'g'ri lug'at tuzish xavfsiz.)
        var boundsByClass = memberships.ToDictionary(
            m => m.GroupId,
            m => (Start: JournalService.MemberStart(m),
                  End: m.Status == "frozen" && m.FrozenAt is { Length: >= 10 } ? m.FrozenAt[..10]
                       // Guruhdan CHIQIB KETGAN a'zolikda chegara — chiqqan sana.
                       : !m.IsActive && m.LeftAt is { Length: >= 10 } ? m.LeftAt[..10]
                       : null));
        bool InMemberWindow(string classId, string date)
        {
            if (!boundsByClass.TryGetValue(classId, out var b)) return true;
            if (b.Start is not null && string.CompareOrdinal(date, b.Start) < 0) return false;
            if (b.End is not null && string.CompareOrdinal(date, b.End) > 0) return false;
            return true;
        }
        var report = await StudentReportBuilder.BuildAsync(db, st);
        // Bu builder faqat-o'qish (hisobot generatori) — barcha ro'yxatlar AsNoTracking.
        // (Lug'atlar — AbsenceReasons/Subjects — har chaqiruvda yuklanadi; ReferenceCache'ni ulash
        // static builder imzosini KO'P chaqiruv joyida o'zgartirishni talab qiladi, shu sabab hozircha
        // AsNoTracking bilan cheklanamiz — tracking overhead va identity-map yig'ilishini oldini oladi.)
        var entries = await db.JournalEntries.AsNoTracking().Where(e => e.StudentId == st.Id).ToListAsync();
        var reasons = await db.AbsenceReasons.AsNoTracking().ToListAsync();
        var reasonMap = reasons.ToDictionary(r => r.Id);
        var lateSet = reasons.Where(r => r.IsLate).Select(r => r.Id).ToHashSet();

        // ---- Qatnashish (o'tilgan / qatnashgan) — o'quvchining faol guruh(lar)i bo'yicha, FAQAT
        // a'zolik oynasi ichidagi darslar (jurnaldagi "Davomat" tabi bilan mos kelishi uchun) ----
        var studentConducted = classIds.Count == 0
            ? new HashSet<(string ClassId, string SubjectId, string Date, int Period)>()
            : (await db.LessonNotes.AsNoTracking().Where(n => n.Conducted && classIds.Contains(n.ClassId))
                    .Select(n => new { n.ClassId, n.SubjectId, n.Date, n.Period }).ToListAsync())
                .Where(n => InMemberWindow(n.ClassId, n.Date))
                .Select(n => (n.ClassId, n.SubjectId, n.Date, n.Period)).ToHashSet();
        var conducted = studentConducted.Count;
        var absent = entries.Count(e => classIds.Contains(e.ClassId) && e.ReasonId != null && !lateSet.Contains(e.ReasonId)
            && studentConducted.Contains((e.ClassId, e.SubjectId, e.Date, e.Period)));
        var attended = Math.Max(0, conducted - absent);
        var pct = conducted > 0 ? (int)Math.Round((double)attended / conducted * 100) : 0;

        // ---- Davomat sabablari taqsimoti (barcha belgilar) ----
        var reasonCounts = entries
            .Where(e => e.ReasonId != null && reasonMap.ContainsKey(e.ReasonId))
            .GroupBy(e => e.ReasonId!)
            .Select(g =>
            {
                var r = reasonMap[g.Key];
                return new AttendanceReasonCountDto(r.Id, r.Name, r.Short, r.IsLate, g.Count());
            })
            .OrderByDescending(x => x.Count).ToList();

        // ---- O'zlashtirish: fan → OY ("yyyy-MM") → o'rtacha baho (daftar oyma-oy) ----
        var gradesByMonth = new Dictionary<string, Dictionary<string, double>>();
        foreach (var subj in report.Subjects)
        {
            var byM = entries.Where(e => e.SubjectId == subj.Id && e.Grade != null && e.Date.Length >= 7)
                .GroupBy(e => e.Date[..7])
                .ToDictionary(g => g.Key, g => Math.Round(g.Average(e => (double)e.Grade!.Value), 2));
            if (byM.Count > 0) gradesByMonth[subj.Id] = byM;
        }

        // ---- Davomat oyma-oy: har metrika OY → son ----
        var illSet = reasons.Where(r => !r.IsLate && r.Name.ToLowerInvariant().Contains("kasal"))
            .Select(r => r.Id).ToHashSet();
        var absencesM = entries.Where(e => e.ReasonId != null && e.Date.Length >= 7).ToList();
        bool IsLateE(JournalEntry e) => lateSet.Contains(e.ReasonId!);
        bool IsIllE(JournalEntry e) => illSet.Contains(e.ReasonId!);
        Dictionary<string, int> PerM(Func<JournalEntry, bool> pred) =>
            absencesM.Where(pred).GroupBy(e => e.Date[..7]).ToDictionary(g => g.Key, g => g.Count());
        Dictionary<string, int> PerMDays(Func<JournalEntry, bool> pred) =>
            absencesM.Where(pred).GroupBy(e => e.Date[..7])
                .ToDictionary(g => g.Key, g => g.Select(e => e.Date).Distinct().Count());
        var monthlyAttendance = new MonthlyAttendanceDto(
            PerMDays(e => !IsLateE(e)), PerMDays(IsIllE),
            PerM(e => !IsLateE(e)), PerM(IsIllE), PerM(IsLateE));

        // ---- Uy vazifa + xulq (jami + OYMA-OY trend) ----
        // Homework: 1=qildi, 2=qilmadi, 3=chala qildi (statistikada "qildi" hisobiga kiradi —
        // bajarishga urinilgan; alohida sanoq DTO'larni og'irlashtirardi).
        var hwDone = entries.Count(e => e.Homework is 1 or 3);
        var hwMissed = entries.Count(e => e.Homework == 2);
        var bGood = entries.Count(e => e.Behavior == 1);
        var bBad = entries.Count(e => e.Behavior == 2);
        var markByMonth = entries.Where(e => (e.Homework != 0 || e.Behavior != 0) && e.Date.Length >= 7)
            .GroupBy(e => e.Date[..7]).ToDictionary(g => g.Key, g => g.ToList());
        var trend = markByMonth.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(m =>
        {
            var list = markByMonth[m];
            return new MonthMarksDto(m,
                list.Count(e => e.Homework is 1 or 3), list.Count(e => e.Homework == 2),
                list.Count(e => e.Behavior == 1), list.Count(e => e.Behavior == 2));
        }).ToList();

        // ---- O'rtacha baho (barcha fan/oy baholarining o'rtachasi) ----
        var allGradeVals = gradesByMonth.Values.SelectMany(d => d.Values).ToList();
        var avgGrade = allGradeVals.Count > 0 ? Math.Round(allGradeVals.Average(), 1) : 0;

        // Guruh nomi — `report.ClassName` (TIRIK a'zolikdan tanlangan asosiy guruh), `st.ClassName`
        // EMAS: eski yorliq birinchi qo'shilgan (masalan muzlatilgan) guruhda qotib qolgan bo'lishi
        // mumkin. Zaxira tarmog'i `StudentReportBuilder` ichida.
        return new StudentNotebookDto(
            st.Id, st.FullName, report.ClassName, report.HomeroomTeacher,
            st.ParentFullName, st.ParentPhone, st.Gender, st.BirthDate,
            st.EnrollmentDate, st.Balance, st.BirthCertificateUrl,
            st.Address, st.DiscountPct, st.DiscountAmount, st.DiscountNote,
            st.ParentPassportUrl,
            report.Subjects, gradesByMonth, avgGrade,
            monthlyAttendance, conducted, attended, pct, reasonCounts,
            hwDone, hwMissed, bGood, bBad, trend);
    }
}
