using WunderkindLC.Application.Services.Kpi;
using WunderkindLC.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// CHEKLIST SEED'i (<see cref="KpiChecklistSeed"/>) va uni bazaga yozadigan
/// <see cref="KpiSeedService"/>.
///
/// <para>Bu testlar Excel'dagi ro'yxatning KODDAGI nusxasini QULFLAYDI: band tushib qolsa,
/// raqami takrorlansa yoki <c>AutoCheckKey</c> da harf xatosi bo'lsa — hech qanday xato
/// chiqmasdi, band shunchaki "hech qachon avtomatik belgilanmaydigan" bo'lib qolardi.</para>
/// </summary>
public class KpiChecklistSeedTests
{
    /// <summary>
    /// Seed'da ishlatilgan, LEKIN hali qo'llanmagan kalitlar — ATAYIN qo'lda belgilanadi
    /// (<see cref="ChecklistAutoCheck"/> sinf izohiga qarang).
    /// </summary>
    /// <remarks>⚠️ Bu ro'yxat "harf xatosi qulfi": seed'dagi kalit yo QO'LLANGAN, yo SHU
    /// yerda atayin sanab o'tilgan bo'lishi kerak. Yangi kalit qo'shsangiz — yoki
    /// <c>ChecklistAutoCheck.Supported</c> ga, yoki shu ro'yxatga qo'shing.</remarks>
    private static readonly HashSet<string> KnownManual = new(StringComparer.Ordinal)
    {
        "auto:kpi.today_numbers",
        "auto:kpi.digest_sent",
        "auto:dm.night_answered",
        "auto:dm.open_dialogs",
        "auto:dm.leads_created",
        "auto:leads.answers_filled",
        "auto:leads.converted",
        "auto:sms.absence_sent",
        "auto:sms.payment_reminder",
        "auto:journal.streak2",
        "auto:journal.streak3_called",
        "auto:contacts.debtor_called",
        "auto:support.sla2",
        "auto:groups.ending_soon",
        "auto:students.left_today",
        "auto:students.reason_filled",
    };

    /* =========================================================================================
     *  SOF SEED
     * ========================================================================================= */

    [Fact]
    public void Bandlar_soni_Excel_bilan_bir_xil_31_va_35()
    {
        Assert.Equal(31, KpiChecklistSeed.Items(KpiConst.Intake).Count);
        Assert.Equal(35, KpiChecklistSeed.Items(KpiConst.Retention).Count);
    }

    /// <remarks>⚠️ Chiquvchida 7 blok — spetsifikatsiya sarlavhasi «8 vaqt bloki» deydi, lekin
    /// ro'yxatning O'ZIDA 7 ta sarlavha bor (13:30–16:50 «UZAYTIRISH» bloki ichida ketish
    /// bandlari 29–32 ham turibdi). Seed ro'yxatga AYNAN amal qiladi.</remarks>
    [Fact]
    public void Vaqt_bloklari_soni_9_va_7()
    {
        Assert.Equal(9, KpiChecklistSeed.Blocks(KpiConst.Intake).Count);
        Assert.Equal(7, KpiChecklistSeed.Blocks(KpiConst.Retention).Count);
    }

    [Fact]
    public void Call_operator_uchun_cheklist_YOQ()
    {
        Assert.Empty(KpiChecklistSeed.Items(KpiConst.Operator));
        Assert.DoesNotContain(KpiConst.Operator, KpiChecklistSeed.Roles);
    }

    [Theory]
    [InlineData(KpiConst.Intake)]
    [InlineData(KpiConst.Retention)]
    public void Band_raqami_rol_ichida_TAKRORLANMAYDI(string role)
    {
        var items = KpiChecklistSeed.Items(role);
        var dupes = items.GroupBy(x => x.No).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(dupes);
        // 1..N ketma-ket — ekranda "12-band yo'q" bo'lib ko'rinmasin.
        Assert.Equal(Enumerable.Range(1, items.Count), items.Select(x => x.No));
    }

