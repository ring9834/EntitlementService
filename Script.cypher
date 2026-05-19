        // = PERMISSIONS & RESOURCES =
        MERGE (view:Permission {name: 'view'})
        MERGE (edit:Permission {name: 'edit'})
        MERGE (approve:Permission {name: 'approve'})
        MERGE (delete:Permission {name: 'delete'})

        MERGE (doc123:Resource {id: 'doc-123', minClearance: 2})
        MERGE (dashboard:Resource {id: 'dashboard-01', minClearance: 2})
        MERGE (folderA:Resource {id: 'folder-A'})
        MERGE (fileA1:Resource {id: 'file-A1'})
        MERGE (fileA1)-[:CHILD_OF]->(folderA)           // resource hierarchy

        // = COMPLIANCE TAGS =
        MERGE (gdpr:ComplianceTag {name: 'GDPR'})
        MERGE (sox:ComplianceTag {name: 'SOX'})
        MERGE (doc123)-[:TAGGED_WITH]->(gdpr)
        MERGE (dashboard)-[:TAGGED_WITH]->(sox)

        // = 1. DIRECT ENTITLEMENT (with ABAC) =
        MERGE (cust100:Customer {id: 'cust-100', clearanceLevel: 2})
        MERGE (acctHolder:PartyRole {name: 'AccountHolder'})
        MERGE (ent001:Entitlement {id: 'ent-001', startDate: datetime('2026-05-01T00:00:00Z'), endDate: datetime('2026-07-01T00:00:00Z')})
        MERGE (cust100)-[:HAS_PARTY_ROLE]->(acctHolder)
        MERGE (acctHolder)-[:GRANTS]->(ent001)
        MERGE (ent001)-[:ALLOWS]->(view)
        MERGE (ent001)-[:ON]->(doc123)
        // ABAC clearance
        MERGE (cust100)-[:CLEARANCE_FOR]->(gdpr)

        // = 2. ROLE INHERITANCE =
        MERGE (adminRole:PartyRole {name: 'Admin'})
        MERGE (custAdmin:Customer {id: 'cust-admin', clearanceLevel: 3})
        MERGE (custAdmin)-[:HAS_PARTY_ROLE]->(adminRole)
        MERGE (adminRole)-[:INHERITS]->(acctHolder)
        MERGE (custAdmin)-[:CLEARANCE_FOR]->(gdpr)

        // = 3. OVERLAPPING MULTIPLE ROLES =
        MERGE (viewerRole:PartyRole {name: 'Viewer'})
        MERGE (editorRole:PartyRole {name: 'Editor'})
        MERGE (cust300:Customer {id: 'cust-300', clearanceLevel: 2})
        MERGE (cust300)-[:HAS_PARTY_ROLE]->(viewerRole)
        MERGE (cust300)-[:HAS_PARTY_ROLE]->(editorRole)
        MERGE (ent003:Entitlement {id: 'ent-003'})
        MERGE (viewerRole)-[:GRANTS]->(ent003)
        MERGE (ent003)-[:ALLOWS]->(view)
        MERGE (ent003)-[:ON]->(doc123)
        MERGE (ent004:Entitlement {id: 'ent-004'})
        MERGE (editorRole)-[:GRANTS]->(ent004)
        MERGE (ent004)-[:ALLOWS]->(edit)
        MERGE (ent004)-[:ON]->(doc123)
        MERGE (cust300)-[:CLEARANCE_FOR]->(gdpr)

        // = 4. TEMPORAL (TIME-BOXED) ENTITLEMENTS =
        MERGE (custTemp:Customer {id: 'cust-temp', clearanceLevel: 2})
        MERGE (tempRole:PartyRole {name: 'TempWorker'})
        MERGE (custTemp)-[:HAS_PARTY_ROLE]->(tempRole)
        MERGE (expiredEnt:Entitlement {id: 'ent-expired', startDate: datetime('2026-04-01T00:00:00Z'), endDate: datetime('2025-04-30T23:59:59Z')})
        MERGE (activeEnt:Entitlement {id: 'ent-active', startDate: datetime('2026-05-01T00:00:00Z'), endDate: datetime('2026-05-31T23:59:59Z')})
        MERGE (tempRole)-[:GRANTS]->(expiredEnt)
        MERGE (tempRole)-[:GRANTS]->(activeEnt)
        MERGE (expiredEnt)-[:ALLOWS]->(view)
        MERGE (expiredEnt)-[:ON]->(doc123)
        MERGE (activeEnt)-[:ALLOWS]->(edit)
        MERGE (activeEnt)-[:ON]->(doc123)
        MERGE (custTemp)-[:CLEARANCE_FOR]->(gdpr)

        // = 5. EMERGENCY / BREAK-GLASS =
        MERGE (emergUser:Customer {id: 'cust-emerg', clearanceLevel: 4})
        MERGE (eg:EmergencyGrant {id: 'eg-001', resource: 'doc-123', expiresAt: datetime('2099-01-01T00:00:00Z')})
        MERGE (eg)-[:ALLOWS]->(view)
        MERGE (eg)-[:ON]->(doc123)
        MERGE (emergUser)-[:HAS_EMERGENCY_ACCESS]->(eg)

        // = 6. SEGREGATION OF DUTIES (SoD) =
        // edit and approve conflict on the same resource
        MERGE (edit)-[:CONFLICTS_WITH]->(approve)

        // = 7. DELEGATED / PROXY ACCESS =
        MERGE (custOwner:Customer {id: 'cust-owner', clearanceLevel: 2})
        MERGE (custProxy:Customer {id: 'cust-proxy', clearanceLevel: 2})
        MERGE (del:Delegation {id: 'del-001', validUntil: datetime('2099-01-01T00:00:00Z')})
        MERGE (custOwner)-[:DELEGATES]->(del)
        MERGE (del)-[:TO]->(custProxy)
        MERGE (del)-[:ALLOWS_DELEGATED]->(view)
        MERGE (del)-[:ON_DELEGATED]->(doc123)

        // = 8. CROSS-ENTITY / THIRD-PARTY (PSD2) =
        MERGE (dataOwner:Customer {id: 'cust-owner', clearanceLevel: 2})
        MERGE (consent:Consent {id: 'cons-001', thirdPartyId: 'tp-123', validUntil: datetime('2099-01-01T00:00:00Z')})
        MERGE (dataOwner)-[:GIVES_CONSENT]->(consent)
        MERGE (consent)-[:ALLOWS]->(view)
        MERGE (consent)-[:ON]->(dashboard)

        // = 9. ATTRIBUTE-BASED ACCESS CONTROL (ABAC) =
        // cust-500 lacks GDPR clearance, so cannot view doc-123 even with a role
        MERGE (cust500:Customer {id: 'cust-500', clearanceLevel: 1})
        MERGE (basicRole:PartyRole {name: 'BasicUser'})
        MERGE (ent500:Entitlement {id: 'ent-500'})
        MERGE (cust500)-[:HAS_PARTY_ROLE]->(basicRole)
        MERGE (basicRole)-[:GRANTS]->(ent500)
        MERGE (ent500)-[:ALLOWS]->(view)
        MERGE (ent500)-[:ON]->(doc123)
        // cust-500 does NOT have CLEARANCE_FOR GDPR -> ABAC will deny

        // = 10. RESOURCE HIERARCHY / DATA SCOPING =
        // AccountHolder (cust-100) already has view on folder-A, so should also access file-A1 (child)
        MERGE (folderAccess:Entitlement {id: 'ent-folder'})
        MERGE (acctHolder)-[:GRANTS]->(folderAccess)
        MERGE (folderAccess)-[:ALLOWS]->(view)
        MERGE (folderAccess)-[:ON]->(folderA)