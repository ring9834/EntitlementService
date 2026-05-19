namespace EntitlementService.Models
{
    /// <summary>
    /// Parameters for a single access-check call.
    /// </summary>
    public record CheckRequest
    {
        public string Subject { get; init; }
        public string Permission { get; init; }
        public string Resource { get; init; }

        public CheckRequest(string subject, string permission, string resource)
        {
            // A blank Subject or Permission would silently produce a nonsensical (and expensive) Neo4j query that matches all nodes,
            // so we proactively validate these parameters here to fail fast and provide clearer error messages.
            ArgumentException.ThrowIfNullOrWhiteSpace(subject, nameof(subject));
            ArgumentException.ThrowIfNullOrWhiteSpace(permission, nameof(permission));
            ArgumentException.ThrowIfNullOrWhiteSpace(resource, nameof(resource));

            Subject = subject;
            Permission = permission;
            Resource = resource;
        }
    }
}
