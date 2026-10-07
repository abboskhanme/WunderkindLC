using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Tranzaksiya turlari katalogining SOF qismi: bo'limlar, bo'lim → yo'nalish qoidasi va
/// markazning boshlang'ich ro'yxati (edutizimdan ko'chirilgan).
///
/// <para>⚠️ Tur nomi erkin, lekin har biri TIZIM toifasiga (<c>tuition</c>, <c>salary</c>,
/// <c>penalty</c> ...) bog'langan — sabab <see cref="TransactionType"/> izohida.</para>
/// </summary>
public static class TransactionTypeCatalog
{
    /// <summary>Bo'limlar (edutizimdagi to'rtta tab) — tartibi ham shu.</summary>
    public static readonly string[] Kinds = ["kirim", "chiqim", "vaucher", "jarima"];

    /// <summary>
    /// Bo'lim PUL YO'NALISHINI belgilaydi: vaucher — kirim, jarima — chiqim.
    ///
    /// <para>⚠️ Yo'nalish foydalanuvchidan SO'RALMAYDI: edutizimda ham bo'lim tanlanadi, xolos.
    /// Alohida so'ralsa "chiqim bo'limidagi kirim turi" kabi zid yozuv paydo bo'lardi.</para>
    /// </summary>
    public static string DirectionOf(string kind) =>
        kind is "chiqim" or "jarima" ? "expense" : "income";

    /// <summary>Bo'lim nomi to'g'rimi (noma'lum bo'lim jimgina "kirim" ga tushib qolmasin).</summary>
    public static bool IsKind(string? kind) => kind is not null && Kinds.Contains(kind);

    /// <summary>
    /// Markazning boshlang'ich ro'yxati — <c>(bo'lim, nom, tizim toifasi)</c>.
    /// Bir marta (bo'sh jadvalga) yoziladi; keyin foydalanuvchi o'zi boshqaradi.
    /// </summary>
    public static readonly (string Kind, string Name, string BaseCategory)[] Seed =
    [
        // — KIRIM —
        // ⚠️ Kitob/Mock/Internet/Kanstovar 2026-10 dan O'Z toifasida (`FinanceTypeSeed` — yig'ilish talabi:
        // to'lov va filial xarajatlari ALOHIDA toifalarda). Mavjud bazada ular bir marta qayta bog'lanadi.
        ("kirim", "O'quvchi to'ladi", "tuition"),
        ("kirim", "Kitob uchun", "book"),
        ("kirim", "Mock to'lov", "mock"),
        ("kirim", "Oldingi oydagi qarzi", "tuition"),
        ("kirim", "Fond", "other"),
        ("kirim", "SAT oylik to'lovlari", "tuition"),
        ("kirim", "Boshqa kirim", "other"),
        // — CHIQIM —
        ("chiqim", "Adashib kiritilgan pulni balansdan chiqarish", "other"),
        ("chiqim", "Hodimga avans", "salary"),
        ("chiqim", "Hodimga oylik", "salary"),
        ("chiqim", "Internet va telefon", "internet"),
        ("chiqim", "Kommunal to'lovlar", "utilities"),
        ("chiqim", "Arenda", "rent"),
        ("chiqim", "Kanstovar", "stationery"),
        ("chiqim", "Boshqa", "other"),
        ("chiqim", "One Family", "other"),
        ("chiqim", "Marketing", "other"),
        ("chiqim", "SAT o'qituvchisi uchun oylik", "salary"),
        ("chiqim", "Uyga xarajatlar", "other"),
        ("chiqim", "Vaucher", "other"),
        // — VAUCHER —
        ("vaucher", "O'quv markaz hisobidan", "other"),
        ("vaucher", "O'qituvchiga bonus", "other"),
        ("vaucher", "1000_tekin bonus", "other"),
        // — JARIMA —
        ("jarima", "O'qituvchiga jarima", "penalty"),
    ];

    /// <summary>Seed ro'yxatidan entity qatorlari (tartib — ro'yxatdagi o'rni).</summary>
    public static List<TransactionType> SeedRows()
    {
        var rows = new List<TransactionType>();
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (kind, name, baseCategory) in Seed)
        {
            order.TryGetValue(kind, out var n);
            order[kind] = n + 1;
            rows.Add(new TransactionType
            {
                Name = name,
                Kind = kind,
                Direction = DirectionOf(kind),
                BaseCategory = baseCategory,
                Order = n,
            });
        }
        return rows;
    }
}
