using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// KARYERA (ishga qabul) moduli — YAGONA mantiq. Admin paneli (<c>CareerController</c>),
/// Mini App API (<c>PublicCareerController</c>) va karyera boti (<see cref="CareerBotService"/>)
/// shu servisdan foydalanadi: bosqichlar katalogi, ariza yaratish, bosqich o'zgartirish
/// (tarix + nomzodga bildirishnoma) va adminlarga xabarnoma.
/// </summary>
public class CareerService(CareerTelegramService careerBot, ILogger<CareerService> logger)
{
    /* =============================================================================================
     *  BOSQICHLAR — yagona katalog (backend, Mini App va admin paneli bir xil kalitlarni ishlatadi)
     * ============================================================================================= */

    public const string StatusNew = "new";
    public const string StatusReview = "review";
    public const string StatusInterview = "interview";
    public const string StatusTrial = "trial";
    public const string StatusHired = "hired";
    public const string StatusRejected = "rejected";

    /// <param name="Key">Bosqich kaliti (bazada shu saqlanadi).</param>
    /// <param name="Label">Admin panelidagi nomi.</param>
    /// <param name="CandidateText">Nomzod "Arizalarim"da ko'radigan matn.</param>
    /// <param name="Icon">Emoji (bot xabari va ilovada).</param>
    /// <param name="Order">Yo'l-xaritadagi tartib (rad etish — yakuniy, tartibi yo'q).</param>
    /// <param name="IsFinal">Yakuniy bosqichmi (keyin o'zgarmaydi degani emas — shunchaki natija).</param>
    public record Stage(string Key, string Label, string CandidateText, string Icon, int Order, bool IsFinal);

    /// <summary>Barcha bosqichlar — TARTIB bilan. "rejected" ro'yxat oxirida (yo'l-xaritaga kirmaydi).</summary>
    public static readonly IReadOnlyList<Stage> Stages =
    [
        new(StatusNew, "Yangi ariza", "Arizangiz qabul qilindi va navbatda turibdi.", "📥", 1, false),
        new(StatusReview, "Ko'rib chiqilmoqda", "Hujjatlaringiz mutaxassislar tomonidan o'rganilmoqda.", "🔍", 2, false),
        new(StatusInterview, "Suhbatga taklif", "Siz suhbatga taklif qilindingiz — tafsilotlar quyida.", "🗣", 3, false),
        new(StatusTrial, "Sinov bosqichi", "Sinov dars / amaliy topshiriq bosqichidasiz.", "🎯", 4, false),
        new(StatusHired, "Ishga qabul qilindi", "Tabriklaymiz! Siz jamoamizga qabul qilindingiz.", "✅", 5, true),
        new(StatusRejected, "Rad etildi", "Afsuski, bu safar sizning nomzodingiz tanlanmadi.", "❌", 99, true),
    ];

    public static Stage StageOf(string? key) =>
        Stages.FirstOrDefault(s => s.Key == key) ?? Stages[0];

    public static bool IsValidStatus(string? key) => Stages.Any(s => s.Key == key);

    /* =============================================================================================
     *  ARIZA YARATISH
     * ============================================================================================= */

