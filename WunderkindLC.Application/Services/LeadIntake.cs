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
    // ==================== Vaqtinchalik (placeholder) ismlar ====================
    //
    // Ismsiz kelgan lidga kod O'ZI qo'yadigan nomlar. Keyin haqiqiy ism kelsa (forma, test, AI
    // xulosasi, reklama formasi) ular ALMASHTIRILADI — `IsPlaceholderName`. Yangi placeholder
    // qo'shsangiz uni SHU YERDA e'lon qiling, aks holda u abadiy almashmay qoladi (IG-4 xatosi:
    // ilgari faqat "Noma'lum…" tanilardi, "ali_v (Instagram)" esa hech qachon almashmasdi).

    /// <summary>Instagram suhbati — username ham, ism ham yo'q.</summary>
    public const string InstagramAnonymousName = "Instagram mijozi";
    /// <summary>Instagram suhbati — faqat username bor: <c>"{username} (Instagram)"</c>.</summary>
    public const string InstagramUsernameSuffix = " (Instagram)";
    /// <summary>Meta Lead Ads — formada ism yo'q.</summary>
    public const string AdAnonymousName = "Reklama lidi (ismsiz)";
    /// <summary>Daraja testi — ism kiritilmagan.</summary>
    public const string LevelTestAnonymousName = "Noma'lum (daraja testi)";

    /// <summary>Instagram username'idan vaqtinchalik ism (`@` olib tashlanadi — Telegram mentioni bo'lmasin).</summary>
    public static string InstagramUsernameName(string username) =>
        $"{(username ?? "").Trim().TrimStart('@')}{InstagramUsernameSuffix}";

    /// <summary>
    /// Ism VAQTINCHALIK (kod qo'ygan) yoki bo'shmi — ya'ni haqiqiy ism kelsa ALMASHTIRISH mumkinmi.
    /// Menejer qo'lda yozgan har qanday boshqa ism tegilmaydi.
    /// </summary>
    public static bool IsPlaceholderName(string? fullName)
    {
        var n = (fullName ?? "").Trim();
        if (n.Length == 0) return true;
        // "Noma'lum…" — apostrofning har xil ko'rinishi bilan (klaviaturaga qarab).
        var norm = n.Replace('ʻ', '\'').Replace('ʼ', '\'').Replace('’', '\'').Replace('‘', '\'').Replace('`', '\'');
        if (norm.StartsWith("Noma'lum", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(n, InstagramAnonymousName, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(n, AdAnonymousName, StringComparison.OrdinalIgnoreCase)) return true;
        return n.EndsWith(InstagramUsernameSuffix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Telefon bo'yicha MAVJUD OCHIQ lidni topadi (topilmasa null — chaqiruvchi yangi lid ochadi).
    ///
    /// <para>Solishtirish <b>oxirgi 9 raqam</b> bo'yicha (<see cref="PhoneUtil.Key"/>): bazada
    /// telefonlar xilma-xil formatda (`+998-90-…`, `998…`, xom) saqlangani uchun xom ustunni
    /// taqqoslab bo'lmaydi. Shuning uchun kalit <see cref="Lead.PhoneKey"/> ustunida ALOHIDA
    /// saqlanadi (indekslangan; qiymatni <c>AppDbContext.SaveChanges</c> o'zi yozadi) va qidiruv
    /// BITTA SQL so'rovi bo'ladi.</para>
    ///
    /// <para>⚠️ <b>O'QUVCHIGA AYLANTIRILGAN lid QAYTARILMAYDI</b> (CRM-1). Ilgari shu telefon bilan
    /// kelgan har qanday yangi murojaat (aka-uka, qayta ariza) eski — allaqachon yopilgan — lidga
    /// "takroriy" bo'lib yopishardi: ochiq lid ochilmas, <c>lead_new</c> SMS ketmas, Meta CAPI esa
    /// darhol yolg'on "sifatli lid" hodisasini yuborardi. Endi bunday murojaat YANGI lid ochadi,
    /// unga esa <see cref="MarkFormerStudentAsync"/> izoh qo'shadi.</para>
    ///
    /// <para>⚠️ Ilgari bu yerda butun <c>Leads</c> jadvali xotiraga o'qilar va kalit har safar
    /// qayta hisoblanardi — ommaviy forma/daraja testi anonim endpointlari bo'lgani uchun bu
    /// tashqaridan chaqiriladigan og'irlik edi. Bir nechta mos lid bo'lsa — ENG BIRINCHI
    /// yaratilgani (asosiy yozuv) qaytadi.</para>
    /// </summary>
    public static async Task<Lead?> FindByPhoneAsync(IAppDbContext db, string? rawPhone, CancellationToken ct = default)
    {
        var phoneKey = PhoneUtil.Key(rawPhone);
        // 7 dan qisqa "kalit" — telefon emas (yoki chala kiritilgan): tasodifan begona lidga
        // biriktirib yubormaslik uchun umuman qidirilmaydi.
        if (phoneKey.Length < 7) return null;

        return await db.Leads
            .Where(l => l.PhoneKey == phoneKey && l.ConvertedStudentId == null)
            .OrderBy(l => l.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Shu telefon bilan AVVAL o'quvchiga aylantirilgan lid bo'lsa — "Avval o'quvchi bo'lgan: &lt;ism&gt;"
    /// matni (o'quvchining joriy ismi, o'quvchi o'chirilgan bo'lsa lidning ismi); bo'lmasa <c>""</c>.
    /// </summary>
    public static async Task<string> FormerStudentNoteAsync(IAppDbContext db, string? rawPhone, CancellationToken ct = default)
    {
        var phoneKey = PhoneUtil.Key(rawPhone);
        if (phoneKey.Length < 7) return "";
        var prior = await db.Leads.AsNoTracking()
            .Where(l => l.PhoneKey == phoneKey && l.ConvertedStudentId != null)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new { l.FullName, l.ConvertedStudentId })
            .FirstOrDefaultAsync(ct);
        if (prior is null) return "";
        var studentName = await db.Students.AsNoTracking()
            .Where(s => s.Id == prior.ConvertedStudentId)
            .Select(s => s.FullName)
            .FirstOrDefaultAsync(ct);
        var name = !string.IsNullOrWhiteSpace(studentName) ? studentName.Trim() : (prior.FullName ?? "").Trim();
        return $"Avval o'quvchi bo'lgan: {name}";
    }

    /// <summary>
    /// YANGI lidga (hali <c>db.Leads.Add</c> qilinmagan yoki qilingan — farqi yo'q) "Avval o'quvchi
    /// bo'lgan" izohini qo'shadi, agar shu telefon bilan aylantirilgan lid bo'lsa. Menejer kartadan
    /// darhol ko'radi: bu yangi odam emas — aka-uka yoki qaytgan o'quvchi. SaveChanges QILMAYDI.
    /// </summary>
    public static async Task MarkFormerStudentAsync(IAppDbContext db, Lead lead, string? rawPhone, CancellationToken ct = default)
    {
        var note = await FormerStudentNoteAsync(db, rawPhone, ct);
        if (note.Length == 0) return;
        lead.Note = string.IsNullOrWhiteSpace(lead.Note) ? note : $"{lead.Note!.TrimEnd()}\n{note}";
    }

    /// <summary>Yangi lid tushadigan BIRINCHI bosqich (Order bo'yicha). Bosqich yo'q bo'lsa "".</summary>
    public static async Task<string> FirstStageIdAsync(IAppDbContext db, CancellationToken ct = default) =>
        await db.LeadStages.OrderBy(s => s.Order).Select(s => s.Id).FirstOrDefaultAsync(ct) ?? "";

    /// <summary>
    /// YANGI lidga «Yangi lid kelganda» (<c>lead_new</c>) avto-xabarini yuboradi — telefoni bo'lsa.
    /// Ommaviy forma, landing, Instagram va Meta reklama lidlari AYNAN shu yerdan chaqiradi
    /// (qoida bitta: takroriy murojaatga tanishuv SMS'i qayta ketmaydi).
    /// Xato JIM yutiladi (<see cref="AutoMessageService.DispatchLeadAsync"/> siyosati).
    /// </summary>
    public static async Task DispatchLeadNewAsync(
        IAppDbContext db, AutoMessageService? autoMsg, Lead lead, bool isNewLead, CancellationToken ct = default)
    {
        if (autoMsg is null || !isNewLead) return;
        if (string.IsNullOrWhiteSpace(lead.Phone) && string.IsNullOrWhiteSpace(lead.FatherPhone)
            && string.IsNullOrWhiteSpace(lead.MotherPhone)) return;
        await autoMsg.DispatchLeadAsync(db, AutoMessageTriggers.LeadNew, lead, ct: ct);
    }
}
