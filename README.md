# Graph-Backed Entitlement Check Service
This is an implementation of a centralised entitlement engine following BIAN standards using **.NET 10** and **Neo4j**.

Recognizing the complexity of real-world banking authorization, I incorporated a range of BIAN-compliant patterns, from role inheritance to regulatory compliance checks, into the graph model. This both validates the model's comprehensiveness and supplies diverse test data to rigorously cover positive, negative, and boundary conditions.

## Features
- Supports 10 of banking identity and relationship scenarios based on BIAN standards:

  :point_right: 1. Direct Entitlement
  
  A customer holds a role that grants a specific permission on a resource. The query follows the BIAN path: `Customer -> PartyRole -> Entitlement -> Permission + Resource`.

  :point_right: 2. Role Inheritance

  Roles can inherit from others, for example, Admin inherits AccountHolder. The query traverses variable‑length paths (`[:INHERITS*0..]`) to automatically include all parent‑role entitlements without additional application logic.

  :point_right: 3. Overlapping Multiple Roles

  A customer may hold several roles simultaneously. The check evaluates all roles independently; access is granted if **any** role provides the required entitlement.

  :point_right: 4. Temporal (Time‑boxed) Entitlements.

  Entitlements carry optional `startDate` and `endDate` properties. The query only considers entitlements where `startDate <= datetime() AND endDate >= datetime()`, enabling time‑bound access such as contract workers, limited promotions.

  :point_right: 5. Emergency / Break‑Glass Access.

  Special emergency grants allow temporary access that bypasses normal role‑based controls. A separate `EmergencyGrant` node connected via `[:HAS_EMERGENCY_ACCESS]` holds its own `permission`, `resource`, and `expiresAt` fields. This path is evaluated right after direct entitlements.

  :point_right: 6. Segregation of Duties (SoD).

  Conflicting permissions such as `edit` and `approve` are linked with a `[:CONFLICTS_WITH]` edge. During an entitlement check, the query first identifies any already‑held conflicting permission on the same resource and denies access if a conflict exists.

  :point_right: 7. Delegated / Proxy Access.

  A customer can delegate a specific permission on a specific resource to another customer without transferring the entire role. A `Delegation` node connects the delegator to the proxy via `[:TO]`, and scopes the grant with `[:ALLOWS_DELEGATED]` and `[:ON_DELEGATED]` relationships, with a `validUntil` timestamp

  :point_right: 8. Third‑Party Consent (PSD2 / Open Banking).

  A data owner gives consent to a third‑party identifier (not a full customer node) through a `Consent` node. The `thirdPartyId` property is matched directly against the check subject, enabling open‑banking scenarios without creating internal customer records for every third party.

  :point_right: 9. Attribute‑Based Access Control (ABAC).

  Resources can be tagged with compliance labels such as GDPR, SOX via `[:TAGGED_WITH]->(:ComplianceTag)`. Customers must hold a `[:CLEARANCE_FOR]` relationship to each required tag. The query collects all tags on the target resource and verifies the customer has clearance for every one before granting access.

  Besides compliance labels, we design to make Resources have a property minClearance and Customers have clearanceLevel. Only customers with the value of clearanceLevel greater or equal to the value of minClearance of one resource can access to the resource.

  :point_right: 10. Resource Hierarchy / Data Scoping.

  Resources form a tree through `[:CHILD_OF]` relationships. If a user holds an entitlement on a parent resource such as a folder, the query expands the resource match to include all descendants—allowing access to child files without explicit individual grants.

  To implement and include all the above scenarios, when modeling, besides the nodes for Identity, PartyRole, Entitlement, Permission, and Resource, I also added the nodes of ComplianceTag, EmergencyGrant, Delegation, Consent, supporting resource hierarchy, overlapping roles, time-boxed conditions, segregation of duties, access delegation, thir-dparty consent, and clearance-based access control.

  For more complex banking business scenarios, we can extend our design in the seed so that we can add relevant nodes and relationships, and revise our Cypher query conresponsively.

- REST endpoint: `POST /api/entitlement/check`.

- Seed endpoint (`POST /api/seed`) with duplicate‑safe `MERGE` statements.

- Unit tests cover all scenarios plus edge cases (unknown subject, no permission, no resource).

## Setup
1. Install .NET 10 SDK and Neo4j v5.23 or later.
2. Update `appsettings.json` with your Neo4j credentials.
3. Run `dotnet run` in `src/EntitlementService.Api`.
4. Call `POST /api/seed` to load demo data, or run the script in Script.cypher file on Neo4j directly.
5. Use the `/api/entitlement/check` endpoint to check the real response results. Requests and responses data should be as follows:
  (1) Direct Entitlement (Allow)
      Requests (please change port 7266 in the following commands into the real port on your machine):
