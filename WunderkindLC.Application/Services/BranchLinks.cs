namespace WunderkindLC.Application.Services;

/// <summary>
/// FILIAL ALMASHTIRGICH (2026-10-27, egasining talabi): har filial — ALOHIDA ilova + ALOHIDA baza (o'z xodimlari, o'z
/// login/parollari, noldan), lekin superadmin tepadagi tanlovdan boshqa filialga o'tadi. Ro'yxat sozlamadan
/// (`Branches:Links`, env `BRANCH_LINKS`): <c>"Yaypan|https://lc.wunderkindedu.uz;Furqat|https://furqat.wunderkindedu.uz"</c>.
/// Barcha filial ilovalariga AYNAN bir xil qiymat beriladi — joriy filial so'rov hostidan aniqlanadi. Sof, testlangan.
/// <para>⚠️ Bu faqat HAVOLA: filiallar orasida ma'lumot ham, sessiya ham o'tmaydi — har filialda superadmin o'z
/// login/paroli bilan kiradi (infra/branch/new-branch.sh).</para>
/// </summary>
public static class BranchLinks
{
    public sealed record Link(string Name, string Url, bool Current);

    /// <summary>Ko'pi bilan shuncha filial — buzuq/ulkan qiymat ekranni to'ldirib yubormasin.</summary>
    public const int MaxLinks = 20;

    public static List<Link> Parse(string? raw, string? currentHost)
    {
        var result = new List<Link>();
        if (string.IsNullOrWhiteSpace(raw)) return result;
        var host = (currentHost ?? "").Trim().ToLowerInvariant();
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bar = part.IndexOf('|');
            if (bar <= 0) continue;
            var name = part[..bar].Trim();
            var url = part[(bar + 1)..].Trim().TrimEnd('/');
            // Faqat https va to'liq manzil — `javascript:` yoki nisbiy yo'l havola bo'lib chiqmasin.
            if (name.Length == 0 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                continue;
            if (result.Any(l => string.Equals(l.Url, url, StringComparison.OrdinalIgnoreCase))) continue;
            result.Add(new Link(name, url, uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)));
            if (result.Count >= MaxLinks) break;
        }
        return result;
    }
}
