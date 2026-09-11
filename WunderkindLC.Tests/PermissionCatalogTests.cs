using System.Text.RegularExpressions;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// RUXSAT KATALOGI — "Xodimlar va rollar" matritsasi bilan KOD orasidagi sinxronlik.
///
/// <para><b>Nima uchun kerak:</b> yangi sahifa qo'shilganda uni rollarga qo'shish UNUTILADI —
/// sahifa bo'lim ruxsati ichida yashirinib qoladi va superadmin uni alohida berib bo'lmasligini
/// faqat "menga shu odam nega bu sahifani ko'ryapti" degan savol paydo bo'lgandan keyin bilardi.
/// Bu yerdagi testlar shuni oldini oladi:</para>
/// <list type="number">
///   <item>serverdagi HAR BIR <c>[AdminPerm("...")]</c> kaliti katalogda (matritsada) bormi;</item>
///   <item>frontenddagi HAR BIR <c>RequirePerm perm="..."</c> kaliti katalogda bormi;</item>
///   <item>menyudagi (<c>navigation.ts</c>) HAR BIR <c>perm</c>/<c>permAny</c> kaliti katalogda bormi;</item>
///   <item>sahifa kaliti haqiqatan O'Z bo'limining ichida turibdimi (nuqtadan oldingi qism).</item>
/// </list>
///
/// <para>Katalog — <c>WunderkindLC.Client/src/config/constants.ts</c> dagi <c>adminPermissions</c>.
/// Test uni matn sifatida o'qiydi (ikkala loyihani bir-biriga bog'lamaslik uchun).</para>
/// </summary>
public class PermissionCatalogTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
                dir = dir.Parent;
            Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
            return dir!.FullName;
        }
    }

    private static string ClientFile(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot, "WunderkindLC.Client", "src" }.Concat(parts).ToArray()));

    /// <summary>Katalogdagi barcha kalitlar (bo'lim + sahifa) — `key: '...'` qatorlaridan.</summary>
    private static HashSet<string> Catalog()
    {
        var src = ClientFile("config", "constants.ts");
        var start = src.IndexOf("export const adminPermissions", StringComparison.Ordinal);
        Assert.True(start > 0, "constants.ts da `adminPermissions` topilmadi");
        var end = src.IndexOf("export function permPagesOf", StringComparison.Ordinal);
        Assert.True(end > start, "constants.ts da katalog oxiri topilmadi");
        var block = src[start..end];
        var keys = Regex.Matches(block, @"key:\s*'([^']+)'").Select(m => m.Groups[1].Value).ToHashSet();
        Assert.True(keys.Count > 40, $"Katalogdan kutilganidan kam kalit o'qildi: {keys.Count}");
        return keys;
    }

    /// <summary>Superadmin uchun ATAYIN katalogda yo'q, faqat rol bilan darvozalangan kalitlar.</summary>
    private static readonly HashSet<string> Istisnolar = [];

    [Fact]
    public void SERVERdagi_har_bir_AdminPerm_kaliti_katalogda_bor()
    {
        var catalog = Catalog();
        var dir = Path.Combine(RepoRoot, "WunderkindLC.Server", "Controllers");
        var yoq = new List<string>();

        foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            // Atributning O'ZI (AdminPermAttribute.cs) tekshirilmaydi — u yerda misol kalitlar bor.
            if (Path.GetFileName(file) == "AdminPermAttribute.cs") continue;
            var src = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(src, @"\[AdminPerm\(([^\]]*)\)\]"))
            foreach (Match k in Regex.Matches(m.Groups[1].Value, @"""([A-Za-z][A-Za-z0-9.\-]*)"""))
            {
                var key = k.Groups[1].Value;
                if (!catalog.Contains(key) && !Istisnolar.Contains(key))
                    yoq.Add($"{Path.GetFileName(file)}: \"{key}\"");
            }
        }

        Assert.True(yoq.Count == 0,
            "Bu ruxsat kalitlari `adminPermissions` katalogida YO'Q — ya'ni superadmin ularni " +
            "\"Xodimlar va rollar\" matritsasidan bera olmaydi:\n  " + string.Join("\n  ", yoq));
    }

    [Fact]
    public void MARSHRUTlardagi_har_bir_RequirePerm_kaliti_katalogda_bor()
    {
        var catalog = Catalog();
        var app = ClientFile("App.tsx");
        // O'qituvchi portali (`/teacher/*`) BOSHQA katalogdan (`teacherPermissions`) ishlaydi.
        string[] teacherPortal = ["journal", "salary"];
        var yoq = Regex.Matches(app, @"RequirePerm perm=""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .Where(k => !catalog.Contains(k) && !teacherPortal.Contains(k))
            .Distinct()
            .ToList();

        Assert.True(yoq.Count == 0,
            "App.tsx dagi marshrut darvozalari katalogda yo'q: " + string.Join(", ", yoq));
    }

    [Fact]
    public void MENYUdagi_har_bir_ruxsat_kaliti_katalogda_bor()
    {
        var catalog = Catalog();
        var nav = ClientFile("config", "navigation.ts");
        var start = nav.IndexOf("export const navByRole", StringComparison.Ordinal);
        var block = nav[start..];

        var keys = Regex.Matches(block, @"perm:\s*'([^']+)'").Select(m => m.Groups[1].Value)
            .Concat(Regex.Matches(block, @"permAny:\s*\[([^\]]+)\]")
                .SelectMany(m => Regex.Matches(m.Groups[1].Value, @"'([^']+)'")
                    .Select(k => k.Groups[1].Value)))
            .Distinct()
            .ToList();

        // O'qituvchi portalining kalitlari (`journal`, `salary`, ...) boshqa katalogda
        // (`teacherPermissions`) — ular admin matritsasiga kirmaydi.
        string[] teacherPortal = ["journal", "salary"];
        var yoq = keys.Where(k => !catalog.Contains(k) && !teacherPortal.Contains(k)).ToList();

        Assert.True(yoq.Count == 0,
            "navigation.ts dagi ruxsat kalitlari katalogda yo'q: " + string.Join(", ", yoq));
    }

    /// <summary>
    /// HAR BIR sahifa kaliti KAMIDA BITTA joyda ISHLATILGAN bo'lishi shart: menyu bandida,
    /// marshrut darvozasida yoki serverdagi <c>[AdminPerm]</c> da.
    ///
    /// <para><b>Nega:</b> aks holda matritsada "o'lik katakcha" paydo bo'ladi — superadmin
    /// sahifani belgilaydi, lekin xodimda hech narsa ochilmaydi. Buni faqat xodim shikoyat
    /// qilgandan keyin bilib bo'lardi.</para>
    /// </summary>
    [Fact]
    public void HAR_BIR_sahifa_kaliti_KODDA_ishlatilgan()
    {
        var nav = ClientFile("config", "navigation.ts");
        var app = ClientFile("App.tsx");
        var constants = ClientFile("config", "constants.ts");
        var server = string.Join("\n", Directory
            .GetFiles(Path.Combine(RepoRoot, "WunderkindLC.Server", "Controllers"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText));

        // Katalogning O'ZIDAGI ta'rif qatorlari hisobga olinmasin — shuning uchun `constants.ts`
        // dan faqat KATALOGDAN TASHQARI qismi olinadi (`settingsPagePerm` kabi yordamchilar).
        var afterCatalog = constants[constants.IndexOf("export function permPagesOf", StringComparison.Ordinal)..];

        var pageKeys = Catalog().Where(k => k.Contains('.')).ToList();
        Assert.NotEmpty(pageKeys);

        var ishlatilmagan = pageKeys
            .Where(k => !nav.Contains($"'{k}'") && !app.Contains($"\"{k}\"")
                        && !server.Contains($"\"{k}\"") && !afterCatalog.Contains($"'{k}'")
                        // `/admin/settings/:section` darvozasi kalitni URL segmentidan QURADI
                        // (`settingsPagePerm`), ya'ni App.tsx da yalang matn bo'lib turmaydi.
                        && !(k.StartsWith("settings.", StringComparison.Ordinal)
                             && nav.Contains($"'{k}'")))
            .ToList();

        Assert.True(ishlatilmagan.Count == 0,
            "Bu sahifa kalitlari hech qayerda ishlatilmagan — matritsada belgilansa ham hech narsa " +
            "ochilmaydi (o'lik katakcha):\n  " + string.Join("\n  ", ishlatilmagan));
    }

    [Fact]
    public void SAHIFA_kaliti_OZ_bolimi_ichida_boladi()
    {
        var src = ClientFile("config", "constants.ts");
        // Har bir `pages: [...]` bloki uning ustidagi `key: '...'` (bo'lim) ga tegishli.
        var sections = Regex.Matches(src, @"key:\s*'([^']+)',\s*\r?\n\s*label:[^\n]*\r?\n(?:\s*//[^\n]*\r?\n)*\s*(?://[^\n]*\r?\n\s*)*pages:\s*\[(.*?)\n\s*\],", RegexOptions.Singleline);
        Assert.True(sections.Count >= 8, $"`pages` bloklari kam topildi: {sections.Count}");

        foreach (Match s in sections)
        {
            var section = s.Groups[1].Value;
            foreach (Match p in Regex.Matches(s.Groups[2].Value, @"key:\s*'([^']+)'"))
            {
                var page = p.Groups[1].Value;
                Assert.True(page.StartsWith(section + ".", StringComparison.Ordinal),
                    $"Sahifa kaliti \"{page}\" \"{section}\" bo'limining ichida emas — meros " +
                    "(bo'lim ruxsati sahifani ochishi) ishlamaydi.");
            }
        }
    }
}