    [Theory]
    [InlineData(KpiConst.Intake)]
    [InlineData(KpiConst.Retention)]
    public void Matn_norma_va_vaqt_bloki_BOSH_EMAS(string role)
    {
        foreach (var i in KpiChecklistSeed.Items(role))
        {
            Assert.False(string.IsNullOrWhiteSpace(i.Text), $"{role} #{i.No}: matn bo'sh");
            Assert.False(string.IsNullOrWhiteSpace(i.Norm), $"{role} #{i.No}: norma bo'sh");
            Assert.False(string.IsNullOrWhiteSpace(i.TimeBlock), $"{role} #{i.No}: vaqt bloki bo'sh");
        }
    }

    [Theory]
    [InlineData(KpiConst.Intake)]
    [InlineData(KpiConst.Retention)]
    public void Vaqt_bloklari_UZLUKSIZ_va_tartibda(string role)
    {
        var items = KpiChecklistSeed.Items(role);

        // Order — ro'yxatdagi o'rin (0..N-1).
        Assert.Equal(Enumerable.Range(0, items.Count), items.Select(x => x.Order));

        // Har blok BIR MARTA va yaxlit uchraydi (aralashib ketmagan).
        var seen = new List<string>();
        string? current = null;
        foreach (var i in items)
        {
            if (i.TimeBlock == current) continue;
            Assert.DoesNotContain(i.TimeBlock, seen);
            seen.Add(i.TimeBlock);
            current = i.TimeBlock;
        }
        Assert.Equal(KpiChecklistSeed.Blocks(role).Select(b => b.TimeBlock), seen);
    }

    [Theory]
    [InlineData(KpiConst.Intake)]
    [InlineData(KpiConst.Retention)]
    public void Mezon_raqami_1_dan_13_gacha(string role)
    {
        foreach (var i in KpiChecklistSeed.Items(role).Where(x => x.CriterionNo is not null))
            Assert.InRange(i.CriterionNo!.Value, 1, 13);
    }

    [Fact]
    public void KPI_tegi_faqat_CHIQUVCHIDA_va_faqat_U_K_Q_T()
    {
        Assert.All(KpiChecklistSeed.Items(KpiConst.Intake), i => Assert.Null(i.KpiTag));

        var allowed = new[] { "U", "K", "Q", "T" };
        foreach (var i in KpiChecklistSeed.Items(KpiConst.Retention).Where(x => x.KpiTag is not null))
            Assert.Contains(i.KpiTag, allowed);
    }

    [Fact]
    public void Har_AutoCheckKey_yo_QOLLANGAN_yo_atayin_QOLDA()
    {
        foreach (var role in KpiChecklistSeed.Roles)
            foreach (var i in KpiChecklistSeed.Items(role).Where(x => x.AutoCheckKey is not null))
            {
                var key = i.AutoCheckKey!;
                Assert.True(
                    ChecklistAutoCheck.Supported.Contains(key) || KnownManual.Contains(key),
                    $"{role} #{i.No}: noma'lum AutoCheckKey '{key}' — harf xatosimi yoki ro'yxatga qo'shilmaganmi?");
            }
    }

    [Fact]
    public void Qollangan_kalitlarning_HAMMASI_seedda_ishlatilgan()
    {
        var used = KpiChecklistSeed.Roles
            .SelectMany(KpiChecklistSeed.Items)
            .Where(x => x.AutoCheckKey is not null)
            .Select(x => x.AutoCheckKey!)
            .ToHashSet(StringComparer.Ordinal);

        // Qo'llangan, lekin hech qaysi bandda ishlatilmagan kalit — o'lik kod.
        foreach (var key in ChecklistAutoCheck.Supported)
            Assert.Contains(key, used);
    }

    /* =========================================================================================
     *  BAZAGA SEED (idempotentlik)
     * ========================================================================================= */

