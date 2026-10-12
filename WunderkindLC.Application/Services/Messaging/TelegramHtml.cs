using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>
/// TELEGRAM HTML — the only way text with <c>parse_mode=HTML</c> is produced (SPEC P2.3, pure, <c>TelegramHtmlTests</c>).
///
/// <para>Admin templates may use a small formatting whitelist (bold / italic / underline / strike / code / link).
/// Everything else — a stray <c>&lt;</c>, an unknown tag, a bare <c>&amp;</c> — is ESCAPED as text, because Telegram
/// rejects the whole message on any markup it cannot parse. Token VALUES (student names, group names…) are
/// always escaped with <see cref="Escape"/>, so data can never inject markup («Ali &lt;b&gt;» arrives as text).</para>
/// </summary>
public static class TelegramHtml
{
    /// <summary>Telegram limit of a text message (visible characters).</summary>
    public const int MaxText = 4096;
    /// <summary>Telegram limit of a media caption (visible characters).</summary>
    public const int MaxCaption = 1024;

    /// <summary>Allowed tag (lower-case, any alias) → canonical Telegram tag.</summary>
    private static readonly Dictionary<string, string> Canonical = new(StringComparer.OrdinalIgnoreCase)
    {
        ["b"] = "b", ["strong"] = "b",
        ["i"] = "i", ["em"] = "i",
        ["u"] = "u", ["ins"] = "u",
        ["s"] = "s", ["strike"] = "s", ["del"] = "s",
        ["code"] = "code", ["pre"] = "pre",
        ["a"] = "a",
    };

    private static readonly Regex TagRx = new(
        @"\G<(?<close>/)?(?<name>[a-zA-Z][a-zA-Z0-9]*)(?<attrs>(?:\s[^<>]*)?)/?>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HrefRx = new(
        @"href\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s""'>]+))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex EntityRx = new(
        @"\G&(?:lt|gt|amp|quot|#[0-9]{1,7}|#[xX][0-9a-fA-F]{1,6});",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public const string LinkError = "Havola noto'g'ri: faqat http(s) manzil";

    public static string UnclosedError(string tag) => $"Formatlash yopilmagan: <{tag}>";

    /// <summary>
    /// Validates and normalises admin markup. Allowed tags are kept in their canonical form (<c>strong</c> → <c>b</c>,
    /// attributes dropped except a checked <c>href</c>), everything else is escaped as text. Errors (Uzbek) are
    /// returned for unclosed / mis-nested allowed tags and for links with a scheme other than http/https/tg.
    /// </summary>
    public static (string Html, IReadOnlyList<string> Errors) SanitizeTemplate(string? text)
    {
        var src = text ?? "";
        var sb = new StringBuilder(src.Length + 16);
        var errors = new List<string>();
        // Open tags: canonical name + whether it was emitted (a rejected link is escaped together with its </a>).
        var stack = new Stack<(string Name, bool Emitted)>();

        var i = 0;
        while (i < src.Length)
        {
            var c = src[i];
            if (c == '<')
            {
                var m = TagRx.Match(src, i);
                if (m.Success && Canonical.TryGetValue(m.Groups["name"].Value, out var canon))
                {
                    var closing = m.Groups["close"].Success;
                    if (!closing)
                    {
                        if (canon == "a")
                        {
                            var href = HrefRx.Match(m.Groups["attrs"].Value);
                            var url = href.Success ? WebUtility.HtmlDecode(href.Groups["v"].Value).Trim() : "";
                            if (!IsAllowedUrl(url))
                            {
                                AddOnce(errors, LinkError);
                                sb.Append(Escape(m.Value));
                                stack.Push(("a", false));
                            }
                            else
                            {
                                sb.Append("<a href=\"").Append(Escape(url)).Append("\">");
                                stack.Push(("a", true));
                            }
                        }
                        else
                        {
                            sb.Append('<').Append(canon).Append('>');
                            stack.Push((canon, true));
                        }
                    }
                    else if (stack.Count > 0 && stack.Peek().Name == canon)
                    {
                        var (_, emitted) = stack.Pop();
                        sb.Append(emitted ? $"</{canon}>" : Escape(m.Value));
                    }
                    else
                    {
                        // Mis-nested (`<b><i></b>`) or a closing tag without an opening one.
                        AddOnce(errors, UnclosedError(stack.Count > 0 ? stack.Peek().Name : canon));
                        sb.Append(Escape(m.Value));
                    }
                    i += m.Length;
                    continue;
                }
                sb.Append("&lt;");
                i++;
                continue;
            }
            if (c == '>') { sb.Append("&gt;"); i++; continue; }
            if (c == '&')
            {
                var e = EntityRx.Match(src, i);
                if (e.Success) { sb.Append(e.Value); i += e.Length; continue; }
                sb.Append("&amp;");
                i++;
                continue;
            }
            sb.Append(c);
            i++;
        }

        foreach (var (name, emitted) in stack)
        {
            if (emitted) AddOnce(errors, UnclosedError(name));
        }
        return (sb.ToString(), errors);
    }

    /// <summary>HTML-escapes a token value / plain text (<c>&amp; &lt; &gt; "</c>). <c>null</c> → <c>""</c>.</summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new StringBuilder(value.Length + 8);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    private static readonly Regex AnyTagRx = new(@"<[^<>]*>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Strips tags and decodes entities — the plain view (cabinet bell, previews, BadMarkup fallback).</summary>
    public static string ToPlain(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        return WebUtility.HtmlDecode(AnyTagRx.Replace(html, ""));
    }

    /// <summary>Characters the recipient sees (Telegram counts limits on the parsed text).</summary>
    public static int VisibleLength(string? html) => ToPlain(html).Length;

    /// <summary>A plain text cut to <paramref name="max"/> characters with «…» (surrogate pairs kept whole), escaped.</summary>
    public static string PlainCut(string? html, int max = 4000)
    {
        var plain = ToPlain(html);
        if (plain.Length <= max) return Escape(plain);
        var cut = max;
        if (cut > 0 && char.IsHighSurrogate(plain[cut - 1])) cut--;
        return Escape(plain[..cut]) + "…";
    }

    /// <summary>How a message with/without an attachment is delivered: <c>text</c> (plain message),
    /// <c>caption</c> (the text is the media caption) or <c>separate</c> (media first, then the text — the text is
    /// longer than <see cref="MaxCaption"/>).</summary>
    public static string DeliveryMode(bool hasAttachment, int visibleLength) =>
        !hasAttachment ? "text" : visibleLength <= MaxCaption ? "caption" : "separate";

    /// <summary>Only http, https and tg links are allowed (no <c>javascript:</c>, <c>data:</c>, relative …).</summary>
    public static bool IsAllowedUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var u = url.Trim();
        if (u.StartsWith("tg://", StringComparison.OrdinalIgnoreCase)) return u.Length > 5;
        return Uri.TryCreate(u, UriKind.Absolute, out var parsed)
               && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
               && !string.IsNullOrEmpty(parsed.Host);
    }

    private static void AddOnce(List<string> list, string value)
    {
        if (!list.Contains(value)) list.Add(value);
    }
}
