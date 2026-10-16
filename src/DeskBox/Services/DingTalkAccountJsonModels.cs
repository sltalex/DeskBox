using System.Text.Json.Serialization;

namespace DeskBox.Services;

internal class DingTalkIdentityRequest
{
    [JsonPropertyName("corp_id")] public string CorpId { get; set; } = string.Empty;
    [JsonPropertyName("dingtalk_user_id")] public string DingtalkUserId { get; set; } = string.Empty;
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("organization_name")] public string OrganizationName { get; set; } = string.Empty;
    [JsonPropertyName("department_name")] public string DepartmentName { get; set; } = string.Empty;
    [JsonPropertyName("avatar_url")] public string AvatarUrl { get; set; } = string.Empty;
}

internal sealed class DingTalkIdentityDto : DingTalkIdentityRequest
{
    [JsonPropertyName("provider")] public string Provider { get; set; } = string.Empty;
    [JsonPropertyName("linked_at")] public DateTimeOffset LinkedAt { get; set; }
}

internal sealed class DingTalkIdentityResponse
{
    [JsonPropertyName("identity")] public DingTalkIdentityDto? Identity { get; set; }
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DingTalkIdentityRequest))]
[JsonSerializable(typeof(DingTalkIdentityResponse))]
internal sealed partial class DingTalkAccountJsonContext : JsonSerializerContext
{
}
