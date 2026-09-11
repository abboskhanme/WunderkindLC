using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Statik fayl provayderining ustki qatlami: berilgan papka(lar) ichidagi fayllarni
/// <b>"yo'q"</b> deb ko'rsatadi, qolganini o'zgarishsiz uzatadi.
///
/// <para><b>Nega kerak?</b> <c>/uploads</c> — ochiq statik papka: manzilni bilgan har kim
/// login'siz oladi. Sertifikatlar esa shaxsiy ma'lumot (F.I.Sh, ball, foiz, o'quvchining SURATI)
/// va ular <c>uploads/certificates</c> ga yoziladi. API tomonda egalik tekshiriladi
/// (<c>OwnsGroup</c>, <c>AdminPerm</c>), lekin faylning o'zi shu tekshiruvni CHETLAB o'tib
/// olinardi: manzil bir marta oshkor bo'lsa (brauzer tarixi, ulashilgan havola) u abadiy
/// ishlayverardi — o'qituvchi guruhdan chiqarilsa ham.</para>
///
/// <para><b>Nega fayllar ko'chirilmaydi?</b> <c>uploads</c> — docker volume va tungi zaxiraga
/// kiradigan YAGONA papka (<c>docker-compose.yml</c>: <c>uploads:/app/uploads</c>, backup
/// <c>tar czf ... -C /data uploads</c>). Fayllarni undan chiqarish zaxiradan tushib qolish
/// degani bo'lardi. Shuning uchun fayllar JOYIDA qoladi — faqat ular statik tarzda
/// BERILMAYDI. Yuklab olish avvalgidek avtorizatsiyalangan endpointlar orqali ishlaydi
/// (<c>test-results/certificates/{id}/download</c>, <c>students/{id}/certificates/{id}/download</c>,
/// <c>student/certificates/{id}/download</c>) — ular faylni diskdan o'zi o'qiydi.</para>
///
/// <para><b>Nega URL emas, FIZIK yo'l tekshiriladi?</b> "Manzil <c>/uploads/certificates</c> bilan
/// boshlansa rad et" degan tekshiruvni <c>..</c> segmentlari yoki kodlash bilan chetlab o'tishga
/// urinish mumkin. Bu yerda esa statik middleware fayl yo'lini O'ZI hal qilgandan KEYIN,
/// natijaviy fizik yo'l yopiq papka ichidami — shu tekshiriladi. Ya'ni manzil qanday yozilganidan
/// qat'i nazar, yopiq papkadagi fayl hech qachon berilmaydi.</para>
/// </summary>
public sealed class PrivateFolderFileProvider(
    IFileProvider inner, ILogger logger, params string[] privateFolders) : IFileProvider
{
    /// <summary>Yopiq papkalarning to'liq (normallashtirilgan) yo'llari.</summary>
    private readonly string[] _blocked = privateFolders
        .Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)) + Path.DirectorySeparatorChar)
        .ToArray();

    /// <summary>Fizik yo'l yopiq papkaning O'ZIMI yoki uning ichidami.</summary>
    private bool IsBlocked(string? physicalPath)
    {
        if (string.IsNullOrEmpty(physicalPath)) return false;
        // Solishtirishdan oldin ikkala tomonga ham ajratuvchi qo'shiladi. Bu ikki narsani beradi:
        //   • papkaning O'ZI ham bloklanadi ("…/certificates" → "…/certificates/");
        //   • qo'shni papka ADASHIB bloklanmaydi ("…/certificates-eski" prefiksga mos kelmaydi).
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(physicalPath))
                   + Path.DirectorySeparatorChar;
        // Fayl tizimi registrga sezgirmi — platformaga bog'liq. Linux (prod) sezgir, lekin
        // rad etishda qattiqroq bo'lgan yaxshi: registrni e'tiborsiz solishtiramiz.
        return _blocked.Any(b => full.StartsWith(b, StringComparison.OrdinalIgnoreCase));
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        var info = inner.GetFileInfo(subpath);
        if (!info.Exists || !IsBlocked(info.PhysicalPath)) return info;

        // Log: bu manzilga kim murojaat qilayotganini bilish uchun (mobil ilova yoki eski havola
        // bo'lsa — shu yerdan ko'rinadi). Fayl nomi maxfiy emas, mazmuni maxfiy.
        logger.LogWarning("Statik yo'l bilan MAXFIY faylga urinish rad etildi: {Subpath}", subpath);
        return new NotFoundFileInfo(subpath);
    }

    /// <summary>Papka ro'yxati — yopiq papkaning o'zi va ichidagilar ko'rsatilmaydi.</summary>
    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        var contents = inner.GetDirectoryContents(subpath);
        if (!contents.Exists) return contents;
        return contents.Any(f => IsBlocked(f.PhysicalPath))
            ? new FilteredDirectoryContents(contents.Where(f => !IsBlocked(f.PhysicalPath)))
            : contents;
    }

    public IChangeToken Watch(string filter) => inner.Watch(filter);

    private sealed class FilteredDirectoryContents(IEnumerable<IFileInfo> files) : IDirectoryContents
    {
        public bool Exists => true;
        public IEnumerator<IFileInfo> GetEnumerator() => files.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
