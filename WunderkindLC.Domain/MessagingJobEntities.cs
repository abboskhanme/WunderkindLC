namespace WunderkindLC.Domain;

/// <summary>
/// FON ISHI BELGISI — "shu ish (yoki shu oluvchiga shu ish) shu SANA uchun allaqachon bajarilgan".
///
/// <para><b>NEGA KERAK:</b> kunlik eslatma xizmatlari (qarzdorlik, tug'ilgan kun, sinov darsi,
/// jadvalli erkin eslatma) "bugun ishladimi" belgisini XOTIRADA saqlardi. Har deploy/restart
/// (09:00 dan keyin) belgini o'chirar va BUTUN ro'yxat qayta yuborilardi — har qarzdorga yana SMS
/// (pul), Telegram va push. Endi belgi bazada.</para>
///
/// <para>Ikki xil kalit (yagona yozuvchi — <c>JobRunMarker</c>):</para>
/// <list type="bullet">
///   <item><c>"payment_debt"</c> — ISH darajasi: kunlik ish TO'LIQ tugadi (keyingi uyg'onishda qayta
///     boshlanmaydi);</item>
///   <item><c>"payment_debt:{studentId}"</c> — OLUVCHI darajasi: yuborishdan OLDIN atomik band
///     qilinadi. Ish yarmida yiqilsa, qayta ishga tushganda allaqachon olganlarga TAKROR ketmaydi,
///     qolganlariga esa yetib boradi.</item>
/// </list>
///
/// <para><see cref="LastRunDate"/> — concurrency token: ikki nusxa bir vaqtda band qilmoqchi bo'lsa
/// faqat bittasining <c>UPDATE ... WHERE LastRunDate = eski</c> so'rovi o'tadi.</para>
/// </summary>
public class BackgroundJobRun
{
    /// <summary>Ish (yoki ish + oluvchi) kaliti — PK.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Oxirgi bajarilgan (band qilingan) sana "yyyy-MM-dd" — markaz vaqti.</summary>
    public string LastRunDate { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = AppClock.Now;
}

/// <summary>
/// OMMAVIY SMS NAVBATI — partiya ishi BAZADA (ilgari faqat xotirada edi).
///
/// <para><b>NEGA:</b> 300 ta SMS fonda bir necha daqiqa ketadi. Shu orada deploy/restart bo'lsa
/// xotiradagi navbat yo'qolar, qolgan 260 ta hech qachon ketmas, progress esa bazadan "tugadi
/// 40/300" bo'lib ko'rinardi. Endi har oluvchi <see cref="SmsQueueItem"/> qatori — ilova qayta
/// ishga tushganda tugamagan partiya DAVOM ETTIRILADI (allaqachon ketganlarga qayta yuborilmaydi).</para>
///
/// <para>PK — <see cref="SmsBatch"/> id'si (bitta partiya = bitta navbat ishi).</para>
/// </summary>
public class SmsQueueJob
{
    /// <summary><see cref="SmsBatch.Id"/> — PK.</summary>
    public string BatchId { get; set; } = string.Empty;
    /// <summary>"eskiz" | "local".</summary>
    public string Provider { get; set; } = "eskiz";
    /// <summary>Local SMS agenti (bo'sh — Sozlamalardagi standart).</summary>
    public string? AgentId { get; set; }
    /// <summary>Eskiz yetkazish holati callback manzili.</summary>
    public string? CallbackUrl { get; set; }
    /// <summary>Lid tarixiga yoziladigan izoh (lid SMS'lari uchun).</summary>
    public string? LeadNote { get; set; }
    public string ActorName { get; set; } = string.Empty;
    /// <summary>"pending" (yuborilmoqda / davom ettiriladi) | "done" | "interrupted" (qayta ishga
    /// tushganda juda eski bo'lgani uchun qolgani BEKOR qilindi).</summary>
    public string State { get; set; } = "pending";
    /// <summary>Necha marta restartdan keyin davom ettirilgan (0 — uzilmagan).</summary>
    public int ResumeCount { get; set; }
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    public DateTime? FinishedAt { get; set; }
}

/// <summary>Ommaviy SMS partiyasining BITTA oluvchisi (<see cref="SmsQueueJob"/>).</summary>
public class SmsQueueItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string BatchId { get; set; } = string.Empty;
    /// <summary>Partiya ichidagi tartib (yuborish tartibi saqlansin).</summary>
    public int Seq { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Allaqachon shaxsiylashtirilgan matn.</summary>
    public string Message { get; set; } = string.Empty;
    /// <summary>Lid SMS'i bo'lsa — lid tarixiga yozuv qo'shiladi.</summary>
    public string? LeadId { get; set; }
    /// <summary>pending | sending (band qilindi, yuborilmoqda) | sent | failed |
    /// unknown (yuborish paytida ilova to'xtadi — QAYTA yuborilmaydi, chunki SMS ketgan bo'lishi
    /// mumkin) | cancelled. Concurrency token — bir qatorni ikki ishchi band qila olmaydi.</summary>
    public string Status { get; set; } = "pending";
    public DateTime UpdatedAt { get; set; } = AppClock.Now;
}