    [Fact]
    public async Task EnsureAsync_bosh_bazada_qoidalar_va_ikkala_shablonni_yaratadi()
    {
        using var db = TestDb.Sqlite();

        var (rules, templates, items) = await KpiSeedService.EnsureAsync(db.Context, "Tizim");

        Assert.Equal(3, rules);            // intake + retention + operator
        Assert.Equal(2, templates);        // cheklisti bor ikki rol
        Assert.Equal(31 + 35, items);

        Assert.Equal(66, await db.Context.ChecklistTemplateItems.CountAsync());
        Assert.All(await db.Context.KpiRuleSets.ToListAsync(),
            r => Assert.False(string.IsNullOrWhiteSpace(r.Json)));
    }

    [Fact]
    public async Task EnsureAsync_IKKINCHI_marta_hech_narsa_qoshmaydi()
    {
        using var db = TestDb.Sqlite();
        await KpiSeedService.EnsureAsync(db.Context, "Tizim");

        var (rules, templates, items) = await KpiSeedService.EnsureAsync(db.Context, "Tizim");

        Assert.Equal((0, 0, 0), (rules, templates, items));
        Assert.Equal(3, await db.Context.KpiRuleSets.CountAsync());
        Assert.Equal(66, await db.Context.ChecklistTemplateItems.CountAsync());
    }

    [Fact]
    public async Task EnsureAsync_rahbar_TAHRIRINI_ustidan_YOZMAYDI()
    {
        using var db = TestDb.Sqlite();
        await KpiSeedService.EnsureAsync(db.Context, "Tizim");

        var rule = await db.Context.KpiRuleSets.FirstAsync(r => r.RoleCode == KpiConst.Intake);
        rule.Json = "{\"ContractBase\":99000}";
        var item = await db.Context.ChecklistTemplateItems.FirstAsync(i => i.No == 1);
        item.Text = "Rahbar tahriri";
        await db.Context.SaveChangesAsync();

        await KpiSeedService.EnsureAsync(db.Context, "Tizim");

        Assert.Equal("{\"ContractBase\":99000}",
            (await db.Context.KpiRuleSets.FirstAsync(r => r.RoleCode == KpiConst.Intake)).Json);
        Assert.Equal("Rahbar tahriri",
            (await db.Context.ChecklistTemplateItems.FirstAsync(i => i.Id == item.Id)).Text);
    }

    [Fact]
    public async Task EnsureAsync_ochirilgan_bandni_QAYTA_qoshadi_qolganiga_tegmaydi()
    {
        using var db = TestDb.Sqlite();
        await KpiSeedService.EnsureAsync(db.Context, "Tizim");

        var tpl = await db.Context.ChecklistTemplates.FirstAsync(t => t.RoleCode == KpiConst.Intake);
        var dropped = await db.Context.ChecklistTemplateItems
            .FirstAsync(i => i.TemplateId == tpl.Id && i.No == 7);
        db.Context.ChecklistTemplateItems.Remove(dropped);
        await db.Context.SaveChangesAsync();

        var (_, templates, items) = await KpiSeedService.EnsureAsync(db.Context, "Tizim");

        Assert.Equal(0, templates);
        Assert.Equal(1, items);
        var back = await db.Context.ChecklistTemplateItems
            .FirstAsync(i => i.TemplateId == tpl.Id && i.No == 7);
        // Yangi band OXIRIGA qo'shiladi — qo'lda o'zgartirilgan tartib buzilmasin.
        Assert.Equal(31, back.Order);
        Assert.Equal(66, await db.Context.ChecklistTemplateItems.CountAsync());
    }

    [Fact]
    public async Task EnsureAsync_mavjud_QOIDA_versiyasi_borida_yangisini_YARATMAYDI()
    {
        using var db = TestDb.Sqlite();
        db.Context.KpiRuleSets.Add(new KpiRuleSet
        {
            RoleCode = KpiConst.Retention,
            EffectiveFrom = "2020-01",
            Json = "{}",
        });
        await db.Context.SaveChangesAsync();

        var (rules, _, _) = await KpiSeedService.EnsureAsync(db.Context, "Tizim");

        Assert.Equal(2, rules);   // faqat intake + operator
        Assert.Single(await db.Context.KpiRuleSets.Where(r => r.RoleCode == KpiConst.Retention).ToListAsync());
    }
}
