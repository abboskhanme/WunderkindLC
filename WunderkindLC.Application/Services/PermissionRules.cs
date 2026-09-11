namespace WunderkindLC.Application.Services;

/// <summary>
/// XODIM (staff) RUXSATLARI — sof qoidalar (rol/HTTP kontekstisiz), shuning uchun testlanadi.
///
/// <para>Ruxsat claim'i uch ko'rinishda bo'ladi:</para>
/// <list type="bullet">
///   <item><c>"bolim"</c> — BUTUN BO'LIM (barcha sahifalar, barcha amallar). Eski yozuvlar shunday.</item>
///   <item><c>"bolim:amal"</c> — butun bo'lim, faqat shu amal: <c>create</c> / <c>edit</c> / <c>delete</c>.</item>
///   <item><c>"bolim.sahifa"</c> yoki <c>"bolim.sahifa:amal"</c> — FAQAT BITTA SAHIFA
///     (masalan <c>students.turnstile</c> — "Turniket"). Nuqta (<c>.</c>) sahifa ajratkichi.</item>
/// </list>
///
/// <para><b>MEROS — bitta yo'nalishda (PASTGA):</b> bo'lim ruxsati o'z sahifalarining HAMMASINI
/// qamrab oladi (o'qish ham, yozish ham). Ya'ni eski <c>"students"</c> claim'i yangi
/// <c>"students.turnstile"</c> darvozasidan ham o'tadi — <b>mavjud xodimlar ruxsati o'zgarmaydi</b>.</para>
///
/// <para><b>YUQORIGA — FAQAT O'QISH:</b> bitta sahifaga ruxsati bor xodim shu BO'LIMDA ishlaydi
/// deb hisoblanadi (<see cref="HasSection"/>), ya'ni bo'limning GET'lari unga ochiladi. Bu kerak:
/// sahifa o'z ma'lumotini bo'lim controlleridan o'qiydi. Lekin <b>YOZISH yuqoriga MEROS
/// BO'LMAYDI</b> (<see cref="CanWrite"/>) — aks holda "Turniket" operatori
/// <c>POST /admin/students</c> bilan o'quvchi yarata olardi.</para>
///
/// <para>⚠️ Shu sababli NOZIK o'qish darvozalari (<c>ReadRequiresPerm</c>, javobni tozalash)
/// bo'lim kaliti bilan emas, <b>SAHIFA kaliti</b> bilan qo'yilishi kerak — masalan passport
/// skanlarini tozalash <c>"students.list"</c> bo'yicha tekshiriladi, <c>"students"</c> bo'yicha
/// emas (aks holda turniket operatoriga hujjatlar ochilib ketardi).</para>
///
/// <para>Mantiq <c>AdminPermAttribute</c> dan AJRATIB olindi (nusxa emas): u Server qatlamida,
/// test loyihasi esa unga bog'lanmagan. Ruxsat qoidasi — xavfsizlikning tayanch nuqtasi,
/// shuning uchun u test bilan qoplangan bo'lishi kerak.</para>
/// </summary>
public static class PermissionRules
{
    /// <summary>Bo'lim va sahifa kalitlari orasidagi ajratkich: <c>"students.turnstile"</c>.</summary>
    public const char PageSeparator = '.';

    /// <summary>
    /// Sahifa kalitining BO'LIMI (<c>"students.turnstile"</c> → <c>"students"</c>).
    /// Kalit sahifa bo'lmasa (nuqta yo'q) — <c>null</c>.
    /// </summary>
    public static string? ParentOf(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        var i = key.IndexOf(PageSeparator);
        return i <= 0 ? null : key[..i];
    }

    /// <summary>Claim aynan shu kalitga (yalang yoki <c>kalit:amal</c>) tegishlimi.</summary>
    private static bool Matches(string? claim, string key) =>
        claim is not null && (claim == key || claim.StartsWith(key + ":", StringComparison.Ordinal));

