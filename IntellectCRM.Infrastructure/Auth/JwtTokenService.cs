using Microsoft.IdentityModel.Tokens;
using IntellectCRM.Domain;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace IntellectCRM.Infrastructure.Auth;

public class JwtOptions
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "IntellectCRM";
    public string Audience { get; set; } = "IntellectCRM";
    /// <summary>ESKI sozlama — agent (CTI) tokeni uchun standart muddat. Oddiy foydalanuvchi access
    /// tokeni endi <see cref="AccessMinutes"/> ishlatadi (12 soat emas, 60 daqiqa).</summary>
    public int ExpiresHours { get; set; } = 12;
    /// <summary>Oddiy foydalanuvchi ACCESS tokeni muddati (daqiqa). Qisqa: o'g'irlansa uzoq
    /// yashamasin — mijoz refresh token bilan tinch uzaytiradi.</summary>
    public int AccessMinutes { get; set; } = 60;
    /// <summary>REFRESH token muddati (kun). Shu davr ichida foydalanuvchi qayta login qilmaydi.</summary>
    public int RefreshDays { get; set; } = 30;
}

public class JwtTokenService(JwtOptions options)
{
    private readonly JwtOptions _o = options;

    /// <summary>Foydalanuvchi uchun token.</summary>
    public string CreateToken(AppUser user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
        };
        // Access token QISQA (AccessMinutes, standart 60 daq) — refresh oqimi bilan uzaytiriladi.
        return Write(claims, TimeSpan.FromMinutes(_o.AccessMinutes));
    }

    /// <summary>CTI (Local Call) Android agent-ilovasi uchun token — <see cref="AppUser"/>siz,
    /// CtiAgent bo'yicha. Rol <see cref="Roles.CtiAgent"/> (faqat mobil API + WebSocket).</summary>
    public string CreateAgentToken(string agentId, string displayName)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, agentId),
            new(ClaimTypes.NameIdentifier, agentId),
            new(ClaimTypes.Name, displayName),
            new(ClaimTypes.Role, Roles.CtiAgent),
        };
        return Write(claims);
    }

    /// <summary>
    /// CHEKLANGAN token — login yuz tasdig'ini talab qilganda beriladi. Odatdagi token bilan
    /// bir xil, lekin ichida <c>scope=face</c> claim'i bor: shunday token BILAN faqat yuz
    /// tasdiqlash oqimiga kirish mumkin (qoida — <c>FaceScopeGate</c>, darvoza — Program.cs),
    /// qolgan har qanday API so'rovi 401 oladi.
    ///
    /// <para>Muddat ATAYIN qisqa (<c>minutes</c>, standart 15 daqiqa): bu token selfi olish uchun
    /// yetadi, lekin o'g'irlansa uzoq yashamasin. Odatdagi token 12 soat.</para>
    /// </summary>
    public string CreateFaceScopedToken(AppUser user, int minutes)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
            new(FaceScopeClaimType, FaceScopeClaimValue),
        };
        return Write(claims, TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 120)));
    }

    /// <summary>Cheklangan token claim'i — qiymatlar <c>FaceScopeGate</c> bilan bir xil bo'lishi
    /// shart. (Infrastructure Application'ga referens qilmagani uchun bu yerda takrorlangan;
    /// mos kelishini <c>FaceLoginTests</c> tekshiradi.)</summary>
    public const string FaceScopeClaimType = "scope";
    public const string FaceScopeClaimValue = "face";

    private string Write(IEnumerable<Claim> claims, TimeSpan? lifetime = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _o.Issuer,
            audience: _o.Audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromHours(_o.ExpiresHours)),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
