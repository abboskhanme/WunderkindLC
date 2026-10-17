using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// LIDGA AVTOMATIK TOPSHIRIQ (SPEC-backlog-2026-10-17 F4 L1; TZ 3.3: "lid ma'lumot qoldirganda administratorga
/// qo'ng'iroq qilish vazifasini avtomatik biriktirish va bildirishnoma yuborish").
///
/// <para><b>Qachon:</b> lid TASHQI kanaldan keldi (forma, landing, daraja testi, Instagram, reklama formasi) —
/// <see cref="LeadNotifier.NotifyNewLeadAsync"/> ichidan, Telegram tekshiruvidan OLDIN (bot sozlanmagan markazda ham
/// ishlaydi). Takroriy murojaatda ham — agar lidda ochiq topshiriq qolmagan bo'lsa. ⚠️ QO'LDA kiritilgan lidga
/// YARATILMAYDI: uni kiritgan xodim lid bilan allaqachon ishlayapti.</para>
///
/// <para><b>Kimga:</b> lidning mas'uli (bo'lsa, panel akkaunti) — aks holda sozlamadagi xodimlar NAVBAT bilan
/// (<see cref="Pick"/>). Lidning o'z mas'uli ATAYIN o'zgartirilmaydi (KPI "biriktirilmagan lid" qoidasi tegilmasin).</para>
///
/// <para><b>Bir lid — bitta ochiq topshiriq:</b> ochiq (yopilmagan, arxivlanmagan) <see cref="WorkTask.LeadId"/> bo'lsa
/// yangisi yaratilmaydi. Yopilgan/yo'qotilgan (aylantirilgan, <c>LostAt</c>) lidga yaratilmaydi.</para>
///
/// <para>⚠️ <b>Hech qachon istisno chiqarmaydi</b> (<c>OperationCanceledException</c> dan tashqari): topshiriq — yordamchi
/// ish, lid qabul qilinishini buzmasin. Saqlash yiqilsa O'Z qatorlari kontekstdan chiqariladi — chaqiruvchining
/// keyingi <c>SaveChanges</c>i buzuq INSERT'ni qayta urinmasin (<see cref="LeadNotifier"/> dagi saboq).</para>
/// </summary>
public static class LeadAutoTask
{
    /// <summary>Jarayon qulfi — bir vaqtda kelgan ikki lid bitta "oxirgi avto-topshiriq"ni ko'rib, bir xodimga tushmasin.</summary>
    public const string LockKey = "lead-auto-task";

    /// <summary>Topshiriq muhimligi — «Yuqori» (yangi lidga tez qo'ng'iroq qilinishi kerak).</summary>
    public const int Priority = 2;

    /// <summary>
    /// Mas'ulni tanlaydi. <paramref name="leadAssignee"/> panel akkaunti bo'lsa — o'sha; aks holda <paramref name="pool"/> ning
    /// tirik a'zolaridan <paramref name="cursor"/> o'rnidagi xodim (cursor — oxirgi avto-topshiriq olgan xodimdan keyingisi). Hovuz bo'sh —
    /// <c>null</c> (topshiriq mas'ulsiz yaratiladi, doskada ko'rinadi).
    /// </summary>
    public static (string? AssigneeId, int NextCursor) Pick(
        IReadOnlyList<string> pool, string? leadAssignee, int cursor, ISet<string> validUsers)
    {
        if (!string.IsNullOrWhiteSpace(leadAssignee) && validUsers.Contains(leadAssignee)) return (leadAssignee, cursor);
        var live = pool.Where(validUsers.Contains).ToList();
        if (live.Count == 0) return (null, cursor);
        var i = ((cursor % live.Count) + live.Count) % live.Count;
        return (live[i], (i + 1) % live.Count);
    }

