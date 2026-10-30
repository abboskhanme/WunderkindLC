using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;
using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// BILIMLAR BAZASI (wiki) — `WikiRules` (faqat matn, xavfsiz havola, daraxt, qulf, kim kimning eslatmasini
/// ko'radi) va `WikiPageOps` (saqlash, ko'chirish). Qoidalar: `.claude/rules/wiki.md`.
/// </summary>
public class WikiRulesTests
{
    /* ---------------------------------------------------------------- matn */

    [Fact]
    public void ParseContent_bosh_sahifa_xato_emas()
    {
        Assert.Null(WikiRules.ParseContent("", out var text));
        Assert.Equal("", text);
        Assert.Null(WikiRules.ParseContent("[]", out _));
    }

    [Fact]
    public void ParseContent_matnni_qidiruv_uchun_chiqaradi_royxat_ichma_ich_va_havola_bilan()
    {
        const string json = """
            [
              {"id":"1","type":"heading","props":{"level":1},"content":[{"type":"text","text":"Qabul","styles":{}}],"children":[]},
              {"id":"2","type":"bulletListItem","props":{},"content":[{"type":"text","text":"Sinov darsi","styles":{"bold":true}}],
               "children":[{"id":"3","type":"checkListItem","props":{"checked":true},"content":[
                 {"type":"link","href":"https://t.me/markaz","content":[{"type":"text","text":"Telegram","styles":{}}]}],"children":[]}]},
              {"id":"4","type":"divider","props":{},"children":[]}
            ]
            """;
        Assert.Null(WikiRules.ParseContent(json, out var text));
        Assert.Equal("Qabul Sinov darsi Telegram", text);
    }

    [Fact]
    public void ParseContent_jadval_kataklari_ham_qidiruvga_tushadi()
    {
        const string json = """
            [{"id":"t","type":"table","props":{},"content":{"type":"tableContent","columnWidths":[null,null],"rows":[
              {"cells":[{"type":"tableCell","props":{},"content":[{"type":"text","text":"Kun","styles":{}}]},
                        [{"type":"text","text":"Mas'ul","styles":{}}]]}]},"children":[]}]
            """;
        Assert.Null(WikiRules.ParseContent(json, out var text));
        Assert.Equal("Kun Mas'ul", text);
    }

    [Theory]
    [InlineData("image")]
    [InlineData("video")]
    [InlineData("audio")]
    [InlineData("file")]
    [InlineData("")]
    public void ParseContent_media_bloklari_RAD_etiladi_faqat_matn(string type)
    {
        var json = $$"""[{"id":"x","type":"{{type}}","props":{"url":"https://evil.example/p.png"},"children":[]}]""";
        Assert.NotNull(WikiRules.ParseContent(json, out _));
    }

    [Fact]
    public void ParseContent_ichma_ich_yashirilgan_rasm_ham_RAD_etiladi()
    {
        const string json = """
            [{"id":"a","type":"paragraph","content":[],"children":[{"id":"b","type":"image","props":{"url":"https://x/y.png"},"children":[]}]}]
            """;
        Assert.NotNull(WikiRules.ParseContent(json, out _));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData(" JavaScript:alert(1)")]
    [InlineData("data:text/html,<script>")]
    [InlineData("/admin/settings")]
    [InlineData("")]
    public void ParseContent_xavfli_havola_RAD_etiladi(string href)
    {
        var json = $$"""[{"type":"paragraph","content":[{"type":"link","href":"{{href}}","content":[{"type":"text","text":"x"}]}]}]""";
        Assert.NotNull(WikiRules.ParseContent(json, out _));
    }

    [Theory]
    [InlineData("https://t.me/x")]
    [InlineData("http://example.uz")]
    [InlineData("mailto:info@markaz.uz")]
    [InlineData("tel:+998901234567")]
    public void IsSafeHref_ruxsat_etilganlar(string href) => Assert.True(WikiRules.IsSafeHref(href));

    [Theory]
    [InlineData("{}")]
    [InlineData("salom")]
    [InlineData("[1,2]")]
    [InlineData("[{\"type\":\"paragraph\",\"content\":[{\"type\":\"mention\"}]}]")]
    public void ParseContent_buzuq_yoki_notanish_tuzilma_RAD_etiladi(string json) =>
        Assert.NotNull(WikiRules.ParseContent(json, out _));

    [Fact]
    public void ParseContent_juda_katta_sahifa_RAD_etiladi() =>
        Assert.NotNull(WikiRules.ParseContent("[" + new string(' ', WikiRules.MaxContentChars) + "]", out _));

