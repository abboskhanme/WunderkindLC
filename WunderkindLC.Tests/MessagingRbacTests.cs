using System.Text.RegularExpressions;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// CRM upgrade Phase 2 — RBAC of the new endpoints (P2.11 <c>MessagingRbacTests</c>). The test project does not
/// reference the Server, so the gates are read from the controller SOURCE (same approach as
/// <see cref="SensitiveReadPermTests"/>): a removed gate or an <c>[AllowAnonymous]</c> turns the test red.
/// <para><c>AdminPerm</c> semantics: staff without the key → 403 on POST/PUT/DELETE (create/edit/delete) and — with
/// <c>ReadRequiresPerm = true</c> — on every GET as well; teacher/student/parent roles → 403 always.</para>
/// </summary>
public class MessagingRbacTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
                dir = dir.Parent;
            Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
            return dir!.FullName;
        }
    }

    private static string Source(string file)
    {
        var path = Path.Combine(RepoRoot, "WunderkindLC.Server", "Controllers", file);
        Assert.True(File.Exists(path), $"Controller fayli topilmadi: {path}");
        return File.ReadAllText(path);
    }

    private static string ClassHeader(string source, string className)
    {
        var m = Regex.Match(source, $@"public (partial )?class {className}\b");
        Assert.True(m.Success, $"{className} topilmadi");
        return source[..m.Index];
    }

    [Fact]
    public void MessagingController_hamma_endpoint_messages_broadcast_va_oqish_ham_darvozada()
    {
        var src = Source("MessagingController.cs");
        var header = ClassHeader(src, "MessagingController");
        Assert.Matches(new Regex(@"\[AdminPerm\(""messages\.broadcast"",\s*ReadRequiresPerm\s*=\s*true\)\]"), header);
        Assert.Contains("[Authorize]", header);
        Assert.Contains("[Route(\"api/admin/messaging\")]", header);
        Assert.DoesNotContain("AllowAnonymous", src);
        // No method-level AdminPerm (it would REPLACE the class gate — permissions.md §4).
        Assert.Single(Regex.Matches(src, @"\[AdminPerm\("));
        // Preview / count / campaigns are POST → `create` action of the key; nothing is GET-only writable.
        foreach (var route in new[] { "preview", "audience/count", "attachments", "campaigns", "campaigns/{id}/cancel", "templates" })
            Assert.Contains($"[HttpPost(\"{route}\")]", src);
        foreach (var route in new[] { "campaigns", "campaigns/{id}", "campaigns/{id}/deliveries", "auto-stats", "debtors",
                                      "templates", "attachments/{id}/content" })
            Assert.Contains($"[HttpGet(\"{route}\")]", src);
    }

    [Fact]
    public void Biriktirma_fayli_statik_emas_ruxsatli_endpointdan_no_store_bilan()
    {
        var src = Source("MessagingController.cs");
        Assert.Contains("\"private, no-store\"", src);
        Assert.DoesNotContain("FileUrl =", src.Replace("a.FileUrl", ""));   // the storage path is never returned
    }

    [Fact]
    public void Yutuqlar_oquvchilar_ruxsati_bilan_oqish_ham()
    {
        var src = Source("StudentAchievementsController.cs");
        var header = ClassHeader(src, "StudentAchievementsController");
        Assert.Matches(new Regex(@"\[AdminPerm\(""students\.list"",\s*ReadRequiresPerm\s*=\s*true\)\]"), header);
        Assert.Contains("[Route(\"api/admin/achievements\")]", header);
        // Own photo upload under the same gate (POST → create) — no messaging permission needed; photos only.
        Assert.Contains("[HttpPost(\"photo\")]", src);
        Assert.Contains("Yutuq uchun faqat rasm (jpg, png) biriktiriladi", src);
        Assert.DoesNotContain("AllowAnonymous", src);
        Assert.Single(Regex.Matches(src, @"\[AdminPerm\("));
    }

    [Fact]
    public void Kabinet_yutuqlari_faqat_oziniki()
    {
        var src = Source("StudentPortalController.Achievements.cs");
        Assert.DoesNotContain("AllowAnonymous", src);
        // Ownership through TargetAsync in BOTH endpoints; the photo also checks the student id.
        Assert.Equal(2, Regex.Matches(src, @"await TargetAsync\(studentId\)").Count);
        Assert.Contains("x.StudentId == s.Id", src);
        var portal = ClassHeader(Source("StudentPortalController.cs"), "StudentPortalController");
        Assert.Contains("[Authorize(Roles = \"student,parent,admin\")]", portal);
    }

    [Fact]
    public void Oquvchi_telegram_xabarlari_ikki_ruxsatdan_biri_bilan_va_oqish_darvozada()
    {
        var src = Source("StudentsController.Telegram.cs");
        Assert.Matches(new Regex(
            @"\[AdminPerm\(""students\.list"",\s*""messages\.broadcast"",\s*ReadRequiresPerm\s*=\s*true\)\]\s*public async Task<ActionResult<List<StudentDeliveryDto>>> TelegramDeliveries"),
            src);
        // The relation edit is a PUT under the class gate `students.list` (PUT → edit).
        Assert.Contains("[HttpPut(\"{id}/telegram-links/{regId}\")]", src);
        Assert.DoesNotContain("AllowAnonymous", src);
        Assert.Contains("[AdminPerm(\"students.list\")]", ClassHeader(Source("StudentsController.cs"), "StudentsController"));
    }

    [Fact]
    public void Avto_xabarlar_ruxsati_ozgarmadi_va_audit_yoziladi()
    {
        var src = Source("AutoMessagesController.cs");
        Assert.Contains("[AdminPerm(\"messages.broadcast\")]", ClassHeader(src, "AutoMessagesController"));
        Assert.Contains("audit.Record(EntityType, rule.Id, \"create\"", src);
        Assert.Contains("audit.Record(EntityType, rule.Id, \"update\"", src);
        Assert.Contains("audit.Record(EntityType, rule.Id, \"delete\"", src);
    }

    [Fact]
    public void Bot_mack_tugmasini_umumiy_javobdan_OLDIN_ushlaydi()
    {
        var path = Path.Combine(RepoRoot, "WunderkindLC.Application", "Services", "TelegramBotService.cs");
        var src = File.ReadAllText(path);
        var mack = src.IndexOf("MessageAckService.CallbackPrefix", StringComparison.Ordinal);
        var generic = src.IndexOf("if (cbId.Length > 0) await telegram.AnswerCallbackAsync(cbId, ct: ct);", StringComparison.Ordinal);
        Assert.True(mack > 0 && generic > 0 && mack < generic, "mack: must be handled before the generic empty answer");
    }

    [Fact]
    public void Xabar_biriktirmalari_papkasi_statik_yopiq()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot, "WunderkindLC.Server", "Program.cs"));
        Assert.Contains("MessageAttachmentStore.FolderName", program);
        Assert.Matches(new Regex(@"\[\.\. certificateFolders, \.\. faceFolders, \.\. messagingFolders\]"), program);
        Assert.Matches(new Regex(@"\[\.\. faceFolders, \.\. messagingFolders\]"), program);
    }
}
