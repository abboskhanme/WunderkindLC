using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// «YO'QOTILGAN LID» — belgilash va tiklashning <b>YAGONA manbai</b>
/// (`.claude/rules/crm-leads.md` «YO'QOTILGAN LIDLAR»).
///
/// <para><b>Bu BOSQICH EMAS.</b> Kanbandagi ustunlarni (<see cref="LeadStage"/>) foydalanuvchi
/// o'zi yaratadi/o'chiradi, ya'ni "yo'qotilgan" ustunini ham o'chirib yuborishi mumkin edi —
/// shunda hisobot va filtrlar jimgina buzilardi. Shuning uchun yo'qotish lidning O'Z maydonlarida
/// (<see cref="Lead.LostAt"/> …), bosqich esa TEGILMAYDI: hisobotda "qaysi bosqichda yo'qotildi"
/// ko'rinadi va tiklanganda lid o'sha joyiga qaytadi. Kanbandagi "Yo'qotilgan" ustuni — DOIMIY,
/// tizim ustuni (klientda chiziladi, bazada qatori yo'q).</para>
///
/// <para>⚠️ Bosqichning BO'SH qiymat bilan ifodalanmagani ATAYIN: <c>Program.cs</c> dagi "yetim
/// lidlarni tuzatish" bloki mavjud bo'lmagan bosqichdagi lidni har restartda birinchi ustunga
/// ko'chiradi — "lost" degan soxta bosqich id'si u yerda jimgina yo'qolib ketardi.</para>
///
/// <para><b>Holat o'tishlari:</b> faqat OCHIQ (aylantirilmagan) lid yo'qotiladi; yo'qotilgan
/// lidni bosqichdan bosqichga surish ham, o'quvchiga aylantirish ham MUMKIN EMAS — avval
/// tiklanadi (aks holda bitta lid bir vaqtda "yo'qotilgan" va "o'quvchi" bo'lib qolardi).</para>
/// </summary>
public static class LeadLoss
{
    /// <summary>Sabablar katalogidagi kategoriya kaliti (<see cref="ActionReason.Category"/>).</summary>
    public const string ReasonCategory = "lead_lost";

    /// <summary>Standart sabablar — kategoriya BO'SH bo'lsa <c>Program.cs</c> seed qiladi.</summary>
    public static readonly IReadOnlyList<string> DefaultReasons =
    [
        "Vaqt to'g'ri kelmadi",
        "Narx qimmatlik qildi",
        "Boshqa kurs/markazga ketdi",
        "Javob bermadi / bog'lanib bo'lmadi",
        "Boshqa",
    ];

    /// <summary>Lid hodisasi turlari (<see cref="LeadEvent.Type"/>). Voronka analitikasi ularni
    /// O'QIMAYDI (u faqat stage/created/convert ga qaraydi) — ya'ni bosqich sanoqlari buzilmaydi.</summary>
    public const string EventLost = "lost";
    public const string EventRestore = "restore";

    /// <summary>Izohning eng uzun qiymati — undan uzuni 400 (jimgina qirqilmaydi).</summary>
    public const int MaxCommentLength = 1000;

    /// <summary>Audit turi — <see cref="AuditSections"/> da "Lidlar" bo'limiga tushadi.</summary>
    public const string AuditEntity = "Lead";

    /// <summary>Yo'qotilgan lidni bosqichdan bosqichga surishga urinilganda (PATCH {id}) javob.</summary>
    public const string StageLockedMessage =
        "Lid yo'qotilganlar ro'yxatida — bosqichni o'zgartirish uchun avval uni tiklang.";

    /// <summary>Yo'qotilgan lidni o'quvchiga aylantirishga urinilganda javob.</summary>
    public const string ConvertLockedMessage =
        "Lid yo'qotilganlar ro'yxatida — o'quvchiga aylantirish uchun avval uni tiklang.";

    /// <summary>Yo'qotilgan lidga sinov darsi belgilashga urinilganda javob (sinov — faol ish).</summary>
    public const string TrialLockedMessage =
        "Lid yo'qotilganlar ro'yxatida — sinov darsiga yozish uchun avval uni tiklang.";

