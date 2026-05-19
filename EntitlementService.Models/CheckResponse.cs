namespace EntitlementService.Models
{
    public record CheckResponse(
        bool Allowed, 
        string Reason, 
        string? GrantedPermission = null, 
        string? GrantedByRole = null, 
        AccessReasonType? ReasonType = null
    );
}
