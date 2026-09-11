using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// «LID KIRITISH FORMASI» qoidalari — SOF funksiyalar (baza yo'q, testlangan:
/// <c>LeadEntryFormTests</c>).
///
/// <para><b>Modulning maqsadi:</b> menejer <c>/admin/leads</c> da YANGI LID kiritganda qaysi
/// maydon so'ralishi va qaysisi MAJBURIY ekanini markaz o'zi belgilaydi
/// ("O'quv bo'limi → Formalar → Lid kiritish formasi"). Ustiga QO'SHIMCHA savollar
/// qo'shilishi mumkin (ixtiyoriy).</para>
///
/// <para>⚠️ <b>ENG MUHIM CHEGARA:</b> bu qoidalar FAQAT QO'LDA kiritishga tegishli
/// (<c>POST/PUT /api/admin/leads</c>). Ommaviy lid formasi, daraja testi, landing, Instagram va
/// Meta leadgen lidni O'ZLARINING qoidalari bilan yaratadi — u yerda bu tekshiruv YO'Q. Aks
/// holda "Tug'ilgan kun majburiy" degan sozlama Instagram'dan kelayotgan lidlarni jimgina rad
/// etar va markaz mijozini YO'QOTARDI.</para>
/// </summary>
public static class LeadEntryRules
{
    // ==================== Maydon holati ====================

    /// <summary>Maydon umuman so'ralmaydi (formada chizilmaydi).</summary>
    public const string StateHidden = "hidden";
    /// <summary>So'raladi, lekin bo'sh qoldirilishi mumkin.</summary>
    public const string StateOptional = "optional";
    /// <summary>So'raladi va bo'sh qoldirilsa saqlanmaydi.</summary>
    public const string StateRequired = "required";

    public static readonly IReadOnlyList<string> States =
        new[] { StateHidden, StateOptional, StateRequired };

    /// <summary>Noma'lum holat "ixtiyoriy"ga tushadi — sozlama buzilib qolgandan ko'ra ishlagani yaxshi.</summary>
    public static string NormalizeState(string? s) =>
        s is not null && States.Contains(s) ? s : StateOptional;

    public static string StateOf(bool visible, bool required) =>
        !visible ? StateHidden : required ? StateRequired : StateOptional;

    // ==================== STANDART maydonlar katalogi ====================

    /// <summary>Standart maydon kaliti — <see cref="Lead"/> maydonining camelCase nomi.</summary>
    public const string KeyFullName = "fullName";
    public const string KeyGender = "gender";
    public const string KeyBirthDate = "birthDate";
    public const string KeyPhone = "phone";
    public const string KeyFatherFullName = "fatherFullName";
    public const string KeyFatherPhone = "fatherPhone";
    public const string KeyMotherFullName = "motherFullName";
    public const string KeyMotherPhone = "motherPhone";
    public const string KeySource = "source";
    public const string KeyInterestSubject = "interestSubject";
    public const string KeyDistrict = "districtId";
    public const string KeySchool = "schoolId";
    public const string KeyNote = "note";

    /// <param name="Key">Kalit (<see cref="Lead"/> maydoni).</param>
    /// <param name="Label">Formadagi standart yorlig'i.</param>
    /// <param name="Locked">Sozlab bo'lmaydigan maydon — DOIM ko'rinadi va DOIM majburiy.</param>
    public record StandardField(string Key, string Label, bool Locked = false);

