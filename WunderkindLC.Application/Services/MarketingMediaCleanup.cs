using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;

namespace WunderkindLC.Application.Services;

/// <summary>
/// KONTENT MODULINING <b>OCHIQ</b> MEDIA PAPKASINI TOZALASH — yetim fayllarni yig'ib olish
/// (<c>uploads/marketing-public/</c>, qarang <see cref="MarketingPublicMedia"/>).
///
/// <para><b>Muammo:</b> papkaga yozamiz, lekin hech qachon o'chirmasdik. Fayl uch xil yo'l bilan
/// yetim qolardi: post media'si almashtirilsa (eskisi diskda qolardi), post o'chirilsa va
/// foydalanuvchi faylni yuklab, postni SAQLAMASDAN chiqib ketsa. Papka esa <b>OCHIQ</b>
/// (<c>uploads-security.md</c> dagi yagona istisno) va tungi zaxiraga kiradi — ya'ni yetim fayl
/// ham abadiy internetda turadi, ham arxivni shishiradi.</para>
///
/// <para><b>🔴 ENG MUHIM DETAL — YOSH SHARTI</b> (<see cref="MinAgeHours"/>). Fayl yuklanadi,
/// post esa keyinroq (yoki umuman) saqlanadi: o'sha oraliqda fayl HECH QAYSI postda ko'rinmaydi,
/// ya'ni "yetim" bo'lib turadi. Yosh shartisiz tozalash uni <b>modal ochiq turganda</b> o'chirib
/// yuborardi va post saqlanishi bilan Meta uni yuklab ololmay <c>2207052</c> («Media yuklab
/// bo'lmadi») bilan yiqilardi. Shuning uchun tozalash faqat <b>bir kundan eski</b> fayllarga
/// tegadi — bu vaqt ichida fayl yo postga bog'lanadi, yo haqiqatan tashlab ketilgan bo'ladi.</para>
///
/// <para><b>⚠️ HAMMA HOLAT «ishlatilmoqda» hisoblanadi</b> — <c>scheduled</c>, <c>processing</c>,
/// <c>published</c>, <c>failed</c>, <c>cancelled</c>. Joylangan postniki ham saqlanadi: CRM
/// yozuvi tarix bo'lib qoladi va admin nima joylanganini qayta ko'ra olishi kerak.</para>
///
/// <para><b>Ikki ishlatilish usuli:</b></para>
/// <list type="number">
///   <item><see cref="SweepAsync"/> — <b>kuniga bir marta</b> fon xizmatidan chaqiriladi
///     (<c>InstagramWorkerService</c> dagi "tozalash" vazifasi bilan bir xil chastota).
///     Bu — «tarmoq» (safety net): saqlanmagan yuklamalarni va o'tmishdan qolgan yetimlarni
///     olib tashlaydi.</item>
///   <item><see cref="RemoveUnusedAsync"/> — <b>darhol</b>: post o'chirilganda yoki media
///     almashtirilganda controller chaqiradi, ya'ni fayl bir kun kutmasdan yo'qoladi.</item>
/// </list>
///
/// <para>Sof funksiyalar (<see cref="UsedNames"/>, <see cref="IsSweepable"/>) ATAYIN
/// static va I/O'siz — qoida aynan shular orqali testlanadi
/// (<c>MarketingMediaCleanupTests</c>).</para>
///
/// <para>DI: <c>builder.Services.AddScoped&lt;MarketingMediaCleanup&gt;();</c></para>
/// </summary>
public sealed class MarketingMediaCleanup
{
    /// <summary>
    /// Fayl shuncha soatdan eski bo'lsagina yetim deb o'chiriladi.
    ///
    /// <para>🔴 Bu shart MAJBURIY (sinf izohidagi sabab): yangi yuklangan fayl hali hech qaysi
    /// postda bo'lmaydi. 24 soat — foydalanuvchi bir kun ichida postni saqlab ulguradi degan
    /// ehtiyotkor taxmin; kichraytirilsa "saqlanmagan chernovik" xavfi ortadi, kattalashtirilsa
    /// faqat tozalash kechikadi (zarari yo'q).</para>
    /// </summary>
    public const int MinAgeHours = 24;

