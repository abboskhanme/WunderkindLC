using System.Linq.Expressions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// GURUH O'QITUVCHIGA KO'RINADIMI — sof qoidalar (DB/HTTP kontekstisiz), shuning uchun testlanadi.
///
/// <para>Savol bitta: <b>"o'qituvchi ilovasida shu guruh bo'lishi kerakmi?"</b>. Javob uch joyda
/// bir xil bo'lishi shart — guruhlar RO'YXATIDA, guruhning ICHIGA kirishda (jurnal, baholash,
/// testlar, o'quv dasturi, "Aloqa") va CHAT kanallarida. Ilgari qoida yo'q edi: ro'yxat BARCHA
/// guruhlarni qaytarardi, ya'ni <b>tugatilgan (sertifikat bilan yopilgan) va arxivlangan</b>
/// guruhlar o'qituvchida yillab osilib turardi.</para>
///
/// <para>Yashirinish sabablari:</para>
/// <list type="bullet">
///   <item><b>arxiv</b> — guruh arxivlangan yoki "Tugatish (sertifikat bilan)" / "Guruhni yopish"
///     orqali yopilgan (<see cref="Group.IsArchived"/> yoki <c>Status=="archived"</c> —
///     ikkalasi ham tekshiriladi, chunki holat qo'lda ham qo'yilishi mumkin).</item>
///   <item><b>blok</b> — admin "Guruhlar → Amallar → Vaqtincha bloklash" qilgan
///     (<see cref="Group.IsBlocked"/>). Vaqtinchalik va bir tugma bilan qaytariladi.</item>
/// </list>
///
/// <para>DIQQAT: bu qoida faqat O'QITUVCHI tomoni uchun. Admin panelida arxiv guruh ham,
/// bloklangan guruh ham odatdagidek ochiladi; o'quvchi/ota-ona portali va MAOSH hisobi ham
/// tegilmaydi (o'qituvchi yopilgan guruhda ishlagan pulini ko'raverishi kerak).</para>
/// </summary>
public static class TeacherGroupAccess
{
    /// <summary><see cref="Group.Status"/> ning "arxiv" qiymati.</summary>
    public const string StatusArchived = "archived";

    /// <summary>Yashirinish sababi: guruh arxivlangan/yopilgan.</summary>
    public const string ReasonArchived = "archived";

    /// <summary>Yashirinish sababi: admin vaqtincha bloklagan.</summary>
    public const string ReasonBlocked = "blocked";

    /// <summary>
    /// Guruh o'qituvchidan YASHIRINMI va nega — <c>null</c> bo'lsa ko'rinadi.
    /// Blok arxivdan USTUN (ikkalasi bo'lsa "blocked" qaytadi): admin ataylab qilgan amal
    /// tushuntirishga muhimroq.
    /// </summary>
    public static string? HiddenReason(Group g) =>
        g is null ? null
        : g.IsBlocked ? ReasonBlocked
        : g.IsArchived || g.Status == StatusArchived ? ReasonArchived
        : null;

    /// <summary>Guruh o'qituvchi ilovasida ko'rinadimi (sababsiz, qisqa shakl).</summary>
    public static bool Visible(Group g) => HiddenReason(g) is null;

    /// <summary>
    /// Guruh AYNAN shu o'qituvchiniki (EGASI) VA unga ko'rinadimi. Guruhga kirishning yagona
    /// darvozasi: <c>TeacherPortalController.ResolveOwnedGroup</c> va <c>Teaches</c> shuni chaqiradi.
    ///
    /// <para>⚠️ <b>EGALIK — YAGONA yo'l EMAS:</b> guruhga vaqtincha O'RINBOSAR ham kira oladi.
    /// Bu qoida SHU faylda emas, <see cref="SubstituteTeacherService"/> da (u SANAGA bog'liq va
    /// bazani talab qiladi — bu yerdagi sof funksiyalarga sig'maydi). <c>ResolveOwnedGroup</c>
    /// ikkalasini ham BIRGA hal qiladi va <c>IsSubstitute</c> bayrog'ini qaytaradi; har bir
    /// endpoint o'rinbosarga NIMA ochilishini o'zi hal qiladi
    /// (jadval: <c>.claude/rules/substitute-teachers.md</c> §3).</para>
    ///
    /// <para>Bu yerdagi <see cref="Visible"/> tekshiruvi esa IKKALA yo'l uchun ham majburiy:
    /// arxivlangan/bloklangan guruh o'rinbosarga ham ochilmaydi.</para>
    /// </summary>
    public static bool OwnedBy(Group g, string teacherId) =>
        g is not null && !string.IsNullOrEmpty(teacherId)
        && g.TeacherId == teacherId && Visible(g);

    /// <summary>
    /// AYNAN <see cref="Visible(Group)"/> qoidasining EF (SQL) tarjimasi — ro'yxat so'rovlarida
    /// <c>db.Classes.Where(TeacherGroupAccess.VisibleQuery)</c> ko'rinishida ishlatiladi.
    ///
    /// <para>Nega ikkita nusxa: <c>Visible</c> xotiradagi obyekt ustida ishlaydi (u yerda
    /// <c>Compile()</c> qilish har chaqiruvda qimmat), bu esa ifoda daraxti sifatida SQL'ga
    /// tarjima bo'ladi. Ikkisi bir-biridan ajralib ketmasligini <c>TeacherGroupAccessTests</c>
    /// tekshiradi (har ikkala qoida bir xil holatlar to'plamida solishtiriladi).</para>
    /// </summary>
    public static readonly Expression<Func<Group, bool>> VisibleQuery =
        g => !g.IsBlocked && !g.IsArchived && g.Status != StatusArchived;
}