    /// <summary>
    /// Standart maydonlar — TARTIBI formadagi chizilish tartibi bilan bir xil.
    ///
    /// <para>⚠️ Standart maydonlarning TARTIBI sozlanmaydi (faqat holati): ular formada juftlik
    /// va bog'liqlik bilan chiziladi (tuman → maktab kaskadi, ikki ustunli setka), tartibni
    /// erkin o'zgartirish bu tuzilishni buzardi. Erkin tartib — QO'SHIMCHA savollarda.</para>
    /// </summary>
    public static readonly IReadOnlyList<StandardField> Standard = new StandardField[]
    {
        // F.I.SH — lidning eng kam ma'lumoti (ommaviy formadagi "ism va telefon HAR DOIM
        // so'raladi" qoidasi bilan bir xil sabab): ismsiz lid kanbanda tanib bo'lmas kartaga
        // aylanardi. Shuning uchun u sozlamadan CHIQARILGAN.
        new(KeyFullName, "F.I.SH", Locked: true),
        new(KeyGender, "Jinsi"),
        new(KeyBirthDate, "Tug'ilgan kun"),
        new(KeyPhone, "O'z telefon raqami"),
        new(KeyFatherFullName, "Otasi F.I.SH"),
        new(KeyFatherPhone, "Otasi raqami"),
        new(KeyMotherFullName, "Onasi F.I.SH"),
        new(KeyMotherPhone, "Onasi raqami"),
        new(KeySource, "Manba"),
        new(KeyInterestSubject, "Qiziqqan fani (kurs)"),
        new(KeyDistrict, "Tuman"),
        new(KeySchool, "Maktab"),
        new(KeyNote, "Izoh"),
    };

    public static StandardField? FindStandard(string? key) =>
        key is null ? null : Standard.FirstOrDefault(x => x.Key == key);

    public static bool IsStandardKey(string? key) => FindStandard(key) is not null;

    /// <summary>Bir formadagi qo'shimcha savollar chegarasi (lid formasi bilan bir xil).</summary>
    public const int MaxFields = LeadFormService.MaxFields;
    public const int MaxOptions = LeadFormService.MaxOptions;
    public const int MaxAnswerLength = LeadFormService.MaxAnswerLength;

    // ==================== Normalizatsiya ====================

    /// <summary>
    /// Bitta standart maydon uchun SAQLANADIGAN holat. Qoidalar:
    /// <list type="bullet">
    /// <item>qulflangan maydon (F.I.SH) — DOIM ko'rinadi va majburiy;</item>
    /// <item>ko'rinmaydigan maydon MAJBURIY bo'la olmaydi (aks holda foydalanuvchi to'ldira
    /// olmaydigan maydon tufayli lid umuman saqlanmasdi);</item>
    /// <item>maktab tumansiz ko'rsatilmaydi — maktab ro'yxati tumandan quriladi (kaskad),
    /// tumansiz select doim bo'sh turardi.</item>
    /// </list>
    /// </summary>
    public static (bool Visible, bool Required) NormalizeStandard(
        string key, string? state, IReadOnlyDictionary<string, string> all)
    {
        var def = FindStandard(key);
        if (def is null) return (false, false);
        if (def.Locked) return (true, true);

        var s = NormalizeState(state);
        if (key == KeySchool)
        {
            var district = NormalizeState(all.TryGetValue(KeyDistrict, out var d) ? d : null);
            if (district == StateHidden) return (false, false);
        }
        return s switch
        {
            StateHidden => (false, false),
            StateRequired => (true, true),
            _ => (true, false),
        };
    }

    /// <summary>
    /// Qo'shimcha savolni tozalaydi. <c>null</c> — savol tashlab yuboriladi (yorlig'i bo'sh).
    /// Variantli tur variantsiz qolsa oddiy matnga tushadi (lid formasidagi <c>WriteFields</c>
    /// bilan AYNAN bir xil qoida — menejer hech narsa tanlay olmaydigan bo'sh select ko'rmasin).
    /// </summary>
    public static LeadEntryField? CleanCustom(string? label, string? kind, IEnumerable<string>? options,
        string? placeholder, bool required, int order)
    {
        var l = (label ?? "").Trim();
        if (l.Length == 0) return null;
        if (l.Length > 200) l = l[..200];
        var k = LeadFormService.NormalizeKind(kind);
        var opts = (options ?? Enumerable.Empty<string>())
            .Select(o => (o ?? "").Trim())
            .Where(o => o.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxOptions)
            .ToList();
        if (LeadFormService.NeedsOptions(k) && opts.Count == 0) k = LeadFormService.KindText;
        return new LeadEntryField
        {
            Key = "",
            Label = l,
            Kind = k,
            Options = LeadFormService.NeedsOptions(k) ? opts : new(),
            Placeholder = (placeholder ?? "").Trim(),
            Visible = true,
            Required = required,
            Order = order,
        };
    }

