using System.Text.Json;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;
using Microsoft.EntityFrameworkCore;

namespace WunderkindLC.Application.Services.Kpi;

/// <summary>
/// KPI modulining BOSHLANG'ICH ma'lumoti: rol QOIDALARI (<see cref="KpiRuleSet"/>) va kunlik
/// CHEKLIST shablonlari (<see cref="ChecklistTemplate"/>).
///
/// <para>IDEMPOTENT — loyihadagi mavjud seed uslubi bilan bir xil (<c>ContactService.SystemStages</c>
/// va topshiriq doskasi <c>EnsureSeedAsync</c>): har startupda arzon ishlaydi va faqat YO'Q
/// bo'lgan narsani qo'shadi. Shuning uchun uni startupdan ham, admin endpointidan ham
/// (<c>POST rules/seed</c>, <c>POST checklist/templates/seed</c>) bemalol chaqirish mumkin.</para>
///
/// <para>⚠️ <b>MAVJUD NARSA HECH QACHON QAYTA YOZILMAYDI.</b> Qoidalar ham, cheklist bandlari
/// ham «Qoidalar» sahifasidan TAHRIRLANADI: seed ustidan yozsa, rahbarning har o'zgarishi
/// keyingi restartda jimgina Excel qiymatiga qaytib qolardi — va buni hech kim sezmasdi
/// (raqamlar "o'zi o'zgargan" bo'lib ko'rinardi).</para>
///
/// <para>⚠️ Bu servis <c>SaveChangesAsync</c> ni O'ZI chaqiradi (loyihadagi odatdagi
/// "servis saqlamaydi" qoidasidan ISTISNO): u startupdan — hech qanday controller
/// tranzaksiyasi bo'lmagan joydan — chaqiriladi.</para>
/// </summary>
public static class KpiSeedService
{
    /// <summary>
    /// <see cref="KpiRuleSet.Json"/> uchun JSON sozlamalari. Yozishda PascalCase (record
    /// xossalari bilan bir xil), o'qishda registrga BEFARQ — ya'ni qoidalar sahifasidan
    /// camelCase bilan kelgan JSON ham o'qiladi.
    /// </summary>
    public static readonly JsonSerializerOptions RuleJson = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Yo'q bo'lgan qoidalar to'plami va cheklist shablonlarini qo'shadi.
    /// </summary>
    /// <param name="actor">Kim seed qildi (foydalanuvchi ismi yoki "Tizim") — versiya izi uchun.</param>
    /// <returns>Nechta qoida to'plami, shablon va cheklist bandi QO'SHILDI.</returns>
    public static async Task<(int Rules, int Templates, int Items)> EnsureAsync(
        IAppDbContext db, string actor, CancellationToken ct = default)
    {
        var month = AppClock.Today.ToString("yyyy-MM");
        var rules = await EnsureRulesAsync(db, actor, month, ct);
        var (templates, items) = await EnsureChecklistsAsync(db, ct);

        if (rules > 0 || templates > 0 || items > 0)
            await db.SaveChangesAsync(ct);

        return (rules, templates, items);
    }

    /* =========================================================================================
     *  QOIDALAR
     * ========================================================================================= */

    /// <summary>
    /// Har rol uchun BIRINCHI qoidalar versiyasi (Excel konstantalari).
    /// </summary>
    /// <remarks>
    /// ⚠️ Shart — "shu rolda VERSIYA UMUMAN yo'qmi", "shu oyda versiya yo'qmi" EMAS. Rahbar
    /// koeffitsientni tahrirlab yangi versiya qo'ygan bo'lsa, seed keyingi oyda yana Excel
    /// qiymatini yozib qo'yardi va u ENG SO'NGGI versiya bo'lib, tahrirni bekor qilardi.
    ///
    /// ⚠️ <c>EffectiveFrom</c> — JORIY oy: seed birinchi marta qo'yilayotganida shu oyning
    /// hisobi ham qoidasiz qolmasin. Keyingi versiyalar «Qoidalar» sahifasidan KEYINGI oyga
    /// qo'yiladi (<see cref="KpiVersioning"/>).
    /// </remarks>
    private static async Task<int> EnsureRulesAsync(
        IAppDbContext db, string actor, string month, CancellationToken ct)
    {
        var existing = await db.KpiRuleSets.Select(x => x.RoleCode).Distinct().ToListAsync(ct);
        var have = existing.ToHashSet(StringComparer.Ordinal);

        var added = 0;
        foreach (var role in KpiRuleSeed.Roles)
        {
            if (have.Contains(role)) continue;
            db.KpiRuleSets.Add(new KpiRuleSet
            {
                RoleCode = role,
                EffectiveFrom = month,
                Json = JsonSerializer.Serialize(KpiRuleSeed.For(role), RuleJson),
                Note = "Excel konstantalari (boshlang'ich versiya)",
                CreatedBy = actor,
            });
            added++;
        }
        return added;
    }

