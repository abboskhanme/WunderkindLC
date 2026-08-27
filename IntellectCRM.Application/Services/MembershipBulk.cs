namespace IntellectCRM.Application.Services;

/// <summary>
/// OMMAVIY (bir paytda ko'p o'quvchi) muzlatish/aktivlashtirish QOIDALARI — sof funksiyalar.
///
/// <para>Hisob-kitobning o'zi bu yerda EMAS: u yakka amallar bilan AYNAN bir xil qoladi
/// (<see cref="MembershipBilling.SettleFreezeAsync"/> / <see cref="TuitionService"/>). Bu yerda
/// faqat "QAYSI a'zolik amalga tushadi" va "eng ko'pi bilan nechta" savoli.</para>
///
/// <para><c>ClassesController.BulkApplyAsync</c> a'zoliklarni SQL'dan holatiga QARAMASDAN oladi va
/// filtrni AYNAN shu funksiya bilan qiladi — qoida bir joyda tursin (SQL predikatiga ko'chirilsa
/// ikkisi vaqt o'tib ayrilib ketardi). Yon foydasi: mos kelmagan a'zolik yo'qolmaydi, u javobda
/// "o'tkazib yuborildi" bo'lib sanaladi.</para>
/// </summary>
public static class MembershipBulk
{
    /// <summary>Bir so'rovda ko'rib chiqiladigan A'ZOLIKLAR chegarasi.
    /// Ommaviy amal navbatda emas, so'rov ichida bajariladi — cheksiz tanlov so'rovni cho'zib yuborardi.</summary>
    public const int MaxTargets = 500;

    /// <summary>
    /// A'zolik ommaviy amalga tushadimi (faqat FAOL — <c>IsActive</c> — a'zoliklar ko'riladi).
    /// <list type="bullet">
    ///   <item><b>Muzlatish</b>: <c>active</c> va <c>trial</c>. Allaqachon <c>frozen</c> bo'lgani
    ///     tushmaydi — qayta muzlatish qisman to'lovni IKKI marta yozardi.</item>
    ///   <item><b>Aktivlashtirish</b>: <c>active</c> dan boshqa hammasi (<c>trial</c>, <c>frozen</c>).
    ///     Allaqachon faoli tushmaydi — qayta aktivlashtirish <c>ActivatedAt</c> ni surib,
    ///     hisoblangan oyni buzardi.</item>
    /// </list>
    /// ⚠️ Mos kelmagan tanlov XATO EMAS — u jimgina o'tkazib yuboriladi va javobda
    /// "o'tkazib yuborildi" bo'lib sanaladi: 30 ta tanlangandan bittasi tufayli butun amal
    /// to'xtamasligi kerak.
    /// </summary>
    /// <param name="status"><c>StudentGroup.Status</c>.</param>
    /// <param name="freeze"><c>true</c> — muzlatish, <c>false</c> — aktivlashtirish.</param>
    public static bool IsEligible(string? status, bool freeze) =>
        freeze
            ? status is "active" or "trial"
            : status != "active";
}
