using System.Text;
using IntellectCRM.Domain;

namespace IntellectCRM.Application.Services;

/// <summary>
/// Global o'quvchi qidiruvi (topbar / Ctrl+K) QOIDALARI — sof funksiyalar
/// (<c>StudentSearchTests</c> bilan qoplangan).
///
/// <para>SQL tarjimasi <c>StudentsController.Search</c> da: Npgsql'da <c>EF.Functions.ILike</c>
/// (pg_trgm GIN indeksidan foydalanadi), SQLite testlarida esa bu yerdagi
/// <see cref="WhereNameFallback"/> tarmog'i (<c>ILike</c> SQLite'da ishlamaydi —
/// <c>AuditController</c> dagi bilan bir xil sabab). Ikkala tarmoq BIR XIL natija berishi shart.</para>
///
/// <para>APOSTROF muammosi: matn turli klaviaturalardan kiritiladi — "To'lqin" / "Toʻlqin" /
/// "To’lqin" bitta ism. Shuning uchun:
/// so'rov so'zlari <see cref="Words"/> da <c>'</c> ga birxillashtiriladi;
/// Npgsql naqshida apostrof <c>_</c> (bitta-belgi joker) bo'ladi — bazadagi QAYSI variant
/// turganidan qat'i nazar topiladi (<see cref="LikePattern"/>);
/// SQLite tarmog'ida esa ustunning o'zi REPLACE bilan birxillashtiriladi.</para>
/// </summary>
public static class StudentSearch
{
    /// <summary>Bir so'rovda ko'pi bilan nechta natija so'rash mumkin.</summary>
    public const int MaxLimit = 50;
    public const int DefaultLimit = 12;
    /// <summary>So'rovdan ko'pi bilan nechta so'z olinadi (har so'z alohida WHERE sharti —
    /// cheksiz so'z cheksiz SQL shartiga aylanardi).</summary>
    public const int MaxWords = 5;
    /// <summary>Telefon bo'yicha qidirish uchun kamida shuncha raqam kerak (frontenddagi eski
    /// qoida bilan bir xil — 1-2 raqam deyarli hammani topib yuborardi).</summary>
    public const int MinPhoneDigits = 3;

    /// <summary>Klaviaturaga qarab har xil keladigan apostrof ko'rinishlari.</summary>
    private static readonly char[] Apostrophes = { '\'', 'ʻ', 'ʼ', '‘', '’', '`', '´' };

    public static int ClampLimit(int limit) => Math.Clamp(limit, 1, MaxLimit);

    /// <summary>
    /// So'rovni SO'ZLARGA ajratadi: kichik harf, apostroflar <c>'</c> ga birxillashtirilgan,
    /// ko'pi bilan <see cref="MaxWords"/> ta. Har so'z ALOHIDA topilishi shart, TARTIBI muhim emas —
    /// bazada "Familiya Ism" turadi, odam esa "ism familiya" deb yozadi.
    /// </summary>
    public static string[] Words(string term)
    {
        var sb = new StringBuilder(term.Length);
        foreach (var ch in term)
            sb.Append(Array.IndexOf(Apostrophes, ch) >= 0 ? '\'' : char.ToLowerInvariant(ch));
        return sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Take(MaxWords).ToArray();
    }

    /// <summary>So'rovdan faqat RAQAMLARNI oladi (telefon qidiruvi uchun).</summary>
    public static string Digits(string term) => new(term.Where(char.IsDigit).ToArray());

    /// <summary>
    /// Bitta so'z uchun <c>ILIKE</c> naqshi: <c>%so'z%</c>. LIKE maxsus belgilari
    /// (<c>% _ \</c>) <c>\</c> bilan ekranlanadi (SQL'da <c>ESCAPE '\'</c> bilan ishlatilsin),
    /// apostrof esa <c>_</c> jokeriga aylanadi — bazadagi apostrof varianti qanday bo'lsa ham topadi.
    /// </summary>
    public static string LikePattern(string word)
    {
        var sb = new StringBuilder(word.Length + 2).Append('%');
        foreach (var ch in word)
        {
            if (ch is '%' or '_' or '\\') sb.Append('\\').Append(ch);
            else if (ch == '\'') sb.Append('_');
            else sb.Append(ch);
        }
        return sb.Append('%').ToString();
    }

