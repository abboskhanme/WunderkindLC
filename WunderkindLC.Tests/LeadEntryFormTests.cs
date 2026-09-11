using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// «LID KIRITISH FORMASI» testlari (<see cref="LeadEntryRules"/> + <see cref="LeadEntryFormService"/>).
/// Rasmiy manba: <c>.claude/rules/lead-entry-form.md</c>.
///
/// <para>Modulning mohiyati: markaz xodimi <c>/admin/leads</c> da qo'lda lid kiritganda QAYSI
/// maydon so'ralishi va qaysisi MAJBURIY ekanini markazning o'zi belgilaydi. Shu sababdan
/// testlarning asosiy og'irligi ikki joyda: (1) sozlama normallashtirilishi (foydalanuvchi
/// to'ldira olmaydigan majburiy maydon YARATILMASIN), (2) tekshiruvning tashqi kanallarga
/// TEGMASLIGI.</para>
/// </summary>
public class LeadEntryFormTests
{
    // ===================== Yordamchilar =====================

    private static Lead NewLead(string name = "Aliyev Ali") => new()
    {
        FullName = name,
        Gender = "male",
        CreatedAt = "2026-09-02T10:00:00",
    };

    private static LeadEntryStandardInput St(string key, string state) => new(key, state);

    private static LeadEntryFieldInput Q(
        string label, string kind = "text", List<string>? options = null, bool required = false) =>
        new(null, label, kind, options, null, required);

    // ===================== 1) Bo'sh baza = STANDART forma =====================

    [Fact]
    public async Task Sozlama_yoq_bolsa_hamma_maydon_korinadi_va_faqat_FISH_majburiy()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;

        var dto = LeadEntryFormService.BuildDto(await LeadEntryFormService.LoadAsync(db));

