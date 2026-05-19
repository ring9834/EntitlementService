using Neo4j.Driver;

namespace EntitlementService.Data
{
    public interface INeo4jDataAccess
    {
        Task<List<T>> ExecuteReadListAsync<T>(string query, object parameters, Func<IRecord, T> map);
        Task ExecuteWriteAsync(string query, object parameters);
    }
}