    /* =========================================================================================
     *  CHEKLIST SHABLONLARI
     * ========================================================================================= */

    /// <summary>
    /// Har cheklist roli uchun faol shablon va uning bandlari.
    /// </summary>
    /// <remarks>
    /// ⚠️ Shablon BOR bo'lsa — faqat <c>No</c> si YETISHMAYDIGAN bandlar qo'shiladi. Sabab:
    /// spetsifikatsiyaga keyinchalik yangi band qo'shilsa (masalan 32-band), u mavjud bazada
    /// ham o'z-o'zidan paydo bo'lishi kerak — LEKIN rahbar tahrirlagan matn, norma va
    /// <c>AutoCheckKey</c> qiymatlari TEGILMAYDI. Ya'ni seed "to'ldiradi", "sinxronlamaydi".
    ///
    /// ⚠️ Qo'shilgan bandning <c>Order</c> i mavjud eng katta tartibdan KEYIN qo'yiladi —
    /// aks holda yangi band ro'yxat o'rtasiga tushib, rahbar qo'lda o'zgartirgan tartibni
    /// buzardi.
    ///
    /// ⚠️ Faol shablon YO'Q, lekin ARXIV shablon bor bo'lishi mumkin (rol shabloni
    /// almashtirilgan): u holda YANGI faol shablon yaratiladi va eski o'z bandlari bilan
    /// tegilmay qoladi — o'tgan kunlarning belgilari (<c>ChecklistEntry</c>) o'sha bandlarga
    /// bog'langan.
    /// </remarks>
    private static async Task<(int Templates, int Items)> EnsureChecklistsAsync(
        IAppDbContext db, CancellationToken ct)
    {
        var templatesAdded = 0;
        var itemsAdded = 0;

        foreach (var role in KpiChecklistSeed.Roles)
        {
            var seed = KpiChecklistSeed.Items(role);
            if (seed.Count == 0) continue;

            var template = await db.ChecklistTemplates
                .Where(t => t.RoleCode == role && t.IsActive)
                .OrderBy(t => t.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (template is null)
            {
                template = new ChecklistTemplate
                {
                    RoleCode = role,
                    Name = KpiChecklistSeed.TemplateName(role),
                    IsActive = true,
                };
                db.ChecklistTemplates.Add(template);
                templatesAdded++;

                foreach (var row in seed)
                {
                    db.ChecklistTemplateItems.Add(Build(template.Id, row, row.Order));
                    itemsAdded++;
                }
                continue;
            }

            // Shablon BOR — faqat yetishmaydigan bandlar (No bo'yicha).
            var current = await db.ChecklistTemplateItems
                .Where(i => i.TemplateId == template.Id)
                .Select(i => new { i.No, i.Order })
                .ToListAsync(ct);

            var haveNo = current.Select(x => x.No).ToHashSet();
            var nextOrder = current.Count == 0 ? 0 : current.Max(x => x.Order) + 1;

            foreach (var row in seed)
            {
                if (haveNo.Contains(row.No)) continue;
                db.ChecklistTemplateItems.Add(Build(template.Id, row, nextOrder++));
                itemsAdded++;
            }
        }

        return (templatesAdded, itemsAdded);
    }

    private static ChecklistTemplateItem Build(string templateId, KpiChecklistSeed.Row row, int order) => new()
    {
        TemplateId = templateId,
        No = row.No,
        TimeBlock = row.TimeBlock,
        Text = row.Text,
        Norm = row.Norm,
        KpiTag = row.KpiTag,
        CriterionNo = row.CriterionNo,
        AutoCheckKey = row.AutoCheckKey,
        Order = order,
    };
}