        Assert.Equal(LeadEntryRules.Standard.Count, dto.Standard.Count);
        Assert.Empty(dto.Fields);
        var required = dto.Standard.Where(s => s.State == LeadEntryRules.StateRequired).ToList();
        Assert.Single(required);
        Assert.Equal(LeadEntryRules.KeyFullName, required[0].Key);
        // Qolganlari SO'RALADI, lekin majburiy emas (eski xatti-harakat AYNAN saqlanadi).
        Assert.All(dto.Standard.Where(s => s.Key != LeadEntryRules.KeyFullName),
            s => Assert.Equal(LeadEntryRules.StateOptional, s.State));
    }

    [Fact]
    public async Task Sozlama_yoq_bolsa_oddiy_lid_tekshiruvdan_otadi()
    {
        using var t = TestDb.Sqlite();
        var (error, json) = await LeadEntryFormService.ValidateAsync(
            t.Context, NewLead(), new Dictionary<string, List<string>>());

        Assert.Null(error);
        Assert.Equal("", json);
    }

    // ===================== 2) Majburiy standart maydon =====================

    [Fact]
    public async Task Majburiy_standart_maydon_bosh_bolsa_lid_rad_etiladi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        await LeadEntryFormService.SaveAsync(db, new LeadEntryFormPayload(
            new() { St(LeadEntryRules.KeyPhone, LeadEntryRules.StateRequired) }, null));

        var (error, _) = await LeadEntryFormService.ValidateAsync(
            db, NewLead(), new Dictionary<string, List<string>>());

        Assert.NotNull(error);
        Assert.Contains("O'z telefon raqami", error);
    }

    [Fact]
    public async Task Majburiy_maydon_toldirilsa_lid_otadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        await LeadEntryFormService.SaveAsync(db, new LeadEntryFormPayload(
            new() { St(LeadEntryRules.KeyPhone, LeadEntryRules.StateRequired) }, null));

        var lead = NewLead();
        lead.Phone = "998901234567";
        var (error, _) = await LeadEntryFormService.ValidateAsync(
            db, lead, new Dictionary<string, List<string>>());

        Assert.Null(error);
    }

    [Fact]
    public async Task Yashirilgan_maydon_majburiy_bola_olmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        // Foydalanuvchi ikkisini birga yubordi — formada ko'rinmaydigan maydon MAJBURIY bo'lsa
        // lid umuman saqlanmasdi (chiqib bo'lmaydigan tuzoq).
        await LeadEntryFormService.SaveAsync(db, new LeadEntryFormPayload(
            new() { St(LeadEntryRules.KeyBirthDate, LeadEntryRules.StateHidden) }, null));

        var row = await db.LeadEntryFields.SingleAsync(x => x.Key == LeadEntryRules.KeyBirthDate);
        Assert.False(row.Visible);
        Assert.False(row.Required);

        var (error, _) = await LeadEntryFormService.ValidateAsync(
            db, NewLead(), new Dictionary<string, List<string>>());
        Assert.Null(error);
    }

    [Fact]
    public async Task FISH_qulflangan_yashirishga_urinish_ham_ish_bermaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        await LeadEntryFormService.SaveAsync(db, new LeadEntryFormPayload(
            new() { St(LeadEntryRules.KeyFullName, LeadEntryRules.StateHidden) }, null));

        var row = await db.LeadEntryFields.SingleAsync(x => x.Key == LeadEntryRules.KeyFullName);
        Assert.True(row.Visible);
        Assert.True(row.Required);

        var lead = NewLead(name: "");
        var (error, _) = await LeadEntryFormService.ValidateAsync(
            db, lead, new Dictionary<string, List<string>>());
        Assert.NotNull(error);
        Assert.Contains("F.I.SH", error);
    }

    [Fact]
    public async Task Tuman_yashirilsa_maktab_ham_yashiriladi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        // Maktab ro'yxati TUMANDAN quriladi (kaskad) — tumansiz select doim bo'sh turardi.
        await LeadEntryFormService.SaveAsync(db, new LeadEntryFormPayload(
            new()
            {
                St(LeadEntryRules.KeyDistrict, LeadEntryRules.StateHidden),
                St(LeadEntryRules.KeySchool, LeadEntryRules.StateRequired),
            }, null));

        var school = await db.LeadEntryFields.SingleAsync(x => x.Key == LeadEntryRules.KeySchool);
        Assert.False(school.Visible);
        Assert.False(school.Required);
    }

    // ===================== 3) Qo'shimcha savollar =====================

    [Fact]
    public async Task Majburiy_qoshimcha_savol_javobsiz_qolsa_lid_rad_etiladi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        await LeadEntryFormService.SaveAsync(db, new LeadEntryFormPayload(
            null, new() { Q("Qaysi vaqtda o'qimoqchi?", required: true) }));

        var (error, _) = await LeadEntryFormService.ValidateAsync(
            db, NewLead(), new Dictionary<string, List<string>>());

        Assert.NotNull(error);
        Assert.Contains("Qaysi vaqtda o'qimoqchi?", error);
    }

    [Fact]
    public async Task Javob_savol_MATNI_bilan_saqlanadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        await LeadEntryFormService.SaveAsync(db, new LeadEntryFormPayload(
            null, new() { Q("Qayerdan eshitdingiz?") }));
        var field = await db.LeadEntryFields.SingleAsync(x => x.Key == "");

        var (error, json) = await LeadEntryFormService.ValidateAsync(
            db, NewLead(), new Dictionary<string, List<string>> { [field.Id] = new() { "Tanishlardan" } });

        Assert.Null(error);
        // Sozlama keyin tahrirlansa yoki savol o'chirilsa ham lid javobi tushunarli qolsin.
        var parsed = LeadFormService.ParseAnswers(json);
        Assert.Single(parsed);
        Assert.Equal("Qayerdan eshitdingiz?", parsed[0].Question);
        Assert.Equal(new[] { "Tanishlardan" }, parsed[0].Answers);
    }

    [Fact]
    public async Task Variantli_savolda_begona_javob_qabul_qilinmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        await LeadEntryFormService.SaveAsync(db, new LeadEntryFormPayload(
            null, new() { Q("Smena", "select", new() { "Ertalab", "Kechqurun" }, required: true) }));
        var field = await db.LeadEntryFields.SingleAsync(x => x.Key == "");

        var (error, _) = await LeadEntryFormService.ValidateAsync(
            db, NewLead(), new Dictionary<string, List<string>> { [field.Id] = new() { "Tunda" } });

        // Begona qiymat tashlanadi → majburiy savol javobsiz qoladi.
        Assert.NotNull(error);
        Assert.Contains("Smena", error);
    }

    [Fact]
    public async Task Bitta_tanlovli_savolga_bir_nechta_javob_kelsa_birinchisi_olinadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        await LeadEntryFormService.SaveAsync(db, new LeadEntryFormPayload(
            null, new() { Q("Smena", "radio", new() { "Ertalab", "Kechqurun" }) }));
        var field = await db.LeadEntryFields.SingleAsync(x => x.Key == "");

        var (_, json) = await LeadEntryFormService.ValidateAsync(
            db, NewLead(),
            new Dictionary<string, List<string>> { [field.Id] = new() { "Ertalab", "Kechqurun" } });

        var parsed = LeadFormService.ParseAnswers(json);
        Assert.Equal(new[] { "Ertalab" }, parsed[0].Answers);
    }

    [Fact]
    public async Task Checkbox_savolda_bir_nechta_javob_saqlanadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        await LeadEntryFormService.SaveAsync(db, new LeadEntryFormPayload(
            null, new() { Q("Qiziqishlari", "checkbox", new() { "IELTS", "Matematika", "IT" }) }));
        var field = await db.LeadEntryFields.SingleAsync(x => x.Key == "");

        var (_, json) = await LeadEntryFormService.ValidateAsync(
            db, NewLead(),
            new Dictionary<string, List<string>> { [field.Id] = new() { "IELTS", "IT" } });

        var parsed = LeadFormService.ParseAnswers(json);
        Assert.Equal(new[] { "IELTS", "IT" }, parsed[0].Answers);
    }

    [Fact]
    public async Task Variantsiz_qolgan_select_oddiy_matnga_tushadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        // Aks holda menejer hech narsa tanlay olmaydigan bo'sh select ko'rardi.
        await LeadEntryFormService.SaveAsync(db, new LeadEntryFormPayload(
            null, new() { Q("Izoh", "select", new()) }));

        var field = await db.LeadEntryFields.SingleAsync(x => x.Key == "");
        Assert.Equal(LeadFormService.KindText, field.Kind);
        Assert.Empty(field.Options);
    }

    // ===================== 4) Saqlash qoidalari =====================

    [Fact]
    public async Task Yorligi_bosh_savol_saqlanmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        await LeadEntryFormService.SaveAsync(db, new LeadEntryFormPayload(
            null, new() { Q("   "), Q("Haqiqiy savol") }));

        var custom = await db.LeadEntryFields.Where(x => x.Key == "").ToListAsync();
        Assert.Single(custom);
        Assert.Equal("Haqiqiy savol", custom[0].Label);
    }

    [Fact]
    public async Task Saqlash_bandlarni_TOLIQ_almashtiradi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        await LeadEntryFormService.SaveAsync(db, new LeadEntryFormPayload(
            null, new() { Q("Birinchi"), Q("Ikkinchi") }));
        await LeadEntryFormService.SaveAsync(db, new LeadEntryFormPayload(
            null, new() { Q("Yagona") }));

        var custom = await db.LeadEntryFields.Where(x => x.Key == "").ToListAsync();
        Assert.Single(custom);
        Assert.Equal("Yagona", custom[0].Label);
        // Standart maydonlar har saqlashda TO'LIQ qayta yoziladi — takror qator qolmasin.
        Assert.Equal(LeadEntryRules.Standard.Count,
            await db.LeadEntryFields.CountAsync(x => x.Key != ""));
    }

    [Fact]
    public async Task Savollar_soni_chegaralanadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var many = Enumerable.Range(1, LeadEntryRules.MaxFields + 10)
            .Select(i => Q($"Savol {i}")).ToList();
        await LeadEntryFormService.SaveAsync(db, new LeadEntryFormPayload(null, many));

        Assert.Equal(LeadEntryRules.MaxFields, await db.LeadEntryFields.CountAsync(x => x.Key == ""));
    }

    [Fact]
    public async Task Savollar_tartibi_saqlanadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        await LeadEntryFormService.SaveAsync(db, new LeadEntryFormPayload(
            null, new() { Q("Birinchi"), Q("Ikkinchi"), Q("Uchinchi") }));

        var dto = LeadEntryFormService.BuildDto(await LeadEntryFormService.LoadAsync(db));
        Assert.Equal(new[] { "Birinchi", "Ikkinchi", "Uchinchi" },
            dto.Fields.Select(f => f.Label).ToArray());
    }

    // ===================== 5) Tahrirlash: javoblar TEGILMAYDI =====================

    [Fact]
    public async Task Javoblar_uzatilmasa_ular_TEGILMAYDI()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        await LeadEntryFormService.SaveAsync(db, new LeadEntryFormPayload(
            null, new() { Q("Majburiy savol", required: true) }));

        // `answers = null` — chaqiruvchi qo'shimcha savollarni bilmaydi (eski klient yoki
        // boshqa oqim). Bunday holatda lidning eski javoblari O'CHIRILMASIN, majburiylik esa
        // tahrirni bloklab qo'ymasin.
        var (error, json) = await LeadEntryFormService.ValidateAsync(db, NewLead(), null);

        Assert.Null(error);
        Assert.Null(json);
    }

    // ===================== 6) Sof qoidalar =====================

    [Fact]
    public void Nomalum_holat_ixtiyoriyga_tushadi()
    {
        Assert.Equal(LeadEntryRules.StateOptional, LeadEntryRules.NormalizeState("allaqanday"));
        Assert.Equal(LeadEntryRules.StateOptional, LeadEntryRules.NormalizeState(null));
        Assert.Equal(LeadEntryRules.StateRequired, LeadEntryRules.NormalizeState("required"));
    }

    [Fact]
    public void Standart_katalogda_takror_kalit_yoq()
    {
        var keys = LeadEntryRules.Standard.Select(x => x.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        // Qulflangan maydon AYNAN bitta bo'lishi kerak (F.I.SH) — aks holda sozlamaning ma'nosi
        // yo'qolib borardi.
        Assert.Single(LeadEntryRules.Standard, x => x.Locked);
    }

    [Fact]
    public void Har_standart_kalit_uchun_qiymat_oquvchisi_bor()
    {
        // `ValueOf` da unutilgan kalit "hech qachon to'ldirilmagan" bo'lib qolardi, ya'ni uni
        // majburiy qilish lidni butunlay saqlab bo'lmaydigan holga keltirardi.
        var lead = new Lead
        {
            FullName = "a", Gender = "male", BirthDate = "2000-01-01", Phone = "998901234567",
            FatherFullName = "b", FatherPhone = "998901234568",
            MotherFullName = "c", MotherPhone = "998901234569",
            Source = "Instagram", InterestSubject = "IELTS",
            DistrictId = "d1", SchoolId = "s1", Note = "izoh",
        };
        foreach (var def in LeadEntryRules.Standard)
            Assert.False(string.IsNullOrWhiteSpace(LeadEntryRules.ValueOf(def.Key, lead)),
                $"«{def.Key}» uchun ValueOf bo'sh qaytdi");
    }
}
