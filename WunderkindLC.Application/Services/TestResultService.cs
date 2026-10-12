using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Test natijalari — guruh uchun o'tkazilgan testlar (nomi, sanasi, maksimal ball) va har o'quvchining
/// olgan bali. Admin ("O'quv bo'limi" → Testlar natijalari) va o'qituvchi ilovasi (profil → Testlar)
/// bir xil mantiqni ishlatadi (bu servis), faqat controllerlar KIRISH ruxsatini (admin = barcha guruh,
/// o'qituvchi = faqat o'z guruhi) alohida tekshiradi.
///
/// Ballar <see cref="TestScore"/> ichida per (test, o'quvchi) saqlanadi; tafsilotdagi qatorlar
/// guruhning FAOL a'zolari (<see cref="StudentGroup.IsActive"/>) bilan chapdan birlashtiriladi va
/// ball bo'yicha kamayish tartibida (ball kiritilmaganlar oxirida) saralanadi.
///
/// <para>2026-10: ONLAYN rejim (bot orqali PDF, javob kaliti, test kodi, markazdan tashqari
/// ishtirokchilar) olib tashlandi — test faqat OFLAYN: nom, sana, maksimal ball va o'qituvchi/admin
/// kiritadigan ballar. Eski onlayn testlarning qatorlari oddiy test bo'lib ko'rinadi (ballari joyida).</para>
/// </summary>
public static class TestResultService
{

