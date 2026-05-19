namespace EntitlementService.Models
{
    public record EntitlementResult(
        string? RoleName,
        string EntitlementId,
        string PermissionName,
        AccessReasonType ReasonType
    );
}
