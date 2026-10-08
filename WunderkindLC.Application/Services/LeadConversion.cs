using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// LIDNI O'QUVCHIGA AYLANTIRISH — <c>POST api/admin/leads/{id}/convert</c> ning butun mantig'i.
///
/// <para><b>Nega alohida:</b> ilgari konversiya o'quvchi yaratishning "yupqa" IKKINCHI yo'li edi
/// (audit C5): lidning o'z telefoni ko'chirilmasdi (formadan/Instagram/Meta/daraja testidan
/// kelgan o'quvchi TELEFONSIZ qolardi — to'lov/davomat SMS'i, qo'ng'iroq moslash, ota-ona
/// kabineti ishlamasdi), ota/ona maydonlari tushib qolardi, login akkaunt, <c>CreatedAt</c>,
/// audit va <c>StudentAdded</c> avto-xabari yo'q edi, guruh to'lsa ham <c>ClassName</c> o'sha
/// guruhga yozilardi, kutilayotgan sinov darsi abadiy "kutilmoqda" qolardi.</para>
///
/// <para>Endi qoidalar AYNAN ikki manbadan ko'chirilgan (ularning fayllariga tegilmagan):</para>
/// <list type="bullet">
///   <item>o'quvchi yozuvi — <c>StudentsController.AddStudent</c> (telefonlar normalizatsiyasi,
///     ASOSIY ota-ona = ota, bo'lmasa ona; login akkaunt; <c>CreatedAt</c>; "O'quvchi qo'shildi" auditi);</item>
///   <item>a'zolik — <c>ClassesController.AddMember</c> (sig'im = o'rin band qilganlar,
///     <c>Status = "trial"</c>, <c>RecordedAt</c> = bugun, qayta qo'shishda davr tarixga, "Membership" auditi,
///     <c>ClassName</c> faqat qo'shilganda).</item>
/// </list>
///
/// <para>⚠️ SaveChanges QILMAYDI — controller hamma narsani BITTA saqlashda yozadi (yarim
/// konversiya qolmasin), keyin avto-xabar va Telegram kartasi.</para>
/// </summary>
public static class LeadConversion
{
    /// <summary>409 javobidagi kod — "shu odam allaqachon o'quvchi" (klient «Mavjud o'quvchiga bog'lash»ni taklif qiladi).</summary>
    public const string DuplicateStudentCode = "duplicate_student";

    /// <summary>Amalni bajargan xodim (lid hodisasi va KPI "shartnoma" uchun).</summary>
    public sealed record Actor(string Name, string? UserId);

    /// <summary>Dublikat nomzod — 409 javobida klientga qaytadi.</summary>
    public sealed record StudentCandidate(string Id, string FullName, string ClassName, string Phone, string ParentPhone);

    /// <summary>A'zolik natijasi (<c>AddMember</c> bilan bir xil holatlar).</summary>
    public enum MembershipOutcome
    {
        /// <summary>Guruh tanlanmagan.</summary>
        None,
        /// <summary>Yangi a'zolik yaratildi (sinov).</summary>
        Added,
        /// <summary>Avval chiqqan guruhga qayta qo'shildi (eski davr tarixga ko'chdi).</summary>
        Readded,
        /// <summary>O'quvchi allaqachon shu guruhda (faqat «mavjudga bog'lash» da).</summary>
        AlreadyMember,
        /// <summary>Guruh to'lgan — a'zolik YARATILMADI, <c>ClassName</c> ham yozilmadi.</summary>
        GroupFull,
    }

    /// <summary>Konversiya natijasi.</summary>
    public sealed record Result(Student Student, bool Created, MembershipOutcome Membership)
    {
        /// <summary>A'zolik haqiqatan yaratildi/tiklandi (avto-xabar faqat shunda).</summary>
        public bool MembershipAdded => Membership is MembershipOutcome.Added or MembershipOutcome.Readded;
    }

    // ============================ Sof qoidalar ============================