    [Fact]
    public void ParseContent_juda_chuqur_ichma_ichlik_RAD_etiladi_stek_toshmaydi()
    {
        var inner = """{"type":"paragraph","children":[]}""";
        for (var i = 0; i < WikiRules.MaxDepth + 5; i++)
            inner = $$"""{"type":"bulletListItem","content":[],"children":[{{inner}}]}""";
        Assert.NotNull(WikiRules.ParseContent("[" + inner + "]", out _));
    }

    /* ---------------------------------------------------------------- sarlavha va nom */

    [Fact]
    public void ValidateTitle_bosh_sarlavha_Nomsiz_sahifa_uzuni_xato()
    {
        Assert.Null(WikiRules.ValidateTitle("  ", out var clean));
        Assert.Equal("Nomsiz sahifa", clean);
        Assert.NotNull(WikiRules.ValidateTitle(new string('a', WikiRules.MaxTitleLength + 1), out _));
    }


    /* ---------------------------------------------------------------- daraxt */

    private static readonly (string Id, string ParentId)[] Tree =
    [
        ("a", ""), ("b", "a"), ("c", "b"), ("d", ""), ("e", "d"),
    ];

    [Fact]
    public void SubtreeIds_ichki_sahifalar_bilan()
    {
        Assert.Equal(new HashSet<string> { "a", "b", "c" }, WikiRules.SubtreeIds(Tree, "a"));
        Assert.Equal(new HashSet<string> { "e" }, WikiRules.SubtreeIds(Tree, "e"));
    }

    [Fact]
    public void SubtreeIds_halqaga_chidamli()
    {
        (string, string)[] loop = [("x", "y"), ("y", "x")];
        Assert.Equal(new HashSet<string> { "x", "y" }, WikiRules.SubtreeIds(loop, "x"));
    }

    [Theory]
    [InlineData("a", "", true)]
    [InlineData("a", "d", true)]
    [InlineData("a", "a", false)]   // o'zining ichiga
    [InlineData("a", "c", false)]   // o'z nevarasining ichiga — halqa
    [InlineData("c", "a", true)]
    public void CanMoveUnder_halqa_yasalmaydi(string page, string newParent, bool expected) =>
        Assert.Equal(expected, WikiRules.CanMoveUnder(Tree, page, newParent));

    /* ---------------------------------------------------------------- qidiruv */

    [Fact]
    public void Snippet_topilgan_soz_atrofida()
    {
        var text = new string('x', 100) + " Sinov darsi " + new string('y', 100);
        var s = WikiRules.Snippet(text, "sinov", radius: 10);
        Assert.Contains("Sinov darsi", s);
        Assert.StartsWith("…", s);
        Assert.EndsWith("…", s);
    }

    /* ---------------------------------------------------------------- qulf */

    private static readonly (string Id, string ParentId, bool Locked)[] LockTree =
    [
        ("sop", "", true), ("sop-ichi", "sop", false), ("sop-nevara", "sop-ichi", false),
        ("ochiq", "", false), ("ochiq-ichi", "ochiq", false),
    ];

    [Theory]
    [InlineData("sop", true)]
    [InlineData("sop-ichi", true)]     // qulf pastga meros
    [InlineData("sop-nevara", true)]
    [InlineData("ochiq-ichi", false)]
    [InlineData("", false)]            // yuqori daraja (yangi eslatma)
    public void IsLocked_ozi_yoki_otasi(string pageId, bool expected) =>
        Assert.Equal(expected, WikiRules.IsLocked(LockTree, pageId));

    [Fact]
    public void IsLocked_halqaga_chidamli()
    {
        (string, string, bool)[] loop = [("x", "y", false), ("y", "x", false)];
        Assert.False(WikiRules.IsLocked(loop, "x"));
    }

    /* ---------------------------------------------------------------- kim kimning eslatmasini ko'radi */

    // direktor ─ menejer ─┬─ admin1 (Administrator)
    //                     ├─ ustoz1 (O'qituvchi) ─ ustoz2 (O'qituvchi, ichma-ich)
    //                     └─ ketgan (nofaol)
    // boshqa (menejerga bog'lanmagan)
    private static readonly (string, string)[] Edges =
    [
        ("e-menejer", "e-direktor"), ("e-admin1", "e-menejer"), ("e-ustoz1", "e-menejer"),
        ("e-ustoz2", "e-ustoz1"), ("e-ketgan", "e-menejer"),
    ];

    private static readonly (string, string, string?, bool)[] Employees =
    [
        ("e-direktor", "u-direktor", null, true), ("e-menejer", "u-menejer", "p-menejer", true),
        ("e-admin1", "u-admin1", "p-admin", true), ("e-ustoz1", "u-ustoz1", "p-ustoz", true),
        ("e-ustoz2", "u-ustoz2", "p-ustoz", true), ("e-ketgan", "u-ketgan", "p-admin", false),
        ("e-boshqa", "u-boshqa", "p-admin", true), ("e-ochirilgan", "deleted:u-x", "p-admin", true),
    ];

