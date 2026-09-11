using WunderkindLC.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace WunderkindLC.Tests;

/// <summary>
/// Testlar uchun izolyatsiyalangan <see cref="AppDbContext"/> yaratadigan yordamchi.
///
/// ASOSIY variant — <see cref="Sqlite"/>: haqiqiy relyatsion baza (PostgreSQL'ga InMemory'dan
/// ko'ra yaqinroq) — unique indekslar, FK, DeleteBehavior kabi cheklovlar HAQIQATDA ishlaydi.
/// Model ba'zi entity'larda <c>List&lt;string&gt;</c>/<c>List&lt;int&gt;</c> xossalarni ishlatadi
/// (masalan <c>AppUser.Permissions</c>, <c>Group.Days</c>, <c>StaffRoleTemplate.DefaultPermissions</c>).
/// PostgreSQL'da bular Npgsql orqali tabiiy massiv ustuniga tushadi; SQLite esa massivni
/// tabiiy qo'llamaydi — LEKIN EF Core 8'ning "primitive collections" xususiyati bunday
/// xossalarni avtomatik JSON ustuniga o'giradi (HasConversion yozish shart emas). Tekshirib
/// ko'rilgan: SQLite ustida <c>EnsureCreated()</c> ham, massiv xossali entity saqlab-o'qish ham
/// (masalan <c>AppUser.Permissions</c>) MUAMMOSIZ ishlaydi — shu sabab SQLite variant to'liq
/// modelni qamrab oladi va standart tanlov qilib olindi.
///
/// ZAXIRA variant — <see cref="InMemory"/>: Microsoft.EntityFrameworkCore.InMemory provayderi.
/// SQLite biror sabab bilan ishlamay qolsa (masalan kelajakda haqiqiy Npgsql-maxsus ustun turi
/// qo'shilsa — jsonb, range va h.k.) shu bilan almashtiriladi. Kamchiligi: unique indeks/FK
/// cheklovlarini haqiqiy bazadagidek qattiq tekshirmaydi.
/// </summary>
public sealed class TestDb : IDisposable
{
    public AppDbContext Context { get; }

    private readonly SqliteConnection? _sqliteConnection;

    private TestDb(AppDbContext context, SqliteConnection? sqliteConnection = null)
    {
        Context = context;
        _sqliteConnection = sqliteConnection;
    }

    /// <summary>
    /// SQLite IN-MEMORY (<c>Filename=:memory:</c>) ustida <see cref="AppDbContext"/>. Ulanish
    /// ochiq holda ushlab turiladi — SQLite in-memory bazasi ulanish yopilishi bilan yo'qoladi,
    /// shuning uchun uni <see cref="Dispose"/> chaqirilguncha ushlab turamiz. Har chaqiriqda
    /// yangi, boshqalardan mustaqil baza yaratiladi.
    /// </summary>
    public static TestDb Sqlite()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return new TestDb(context, connection);
    }

    /// <summary>
    /// Microsoft.EntityFrameworkCore.InMemory provayderi ustida <see cref="AppDbContext"/>.
    /// Har chaqiriqda o'ziga xos (Guid bilan nomlangan) ma'lumotlar bazasi yaratiladi. Faqat
    /// SQLite variant ishlamay qolgan holatlar uchun zaxira sifatida qoldirilgan — batafsili
    /// izoh uchun sinf tepasidagi izohga qarang.
    /// </summary>
    public static TestDb InMemory()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return new TestDb(context);
    }

    /// <summary>
    /// AYNI bazaga IKKINCHI (mustaqil o'zgarish kuzatuvchisiga ega) kontekst — <b>parallel ikki
    /// so'rovni</b> modellashtirish uchun. Faqat <see cref="Sqlite"/> variantida ishlaydi:
    /// ulanish bitta, ya'ni ikkala kontekst ham bir xil ma'lumotni ko'radi.
    ///
    /// <para>Nima uchun kerak? Konkurentlik tokenlari (masalan <c>Book.Stock</c>,
    /// <c>FaceChallenge.UsedAt</c>) FAQAT shunday sinaladi: bitta kontekstda o'qilgan asl qiymat
    /// boshqasida allaqachon o'zgargan bo'lishi kerak. Bitta kontekst ichida buni yasab bo'lmaydi.</para>
    ///
    /// <para>Qaytarilgan kontekstni chaqiruvchi O'ZI yopadi (<c>using</c>).</para>
    /// </summary>
    public AppDbContext NewContext()
    {
        if (_sqliteConnection is null)
            throw new InvalidOperationException("NewContext() faqat TestDb.Sqlite() bilan ishlaydi");
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection).Options);
    }

    public void Dispose()
    {
        Context.Dispose();
        _sqliteConnection?.Dispose();
    }
}
