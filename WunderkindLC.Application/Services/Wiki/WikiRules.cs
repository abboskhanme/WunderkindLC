using System.Text;
using System.Text.Json;

namespace WunderkindLC.Application.Services;

/// <summary>
/// BILIMLAR BAZASI (wiki) qoidalari — sof funksiyalar (testlar: <c>WikiRulesTests</c>).
/// Controller faqat IO qiladi; "nima ruxsat etilgan" savoliga javob AYNAN shu yerda.
/// Qoidalar hujjati: <c>.claude/rules/wiki.md</c>.
/// </summary>
public static class WikiRules
{
    public const int MaxTitleLength = 200;
    public const int MaxIconLength = 16;
    /// <summary>Bitta sahifaning JSON hajmi (~1 MB). Faqat matn bo'lgani uchun bu juda ko'p —
    /// chegara serverni ataylab katta so'rov bilan to'ldirishdan himoya qiladi.</summary>
    public const int MaxContentChars = 1_000_000;
    /// <summary>Bloklar ichma-ichligi chegarasi (ro'yxat ichida ro'yxat...). Rekursiv o'qish
    /// stekni to'ldirib yubormasin.</summary>
    public const int MaxDepth = 32;
    /// <summary>Bir odamning eslatmalari soni — daraxt bitta so'rovda xotiraga yuklanadi.</summary>
    public const int MaxPagesPerOwner = 2000;

