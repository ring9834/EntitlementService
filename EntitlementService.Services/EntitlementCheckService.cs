using EntitlementService.Data;
using EntitlementService.Models;
using Neo4j.Driver;

namespace EntitlementService.Services
{
    public sealed class EntitlementCheckService : IEntitlementCheckService
    {
        // Cypher query
        // Four UNION ALL branches are evaluated in priority order; only the highest-priority match (LIMIT 1) is returned.
        private static readonly string AccessQuery = """
            WITH $subject AS customerId,
                 $resource  AS resourceId,
                 $permission AS permName
 
            CALL (customerId, resourceId, permName) {
 
                // PATH 1 — Role-based (direct + inherited)
                // Checks: temporal validity, resource hierarchy, ABAC tags, SoD
                MATCH (c:Customer {id: customerId})-[:HAS_PARTY_ROLE]->(pr:PartyRole)
                MATCH (pr)-[:INHERITS*0..]->(role:PartyRole)
                MATCH (role)-[:GRANTS]->(e:Entitlement)
                WHERE (e.startDate IS NULL OR e.startDate <= datetime())
                  AND (e.endDate   IS NULL OR e.endDate   >= datetime())
                MATCH (e)-[:ALLOWS]->(p:Permission {name: permName})
                MATCH (e)-[:ON]->(r:Resource)
                MATCH (target:Resource {id: resourceId})
                WHERE (r = target
                   OR EXISTS { MATCH (r)-[:CHILD_OF*0..]->(target) }
                   OR EXISTS { MATCH (target)-[:CHILD_OF*0..]->(r) })
                  AND (r.minClearance IS NULL OR c.clearanceLevel >= r.minClearance)
 
                OPTIONAL MATCH (r)-[:TAGGED_WITH]->(tag:ComplianceTag)
                WITH c, role, e, p, r, target, collect(DISTINCT tag) AS tags
                WHERE all(t IN tags WHERE EXISTS { MATCH (c)-[:CLEARANCE_FOR]->(t) })
 
                WITH c, role, e, p, target,
                     CASE WHEN EXISTS {
                         MATCH (c)-[:HAS_PARTY_ROLE]->(:PartyRole)
                               -[:INHERITS*0..]->(role2:PartyRole)
                               -[:GRANTS]->(e2:Entitlement)
                               -[:ALLOWS]->(p2:Permission)
                         WHERE e2 <> e
                           AND (e2.startDate IS NULL OR e2.startDate <= datetime())
                           AND (e2.endDate   IS NULL OR e2.endDate   >= datetime())
                           AND ((p)-[:CONFLICTS_WITH]->(p2) OR (p2)-[:CONFLICTS_WITH]->(p))
                         MATCH (e2)-[:ON]->(r2:Resource)
                         WHERE r2 = target
                            OR EXISTS { MATCH (r2)-[:CHILD_OF*0..]->(target) }
                            OR EXISTS { MATCH (target)-[:CHILD_OF*0..]->(r2) }
                     } THEN 1 ELSE 0 END AS sodConflict
                WHERE sodConflict = 0
 
                RETURN role.name           AS RoleName,
                       e.id                AS EntitlementId,
                       p.name              AS PermissionName,
                       'DirectEntitlement' AS ReasonType,
                       1                   AS priority
 
                UNION ALL
 
                // PATH 2 — Emergency break-glass
                MATCH (c:Customer {id: customerId})-[:HAS_EMERGENCY_ACCESS]->(eg:EmergencyGrant)
                WHERE eg.expiresAt > datetime()
                MATCH (eg)-[:ALLOWS]->(p:Permission {name: permName})
                MATCH (eg)-[:ON]->(res:Resource {id: resourceId})
 
                RETURN null                  AS RoleName,
                       eg.id                 AS EntitlementId,
                       p.name                AS PermissionName,
                       'EmergencyBreakGlass' AS ReasonType,
                       2                     AS priority
 
                UNION ALL
 
                // PATH 3 — Delegation (proxy access)
                MATCH (delegator:Customer)-[:DELEGATES]->(d:Delegation)-[:TO]->(c:Customer {id: customerId})
                WHERE d.validUntil > datetime()
                MATCH (d)-[:ALLOWS_DELEGATED]->(perm:Permission {name: permName})
                MATCH (d)-[:ON_DELEGATED]->(res:Resource {id: resourceId})
 
                RETURN null         AS RoleName,
                       d.id         AS EntitlementId,
                       perm.name    AS PermissionName,
                       'Delegation' AS ReasonType,
                       3            AS priority
 
                UNION ALL
 
                // PATH 4 — Third-party consent (PSD2 / Open Banking)
                MATCH (owner:Customer)-[:GIVES_CONSENT]->(consent:Consent)
                WHERE consent.thirdPartyId = customerId
                  AND consent.validUntil   > datetime()
                MATCH (consent)-[:ALLOWS]->(perm:Permission {name: permName})
                MATCH (consent)-[:ON]->(res:Resource {id: resourceId})
 
                RETURN null        AS RoleName,
                       consent.id  AS EntitlementId,
                       perm.name   AS PermissionName,
                       'Consent'   AS ReasonType,
                       4           AS priority
            }
 
            ORDER BY priority ASC
            LIMIT 1
 
            RETURN RoleName, EntitlementId, PermissionName, ReasonType
            """;

