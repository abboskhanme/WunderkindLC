using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>
/// WHO owns a Telegram chat linked to a student (SPEC P2.3, pure, <c>TelegramRelationsTests</c>).
/// <para>The bot matches a shared contact against all four numbers of a student. A number may be on several
/// fields at once (ParentPhone is usually the father's or the mother's), so the order is fixed:
/// <b>FatherPhone → MotherPhone → ParentPhone → Phone</b> — parents are the likely owners of a shared number.
/// No match → <c>other</c>. The admin can correct it in the profile.</para>
/// </summary>
public static class TelegramRelations
{
    public const string Father = "father";
    public const string Mother = "mother";
    public const string Parent = "parent";
    public const string StudentSelf = "student";
    public const string Other = "other";

    /// <summary>All relations in UI order.</summary>
    public static readonly string[] All = { Father, Mother, Parent, StudentSelf, Other };

    /// <summary>Default audience «Barcha ota-onalar» — every relation except the student's own chat.</summary>
    public static readonly string[] Parents = { Father, Mother, Parent, Other };

    public static bool IsKnown(string? relation) => relation is not null && All.Contains(relation);

    /// <summary>Detects the relation of a registration created from <paramref name="phone"/>.</summary>
    public static string Detect(Student s, string? phone)
    {
        var key = PhoneUtil.Key(phone);
        if (key.Length < 7) return Other;
        if (Same(s.FatherPhone, key)) return Father;
        if (Same(s.MotherPhone, key)) return Mother;
        if (Same(s.ParentPhone, key)) return Parent;
        if (Same(s.Phone, key)) return StudentSelf;
        return Other;

        static bool Same(string? field, string k)
        {
            var f = PhoneUtil.Key(field);
            return f.Length >= 7 && f == k;
        }
    }

    /// <summary>Is the chat a parent's (or unknown — <c>""</c> = not yet backfilled → treated as a parent)?</summary>
    public static bool IsParent(string? relation) =>
        relation is null or "" or Father or Mother or Parent or Other;

    /// <summary>Uzbek label: father «Otasi», mother «Onasi», parent «Ota-onasi», student «O'zi», other «Boshqa».</summary>
    public static string Label(string? relation) => relation switch
    {
        Father => "Otasi",
        Mother => "Onasi",
        Parent => "Ota-onasi",
        StudentSelf => "O'zi",
        Other => "Boshqa",
        _ => "Ota-onasi",
    };

    /// <summary>Normalises a stored relation for filters: <c>""</c> (not backfilled) is a parent.</summary>
    public static string Effective(string? relation) => string.IsNullOrEmpty(relation) ? Parent : relation;
}