    /// <summary>
    /// Qabul sanasi: bo'sh → bugun; aniq "yyyy-MM-dd" → o'zi; boshqa har qanday qiymat → <c>false</c>
    /// (controller 400 qaytaradi). <c>AddStudent</c> buzuq sanani jimgina bugunga tushiradi (import
    /// uchun), bu yerda esa forma bitta — xato sana jimgina almashib qolmasin.
    /// </summary>
    public static bool TryEnrollmentDate(string? raw, DateOnly today, out string date)
    {
        var v = (raw ?? "").Trim();
        if (v.Length == 0)
        {
            date = today.ToString("yyyy-MM-dd");
            return true;
        }
        if (v.Length == 10 && DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            date = v;
            return true;
        }
        date = "";
        return false;
    }

    /// <summary>ASOSIY ota-ona kontakti — ota (bo'lmasa ona); <c>StudentsController.DerivePrimary</c> bilan bir xil.</summary>
    public static string DerivePrimary(string? father, string? mother)
    {
        if (!string.IsNullOrWhiteSpace(father)) return father.Trim();
        if (!string.IsNullOrWhiteSpace(mother)) return mother.Trim();
        return "";
    }

    /// <summary>
    /// Liddan o'quvchi yozuvi (<c>AddStudent</c> qoidalari): lidning O'Z telefoni, ota/ona F.I.Sh va
    /// telefonlari ko'chadi, hammasi <see cref="PhoneUtil.Normalize"/> dan o'tadi; ASOSIY ota-ona
    /// kontakti (<c>ParentFullName</c>/<c>ParentPhone</c>) — ota, bo'lmasa ona. <c>ClassName</c> BO'SH —
    /// u faqat a'zolik haqiqatan yaratilganda yoziladi.
    /// </summary>
    public static Student BuildStudent(Lead lead, string enrollment, string createdAtIso)
    {
        var fatherFull = (lead.FatherFullName ?? "").Trim();
        var fatherPhone = PhoneUtil.Normalize(lead.FatherPhone ?? "");
        var motherFull = (lead.MotherFullName ?? "").Trim();
        var motherPhone = PhoneUtil.Normalize(lead.MotherPhone ?? "");
        return new Student
        {
            FullName = (lead.FullName ?? "").Trim(),
            Gender = string.IsNullOrWhiteSpace(lead.Gender) ? "male" : lead.Gender,
            BirthDate = lead.BirthDate ?? "",
            Phone = PhoneUtil.Normalize(lead.Phone ?? ""),
            FatherFullName = fatherFull,
            FatherPhone = fatherPhone,
            MotherFullName = motherFull,
            MotherPhone = motherPhone,
            ParentFullName = DerivePrimary(fatherFull, motherFull),
            ParentPhone = DerivePrimary(fatherPhone, motherPhone),
            ClassName = "",
            EnrollmentDate = enrollment,
            CreatedAt = createdAtIso,
            Balance = 0,
        };
    }

    /// <summary>
    /// Dublikat qoidasi — <see cref="StudentImportPlan"/> bilan AYNAN bir xil (qat'iy): ISM bir xil
    /// (<see cref="StudentImportPlan.NameKey"/>) VA kamida bitta TELEFON mos (o'quvchi/ota/ona/ota-ona,
    /// oxirgi 9 raqam). Faqat ism yoki faqat telefon mos kelishi YETMAYDI — aka-uka bir xil ota-ona
    /// raqamida, ismdoshlar esa boshqa-boshqa odam.
    /// </summary>
    public static bool IsSamePerson(
        string? leadName, IEnumerable<string?> leadPhones, string? studentName, IEnumerable<string?> studentPhones)
    {
        var key = StudentImportPlan.NameKey(leadName);
        if (key.Length == 0 || key != StudentImportPlan.NameKey(studentName)) return false;
        var a = StudentImportPlan.PhoneKeys(leadPhones);
        return a.Count > 0 && a.Overlaps(StudentImportPlan.PhoneKeys(studentPhones));
    }

    /// <summary>«Mavjud o'quvchiga bog'lash» rad etilganda (o'quvchi dublikat nomzodi emas).</summary>
    public const string LinkNotCandidateMessage =
        "Lidni faqat o'sha odamning o'zi bo'lgan o'quvchiga bog'lash mumkin (ism va telefon mos kelishi kerak).";

