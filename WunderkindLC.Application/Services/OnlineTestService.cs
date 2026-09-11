using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// ONLAYN TEST — o'quvchi ilovasi (portal) oqimi. Telegram bot bilan AYNAN BIR XIL qoidalar:
/// o'quvchining FAOL (muzlatilmagan) a'zoligi bor guruhlaridagi <c>Mode="online"</c> testlar,
/// vaqt oynasi (<see cref="TestResult.StartAt"/>..<see cref="TestResult.EndAt"/>) ichida bir marta
/// topshiriladi, ball <see cref="TestScore"/>ga yoziladi — ya'ni natija "Testlar natijalari" va
/// reyting bilan BIR JOYDA (alohida jadval yaratilmaydi).
///
/// <para>Farq faqat kanalda: bot <c>Source="bot"</c>, ilova <c>Source="app"</c> yozadi. Ikkalasi ham
/// "o'quvchi o'zi topshirgan" hisoblanadi — biri orqali topshirilgan bo'lsa, ikkinchisidan
/// qayta topshirib bo'lmaydi.</para>
///
/// <para>Javob kaliti test vaqti TUGAGUNCHA berilmaydi (birinchi topshirgan kalitni tarqatmasin).</para>
/// </summary>
public static class OnlineTestService
{
    /// <summary>Ilova orqali topshirilgan javoblar manbasi.</summary>
    public const string SourceApp = "app";

    /// <summary>O'quvchi topshirgan deb hisoblanadigan manbalar (bot yoki ilova).
    /// Bo'sh manba — o'qituvchi qo'lda kiritgan ball, u qayta topshirishni bloklamaydi.</summary>
    public static bool IsStudentSubmission(string? source) => source is "bot" or SourceApp;

    private static string NowStamp() => AppClock.Now.ToString("yyyy-MM-ddTHH:mm");
    private static string StartOf(TestResult t) => t.StartAt.Length >= 16 ? t.StartAt[..16] : t.Date + "T00:00";
    private static string EndOf(TestResult t) => t.EndAt.Length >= 16 ? t.EndAt[..16] : t.Date + "T23:59";

    /// <summary>Test hozir tugaganmi (javob kaliti/tahlil ochiladigan holat).</summary>
    public static bool IsFinished(TestResult t) => string.CompareOrdinal(NowStamp(), EndOf(t)) > 0;

    private static string StateOf(TestResult t, bool submitted)
    {
        if (submitted) return "submitted";
        var now = NowStamp();
        if (string.CompareOrdinal(now, StartOf(t)) < 0) return "upcoming";
        if (string.CompareOrdinal(now, EndOf(t)) > 0) return "closed";
        return "open";
    }

    /// <summary>O'quvchining faol (muzlatilmagan) guruh id'lari — bot bilan bir xil filtr.</summary>
    private static async Task<List<string>> GroupIdsAsync(IAppDbContext db, string studentId) =>
        await db.StudentGroups.AsNoTracking()
            .Where(sg => sg.StudentId == studentId && sg.IsActive && sg.Status != "frozen")
            .Select(sg => sg.GroupId)
            .Distinct()
            .ToListAsync();

    /// <summary>O'quvchiga ko'rinadigan onlayn testlar: hozirgi/kelgusi testlar va oxirgi 7 kun
    /// ichida tugaganlar (bot ro'yxati bilan bir xil oyna), yangisi tepada.</summary>
    public static async Task<List<StudentOnlineTestDto>> ListForStudentAsync(IAppDbContext db, string studentId)
    {
        var groupIds = await GroupIdsAsync(db, studentId);
        if (groupIds.Count == 0) return new List<StudentOnlineTestDto>();

        // GroupOpen=false — "FAQAT ONLAYN" test: guruhga e'lon qilinmaydi, faqat bot orqali TEST KODI
        // bilan ishlanadi (bot ro'yxati bilan bir xil qoida).
        var tests = await db.TestResults.AsNoTracking()
            .Where(t => t.Mode == "online" && t.GroupOpen && groupIds.Contains(t.GroupId))
            .ToListAsync();
        if (tests.Count == 0) return new List<StudentOnlineTestDto>();

        var cutoff = AppClock.Now.AddDays(-7).ToString("yyyy-MM-ddTHH:mm");
        tests = tests.Where(t => string.CompareOrdinal(EndOf(t), cutoff) >= 0).ToList();
        if (tests.Count == 0) return new List<StudentOnlineTestDto>();

        var testIds = tests.Select(t => t.Id).ToList();
        var myScores = await db.TestScores.AsNoTracking()
            .Where(s => testIds.Contains(s.TestResultId) && s.StudentId == studentId)
            .ToListAsync();
        var byTest = myScores.ToDictionary(s => s.TestResultId);

        var groupNames = await db.Classes.AsNoTracking()
            .Where(g => groupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name);

        return tests
            .OrderByDescending(t => t.Date, StringComparer.Ordinal)
            .ThenByDescending(t => t.CreatedAt, StringComparer.Ordinal)
            .Select(t =>
            {
                var mine = byTest.GetValueOrDefault(t.Id);
                var submitted = mine is not null && IsStudentSubmission(mine.Source);
                return new StudentOnlineTestDto(
                    t.Id, t.GroupId, groupNames.GetValueOrDefault(t.GroupId, ""), t.Name, t.Date,
                    t.QuestionCount, t.OptionCount, StartOf(t), EndOf(t),
                    t.PdfUrl, t.PdfName, StateOf(t, submitted),
                    mine?.Score, mine?.Answers ?? "", mine?.SubmittedAt ?? "");
            })
            .ToList();
    }