    /// <summary>
    /// Xodim shu bo'limda/sahifada ISHLAYDIMI — ya'ni O'QISHGA yo'l bormi.
    ///
    /// <para>Hisobga olinadi:</para>
    /// <list type="number">
    ///   <item>aynan shu kalit (<c>"kalit"</c> yoki <c>"kalit:amal"</c>);</item>
    ///   <item><b>pastga meros</b> — kalit sahifa bo'lsa, uning BO'LIMI berilgan bo'lsa ham yetadi;</item>
    ///   <item><b>yuqoriga meros (faqat o'qish)</b> — kalit bo'lim bo'lsa, uning ISTALGAN sahifasi
    ///     berilgan bo'lsa ham yetadi (sahifa o'z ma'lumotini bo'lim controlleridan o'qiydi).</item>
    /// </list>
    ///
    /// <para>DIQQAT: nom bo'yicha ADASHISH bo'lmasligi kerak — <c>"students"</c> so'ragan joy
    /// <c>"students-arxiv"</c> claim'i bilan ochilib ketmasin. Shuning uchun mos kelish shartlari
    /// faqat aniq: tenglik, <c>"kalit:"</c> yoki <c>"kalit."</c> prefiksi.</para>
    /// </summary>
    /// <param name="permClaims">Foydalanuvchining <c>perm</c> turidagi claim qiymatlari.</param>
    public static bool HasSection(IEnumerable<string>? permClaims, string section)
    {
        if (permClaims is null || string.IsNullOrEmpty(section)) return false;
        var parent = ParentOf(section);
        var childPrefix = section + PageSeparator;
        foreach (var c in permClaims)
        {
            if (c is null) continue;
            if (Matches(c, section)) return true;
            // Yuqoriga (faqat o'qish): "students" so'raldi, xodimda "students.turnstile" bor.
            if (c.StartsWith(childPrefix, StringComparison.Ordinal)) return true;
            // Pastga: "students.turnstile" so'raldi, xodimda butun "students" bor.
            if (parent is not null && Matches(c, parent)) return true;
        }
        return false;
    }

    /// <summary>
    /// Bo'lim/sahifa bo'yicha TO'LIQ ruxsat (barcha amallar) berilganmi — faqat yalang kalit
    /// (yoki sahifa uchun: yalang BO'LIM kaliti). Nozik amallar uchun (parol eksporti va h.k.).
    /// </summary>
    public static bool HasFullSection(IEnumerable<string>? permClaims, string section)
    {
        if (permClaims is null || string.IsNullOrEmpty(section)) return false;
        var parent = ParentOf(section);
        foreach (var c in permClaims)
        {
            if (c == section) return true;
            if (parent is not null && c == parent) return true;
        }
        return false;
    }

    /// <summary>
    /// HTTP amaliga mos keladigan ruxsat harakati: POST → <c>create</c>, DELETE → <c>delete</c>,
    /// qolgan yozish amallari (PUT/PATCH) → <c>edit</c>.
    /// </summary>
    public static string ActionFor(string httpMethod) =>
        string.Equals(httpMethod, "POST", StringComparison.OrdinalIgnoreCase) ? "create"
        : string.Equals(httpMethod, "DELETE", StringComparison.OrdinalIgnoreCase) ? "delete"
        : "edit";

    /// <summary>
    /// Yozish amaliga ruxsat bormi: yalang kalit (to'liq) YOKI aniq <c>kalit:amal</c>.
    ///
    /// <para>⚠️ MEROS FAQAT PASTGA: bo'lim ruxsati sahifaning yozishini ochadi, lekin sahifa
    /// ruxsati BO'LIMning yozishini OCHMAYDI (bitta sahifa berilgan xodim boshqa sahifalarning
    /// endpointlariga yozib yubormasin).</para>
    /// </summary>
    public static bool CanWrite(IEnumerable<string>? permClaims, string section, string httpMethod)
    {
        if (permClaims is null || string.IsNullOrEmpty(section)) return false;
        var list = permClaims as IReadOnlyCollection<string> ?? permClaims.ToList();
        var action = ActionFor(httpMethod);
        if (list.Contains(section) || list.Contains(section + ":" + action)) return true;
        var parent = ParentOf(section);
        return parent is not null && (list.Contains(parent) || list.Contains(parent + ":" + action));
    }
}