        private const string DeniedMessage =
            "No matching entitlement, delegation, consent, or emergency grant found.";

        private readonly INeo4jDataAccess _dataAccess;

        public EntitlementCheckService(INeo4jDataAccess dataAccess) =>
            _dataAccess = dataAccess;

        /// <summary>
        /// Determines whether <paramref name="request.Subject"/> holds
        /// <paramref name="request.Permission"/> on <paramref name="request.Resource"/>.
        /// </summary>
        /// <remarks>
        /// Evaluation priority:
        /// 1. Role-based entitlements (inheritance, temporal, ABAC, SoD, resource hierarchy)
        /// 2. Emergency break-glass grants
        /// 3. Delegations
        /// 4. Third-party consents (PSD2 / Open Banking)
        /// The first matching path wins; absence of any match returns access-denied.
        /// </remarks>
        public async Task<CheckResponse> CheckAccessAsync(CheckRequest request)
        {
            var results = await _dataAccess.ExecuteReadListAsync(
                AccessQuery,
                new { subject = request.Subject, permission = request.Permission, resource = request.Resource },
                record => new EntitlementResult(
                    record["RoleName"].As<string?>(),
                    record["EntitlementId"].As<string>(),
                    record["PermissionName"].As<string>(),
                    Enum.Parse<AccessReasonType>(record["ReasonType"].As<string>(), ignoreCase: true)
                ));

            var match = results.FirstOrDefault();

            return match is null
                ? new CheckResponse(false, DeniedMessage)
                : new CheckResponse(
                    true,
                    BuildReason(match, request.Resource),
                    match.PermissionName,
                    match.RoleName,
                    match.ReasonType);
        }

        // Helpers
        private static string BuildReason(EntitlementResult r, string resource) =>
            r.ReasonType switch
            {
                AccessReasonType.DirectEntitlement =>
                    $"Entitlement '{r.EntitlementId}' via role '{r.RoleName}' grants '{r.PermissionName}' on '{resource}'.",
                AccessReasonType.EmergencyBreakGlass =>
                    $"Emergency access grant '{r.EntitlementId}' allows '{r.PermissionName}' on '{resource}'.",
                AccessReasonType.Delegation =>
                    $"Delegation '{r.EntitlementId}' grants proxy '{r.PermissionName}' on '{resource}'.",
                AccessReasonType.Consent =>
                    $"Third-party consent '{r.EntitlementId}' allows '{r.PermissionName}' on '{resource}'.",
                _ => "Access granted."
            };
    }
}
