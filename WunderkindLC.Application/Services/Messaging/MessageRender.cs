using System.Text.RegularExpressions;
using WunderkindLC.Domain;
using Group = WunderkindLC.Domain.Group;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>
/// Renders a template into the final Telegram-HTML text (SPEC P2.3, pure).
/// <para>Step 1 — <see cref="TelegramHtml.SanitizeTemplate"/> validates the ADMIN markup (stray <c>&lt;</c>/<c>&amp;</c>
/// escaped). Step 2 — token substitution with every VALUE HTML-escaped (<see cref="MessageTokenizer"/> with
/// <c>html: true</c>). Data can therefore never inject tags.</para>
/// </summary>
public static class MessageRender
{
    public const string HeaderCampaign = "<b>📢 Markaz e'loni</b>\n\n";
    public const string HeaderDebt = "<b>💰 To'lov eslatmasi</b>\n\n";

    /// <summary>Header of an auto message: «🔔 {rule name or trigger label}» in bold.</summary>
    public static string HeaderAuto(string title) => "<b>🔔 " + TelegramHtml.Escape(title) + "</b>\n\n";

    /// <summary>Campaign header by kind: broadcast/archive — «📢 Markaz e'loni»; debt — none (the system debt text
    /// is self-explanatory).</summary>
    public static string HeaderForCampaign(string kind) => kind == "debt" ? "" : HeaderCampaign;

    /// <summary>
    /// Student / parent message. <paramref name="reg"/> (the destination chat) fills <c>{ota-ona}</c>/<c>{telefon}</c>
    /// from the registration and <c>{qarindosh}</c> from its relation; without it the student card is used.
    /// <paramref name="extra"/> (event values) override common values.
    /// </summary>
    public static (string Html, IReadOnlyList<string> Errors) RenderStudent(
        string? template, Student student, TelegramRegistration? reg, Group? group, string? teacherName,
        IReadOnlyDictionary<string, string>? extra, string? centerName, string? audience = null)
    {
        var (sanitized, errors) = TelegramHtml.SanitizeTemplate(template);
        var parentName = !string.IsNullOrWhiteSpace(reg?.ParentName) ? reg!.ParentName : student.ParentFullName;
        var phone = reg is not null && reg.Phone.Length > 0
            ? PhoneUtil.Normalize(reg.Phone)
            : MessagingAudience.StudentPhone(student, audience);
        var withExtra = MessageTokenizer.ApplyExtra(sanitized, WithRelation(extra, reg), html: true);
        var html = MessageTokenizer.Student(withExtra, student, parentName, phone, centerName,
            extra: null, group: group, teacherName: teacherName, html: true);
        return (html, errors);
    }

    /// <summary>Teacher message (teachers audience) — <see cref="MessageTokenizer.Teacher"/> with escaped values.</summary>
    public static (string Html, IReadOnlyList<string> Errors) RenderTeacher(
        string? template, Teacher teacher, Group? group, IReadOnlyDictionary<string, string>? extra, string? centerName)
    {
        var (sanitized, errors) = TelegramHtml.SanitizeTemplate(template);
        var withExtra = MessageTokenizer.ApplyExtra(sanitized, extra, html: true);
        var html = MessageTokenizer.Teacher(withExtra, teacher, centerName, extra: null, group: group, html: true);
        return (html, errors);
    }

    private static IReadOnlyDictionary<string, string>? WithRelation(
        IReadOnlyDictionary<string, string>? extra, TelegramRegistration? reg)
    {
        if (reg is null) return extra;
        var d = extra is null ? new Dictionary<string, string>() : new Dictionary<string, string>(extra);
        if (!d.ContainsKey("{qarindosh}")) d["{qarindosh}"] = TelegramRelations.Label(reg.Relation);
        return d;
    }

    private static readonly Regex TokenRx = new(@"\{[a-z_\-]+\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Tokens that make a message student-specific: every catalog token of group <c>student</c>, plus
    /// <c>{guruh}</c>, <c>{oqituvchi}</c>, <c>{dars_*}</c>, <c>{qarzdorlik}</c>.</summary>
    private static readonly HashSet<string> StudentTokens = new(
        MessageTokenCatalog.All.Where(t => t.Group == "student").Select(t => t.Token)
            .Concat(new[] { "{guruh}", "{oqituvchi}", "{dars_sana}", "{dars_vaqti}", "{dars_kunlari}", "{qarzdorlik}", "{ota_ona}" }),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Does the template depend on the student? False ⇒ a campaign sends ONE message per chat (a parent of
    /// two children does not get two identical messages).</summary>
    public static bool HasStudentTokens(string? template) =>
        !string.IsNullOrEmpty(template) && TokenRx.Matches(template).Any(m => StudentTokens.Contains(m.Value));

    /// <summary><c>{…}</c> occurrences the catalog does not know (distinct, in order of appearance).</summary>
    public static IReadOnlyList<string> UnknownTokens(string? template)
    {
        if (string.IsNullOrEmpty(template)) return Array.Empty<string>();
        return TokenRx.Matches(template).Select(m => m.Value)
            .Where(t => !MessageTokenCatalog.IsKnown(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Uzbek error for unknown tokens: «Noma'lum o'zgaruvchi: {ismi}».</summary>
    public static string UnknownTokensError(IReadOnlyList<string> unknown) =>
        "Noma'lum o'zgaruvchi: " + string.Join(", ", unknown);

    /// <summary>Event tokens of <paramref name="template"/> filled with catalog examples — the preview of an auto
    /// rule (the dispatcher would pass the real event values).</summary>
    public static Dictionary<string, string> PreviewExtras(string? template, string? trigger)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(template)) return d;
        foreach (Match m in TokenRx.Matches(template))
        {
            var info = MessageTokenCatalog.All.FirstOrDefault(
                t => string.Equals(t.Token, m.Value, StringComparison.OrdinalIgnoreCase));
            if (info is null || info.Group != "event" || info.Example.Length == 0) continue;
            d[info.Token] = info.Example;
        }
        return d;
    }
}
