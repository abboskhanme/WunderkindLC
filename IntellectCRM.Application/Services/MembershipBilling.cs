using IntellectCRM.Application.Abstractions;
using IntellectCRM.Domain;

namespace IntellectCRM.Application.Services;

/// <summary>
/// A'ZOLIKNI MUZLATISHDAGI HISOB-KITOB — <b>YAGONA MANBA</b>.
///
/// <para>Muzlatish to'rt yo'l bilan sodir bo'ladi va HAMMASIDA hisob AYNAN bir xil bo'lishi shart:
/// <list type="number">
///   <item>"Muzlatish" tugmasi (<c>ClassesController.FreezeMember</c>);</item>
///   <item>"Guruh almashtirish" (<c>TransferMember</c>) — eski guruh tomoni;</item>
///   <item>"Guruhni yopish" (<c>Close</c>) — barcha a'zolar birdan;</item>
///   <item>"Guruhni tugatish (sertifikat bilan)" (<c>CompleteAndTransfer</c>) — barcha a'zolar birdan.</item>
/// </list>
/// Ilgari bu 5 qator har joyda qo'lda takrorlangan edi va (4)-yo'lda UMUMAN yo'q edi — sertifikat
/// bilan tugatishda o'quvchiga eski guruh uchun qisman oylik YOZILMASDI (allaqachon yozilgan
/// TO'LIQ oylik esa kamaymasdi). Endi hamma joy shu metodni chaqiradi.</para>
///
/// <para>SaveChanges QILINMAYDI — chaqiruvchi saqlaydi.</para>
/// </summary>
public static class MembershipBilling
{
    /// <summary>Muzlatish hisob-kitobining natijasi.</summary>
    /// <param name="Charged">Muzlatish oyi uchun qisman to'lov yozildimi (o'quvchi haqiqatan o'qigan bo'lsa).</param>
    /// <param name="Restored">Muzlatishdan keyingi oylar hisobi bekor qilinib, balansga qaytarilgan summa.</param>
    /// <param name="PurgedMonths">Hisobi bekor qilingan oylar ("yyyy-MM") — <see cref="TuitionService.CarryGroupAdvanceAsync"/>
    /// ga <c>zeroOwedMonths</c> sifatida uzatiladi (EF hali flush qilinmagan qatorni so'rovda baribir qaytaradi).</param>
    public readonly record struct FreezeSettlement(bool Charged, decimal Restored, List<string> PurgedMonths);

    /// <summary>
    /// A'zolikni <paramref name="freezeDate"/> sanasidan muzlatishdagi HISOB:
    /// <list type="bullet">
    ///   <item>shu oyda muzlatish SANASIGACHA (shu sana ham) qatnashgan darslar uchun QISMAN to'lov
    ///     (<see cref="TuitionService.ChargeFreezeProrateAsync"/>);</item>
    ///   <item>muzlatish oyidan KEYINGI oylarga allaqachon yozilgan hisoblar BEKOR qilinadi
    ///     (<see cref="TuitionService.PurgeChargesAfterMonthAsync"/>) — orqaga sanalgan muzlatishda
    ///     qarz sanadan keyin o'smasin; <c>Locked</c> qatorlar tegilmaydi;</item>
    ///   <item>muzlatish sanasi AKTIVLASHTIRISH sanasidan OLDIN bo'lsa (o'quvchi bu guruhda umuman
    ///     o'qimagan) — qisman to'lov ham yozilmaydi va aktivlashtirish oyi hisobi ham bekor qilinadi.</item>
    /// </list>
    /// A'zolik maydonlarini (<c>Status</c>/<c>FrozenAt</c>/<c>LeftAt</c>) bu metod O'ZGARTIRMAYDI —
    /// har bir chaqiruvchi o'z holatini o'zi qo'yadi (muzlatish, ketkazish yoki "tugatgan").
    /// </summary>
    /// <param name="activatedAt">A'zolikning aktivlashtirilgan sanasi (<see cref="StudentGroup.ActivatedAt"/>).</param>
    /// <param name="lessonFee">Kursning bir dars yaxlit narxi, OLDINDAN hisoblangan bo'lsa
    /// (<see cref="TuitionService.LessonFeesForCoursesAsync"/>). ⚠️ FAQAT tezlik uchun — <c>null</c>
    /// bo'lsa AYNAN o'sha qiymat quyida yakka so'rov bilan olinadi, ya'ni hisob o'zgarmaydi.
    /// Ommaviy muzlatishda guruh (demak kurs) bitta bo'lgani uchun bu bir xil so'rovni har a'zolik
    /// uchun takrorlamaslikka imkon beradi. Qolgan uchta yo'l (guruh almashtirish, guruhni yopish,
    /// sertifikat bilan tugatish) parametrni bermaydi va avvalgidek ishlaydi.</param>
    public static async Task<FreezeSettlement> SettleFreezeAsync(
        IAppDbContext db, Student student, Group group, string activatedAt, string freezeDate,
        decimal? lessonFee = null)
    {
        var frozenBeforeActive = activatedAt.Length >= 10
                                 && string.CompareOrdinal(activatedAt, freezeDate) > 0;

        if (!frozenBeforeActive)
            await TuitionService.ChargeFreezeProrateAsync(db, student, group, activatedAt, freezeDate, lessonFee);

        var (restored, purged) = await TuitionService.PurgeChargesAfterMonthAsync(
            db, student, group.Id, freezeDate, inclusive: frozenBeforeActive);

        return new FreezeSettlement(!frozenBeforeActive, restored, purged);
    }
}
