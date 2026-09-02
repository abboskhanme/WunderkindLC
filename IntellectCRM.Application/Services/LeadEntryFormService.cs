using Microsoft.EntityFrameworkCore;
using IntellectCRM.Application.Abstractions;
using IntellectCRM.Application.Dtos;
using IntellectCRM.Domain;

namespace IntellectCRM.Application.Services;

/// <summary>
/// «LID KIRITISH FORMASI» — sozlamani O'QISH, SAQLASH va lidni TEKSHIRISH (baza bilan ishlaydigan
/// qismi; sof qoidalar <see cref="LeadEntryRules"/> da).
///
/// <para>Sozlama BITTA (markazda bitta CRM formasi) — shuning uchun <c>FormId</c> yo'q, bandlar
/// to'g'ridan-to'g'ri <c>LeadEntryFields</c> jadvalida turadi.</para>
///
/// <para>⚠️ Tekshiruv (<see cref="ValidateAsync"/>) FAQAT admin CRM formasidan chaqiriladi
/// (<c>LeadsController</c> Create/Update). Ommaviy lid formasi, daraja testi, landing, Instagram
/// va Meta leadgen o'z yo'llari bilan lid yaratadi — u yerda bu qoidalar QO'LLANMAYDI
/// (<see cref="LeadEntryRules"/> izohiga qarang).</para>
/// </summary>
public static class LeadEntryFormService
{
    /// <summary>Saqlangan bandlar (standart holatlar + qo'shimcha savollar), tartib bo'yicha.</summary>
    public static async Task<List<LeadEntryField>> LoadAsync(IAppDbContext db) =>
        await db.LeadEntryFields.AsNoTracking().OrderBy(x => x.Order).ThenBy(x => x.Label).ToListAsync();

    /// <summary>
    /// EFFEKTIV standart qoidalar: har bir katalog maydoni uchun BITTA qator qaytadi.
    ///
    /// <para>⚠️ Saqlangan qatori BO'LMAGAN maydon ham qaytadi (ko'rinadi, majburiy emas) — ya'ni
    /// bo'sh baza standart formaning O'ZI: eski o'rnatishlarni to'ldirish (backfill) kerak emas.</para>
    /// </summary>
    public static List<LeadEntryField> EffectiveStandard(IEnumerable<LeadEntryField> rows)
    {
        var stored = rows.Where(r => r.Key.Length > 0)
            .GroupBy(r => r.Key)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var states = LeadEntryRules.Standard.ToDictionary(
            d => d.Key,
            d => stored.TryGetValue(d.Key, out var r)
                ? LeadEntryRules.StateOf(r.Visible, r.Required)
                : LeadEntryRules.StateOptional,
            StringComparer.Ordinal);

        return LeadEntryRules.Standard.Select(def =>
        {
            var (visible, required) = LeadEntryRules.NormalizeStandard(def.Key, states[def.Key], states);
            return new LeadEntryField
            {
                Id = stored.TryGetValue(def.Key, out var r) ? r.Id : def.Key,
                Key = def.Key,
                Label = def.Label,
                Visible = visible,
                Required = required,
            };
        }).ToList();
    }

    /// <summary>Qo'shimcha savollar (saqlangan tartibda).</summary>
    public static List<LeadEntryField> CustomOf(IEnumerable<LeadEntryField> rows) =>
        rows.Where(r => r.Key.Length == 0).OrderBy(r => r.Order).ToList();

    /// <summary>Klient uchun to'liq sozlama.</summary>
    public static LeadEntryFormDto BuildDto(IEnumerable<LeadEntryField> rows)
    {
        var list = rows.ToList();
        var standard = EffectiveStandard(list)
            .Select(r => new LeadEntryStandardDto(
                r.Key,
                LeadEntryRules.FindStandard(r.Key)?.Label ?? r.Label,
                LeadEntryRules.StateOf(r.Visible, r.Required),
                LeadEntryRules.FindStandard(r.Key)?.Locked ?? false))
            .ToList();
        var fields = CustomOf(list)
            .Select(r => new LeadEntryFieldDto(
                r.Id, r.Label, r.Kind, r.Options, r.Placeholder, r.Required, r.Order))
            .ToList();
        return new LeadEntryFormDto(standard, fields);
    }

    /// <summary>
    /// Sozlamani saqlaydi — bandlar TO'LIQ almashtiriladi (lid formasidagi <c>WriteFields</c> bilan
    /// bir xil, sodda va ishonchli usul). Javoblar savol MATNI bo'yicha saqlangani uchun
    /// (<see cref="Lead.AnswersJson"/>) qatorlarning id'si o'zgarishi tarixni buzmaydi.
    /// </summary>
    public static async Task SaveAsync(IAppDbContext db, LeadEntryFormPayload p)
    {
        db.LeadEntryFields.RemoveRange(await db.LeadEntryFields.ToListAsync());

        // ---- Standart maydonlar ----
        var wanted = (p.Standard ?? new())
            .Where(x => LeadEntryRules.IsStandardKey(x.Key))
            .GroupBy(x => x.Key)
            .ToDictionary(g => g.Key, g => LeadEntryRules.NormalizeState(g.First().State), StringComparer.Ordinal);
        var states = LeadEntryRules.Standard.ToDictionary(
            d => d.Key,
            d => wanted.TryGetValue(d.Key, out var s) ? s : LeadEntryRules.StateOptional,
            StringComparer.Ordinal);

        var order = 0;
        foreach (var def in LeadEntryRules.Standard)
        {
            var (visible, required) = LeadEntryRules.NormalizeStandard(def.Key, states[def.Key], states);
            db.LeadEntryFields.Add(new LeadEntryField
            {
                Key = def.Key,
                Label = def.Label,
                Kind = LeadFormService.KindText,
                Visible = visible,
                Required = required,
                Order = order++,
            });
        }

        // ---- Qo'shimcha savollar ----
        foreach (var f in (p.Fields ?? new()).Take(LeadEntryRules.MaxFields))
        {
            var row = LeadEntryRules.CleanCustom(f.Label, f.Kind, f.Options, f.Placeholder, f.Required, order);
            if (row is null) continue; // yorliqsiz savol — menejerga ma'nosiz
            order++;
            db.LeadEntryFields.Add(row);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Lid saqlashdan OLDINGI tekshiruv.
    ///
    /// <para><paramref name="answers"/> <c>null</c> bo'lsa qo'shimcha savollar UMUMAN tegilmaydi
    /// (<c>Json</c> = null qaytadi — chaqiruvchi mavjud javoblarni SAQLAB qoladi). Bu tahrirlash
    /// uchun: qo'shimcha savollarni bilmaydigan chaqiruvchi lidning javoblarini jimgina
    /// o'chirib yubormasin. YARATISHDA esa chaqiruvchi bo'sh lug'at uzatadi, ya'ni majburiy
    /// savol baribir tekshiriladi.</para>
    /// </summary>
    public static async Task<(string? Error, string? Json)> ValidateAsync(
        IAppDbContext db, Lead lead, IReadOnlyDictionary<string, List<string>>? answers)
    {
        var rows = await LoadAsync(db);

        var standardError = LeadEntryRules.ValidateStandard(EffectiveStandard(rows), lead);
        if (standardError is not null) return (standardError, null);

        if (answers is null) return (null, null);

        var (list, error) = LeadEntryRules.BuildAnswers(CustomOf(rows), answers);
        if (error is not null) return (error, null);
        return (null, list!.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(list) : "");
    }
}
