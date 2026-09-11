namespace WunderkindLC.Application.Services.Kpi;

/// <summary>
/// POG'ONA tanlash — Excel <c>INDEX(...; MATCH(x; froms; 1))</c> ning AYNAN nusxasi
/// (sof funksiyalar, testlangan: <c>KpiRulesTests</c>).
///
/// <para>Butun KPI moduli shu bitta funksiyaga tayanadi: konversiya, ketish foizi, uzaytirish va
/// samaradorlik — hammasi bir xil "oxirgi mos pog'ona" qoidasi bilan tanlanadi. Shuning uchun u
/// YAGONA joyda turadi: to'rt joyda qayta yozilsa, bittasidagi <c>&lt;</c> / <c>&lt;=</c> farqi
/// xodimning oyligini jimgina o'zgartirib qo'yardi.</para>
/// </summary>
public static class KpiRules
{
    /// <summary>
    /// <paramref name="value"/> tushadigan pog'onani topadi: <c>From &lt;= value</c> bo'lgan
    /// ENG OXIRGI qator.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Excel'da x birinchi <c>From</c> dan kichik bo'lsa <c>#N/A</c> chiqadi — biz esa
    /// ATAYIN ENG PAST pog'onani olamiz.</b> Sabab: jadvallar 0 dan boshlangani uchun bu holat
    /// odatda bo'lmaydi, lekin qoida qo'lda tahrirlanadi (Qoidalar sahifasi) va admin birinchi
    /// qatorni 0.05 dan boshlab qo'yishi mumkin. O'shanda <c>#N/A</c> ni takrorlasak, hisob
    /// XATOGA yiqilib, xodim oyligini umuman ko'rmasdi. Eng past pog'ona esa "yomon natija —
    /// eng past koeffitsient" degan izchil javob beradi.
    ///
    /// ⚠️ Ro'yxat BO'SH bo'lsa 1.0 — ya'ni "koeffitsient qo'llanmadi", 0 emas. Nol qaytarilsa
    /// qoidasi hali seed qilinmagan rolda butun bonus jimgina yo'qolardi.
    ///
    /// ⚠️ Ro'yxat <c>From</c> bo'yicha O'SISH tartibida deb faraz QILINMAYDI — u qo'lda
    /// tahrirlanadi, ya'ni tartibi buzilishi mumkin. Shuning uchun "eng oxirgi qator" emas,
    /// "eng KATTA mos <c>From</c>" izlanadi (teng bo'lsa — ro'yxatdagi keyingisi g'olib,
    /// Excel bilan bir xil).
    /// </remarks>
    public static KpiTier? TierOf(IReadOnlyList<KpiTier>? tiers, double value)
    {
        if (tiers is null || tiers.Count == 0) return null;

        KpiTier? best = null;
        KpiTier? lowest = null;
        foreach (var t in tiers)
        {
            if (lowest is null || t.From < lowest.From) lowest = t;
            if (t.From <= value && (best is null || t.From >= best.From)) best = t;
        }
        // Barcha pog'onalardan ham past qiymat — eng pastini beramiz (Excel'dagi #N/A o'rniga).
        return best ?? lowest;
    }

    /// <summary>Pog'ona koeffitsienti. Mos pog'ona topilmasa (ro'yxat bo'sh) — 1.0.</summary>
    public static double Coef(IReadOnlyList<KpiTier>? tiers, double value) =>
        TierOf(tiers, value)?.Coef ?? 1.0;
}
