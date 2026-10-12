using System.Text;

namespace WunderkindLC.Application.Services.Accounts;

/// <summary>
/// One row of the group «Login/parollar» window and its Excel export (CRM Phase 6, SPEC §7 P6.1).
/// <paramref name="Password"/> is null when unknown (<paramref name="Known"/> = false) or when the student has
/// no account (<paramref name="HasAccount"/> = false, <paramref name="Login"/> = "").
/// </summary>
public sealed record GroupCredentialRow(
    string StudentId, string FullName, string Status, bool YearFreeze,
    bool HasAccount, string Login, string? Password, bool Known, string Source, int TelegramChats);

/// <summary>Pure formatting of the per-group login/password Excel file (tests: <c>GroupCredentialsTests</c>).</summary>
public static class GroupCredentialsSheet
{
    /// <summary>Column order fixed by the SPEC: «№, F.I.SH., Login, Parol, Telegram, Holat».</summary>
    public static readonly IReadOnlyList<string> Headers = new[] { "№", "F.I.SH.", "Login", "Parol", "Telegram", "Holat" };

    /// <summary>Membership state label — the same words as the group's «O'quvchilar» tab.</summary>
    public static string StatusLabel(string status, bool yearFreeze) => status switch
    {
        "active" => "Aktiv",
        "trial" => "Sinov",
        "frozen" when yearFreeze => "Aktiv muzlatilgan",
        "frozen" => "Muzlatilgan",
        _ => status,
    };

    /// <summary>Telegram cell: number of linked chats or «Ulanmagan».</summary>
    public static string TelegramCell(int chats) => chats > 0 ? $"Ulangan ({chats})" : "Ulanmagan";

    public static IEnumerable<IReadOnlyList<string>> Rows(IEnumerable<GroupCredentialRow> rows) =>
        rows.Select((r, i) => (IReadOnlyList<string>)new[]
        {
            (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            r.FullName,
            r.HasAccount ? r.Login : "(akkaunt yo'q)",
            r.HasAccount ? (r.Known ? r.Password ?? "" : StudentCredentials.UnknownExportText) : "",
            TelegramCell(r.TelegramChats),
            StatusLabel(r.Status, r.YearFreeze),
        });

    /// <summary>
    /// <c>login_parollar_{guruh}_{sana}.xlsx</c>. The group name is reduced to letters/digits/'-' (others → '_')
    /// so it is a safe file name on every OS; an empty result becomes "guruh".
    /// </summary>
    public static string FileName(string groupName, DateTime now)
    {
        var sb = new StringBuilder();
        foreach (var ch in (groupName ?? "").Trim())
        {
            if (char.IsLetterOrDigit(ch) || ch == '-') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
        }
        var name = sb.ToString().Trim('_');
        if (name.Length > 60) name = name[..60].TrimEnd('_');
        if (name.Length == 0) name = "guruh";
        return $"login_parollar_{name}_{now:yyyy-MM-dd}.xlsx";
    }
}