    /// <summary>Saqlangan nomdagi hex qismining uzunligi (<c>{Guid:N}</c>) — buzuq JSON'ni
    /// xom skanerlashda ishlatiladi.</summary>
    private const int NameHexLength = 32;

    private readonly IAppDbContext _db;
    private readonly string _dir;
    private readonly ILogger _log;

    /// <summary>DI uchun (fon xizmati): papka <c>ContentRoot/uploads/marketing-public</c>.</summary>
    public MarketingMediaCleanup(
        IAppDbContext db, IHostEnvironment env, ILogger<MarketingMediaCleanup> logger)
        : this(db,
            Path.Combine(env.ContentRootPath, "uploads", MarketingPublicMedia.FolderName),
            logger)
    {
    }

    /// <summary>
    /// Papka TO'G'RIDAN-TO'G'RI berilganda: controller o'zining <c>PublicMediaDir()</c> sini
    /// beradi (yo'l bitta joyda hisoblansin), testlar esa vaqtinchalik papkani.
    /// </summary>
    public MarketingMediaCleanup(IAppDbContext db, string mediaDir, ILogger logger)
    {
        _db = db;
        _dir = mediaDir;
        _log = logger;
    }

    // =============================================================================================
    //  SOF QOIDALAR (I/O yo'q — testlar aynan shularni qoplaydi)
    // =============================================================================================

