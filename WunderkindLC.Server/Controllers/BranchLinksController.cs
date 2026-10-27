using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Application.Services;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Tepadagi FILIAL ALMASHTIRGICH ro'yxati — FAQAT superadmin (boshqa xodimlar o'z filialida qoladi, tanlovni ko'rmaydi).
/// Manba — `Branches:Links` (env `BRANCH_LINKS`), qoida <see cref="BranchLinks"/>.
/// </summary>
[ApiController]
[Authorize(Roles = "superadmin")]
[Route("api/admin/branch-links")]
public class BranchLinksController(IConfiguration config) : ControllerBase
{
    [HttpGet]
    public ActionResult<List<BranchLinks.Link>> Get() =>
        BranchLinks.Parse(config["Branches:Links"], Request.Host.Host);
}
