using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// LID QABULI — tashqaridan (ommaviy forma, daraja testi) kelgan murojaatni CRM'ga qo'shishning
/// UMUMIY qismi.
///
/// <para>Eng muhimi — <b>dublikat lid yaratmaslik</b>: bir odam Instagram formasini to'ldirib,
/// keyin daraja testini ham ishlashi mumkin. Har safar yangi lid ochilsa, Kanban bir xil odam bilan
/// to'lib ketardi va menejer kim bilan bog'langanini bilmasdi. Shuning uchun telefon bo'yicha
/// MAVJUD lid izlanadi va natija o'shaning tagiga tushadi.</para>
/// </summary>
public static class LeadIntake
{
    /// <summary>
    /// Telefon bo'yicha MAVJUD lidni topadi (topilmasa null — chaqiruvchi yangi lid ochadi).
    ///
    /// <para>Solishtirish <b>oxirgi 9 raqam</b> bo'yicha (<see cref="PhoneUtil.Key"/>): bazada
    /// telefonlar xilma-xil formatda (`+998-90-…`, `998…`, xom) saqlangani uchun xom ustunni
    /// taqqoslab bo'lmaydi. Shuning uchun kalit <see cref="Lead.PhoneKey"/> ustunida ALOHIDA
    /// saqlanadi (indekslangan; qiymatni <c>AppDbContext.SaveChanges</c> o'zi yozadi) va qidiruv
    /// BITTA SQL so'rovi bo'ladi.</para>
    ///
    /// <para>⚠️ Ilgari bu yerda butun <c>Leads</c> jadvali xotiraga o'qilar va kalit har safar
    /// qayta hisoblanardi — ommaviy forma/daraja testi anonim endpointlari bo'lgani uchun bu
    /// tashqaridan chaqiriladigan og'irlik edi. Bir nechta mos lid bo'lsa — ENG BIRINCHI
    /// yaratilgani (asosiy yozuv) qaytadi, ya'ni natija o'zgarmadi.</para>
    /// </summary>
    public static async Task<Lead?> FindByPhoneAsync(IAppDbContext db, string? rawPhone, CancellationToken ct = default)
    {
        var phoneKey = PhoneUtil.Key(rawPhone);
        // 7 dan qisqa "kalit" — telefon emas (yoki chala kiritilgan): tasodifan begona lidga
        // biriktirib yubormaslik uchun umuman qidirilmaydi.
        if (phoneKey.Length < 7) return null;

        return await db.Leads
            .Where(l => l.PhoneKey == phoneKey)
            .OrderBy(l => l.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Yangi lid tushadigan BIRINCHI bosqich (Order bo'yicha). Bosqich yo'q bo'lsa "".</summary>
    public static async Task<string> FirstStageIdAsync(IAppDbContext db, CancellationToken ct = default) =>
        await db.LeadStages.OrderBy(s => s.Order).Select(s => s.Id).FirstOrDefaultAsync(ct) ?? "";
}
