using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;

namespace WunderkindLC.Application.Services;

/// <summary>
/// "Bu `/uploads/...` fayli hali BIROR JOYDA ishlatilyaptimi?" — fayl o'chirishdan OLDINGI tekshiruv.
///
/// <para><b>Nega:</b> `/uploads` — YAGONA tekis papka, manzillar esa ko'p joyda klientdan keladi
/// (shartnoma PDF'i, o'quvchi hujjati, test savollari). Bir yozuv BEGONA faylga (dars PDF'i, boshqa
/// shartnoma, o'qituvchi surati) ishora qilgan bo'lsa, o'sha yozuv o'chirilganda begona fayl diskdan
/// o'chib ketardi. Shuning uchun o'chirishdan oldin: (1) nom server naqshida
/// (<see cref="UploadUrlRules"/>) VA (2) boshqa hech bir yozuv unga ishora qilmaydi.</para>
///
/// <para>⚠️ Chaqiruvchi o'chirilayotgan yozuvni HISOBGA OLMASLIGI kerak: yoki tekshiruvni
/// <c>SaveChanges</c> dan KEYIN qiladi (yozuv bazada yo'q), yoki o'quvchi uchun
/// <paramref name="exceptStudentId"/> beradi.</para>
/// </summary>
public static class UploadReferences
{
    /// <summary>
    /// <paramref name="urls"/> dan qaysilari hali ishlatilayotganini qaytaradi (o'chirish MUMKIN EMAS).
    /// Yangi entity'da `/uploads` manzili saqlansa — shu ro'yxatga ham qo'shing.
    /// </summary>
    public static async Task<HashSet<string>> InUseAsync(
        IAppDbContext db, IEnumerable<string?> urls, string? exceptStudentId = null, CancellationToken ct = default)
    {
        var list = urls.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u!).Distinct().ToList();
        var used = new HashSet<string>(StringComparer.Ordinal);
        if (list.Count == 0) return used;

        void Add(IEnumerable<string?> found)
        {
            foreach (var u in found) if (u is not null && list.Contains(u)) used.Add(u);
        }

        foreach (var x in await db.Students.AsNoTracking()
                     .Where(s => exceptStudentId == null || s.Id != exceptStudentId)
                     .Where(s => list.Contains(s.BirthCertificateUrl!) || list.Contains(s.ParentPassportUrl!))
                     .Select(s => new { s.BirthCertificateUrl, s.ParentPassportUrl }).ToListAsync(ct))
            Add([x.BirthCertificateUrl, x.ParentPassportUrl]);
        Add(await db.Teachers.AsNoTracking().Where(t => list.Contains(t.PhotoUrl!)).Select(t => t.PhotoUrl).ToListAsync(ct));
        Add(await db.Users.AsNoTracking().Where(u => list.Contains(u.AvatarUrl!)).Select(u => u.AvatarUrl).ToListAsync(ct));
        foreach (var x in await db.Contracts.AsNoTracking()
                     .Where(c => list.Contains(c.PdfUrl) || list.Contains(c.DocxUrl))
                     .Select(c => new { c.PdfUrl, c.DocxUrl }).ToListAsync(ct))
            Add([x.PdfUrl, x.DocxUrl]);
        Add(await db.ContractTemplates.AsNoTracking().Where(t => list.Contains(t.FileUrl)).Select(t => t.FileUrl).ToListAsync(ct));
        Add(await db.TestResults.AsNoTracking().Where(t => list.Contains(t.PdfUrl)).Select(t => t.PdfUrl).ToListAsync(ct));
        Add(await db.TestCertificateTemplates.AsNoTracking().Where(t => list.Contains(t.FileUrl)).Select(t => t.FileUrl).ToListAsync(ct));
        foreach (var x in await db.CourseItems.AsNoTracking()
                     .Where(i => list.Contains(i.PdfUrl) || list.Contains(i.AudioUrl) || list.Contains(i.VideoUrl))
                     .Select(i => new { i.PdfUrl, i.AudioUrl, i.VideoUrl }).ToListAsync(ct))
            Add([x.PdfUrl, x.AudioUrl, x.VideoUrl]);
        Add(await db.Books.AsNoTracking().Where(b => list.Contains(b.CoverUrl)).Select(b => b.CoverUrl).ToListAsync(ct));
        Add(await db.BookOrders.AsNoTracking().Where(o => list.Contains(o.ReceiptUrl)).Select(o => o.ReceiptUrl).ToListAsync(ct));
        Add(await db.JobApplications.AsNoTracking().Where(a => list.Contains(a.CvUrl)).Select(a => a.CvUrl).ToListAsync(ct));
        Add(await db.Feedbacks.AsNoTracking().Where(f => list.Contains(f.ImageUrl!)).Select(f => f.ImageUrl).ToListAsync(ct));
        Add(await db.AiChecks.AsNoTracking().Where(a => list.Contains(a.AudioUrl)).Select(a => a.AudioUrl).ToListAsync(ct));
        Add(await db.LandingTeachers.AsNoTracking().Where(l => list.Contains(l.PhotoUrl)).Select(l => l.PhotoUrl).ToListAsync(ct));
        Add(await db.LandingCertificates.AsNoTracking().Where(l => list.Contains(l.ImageUrl)).Select(l => l.ImageUrl).ToListAsync(ct));
        Add(await db.LandingTestimonials.AsNoTracking().Where(l => list.Contains(l.AvatarUrl)).Select(l => l.AvatarUrl).ToListAsync(ct));
        Add(await db.CenterMeta.AsNoTracking().Where(m => list.Contains(m.LogoUrl)).Select(m => m.LogoUrl).ToListAsync(ct));
        Add(await db.CareerAbout.AsNoTracking().Where(m => list.Contains(m.LogoUrl)).Select(m => m.LogoUrl).ToListAsync(ct));
        return used;
    }
}