    /// <summary>
    /// RUXSAT ETILGAN blok turlari — FAQAT MATN. `image`, `video`, `audio`, `file` ATAYIN yo'q:
    /// foydalanuvchi talabi ("faqat yozuv va matn") va ular tashqi manzilni sahifaga joylab
    /// (kuzatuv pikseli), yoki `/uploads` xavfsizlik qoidalarini aylanib o'tishga yo'l ochardi.
    /// Klient sxemasi (`wikiSchema.ts`) ham AYNAN shu ro'yxat — ikkalasi bir xil bo'lishi shart.
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedBlockTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "paragraph", "heading", "quote", "bulletListItem", "numberedListItem", "checkListItem",
        "toggleListItem", "codeBlock", "table", "divider",
    };

    /// <summary>Ichki (inline) matn turlari: oddiy matn va havola.</summary>
    public static readonly IReadOnlySet<string> AllowedInlineTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "text", "link",
    };

    public const string DefaultSectionTitle = "Yangi bo'lim";

    /// <summary>Sahifa sarlavhasi: bo'sh bo'lsa "Nomsiz sahifa" (yoki <paramref name="fallback"/>), juda uzun bo'lsa — xato.</summary>
    public static string? ValidateTitle(string? title, out string clean, string? fallback = null)
    {
        clean = (title ?? "").Trim();
        if (clean.Length == 0) clean = fallback ?? "Nomsiz sahifa";
        if (clean.Length > MaxTitleLength) return $"Sarlavha {MaxTitleLength} belgidan oshmasin";
        return null;
    }

    /// <summary>BO'LIM faqat yuqori darajada turadi — boshqa sahifa (yoki bo'lim) ichiga qo'yilmaydi.
    /// Oddiy sahifa esa bo'lim ichiga ham, «Mening eslatmalarim» ga ham tushadi.</summary>
    public static string? SectionPlacementError(bool isSection, string parentId) =>
        isSection && parentId.Length > 0 ? "Bo'lim faqat yuqori darajada bo'ladi — uni sahifa ichiga qo'yib bo'lmaydi" : null;

    /// <summary>Emoji ikonka — qisqa matn, aks holda bo'sh (xato emas: ikonka ikkilamchi).</summary>
    public static string NormalizeIcon(string? icon)
    {
        var s = (icon ?? "").Trim();
        return s.Length is > 0 and <= MaxIconLength ? s : "";
    }

    // =============================================================================================
    //  MATN (BlockNote JSON) — tekshirish va qidiruv uchun oddiy matnga aylantirish
    // =============================================================================================

    /// <summary>
    /// BlockNote bloklarini tekshiradi va qidiruv uchun oddiy matnni chiqaradi.
    /// Bo'sh qator — bo'sh sahifa (xato emas). Xato bo'lsa sabab matni qaytadi (400 uchun).
    /// <para>Tekshiriladi: JSON massivmi · hajm · ichma-ichlik · blok turi (faqat matn) ·
    /// ichki tur (matn/havola) · havola manzili (faqat http/https/mailto/tel — `javascript:`
    /// havolasi bosilganda CRM ichida kod ishga tushirardi).</para>
    /// </summary>
    public static string? ParseContent(string? json, out string text)
    {
        text = "";
        if (string.IsNullOrWhiteSpace(json)) return null;
        if (json.Length > MaxContentChars) return "Sahifa juda katta — uni bir nechta sahifaga bo'ling";

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxDepth * 4 }); }
        catch (JsonException) { return "Sahifa matni buzilgan (JSON o'qilmadi)"; }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return "Sahifa matni bloklar ro'yxati bo'lishi kerak";
            var sb = new StringBuilder();
            var err = WalkBlocks(doc.RootElement, sb, 0);
            if (err is not null) return err;
            text = Collapse(sb.ToString());
            return null;
        }
    }

    private static string? WalkBlocks(JsonElement blocks, StringBuilder sb, int depth)
    {
        if (depth > MaxDepth) return "Ro'yxatlar juda chuqur joylashgan";
        foreach (var block in blocks.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object) return "Sahifada noto'g'ri blok bor";
            var type = block.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
            if (!AllowedBlockTypes.Contains(type))
                return $"Bu bo'limda faqat matn yoziladi — «{type}» turidagi blok qo'shib bo'lmaydi";

            if (block.TryGetProperty("content", out var content))
            {
                var err = content.ValueKind switch
                {
                    JsonValueKind.Array => WalkInline(content, sb, depth),
                    JsonValueKind.Object => WalkTable(content, sb, depth),
                    JsonValueKind.Null or JsonValueKind.Undefined => null,
                    _ => "Sahifada noto'g'ri blok bor",
                };
                if (err is not null) return err;
            }
            sb.Append('\n');

            if (block.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            {
                var err = WalkBlocks(children, sb, depth + 1);
                if (err is not null) return err;
            }
        }
        return null;
    }

    private static string? WalkInline(JsonElement items, StringBuilder sb, int depth)
    {
        if (depth > MaxDepth) return "Ro'yxatlar juda chuqur joylashgan";
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) return "Sahifada noto'g'ri matn bor";
            var type = item.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
            if (!AllowedInlineTypes.Contains(type)) return $"«{type}» turidagi matn qo'llanmaydi";

            if (type == "link")
            {
                var href = item.TryGetProperty("href", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null;
                if (!IsSafeHref(href)) return "Havola faqat http://, https://, mailto: yoki tel: bilan boshlanishi mumkin";
                if (item.TryGetProperty("content", out var inner) && inner.ValueKind == JsonValueKind.Array)
                {
                    var err = WalkInline(inner, sb, depth + 1);
                    if (err is not null) return err;
                }
            }
            else if (item.TryGetProperty("text", out var txt) && txt.ValueKind == JsonValueKind.String)
            {
                sb.Append(txt.GetString());
            }
        }
        return null;
    }

    /// <summary>Jadval: <c>{ type: "tableContent", rows: [ { cells: [...] } ] }</c>. Katak — yoki
    /// ichki matn massivi (eski BlockNote), yoki <c>{ type: "tableCell", content: [...] }</c>.</summary>
    private static string? WalkTable(JsonElement table, StringBuilder sb, int depth)
    {
        if (!table.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array) return null;
        foreach (var row in rows.EnumerateArray())
        {
            if (!row.TryGetProperty("cells", out var cells) || cells.ValueKind != JsonValueKind.Array) continue;
            foreach (var cell in cells.EnumerateArray())
            {
                var inline = cell.ValueKind == JsonValueKind.Object && cell.TryGetProperty("content", out var c) ? c : cell;
                if (inline.ValueKind != JsonValueKind.Array) continue;
                var err = WalkInline(inline, sb, depth + 1);
                if (err is not null) return err;
                sb.Append(' ');
            }
            sb.Append('\n');
        }
        return null;
    }

    /// <summary>Havola manzili xavfsizmi: faqat http(s), mailto, tel. Nisbiy va `javascript:` — yo'q.</summary>
    public static bool IsSafeHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) return false;
        var h = href.Trim();
        return h.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || h.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || h.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || h.StartsWith("tel:", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ketma-ket bo'shliq va qatorlarni bitta bo'shliqqa qisqartiradi.</summary>
    private static string Collapse(string s)
    {
        var sb = new StringBuilder(s.Length);
        var space = false;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch)) { space = sb.Length > 0; continue; }
            if (space) { sb.Append(' '); space = false; }
            sb.Append(ch);
        }
        return sb.ToString();
    }

    // =============================================================================================
    //  DARAXT
    // =============================================================================================

    /// <summary>Sahifa va uning BARCHA ichki sahifalari id'lari (o'chirish uchun). Halqaga chidamli.</summary>
    public static HashSet<string> SubtreeIds(IEnumerable<(string Id, string ParentId)> pages, string rootId)
    {
        var byParent = pages.GroupBy(p => p.ParentId).ToDictionary(g => g.Key, g => g.Select(p => p.Id).ToList());
        var result = new HashSet<string> { rootId };
        var stack = new Stack<string>();
        stack.Push(rootId);
        while (stack.Count > 0)
            if (byParent.TryGetValue(stack.Pop(), out var kids))
                foreach (var k in kids)
                    if (result.Add(k)) stack.Push(k);
        return result;
    }

    /// <summary>
    /// Sahifani <paramref name="newParentId"/> ostiga ko'chirish mumkinmi: o'zining ostiga yoki
    /// o'z ichki sahifasi ostiga ko'chirilsa daraxt halqaga aylanardi (sahifa daraxtdan "yo'qolardi").
    /// </summary>
    public static bool CanMoveUnder(IEnumerable<(string Id, string ParentId)> pages, string pageId, string newParentId) =>
        newParentId.Length == 0 || !SubtreeIds(pages, pageId).Contains(newParentId);

    // =============================================================================================
    //  QULF
    // =============================================================================================

    /// <summary>
    /// Eslatma amalda QULFLANGANMI: o'zi yoki BIRORTA otasi qulflangan. Qulf pastga meros bo'ladi — qulflangan
    /// eslatma ichiga xodim yangi sahifa qo'shib, uni "o'zgartirib" qo'ya olmasin. `pageId = ""` — yuqori daraja
    /// (hech qachon qulflanmagan). Halqaga chidamli.
    /// </summary>
    public static bool IsLocked(IEnumerable<(string Id, string ParentId, bool Locked)> pages, string pageId)
    {
        var byId = pages.ToDictionary(p => p.Id);
        var seen = new HashSet<string>();
        var cur = pageId;
        while (cur.Length > 0 && seen.Add(cur) && byId.TryGetValue(cur, out var p))
        {
            if (p.Locked) return true;
            cur = p.ParentId;
        }
        return false;
    }

    public const string NoteLockedMessage = "Bu eslatmani rahbariyat qulflagan — uni faqat o'qish mumkin";

    // =============================================================================================
    //  SHAXSIY ESLATMALAR — kim kimnikini ko'radi
    // =============================================================================================

    /// <summary>
    /// HR menejeri qaysi xodimlarning (USER id) eslatmalarini ko'radi: HR daraxtidagi BARCHA
    /// bo'ysunuvchilari (o'zisiz, ichma-ich), faqat FAOL va akkauntli, sozlama yoqilgan bo'lsa,
    /// va lavozimlar ro'yxati bo'sh bo'lmasa — faqat o'sha lavozimdagilar.
    /// <para>⚠️ Direktor va admin bu yerdan o'tmaydi — ular hammasini ko'radi (<see cref="WikiViewer.IsLeader"/>).
    /// Oddiy xodim (`employee`) — hech kimnikini (faqat o'zinikini).</para>
    /// </summary>
    public static HashSet<string> TeamNoteOwners(
        string selfEmployeeId,
        IEnumerable<(string EmployeeId, string ManagerId)> edges,
        IEnumerable<(string Id, string UserId, string? PositionId, bool IsActive)> employees,
        bool managersSeeTeamNotes,
        IReadOnlyCollection<string> visiblePositionIds)
    {
        var result = new HashSet<string>();
        if (!managersSeeTeamNotes) return result;
        // Bo'ysunish daraxti — «Topshiriqlar» (HR) bilan BITTA manba: kim kimga biriktirilgani bir joyda sozlanadi.
        var subtree = WunderkindLC.Application.HrTasks.HrTasksService.Subtree(edges, selfEmployeeId);
        subtree.Remove(selfEmployeeId);
        foreach (var e in employees)
        {
            if (!subtree.Contains(e.Id) || !e.IsActive || !IsRealUserId(e.UserId)) continue;
            if (visiblePositionIds.Count > 0 && (e.PositionId is null || !visiblePositionIds.Contains(e.PositionId))) continue;
            result.Add(e.UserId);
        }
        return result;
    }

    /// <summary>O'chirilgan akkauntning HR qatori `deleted:&lt;id&gt;` bilan qoladi (`hr-tasks.md` §4) — u "egasi" emas.</summary>
    public static bool IsRealUserId(string? userId) =>
        !string.IsNullOrEmpty(userId) && !userId.StartsWith("deleted:", StringComparison.Ordinal);

    // =============================================================================================
    //  QIDIRUV
    // =============================================================================================

    /// <summary>Topilgan so'z atrofidagi qisqa parcha (ro'yxatda ko'rsatish uchun). Topilmasa — matn boshi.</summary>
    public static string Snippet(string text, string term, int radius = 60)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var i = string.IsNullOrEmpty(term) ? -1 : text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return text.Length <= radius * 2 ? text : text[..(radius * 2)] + "…";
        var start = Math.Max(0, i - radius);
        var end = Math.Min(text.Length, i + term.Length + radius);
        return (start > 0 ? "…" : "") + text[start..end] + (end < text.Length ? "…" : "");
    }
}
