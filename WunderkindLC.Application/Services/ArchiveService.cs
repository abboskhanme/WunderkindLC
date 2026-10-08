using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Arxiv (soft-delete surat) xizmati. O'chirish endpointlari entity'ni hard-delete
/// qilishdan OLDIN bu yerda <see cref="ArchivedRecord"/> yaratadi — entity'ning to'liq
/// JSON surati saqlanadi, shuning uchun keyinchalik ko'rish va TIKLASH mumkin.
///
/// <para>⚠️ <b>MAXFIY MAYDONLAR SURATGA TUSHMAYDI</b> (<see cref="StripSecrets"/>): parol xeshi,
/// OCHIQ dastlabki parol, har qanday token/secret. Ilgari o'chirilgan xodimning to'liq
/// <c>AppUser</c> obyekti (<c>PasswordHash</c> + ochiq <c>InitialPassword</c>) shu yerda saqlanib,
/// tungi JSON zaxirasi bilan Telegram'ga ham ketardi. Tiklangan akkaunt shuning uchun PAROLSIZ
/// qaytadi — admin parolni qayta o'rnatadi (tiklash javobida aytiladi).</para>
/// </summary>
public static class ArchiveService
{
    /// <summary>
    /// Lid o'chirilganda uning HODISALARI va SINOV DARSLARI alohida yozuvga tushadi (CRM-4).
    /// <see cref="ArchivedRecord.EntityId"/> = lid id'si, JSON = <see cref="LeadHistorySnapshot"/>.
    /// Bu tur arxiv ro'yxatida KO'RINMAYDI — u lid bilan BIRGA tiklanadi va birga o'chiriladi.
    /// </summary>
    public const string TypeLeadHistory = "lead_history";

    /// <summary>Arxiv ro'yxati va sanog'ida ko'rsatilmaydigan (yordamchi) turlar.</summary>
    public static readonly IReadOnlyList<string> HiddenTypes = [TypeLeadHistory];

    /// <summary>Lid tarixi surati — <c>new { Events, Trials }</c> (System.Text.Json standart nomlari).</summary>
    public sealed record LeadHistorySnapshot(List<LeadEvent>? Events, List<TrialLesson>? Trials);

    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// O'chirilayotgan <paramref name="entity"/> uchun arxiv surati qo'shadi (SaveChanges'ni
    /// chaqiruvchi bajaradi — odatda mavjud delete SaveChanges'iga qo'shilib ketadi).
    /// </summary>
    public static void Snapshot(
        IAppDbContext db, string type, string entityId, string title, string subtitle,
        object entity, string? reason, string actorName)
    {
        db.ArchivedRecords.Add(new ArchivedRecord
        {
            Type = type,
            EntityId = entityId,
            Title = title ?? string.Empty,
            Subtitle = subtitle ?? string.Empty,
            Json = StripSecrets(JsonSerializer.Serialize(entity)),
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason,
            DeletedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            ActorName = string.IsNullOrWhiteSpace(actorName) ? "Admin" : actorName,
        });
    }

