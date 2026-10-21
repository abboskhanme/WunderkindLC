using Microsoft.AspNetCore.Mvc.Filters;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// <c>Cache-Control: no-store</c> (+ <c>Vary: Authorization</c>) on every result of the
/// controller — including a 403 from <see cref="AdminPermAttribute"/> and automatic 400s,
/// because <see cref="IAlwaysRunResultFilter"/> runs for short-circuited results too.
///
/// <para>
/// Ported with HR topshiriqlari from WunderkindLMS (docs/SPEC-hr-tasks.md). Why: a browser can replay a
/// cached GET for the same URL after another account logged in on the same device (a shared
/// office computer) — the next person would see the previous person's tasks.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class NoStoreAttribute : Attribute, IAlwaysRunResultFilter
{
    public const string Value = "no-store, no-cache, must-revalidate, private";

    public void OnResultExecuting(ResultExecutingContext context)
    {
        var headers = context.HttpContext.Response.Headers;
        headers.CacheControl = Value;
        headers.Pragma = "no-cache";
        headers.Vary = "Authorization";
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