    /// <summary>
    /// <c>?linkStudentId=</c> — lidni SHU o'quvchiga bog'lash mumkinmi: u dublikat NOMZODI bo'lishi
    /// shart (<see cref="IsSamePerson"/>, arxivlanmagan — <see cref="FindDuplicateStudentsAsync"/> bilan
    /// AYNAN bir xil qoida). ⚠️ Ilgari istalgan id qabul qilinardi: lid ruxsati bor xodim lidni BEGONA
    /// o'quvchiga (masalan aka-ukasiga) yopib, KPI "shartnoma"ni yozdirar va uni o'sha guruhga qo'shardi.
    /// </summary>
    public static bool CanLinkTo(Lead lead, Student student) =>
        !student.IsArchived
        && IsSamePerson(lead.FullName, new[] { lead.Phone, lead.FatherPhone, lead.MotherPhone },
            student.FullName, new[] { student.Phone, student.FatherPhone, student.MotherPhone, student.ParentPhone });

    // ============================ Baza bilan ============================

    /// <summary>
    /// Shu lid allaqachon (ARXIVLANMAGAN) o'quvchi sifatida bormi — <see cref="IsSamePerson"/> qoidasi.
    /// Lidda telefon bo'lmasa — bo'sh (ism bo'yicha birlashtirilmaydi).
    /// </summary>
    public static async Task<List<StudentCandidate>> FindDuplicateStudentsAsync(
        IAppDbContext db, Lead lead, CancellationToken ct = default)
    {
        var leadPhones = new[] { lead.Phone, lead.FatherPhone, lead.MotherPhone };
        if (StudentImportPlan.PhoneKeys(leadPhones).Count == 0) return new();
        var key = StudentImportPlan.NameKey(lead.FullName);
        if (key.Length == 0) return new();

        // `NameKey` normalizatsiyasi SQL'ga tarjima bo'lmaydi — faqat kerakli ustunlar o'qiladi
        // (`StudentsController.CheckPhones` bilan bir xil yondashuv).
        var rows = await db.Students.AsNoTracking()
            .Where(s => !s.IsArchived)
            .Select(s => new
            {
                s.Id, s.FullName, s.ClassName, s.Phone, s.FatherPhone, s.MotherPhone, s.ParentPhone,
            })
            .ToListAsync(ct);
        return rows
            .Where(s => IsSamePerson(lead.FullName, leadPhones, s.FullName,
                new[] { s.Phone, s.FatherPhone, s.MotherPhone, s.ParentPhone }))
            .Select(s => new StudentCandidate(s.Id, s.FullName ?? "", s.ClassName ?? "", s.Phone ?? "", s.ParentPhone ?? ""))
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Guruhda O'RIN band qilganlar soni — <c>AddMember</c> dagi sig'im tekshiruvi
    /// (<see cref="MembershipLifecycle.OccupiesSeatExpr"/>: active + trial, muzlatilganlar SANALMAYDI).
    /// ⚠️ ARXIVDAGI o'quvchi sanalmaydi (arxiv = o'quvchi ketdi; eski bazada arxivdagi o'quvchining
    /// a'zoligi tirik qolgan bo'lishi mumkin — u bo'sh o'rinni egallab turmasin). Ta'rif YAGONA —
    /// <see cref="MembershipLifecycle.SeatHolders"/>.
    /// </summary>
    public static Task<int> SeatsTakenAsync(IAppDbContext db, string groupId, CancellationToken ct = default) =>
        GroupSeats.OccupiedAsync(db, groupId, ct);

    /// <summary>
    /// O'quvchini guruhga qo'shadi — <c>ClassesController.AddMember</c> qoidalari: allaqachon faol
    /// a'zo → <see cref="MembershipOutcome.AlreadyMember"/>; sig'im to'lgan → <see cref="MembershipOutcome.GroupFull"/>
    /// (hech narsa yozilmaydi); avval chiqqan bo'lsa — eski davr tarixga ko'chib, yana SINOV holatida
    /// tiklanadi; aks holda yangi SINOV a'zoligi. <c>RecordedAt</c> — HAQIQIY bugungi sana (jurnal
    /// avto-"keldi" qoidasi orqaga sanalgan darslarga qo'llanmasin). "Membership" auditi yoziladi.
    /// </summary>
    public static async Task<MembershipOutcome> AddMembershipAsync(
        IAppDbContext db, AuditService audit, Student s, Group cls, string joinedAt, string todayIso,
        CancellationToken ct = default)
    {
        var existing = await db.StudentGroups
            .FirstOrDefaultAsync(sg => sg.StudentId == s.Id && sg.GroupId == cls.Id, ct);
        if (existing is { IsActive: true }) return MembershipOutcome.AlreadyMember;

        if (cls.Capacity > 0 && await SeatsTakenAsync(db, cls.Id, ct) >= cls.Capacity)
            return MembershipOutcome.GroupFull;

        if (existing is not null)
        {
            // Joriy faol davr tarixga ko'chiriladi — pastda ActivatedAt/FrozenAt/LeftAt TOZALANADI
            // (`membership-periods.md` §3: ClosePeriod `LeftAt = null` dan OLDIN).
            MembershipLifecycle.ClosePeriod(existing, todayIso);
            existing.IsActive = true;
            existing.LeftAt = null;
            existing.JoinedAt = joinedAt;
            existing.Status = "trial";
            existing.ActivatedAt = string.Empty;
            existing.FrozenAt = string.Empty;
            existing.YearFreeze = false;
            existing.RecordedAt = todayIso;
        }
        else
        {
            db.StudentGroups.Add(new StudentGroup
            {
                StudentId = s.Id, GroupId = cls.Id, JoinedAt = joinedAt, IsActive = true,
                Status = "trial", RecordedAt = todayIso,
            });
        }
        // Eski (single-class) ko'rinishlar uchun asosiy guruh nomi — FAQAT a'zolik yaratilganda.
        if (string.IsNullOrWhiteSpace(s.ClassName)) s.ClassName = cls.Name;

        audit.Record("Membership", $"{cls.Id}:{s.Id}", "create",
            $"Guruhga qo'shildi: {s.FullName} → {cls.Name} (sinov, {joinedAt})"
            + (existing is not null ? " — qayta qo'shildi" : "") + " — liddan aylantirildi",
            studentId: s.Id);
        return existing is not null ? MembershipOutcome.Readded : MembershipOutcome.Added;
    }

    /// <summary>
    /// Lidning KUTILAYOTGAN sinov darslari: sanasi o'tgan yoki BUGUN bo'lganlari → «qoldi»
    /// (lid o'quvchi bo'ldi — ya'ni sinovga kelgan va qolgan), kelgan sana = sinov kuni (KPI
    /// "sinovga keldi" o'sha oyga tushadi). KELAJAKDAGI sinov tegilmaydi: u hali bo'lmagan va
    /// odam kelish-kelmasligi noma'lum (lid yopiq — eslatma SMS'i baribir ketmaydi).
    /// </summary>
    /// <returns>Yopilgan sinovlar soni.</returns>
    public static async Task<int> ResolvePendingTrialsAsync(
        IAppDbContext db, string leadId, string todayIso, Actor actor, string nowIso, CancellationToken ct = default)
    {
        var pending = await db.TrialLessons
            .Where(t => t.LeadId == leadId && t.Result == "pending")
            .ToListAsync(ct);
        var n = 0;
        foreach (var t in pending)
        {
            var at = t.ScheduledAt ?? "";
            var day = at.Length >= 10 ? at[..10] : "";
            if (!DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) || string.CompareOrdinal(day, todayIso) > 0)
                continue;
            t.Result = "stayed";
            if (string.IsNullOrWhiteSpace(t.AttendedAt)) t.AttendedAt = day;
            db.LeadEvents.Add(new LeadEvent
            {
                LeadId = leadId, Type = "trial", Text = "Sinov darsi natijasi: qoldi (o'quvchiga aylantirildi)",
                ActorName = actor.Name, ActorUserId = actor.UserId, CreatedAt = nowIso,
            });
            n++;
        }
        return n;
    }