```sh
      curl -X POST https://localhost:7266/api/entitlement/check \
        -H "Content-Type: application/json" \
        -d '{"subject":"cust-100","permission":"view","resource":"doc-123"}'
```
      Response:
```sh
      {
        "allowed": true,
        "reason": "Entitlement 'ent-001' via role 'AccountHolder' grants 'view' on 'doc-123'.",
        "grantedPermission": "view",
        "grantedByRole": "AccountHolder",
        "reasonType": 0
      }
```
  (2) Role Inheritance (Allow)
      Requests:
```sh
      curl -X POST https://localhost:7266/api/entitlement/check \
        -H "Content-Type: application/json" \
        -d '{"subject": "cust-admin", "permission": "view", "resource": "doc-123"}'
```

      Response:
```sh
      {
        "allowed": true,
        "reason": "Entitlement 'ent-001' via role 'AccountHolder' grants 'view' on 'doc-123'.",
        "grantedPermission": "view",
        "grantedByRole": "AccountHolder",
        "reasonType": 0
      }
```
  (3) Overlapping Multiple Roles (Allow)
      Requests:
```sh
      curl -X POST https://localhost:7266/api/entitlement/check \
        -H "Content-Type: application/json" \
        -d '{"subject": "cust-300", "permission": "edit", "resource": "doc-123"}'
```
      Response:
      {
```sh
        "allowed": true,
        "reason": "Entitlement 'ent-004' via role 'Editor' grants 'edit' on 'doc-123'.",
        "grantedPermission": "edit",
        "grantedByRole": "Editor",
        "reasonType": 0
      }
```
  (4.1) Temporal - Expired Entitlement (Deny)
      Requests:
```sh
      curl -X POST https://localhost:7266/api/entitlement/check \
        -H "Content-Type: application/json" \
        -d '{"subject": "cust-temp", "permission": "view", "resource": "doc-123"}'
```
      Response:
```sh
      {
        "allowed": false,
        "reason": "No matching entitlement, delegation, consent, or emergency grant found.",
        "grantedPermission": null,
        "grantedByRole": null,
        "reasonType": null
      }
```
  (4.2) Temporal - Expired Entitlement (Allow)
      Requests:
```sh
      curl -X POST https://localhost:7266/api/entitlement/check \
        -H "Content-Type: application/json" \
        -d '{"subject": "cust-temp", "permission": "edit", "resource": "doc-123"}'
```
      Response:
```sh
      {
        "allowed": true,
        "reason": "Entitlement 'ent-active' via role 'TempWorker' grants 'edit' on 'doc-123'.",
        "grantedPermission": "edit",
        "grantedByRole": "Editor",
        "reasonType": 0
      }
```
  (5) Emergency Break‑Glass (Allow)
      Requests:
```sh
      curl -X POST https://localhost:7266/api/entitlement/check \
        -H "Content-Type: application/json" \
        -d '{"subject": "cust-emerg", "permission": "view", "resource": "doc-123" }'
```
      Response:
```sh
      {
        "allowed": true,
        "reason": "Emergency access grant 'eg-001' allows 'view' on 'doc-123'.",
        "grantedPermission": "view",
        "grantedByRole": null,
        "reasonType": 1
      }
```
  (6) Segregation of Duties Conflict (Deny)
      Requests:
```sh
      curl -X POST https://localhost:7266/api/entitlement/check \
        -H "Content-Type: application/json" \
        -d '{"subject": "cust-300", "permission": "approve", "resource": "doc-123"}'
```
      Response:
```sh
      {
        "allowed": false,
        "reason": "No matching entitlement, delegation, consent, or emergency grant found.",
        "grantedPermission": null,
        "grantedByRole": null,
        "reasonType": null
      }
```
  (7) Delegation / Proxy Access (Allow)
      Requests:
```sh
      curl -X POST https://localhost:7266/api/entitlement/check \
        -H "Content-Type: application/json" \
        -d '{"subject": "cust-proxy", "permission": "view", "resource": "doc-123"}'
```
      Response:
```sh
      {
        "allowed": true,
        "reason": "Delegation 'del-001' grants proxy 'view' on 'doc-123'.",
        "grantedPermission": "view",
        "grantedByRole": null,
        "reasonType": 2
      }
```
  (8) Third‑Party Consent / PSD2 (Allow)
      Requests:
```sh
      curl -X POST https://localhost:7266/api/entitlement/check \
        -H "Content-Type: application/json" \
        -d '{"subject": "tp-123", "permission": "view", "resource": "dashboard-01"}'
```
      Response:
```sh
      {
        "allowed": true,
        "reason": "Third‑party consent 'cons-001' allows 'view' on 'dashboard-01'.",
        "grantedPermission": "view",
        "grantedByRole": null,
        "reasonType": 3
      }
```
  (9) ABAC - Missing Clearance Tag or Customer's clearanceLevel <  Resource's minClearance (Deny)
      Requests:
```sh
      curl -X POST https://localhost:7266/api/entitlement/check \
        -H "Content-Type: application/json" \
        -d '{"subject": "cust-500", "permission": "view","resource": "doc-123"}'
```
      Response:
```sh
      {
        "allowed": false,
        "reason": "No matching entitlement, delegation, consent, or emergency grant found.",
        "grantedPermission": null,
        "grantedByRole": null,
        "reasonType": null
      }
```
  (10) Resource Hierarchy - Access Child via Parent (Allow)
      Requests:
```sh
      curl -X POST https://localhost:7266/api/entitlement/check \
        -H "Content-Type: application/json" \
        -d '{"subject": "cust-100", "permission": "view", "resource": "file-A1"}'
```
      Response:
```sh
      {
        "allowed": true,
        "reason": "Entitlement 'ent-folder' via role 'AccountHolder' grants 'view' on 'file-A1'.",
        "grantedPermission": "view",
        "grantedByRole": "AccountHolder",
        "reasonType": 0
      }
```
  (11) Unknown Subject (Deny)
      Requests:
```sh
      curl -X POST https://localhost:7266/api/entitlement/check \
        -H "Content-Type: application/json" \
        -d '{"subject": "dogOlli", "permission": "view", "resource": "doc-123"}'
```
      Response:
```sh
      {
        "allowed": false,
        "reason": "No matching entitlement, delegation, consent, or emergency grant found.",
        "grantedPermission": null,
        "grantedByRole": null,
        "reasonType": null
      }
```
  (12) No Permission (Deny)
      Requests:
```sh
      curl -X POST https://localhost:7266/api/entitlement/check \
        -H "Content-Type: application/json" \
        -d '{"subject": "cust-300", "permission": "approve", "resource": "doc-123"}'
```
      Response:
```sh
      {
        "allowed": false,
        "reason": "No matching entitlement, delegation, consent, or emergency grant found.",
        "grantedPermission": null,
        "grantedByRole": null,
        "reasonType": null
      } 
```
  (13) Correct Permission, Wrong Resource (Deny)
      Requests:
```sh
      curl -X POST https://localhost:7266/api/entitlement/check \
        -H "Content-Type: application/json" \
        -d '{"subject": "cust-100", "permission": "view", "resource": "dashboard-01"}'
```
      Response:
```sh
      {
        "allowed": false,
        "reason": "No matching entitlement, delegation, consent, or emergency grant found.",
        "grantedPermission": null,
        "grantedByRole": null,
        "reasonType": null
      } 
```
  (14) Permission Does Not Exist (Deny)
      Requests:
```sh
      curl -X POST https://localhost:7266/api/entitlement/check \
        -H "Content-Type: application/json" \
        -d '{"subject": "cust-100", "permission": "update", "resource": "doc-123"}'
```
      Response:
```sh
      {
        "allowed": false,
        "reason": "No matching entitlement, delegation, consent, or emergency grant found.",
        "grantedPermission": null,
        "grantedByRole": null,
        "reasonType": null
      } 
```
  (15) Resource Does Not Exist (Deny)
      Requests:
```sh
      curl -X POST https://localhost:7266/api/entitlement/check \
        -H "Content-Type: application/json" \
        -d '{"subject": "cust-100", "permission": "view", "resource": "nonexistent-resource"}'
```
      Response:
```sh
      {
        "allowed": false,
        "reason": "No matching entitlement, delegation, consent, or emergency grant found.",
        "grantedPermission": null,
        "grantedByRole": null,
        "reasonType": null
      } 
```

## Design Principles
- BIAN alignment: Entities like PartyRole and Entitlement follow BIAN’s semantic model.
- Graph-native traversal: Authorization logic lives in one efficient Cypher query.
- Clean architecture: Service layer has no infrastructure dependencies, fully testable.
- Duplicate-safe data loading: `MERGE` prevents replays from creating duplicates.
- Principle of least privilege: Access is denied unless a traversal path exists.