    /// <summary>Bitta onlayn test tafsiloti. Test o'quvchining guruhiga tegishli bo'lmasa — null.</summary>
    public static async Task<StudentOnlineTestDetailDto?> DetailAsync(
        IAppDbContext db, string studentId, string testId)
    {
        var t = await db.TestResults.AsNoTracking().FirstOrDefaultAsync(x => x.Id == testId);
        if (t is null || t.Mode != "online") return null;
        // "Faqat onlayn" (kod bilan) test ilovada ochilmaydi — u faqat botda, kod orqali ishlanadi.
        if (!t.GroupOpen) return null;

        var groupIds = await GroupIdsAsync(db, studentId);
        if (!groupIds.Contains(t.GroupId)) return null;

        var all = await db.TestScores.AsNoTracking()
            .Where(s => s.TestResultId == t.Id)
            .Select(s => new { s.StudentId, s.Score, s.Answers, s.SubmittedAt, s.Source })
            .ToListAsync();
        var mine = all.FirstOrDefault(s => s.StudentId == studentId);
        var submitted = mine is not null && IsStudentSubmission(mine.Source);
        var rank = mine is null ? 0 : all.Count(x => x.Score > mine.Score) + 1;

        var groupName = await db.Classes.AsNoTracking()
            .Where(g => g.Id == t.GroupId).Select(g => g.Name).FirstOrDefaultAsync() ?? "";

        return new StudentOnlineTestDetailDto(
            t.Id, t.GroupId, groupName, t.Name, t.Date,
            t.QuestionCount, t.OptionCount, StartOf(t), EndOf(t),
            t.PdfUrl, t.PdfName, StateOf(t, submitted),
            mine?.Score, mine?.Answers ?? "", mine?.SubmittedAt ?? "",
            IsFinished(t) ? t.AnswerKey : "", rank, all.Count);
    }

    /// <summary>Javoblarni qabul qiladi va avtomatik tekshiradi.
    /// Xato bo'lsa <c>Error</c> to'ldiriladi (foydalanuvchiga ko'rsatiladigan matn).</summary>
    public static async Task<(StudentOnlineTestDetailDto? Result, string? Error)> SubmitAsync(
        IAppDbContext db, string studentId, string testId, string rawAnswers)
    {
        var t = await db.TestResults.AsNoTracking().FirstOrDefaultAsync(x => x.Id == testId);
        if (t is null || t.Mode != "online") return (null, "Test topilmadi");
        if (!t.GroupOpen)
            return (null, "Bu test faqat TEST KODI bilan, Telegram bot orqali ishlanadi");

        var groupIds = await GroupIdsAsync(db, studentId);
        if (!groupIds.Contains(t.GroupId)) return (null, "Bu test sizning guruhingizga tegishli emas");

        var now = NowStamp();
        if (string.CompareOrdinal(now, StartOf(t)) < 0)
            return (null, "Test hali boshlanmagan");
        if (string.CompareOrdinal(now, EndOf(t)) > 0)
            return (null, "Test vaqti tugagan — javoblar qabul qilinmadi");

        // Bot bilan bir xil qoida: o'quvchi bir marta topshiradi (bot yoki ilova — farqi yo'q).
        var existing = await db.TestScores
            .FirstOrDefaultAsync(s => s.TestResultId == t.Id && s.StudentId == studentId);
        if (existing is not null && IsStudentSubmission(existing.Source))
            return (null, "Siz bu testni allaqachon topshirgansiz");

        var answers = Normalize(rawAnswers ?? "", t.QuestionCount, t.OptionCount);
        if (answers.All(ch => ch == '-'))
            return (null, "Hech bo'lmasa bitta javob belgilang");

        var correct = OnlineTestBotService.CountCorrect(answers, t.AnswerKey);
        var submittedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");

        if (existing is null)
            db.TestScores.Add(new TestScore
            {
                TestResultId = t.Id,
                StudentId = studentId,
                Score = correct,
                Answers = answers,
                SubmittedAt = submittedAt,
                Source = SourceApp,
            });
        else
        {
            existing.Score = correct;
            existing.Answers = answers;
            existing.SubmittedAt = submittedAt;
            existing.Source = SourceApp;
        }
        await db.SaveChangesAsync();

        return (await DetailAsync(db, studentId, testId), null);
    }

    /// <summary>
    /// Ilovadan kelgan javoblarni POZITSIYA bo'yicha normallashtiradi: har belgi bitta savol,
    /// javobsiz savol — <c>'-'</c>. Kirill harflari lotinga o'giriladi, variantlar sonidan
    /// tashqaridagi yoki notanish belgilar javobsiz deb olinadi; uzunlik savollar soniga tenglashadi.
    ///
    /// <para>DIQQAT: <see cref="OnlineTestBotService.ParseAnswers"/> dan farqli — u erkin matndan
    /// (masalan "a b c d") FAQAT harflarni yig'adi va bo'shliqni TASHLAB YUBORADI. Bu yerda esa
    /// javobsiz savol o'z o'rnini saqlashi SHART, aks holda javoblar siljib ketadi.</para>
    /// </summary>
    public static string Normalize(string raw, int questionCount, int optionCount)
    {
        if (questionCount <= 0) return "";
        var max = (char)('A' + Math.Clamp(optionCount, 2, 6) - 1);
        var sb = new System.Text.StringBuilder(questionCount);
        foreach (var r in raw.ToUpperInvariant())
        {
            if (sb.Length >= questionCount) break;
            var ch = r switch
            {
                'А' => 'A', 'В' => 'B', 'С' => 'C', 'Е' => 'E', 'Д' => 'D', 'Ф' => 'F',
                _ => r,
            };
            sb.Append(ch >= 'A' && ch <= max ? ch : '-');
        }
        return sb.ToString().PadRight(questionCount, '-');
    }
}
