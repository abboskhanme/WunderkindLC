using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace WunderkindLC.Application.Services;

/// <summary>
/// SOZLAMA O'ZGARISHINI "O'zgarishlar tarixi"ga yozish — yagona yordamchi (ilgari faqat
/// <c>SettingsController.LogSettings</c> ichida edi, shu sababdan boshqa controllerlardagi
/// PULGA ta'sir qiladigan sozlamalar — ushlab turish bonusi summasi/oylari, AI tekshiruv limitlari,
/// landing sozlamalari — tarixga UMUMAN tushmasdi).
///
/// <para>O'zgargan MAYDONLAR ro'yxatini EF ChangeTracker'dan o'zi oladi. Shu sababdan
/// <c>SaveChangesAsync</c> dan <b>OLDIN</b> chaqirilishi SHART: saqlangandan keyin "o'zgargan" belgisi
/// tozalanadi va ro'yxat bo'sh chiqadi. Yozuvning o'zi ham o'sha SaveChanges bilan birga saqlanadi.</para>
///
/// <para>Ikki rejim:</para>
/// <list type="bullet">
///   <item><b>faqat NOMLAR</b> (standart) — <c>SettingsController</c> dagi eski xatti-harakat: qiymat
///     yozilmaydi (ehtiyot uchun);</item>
///   <item><b>qiymatlar bilan</b> (<c>withValues: true</c>) — pul/limit sozlamalari uchun: tarixda
///     "Standart summa: 100 000 → 150 000" ko'rinadi. ⚠️ Maxfiy nomli maydonlar
///     (<see cref="SecretScrub.IsSecretName"/>) bu rejimda ham FAQAT nomi bilan yoziladi.</item>
/// </list>
/// </summary>
public static class SettingsAudit
{
    /// <summary>Kuzatilayotgan <typeparamref name="TEntity"/> yozuvlarida o'zgargan (yoki yangi qo'shilgan
    /// yozuvning) maydon nomlari — alifbo tartibida, takrorsiz.</summary>
    public static IReadOnlyList<string> ChangedFields<TEntity>(ChangeTracker tracker) where TEntity : class =>
        tracker.Entries<TEntity>()
            .SelectMany(e => e.Properties.Where(p => e.State == EntityState.Added || p.IsModified))
            .Select(p => p.Metadata.Name)
            .Distinct().Order().ToList();

    /// <summary>
    /// Sozlama o'zgarishini tarixga yozadi (bitta qator). <paramref name="labels"/> — maydon nomi →
    /// o'zbekcha yorliq (berilmasa texnik nom ko'rinadi).
    /// </summary>
    public static void Record<TEntity>(
        AuditService audit, ChangeTracker tracker, string what,
        bool withValues = false, IReadOnlyDictionary<string, string>? labels = null,
        string entityType = "CenterMeta", string entityId = "center", string? studentId = null)
        where TEntity : class
    {
        string Label(string field) => labels is not null && labels.TryGetValue(field, out var l) ? l : field;

        if (!withValues)
        {
            var changed = ChangedFields<TEntity>(tracker);
            var detail = changed.Count > 0 ? $" ({string.Join(", ", changed.Select(Label))})" : "";
            audit.Record(entityType, entityId, "update", $"Sozlama o'zgartirildi — {what}{detail}", studentId: studentId);
            return;
        }

        var before = new Dictionary<string, object?>(StringComparer.Ordinal);
        var after = new Dictionary<string, object?>(StringComparer.Ordinal);
        var parts = new List<string>();
        foreach (var entry in tracker.Entries<TEntity>())
        {
            var added = entry.State == EntityState.Added;
            foreach (var p in entry.Properties.Where(p => added || p.IsModified).OrderBy(p => p.Metadata.Name, StringComparer.Ordinal))
            {
                var name = p.Metadata.Name;
                // YANGI qator (masalan CenterMeta hali yo'q edi) — barcha ustunlar "o'zgargan" bo'ladi;
                // tarixga faqat yorlig'i berilgan (ya'ni shu amal tegadigan) maydonlar yoziladi.
                if (added && (labels is null || !labels.ContainsKey(name))) continue;
                if (name == "UpdatedAt") continue;   // texnik vaqt belgisi — har saqlashda o'zgaradi
                if (SecretScrub.IsSecretName(name))
                {
                    parts.Add(Label(name));
                    continue;
                }
                var oldValue = added ? null : p.OriginalValue;
                if (!added && Equals(oldValue, p.CurrentValue)) continue;
                before[name] = oldValue;
                after[name] = p.CurrentValue;
                parts.Add(added
                    ? $"{Label(name)}: {Format(p.CurrentValue)}"
                    : $"{Label(name)}: {Format(oldValue)} → {Format(p.CurrentValue)}");
            }
        }
        var text = parts.Count > 0 ? $" — {string.Join("; ", parts)}" : " (o'zgarish yo'q)";
        audit.Record(entityType, entityId, "update", $"Sozlama o'zgartirildi — {what}{text}",
            before: before.Count > 0 ? before : null, after: after.Count > 0 ? after : null, studentId: studentId);
    }

    /// <summary>Qiymatni tarix matni uchun: pul — "150 000", bool — "ha/yo'q", bo'sh — "—".</summary>
    public static string Format(object? value) => value switch
    {
        null => "—",
        bool b => b ? "ha" : "yo'q",
        decimal d => AuditService.Money(d),
        double d => d.ToString("0.##", CultureInfo.InvariantCulture),
        string s when string.IsNullOrWhiteSpace(s) => "—",
        string s => s.Length > 120 ? s[..120] + "…" : s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "—",
    };
}
