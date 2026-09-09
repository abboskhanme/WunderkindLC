using Microsoft.EntityFrameworkCore;
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
    /// <param name="membership">Muzlatilayotgan a'zolik — berilsa uning YOPILGAN faol davrlari
    /// (<see cref="StudentGroup.PastPeriods"/>) muzlatish sanasiga QIRQILADI.
    /// <para>⚠️ ORQAGA sanalgan muzlatishda purge shu (o'quvchi, guruh) ning muzlatish oyidan
    /// keyingi BARCHA hisoblarini o'chiradi — eski yopilgan davr ichidagilarni ham. Davrlar
    /// qirqilmasa, keyingi accrual sikli o'sha oylarni QAYTA yozardi
    /// (<see cref="MembershipLifecycle.TruncatePastPeriodsAfter"/>).</para>
    /// <c>null</c> bo'lsa tarixga tegilmaydi (eski chaqiruvlar).</param>
    public static async Task<FreezeSettlement> SettleFreezeAsync(
        IAppDbContext db, Student student, Group group, string activatedAt, string freezeDate,
        decimal? lessonFee = null, StudentGroup? membership = null)
    {
        if (membership is not null)
            MembershipLifecycle.TruncatePastPeriodsAfter(membership, freezeDate);

        var frozenBeforeActive = activatedAt.Length >= 10
                                 && string.CompareOrdinal(activatedAt, freezeDate) > 0;

        if (!frozenBeforeActive)
            await TuitionService.ChargeFreezeProrateAsync(db, student, group, activatedAt, freezeDate, lessonFee);

        var (restored, purged) = await TuitionService.PurgeChargesAfterMonthAsync(
            db, student, group.Id, freezeDate, inclusive: frozenBeforeActive);

        return new FreezeSettlement(!frozenBeforeActive, restored, purged);
    }

    /// <summary>Guruh o'chirilishidan oldin uning hisoblari balansga qaytarilishi natijasi.</summary>
    /// <param name="Rows">O'chirilgan hisob qatorlari soni.</param>
    /// <param name="Students">Balansi tuzatilgan o'quvchilar soni.</param>
    /// <param name="Credited">Balanslarga QAYTARILGAN jami effektiv summa.</param>
    public readonly record struct GroupChargeCredit(int Rows, int Students, decimal Credited);

    /// <summary>
    /// GURUH BUTUNLAY O'CHIRILAYOTGANDA uning barcha <c>MonthlyCharge</c> qatorlarini o'chiradi va
    /// har bir o'quvchining balansiga effektiv summani QAYTARADI.
    ///
    /// <para>⚠️ <b>NEGA KERAK — DOIMIY SOXTA QARZ.</b> Har hisob yaratilganda balans effektiv
    /// miqdorda KAMAYADI (<c>TuitionService.AccrueOne</c>), ya'ni qator va balans juftlik. Guruhni
    /// o'chirish esa qatorlarni <c>ExecuteDeleteAsync</c> bilan olib tashlar, balansga esa
    /// TEGMASDI. Natija: sertifikat bilan yopilgan guruhning har bir o'quvchisi (ular
    /// <c>IsActive=false</c>, ya'ni "faol a'zo bor" himoyasi ham o'tkazib yuboradi) balansida
    /// masalan −180 000 bilan qolar va uni TUSHUNTIRADIGAN birorta qator qolmasdi: hisobotda ham,
    /// to'lov oynasida ham "qarz bor, lekin qaysi oy uchun ekani noma'lum".</para>
    ///
    /// <para>⚠️ <c>Locked</c> (qo'lda tahrirlangan) qatorlar ham o'chiriladi — guruhning O'ZI
    /// yo'qolyapti, ya'ni ularni qoldirib bo'lmaydi (yetim qator). Muzlatishdagi
    /// <c>TuitionService.PurgeChargesAfterMonthAsync</c> dan farqi shu va u ATAYIN: u yerda guruh
    /// qoladi, bu yerda esa yo'q.</para>
    ///
    /// <para>SaveChanges QILINMAYDI — chaqiruvchi (tranzaksiya ichida) saqlaydi.</para>
    /// </summary>
    public static async Task<GroupChargeCredit> CreditAndDropGroupChargesAsync(
        IAppDbContext db, string groupId)
    {
        var rows = await db.MonthlyCharges.Where(c => c.GroupId == groupId).ToListAsync();
        if (rows.Count == 0) return new GroupChargeCredit(0, 0, 0m);

        var studentIds = rows.Select(r => r.StudentId).Distinct().ToList();
        var students = (await db.Students.Where(s => studentIds.Contains(s.Id)).ToListAsync())
            .ToDictionary(s => s.Id);

        var credited = 0m;
        var touched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            // O'quvchining o'zi allaqachon o'chirilgan bo'lsa (yetim hisob) — qaytaradigan balans
            // yo'q, lekin qator baribir o'chadi.
            if (students.TryGetValue(row.StudentId, out var s))
            {
                var effective = Math.Max(0m, row.Amount - row.Discount);
                s.Balance += effective;
                credited += effective;
                touched.Add(s.Id);
            }
            db.MonthlyCharges.Remove(row);
        }
        return new GroupChargeCredit(rows.Count, touched.Count, credited);
    }
}
