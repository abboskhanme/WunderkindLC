namespace WunderkindLC.Application.Services;

/// <summary>
/// LID QAYERDAN KELGAN — kanal (origin) tasnifi. Sof funksiyalar, testlangan
/// (<c>LeadOriginsTests</c>).
///
/// <para><b>Nima uchun kerak:</b> "Formalar statistikasi" faqat FORMADAN kelgan lidlarni
/// ko'rsatadi, CRM'da esa qo'lda kiritilgan, daraja testidan va Instagramdan kelgan lidlar ham
/// bor. Ikkala sahifada ham "jami lid" degan raqam bo'lgani uchun, ular boshqa-boshqa narsani
/// bildirsa foydalanuvchi chalkashadi. Shu sababli kanal tasnifi BITTA joyda.</para>
///
/// <para><b>Kanal = BIRINCHI TEGINISH</b> (lid QANDAY paydo bo'lgan), keyingi murojaatlar emas.
/// Shuning uchun "qo'lda kiritilgan" eng ustun: xodim lidni o'zi kiritgan bo'lsa, o'sha odam
/// keyinroq forma to'ldirgani (takroriy murojaat) kanalni o'zgartirmaydi.</para>
/// </summary>
public static class LeadOrigins
{
    /// <summary>Xodim CRM'da qo'lda kiritgan (<c>created</c> hodisasida <c>ActorUserId</c> bor).</summary>
    public const string Manual = "manual";
    /// <summary>Ommaviy lid formasi (<c>LeadFormSubmission</c>).</summary>
    public const string Form = "form";
    /// <summary>Daraja testi (<c>LevelTestResult</c>).</summary>
    public const string Test = "test";
    /// <summary>Instagram AI agenti (<c>IgConversation.LeadId</c>) — izoh yoki DM.</summary>
    public const string Instagram = "instagram";
    /// <summary>Instagram/Facebook REKLAMASI — Lead Ads formasi (<c>IgAdLead.LeadId</c>).</summary>
    public const string Ads = "ads";
    /// <summary>Qolgani: landing, bot, eski yozuvlar — tasniflab bo'lmadi.</summary>
    public const string Other = "other";

    /// <summary>Ko'rsatish TARTIBI (hisobotlarda doim shu ketma-ketlik).</summary>
    public static readonly IReadOnlyList<string> Order = [Form, Test, Ads, Instagram, Manual, Other];

    /// <summary>Kanal yorlig'i (o'zbekcha). Noma'lum kalit — o'zi qaytadi.</summary>
    public static string LabelOf(string key) => key switch
    {
        Manual => "Qo'lda kiritilgan",
        Form => "Lid formasi",
        Test => "Daraja testi",
        Instagram => "Instagram (izoh/DM)",
        Ads => "Instagram reklamasi",
        Other => "Boshqa (sayt, eski yozuvlar)",
        _ => key,
    };

    /// <summary>
    /// Lidning kanali. Tekshiruv TARTIBI muhim:
    /// <list type="number">
    ///   <item><b>qo'lda</b> — xodim kiritgan bo'lsa boshqa hech narsa qaralmaydi (birinchi teginish);</item>
    ///   <item>lid formasi → daraja testi → reklama → Instagram (avtomatik kanallar);</item>
    ///   <item>hech qayerda topilmasa — <see cref="Other"/>.</item>
    /// </list>
    /// </summary>
    /// <param name="manualLeadIds">
    /// <c>created</c> hodisasida <c>ActorUserId</c> to'ldirilgan lidlar. ⚠️ Bu maydon 2026-08 dan
    /// oldin YOZILMAGAN, ya'ni eski qo'lda kiritilgan lidlar <see cref="Other"/> ga tushadi —
    /// yorlig'ida shu sabab ochiq yozilgan ("eski yozuvlar").
    /// </param>
    public static string Classify(
        string leadId,
        IReadOnlySet<string>? manualLeadIds = null,
        IReadOnlySet<string>? formLeadIds = null,
        IReadOnlySet<string>? testLeadIds = null,
        IReadOnlySet<string>? instagramLeadIds = null,
        IReadOnlySet<string>? adsLeadIds = null)
    {
        if (string.IsNullOrEmpty(leadId)) return Other;
        if (manualLeadIds?.Contains(leadId) == true) return Manual;
        if (formLeadIds?.Contains(leadId) == true) return Form;
        if (testLeadIds?.Contains(leadId) == true) return Test;
        // ⚠️ REKLAMA Instagram'dan OLDIN tekshiriladi: reklama formasini to'ldirgan odam keyin
        // DM ham yozishi mumkin va u holda lid IKKALA ro'yxatda ham bo'ladi. Kanal — BIRINCHI
        // teginish, birinchisi esa pul to'langan reklama edi.
        if (adsLeadIds?.Contains(leadId) == true) return Ads;
        if (instagramLeadIds?.Contains(leadId) == true) return Instagram;
        return Other;
    }
}