    /// <summary>Keyingi ketma-ket ariza raqami (#1, #2 ...).</summary>
    public static async Task<int> NextNumberAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var max = await db.JobApplications.AsNoTracking()
            .Select(a => (int?)a.Number).MaxAsync(ct) ?? 0;
        return max + 1;
    }

    /// <summary>
    /// Yangi arizani yozadi (birinchi bosqich hodisasi bilan). SaveChanges CHAQIRILMAYDI —
    /// chaqiruvchi o'zi saqlaydi (bitta tranzaksiyada).
    /// </summary>
    public static JobApplication BuildApplication(
        Vacancy vacancy, long chatId, string tgUsername, string fullName, string phone,
        string experience, string motivation, string cvUrl, string cvName, int number)
    {
        var now = AppClock.Iso();
        return new JobApplication
        {
            Number = number,
            VacancyId = vacancy.Id,
            VacancyTitle = vacancy.Title,
            ChatId = chatId,
            TgUsername = tgUsername,
            FullName = fullName,
            Phone = phone,
            Experience = experience,
            Motivation = motivation,
            CvUrl = cvUrl,
            CvName = cvName,
            Status = StatusNew,
            StatusChangedAt = now,
            StatusChangedBy = "Nomzod",
            CreatedAt = now,
        };
    }

    /* =============================================================================================
     *  BOSQICHNI O'ZGARTIRISH
     * ============================================================================================= */

    /// <summary>
    /// Ariza bosqichini o'zgartiradi: yozuvni yangilaydi, tarixga hodisa qo'shadi va nomzodga
    /// KARYERA BOTI orqali xabar yuboradi. SaveChanges chaqiruvchida.
    /// </summary>
    /// <returns>Bosqich haqiqatan o'zgarganmi (bir xil bo'lsa ham izoh yangilanadi).</returns>
    public async Task<bool> SetStatusAsync(
        IAppDbContext db, JobApplication app, string status, string note, string actor,
        CancellationToken ct = default)
    {
        if (!IsValidStatus(status)) return false;
        var changed = app.Status != status;

        app.Status = status;
        app.StatusNote = (note ?? "").Trim();
        app.StatusChangedAt = AppClock.Iso();
        app.StatusChangedBy = actor;

        db.JobApplicationEvents.Add(new JobApplicationEvent
        {
            ApplicationId = app.Id,
            Status = status,
            Note = app.StatusNote,
            CreatedAt = app.StatusChangedAt,
            CreatedBy = actor,
        });

        await NotifyCandidateAsync(app, ct);
        return changed;
    }

    /// <summary>Nomzodga bosqich o'zgargani haqida karyera botida xabar (bot sozlanmagan bo'lsa jim o'tadi).</summary>
    public async Task NotifyCandidateAsync(JobApplication app, CancellationToken ct = default)
    {
        try
        {
            if (!careerBot.IsConfigured || app.ChatId == 0) return;
            var stage = StageOf(app.Status);
            var lines = new List<string>
            {
                $"{stage.Icon} <b>Arizangiz holati yangilandi</b>",
                "",
                $"💼 Vakansiya: <b>{Esc(app.VacancyTitle)}</b>",
                $"📌 Bosqich: <b>{Esc(stage.Label)}</b>",
                "",
                Esc(stage.CandidateText),
            };
            if (!string.IsNullOrWhiteSpace(app.StatusNote))
            {
                lines.Add("");
                lines.Add($"📝 {Esc(app.StatusNote)}");
            }
            await careerBot.SendMessageAsync(app.ChatId, string.Join("\n", lines), null, ct, "HTML");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Karyera: nomzodga bildirishnoma yuborilmadi (ariza {Id})", app.Id);
        }
    }

    /* =============================================================================================
     *  ADMINLARGA XABARNOMA (markazning ASOSIY boti orqali — adminlar o'sha yerda ro'yxatdan o'tgan)
     * ============================================================================================= */

    /// <summary>Yangi ariza tushganda superadminlarga va bot qo'shilgan faol guruhlarga xabar.
    /// <see cref="LeadNotifier"/> bilan bir xil oluvchi mantig'i. Hech qachon arizani buzmaydi.</summary>
    public static async Task NotifyAdminsAsync(
        IAppDbContext db, TelegramService telegram, JobApplication app, CancellationToken ct = default)
    {
        try
        {
            if (!telegram.IsConfigured) return;

            var regs = await db.TelegramRegistrations
                .Where(r => r.UserId != null && r.UserId != "").ToListAsync(ct);
            var groupChatIds = await db.TelegramGroups
                .Where(g => g.IsActive).Select(g => g.ChatId).ToListAsync(ct);
            if (regs.Count == 0 && groupChatIds.Count == 0) return;

            var userIds = regs.Select(r => r.UserId!).Distinct().ToList();
            var users = (await db.Users.Where(u => userIds.Contains(u.Id)).ToListAsync(ct))
                .ToDictionary(u => u.Id);

            var lines = new List<string>
            {
                $"🧑‍💼 Yangi ariza #{app.Number}",
                $"💼 Vakansiya: {app.VacancyTitle}",
                $"👤 {app.FullName}",
            };
            if (!string.IsNullOrWhiteSpace(app.Phone)) lines.Add($"📞 {app.Phone}");
            if (!string.IsNullOrWhiteSpace(app.TgUsername)) lines.Add($"✈️ @{app.TgUsername}");
            if (!string.IsNullOrWhiteSpace(app.CvUrl)) lines.Add("📎 CV biriktirilgan");
            lines.Add("");
            lines.Add("Boshqaruv → Vakansiyalar → Arizalar bo'limida ko'ring.");
            var text = string.Join("\n", lines);

            var sent = new HashSet<long>();
            foreach (var r in regs)
            {
                if (!users.TryGetValue(r.UserId!, out var u) || u.Role != Roles.SuperAdmin) continue;
                if (!sent.Add(r.ChatId)) continue;
                await telegram.SendMessageAsync(r.ChatId, text, ct: ct);
            }
            foreach (var gid in groupChatIds)
            {
                if (!sent.Add(gid)) continue;
                await telegram.SendMessageAsync(gid, text, ct: ct);
            }
        }
        catch
        {
            // Xabarnoma ariza qabul qilishni hech qachon buzmasligi kerak.
        }
    }

    /// <summary>Telegram HTML parse_mode uchun minimal ekranlash.</summary>
    public static string Esc(string? s) =>
        (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