    /// <summary>Testlar natijalari bosh sahifasi — barcha guruhlar + har biriga yaratilgan testlar soni.
    /// Faol (muzlatilmagan) a'zolar soni ham qaytadi. Arxivlanmagan guruhlar, nomi bo'yicha tartiblangan.</summary>
    public static async Task<List<TestGroupOverviewDto>> GroupsOverviewAsync(IAppDbContext db)
    {
        var groups = await db.Classes.AsNoTracking()
            .Where(g => !g.IsArchived)
            .OrderBy(g => g.Name)
            .ToListAsync();

        var courseNames = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name);
        var teacherNames = await db.Teachers.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.FullName);

        var testCounts = await db.TestResults.AsNoTracking()
            .GroupBy(t => t.GroupId)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GroupId, x => x.Count);

        // Chiqarib yuborilganlar (IsActive=false) va muzlatilganlar (Status="frozen") hisobga olinmaydi
        // (GradingService bilan bir xil qoida — baholash/test faqat haqiqiy faol a'zolarga tegishli).
        var memberCounts = await db.StudentGroups.AsNoTracking()
            .Where(sg => sg.IsActive && sg.Status != "frozen")
            .GroupBy(sg => sg.GroupId)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GroupId, x => x.Count);

        return groups.Select(g => new TestGroupOverviewDto(
            g.Id, g.Name,
            courseNames.GetValueOrDefault(g.CourseId, ""),
            g.TeacherId,
            teacherNames.GetValueOrDefault(g.TeacherId, ""),
            memberCounts.GetValueOrDefault(g.Id, 0),
            testCounts.GetValueOrDefault(g.Id, 0))).ToList();
    }

    /// <summary>Bitta guruhning testlar ro'yxati (sana desc → yangi test tepada), har testda
    /// faol o'quvchilar soni, ball kiritilganlar soni va o'rtacha ball.</summary>
    public static async Task<List<GroupTestDto>> ListForGroupAsync(IAppDbContext db, string groupId)
    {
        var tests = await db.TestResults.AsNoTracking()
            .Where(t => t.GroupId == groupId)
            .ToListAsync();
        if (tests.Count == 0) return new List<GroupTestDto>();

        var studentCount = await db.StudentGroups.AsNoTracking()
            .CountAsync(sg => sg.GroupId == groupId && sg.IsActive && sg.Status != "frozen");

        var testIds = tests.Select(t => t.Id).ToList();
        var scores = await db.TestScores.AsNoTracking()
            .Where(s => testIds.Contains(s.TestResultId))
            .ToListAsync();
        var byTest = scores.GroupBy(s => s.TestResultId)
            .ToDictionary(g => g.Key, g => g.ToList());
        // Berilgan sertifikatlar soni (ro'yxatda "N ta sertifikat" belgisi uchun).
        var certCounts = (await db.TestCertificates.AsNoTracking()
                .Where(c => testIds.Contains(c.TestResultId))
                .Select(c => c.TestResultId).ToListAsync())
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        return tests
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .Select(t =>
            {
                var s = byTest.GetValueOrDefault(t.Id) ?? new List<TestScore>();
                decimal? avg = s.Count > 0 ? Math.Round(s.Average(x => x.Score), 1) : null;
                return new GroupTestDto(t.Id, t.GroupId, t.Name, t.Date, t.MaxScore,
                    t.CreatedAt, t.CreatedBy, studentCount, s.Count, avg,
                    t.CertificateEnabled, t.CertificateTemplateId,
                    certCounts.GetValueOrDefault(t.Id, 0));
            })
            .ToList();
    }

    /// <summary>Yangi test yaratish. Nomi bo'sh yoki maksimal ball ≤ 0 bo'lsa xato (message qaytadi).</summary>
    public static async Task<(GroupTestDto? Dto, string? Error)> CreateAsync(
        IAppDbContext db, string groupId, string name, string date, decimal maxScore, string createdBy,
        bool certificateEnabled = false, string? certificateTemplateId = null)
    {
        var group = await db.Classes.FindAsync(groupId);
        if (group is null) return (null, "Guruh topilmadi");
        var testDate = string.IsNullOrWhiteSpace(date) ? AppClock.Today.ToString("yyyy-MM-dd") : date.Trim();

        var t = new TestResult
        {
            GroupId = groupId,
            Name = (name ?? "").Trim(),
            Date = testDate,
            MaxScore = maxScore,
            CreatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            CreatedBy = createdBy,
            CertificateEnabled = certificateEnabled,
            CertificateTemplateId = certificateEnabled ? (certificateTemplateId ?? "").Trim() : "",
        };
        var err = Validate(t.Name, t.MaxScore);
        if (err != null) return (null, err);

        db.TestResults.Add(t);
        await db.SaveChangesAsync();

        var studentCount = await db.StudentGroups.AsNoTracking()
            .CountAsync(sg => sg.GroupId == groupId && sg.IsActive);
        return (new GroupTestDto(t.Id, t.GroupId, t.Name, t.Date, t.MaxScore,
            t.CreatedAt, t.CreatedBy, studentCount, 0, null,
            t.CertificateEnabled, t.CertificateTemplateId, 0), null);
    }

    /// <summary>Test ma'lumotini tahrirlash (nomi/sana/maksimal ball + sertifikat sozlamasi).</summary>
    public static async Task<(bool Ok, string? Error)> UpdateAsync(
        IAppDbContext db, string id, string name, string date, decimal maxScore,
        bool certificateEnabled = false, string? certificateTemplateId = null)
    {
        var r = await UpdateDetailedAsync(db, id, name, date, maxScore, certificateEnabled, certificateTemplateId);
        return (r.Ok, r.Error);
    }

    /// <summary>Tahrirlash natijasi. <paramref name="Regraded"/> — bali O'ZGARGAN natijalar soni
    /// (maksimal ball kamayib qisilganlar) — chaqiruvchi shunga qarab sertifikatlarni qayta yaratadi.</summary>
    public sealed record TestUpdateResult(bool Ok, string? Error, int Regraded = 0);

    /// <summary>
    /// <see cref="UpdateAsync"/> ning to'liq shakli. Maksimal ball kamaysa qo'lda qo'yilgan ballar
    /// yangi maksimalga QISILADI (<see cref="ClampScore"/>). ⚠️ Ilgari eski ball qolardi va
    /// "9/5 = 180%" chiqardi (sertifikatda ham).
    /// </summary>
    public static async Task<TestUpdateResult> UpdateDetailedAsync(
        IAppDbContext db, string id, string name, string date, decimal maxScore,
        bool certificateEnabled = false, string? certificateTemplateId = null)
    {
        var t = await db.TestResults.FindAsync(id);
        if (t is null) return new(false, "Test topilmadi");

        t.Name = (name ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(date)) t.Date = date.Trim();
        t.MaxScore = maxScore;
        t.CertificateEnabled = certificateEnabled;
        t.CertificateTemplateId = certificateEnabled ? (certificateTemplateId ?? "").Trim() : "";
        var err = Validate(t.Name, t.MaxScore);
        if (err != null) return new(false, err);

        var regraded = 0;
        foreach (var sc in await db.TestScores.Where(x => x.TestResultId == t.Id).ToListAsync())
        {
            var next = ClampScore(sc.Score, t.MaxScore);
            if (next != sc.Score) { sc.Score = next; regraded++; }
        }

        await db.SaveChangesAsync();
        return new(true, null, regraded);
    }

    /// <summary>Ballni <c>[0, MaxScore]</c> oralig'iga qisadi (sof funksiya, testlangan).</summary>
    public static decimal ClampScore(decimal score, decimal maxScore) =>
        Math.Clamp(score, 0, Math.Max(0, maxScore));

    /// <summary>Test bo'yicha sertifikat berilganmi (qayta baholashdan keyin yangilash kerakmi).</summary>
    public static Task<bool> HasCertificatesAsync(IAppDbContext db, string testId) =>
        db.TestCertificates.AsNoTracking().AnyAsync(c => c.TestResultId == testId);

    /// <summary>Testni o'chirish (ballari FK CASCADE bilan o'chadi).</summary>
    public static async Task<bool> DeleteAsync(IAppDbContext db, string id)
    {
        var t = await db.TestResults.FindAsync(id);
        if (t is null) return false;
        db.TestResults.Remove(t);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Bitta testni topib GroupId'sini qaytaradi (ruxsat tekshiruvi uchun). null = topilmadi.</summary>
    public static async Task<string?> GroupIdOfAsync(IAppDbContext db, string testId) =>
        (await db.TestResults.AsNoTracking().FirstOrDefaultAsync(t => t.Id == testId))?.GroupId;

    /// <summary>Test tafsiloti — guruhning FAOL (chiqarib yuborilmagan, muzlatilmagan) o'quvchilari +
    /// ballari; ustiga bali bor, lekin hozir faol a'zo bo'lmagan o'quvchilar (<c>Member=false</c> —
    /// masalan guruhdan chiqqan). Ball desc bo'yicha, ball kiritilmaganlar oxirida (Rank=0).
    /// null = test topilmadi.</summary>
    public static async Task<TestResultDetailDto?> DetailAsync(IAppDbContext db, string id)
    {
        var t = await db.TestResults.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return null;
        var group = await db.Classes.AsNoTracking().FirstOrDefaultAsync(g => g.Id == t.GroupId);

        var members = await (from sg in db.StudentGroups.AsNoTracking()
                             join s in db.Students.AsNoTracking() on sg.StudentId equals s.Id
                             where sg.GroupId == t.GroupId && sg.IsActive && sg.Status != "frozen"
                             select new { s.Id, s.FullName }).ToListAsync();

        var scoreRows = await db.TestScores.AsNoTracking()
            .Where(s => s.TestResultId == id)
            .ToDictionaryAsync(s => s.StudentId, s => s);

        // Bali bor, lekin hozir faol a'zo bo'lmagan o'quvchilar (guruhdan chiqqan, eski onlayn testga
        // kod bilan kirgan) ham ro'yxatga kiradi — aks holda ballari hech qayerda ko'rinmasdi.
        var memberIds = members.Select(m => m.Id).ToHashSet();
        var guestIds = scoreRows.Keys.Where(sid => !memberIds.Contains(sid)).ToList();
        var guests = guestIds.Count == 0
            ? new List<(string Id, string FullName)>()
            : (await db.Students.AsNoTracking()
                    .Where(s => guestIds.Contains(s.Id))
                    .Select(s => new { s.Id, s.FullName }).ToListAsync())
                .Select(s => (s.Id, s.FullName)).ToList();

        var rows = members.Select(m => (m.Id, m.FullName, Member: true))
            .Concat(guests.Select(g => (g.Id, g.FullName, Member: false)))
            .Select(m => new
            {
                m.Id, m.FullName, m.Member,
                Score = scoreRows.TryGetValue(m.Id, out var sc) ? (decimal?)sc.Score : null,
            })
            // Ball kiritilganlar tepada (ball desc), keyin ism bo'yicha; kiritilmaganlar oxirida.
            .OrderByDescending(x => x.Score.HasValue)
            .ThenByDescending(x => x.Score ?? 0)
            .ThenBy(x => x.FullName)
            .ToList();

        // Standart musobaqa reytingi: ball TENG bo'lsa BIR XIL o'rin beriladi, keyingi (kichikroq)
        // ball esa nechta o'quvchi tepasida bo'lsa shuncha o'rinni "sakrab" o'tadi (masalan 1,1,3,4).
        var result = new List<TestScoreRowDto>();
        decimal? prevScore = null;
        var rank = 0;
        var position = 0;
        foreach (var x in rows)
        {
            if (!x.Score.HasValue)
            {
                result.Add(new TestScoreRowDto(x.Id, x.FullName, x.Score, 0, x.Member));
                continue;
            }
            position++;
            if (prevScore is null || x.Score.Value != prevScore.Value) rank = position;
            prevScore = x.Score.Value;
            result.Add(new TestScoreRowDto(x.Id, x.FullName, x.Score, rank, x.Member));
        }

        var certificates = await TestCertificateService.ListForTestAsync(db, id);
        return new TestResultDetailDto(t.Id, t.GroupId, group?.Name ?? "", t.Name, t.Date,
            t.MaxScore, t.CreatedAt, t.CreatedBy, result,
            t.CertificateEnabled, t.CertificateTemplateId, certificates);
    }

    /// <summary>Bitta o'quvchiga ball qo'yish/yangilash yoki tozalash (score=null). Ball 0..MaxScore
    /// oralig'iga qisiladi. Qaytadi: yangilangan tafsilot (qayta saralangan).</summary>
    public static async Task<(TestResultDetailDto? Detail, string? Error)> SetScoreAsync(
        IAppDbContext db, string testId, string studentId, decimal? score)
    {
        var t = await db.TestResults.FindAsync(testId);
        if (t is null) return (null, "Test topilmadi");

        var existing = await db.TestScores
            .FirstOrDefaultAsync(s => s.TestResultId == testId && s.StudentId == studentId);

        if (score is null)
        {
            if (existing is not null) db.TestScores.Remove(existing);
        }
        else
        {
            var val = Math.Clamp(score.Value, 0, t.MaxScore);
            if (existing is null)
                db.TestScores.Add(new TestScore { TestResultId = testId, StudentId = studentId, Score = val });
            else
                existing.Score = val;
        }
        await db.SaveChangesAsync();
        return (await DetailAsync(db, testId), null);
    }

    /// <summary>Bitta o'quvchining barcha test natijalari (guruhlaridan), sana desc. Har testda
    /// o'quvchining ballari orasidagi o'rni (Rank) va jami ball kiritilganlar soni (Total).</summary>
    public static async Task<List<StudentGroupTestDto>> StudentResultsAsync(IAppDbContext db, string studentId)
    {
        // O'quvchining ball kiritilgan testlari.
        var myScores = await db.TestScores.AsNoTracking()
            .Where(s => s.StudentId == studentId)
            .ToListAsync();
        if (myScores.Count == 0) return new List<StudentGroupTestDto>();

        var testIds = myScores.Select(s => s.TestResultId).Distinct().ToList();
        var tests = await db.TestResults.AsNoTracking()
            .Where(t => testIds.Contains(t.Id))
            .ToListAsync();
        var groupNames = await db.Classes.AsNoTracking()
            .Where(g => tests.Select(t => t.GroupId).Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name);

        // Shu testlardagi BARCHA ballar (rank/total hisoblash uchun).
        var allScores = await db.TestScores.AsNoTracking()
            .Where(s => testIds.Contains(s.TestResultId))
            .ToListAsync();
        var byTest = allScores.GroupBy(s => s.TestResultId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Score).ToList());

        var myByTest = myScores.ToDictionary(s => s.TestResultId, s => s.Score);

        return tests
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .Select(t =>
            {
                var ordered = byTest.GetValueOrDefault(t.Id) ?? new List<TestScore>();
                var myScore = myByTest.GetValueOrDefault(t.Id);
                // Standart musobaqa reytingi: mendan KATTA ball olganlar soni + 1 — ball teng bo'lganlar
                // bir xil o'rinda turadi (DetailAsync bilan bir xil mantiq).
                int rank = ordered.Count(x => x.Score > myScore) + 1;
                return new StudentGroupTestDto(t.Id, t.GroupId,
                    groupNames.GetValueOrDefault(t.GroupId, ""), t.Name, t.Date,
                    t.MaxScore, myScore, rank, ordered.Count);
            })
            .ToList();
    }

    private static string? Validate(string name, decimal maxScore)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Test nomi kerak";
        if (maxScore <= 0) return "Maksimal ball 0 dan katta bo'lishi kerak";
        return null;
    }
}