    /// <summary>
    /// Postlarning <c>MediaJson</c> va <c>OptionsJson</c> satrlaridan <b>ishlatilayotgan</b>
    /// fayl nomlari to'plamini yig'adi (nom — <see cref="MarketingPublicMedia.SafeStoredName"/>
    /// dan o'tgan ko'rinishda, ya'ni kichik harfda).
    ///
    /// <para>JSON <b>ichidagi HAR QANDAY satr qiymati</b> ko'riladi (rekursiv), faqat
    /// <c>url</c> emas: shu sababdan <c>coverUrl</c> (Reels muqovasi) va kelajakda qo'shiladigan
    /// yangi maydonlar ham o'z-o'zidan hisobga olinadi. Nomi/manzili naqshga tushmagan qiymat
    /// (tashqi CDN havolasi, matn) jimgina tashlab yuboriladi.</para>
    ///
    /// <para>⚠️ <b>BUZUQ JSON — xavfli holat:</b> uni "media yo'q" deb talqin qilsak, ishlab
    /// turgan postning fayli yetim deb O'CHIRILARDI. Shuning uchun parser yiqilsa satr
    /// <b>xom</b> skanerlanadi (32 hex + kengaytma naqshi qidiriladi) — ortiqcha saqlab qolish
    /// noto'g'ri o'chirishdan yaxshi.</para>
    /// </summary>
    public static IReadOnlySet<string> UsedNames(
        IEnumerable<string?>? mediaJson, IEnumerable<string?>? optionsJson)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in mediaJson ?? Enumerable.Empty<string?>()) Collect(s, used);
        foreach (var s in optionsJson ?? Enumerable.Empty<string?>()) Collect(s, used);
        return used;
    }

    /// <summary>Bitta postning ikkala JSON'idan nomlar (qulaylik uchun).</summary>
    public static IReadOnlySet<string> NamesOf(string? mediaJson, string? optionsJson) =>
        UsedNames(new[] { mediaJson }, new[] { optionsJson });

    /// <summary>
    /// Shu faylni o'chirsa bo'ladimi.
    ///
    /// <para>Uchta shart va uchalasi ham "YO'Q" tomonga ishlaydi (shubha bo'lsa — saqlaymiz):</para>
    /// <list type="number">
    ///   <item>nom <see cref="MarketingPublicMedia.SafeStoredName"/> naqshiga tushmasa — bu
    ///     bizning oqimimiz yozgan fayl EMAS (kimdir qo'lda qo'ygan bo'lishi mumkin), tegilmaydi;</item>
    ///   <item>biror postda ishlatilayotgan bo'lsa — tegilmaydi;</item>
    ///   <item>yoshi <see cref="MinAgeHours"/> dan kichik bo'lsa — tegilmaydi (🔴 saqlanmagan
    ///     chernovikning fayli, sinf izohiga qarang).</item>
    /// </list>
    /// </summary>
    /// <param name="fileName">Diskdagi fayl nomi.</param>
    /// <param name="lastWriteUtc">Faylning oxirgi yozilish vaqti.</param>
    /// <param name="nowUtc">Hozirgi vaqt — <paramref name="lastWriteUtc"/> bilan AYNAN bir xil
    /// mintaqada bo'lishi shart (ikkisi ayiriladi, ko'rsatilmaydi).</param>
    /// <param name="used">Ishlatilayotgan nomlar (<see cref="UsedNames"/>).</param>
    public static bool IsSweepable(
        string fileName, DateTime lastWriteUtc, DateTime nowUtc, IReadOnlySet<string> used)
    {
        var name = MarketingPublicMedia.SafeStoredName(fileName);
        if (name is null) return false;
        if (used.Contains(name)) return false;
        return nowUtc - lastWriteUtc >= TimeSpan.FromHours(MinAgeHours);
    }

    // =============================================================================================
    //  1) KUNLIK TOZALASH
    // =============================================================================================

    /// <summary>
    /// Papkani bir marta ko'rib chiqadi va yetim fayllarni o'chiradi.
    ///
    /// <para>Fon xizmatidan <b>kuniga bir marta</b> chaqirish uchun mo'ljallangan: ish og'ir
    /// emas (bitta <c>Directory.GetFiles</c> + bitta yengil so'rov), lekin tez-tez chaqirishning
    /// ma'nosi ham yo'q — yosh sharti tufayli bir sutkadan yosh fayl baribir tegilmaydi.</para>
    ///
    /// <para>⚠️ Bitta faylni o'chirib bo'lmasa (band, ruxsat yo'q) <b>butun tsikl to'xtamaydi</b>:
    /// u sanoqda qoladi va ogohlantirish logi yoziladi. Papka umuman yo'q bo'lsa — bu xato EMAS
    /// (modul hali ishlatilmagan): <c>(0, 0, "")</c>.</para>
    /// </summary>
    /// <returns><c>Deleted</c> — o'chirilgan fayllar soni; <c>Kept</c> — qoldirilganlar;
    /// <c>Error</c> — butun tsiklni to'xtatgan sabab (bo'sh bo'lsa muammo yo'q).</returns>
    public async Task<(int Deleted, int Kept, string Error)> SweepAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_dir)) return (0, 0, "");

        string[] files;
        try
        {
            files = Directory.GetFiles(_dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Ochiq media papkasini o'qib bo'lmadi: {Dir}", _dir);
            return (0, 0, "Ochiq media papkasini o'qib bo'lmadi.");
        }

        if (files.Length == 0) return (0, 0, "");

        var used = await UsedNamesFromDbAsync(ct);

        // ⚠️ `DateTime.Now` TAQIQ. Bu yerda `AppClock` ham ishlatilmaydi: taqqoslanayotgani
        // ko'rsatiladigan vaqt emas, fayl tizimi bergan UTC belgi bilan hisoblanadigan ORALIQ
        // (mintaqa aralashtirilsa yosh 5 soatga siljib ketardi).
        var nowUtc = DateTime.UtcNow;

        int deleted = 0, kept = 0, foreign = 0, failed = 0;

        foreach (var path in files)
        {
            if (ct.IsCancellationRequested) break;

            var name = Path.GetFileName(path);

            // Begona nom — bizning oqimimiz yozmagan fayl. O'CHIRILMAYDI, lekin jimgina ham
            // qolmaydi: papkaga qayerdan begona fayl tushayotgani ko'rinib tursin.
            if (MarketingPublicMedia.SafeStoredName(name) is null)
            {
                foreign++;
                kept++;
                continue;
            }

            DateTime lastWriteUtc;
            try
            {
                lastWriteUtc = File.GetLastWriteTimeUtc(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                kept++;
                continue;
            }

            if (!IsSweepable(name, lastWriteUtc, nowUtc, used))
            {
                kept++;
                continue;
            }

            try
            {
                File.Delete(path);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Bitta fayl butun tsiklni to'xtatmaydi — keyingi kuni yana urinib ko'riladi.
                kept++;
                failed++;
                _log.LogWarning(ex, "Yetim media faylni o'chirib bo'lmadi: {Name}", name);
            }
        }

        if (foreign > 0)
            _log.LogWarning(
                "Ochiq media papkasida {Count} ta BEGONA nomli fayl bor — o'chirilmadi: {Dir}",
                foreign, _dir);

        if (deleted > 0 || failed > 0)
            _log.LogInformation(
                "Ochiq media tozalandi: {Deleted} o'chirildi, {Kept} qoldi ({Failed} o'chirib bo'lmadi)",
                deleted, kept, failed);

        return (deleted, kept, "");
    }

    // =============================================================================================
    //  2) DARHOL O'CHIRISH (post o'chirilganda / media almashtirilganda)
    // =============================================================================================

    /// <summary>
    /// Berilgan fayllarni <b>darhol</b> o'chiradi — lekin faqat ular <b>hech qaysi postda
    /// ishlatilmayotgan</b> bo'lsa.
    ///
    /// <para>🔴 <b>Nega tekshiruv shart:</b> bitta manzil IKKI postda bo'lishi mumkin
    /// (foydalanuvchi rejani nusxalagan yoki bir rasmni ikki kunga qo'ygan). Tekshirmasdan
    /// o'chirish <b>ishlab turgan postni</b> buzardi — Meta faylni yuklab ololmay post
    /// <c>2207052</c> bilan yiqilardi.</para>
    ///
    /// <para>⚠️ <b>SAQLAGANDAN KEYIN chaqiriladi</b> (<c>SaveChangesAsync</c> dan so'ng):
    /// "ishlatilmoqda" ro'yxati bazaning JORIY holatidan olinadi, ya'ni o'chirilgan post allaqachon
    /// yo'q, tahrirlangan postda esa yangi media turadi. Shu sababdan bu yerda «bundan tashqari»
    /// istisnosi kerak emas — baza yagona haqiqat.</para>
    ///
    /// <para>⚠️ Bu <b>yordamchi</b> ish: istisno TASHQARIGA CHIQMAYDI va natija chaqiruvchining
    /// javobiga ta'sir qilmaydi — fayl o'chmagani uchun post o'chirish/tahrirlash
    /// "muvaffaqiyatsiz" bo'lib qolmasin. Yiqilgan holatda fayl baribir
    /// <see cref="SweepAsync"/> ga qoladi.</para>
    /// </summary>
    /// <param name="storedNames">Nomzod fayllar (nom yoki manzil — har biri qayta tekshiriladi).</param>
    /// <returns>O'chirilgan fayllar soni.</returns>
    public async Task<int> RemoveUnusedAsync(IEnumerable<string?>? storedNames, CancellationToken ct)
    {
        try
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in storedNames ?? Enumerable.Empty<string?>())
            {
                var name = MarketingPublicMedia.SafeStoredName(raw);
                if (name is not null) candidates.Add(name);
            }
            if (candidates.Count == 0) return 0;

            var used = await UsedNamesFromDbAsync(ct);

            var deleted = 0;
            foreach (var name in candidates)
            {
                if (used.Contains(name)) continue;

                // Ikkinchi qatlam — `InstagramController.DeleteContentMedia` dagi bilan bir xil:
                // sof funksiyada kutilmagan kamchilik chiqsa ham fayl tizimiga chiqib ketilmasin.
                var fullPath = Path.GetFullPath(Path.Combine(_dir, name));
                var root = Path.GetFullPath(_dir) + Path.DirectorySeparatorChar;
                if (!fullPath.StartsWith(root, StringComparison.Ordinal)) continue;

                try
                {
                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                        deleted++;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _log.LogWarning(ex, "Ishlatilmayotgan media faylni o'chirib bo'lmadi: {Name}", name);
                }
            }

            return deleted;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Ochiq media tozalashda kutilmagan xato (amal baribir bajarildi)");
            return 0;
        }
    }

    // =============================================================================================
    //  ICHKI YORDAMCHILAR
    // =============================================================================================

    /// <summary>
    /// Bazadagi BARCHA postlarning media nomlari.
    /// <para>⚠️ Holat bo'yicha filtr ATAYIN YO'Q: <c>cancelled</c> va <c>failed</c> postlar ham
    /// CRM'da ko'rinadi (admin ularni ochib media'sini ko'radi), <c>published</c> esa tarix.
    /// Filtr qo'yilsa ekranda sinuq rasm paydo bo'lardi.</para>
    /// </summary>
    private async Task<IReadOnlySet<string>> UsedNamesFromDbAsync(CancellationToken ct)
    {
        var rows = await _db.IgScheduledPosts
            .AsNoTracking()
            .Select(p => new { p.MediaJson, p.OptionsJson })
            .ToListAsync(ct);

        return UsedNames(rows.Select(r => r.MediaJson), rows.Select(r => r.OptionsJson));
    }

    /// <summary>Bitta JSON satridan nomlarni yig'adi (buzuq bo'lsa xom skan).</summary>
    private static void Collect(string? json, HashSet<string> into)
    {
        var s = (json ?? "").Trim();
        if (s.Length == 0) return;

        try
        {
            using var doc = JsonDocument.Parse(s);
            Walk(doc.RootElement, into);
        }
        catch (JsonException)
        {
            ScanRaw(s, into);
        }
    }

    /// <summary>JSON daraxtining HAR QANDAY chuqurligidagi satr qiymatlarini ko'radi.</summary>
    private static void Walk(JsonElement el, HashSet<string> into)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                var name = MarketingPublicMedia.SafeStoredName(el.GetString());
                if (name is not null) into.Add(name);
                break;

            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject()) Walk(p.Value, into);
                break;

            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray()) Walk(item, into);
                break;
        }
    }

    /// <summary>
    /// Buzuq JSON uchun zaxira: matndan <c>{32 hex}.{kengaytma}</c> naqshini qidiradi.
    /// <para>Ataylab "keng" ishlaydi — bitta ortiqcha nom fayl saqlanib qolishiga olib keladi,
    /// bitta tushib qolgan nom esa ishlab turgan postni buzardi.</para>
    /// </summary>
    private static void ScanRaw(string text, HashSet<string> into)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (!Uri.IsHexDigit(text[i])) { i++; continue; }

            var start = i;
            while (i < text.Length && Uri.IsHexDigit(text[i])) i++;

            // Aynan 32 ta hex va darhol keyin nuqta bo'lishi shart (naqsh `SafeStoredName` niki).
            if (i - start != NameHexLength || i >= text.Length || text[i] != '.') continue;

            var j = i + 1;
            while (j < text.Length && char.IsLetterOrDigit(text[j])) j++;

            var name = MarketingPublicMedia.SafeStoredName(text[start..j]);
            if (name is not null) into.Add(name);
            i = j;
        }
    }
}