    /// <summary>
    /// YANGI o'quvchi yaratib lidni yopadi. <paramref name="createAccount"/> — F.I.Sh → yangi
    /// "student" akkauntining id'si (<c>AccountFactory.CreateAccountFor</c>; Application undan
    /// bexabar, shuning uchun tashqaridan beriladi).
    /// </summary>
    public static async Task<Result> ConvertAsync(
        IAppDbContext db, AuditService audit, Lead lead, Group? group, string enrollment,
        Func<string, string> createAccount, Actor actor, CancellationToken ct = default)
    {
        var todayIso = AppClock.Today.ToString("yyyy-MM-dd");
        var nowIso = AppClock.Iso();

        var student = BuildStudent(lead, enrollment, nowIso);
        db.Students.Add(student);
        // O'quvchiga "student" rolli tizim akkaunti (AddStudent bilan bir xil).
        student.UserId = createAccount(student.FullName);

        audit.Record(AuditService.EntityStudent, student.Id, "create",
            $"O'quvchi qo'shildi ({student.FullName}) — liddan aylantirildi",
            after: AuditService.StudentProfileSnapshot(student), studentId: student.Id);

        var membership = group is null
            ? MembershipOutcome.None
            : await AddMembershipAsync(db, audit, student, group, enrollment, todayIso, ct);

        await CloseLeadAsync(db, audit, lead, student, group, membership, created: true, actor, todayIso, nowIso, ct);
        return new Result(student, true, membership);
    }