    // ==================== Tekshiruv (server tomonda) ====================

    /// <summary>Standart maydonning kiritilgan QIYMATI (bo'sh = to'ldirilmagan).</summary>
    public static string ValueOf(string key, Lead lead) => key switch
    {
        KeyFullName => lead.FullName,
        KeyGender => lead.Gender,
        KeyBirthDate => lead.BirthDate,
        KeyPhone => lead.Phone,
        KeyFatherFullName => lead.FatherFullName,
        KeyFatherPhone => lead.FatherPhone,
        KeyMotherFullName => lead.MotherFullName,
        KeyMotherPhone => lead.MotherPhone,
        KeySource => lead.Source,
        KeyInterestSubject => lead.InterestSubject,
        KeyDistrict => lead.DistrictId,
        KeySchool => lead.SchoolId,
        KeyNote => lead.Note ?? "",
        _ => "",
    };

    /// <summary>
    /// Majburiy STANDART maydonlar to'ldirilganmi. Xato bo'lsa — o'zbekcha jumla, aks holda null.
    ///
    /// <para>⚠️ Tekshiruv FAQAT ko'rinadigan+majburiy maydonlar bo'yicha: yashirilgan maydonning
    /// eski qiymati tegilmaydi (tahrirlashda menejer ko'rmagan ma'lumot jimgina o'chirilmasin).</para>
    /// </summary>
    public static string? ValidateStandard(IEnumerable<LeadEntryField> rules, Lead lead)
    {
        foreach (var r in rules)
        {
            if (r.Key.Length == 0 || !r.Visible || !r.Required) continue;
            if (FindStandard(r.Key) is null) continue;
            if (string.IsNullOrWhiteSpace(ValueOf(r.Key, lead)))
            {
                var label = r.Label.Length > 0 ? r.Label : FindStandard(r.Key)!.Label;
                return $"«{label}» — bu maydon to'ldirilishi shart";
            }
        }
        return null;
    }

    /// <summary>
    /// Qo'shimcha savollarning javoblarini tekshiradi va SNAPSHOT ro'yxatga aylantiradi
    /// (savol MATNI bilan — sozlama keyin o'zgarsa ham lidning tarixi buzilmasin).
    ///
    /// <para>Ommaviy formadagi bilan bir xil qoidalar: variantli savolda faqat mavjud variant,
    /// bitta tanlovli savolda birinchi javob, javob uzunligi <see cref="MaxAnswerLength"/>.</para>
    /// </summary>
    public static (List<SurveyAnswerDto>? Answers, string? Error) BuildAnswers(
        IEnumerable<LeadEntryField> customFields,
        IReadOnlyDictionary<string, List<string>>? raw)
    {
        var result = new List<SurveyAnswerDto>();
        foreach (var f in customFields)
        {
            if (f.Key.Length != 0) continue;
            var given = raw is not null && raw.TryGetValue(f.Id, out var v) && v is not null
                ? v : new List<string>();
            var vals = given.Select(x => (x ?? "").Trim())
                .Where(x => x.Length > 0)
                .Select(x => x.Length > MaxAnswerLength ? x[..MaxAnswerLength] : x)
                .ToList();

            if (LeadFormService.NeedsOptions(f.Kind))
                vals = vals.Where(x => f.Options.Contains(x)).Distinct().ToList();
            if (!LeadFormService.IsMultiple(f.Kind) && vals.Count > 1) vals = vals.Take(1).ToList();

            if (f.Required && vals.Count == 0)
                return (null, $"«{f.Label}» — bu maydon to'ldirilishi shart");
            if (vals.Count > 0) result.Add(new SurveyAnswerDto(f.Label, vals));
        }
        return (result, null);
    }
}
