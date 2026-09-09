using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IntellectCRM.Infrastructure.Data;
using IntellectCRM.Application.Dtos;
using IntellectCRM.Domain;
using IntellectCRM.Application.Services;
using System.Security.Claims;

namespace IntellectCRM.Server.Controllers;

/// <summary>
/// Admin "Xabarlar" bo'limi: (1) guruh ota-onalariga Telegram bot orqali e'lon yuborish;
/// (2) guruh chati (o'quvchilar + dars beruvchi o'qituvchilar + admin). Faqat "admin" roli.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("messages.broadcast")]
[Route("api/admin/messages")]
public class MessagesController(
    AppDbContext db, ChatService chat, TelegramService telegram, FcmService fcm, EskizService eskiz,
    SmsQueueService smsQueue) : ControllerBase
{
    private string Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    /// <summary>Joriy foydalanuvchining roli (chat darvozasi uchun) — claim tipiga bog'lanmasdan,
    /// <c>IsInRole</c> orqali. Bu controllerga faqat admin/superadmin/staff kiradi (AdminPerm).</summary>
    private string RoleName => User.IsInRole(Roles.SuperAdmin) ? Roles.SuperAdmin
        : User.IsInRole(Roles.Admin) ? Roles.Admin
        : User.IsInRole(Roles.Staff) ? Roles.Staff : "";

    /// <summary>
    /// CHAT DARVOZASI: guruh chati va xodimlar kanali — "messages" bo'lim ruxsatini TALAB QILADI.
    ///
    /// <para>AdminPerm xodim uchun GET'larni ruxsatsiz o'tkazadi (bo'limlararo o'qish uchun ataylab).
    /// Chat esa bo'limlararo ma'lumot emas — shu sabab bu yerda alohida tekshiriladi. Qoida va
    /// asoslash: <see cref="ChatService.CanUseAdminChat"/>. Admin/superadmin uchun hech narsa
    /// o'zgarmaydi — ular avvalgidek barcha kanallarni ko'radi.</para>
    /// </summary>
    private bool CanUseChat() => ChatService.CanUseAdminChat(
        RoleName, User.FindAll(AdminPermAttribute.ClaimType).Select(c => c.Value));

    /// <summary>"eskiz" (default) | "local" — bo'sh/notanish qiymat "eskiz"ga tushadi.</summary>
    private static string NormalizeProvider(string? provider) =>
        provider?.Trim().ToLowerInvariant() == "local" ? "local" : "eskiz";

    /// <summary>Lid SMS'lari uchun kanal SOZLAMALARDAN olinadi (lid oynalarida provider tanlovi
    /// yo'q): Local SMS yoqilgan bo'lsa (Sozlamalar → Xabar kanallari → SMS) "local", aks holda
    /// "eskiz". Mijoz provider'ni ANIQ bergan bo'lsa (eski frontend/API) o'sha ishlatiladi —
    /// xatti-harakat buzilmaydi.</summary>
    private static string LeadSmsProviderOf(string? requested, CenterMeta? meta) =>
        string.IsNullOrWhiteSpace(requested)
            ? (meta?.LocalSmsEnabled == true ? "local" : "eskiz")
            : NormalizeProvider(requested);

    /// <summary>provider="local" bo'lsa CenterMeta.LocalSmsEnabled yoqilganini va (agentId berilmasa)
    /// standart agent sozlanganini tekshiradi — batch boshlanishidan oldin tezkor xato qaytarish uchun.</summary>
    private async Task<string?> ValidateLocalSmsAsync(string provider, string? agentId, CenterMeta? meta)
    {
        if (provider != "local") return null;
        if (meta?.LocalSmsEnabled != true)
            return "Local SMS yoqilmagan. Sozlamalar → Xabar kanallari → SMS'da yoqing.";
        if (string.IsNullOrWhiteSpace(agentId) && string.IsNullOrWhiteSpace(meta.LocalSmsDefaultAgentId))
            return "Standart Local SMS agent tanlanmagan. Sozlamalar → Xabar kanallari → SMS'da tanlang.";
        return null;
    }

    // ---------- Guruhlar ro'yxati (chat/e'lon tanlash uchun) ----------

    [HttpGet("classes")]
    public async Task<ActionResult<IEnumerable<ChatClassDto>>> Classes()
    {
        // Chat kanallari ro'yxati (oxirgi xabar vaqti bilan) — chat darvozasi ostida.
        if (!CanUseChat()) return Forbid();
        var classes = await db.Classes.OrderBy(c => c.Grade).ThenBy(c => c.Name).ToListAsync();
        var students = await db.Students.Select(s => new { s.Id, s.ClassName }).ToListAsync();
        var regs = await db.TelegramRegistrations.Select(r => new { r.StudentId, r.ChatId }).ToListAsync();
        var lastByClass = (await db.ChatMessages
                .GroupBy(m => m.ClassName)
                .Select(g => new { Name = g.Key, Last = g.Max(x => x.CreatedAt) })
                .ToListAsync())
            .ToDictionary(x => x.Name, x => x.Last);

        // O'QUVCHI → GURUH NOMLARI: TIRIK a'zoliklardan (bir o'quvchi bir nechta guruhda bo'lishi
        // mumkin), a'zoligi yo'q eski yozuvlarda — `ClassName` zaxirasi.
        // ⚠️ Ilgari o'quvchi FAQAT `ClassName` guruhiga tegishli deb sanalardi va Telegram chatlar
        // sanog'ida yalang `.First()` turardi: ikkinchi guruhda o'qiyotgan o'quvchi u guruhning
        // sanog'iga umuman kirmasdi (eski, muzlatilgan guruhga esa kirib turardi).
        var groupNameById = classes.ToDictionary(c => c.Id, c => c.Name);
        var liveMemberships = await db.StudentGroups.AsNoTracking().Where(m => m.IsActive)
            .Select(m => new { m.StudentId, m.GroupId }).ToListAsync();
        var namesByStudent = liveMemberships
            .Where(m => groupNameById.ContainsKey(m.GroupId))
            .GroupBy(m => m.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(m => groupNameById[m.GroupId]).Distinct().ToList());
        foreach (var s2 in students)
        {
            if (namesByStudent.ContainsKey(s2.Id) || string.IsNullOrEmpty(s2.ClassName)) continue;
            namesByStudent[s2.Id] = new List<string> { s2.ClassName };
        }

        var studentCountByClass = namesByStudent.SelectMany(kv => kv.Value)
            .GroupBy(n => n).ToDictionary(g => g.Key, g => g.Count());
        // Har guruh bo'yicha alohida (distinct) Telegram chatlar soni — e'lon oluvchilar.
        // (O'quvchi ikki guruhda bo'lsa uning chati IKKALASIDA ham sanaladi — u ikkalasidan ham
        // e'lon oladi.)
        var parentChatsByClass = regs
            .Where(r => namesByStudent.ContainsKey(r.StudentId))
            .SelectMany(r => namesByStudent[r.StudentId].Select(n => new { Name = n, r.ChatId }))
            .GroupBy(x => x.Name)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ChatId).Distinct().Count());

        return classes.Select(c => new ChatClassDto(
            c.Name, c.Grade,
            studentCountByClass.GetValueOrDefault(c.Name, 0),
            parentChatsByClass.GetValueOrDefault(c.Name, 0),
            lastByClass.TryGetValue(c.Name, out var last) ? last.ToString("o") : null)).ToList();
    }

    /// <summary>
    /// Har bir kanal uchun oxirgi xabar vaqti (ISO) — frontend o'qilmagan xabarlarni aniqlaydi.
    /// Admin uchun barcha guruhlar + xodimlar kanali qaytadi. Xabari yo'q kanal uchun null.
    /// Ruxsati yo'q xodimga — bo'sh ro'yxat (403 emas): bu endpointni har sahifada o'qilmagan
    /// belgisi uchun umumiy kontekst chaqiradi, xato bermasligi kerak.
    /// </summary>
    [HttpGet("last-messages")]
    public async Task<ActionResult<Dictionary<string, string?>>> LastMessages()
    {
        if (!CanUseChat()) return new Dictionary<string, string?>();
        var channels = await chat.ClassNamesForUserAsync(Uid, Roles.Admin);
        var lastByChannel = (await db.ChatMessages
                .Where(m => channels.Contains(m.ClassName))
                .GroupBy(m => m.ClassName)
                .Select(g => new { Name = g.Key, Last = g.Max(x => x.CreatedAt) })
                .ToListAsync())
            .ToDictionary(x => x.Name, x => (string?)x.Last.ToString("o"));
        return channels.Distinct().ToDictionary(c => c, c => lastByChannel.GetValueOrDefault(c, null));
    }

    // ---------- Guruh chati ----------

    [HttpGet("chat/{className}")]
    public async Task<ActionResult<IEnumerable<ChatMessageDto>>> Chat(string className, [FromQuery] string? since)
    {
        if (!CanUseChat()) return Forbid();
        return await chat.GetMessagesAsync(className, ChatService.ParseSince(since));
    }

    [HttpPost("chat/{className}")]
    // Guruh CHATI — "Chats" bo'limidagi alohida sahifa (ommaviy xabar yuborishdan boshqa ish).
    [AdminPerm("messages.chat")]
    public async Task<ActionResult<ChatMessageDto>> SendChat(string className, SendChatRequest req)
    {
        // POST'ni AdminPerm allaqachon "messages"/"messages:create" bilan darvozalaydi —
        // bu yerdagi tekshiruv qatlam sifatida (o'qish bilan bir xil qoida) qoldirilgan.
        if (!CanUseChat()) return Forbid();
        var dto = await chat.PostAsync(className, Uid, req.Text);
        return dto is null ? BadRequest(new { message = "Xabar bo'sh" }) : dto;
    }

    // ---------- E'lon (Telegram) ----------

    [HttpGet("broadcasts")]
    public async Task<ActionResult<IEnumerable<BroadcastDto>>> Broadcasts([FromQuery] string? className)
    {
        var q = db.Broadcasts.AsQueryable();
        if (!string.IsNullOrEmpty(className)) q = q.Where(b => b.ClassName == className);
        var list = await q.OrderByDescending(b => b.CreatedAt).Take(100).ToListAsync();
        return list.Select(b => new BroadcastDto(
            b.Id, b.ClassName, b.Text, b.SenderName, b.CreatedAt.ToString("o"),
            b.RecipientCount, b.SentCount)).ToList();
    }

    [HttpPost("broadcast")]
    public async Task<ActionResult<BroadcastDto>> SendBroadcast(SendBroadcastRequest req)
    {
        var text = req.Text?.Trim() ?? "";
        if (text.Length == 0) return BadRequest(new { message = "Xabar matni kerak" });
        var scope = (req.Scope ?? "class").Trim().ToLowerInvariant();

        // Qamrov bo'yicha maqsadli o'quvchilar to'plami.
        var studentsQ = db.Students.AsQueryable();
        var teacherIds = scope == "selected"
            ? (req.TeacherIds ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList()
            : new List<string>();
        string audience;
        switch (scope)
        {
            case "selected":
                var ids = req.StudentIds ?? new();
                if (ids.Count == 0 && teacherIds.Count == 0) return BadRequest(new { message = "Hech kim tanlanmadi" });
                studentsQ = studentsQ.Where(s => ids.Contains(s.Id));
                audience = $"Tanlangan ({ids.Count + teacherIds.Count})";
                break;
            case "all":
                audience = "Barcha guruhlar";
                break;
            default: // class
                var cn = req.ClassName?.Trim() ?? "";
                if (cn.Length == 0) return BadRequest(new { message = "Guruh kerak" });
                studentsQ = studentsQ.Where(s => s.ClassName == cn);
                audience = cn;
                break;
        }

        var students = await studentsQ.ToListAsync();
        if (req.OnlyDebtors)
        {
            students = students.Where(s => s.Balance < 0).ToList();
            audience += " — qarzdorlar";
        }
        // OMMAVIY e'lon: arxivdagi va guruhi YOPILGAN/TUGATILGAN o'quvchilar chiqarib tashlanadi
        // ("Tanlangan" rejimida admin kimni tanlagan bo'lsa — o'shanga yuboriladi).
        if (scope != "selected")
        {
            var closed = await MessagingAudience.ClosedGroupStudentIdsAsync(db);
            students = students.Where(s => !s.IsArchived && !closed.Contains(s.Id)).ToList();
        }
        var byId = students.ToDictionary(s => s.Id);
        var sids = students.Select(s => s.Id).ToList();

        // Har bir o'quvchi (ro'yxatdagi chat) uchun matn alohida moslashtiriladi (mail-merge).
        var regs = await db.TelegramRegistrations
            .Where(r => sids.Contains(r.StudentId))
            .ToListAsync();

        // "Tanlab" rejimida tanlangan o'qituvchilarning Telegram registratsiyalari — alohida yuboriladi.
        var teacherRegs = teacherIds.Count > 0
            ? await db.TelegramRegistrations.Where(r => r.TeacherId != null && teacherIds.Contains(r.TeacherId)).ToListAsync()
            : new List<TelegramRegistration>();
        var teachersById = teacherRegs.Count > 0
            ? await db.Teachers.Where(t => teacherIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id)
            : new Dictionary<string, Teacher>();

        var centerName = (await db.CenterMeta.FirstOrDefaultAsync())?.Name ?? "";
        var groupByName = await GroupByNameAsync();
        var teacherNames = await TeacherNamesAsync();
        var sent = 0;
        foreach (var r in regs)
        {
            if (!byId.TryGetValue(r.StudentId, out var s)) continue;
            var grp = groupByName.GetValueOrDefault(s.ClassName ?? "");
            var message = $"📢 Markaz e'loni\n\n{Personalize(text, s, r, centerName, grp, MessageTokenizer.TeacherNameOf(grp, teacherNames))}";
            if (await telegram.SendMessageAsync(r.ChatId, message)) sent++;
        }
        foreach (var r in teacherRegs)
        {
            if (!teachersById.TryGetValue(r.TeacherId!, out var t)) continue;
            var message = $"📢 Markaz e'loni\n\n{MessageTokenizer.Teacher(text, t, centerName)}";
            if (await telegram.SendMessageAsync(r.ChatId, message)) sent++;
        }

        var user = await db.Users.FindAsync(Uid);
        var bc = new Broadcast
        {
            ClassName = audience,
            Text = text,
            SenderUserId = Uid,
            SenderName = user?.FullName ?? "Administrator",
            CreatedAt = AppClock.Now,
            RecipientCount = regs.Count + teacherRegs.Count,
            SentCount = sent,
        };
        db.Broadcasts.Add(bc);
        await db.SaveChangesAsync();

        return new BroadcastDto(bc.Id, bc.ClassName, bc.Text, bc.SenderName,
            bc.CreatedAt.ToString("o"), bc.RecipientCount, bc.SentCount);
    }

    // Matn o'rinbosarlari markazlashgan: <see cref="MessageTokenizer"/> (barcha kanal/auditoriyalar shu yerda).

    /// <summary>O'quvchilarning asosiy guruhi (ClassName → Group) — dars jadvali tokenlari uchun.
    /// Nomi takrorlansa birinchisi olinadi (ToDictionary istisnosiz).</summary>
    private async Task<Dictionary<string, Group>> GroupByNameAsync() =>
        (await db.Classes.ToListAsync()).GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First());

    /// <summary>O'qituvchi id → F.I.Sh ({oqituvchi} tokeni uchun) — ro'yxatga bir marta yuklanadi.</summary>
    private async Task<Dictionary<string, string>> TeacherNamesAsync() =>
        await MessageTokenizer.TeacherNamesByIdAsync(db);

    /// <summary>E'lon matnini shu o'quvchi/ota-ona ma'lumotiga moslab almashtiradi.</summary>
    private static string Personalize(string template, Student s, TelegramRegistration reg, string centerName,
        Group? group = null, string? teacherName = null) =>
        MessageTokenizer.Student(template, s, reg.ParentName, reg.Phone, centerName, null, group, teacherName);

    /// <summary>Push/SMS matnini o'quvchi (ota-ona akkaunti) ma'lumotiga moslaydi.</summary>
    private static string PersonalizePush(string text, Student s, string centerName,
        Group? group = null, string? teacherName = null) =>
        MessageTokenizer.Student(text, s, s.ParentFullName, s.ParentPhone, centerName, null, group, teacherName);

    /// <summary>Push/SMS matnini o'qituvchiga moslaydi — o'quvchi-spetsifik tokenlar bo'sh.</summary>
    private static string PersonalizeTeacherPush(string text, Teacher t, string centerName) =>
        MessageTokenizer.Teacher(text, t, centerName);

    // ---------- Telegram ro'yxati (ota-onalar) ----------

    [HttpGet("telegram/registrations")]
    public async Task<ActionResult<IEnumerable<TelegramParentDto>>> Registrations([FromQuery] string? className)
    {
        var studentsQ = db.Students.AsQueryable();
        if (!string.IsNullOrEmpty(className)) studentsQ = studentsQ.Where(s => s.ClassName == className);
        var students = await studentsQ.ToListAsync();
        var byId = students.ToDictionary(s => s.Id);
        var ids = students.Select(s => s.Id).ToList();

        var regs = await db.TelegramRegistrations
            .Where(r => ids.Contains(r.StudentId))
            .OrderByDescending(r => r.CreatedAt).ToListAsync();

        return regs.Select(r =>
        {
            byId.TryGetValue(r.StudentId, out var s);
            return new TelegramParentDto(
                r.StudentId, s?.FullName ?? "", s?.ClassName ?? "", s?.Balance ?? 0m,
                r.ParentName, r.Phone, r.ChatId.ToString(), r.CreatedAt.ToString("o"));
        }).ToList();
    }

    /// <summary>"Tanlab" e'lon uchun Telegramda ro'yxatdan o'tgan o'qituvchilar (xodim ro'yxati).</summary>
    [HttpGet("telegram/registrations/teachers")]
    public async Task<ActionResult<IEnumerable<TelegramTeacherDto>>> TeacherRegistrations()
    {
        var regs = await db.TelegramRegistrations
            .Where(r => r.TeacherId != null)
            .OrderByDescending(r => r.CreatedAt).ToListAsync();
        var teacherIds = regs.Select(r => r.TeacherId!).Distinct().ToList();
        var teachers = await db.Teachers.Where(t => teacherIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
        return regs.Select(r => new TelegramTeacherDto(
            r.TeacherId!, teachers.GetValueOrDefault(r.TeacherId!)?.FullName ?? "",
            r.Phone, r.ChatId.ToString(), r.CreatedAt.ToString("o"))).ToList();
    }

    /// <summary>Telegram bot holati (sozlanganmi, bot foydalanuvchi nomi) — admin UI ko'rsatishi uchun.</summary>
    [HttpGet("telegram/status")]
    public ActionResult<object> TelegramStatus() =>
        Ok(new { configured = telegram.IsConfigured, botUsername = telegram.BotUsername ?? "" });

    // ---------- Push (Firebase / FCM) ----------

    /// <summary>Firebase (push) sozlanganmi — admin UI ko'rsatishi uchun.</summary>
    [HttpGet("push/status")]
    public ActionResult<object> PushStatus() =>
        // Service account .env dan (FCM_SERVICE_ACCOUNT_JSON) — bazadan emas.
        Ok(new { configured = FcmService.IsConfigured(AppSecrets.FcmServiceAccountJson) });

    /// <summary>Ro'yxatdan o'tgan qurilma tokenlari soni + so'nggilari — push nega yetib bormayotganini
    /// tekshirish uchun (0 bo'lsa: ilova FCM tokenni ro'yxatdan o'tkazmayapti).</summary>
    [HttpGet("push/devices")]
    public async Task<ActionResult<object>> PushDevices()
    {
        var all = await db.DeviceTokens.OrderByDescending(d => d.LastSeenAt).ToListAsync();
        var logins = await db.Users.ToDictionaryAsync(u => u.Id, u => u.Email);
        var recent = all.Take(20).Select(d => new
        {
            login = logins.GetValueOrDefault(d.UserId, ""),
            d.Platform,
            d.DeviceName,
            lastSeenAt = d.LastSeenAt.ToString("o"),
            tokenTail = d.Token.Length > 12 ? "…" + d.Token[^12..] : d.Token,
        });
        return Ok(new { count = all.Count, recent });
    }

    /// <summary>"Tanlab" push uchun oluvchilar ro'yxati: o'quvchilar + o'qituvchilar.
    /// DIQQAT: o'quvchi akkauntidan O'QUVCHI ham, OTA-ONA ham foydalanadi (bitta ilova) —
    /// ota-ona telefonida ro'yxatdan o'tgan qurilma ham shu akkauntga bog'lanadi, shuning uchun
    /// alohida "ota-ona" oluvchisi YO'Q va bo'lishi ham shart emas.</summary>
    [HttpGet("push/recipients")]
    public async Task<ActionResult<IEnumerable<PushRecipientDto>>> PushRecipients()
    {
        var students = await db.Students.Where(s => !s.IsArchived && s.UserId != null)
            .Select(s => new { s.UserId, s.FullName, s.ClassName }).ToListAsync();
        var teachers = await db.Teachers.Where(t => !t.IsArchived && t.UserId != null)
            .Select(t => new { t.UserId, t.FullName }).ToListAsync();
        var withDevice = (await db.DeviceTokens.Select(d => d.UserId).Distinct().ToListAsync()).ToHashSet();

        var list = new List<PushRecipientDto>();
        foreach (var s in students)
            // Guruh yorlig'i: bitta akkaunt — o'quvchi ham, ota-ona ham shundan kiradi.
            list.Add(new PushRecipientDto(s.UserId!, s.FullName, "O'quvchi / ota-ona", s.ClassName, withDevice.Contains(s.UserId!)));
        foreach (var t in teachers)
            list.Add(new PushRecipientDto(t.UserId!, t.FullName, "O'qituvchi", "", withDevice.Contains(t.UserId!)));
        return list
            .OrderBy(r => r.Group, StringComparer.Ordinal)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    [HttpGet("push")]
    public async Task<ActionResult<IEnumerable<PushMessageDto>>> PushHistory()
    {
        var list = await db.PushMessages.OrderByDescending(p => p.CreatedAt).Take(100).ToListAsync();
        var ids = list.Select(p => p.Id).ToList();
        // Har broadcast bo'yicha: jami oluvchi (tarix) + tasdiqlaganlar soni.
        // ⚠️ Sanoq SQL darajasida (GROUP BY) — ilgari mos qatorlar XOTIRAGA tortilib, C# da
        // guruhlanardi. `PushMessageId` indekssiz bo'lgani bilan qo'shilib, prodda bu so'rov
        // 161 mln qator o'qigan edi (jadvalning deyarli hammasi, 8000+ marta).
        var stats = (await db.UserNotifications.AsNoTracking()
                .Where(n => ids.Contains(n.PushMessageId))
                .GroupBy(n => n.PushMessageId)
                .Select(g => new
                {
                    Id = g.Key,
                    Target = g.Count(),
                    Confirmed = g.Sum(n => n.ConfirmedAt != null ? 1 : 0),
                })
                .ToListAsync())
            .ToDictionary(x => x.Id, x => (Target: x.Target, Confirmed: x.Confirmed));
        return list.Select(p =>
        {
            stats.TryGetValue(p.Id, out var s);
            return new PushMessageDto(p.Id, p.Audience, p.Title, p.Body, p.SenderName, p.CreatedAt.ToString("o"),
                p.RecipientCount, p.SentCount, s.Confirmed, s.Target);
        }).ToList();
    }

    /// <summary>Bitta e'lon (broadcast) bo'yicha kim tasdiqlagani — admin ko'rishi uchun.</summary>
    [HttpGet("push/{id}/confirmations")]
    public async Task<ActionResult<IEnumerable<PushConfirmationDto>>> PushConfirmations(string id)
    {
        // Faqat o'qish — tracking keraksiz (bu yerda hech narsa saqlanmaydi).
        var notifs = await db.UserNotifications.AsNoTracking().Where(n => n.PushMessageId == id).ToListAsync();
        if (notifs.Count == 0) return new List<PushConfirmationDto>();
        var userIds = notifs.Select(n => n.UserId).Distinct().ToList();
        var studentByUser = (await db.Students.AsNoTracking().Where(s => s.UserId != null && userIds.Contains(s.UserId)).ToListAsync())
            .GroupBy(s => s.UserId!).ToDictionary(g => g.Key, g => g.First());
        var teacherByUser = (await db.Teachers.AsNoTracking().Where(t => t.UserId != null && userIds.Contains(t.UserId)).ToListAsync())
            .GroupBy(t => t.UserId!).ToDictionary(g => g.Key, g => g.First());
        return notifs.Select(n =>
        {
            string name = "—", group = "";
            if (studentByUser.TryGetValue(n.UserId, out var st)) { name = st.FullName; group = st.ClassName; }
            else if (teacherByUser.TryGetValue(n.UserId, out var tch)) { name = tch.FullName; group = "O'qituvchi"; }
            return new PushConfirmationDto(name, group, n.ConfirmedAt != null, n.ConfirmedAt?.ToString("o"));
        }).OrderByDescending(c => c.Confirmed).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Ilovaga push yuboradi. Audience "parents" (ixtiyoriy ClassName bilan) yoki "teachers".
    /// Maqsadli foydalanuvchilarning ro'yxatdan o'tgan qurilma tokenlariga FCM orqali yuboriladi.
    /// </summary>
    [HttpPost("push/send")]
    public async Task<ActionResult<PushMessageDto>> SendPush(SendPushRequest req)
    {
        var title = (req.Title ?? "").Trim();
        var body = (req.Body ?? "").Trim();
        if (title.Length == 0 && body.Length == 0)
            return BadRequest(new { message = "Sarlavha yoki matn kerak" });
        var audience = (req.Audience ?? "parents").Trim().ToLowerInvariant();

        List<string> userIds;
        string label;
        if (audience == "selected")
        {
            userIds = (req.UserIds ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
            if (userIds.Count == 0) return BadRequest(new { message = "Hech kim tanlanmadi" });
            label = $"Tanlangan ({userIds.Count})";
        }
        else if (audience == "teachers")
        {
            userIds = await db.Teachers.Where(t => !t.IsArchived && t.UserId != null)
                .Select(t => t.UserId!).ToListAsync();
            label = "O'qituvchilar";
        }
        else
        {
            var q = db.Students.Where(s => !s.IsArchived && s.UserId != null);
            var cn = req.ClassName?.Trim() ?? "";
            if (cn.Length > 0) { q = q.Where(s => s.ClassName == cn); label = $"O'quvchilar — {cn}"; }
            else label = "O'quvchilar (o'quvchi/ota-ona ilovasi)";
            // Guruhi YOPILGAN/TUGATILGAN o'quvchilarga ommaviy push yuborilmaydi.
            var closed = await MessagingAudience.ClosedGroupStudentIdsAsync(db);
            userIds = (await q.Select(s => new { s.Id, UserId = s.UserId! }).ToListAsync())
                .Where(x => !closed.Contains(x.Id))
                .Select(x => x.UserId)
                .ToList();
        }

        // Per-oluvchi moslash uchun: foydalanuvchi → o'quvchi/o'qituvchi + uning tokenlari.
        var students = await db.Students.Where(s => s.UserId != null && userIds.Contains(s.UserId)).ToListAsync();
        var studentByUser = students.GroupBy(s => s.UserId!).ToDictionary(g => g.Key, g => g.First());
        var teachers = await db.Teachers.Where(t => t.UserId != null && userIds.Contains(t.UserId)).ToListAsync();
        var teacherByUser = teachers.GroupBy(t => t.UserId!).ToDictionary(g => g.Key, g => g.First());
        var tokensByUser = (await db.DeviceTokens.Where(d => userIds.Contains(d.UserId)).ToListAsync())
            .GroupBy(d => d.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Token).Distinct().ToList());

        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        var json = AppSecrets.FcmServiceAccountJson;
        var centerName = meta?.Name ?? "";
        var groupByName = await GroupByNameAsync();
        var teacherNames = await TeacherNamesAsync();
        var recipientCount = tokensByUser.Sum(kv => kv.Value.Count);
        var sent = 0;
        var deadTokens = new List<string>();
        // Har bir foydalanuvchiga matn o'rinbosarlari moslab yuboriladi (o'quvchi/ota-ona ma'lumoti bilan).
        foreach (var (userId, toks) in tokensByUser)
        {
            var t = title;
            var b = body;
            if (studentByUser.TryGetValue(userId, out var st))
            {
                var grp = groupByName.GetValueOrDefault(st.ClassName ?? "");
                var tn = MessageTokenizer.TeacherNameOf(grp, teacherNames);
                t = PersonalizePush(title, st, centerName, grp, tn);
                b = PersonalizePush(body, st, centerName, grp, tn);
            }
            else if (teacherByUser.TryGetValue(userId, out var tch))
            {
                t = PersonalizeTeacherPush(title, tch, centerName);
                b = PersonalizeTeacherPush(body, tch, centerName);
            }
            var res = await fcm.SendAsync(json, toks, t, b);
            sent += res.Sent;
            deadTokens.AddRange(res.InvalidTokens);
        }

        // O'lik tokenlarni bazadan tozalaymiz (ilova o'chirilgan / web token bekor qilingan).
        if (deadTokens.Count > 0)
            db.DeviceTokens.RemoveRange(db.DeviceTokens.Where(d => deadTokens.Contains(d.Token)));

        // Ilova tarixiga — AUDIENCE'dagi HAR foydalanuvchi uchun (push yetmasa ham bildirishnoma ro'yxatida ko'rinadi).
        var pushId = Guid.NewGuid().ToString();
        foreach (var userId in userIds)
        {
            var t = title;
            var b = body;
            if (studentByUser.TryGetValue(userId, out var stn)) { var grp = groupByName.GetValueOrDefault(stn.ClassName ?? ""); var tn = MessageTokenizer.TeacherNameOf(grp, teacherNames); t = PersonalizePush(title, stn, centerName, grp, tn); b = PersonalizePush(body, stn, centerName, grp, tn); }
            else if (teacherByUser.TryGetValue(userId, out var tchn)) { t = PersonalizeTeacherPush(title, tchn, centerName); b = PersonalizeTeacherPush(body, tchn, centerName); }
            NotificationStore.Add(db, userId, t, b, "announcement", pushId);
        }

        var user = await db.Users.FindAsync(Uid);
        var pm = new PushMessage
        {
            Id = pushId,
            Audience = label,
            Title = title,
            Body = body,
            SenderUserId = Uid,
            SenderName = user?.FullName ?? "Administrator",
            CreatedAt = AppClock.Now,
            RecipientCount = recipientCount,
            SentCount = sent,
        };
        db.PushMessages.Add(pm);
        await db.SaveChangesAsync();
        return new PushMessageDto(pm.Id, pm.Audience, pm.Title, pm.Body, pm.SenderName,
            pm.CreatedAt.ToString("o"), pm.RecipientCount, pm.SentCount);
    }

    // ---------- SMS (Eskiz.uz) ----------

    /// <summary>SMS (Eskiz) sozlanganmi + sender — admin UI ko'rsatishi uchun (tarmoqsiz, tez).</summary>
    [HttpGet("sms/status")]
    public async Task<ActionResult<SmsStatusDto>> SmsStatus()
    {
        var m = await db.CenterMeta.FirstOrDefaultAsync();
        return new SmsStatusDto(eskiz.IsConfigured(m), eskiz.SenderOf(m), null, m?.LocalSmsEnabled ?? false, m?.LocalSmsDefaultAgentId);
    }

    /// <summary>Yuborilgan SMS partiyalari (tarix, eng yangisi birinchi).</summary>
    [HttpGet("sms")]
    public async Task<ActionResult<IEnumerable<SmsBatchDto>>> SmsHistory()
    {
        var list = await db.SmsBatches.OrderByDescending(b => b.CreatedAt).Take(100).ToListAsync();
        return list.Select(b => new SmsBatchDto(b.Id, b.Audience, b.Message, b.SenderName,
            b.CreatedAt.ToString("o"), b.RecipientCount, b.SentCount, b.Provider)).ToList();
    }

    /// <summary>
    /// Ommaviy (fonda ketayotgan) partiyaning JONLI holati — modal "Yuborilmoqda: 12/300" deb ko'rsatadi.
    /// Avval xotiradagi navbat holati o'qiladi; u yerda bo'lmasa (ilova qayta ishga tushgan yoki partiya
    /// ancha oldin tugagan) bazadagi yozuvlardan tiklanadi va tugagan deb qaytariladi.
    /// </summary>
    [HttpGet("sms/{id}/progress")]
    public async Task<ActionResult<SmsProgressDto>> SmsProgress(string id)
    {
        if (smsQueue.Get(id) is { } live)
            return new SmsProgressDto(id, live.Total, live.Done, live.Sent, live.Finished);
        var batch = await db.SmsBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
        if (batch is null) return NotFound();
        var done = await db.SmsLogs.CountAsync(l => l.BatchId == id);
        return new SmsProgressDto(id, batch.RecipientCount, done, batch.SentCount, true);
    }

    /// <summary>Bitta SMS partiyasi bo'yicha raqamlar va yetkazib berish holati.</summary>
    [HttpGet("sms/{id}/logs")]
    public async Task<ActionResult<IEnumerable<SmsLogDto>>> SmsLogs(string id)
    {
        var logs = await db.SmsLogs.Where(l => l.BatchId == id)
            .OrderBy(l => l.RecipientName).ToListAsync();
        return logs.Select(l => new SmsLogDto(l.Id, l.PhoneNumber, l.RecipientName, l.Status,
            l.CreatedAt.ToString("o"), l.Provider)).ToList();
    }

    /// <summary>
    /// "Tanlab" SMS uchun oluvchilar ro'yxati — barcha arxivlanmagan o'quvchilar (ism bo'yicha).
    /// parentPhone = ParentPhone→FatherPhone→MotherPhone (birinchi bo'sh bo'lmagani); studentPhone = s.Phone.
    /// </summary>
    [HttpGet("sms/recipients")]
    public async Task<ActionResult<IEnumerable<SmsRecipientDto>>> SmsRecipients()
    {
        var students = await db.Students.AsNoTracking().Where(s => !s.IsArchived)
            .OrderBy(s => s.FullName)
            .Select(s => new { s.Id, s.FullName, s.ClassName, s.Phone, s.ParentPhone, s.FatherPhone, s.MotherPhone })
            .ToListAsync();
        return students.Select(s =>
        {
            var parentPhone = !string.IsNullOrWhiteSpace(s.ParentPhone) ? s.ParentPhone
                : !string.IsNullOrWhiteSpace(s.FatherPhone) ? s.FatherPhone
                : !string.IsNullOrWhiteSpace(s.MotherPhone) ? s.MotherPhone : null;
            var studentPhone = string.IsNullOrWhiteSpace(s.Phone) ? null : s.Phone;
            return new SmsRecipientDto(s.Id, s.FullName, s.ClassName ?? "", parentPhone, studentPhone);
        }).ToList();
    }

    /// <summary>"Tanlab" SMS uchun o'qituvchi oluvchilar — barcha arxivlanmagan o'qituvchilar (ism bo'yicha).</summary>
    [HttpGet("sms/recipients/teachers")]
    public async Task<ActionResult<IEnumerable<SmsTeacherRecipientDto>>> SmsTeacherRecipients()
    {
        var teachers = await db.Teachers.AsNoTracking().Where(t => !t.IsArchived)
            .OrderBy(t => t.FullName)
            .Select(t => new { t.Id, t.FullName, t.Phone })
            .ToListAsync();
        return teachers.Select(t =>
            new SmsTeacherRecipientDto(t.Id, t.FullName, string.IsNullOrWhiteSpace(t.Phone) ? null : t.Phone))
            .ToList();
    }

    /// <summary>
    /// SMS yuborish (Eskiz). Audience: parents (o'quvchi ota-onasi raqami) | students (o'quvchi raqami) |
    /// teachers (o'qituvchi raqami) | selected (StudentIds — ota-ona raqami). Bir xil raqam bir marta.
    /// Matn har o'quvchiga moslab to'ldiriladi ({fish} {sinf} {qarzdorlik} {balans} {telefon}).
    /// </summary>
    [HttpPost("sms/send")]
    public async Task<ActionResult<SmsBatchDto>> SendSms(SendSmsRequest req)
    {
        var text = req.Text?.Trim() ?? "";
        if (text.Length == 0) return BadRequest(new { message = "SMS matni kerak" });
        var audience = (req.Audience ?? "parents").Trim().ToLowerInvariant();
        var centerName = (await db.CenterMeta.FirstOrDefaultAsync())?.Name ?? "";

        // (telefon, nom, moslangan matn) ro'yxatini yig'amiz.
        var targets = new List<(string Phone, string Name, string Message)>();
        string label;

        if (audience == "teachers")
        {
            var teachers = await db.Teachers.Where(t => !t.IsArchived).ToListAsync();
            foreach (var t in teachers)
            {
                if (string.IsNullOrWhiteSpace(t.Phone)) continue;
                targets.Add((t.Phone, t.FullName, PersonalizeTeacherPush(text, t, centerName)));
            }
            label = "O'qituvchilar";
        }
        else
        {
            // O'quvchilar to'plami (parents/students/selected).
            var q = db.Students.Where(s => !s.IsArchived);
            var teacherIds = new List<string>();
            if (audience == "selected")
            {
                var ids = (req.StudentIds ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
                teacherIds = (req.TeacherIds ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
                if (ids.Count == 0 && teacherIds.Count == 0) return BadRequest(new { message = "Hech kim tanlanmadi" });
                q = q.Where(s => ids.Contains(s.Id));
                label = $"Tanlangan ({ids.Count + teacherIds.Count})";
            }
            else
            {
                var cn = req.ClassName?.Trim() ?? "";
                if (cn.Length > 0) { q = q.Where(s => s.ClassName == cn); label = cn; }
                else label = "Barcha guruhlar";
            }
            var students = await q.ToListAsync();
            if (req.OnlyDebtors) { students = students.Where(s => s.Balance < 0).ToList(); label += " — qarzdorlar"; }
            // OMMAVIY yuborishda guruhi YOPILGAN/TUGATILGAN o'quvchilar chiqarib tashlanadi
            // ("Tanlangan" rejimida esa admin kimni tanlagan bo'lsa — o'shanga yuboriladi).
            if (audience != "selected")
            {
                var closed = await MessagingAudience.ClosedGroupStudentIdsAsync(db);
                students = students.Where(s => !closed.Contains(s.Id)).ToList();
            }
            var groupByName = await GroupByNameAsync();
            var teacherNames = await TeacherNamesAsync();

            // O'quvchining o'z raqami: audience=="students" YOKI "selected" + ToParent=false. Aks holda ota-ona.
            var toStudentPhone = audience == "students" || (audience == "selected" && !req.ToParent);
            foreach (var s in students)
            {
                var phone = toStudentPhone
                    ? s.Phone
                    : (!string.IsNullOrWhiteSpace(s.ParentPhone) ? s.ParentPhone
                        : !string.IsNullOrWhiteSpace(s.FatherPhone) ? s.FatherPhone : s.MotherPhone);
                if (string.IsNullOrWhiteSpace(phone)) continue;
                var grp = groupByName.GetValueOrDefault(s.ClassName ?? "");
                targets.Add((phone, s.FullName, MessageTokenizer.Student(text, s, s.ParentFullName, phone, centerName,
                    null, grp, MessageTokenizer.TeacherNameOf(grp, teacherNames))));
            }
            label = toStudentPhone ? $"O'quvchilar — {label}" : $"Ota-onalar — {label}";

            // "Tanlab" rejimida tanlangan o'qituvchilar — doim o'z raqamiga (ToParent ularga taalluqli emas).
            if (teacherIds.Count > 0)
            {
                var selTeachers = await db.Teachers.Where(t => teacherIds.Contains(t.Id)).ToListAsync();
                foreach (var t in selTeachers)
                {
                    if (string.IsNullOrWhiteSpace(t.Phone)) continue;
                    targets.Add((t.Phone, t.FullName, PersonalizeTeacherPush(text, t, centerName)));
                }
            }
        }

        // Bir xil raqamni bir marta (normallashtirilgan kalit bo'yicha).
        var seen = new HashSet<string>();
        targets = targets.Where(t => seen.Add(EskizService.NormalizePhone(t.Phone))).ToList();
        if (targets.Count == 0) return BadRequest(new { message = "Raqamli oluvchi topilmadi" });

        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        var provider = NormalizeProvider(req.Provider);
        if (provider == "eskiz" && !eskiz.IsConfigured(meta))
            return BadRequest(new { message = "Eskiz SMS sozlanmagan. Sozlamalar → SMS (Eskiz)da login/parol kiriting." });
        if (await ValidateLocalSmsAsync(provider, req.AgentId, meta) is { } localErr)
            return BadRequest(new { message = localErr });

        var user = await db.Users.FindAsync(Uid);
        return await StartBatchAsync(
            label, text, targets.Select(t => new SmsQueueService.Target(t.Phone, t.Name, t.Message)).ToList(),
            provider, req.AgentId, user?.FullName ?? "Administrator");
    }

    /// <summary>
    /// SMS partiyasini BOSHLAYDI: <see cref="SmsBatch"/>ni darhol yozadi (tarixda o'sha zahoti ko'rinadi),
    /// so'ng kichik bo'lsa shu yerda yuboradi, katta bo'lsa <see cref="SmsQueueService"/> navbatiga qo'yadi.
    ///
    /// <para>⚠️ Ommaviy yuborishni so'rov ichida qilib bo'lmaydi: har SMS alohida ketadi (Eskiz — HTTP
    /// so'rov, Local — sozlangan kutish), 100 ta oluvchi bir necha daqiqa oladi, Cloudflare esa javobni
    /// 100 soniyagina kutadi va ulanishni uzadi ("Yuborishda xatolik" — aslida SMS'lar ketayotgan bo'ladi).</para>
    /// </summary>
    private async Task<SmsBatchDto> StartBatchAsync(
        string label, string text, List<SmsQueueService.Target> queueTargets,
        string provider, string? agentId, string senderName, string? leadNote = null)
    {
        var batch = new SmsBatch
        {
            Id = Guid.NewGuid().ToString(),
            Audience = label,
            Message = text,
            SenderUserId = Uid,
            SenderName = senderName,
            CreatedAt = AppClock.Now,
            RecipientCount = queueTargets.Count,
            SentCount = 0,
            Provider = provider,
        };
        db.SmsBatches.Add(batch);
        await db.SaveChangesAsync();

        var callbackUrl = $"{Request.Scheme}://{Request.Host}/api/sms/callback";
        var job = new SmsQueueService.Job(
            batch.Id, provider, agentId, callbackUrl, queueTargets, leadNote, senderName);

        var queued = SmsQueueService.ShouldQueue(queueTargets.Count);
        if (queued) smsQueue.Enqueue(job);
        else await smsQueue.RunInlineAsync(db, job);   // SentCount'ni o'zi yangilab saqlaydi

        return new SmsBatchDto(batch.Id, batch.Audience, batch.Message, batch.SenderName,
            batch.CreatedAt.ToString("o"), batch.RecipientCount, batch.SentCount, batch.Provider, queued);
    }

    /// <summary>
    /// Barcha tayyor matnlar (birlashgan): SMS andozalari (db.SmsTemplates) + avto-xabar qoidalari
    /// shablonlari (db.AutoMessageRules, Template bo'sh bo'lmaganlari). Uchala yuborish oynasida (e'lon/push/SMS) chip.
    /// </summary>
    [HttpGet("templates/all")]
    public async Task<ActionResult<IEnumerable<UnifiedTemplateDto>>> AllTemplates()
    {
        var sms = await db.SmsTemplates.AsNoTracking().OrderBy(t => t.Order).ThenBy(t => t.Name)
            .Select(t => new UnifiedTemplateDto("sms", t.Name, t.Text)).ToListAsync();
        var autos = await db.AutoMessageRules.AsNoTracking()
            .Where(r => r.Template != "")
            .OrderBy(r => r.Name)
            .Select(r => new UnifiedTemplateDto("auto", r.Name, r.Template)).ToListAsync();
        return sms.Concat(autos).ToList();
    }

    // ---------- SMS andozalari (shablonlar) — Sozlamalar → SMS (Eskiz) ----------

    [HttpGet("sms/templates")]
    public async Task<ActionResult<IEnumerable<SmsTemplateDto>>> SmsTemplates()
    {
        var list = await db.SmsTemplates.OrderBy(t => t.Order).ThenBy(t => t.Name).ToListAsync();
        return list.Select(t => new SmsTemplateDto(t.Id, t.Name, t.Text, t.Order)).ToList();
    }

    // Avto-hodisalar YAGONA "Avto xabarlar" (AutoMessageRule) moduliga ko'chdi — SMS andozalari endi
    // faqat QO'LDA yuborish uchun.

    [HttpPost("sms/templates")]
    public async Task<ActionResult<SmsTemplateDto>> CreateSmsTemplate(SaveSmsTemplateRequest req)
    {
        var name = (req.Name ?? "").Trim();
        var text = (req.Text ?? "").Trim();
        if (name.Length == 0 || text.Length == 0) return BadRequest(new { message = "Nom va matn kerak" });
        var order = (await db.SmsTemplates.MaxAsync(t => (int?)t.Order) ?? 0) + 1;
        var t = new SmsTemplate { Name = name, Text = text, Order = order };
        db.SmsTemplates.Add(t);
        await db.SaveChangesAsync();
        return new SmsTemplateDto(t.Id, t.Name, t.Text, t.Order);
    }

    [HttpPut("sms/templates/{id}")]
    public async Task<ActionResult<SmsTemplateDto>> UpdateSmsTemplate(string id, SaveSmsTemplateRequest req)
    {
        var t = await db.SmsTemplates.FindAsync(id);
        if (t is null) return NotFound();
        var name = (req.Name ?? "").Trim();
        var text = (req.Text ?? "").Trim();
        if (name.Length == 0 || text.Length == 0) return BadRequest(new { message = "Nom va matn kerak" });
        t.Name = name; t.Text = text;
        await db.SaveChangesAsync();
        return new SmsTemplateDto(t.Id, t.Name, t.Text, t.Order);
    }

    [HttpDelete("sms/templates/{id}")]
    public async Task<IActionResult> DeleteSmsTemplate(string id)
    {
        var t = await db.SmsTemplates.FindAsync(id);
        if (t is null) return NotFound();
        db.SmsTemplates.Remove(t);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Lidga SMS yuborish ----------

    /// <summary>Lid uchun SMS matnini tayyorlaydi (token render — sinov darsi jadvali bilan) va
    /// navbat oluvchisini qaytaradi. Telefon: Phone→Father→Mother; raqami yo'q lid uchun null.
    /// YUBORMAYDI — yuborish <see cref="SmsQueueService"/> orqali (lid tarixi ham o'sha yerda yoziladi).</summary>
    private async Task<SmsQueueService.Target?> BuildLeadTargetAsync(Lead lead, string text, string centerName)
    {
        var phone = !string.IsNullOrWhiteSpace(lead.Phone) ? lead.Phone
            : !string.IsNullOrWhiteSpace(lead.FatherPhone) ? lead.FatherPhone : lead.MotherPhone;
        if (string.IsNullOrWhiteSpace(phone)) return null;

        // {dars_sana}/{dars_vaqti} uchun — lidning eng so'nggi sinov darsi (avval "pending"ini olamiz).
        var trial = await db.TrialLessons.Where(t => t.LeadId == lead.Id && t.Result == "pending")
                        .OrderByDescending(t => t.ScheduledAt).FirstOrDefaultAsync()
                    ?? await db.TrialLessons.Where(t => t.LeadId == lead.Id)
                        .OrderByDescending(t => t.ScheduledAt).FirstOrDefaultAsync();
        var trialGroup = trial is not null && !string.IsNullOrWhiteSpace(trial.GroupId)
            ? await db.Classes.FindAsync(trial.GroupId) : null;
        // {oqituvchi} — sinov darsi guruhining o'qituvchisi.
        var trialTeacher = await MessageTokenizer.GroupTeacherNameAsync(db, trialGroup);
        var msg = MessageTokenizer.Lead(text, lead, phone, centerName,
            group: trialGroup, trialAt: trial?.ScheduledAt, teacherName: trialTeacher);
        return new SmsQueueService.Target(phone, lead.FullName, msg, lead.Id);
    }

    /// <summary>Lid tarixiga (timeline) yoziladigan izoh matni — yuborilgandan keyin navbat yozadi.</summary>
    private static string LeadSmsNote(string text) =>
        "SMS yuborildi: " + (text.Length > 140 ? text[..140] + "…" : text);

    /// <summary>
    /// Qo'lda yuborib bo'lmaydigan token (hozircha <c>{link}</c>) bo'lsa — xato matni, aks holda null.
    /// </summary>
    /// <remarks>
    /// ⚠️ Bu tekshiruv SERVERDA turishi SHART. Klientda "tayyor matn" ro'yxatidan
    /// <c>test_link</c> olib tashlandi, lekin matnni QO'LDA ham yozish/nusxalash mumkin —
    /// va o'shanda abonentga <c>{link}</c> so'zining O'ZI ketardi (hech qanday xato
    /// ko'rinmasdan, chunki SMS muvaffaqiyatli yuborilgan bo'lardi).
    /// </remarks>
    private static string? ManualTokenError(string text) =>
        MessageTokenCatalog.ForbiddenInManual(text) is { } token
            ? $"«{token}» tokenini qo'lda yuborib bo'lmaydi — u BIR MARTALIK havola va faqat "
              + "«Daraja testi yuborish» bo'limidan (test tanlab) yuboriladi. "
              + "Matndan olib tashlang yoki o'sha bo'limdan yuboring."
            : null;

    [HttpPost("sms/lead")]
    public async Task<ActionResult<SmsBatchDto>> SendLeadSms(SendLeadSmsRequest req)
    {
        var text = (req.Text ?? "").Trim();
        if (text.Length == 0) return BadRequest(new { message = "SMS matni kerak" });
        if (ManualTokenError(text) is { } tokenErr) return BadRequest(new { message = tokenErr });
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == req.LeadId);
        if (lead is null) return NotFound();
        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        // Kanal Sozlamalardan (lid oynasida tanlov yo'q): Local yoqilgan bo'lsa — local, aks holda eskiz.
        var provider = LeadSmsProviderOf(req.Provider, meta);
        if (provider == "eskiz" && !eskiz.IsConfigured(meta))
            return BadRequest(new { message = "Eskiz SMS sozlanmagan. Sozlamalar → SMS (Eskiz)da login/parol kiriting." });
        if (await ValidateLocalSmsAsync(provider, req.AgentId, meta) is { } localErr)
            return BadRequest(new { message = localErr });

        var user = await db.Users.FindAsync(Uid);
        var target = await BuildLeadTargetAsync(lead, text, meta?.Name ?? "");
        if (target is null) return BadRequest(new { message = "Lidda telefon raqami yo'q" });

        return await StartBatchAsync($"Lid: {lead.FullName}", text, [target],
            provider, req.AgentId, user?.FullName ?? "Administrator", LeadSmsNote(text));
    }

    /// <summary>Bir nechta lidga birdan SMS — har lidga sms/lead bilan bir xil mantiq
    /// (tokenlar, telefon tanlash, LeadEvent), hammasi BITTA SmsBatch ostida.</summary>
    [HttpPost("sms/lead-bulk")]
    public async Task<ActionResult<LeadBulkSmsResultDto>> SendLeadBulkSms(SendLeadBulkSmsRequest req)
    {
        var text = (req.Text ?? "").Trim();
        if (text.Length == 0) return BadRequest(new { message = "SMS matni kerak" });
        if (ManualTokenError(text) is { } tokenErr) return BadRequest(new { message = tokenErr });
        var ids = (req.LeadIds ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        if (ids.Count == 0) return BadRequest(new { message = "Kamida bitta lid tanlang" });
        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        // Kanal Sozlamalardan (lid oynasida tanlov yo'q): Local yoqilgan bo'lsa — local, aks holda eskiz.
        var provider = LeadSmsProviderOf(req.Provider, meta);
        if (provider == "eskiz" && !eskiz.IsConfigured(meta))
            return BadRequest(new { message = "Eskiz SMS sozlanmagan. Sozlamalar → SMS (Eskiz)da login/parol kiriting." });
        if (await ValidateLocalSmsAsync(provider, req.AgentId, meta) is { } localErr)
            return BadRequest(new { message = localErr });

        var leads = await db.Leads.Where(l => ids.Contains(l.Id)).ToListAsync();
        var user = await db.Users.FindAsync(Uid);
        var centerName = meta?.Name ?? "";

        // Avval matnlar tayyorlanadi (faqat baza) — yuborishning O'ZI navbat orqali.
        var targets = new List<SmsQueueService.Target>();
        var noPhone = 0;
        foreach (var lead in leads)
        {
            if (await BuildLeadTargetAsync(lead, text, centerName) is { } t) targets.Add(t);
            else noPhone++;
        }
        var notFound = ids.Count - leads.Count;
        if (targets.Count == 0) return new LeadBulkSmsResultDto(0, notFound, noPhone);

        var batch = await StartBatchAsync($"Lidlar (ommaviy): {targets.Count} ta", text, targets,
            provider, req.AgentId, user?.FullName ?? "Administrator", LeadSmsNote(text));

        // Fonda ketayotgan bo'lsa natija hali noma'lum — modal navbat holatini ko'rsatadi.
        return batch.Queued
            ? new LeadBulkSmsResultDto(0, notFound, noPhone, true, targets.Count, batch.Id)
            : new LeadBulkSmsResultDto(batch.SentCount, notFound + (targets.Count - batch.SentCount), noPhone,
                false, 0, batch.Id);
    }

}