    /// <summary>
    /// «MAVJUD O'QUVCHIGA BOG'LASH» — dublikat topilganda: yangi o'quvchi YARATILMAYDI, lid shu
    /// o'quvchiga yopiladi (<c>ConvertedStudentId</c>), guruh tanlangan bo'lsa unga qo'shiladi
    /// (allaqachon a'zo bo'lsa — tegilmaydi).
    /// </summary>
    public static async Task<Result> LinkAsync(
        IAppDbContext db, AuditService audit, Lead lead, Student student, Group? group, string joinedAt,
        Actor actor, CancellationToken ct = default)
    {
        var todayIso = AppClock.Today.ToString("yyyy-MM-dd");
        var nowIso = AppClock.Iso();
        var membership = group is null
            ? MembershipOutcome.None
            : await AddMembershipAsync(db, audit, student, group, joinedAt, todayIso, ct);
        await CloseLeadAsync(db, audit, lead, student, group, membership, created: false, actor, todayIso, nowIso, ct);
        return new Result(student, false, membership);
    }

    /// <summary>
    /// Lidni yopadi: <c>ConvertedStudentId</c>, KPI "shartnoma" (<c>ClosedByUserId</c>/<c>ClosedAt</c>),
    /// mas'ul bo'sh bo'lsa — yopgan xodim; <c>convert</c> hodisasi (voronka va menejer kesimi
    /// shundan), kutilayotgan sinovlar va "Lead" auditi.
    /// </summary>
    private static async Task CloseLeadAsync(
        IAppDbContext db, AuditService audit, Lead lead, Student student, Group? group,
        MembershipOutcome membership, bool created, Actor actor, string todayIso, string nowIso, CancellationToken ct)
    {
        lead.ConvertedStudentId = student.Id;
        // SHARTNOMA IMZOLANDI — kiruvchi admin uchun "shartnoma" hodisasining YAGONA manbai
        // (`KPI-SPEC.md` §8). Sana ATAYIN "yyyy-MM-dd" — KPI oy bo'yicha guruhlaydi.
        lead.ClosedByUserId = actor.UserId;
        lead.ClosedAt = todayIso;
        if (string.IsNullOrWhiteSpace(lead.AssigneeUserId)) lead.AssigneeUserId = actor.UserId;

        var groupText = group is null ? "" : membership switch
        {
            MembershipOutcome.GroupFull => $" — guruh to'lgan, qo'shilmadi: {group.Name}",
            MembershipOutcome.AlreadyMember => $" — allaqachon guruhda: {group.Name}",
            _ => $" — guruh: {group.Name}",
        };
        var head = created
            ? $"O'quvchiga aylantirildi ({lead.FullName})"
            : $"Mavjud o'quvchiga bog'landi ({student.FullName})";
        db.LeadEvents.Add(new LeadEvent
        {
            LeadId = lead.Id, Type = "convert", Text = head + groupText,
            ActorName = actor.Name, ActorUserId = actor.UserId, CreatedAt = nowIso,
        });

        await ResolvePendingTrialsAsync(db, lead.Id, todayIso, actor, nowIso, ct);

        audit.Record(LeadLoss.AuditEntity, lead.Id, "update",
            (created ? $"Lid o'quvchiga aylantirildi: {lead.FullName}" : $"Lid mavjud o'quvchiga bog'landi: {lead.FullName} → {student.FullName}")
            + groupText,
            studentId: student.Id);
    }
}
