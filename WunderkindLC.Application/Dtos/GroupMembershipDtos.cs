namespace WunderkindLC.Application.Dtos;

/// <summary>
/// Bitta UZAYTIRISH hodisasi (<c>StudentExtension</c>) — «Uzaytirish qaydi» oynasidagi ro'yxat uchun
/// (<c>GET classes/{id}/members/{studentId}/extensions</c>). Nomlar — yozilgan paytdagi SNAPSHOT.
/// </summary>
public record StudentExtensionDto(
    string Id, string FromGroupName, string ToGroupId, string ToGroupName, string Date,
    string ByUserName, string Note, string CreatedAt);