    /// <summary>Lid yo'qotilganmi (bitta ta'rif — hamma joy shundan foydalanadi).</summary>
    public static bool IsLost(Lead lead) => !string.IsNullOrWhiteSpace(lead.LostAt);

    /// <summary>
    /// Belgilash qoidasi (sof). Xato matni qaytaradi; <c>null</c> — mumkin.
    /// <paramref name="reason"/> — bazadan topilgan sabab (topilmasa null).
    /// </summary>
    public static string? ValidateMark(Lead lead, ActionReason? reason, string? comment)
    {
        if (lead.ConvertedStudentId is not null)
            return "Lid allaqachon o'quvchiga aylantirilgan — uni yo'qotilgan deb belgilab bo'lmaydi.";
        if (IsLost(lead)) return "Lid allaqachon yo'qotilganlar ro'yxatida.";
        if (reason is null) return "Yo'qotish sababini tanlang.";
        if (reason.Category != ReasonCategory)
            return "Sabab noto'g'ri — «Lid yo'qotilganda» ro'yxatidan tanlang.";
        if ((comment ?? "").Trim().Length > MaxCommentLength)
            return $"Izoh juda uzun — ko'pi bilan {MaxCommentLength} belgi.";
        return null;
    }

    /// <summary>Amalni bajargan odam (controller claim'lardan yig'adi).</summary>
    public readonly record struct Actor(string? UserId, string Name);

    /// <summary>Amal natijasi: <paramref name="Status"/> — 200 | 400 | 404 (controller shu bilan javob beradi).</summary>
    public readonly record struct Outcome(int Status, string? Error, Lead? Lead)
    {
        public static Outcome Ok(Lead lead) => new(200, null, lead);
        public static Outcome Bad(string error) => new(400, error, null);
        public static Outcome NotFound() => new(404, "Lid topilmadi", null);
    }