    [Fact]
    public void TeamNoteOwners_menejer_barcha_boysunuvchilarni_ichma_ich_korad_ozini_emas()
    {
        var team = WikiRules.TeamNoteOwners("e-menejer", Edges, Employees, true, []);
        Assert.Equal(new HashSet<string> { "u-admin1", "u-ustoz1", "u-ustoz2" }, team);
    }

    [Fact]
    public void TeamNoteOwners_lavozim_filtri()
    {
        var team = WikiRules.TeamNoteOwners("e-menejer", Edges, Employees, true, ["p-ustoz"]);
        Assert.Equal(new HashSet<string> { "u-ustoz1", "u-ustoz2" }, team);
    }

    [Fact]
    public void TeamNoteOwners_sozlama_ochiq_bolsa_hech_kim()
    {
        Assert.Empty(WikiRules.TeamNoteOwners("e-menejer", Edges, Employees, false, []));
    }

    [Fact]
    public void TeamNoteOwners_oddiy_xodim_boysunuvchisi_bolmasa_hech_kim()
    {
        Assert.Empty(WikiRules.TeamNoteOwners("e-admin1", Edges, Employees, true, []));
    }

    [Fact]
    public void WikiViewer_oziniki_har_doim_rahbariyat_hammasi_menejer_jamoasi()
    {
        var oddiy = new WikiViewer("u-admin1", "", false, new HashSet<string>());
        Assert.True(oddiy.CanSeeNotesOf("u-admin1"));
        Assert.False(oddiy.CanSeeNotesOf("u-ustoz1"));   // hamkasbiniki — YO'Q

        var direktor = new WikiViewer("u-direktor", "", true, new HashSet<string>());
        Assert.True(direktor.CanSeeNotesOf("u-boshqa"));

        var menejer = new WikiViewer("u-menejer", "", false, new HashSet<string> { "u-ustoz1" });
        Assert.True(menejer.CanSeeNotesOf("u-ustoz1"));
        Assert.False(menejer.CanSeeNotesOf("u-boshqa"));
    }

    /* ---------------------------------------------------------------- saqlash va ko'chirish */

    private static WikiPage Page(string id, string parentId = "", int order = 0, string owner = "u1") =>
        new() { Id = id, ParentId = parentId, Order = order, OwnerUserId = owner, Title = id, Version = 3 };

    [Fact]
    public void ApplySave_eski_versiya_409_va_hech_narsa_ozgarmaydi()
    {
        var p = Page("a");
        var e = WikiPageOps.ApplySave(p, new WikiPageSavePayload("Yangi", "", "", BaseVersion: 2), "Ali");
        Assert.NotNull(e);
        Assert.Equal(409, e!.Status);
        Assert.True(e.Conflict);
        Assert.Equal("a", p.Title);
        Assert.Equal(3, p.Version);
    }

    [Fact]
    public void ApplySave_versiya_oshadi_matn_qidiruvga_yoziladi()
    {
        var p = Page("a");
        var json = """[{"type":"paragraph","content":[{"type":"text","text":"Salom"}]}]""";
        Assert.Null(WikiPageOps.ApplySave(p, new WikiPageSavePayload("Yangi", "📘", json, 3), "Ali"));
        Assert.Equal("Yangi", p.Title);
        Assert.Equal(4, p.Version);
        Assert.Equal("Salom", p.ContentText);
        Assert.Equal("Ali", p.UpdatedBy);
    }

    [Fact]
    public void ApplySave_rasm_bloki_rad_va_versiya_oshmaydi()
    {
        var p = Page("a");
        var e = WikiPageOps.ApplySave(p, new WikiPageSavePayload("a", "", """[{"type":"image"}]""", 3), "Ali");
        Assert.Equal(400, e!.Status);
        Assert.Equal(3, p.Version);
    }

    [Fact]
    public void ApplyMove_tartib_qayta_raqamlanadi()
    {
        List<WikiPage> pool = [Page("a", "", 0), Page("x", "", 1), Page("y", "", 2), Page("b", "a")];
        Assert.Null(WikiPageOps.ApplyMove(pool, pool[2], "", 0));   // y — eng tepaga
        Assert.Equal(new[] { "y", "a", "x" }, pool.Where(p => p.ParentId == "").OrderBy(p => p.Order).Select(p => p.Id));
    }

