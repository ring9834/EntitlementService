using EntitlementService.Data;
using EntitlementService.Models;
using EntitlementService.Services;
using Moq;
using Neo4j.Driver;

namespace EntitlementService.Tests
{
    public class EntitlementCheckServiceTests
    {

        private readonly Mock<INeo4jDataAccess> _mock = new();
        private readonly EntitlementCheckService _checkService;

        public EntitlementCheckServiceTests() => _checkService = new EntitlementCheckService(_mock.Object);

        private void SetupReturn(EntitlementResult accessResult)
        {
            _mock.Setup(x => x.ExecuteReadListAsync(
                    It.IsAny<string>(),
                    It.IsAny<object>(),
                    It.IsAny<Func<IRecord, EntitlementResult>>()))
                .ReturnsAsync(new List<EntitlementResult> { accessResult });
        }

        private void SetupEmptyReturn()
        {
            _mock.Setup(x => x.ExecuteReadListAsync(
                    It.IsAny<string>(),
                    It.IsAny<object>(),
                    It.IsAny<Func<IRecord, EntitlementResult>>()))
                .ReturnsAsync(new List<EntitlementResult>());
        }

        // 1. Direct Entitlement
        [Fact]
        public async Task DirectEntitlement_ReturnsAllowed()
        {
            SetupReturn(new EntitlementResult("AccountHolder", "ent-001", "view", AccessReasonType.DirectEntitlement));
            var resp = await _checkService.CheckAccessAsync(new CheckRequest("cust-100", "view", "doc-123"));
            Assert.True(resp.Allowed);
            Assert.Equal("view", resp.GrantedPermission);
            Assert.Equal("AccountHolder", resp.GrantedByRole);
            Assert.Equal(AccessReasonType.DirectEntitlement, resp.ReasonType);
        }

        // 2. Role Inheritance
        [Fact]
        public async Task RoleInheritance_ReturnsAllowed()
        {
            SetupReturn(new EntitlementResult("AccountHolder", "ent-001", "view", AccessReasonType.DirectEntitlement));
            var resp = await _checkService.CheckAccessAsync(new CheckRequest("cust-admin", "view", "doc-123"));
            Assert.True(resp.Allowed);
            Assert.Equal(AccessReasonType.DirectEntitlement, resp.ReasonType);
        }

        // 3. Overlapping Multiple Roles
        [Fact]
        public async Task MultipleRoles_EditAllowedViaEditorRole()
        {
            SetupReturn(new EntitlementResult("Editor", "ent-004", "edit", AccessReasonType.DirectEntitlement));
            var resp = await _checkService.CheckAccessAsync(new CheckRequest("cust-300", "edit", "doc-123"));
            Assert.True(resp.Allowed);
            Assert.Equal("Editor", resp.GrantedByRole);
        }

        // 4.1 Temporal – expired entitlement is denied
        [Fact]
        public async Task ExpiredEntitlement_ReturnsDenied()
        {
            SetupEmptyReturn();
            var resp = await _checkService.CheckAccessAsync(new CheckRequest("cust-temp", "view", "doc-123"));
            Assert.False(resp.Allowed);
        }

        // 4.2 Temporal – active entitlement is allowed
        [Fact]
        public async Task ActiveEntitlement_ReturnsAllowed()
        {
            SetupReturn(new EntitlementResult("TempWorker", "ent-active", "edit", AccessReasonType.DirectEntitlement));
            var resp = await _checkService.CheckAccessAsync(new CheckRequest("cust-temp", "edit", "doc-123"));
            Assert.True(resp.Allowed);
        }

        // 5. Emergency Break‑Glass
        [Fact]
        public async Task EmergencyAccess_ReturnsAllowed()
        {
            SetupReturn(new EntitlementResult(null, "eg-001", "view", AccessReasonType.EmergencyBreakGlass));
            var resp = await _checkService.CheckAccessAsync(new CheckRequest("cust-emerg", "view", "doc-123"));
            Assert.True(resp.Allowed);
            Assert.Equal(AccessReasonType.EmergencyBreakGlass, resp.ReasonType);
            Assert.Contains("eg-001", resp.Reason);
        }

        // 6. Segregation of Duties (SoD) – deny approve if user already has edit on same resource
        [Fact]
        public async Task SoDConflict_DeniesAccess()
        {
            // cust-300 already has edit via Editor role; requesting approve violates SoD
            SetupEmptyReturn();
            var resp = await _checkService.CheckAccessAsync(new CheckRequest("cust-300", "approve", "doc-123"));
            Assert.False(resp.Allowed);
        }

        // 7. Delegation / Proxy Access
        [Fact]
        public async Task Delegation_AllowsProxy()
        {
            SetupReturn(new EntitlementResult(null, "del-001", "view", AccessReasonType.Delegation));
            var resp = await _checkService.CheckAccessAsync(new CheckRequest("cust-proxy", "view", "doc-123"));
            Assert.True(resp.Allowed);
            Assert.Equal(AccessReasonType.Delegation, resp.ReasonType);
            Assert.Contains("del-001", resp.Reason);
        }

