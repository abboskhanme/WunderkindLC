using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Kamera YOZUVI qoidalari — sof funksiyalar (testlangan: <c>CameraRulesTests</c>).
///
/// <para>Qoidaning butun ma'nosi bitta gapda: <b>jonli kuzatuv va YOZUV — ikki boshqa narsa</b>.
/// Yozuv o'chirilganda kamera baribir jonli ko'rinadi; faqat diskka hech narsa yozilmaydi.
/// Batafsil (nega, va nima uchun disk/trafik masalasi) — <c>.claude/rules/cameras.md</c>.</para>
/// </summary>
public static class CameraRules
{
    /// <summary>Shu kamera HOZIR yozib borilishi kerakmi.</summary>
    /// <param name="globalRecordEnabled">Markazdagi BOSH kalit (<see cref="CenterMeta.CameraRecordEnabled"/>).</param>
    /// <param name="cam">Kamera (uning <see cref="Camera.IsActive"/> va <see cref="Camera.RecordEnabled"/> bayroqlari).</param>
    /// <remarks>
    /// ⚠️ Uchala shart ham SHART: bosh kalit yoqilgan + kamera faol + shu kameraga yozuv ruxsat etilgan.
    /// Ya'ni bosh kalit o'chirilsa BARCHA kameralar yozuvdan chiqadi (har birini alohida tahrirlash
    /// kerak emas) — aynan shuning uchun sozlama o'zgarganda barcha kameralar shlyuzga QAYTA
    /// yuboriladi (<c>SettingsController.SaveCameras</c>).
    /// </remarks>
    public static bool ShouldRecord(bool globalRecordEnabled, Camera cam) =>
        globalRecordEnabled && cam.IsActive && cam.RecordEnabled;

    /// <summary>
    /// Shlyuz RTSP manbani QACHON ochsin: yozuv bo'lsa — DOIM (24/7, uzluksiz yozuv uchun),
    /// yozuv bo'lmasa — faqat kimdir qarab turganda.
    /// </summary>
    /// <remarks>
    /// ⚠️ Bu shunchaki optimizatsiya EMAS. `sourceOnDemand = false` da shlyuz kamera bilan
    /// aloqani hech kim qaramasa ham uzmaydi — ya'ni markazning internet kanali har kamera
    /// uchun 24/7 band bo'ladi. Yozuv o'chirilganda buni saqlab qolishning ma'nosi yo'q.
    /// </remarks>
    public static bool SourceOnDemand(bool record) => !record;

    /// <summary>Saqlash muddatini MediaMTX formatiga o'giradi. 0 kun = cheksiz ("0s").</summary>
    public static string RecordDeleteAfter(int retentionDays) =>
        retentionDays > 0 ? $"{retentionDays * 24}h" : "0s";

    /// <summary>Saqlash muddati chegarasi: 0 (cheksiz) .. 365 kun.</summary>
    public static int ClampRetention(int days) => days < 0 ? 0 : days > 365 ? 365 : days;

    /* ---------- Arxiv (orqaga qaytarish / klip) QAYERDAN olinadi ---------- */

    /// <summary>NVR arxividan.</summary>
    public const string ArchiveNvr = "nvr";
    /// <summary>Bizning shlyuz yozuvidan (server diski).</summary>
    public const string ArchiveLocal = "local";
    /// <summary>Arxiv yo'q — orqaga qaytarish mumkin emas (faqat jonli).</summary>
    public const string ArchiveNone = "none";

    /// <summary>
    /// Shu kameraning arxivi qayerdan olinadi.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>NVR LOKAL yozuvdan USTUN.</b> NVR allaqachon 24/7 o'z disklariga yozib turibdi;
    /// bizning serverda ikkinchi nusxa saqlashning ma'nosi yo'q (disk + markazning kanali
    /// bekorga sarflanardi). Shuning uchun NVR sozlangan kamerada lokal yozuv yoqilgan bo'lsa
    /// ham arxiv NVR'dan olinadi.
    /// </remarks>
    public static string ArchiveSource(bool nvrEnabled, bool globalRecordEnabled, Camera cam)
    {
        if (nvrEnabled && cam.NvrChannel > 0) return ArchiveNvr;
        if (ShouldRecord(globalRecordEnabled, cam)) return ArchiveLocal;
        return ArchiveNone;
    }

    /// <summary>
    /// NVR sozlangan kamerani BIZ yozishimiz kerakmi — <b>yo'q</b>. Takroriy nusxa.
    /// </summary>
    /// <remarks>
    /// ⚠️ Bu <see cref="ShouldRecord"/> ustidan turadi: bosh kalit yoqilgan bo'lsa ham, NVR
    /// kanali berilgan kamera shlyuzda YOZILMAYDI. Aks holda admin NVR'ni ulab, keyin
    /// "nega disk to'lyapti" degan savolga tushardi.
    /// </remarks>
    public static bool ShouldRecordWithNvr(bool nvrEnabled, bool globalRecordEnabled, Camera cam) =>
        ArchiveSource(nvrEnabled, globalRecordEnabled, cam) == ArchiveLocal;
}
