namespace WunderkindLC.Domain;

/// <summary>
/// OMMAVIY DAVOMAT XABARI NAVBATI — "darsga kelmadi" / "darsga keldi" avto-xabarining BITTA
/// oluvchisi (o'quvchi). Yozuvchi — <c>AttendanceNotify.EnqueueBulkAsync</c>, ishlovchi —
/// <c>AttendanceNotifyWorker</c> (fon xizmati).
///
/// <para><b>NEGA BAZADA:</b> ilgari ommaviy davomat xabarlari xotiradagi fon vazifasida
/// (<c>Task.Run</c>) yuborilardi. 25 o'quvchiga SMS bittalab ketadi (Local SMS'da har biri orasida
/// kutish) — shu orada deploy/restart bo'lsa qolgan ota-onalar xabarni JIMGINA olmasdi. Endi har
/// oluvchi qatori jurnal yozuvi bilan BITTA <c>SaveChanges</c> da yoziladi (jurnal saqlandi =
/// xabar navbatda) va fon ishchisi ilova qayta ishga tushganda ham qolganini yuboradi.</para>
///
/// <para><see cref="Status"/> — concurrency token: bitta qatorni ikki ishchi band qila olmaydi.
/// Yuborishdan OLDIN <c>sending</c> deb band qilinadi; aynan yuborish paytida ilova to'xtasa qator
/// <c>unknown</c> bo'ladi va QAYTA yuborilmaydi (xabar ketgan bo'lishi mumkin — takror SMS dan
/// bitta yo'qotish arzon, eski ommaviy SMS navbati bilan bir xil qoida).</para>
/// </summary>
public partial class AttendanceNotifyItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>"absent" (darsga kelmadi) | "present" (darsga keldi).</summary>
    public string Kind { get; set; } = "absent";
    public string ClassId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    /// <summary>Dars sanasi "yyyy-MM-dd".</summary>
    public string Date { get; set; } = string.Empty;
    public int Period { get; set; }
    /// <summary>"Kelmadi" sababi (faqat <c>absent</c> uchun).</summary>
    public string? ReasonId { get; set; }
    public string StudentId { get; set; } = string.Empty;
    /// <summary>pending | sending (band qilindi, yuborilmoqda) | done | failed |
    /// unknown (yuborish paytida ilova to'xtadi — QAYTA yuborilmaydi) | expired (juda kech — yuborilmadi).</summary>
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    public DateTime UpdatedAt { get; set; } = AppClock.Now;
}
