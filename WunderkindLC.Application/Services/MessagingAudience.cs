using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;

namespace WunderkindLC.Application.Services;

/// <summary>
/// OMMAVIY / AVTOMATIK xabar auditoriyasi filtri.
/// <para>Guruhi YOPILGAN (arxivlangan) yoki TUGATILGAN o'quvchiga tizim o'zi xabar yubormasligi kerak:
/// ular endi o'qimaydi, lekin bazada qoladi (tarix, qarzdorlik, sertifikat). Aks holda qarzdorlik
/// eslatmasi, tug'ilgan kun tabrigi va ommaviy e'lonlar allaqachon o'qishni tugatgan oilalarga
/// yuborilaverardi.</para>
/// <para>Qoida: o'quvchi a'zoligi BOR-u, biror ham <b>TIRIK</b> a'zoligi qolmagan bo'lsa — chetlab
/// o'tiladi. Tirik a'zolik = <c>StudentGroup.IsActive</c> VA guruh ARXIVLANMAGAN. Ya'ni:
/// muzlatilgan bo'lsa ham guruhi faol bo'lsa — xabar boradi (ta'tildagi o'quvchi tizimdan uzilmaydi);
/// guruh yopilsa (barchasi muzlatilib arxivga) yoki tugatilsa (a'zolik yopiladi) — bormaydi.
/// A'zoligi UMUMAN yo'q (eski, faqat <c>ClassName</c> bilan yozilgan) o'quvchilar tegilmaydi —
/// ular uchun eski xulq saqlanadi.</para>
/// <para>DIQQAT: bu filtr FAQAT tizim o'zi boshlaydigan yuborishlarga (eslatma/tabrik/ommaviy e'lon)
/// tegishli. Hodisaga javob bo'lgan xabarlar — masalan "to'lov qabul qilindi" — YUBORILAVERADI:
/// yopilgan guruh qarzini to'lagan ota-ona tasdiqni olishi kerak.</para>
/// </summary>
public static class MessagingAudience
{
    /// <summary>
    /// Avtomatik xabar YUBORILMAYDIGAN o'quvchilar id'lari (guruhi yopilgan/tugatilgan yoki hamma
    /// guruhidan chiqarilgan). Chaqiruvchi ro'yxatini shu to'plam bo'yicha filtrlaydi.
    /// </summary>
    public static async Task<HashSet<string>> ClosedGroupStudentIdsAsync(
        IAppDbContext db, CancellationToken ct = default)
    {
        var rows = await (from sg in db.StudentGroups
                          join c in db.Classes on sg.GroupId equals c.Id
                          select new { sg.StudentId, Live = sg.IsActive && !c.IsArchived })
                         .ToListAsync(ct);
        return rows
            .GroupBy(r => r.StudentId)
            .Where(g => !g.Any(r => r.Live))
            .Select(g => g.Key)
            .ToHashSet();
    }
}
