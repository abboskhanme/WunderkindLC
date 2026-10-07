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
    /// <summary>ESKI bosqich ("Ko'rib chiqilmoqda") — yangi voronkada YO'Q, faqat mavjud arizalar
    /// uchun saqlangan (<see cref="Stage.IsLegacy"/>). Ma'nosi "CV ko'rib chiqilmoqda", shuning
    /// uchun yo'l-xaritada <see cref="StatusNew"/> bilan BIR o'rinda turadi (tartibi bir xil).</summary>
    public const string StatusReview = "review";
    public const string StatusTest = "test";
    /// <summary>"Demo dars" — eski "Sinov bosqichi" kaliti (ma'nosi bir xil: sinov/namunaviy dars).</summary>
    public const string StatusTrial = "trial";
    /// <summary>"HR suhbati" — eski "Suhbatga taklif" kaliti (birinchi suhbat — HR bilan).</summary>
    public const string StatusInterview = "interview";
    /// <summary>"Rahbar (direktor) suhbati" — yangi bosqich.</summary>
    public const string StatusDirector = "director";
    public const string StatusHired = "hired";
    public const string StatusRejected = "rejected";

    /// <param name="Key">Bosqich kaliti (bazada shu saqlanadi).</param>
    /// <param name="Label">Admin panelidagi nomi.</param>
    /// <param name="CandidateText">Nomzod "Arizalarim"da va bot xabarida ko'radigan matn.</param>
    /// <param name="Icon">Emoji (bot xabari va ilovada).</param>
    /// <param name="Order">Yo'l-xaritadagi tartib (rad etish — yakuniy, tartibi 99).</param>
    /// <param name="IsFinal">Yakuniy bosqichmi (keyin o'zgarmaydi degani emas — shunchaki natija).</param>
    /// <param name="IsLegacy">ESKI bosqich: mavjud arizalar uchun haqiqiy (ko'rsatiladi, undan
    /// chiqib ketish mumkin), lekin YANGI o'tish uchun taklif qilinmaydi va qabul qilinmaydi
    /// (<see cref="CanMoveTo"/>). Voronkada o'zi bilan bir xil tartibli asosiy bosqichga qo'shiladi.</param>
    public record Stage(string Key, string Label, string CandidateText, string Icon, int Order, bool IsFinal,
        bool IsLegacy = false);

    /// <summary>
    /// Barcha bosqichlar — TARTIB bilan. Voronka (2026-10 yig'ilishi):
    /// <b>CV qabul qilindi → Test → Demo dars → HR suhbati → Rahbar (direktor) suhbati → Ishga
    /// qabul qilindi</b>, yakuniy natija — "Rad etildi" (yo'l-xaritaga kirmaydi).
    ///
    /// <para>⚠️ ESKI KALITLAR SAQLANGAN (bazadagi arizalar "noma'lum bosqich" bo'lib qolmasin):
    /// <c>new</c> = CV, <c>trial</c> = Demo dars, <c>interview</c> = HR suhbati, <c>hired</c>.
    /// Yangilari — <c>test</c>, <c>director</c>. <c>review</c> — ESKI (legacy): ko'rsatiladi,
    /// lekin yangi o'tish uchun yopiq. Eski tartibda <c>interview</c> <c>trial</c> dan OLDIN edi —
    /// endi voronka bo'yicha keyin (tartib bazada saqlanmaydi, faqat shu katalogda).</para>
    /// </summary>
    public static readonly IReadOnlyList<Stage> Stages =
    [
        new(StatusNew, "CV qabul qilindi",
            "CV'ingiz qabul qilindi va ko'rib chiqilmoqda. Keyingi bosqich haqida shu yerda xabar beramiz.",
            "📥", 1, false),
        new(StatusReview, "Ko'rib chiqilmoqda (eski)",
            "Hujjatlaringiz mutaxassislar tomonidan o'rganilmoqda.", "🔍", 1, false, IsLegacy: true),
        new(StatusTest, "Test",
            "Siz test bosqichiga o'tdingiz. Test vaqti va tafsilotlari quyidagi izohda.", "📝", 2, false),
        new(StatusTrial, "Demo dars",
            "Siz demo dars bosqichiga o'tdingiz. Dars mavzusi, sanasi va vaqti quyidagi izohda.", "🎯", 3, false),
        new(StatusInterview, "HR suhbati",
            "Sizni HR suhbatiga taklif qilamiz. Suhbat vaqti va joyi quyidagi izohda.", "🗣", 4, false),
        new(StatusDirector, "Rahbar (direktor) suhbati",
            "Oxirgi qadam — markaz rahbari (direktor) bilan suhbat. Vaqti quyidagi izohda.", "👔", 5, false),
        new(StatusHired, "Ishga qabul qilindi",
            "Tabriklaymiz! Siz jamoamizga qabul qilindingiz. Birinchi 30 kunlik moslashuv rejasi " +
            "bo'yicha HR bo'limi siz bilan bog'lanadi.", "✅", 6, true),
        new(StatusRejected, "Rad etildi", "Afsuski, bu safar sizning nomzodingiz tanlanmadi.", "❌", 99, true),
    ];

    public static Stage StageOf(string? key) =>
        Stages.FirstOrDefault(s => s.Key == key) ?? Stages[0];

    public static bool IsValidStatus(string? key) => Stages.Any(s => s.Key == key);

    /// <summary>
    /// Arizani <paramref name="target"/> bosqichiga o'tkazish mumkinmi. ESKI (legacy) bosqichga
    /// faqat ariza ALLAQACHON o'sha bosqichda bo'lsa (izohni yangilash) — yangi o'tish yopiq.
    /// </summary>
    public static bool CanMoveTo(string? current, string? target)
    {
        var stage = Stages.FirstOrDefault(s => s.Key == target);
        if (stage is null) return false;
        return !stage.IsLegacy || current == target;
    }

    /// <summary>Asosiy voronka bosqichlari (tartib bilan): eski va "rad etildi" kirmaydi.</summary>
    public static IReadOnlyList<Stage> FunnelStages =>
        Stages.Where(s => !s.IsLegacy && s.Key != StatusRejected).OrderBy(s => s.Order).ToList();

    /// <summary>
    /// Bosqichning voronkadagi o'rni (1..N). Eski bosqich — o'zi bilan bir xil tartibli asosiy
    /// bosqichning o'rni; "rad etildi" va noma'lum kalit — 0 (voronkaga hissa qo'shmaydi).
    /// </summary>
    public static int FunnelOrderOf(string? key)
    {
        var s = Stages.FirstOrDefault(x => x.Key == key);
        return s is null || s.Key == StatusRejected ? 0 : s.Order;
    }

    /* =============================================================================================
     *  VORONKA KONVERSIYASI — "nechta nomzod qaysi bosqichgacha yetib bordi"
     * ============================================================================================= */

    /// <summary>Bitta ariza: joriy bosqich + tarixda uchragan bosqichlar.</summary>
    public record FunnelInput(string Status, IEnumerable<string> History);

    /// <summary>Voronka qadami natijasi.</summary>
    /// <param name="Reached">Shu bosqichga (yoki undan keyingisiga) YETIB BORGAN arizalar soni.</param>
    /// <param name="Current">Hozir AYNAN shu bosqichda turgan arizalar (eski bosqich — o'z o'rniga qo'shiladi).</param>
    /// <param name="RejectedHere">Eng uzoq yetgan bosqichi shu bo'lib, RAD ETILGANLAR — "qayerda yo'qotyapmiz".</param>
    /// <param name="FromPrevPct">Oldingi bosqichdan o'tish ulushi (%, butun; birinchi qadam — 100).</param>
    /// <param name="FromStartPct">Jami arizalardan ulush (%, butun).</param>
    public record FunnelStep(string Key, string Label, string Icon, int Order,
        int Reached, int Current, int RejectedHere, int FromPrevPct, int FromStartPct);

    /// <summary>
    /// Voronka konversiyasi (sof funksiya). Ariza qaysi bosqichgacha "yetib borgan" — joriy
    /// bosqichi VA tarixidagi bosqichlarning ENG UZOG'I bo'yicha: rad etilgan nomzod ham qaysi
    /// bosqichda to'xtagani bilan hisoblanadi, bosqich o'tkazib yuborilgan bo'lsa (CV → HR) oraliq
    /// bosqichlar ham "o'tilgan" sanaladi. Har ariza kamida birinchi bosqichga (CV) yetgan.
    /// </summary>
    public static List<FunnelStep> ComputeFunnel(IEnumerable<FunnelInput> apps)
    {
        var funnel = FunnelStages;
        var reached = new int[funnel.Count];
        var current = new int[funnel.Count];
        var rejectedHere = new int[funnel.Count];
        var total = 0;

        foreach (var a in apps)
        {
            total++;
            var max = a.History.Append(a.Status).Select(FunnelOrderOf).DefaultIfEmpty(0).Max();
            if (max < funnel[0].Order) max = funnel[0].Order;
            var cur = FunnelOrderOf(a.Status);
            for (var i = 0; i < funnel.Count; i++)
            {
                if (max >= funnel[i].Order) reached[i]++;
                if (cur == funnel[i].Order) current[i]++;
                if (a.Status == StatusRejected && max == funnel[i].Order) rejectedHere[i]++;
            }
        }

        static int Pct(int part, int whole) => whole <= 0 ? 0 : (int)Math.Floor(part * 100.0 / whole);
        return funnel.Select((s, i) => new FunnelStep(
            s.Key, s.Label, s.Icon, s.Order, reached[i], current[i], rejectedHere[i],
            i == 0 ? (total > 0 ? 100 : 0) : Pct(reached[i], reached[i - 1]),
            Pct(reached[i], total))).ToList();
    }

    /* =============================================================================================
     *  VAKANSIYA HOLATI — bitta tugma bilan YOQISH/O'CHIRISH (ON/OFF) + arxiv
     * ============================================================================================= */

    /// <summary>Yoqilgan — Mini App'da ko'rinadi va ariza qabul qiladi.</summary>
    public const string VacancyActive = "active";
    /// <summary>O'chirilgan (OFF) — vaqtincha yopiq: Mini App'da KO'RINMAYDI, ariza qabul qilmaydi,
    /// lekin admin ro'yxatida joyida turadi va bitta tugma bilan qayta yoqiladi.</summary>
    public const string VacancyPaused = "paused";
    /// <summary>Arxivlangan — butunlay yopilgan (alohida "Arxiv" filtrida).</summary>
    public const string VacancyArchived = "archived";

    public static bool IsValidVacancyStatus(string? s) => s is VacancyActive or VacancyPaused or VacancyArchived;

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