    /// <summary>
    /// Ism bo'yicha filtr — PROVAYDERGA BOG'LIQ BO'LMAGAN tarmoq (SQLite testlari va zaxira).
    /// O'quvchi + ota-ona ismlari BITTA satrga qo'shilib qidiriladi (kim so'ralsa ham topilsin) —
    /// frontenddagi eski xatti-harakat bilan bir xil. So'zlar <see cref="Words"/> dan kelgan
    /// (kichik harf, apostrof <c>'</c>) bo'lishi shart.
    /// </summary>
    public static IQueryable<Student> WhereNameFallback(IQueryable<Student> q, string[] words)
    {
        foreach (var w in words)
        {
            var word = w; // closure har iteratsiyada o'z qiymatini olsin
            q = q.Where(s =>
                (s.FullName + " " + s.ParentFullName + " " + s.FatherFullName + " " + s.MotherFullName)
                    .ToLower()
                    .Replace("ʻ", "'").Replace("ʼ", "'")
                    .Replace("‘", "'").Replace("’", "'")
                    .Replace("`", "'").Replace("´", "'")
                    .Contains(word));
        }
        return q;
    }

    /// <summary>
    /// Telefon bo'yicha filtr — ikkala provayderda ham ishlaydi (REPLACE + LIKE). Ustunlar
    /// "+998-XX-XXX-XX-XX" ko'rinishida saqlanadi — ajratkichlar olib tashlanib, faqat raqamlar
    /// bo'yicha qismiy moslik qidiriladi (o'z + ota + ona + asosiy ota-ona raqami).
    /// </summary>
    public static IQueryable<Student> WherePhone(IQueryable<Student> q, string digits)
    {
        return q.Where(s =>
            s.Phone.Replace("+", "").Replace("-", "").Replace(" ", "").Replace("(", "").Replace(")", "").Contains(digits)
            || s.ParentPhone.Replace("+", "").Replace("-", "").Replace(" ", "").Replace("(", "").Replace(")", "").Contains(digits)
            || s.FatherPhone.Replace("+", "").Replace("-", "").Replace(" ", "").Replace("(", "").Replace(")", "").Contains(digits)
            || s.MotherPhone.Replace("+", "").Replace("-", "").Replace(" ", "").Replace("(", "").Replace(")", "").Contains(digits));
    }

    /// <summary>A'zolik holati yorlig'i — <c>GetAll</c> dagi bilan BIR XIL ustunlik:
    /// active &gt; trial &gt; yearFrozen &gt; frozen, guruhsiz — bo'sh.
    /// <para><paramref name="yearFreeze"/> — o'quvchining muzlatilgan a'zoliklaridan birortasi
    /// «AKTIV MUZLATISH» (yangi o'quv yiliga o'tish) belgisi bilan muzlatilganmi. Bu ALOHIDA
    /// parametr, chunki a'zolik <c>Status</c>i baribir "frozen" bo'lib qoladi — belgi statusda
    /// EMAS, alohida bayroqda yashaydi (aks holda "frozen" ni tekshiradigan o'nlab joy buzilardi).
    /// Standart qiymat <c>false</c> — eski chaqiruvlar avvalgidek "frozen" oladi.</para></summary>
    public static string MemberState(IEnumerable<string?> statuses, bool yearFreeze = false)
    {
        var set = statuses.Where(s => s != null).Select(s => s!).ToHashSet();
        return set.Contains("active") ? "active"
            : set.Contains("trial") ? "trial"
            // "yearFrozen" faqat MUZLATILGAN a'zolik bor bo'lsa mantiqiy: bayroq muzlatishning
            // turini bildiradi, o'z-o'zidan holat emas.
            : set.Contains("frozen") ? (yearFreeze ? "yearFrozen" : "frozen") : "";
    }

    /// <summary>In-memory tartiblash uchun birxillashtirish (kichik harf + apostrof <c>'</c>).</summary>
    public static string Normalize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
            sb.Append(Array.IndexOf(Apostrophes, ch) >= 0 ? '\'' : char.ToLowerInvariant(ch));
        return sb.ToString();
    }
}
