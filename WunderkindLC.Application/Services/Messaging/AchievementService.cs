using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>
/// STUDENT ACHIEVEMENTS (SPEC P2.6, U2c): IELTS / CEFR / certificates / olympiads in the student profile and the
/// cabinet, optionally congratulated through the <c>achievement</c> auto-message trigger (outbox; the achievement
/// photo is attached and overrides the rule's attachment). Audit type <c>StudentAchievement</c> (section students).
/// </summary>
public static class AchievementService
{
    public const string EntityType = "StudentAchievement";
    public static readonly string[] Kinds = { "ielts", "cefr", "certificate", "olympiad", "other" };

    public const string ErrArchived = "Arxivdagi o'quvchiga tabrik yuborilmaydi";
    public const string ErrNoRule = "«Yutuq» avto-xabar qoidasi yoqilmagan — Xabarlar → Avto xabarlar";

    public static string KindLabel(string? kind) => kind switch
    {
        "ielts" => "IELTS",
        "cefr" => "CEFR",
        "certificate" => "Sertifikat",
        "olympiad" => "Olimpiada",
        _ => "Boshqa",
    };

    /// <summary>Validation (Uzbek 400 reason or null). The attachment must be an existing <c>photo</c>.</summary>
    public static async Task<string?> ValidateAsync(IAppDbContext db, SaveAchievementRequest req, CancellationToken ct = default)
    {
        if (!Kinds.Contains(req.Kind ?? "")) return "Yutuq turini tanlang";
        var title = (req.Title ?? "").Trim();
        if (title.Length is 0 or > 200) return "Yutuq nomi 1–200 belgi bo'lsin";
        if ((req.Result ?? "").Trim().Length > 50) return "Natija 50 belgidan oshmasin";
        if ((req.Note ?? "").Trim().Length > 1000) return "Izoh 1000 belgidan oshmasin";
        if (!DateOnly.TryParseExact((req.Date ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return "Sana noto'g'ri (yyyy-MM-dd)";
        if (d > AppClock.Today) return "Sana kelajakda bo'lishi mumkin emas";
        if (!string.IsNullOrWhiteSpace(req.AttachmentId))
        {
            var kind = await db.MessageAttachments.AsNoTracking().Where(a => a.Id == req.AttachmentId)
                .Select(a => a.Kind).FirstOrDefaultAsync(ct);
            if (kind is null) return "Rasm topilmadi — qaytadan yuklang";
            if (kind != "photo") return "Yutuq uchun faqat rasm (jpg, png) biriktiriladi";
        }
        return null;
    }

    public static void Apply(StudentAchievement a, SaveAchievementRequest req)
    {
        a.Kind = req.Kind!;
        a.Title = (req.Title ?? "").Trim();
        a.Result = (req.Result ?? "").Trim();
        a.Date = (req.Date ?? "").Trim();
        a.Note = (req.Note ?? "").Trim();
        a.AttachmentId = string.IsNullOrWhiteSpace(req.AttachmentId) ? null : req.AttachmentId;
    }

    public static string Describe(StudentAchievement a, string studentName) =>
        $"{KindLabel(a.Kind)}: {a.Title}{(a.Result.Length > 0 ? " " + a.Result : "")} ({studentName})";

    /// <summary>
    /// Queues the congratulation (<c>achievement</c> trigger): extra {yutuq} {yutuq_natija} {sana}; eventKey
    /// <c>{id}:{n}</c> (n grows on every explicit resend — the outbox never repeats the same one); photo attached.
    /// Saves. Returns the number of Telegram messages queued, or an Uzbek error.
    /// </summary>
    public static async Task<(int Messages, string? Error)> QueueCongratulationAsync(
        IAppDbContext db, AutoMessageService autoMsg, StudentAchievement a, Student s, CancellationToken ct = default)
    {
        if (s.IsArchived) return (0, ErrArchived);
        if (!await db.AutoMessageRules.AnyAsync(r => r.Enabled && r.Trigger == AutoMessageTriggers.Achievement, ct))
            return (0, ErrNoRule);
        var d = DateOnly.TryParse(a.Date, CultureInfo.InvariantCulture, out var date) ? date : AppClock.Today;
        a.CongratulationCount++;
        a.CongratulatedAt = AppClock.Now;
        await db.SaveChangesAsync(ct);
        var n = await autoMsg.DispatchStudentAsync(db, AutoMessageTriggers.Achievement, s, new Dictionary<string, string>
        {
            ["{yutuq}"] = a.Title.Length > 0 ? a.Title : KindLabel(a.Kind),
            ["{yutuq_natija}"] = a.Result,
            ["{sana}"] = d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
        }, ct: ct, eventKey: $"{a.Id}:{a.CongratulationCount}", sourceId: a.Id, attachmentId: a.AttachmentId);
        return (n, null);
    }

    public static AchievementDto ToDto(StudentAchievement a, string studentName, int messages = 0) => new(
        a.Id, a.StudentId, studentName, a.Kind, KindLabel(a.Kind), a.Title, a.Result, a.Date, a.Note,
        a.AttachmentId is not null, a.CongratulatedAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
        a.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss"), a.CreatedByName, messages);
}