        // 8. Third‑Party Consent (PSD2)
        [Fact]
        public async Task ThirdPartyConsent_Allows()
        {
            SetupReturn(new EntitlementResult(null, "cons-001", "view", AccessReasonType.Consent));
            var resp = await _checkService.CheckAccessAsync(new CheckRequest("tp-123", "view", "dashboard-01"));
            Assert.True(resp.Allowed);
            Assert.Equal(AccessReasonType.Consent, resp.ReasonType);
            Assert.Contains("cons-001", resp.Reason);
        }

        // 9.1 ABAC – missing clearance tag
        [Fact]
        public async Task ABAC_NoClearance_Denies()
        {
            SetupEmptyReturn();
            var resp = await _checkService.CheckAccessAsync(new CheckRequest("cust-500", "view", "doc-123"));
            Assert.False(resp.Allowed);
        }

        // 9.2 Numeric Clearance - cust-500 has clearanceLevel 1, but resource requires minClearance 2 - denied even if other conditions met
        [Fact]
        public async Task InsufficientNumericClearance_Denies()
        {
            SetupEmptyReturn();
            var resp = await _checkService.CheckAccessAsync(new CheckRequest("cust-500", "view", "doc-123"));
            Assert.False(resp.Allowed);
            Assert.Contains("No matching entitlement", resp.Reason);
        }

        // 10. Resource Hierarchy – access child via parent entitlement
        [Fact]
        public async Task ResourceHierarchy_AccessChildViaParent()
        {
            SetupReturn(new EntitlementResult("AccountHolder", "ent-folder", "view", AccessReasonType.DirectEntitlement));
            var resp = await _checkService.CheckAccessAsync(new CheckRequest("cust-100", "view", "file-A1"));
            Assert.True(resp.Allowed);
        }

        // Negative: unknown subject
        [Fact]
        public async Task UnknownSubject_Denied()
        {
            SetupEmptyReturn();
            var resp = await _checkService.CheckAccessAsync(new CheckRequest("dogOlli", "view", "doc-123"));
            Assert.False(resp.Allowed);
        }

        [Fact]
        public async Task CheckAccess_NoPermission_ReturnsDenied()
        {
            // cust-300 tries approve on doc-123, but only has view on dashboard-01
            SetupEmptyReturn();
            var response = await _checkService.CheckAccessAsync(new CheckRequest("cust-300", "approve", "doc-123"));

            Assert.False(response.Allowed);
            Assert.Null(response.GrantedPermission);
            Assert.Contains("No matching entitlement", response.Reason);
        }


        [Fact]
        public async Task CheckAccess_CorrectPermissionWrongResource_ReturnsDenied()
        {
            // Arrange
            // Customer has 'view' permission on 'doc-123', but requests 'view' on 'dashboard-01'
            // The mock returns empty list because no entitlement matches both permission AND resource
            SetupEmptyReturn();

            // Act
            var response = await _checkService.CheckAccessAsync(new CheckRequest("cust-100", "view", "dashboard-01"));

            // Assert
            Assert.False(response.Allowed);
            Assert.Null(response.GrantedPermission);
            Assert.Null(response.GrantedByRole);
            Assert.Contains("No matching entitlement", response.Reason);

            // Verify the denial reason
            Assert.DoesNotContain("ent-001", response.Reason);
            Assert.DoesNotContain("AccountHolder", response.Reason);
        }

        [Fact]
        public async Task CheckAccess_PermissionNotExist_ReturnsDenied()
        {
            // The permission 'delete' does not exist at all
            SetupEmptyReturn();

            var response = await _checkService.CheckAccessAsync(new CheckRequest("cust-100", "delete", "doc-123"));

            Assert.False(response.Allowed);
        }

        [Fact]
        public async Task CheckAccess_ResourceDoesNotExist_ReturnsDenied()
        {
            // Arrange
            // Scenario: cust-100 has view permission on doc-123
            // But they request access to a resource that doesn't exist in the database
            SetupEmptyReturn();

            // Act
            var response = await _checkService.CheckAccessAsync(
                new CheckRequest("cust-100", "view", "nonexistent-resource"));

            // Assert
            Assert.False(response.Allowed);
            Assert.Null(response.GrantedPermission);
            Assert.Null(response.GrantedByRole);
            Assert.Equal("No matching entitlement, delegation, consent, or emergency grant found.", response.Reason);
        }

        [Theory]
        [InlineData("", "view", "doc-123")]
        [InlineData(" ", "view", "doc-123")]
        [InlineData("cust-100", "", "doc-123")]
        [InlineData("cust-100", "view", "")]
        public async Task BlankInputFields_ThrowArgumentException(string subject, string permission, string resource)
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _checkService.CheckAccessAsync(new CheckRequest(subject, permission, resource)));
        }
    }
}