    [Fact]
    public void ApplyMove_boshqa_ota_ostiga()
    {
        List<WikiPage> pool = [Page("a"), Page("x", "", 1)];
        Assert.Null(WikiPageOps.ApplyMove(pool, pool[1], "a", 0));
        Assert.Equal("a", pool[1].ParentId);
    }

    [Fact]
    public void ApplyMove_ozining_ichiga_rad()
    {
        List<WikiPage> pool = [Page("a"), Page("b", "a")];
        Assert.NotNull(WikiPageOps.ApplyMove(pool, pool[0], "b", 0));
    }

    [Fact]
    public void ApplyMove_begona_egasining_eslatmasi_ostiga_rad()
    {
        List<WikiPage> pool = [Page("mening", "", 0, "u1"), Page("begona", "", 0, "u2")];
        Assert.NotNull(WikiPageOps.ApplyMove(pool, pool[0], "begona", 0));
    }

    // ---------------------------------------------------------------- BO'LIMLAR (papkalar)

    [Fact]
    public void Create_bolim_faqat_yuqori_darajada_va_nomi_standart()
    {
        List<WikiPage> pool = [Page("a")];
        Assert.Null(WikiPageOps.Create(pool, "u1", new WikiPageCreatePayload("", "  ", null, true), "Ali", out var s));
        Assert.True(s!.IsSection);
        Assert.Equal(WikiRules.DefaultSectionTitle, s.Title);

        var e = WikiPageOps.Create(pool, "u1", new WikiPageCreatePayload("a", "Ish", null, true), "Ali", out var none);
        Assert.Equal(400, e!.Status);
        Assert.Null(none);
    }

    [Fact]
    public void Create_sahifa_bolim_ichiga_tushadi_tartib_alohida()
    {
        var sec = Page("s", "", 0);
        sec.IsSection = true;
        List<WikiPage> pool = [Page("a", "", 0), Page("b", "", 1), sec];
        Assert.Null(WikiPageOps.Create(pool, "u1", new WikiPageCreatePayload("s", "Ichida", null), "Ali", out var inSec));
        Assert.Equal("s", inSec!.ParentId);
        Assert.False(inSec.IsSection);
        // Yuqori darajadagi yangi bo'lim tartibi faqat bo'limlar orasida hisoblanadi.
        Assert.Null(WikiPageOps.Create(pool, "u1", new WikiPageCreatePayload("", "Ikkinchi", null, true), "Ali", out var sec2));
        Assert.Equal(1, sec2!.Order);
    }

    [Fact]
    public void ApplyMove_bolim_sahifa_ichiga_kochmaydi_sahifa_bolimga_kochadi()
    {
        var sec = Page("s");
        sec.IsSection = true;
        List<WikiPage> pool = [Page("a"), sec];
        Assert.NotNull(WikiPageOps.ApplyMove(pool, sec, "a", 0));
        Assert.Equal("", sec.ParentId);
        Assert.Null(WikiPageOps.ApplyMove(pool, pool[0], "s", 0));
        Assert.Equal("s", pool[0].ParentId);
    }

    [Fact]
    public void ApplyMove_yuqorida_bolimlar_va_sahifalar_alohida_tartiblanadi()
    {
        var s1 = Page("s1", "", 0); s1.IsSection = true;
        var s2 = Page("s2", "", 1); s2.IsSection = true;
        List<WikiPage> pool = [Page("a", "", 0), Page("b", "", 1), s1, s2];
        Assert.Null(WikiPageOps.ApplyMove(pool, s2, "", 0));
        Assert.Equal(new[] { "s2", "s1" }, pool.Where(p => p.IsSection).OrderBy(p => p.Order).Select(p => p.Id));
        Assert.Equal(new[] { "a", "b" }, pool.Where(p => !p.IsSection).OrderBy(p => p.Order).Select(p => p.Id));
    }

    [Fact]
    public void ApplySave_bolimda_matn_saqlanmaydi()
    {
        var s = Page("s");
        s.IsSection = true;
        var json = """[{"type":"paragraph","content":[{"type":"text","text":"x"}]}]""";
        Assert.Null(WikiPageOps.ApplySave(s, new WikiPageSavePayload("", "", json, 3), "Ali"));
        Assert.Equal("", s.ContentJson);
        Assert.Equal(WikiRules.DefaultSectionTitle, s.Title);
    }

    [Fact]
    public void Qulflangan_bolim_ichidagi_sahifa_ham_qulflangan()
    {
        IEnumerable<(string, string, bool)> shape = [("s", "", true), ("p", "s", false), ("q", "", false)];
        Assert.True(WikiRules.IsLocked(shape, "p"));
        Assert.False(WikiRules.IsLocked(shape, "q"));
    }
}
