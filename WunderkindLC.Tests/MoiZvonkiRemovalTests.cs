using System.Text.RegularExpressions;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// BULUTLI TELEFONIYA (MoiZvonki) OLIB TASHLANDI. 2026-10 da uning o'rnini bosgan Local Call (CTI) ham
/// butunlay olib tashlandi — KPI'da qo'ng'iroq normalari (teginish, muloqot vaqti) va haftalik
/// qo'ng'iroq namunasi endi YO'Q (telefoniya manbai qolmadi).
///
/// <para><b>Nima qulflanadi:</b> olib tashlangan qism qaytib kelmasin — ruxsat kaliti, marshrut,
/// model ustuni, kod.</para>
/// </summary>
public class MoiZvonkiRemovalTests
{
    /* =========================================================================================
     *  OLIB TASHLANGAN QISM QAYTIB KELMASIN
     * ========================================================================================= */

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

    [Fact]
    public void CenterMeta_da_sinxron_kursori_ustuni_YOQ()
    {
        Assert.Null(typeof(CenterMeta).GetProperty("MoiZvonkiSyncCursor"));
    }

    [Fact]
    public void Ruxsat_katalogida_bulut_kaliti_ham_Local_Call_kaliti_ham_YOQ()
    {
        var constants = File.ReadAllText(Path.Combine(RepoRoot, "WunderkindLC.Client", "src", "config", "constants.ts"));
        Assert.DoesNotContain("'calls.cloud'", constants);
        Assert.DoesNotContain("'calls.local'", constants);   // 2026-10: Local Call olib tashlandi
    }

    [Fact]
    public void Eski_qongiroq_marshruti_faqat_YONALTIRISH_sahifa_YOQ()
    {
        var app = File.ReadAllText(Path.Combine(RepoRoot, "WunderkindLC.Client", "src", "App.tsx"));
        Assert.Contains("path=\"calls/local\"", app);         // eski xatcho'p 404 bermasin — redirect
        Assert.DoesNotContain("CallCenterPage", app);
        Assert.DoesNotContain("LocalCallPage", app);
    }

    [Fact]
    public void Kodda_bulut_provayderi_izi_QOLMAGAN()
    {
        // Migratsiyalar TARIX (o'zgartirilmaydi) — tekshiruvdan tashqari.
        var roots = new[]
        {
            ("WunderkindLC.Server", "*.cs"), ("WunderkindLC.Application", "*.cs"),
            ("WunderkindLC.Domain", "*.cs"), ("WunderkindLC.Infrastructure", "*.cs"),
            (Path.Combine("WunderkindLC.Client", "src"), "*.ts"),
            (Path.Combine("WunderkindLC.Client", "src"), "*.tsx"),
        };
        var rx = new Regex("moi[_\\- ]?zvonki", RegexOptions.IgnoreCase);
        var hits = new List<string>();
        foreach (var (rel, pattern) in roots)
        {
            var dir = Path.Combine(RepoRoot, rel);
            foreach (var file in Directory.GetFiles(dir, pattern, SearchOption.AllDirectories))
            {
                var p = file.Replace('\\', '/');
                if (p.Contains("/Migrations/") || p.Contains("/bin/") || p.Contains("/obj/")
                    || p.Contains("/node_modules/")) continue;
                if (rx.IsMatch(File.ReadAllText(file))) hits.Add(Path.GetRelativePath(RepoRoot, file));
            }
        }
        Assert.True(hits.Count == 0, "Bulut telefoniya izi qolgan:\n  " + string.Join("\n  ", hits));
    }

    [Fact]
    public void CSRF_istisnolarida_telefoniya_webhook_prefiksi_YOQ()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot, "WunderkindLC.Server", "CsrfMiddleware.cs"));
        Assert.DoesNotContain("/api/telephony/", src);
    }
}