    /// <summary>
    /// Maydon nomi maxfiymi: parol (<c>PasswordHash</c>, <c>InitialPassword</c>, ...), token
    /// (<c>...Token</c>, <c>...TokenHash</c>) yoki secret. Registrsiz. Sof funksiya.
    /// </summary>
    public static bool IsSecretField(string name) =>
        name.Contains("password", StringComparison.OrdinalIgnoreCase)
        || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("token", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("tokenhash", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// JSON suratdan maxfiy maydonlarni (ichma-ich obyektlarda ham) olib tashlaydi. JSON emas
    /// (buzilgan) matn — o'zgarishsiz qaytadi. Sof funksiya.
    /// </summary>
    public static string StripSecrets(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException) { return json; }
        if (node is null || !Strip(node)) return json;
        return node.ToJsonString();
    }

    /// <summary>Tugundan maxfiy maydonlarni olib tashlaydi; biror narsa o'zgardimi — qaytaradi.</summary>
    private static bool Strip(JsonNode node)
    {
        var changed = false;
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kv => kv.Key).Where(IsSecretField).ToList())
                {
                    obj.Remove(key);
                    changed = true;
                }
                foreach (var (_, child) in obj.ToList())
                    if (child is not null && Strip(child)) changed = true;
                break;
            case JsonArray arr:
                foreach (var child in arr)
                    if (child is not null && Strip(child)) changed = true;
                break;
        }
        return changed;
    }

    /// <summary>
    /// ESKI suratlarni tozalash — bu tuzatishdan OLDIN yozilgan yozuvlarda parol xeshi va ochiq
    /// dastlabki parol hamon turibdi (ular tungi zaxiraga ham tushadi). IDEMPOTENT: tozalangan
    /// yozuv qayta topilmaydi. Startupda chaqiriladi. Qaytaradi: nechta yozuv tozalandi.
    /// </summary>
    public static async Task<int> ScrubStoredSecretsAsync(IAppDbContext db, CancellationToken ct = default)
    {
        // SQL tomonda qo'pol tanlov (kalit nomi bo'lishi mumkin bo'lgan matnlar), aniq qaror — StripSecrets'da.
        var rows = await db.ArchivedRecords
            .Where(r => r.Json.Contains("Password") || r.Json.Contains("Token\"")
                        || r.Json.Contains("TokenHash\"") || r.Json.Contains("Secret"))
            .ToListAsync(ct);
        var changed = 0;
        foreach (var r in rows)
        {
            var clean = StripSecrets(r.Json);
            if (clean == r.Json) continue;
            r.Json = clean;
            changed++;
        }
        if (changed > 0) await db.SaveChangesAsync(ct);
        return changed;
    }

    /// <summary>
    /// Tiklanayotgan lid bilan telefon kaliti (<see cref="PhoneUtil.Key"/>) bir xil BOSHQA lidning ismi
    /// (yo'q bo'lsa <c>null</c>). O'chirilgandan keyin shu raqam bilan yangi lid ochilgan bo'lsa tiklash
    /// uni jimgina "ikkinchi nusxa" qilmasin — telefon bo'yicha qidiruvda (<c>LeadIntake.FindByPhoneAsync</c>,
    /// eng ESKI yaratilgani) eski yozuv birinchi chiqib, yangisini to'sib qo'yardi.
    /// </summary>
    public static async Task<string?> ConflictingLeadNameAsync(IAppDbContext db, Lead lead, CancellationToken ct = default)
    {
        var key = PhoneUtil.Key(lead.Phone);
        if (key.Length < 7) return null;
        return await db.Leads.AsNoTracking()
            .Where(l => l.PhoneKey == key && l.Id != lead.Id)
            .OrderBy(l => l.CreatedAt)
            .Select(l => l.FullName)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Lid TIKLANGANDA uning tarixini (<see cref="TypeLeadHistory"/>) qaytaradi: hodisalar va sinov
    /// darslari qayta qo'shiladi (allaqachon bori — o'tkazib yuboriladi), yordamchi yozuv o'chiriladi.
    /// Aks holda lid "yalang'och" qaytardi: tarix bo'sh, kanal "Boshqa" (kanal <c>created</c>
    /// hodisasidan aniqlanadi — <see cref="LeadOrigins"/>), sinov darsi yo'q. <c>SaveChanges</c> QILMAYDI.
    /// </summary>
    public static async Task<(int Events, int Trials)> RestoreLeadHistoryAsync(
        IAppDbContext db, string leadId, CancellationToken ct = default)
    {
        var records = await db.ArchivedRecords
            .Where(r => r.Type == TypeLeadHistory && r.EntityId == leadId)
            .ToListAsync(ct);
        int events = 0, trials = 0;
        foreach (var r in records)
        {
            var h = ReadLeadHistory(r.Json);
            var evs = (h.Events ?? new()).Where(e => !string.IsNullOrEmpty(e.Id)).ToList();
            var evIds = evs.Select(e => e.Id).ToList();
            var existingEv = await db.LeadEvents.Where(e => evIds.Contains(e.Id)).Select(e => e.Id).ToListAsync(ct);
            foreach (var e in evs.Where(e => !existingEv.Contains(e.Id)))
            {
                e.LeadId = leadId;
                db.LeadEvents.Add(e);
                events++;
            }
            var trs = (h.Trials ?? new()).Where(t => !string.IsNullOrEmpty(t.Id)).ToList();
            var trIds = trs.Select(t => t.Id).ToList();
            var existingTr = await db.TrialLessons.Where(t => trIds.Contains(t.Id)).Select(t => t.Id).ToListAsync(ct);
            foreach (var t in trs.Where(t => !existingTr.Contains(t.Id)))
            {
                t.LeadId = leadId;
                db.TrialLessons.Add(t);
                trials++;
            }
            db.ArchivedRecords.Remove(r);
        }
        return (events, trials);
    }

    /// <summary>Lid suratiga tegishli yordamchi yozuvlar (tarix) — lid surati butunlay o'chirilganda birga o'chadi.</summary>
    public static Task<List<ArchivedRecord>> LeadHistoryRecordsAsync(IAppDbContext db, string leadId, CancellationToken ct = default) =>
        db.ArchivedRecords.Where(r => r.Type == TypeLeadHistory && r.EntityId == leadId).ToListAsync(ct);

    /// <summary>Lid tarixi suratini o'qiydi (buzilgan/bo'sh — bo'sh ro'yxatlar).</summary>
    public static LeadHistorySnapshot ReadLeadHistory(string json)
    {
        try
        {
            var h = JsonSerializer.Deserialize<LeadHistorySnapshot>(json, ReadOpts);
            return new LeadHistorySnapshot(h?.Events ?? new(), h?.Trials ?? new());
        }
        catch (JsonException)
        {
            return new LeadHistorySnapshot(new(), new());
        }
    }
}