    /// <summary>
    /// Lidni YO'QOTILGAN deb belgilaydi: sabab (snapshot), izoh, kim/qachon, lid tarixiga hodisa va
    /// audit — hammasi BITTA <c>SaveChanges</c> da. Telegram kartasini chaqiruvchi KEYIN yangilaydi.
    /// </summary>
    public static async Task<Outcome> MarkLostAsync(
        IAppDbContext db, AuditService audit, string leadId, string? reasonId, string? comment,
        Actor actor, CancellationToken ct = default)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, ct);
        if (lead is null) return Outcome.NotFound();

        var rid = (reasonId ?? "").Trim();
        var reason = rid.Length == 0 ? null
            : await db.ActionReasons.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rid, ct);
        var error = ValidateMark(lead, reason, comment);
        if (error is not null) return Outcome.Bad(error);

        var now = AppClock.Iso();
        var note = (comment ?? "").Trim();
        lead.LostAt = now;
        lead.LostReasonId = reason!.Id;
        lead.LostReason = reason.Label;
        lead.LostComment = note.Length == 0 ? null : note;
        lead.LostByUserId = actor.UserId;
        lead.LostByName = actor.Name;
        // MAS'UL — `ChangeStage` dagi bilan bir xil qoida: biriktirilmagan lidni birinchi bo'lib
        // qo'zg'atgan xodim. Mavjud mas'ul USTIDAN YOZILMAYDI (hisobotdagi "kim yo'qotdi" kesimi
        // lidni ishlagan odamni ko'rsatsin, oxirgi tugmani bosganni emas — u `LostByName` da).
        if (string.IsNullOrWhiteSpace(lead.AssigneeUserId) && !string.IsNullOrWhiteSpace(actor.UserId))
            lead.AssigneeUserId = actor.UserId;

        db.LeadEvents.Add(new LeadEvent
        {
            LeadId = lead.Id, Type = EventLost,
            Text = $"Yo'qotildi: {reason.Label}" + (note.Length > 0 ? $" — {note}" : ""),
            ActorName = actor.Name, ActorUserId = actor.UserId, CreatedAt = now,
            // Qaysi bosqichda yo'qotilgani (ma'lumot uchun; voronka bu turni o'qimaydi).
            FromStage = lead.Stage ?? "",
        });
        audit.Record(AuditEntity, lead.Id, "update",
            $"Lid yo'qotilganlarga o'tkazildi ({lead.FullName}) — sabab: {reason.Label}");
        await db.SaveChangesAsync(ct);
        return Outcome.Ok(lead);
    }

    /// <summary>
    /// Yo'qotilgan lidni TIKLAYDI (yana faol ish). <paramref name="stage"/> berilsa lid o'sha
    /// bosqichga o'tadi — bu alohida "stage" hodisasi bilan yoziladi (voronka uchun
    /// <c>FromStage</c>/<c>ToStage</c>), aks holda o'z bosqichida qoladi.
    /// </summary>
    public static async Task<Outcome> RestoreAsync(
        IAppDbContext db, AuditService audit, string leadId, string? stage, Actor actor,
        CancellationToken ct = default)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, ct);
        if (lead is null) return Outcome.NotFound();
        if (!IsLost(lead)) return Outcome.Bad("Lid yo'qotilganlar ro'yxatida emas.");

        var target = (stage ?? "").Trim();
        LeadStage? targetStage = null;
        if (target.Length > 0)
        {
            targetStage = await db.LeadStages.AsNoTracking().FirstOrDefaultAsync(s => s.Id == target, ct);
            if (targetStage is null) return Outcome.Bad("Bosqich topilmadi — sahifani yangilang.");
        }

        var now = AppClock.Iso();
        var reason = lead.LostReason ?? "";
        Clear(lead);
        db.LeadEvents.Add(new LeadEvent
        {
            LeadId = lead.Id, Type = EventRestore,
            Text = "Tiklandi (yo'qotilganlardan qaytarildi)" + (reason.Length > 0 ? $" — avvalgi sabab: {reason}" : ""),
            ActorName = actor.Name, ActorUserId = actor.UserId, CreatedAt = now,
        });
        if (targetStage is not null && targetStage.Id != lead.Stage)
        {
            var from = lead.Stage ?? "";
            lead.Stage = targetStage.Id;
            db.LeadEvents.Add(new LeadEvent
            {
                LeadId = lead.Id, Type = LeadAnalytics.TypeStage, Text = $"Bosqich: {targetStage.Title}",
                ActorName = actor.Name, ActorUserId = actor.UserId, CreatedAt = now,
                FromStage = from, ToStage = targetStage.Id,
            });
        }
        audit.Record(AuditEntity, lead.Id, "update", $"Lid tiklandi ({lead.FullName}) — yo'qotilganlardan qaytarildi");
        await db.SaveChangesAsync(ct);
        return Outcome.Ok(lead);
    }

    /// <summary>Barcha "yo'qotilgan" maydonlarini tozalaydi.</summary>
    public static void Clear(Lead lead)
    {
        lead.LostAt = null;
        lead.LostReasonId = null;
        lead.LostReason = null;
        lead.LostComment = null;
        lead.LostByUserId = null;
        lead.LostByName = null;
    }

    /// <summary>Klientga qaytariladigan holat (mas'ul ismi JORIY ro'yxatdan).</summary>
    public static async Task<LeadLossStateDto> StateAsync(IAppDbContext db, Lead lead, CancellationToken ct = default)
    {
        var assigneeName = string.IsNullOrWhiteSpace(lead.AssigneeUserId) ? null
            : await db.Users.AsNoTracking().Where(u => u.Id == lead.AssigneeUserId)
                .Select(u => u.FullName).FirstOrDefaultAsync(ct);
        return new LeadLossStateDto(
            lead.Id, lead.Stage ?? "", lead.LostAt, lead.LostReasonId, lead.LostReason, lead.LostComment,
            lead.LostByName, lead.AssigneeUserId, assigneeName);
    }
}