    /// <summary>Lid (o'zi + ota/ona) telefonlari — birinchi bo'sh bo'lmagani.</summary>
    public static string PhoneOf(Lead l) =>
        new[] { l.Phone, l.FatherPhone, l.MotherPhone }.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p))?.Trim() ?? "";

    public static string TitleFor(Lead l, bool repeat)
    {
        var name = string.IsNullOrWhiteSpace(l.FullName) ? "lid" : l.FullName.Trim();
        return (repeat ? "Takroriy murojaat — qo'ng'iroq: " : "Lidga qo'ng'iroq: ") + name;
    }

    public static string DescriptionFor(Lead l)
    {
        var lines = new List<string>();
        var phone = PhoneOf(l);
        if (phone.Length > 0) lines.Add($"📞 {phone}");
        if (!string.IsNullOrWhiteSpace(l.Source)) lines.Add($"Manba: {l.Source.Trim()}");
        if (!string.IsNullOrWhiteSpace(l.InterestSubject)) lines.Add($"Qiziqishi: {l.InterestSubject.Trim()}");
        lines.Add("Lid ma'lumot qoldirdi — qo'ng'iroq qilib, natijani lid sahifasiga yozing.");
        return string.Join("\n", lines);
    }

    /// <summary>Panel akkauntlari (topshiriq mas'uli bo'la oladiganlar).</summary>
    private static readonly string[] PanelRoles = [Roles.Staff, Roles.Admin, Roles.SuperAdmin];

    /// <summary>
    /// Sozlama yoqilgan bo'lsa lidga ochiq topshiriq borligini ta'minlaydi (yo'q bo'lsa yaratadi, saqlaydi va mas'ulga
    /// qo'ng'iroqcha + Telegram yuboradi). Yaratilgan topshiriqni qaytaradi (aks holda <c>null</c>).
    /// </summary>
    public static async Task<WorkTask?> EnsureAsync(
        IAppDbContext db, TelegramService? telegram, Lead lead, bool repeat,
        CancellationToken ct = default, ILogger? logger = null)
    {
        WorkTask? task = null;
        WorkTaskEvent? ev = null;
        WorkTaskBoard? newBoard = null;
        List<WorkTaskColumn>? newColumns = null;
        UserNotification? bell = null;
        try
        {
            if (lead.ConvertedStudentId != null || lead.LostAt != null) return null;
            if (!await db.CenterMeta.AsNoTracking().AnyAsync(m => m.LeadAutoTaskEnabled, ct)) return null;

            using var _ = await PaymentIdempotency.LockAsync(LockKey);

            // ⚠️ Sozlama va NAVBAT qulf ICHIDA va kuzatuvsiz (AsNoTracking) o'qiladi: aks holda bir vaqtda kelgan ikki lid
            // bitta eski navbat qiymatini ko'rib, ikkalasi bir xodimga tushardi (chaqiruvchi kontekstda CenterMeta allaqachon
            // kuzatilayotgan bo'lsa ham — identity resolution eski obyektni qaytarardi).
            var meta = await db.CenterMeta.AsNoTracking()
                .Select(m => new { m.LeadAutoTaskEnabled, m.LeadAutoTaskAssigneeIds, m.LeadAutoTaskBoardId })
                .FirstOrDefaultAsync(ct);
            if (meta is null || !meta.LeadAutoTaskEnabled) return null;

            if (await db.WorkTasks.AnyAsync(t => t.LeadId == lead.Id && t.CompletedAt == null && !t.IsArchived, ct))
                return null;

            // Doska: sozlamadagisi (faol bo'lsa) → birinchi faol doska → yangi "Umumiy".
            var board = string.IsNullOrWhiteSpace(meta.LeadAutoTaskBoardId) ? null
                : await db.WorkTaskBoards.FirstOrDefaultAsync(b => b.Id == meta.LeadAutoTaskBoardId && !b.IsArchived, ct);
            board ??= await db.WorkTaskBoards.Where(b => !b.IsArchived).OrderBy(b => b.Order).ThenBy(b => b.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (board is null)
            {
                newBoard = await WorkTaskFlow.AddDefaultBoardIfNoneAsync(db, "", ct);
                if (newBoard is null) { logger?.LogWarning("Lid avto-topshirig'i: faol doska yo'q (lid {LeadId})", lead.Id); return null; }
                board = newBoard;
                newColumns = db.WorkTaskColumns.Local.Where(c => c.BoardId == board.Id).ToList();
            }
            var column = newColumns?.Where(c => !c.IsDone).OrderBy(c => c.Order).FirstOrDefault()
                         ?? await db.WorkTaskColumns.Where(c => c.BoardId == board.Id && !c.IsDone)
                             .OrderBy(c => c.Order).FirstOrDefaultAsync(ct);
            if (column is null) { logger?.LogWarning("Lid avto-topshirig'i: doskada ochiq ustun yo'q (lid {LeadId})", lead.Id); return null; }

            var candidates = meta.LeadAutoTaskAssigneeIds.Append(lead.AssigneeUserId ?? "").Where(x => x.Length > 0).Distinct().ToList();
            var valid = (await db.Users.AsNoTracking()
                    .Where(u => candidates.Contains(u.Id) && PanelRoles.Contains(u.Role))
                    .Select(u => u.Id).ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);
            // NAVBAT SAQLANMAYDI — oxirgi avto-topshiriq KIMGA tushganidan aniqlanadi (qulf ichida, topshiriq saqlangandan
            // keyin keyingi so'rov uni ko'radi). Ilgari CenterMeta.LeadAutoTaskCursor yozilardi: kuzatilayotgan eski
            // obyekt ikki lidni bir xodimga berardi va har lid CenterMeta'ga bog'liq barcha keshlarni bekor qilardi (revyu S1).
            var live = meta.LeadAutoTaskAssigneeIds.Where(valid.Contains).ToList();
            var lastAuto = live.Count == 0 ? null : await db.WorkTasks.AsNoTracking()
                .Where(t => t.LeadId != null && t.CreatedById == "" && live.Contains(t.AssigneeId))
                .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
                .Select(t => t.AssigneeId).FirstOrDefaultAsync(ct);
            var cursor = lastAuto is null ? 0 : live.IndexOf(lastAuto) + 1;
            var (assignee, _) = Pick(meta.LeadAutoTaskAssigneeIds, lead.AssigneeUserId, cursor, valid);

            var order = newColumns is not null ? 0
                : await db.WorkTasks.Where(t => t.ColumnId == column.Id).Select(t => (int?)t.Order).MinAsync(ct) ?? 0;
            var today = AppClock.Today.ToString("yyyy-MM-dd");
            task = new WorkTask
            {
                BoardId = board.Id,
                ColumnId = column.Id,
                Title = TitleFor(lead, repeat),
                Description = DescriptionFor(lead),
                AssigneeId = assignee ?? "",
                CreatedById = "",                 // tizim — tasdiqlovchi yo'q
                Priority = Priority,
                DueDate = today,
                Order = order - 1,
                ApprovalRequired = false,
                LeadId = lead.Id,
            };
            db.WorkTasks.Add(task);
            ev = new WorkTaskEvent
            {
                TaskId = task.Id, ActorId = "", Kind = "created",
                Text = repeat
                    ? $"Avtomatik yaratildi: lid qayta murojaat qildi («{lead.FullName}»)"
                    : $"Avtomatik yaratildi: yangi lid («{lead.FullName}»)",
            };
            db.WorkTaskEvents.Add(ev);
            if (assignee is not null)
            {
                bell = new UserNotification
                {
                    UserId = assignee,
                    Title = repeat ? "Lid qayta murojaat qildi — qo'ng'iroq qiling" : "Yangi lid — qo'ng'iroq qiling",
                    Body = $"{lead.FullName} · {PhoneOf(lead)} — Topshiriqlar",
                    Type = "tasks",
                };
                db.UserNotifications.Add(bell);
            }
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Lid avto-topshirig'ini yaratib bo'lmadi (lid {LeadId})", lead.Id);
            // O'z qatorlarimiz kontekstdan chiqariladi (Added → Remove = Detached).
            if (bell is not null) db.UserNotifications.Remove(bell);
            if (ev is not null) db.WorkTaskEvents.Remove(ev);
            if (task is not null) db.WorkTasks.Remove(task);
            if (newColumns is not null) foreach (var c in newColumns) db.WorkTaskColumns.Remove(c);
            if (newBoard is not null) db.WorkTaskBoards.Remove(newBoard);
            return null;
        }

        if (telegram is not null && !string.IsNullOrWhiteSpace(task.AssigneeId)) await NotifyTelegramAsync(db, telegram, task, ct, logger);
        return task;
    }

    /// <summary>
    /// <see cref="EnsureAsync"/> lid id'si bo'yicha — Telegram kartasi O'CHIRILGAN kanallar uchun (Instagram/Meta:
    /// <c>CenterMeta.InstagramNotifyTelegram = false</c> bo'lsa <see cref="LeadNotifier.NotifyNewLeadAsync"/> umuman
    /// chaqirilmaydi, topshiriq esa baribir kerak — 2026-10-17 revyu, S4).
    /// </summary>
    public static async Task EnsureByIdAsync(
        IAppDbContext db, TelegramService? telegram, string leadId, bool repeat,
        CancellationToken ct = default, ILogger? logger = null)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, ct);
        if (lead is not null) await EnsureAsync(db, telegram, lead, repeat, ct, logger);
    }

    /// <summary>Mas'ulga "🆕 Sizga yangi topshiriq" + «✅ Bajardim» — panel bilan AYNAN bir xil matn
    /// (<see cref="WorkTaskTelegram"/>). Xato jim (log) — xabar topshiriqni buzmaydi.</summary>
    private static async Task NotifyTelegramAsync(
        IAppDbContext db, TelegramService telegram, WorkTask t, CancellationToken ct, ILogger? logger)
    {
        try
        {
            if (!telegram.IsConfigured) return;
            var chats = await db.TelegramRegistrations.AsNoTracking()
                .Where(r => r.UserId == t.AssigneeId).Select(r => r.ChatId).Distinct().ToListAsync(ct);
            if (chats.Count == 0) return;
            var boardTitle = await db.WorkTaskBoards.Where(b => b.Id == t.BoardId).Select(b => b.Title).FirstOrDefaultAsync(ct) ?? "";
            var text = WorkTaskTelegram.NewTaskText(t, boardTitle, "Tizim (avtomatik)", AppClock.Today.ToString("yyyy-MM-dd"));
            foreach (var chatId in chats)
                await telegram.SendMessageAsync(chatId, text, WorkTaskTelegram.DoneKeyboard(t.Id));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Lid avto-topshirig'i: Telegram xabari ketmadi (topshiriq {TaskId})", t.Id);
        }
    }
}
