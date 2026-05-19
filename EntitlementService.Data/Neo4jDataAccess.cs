using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;
using System;
using System.Collections.Generic;
using System.Text;

namespace EntitlementService.Data
{
    public class Neo4jDataAccess : INeo4jDataAccess, IDisposable
    {
        private readonly IDriver _driver;
        private readonly ILogger<Neo4jDataAccess> _logger;

        public Neo4jDataAccess(IConfiguration config, ILogger<Neo4jDataAccess> logger)
        {
            _logger = logger;

            var uri = config.GetRequiredSection("Neo4j:Uri").Value
                           ?? throw new InvalidOperationException("Neo4j:Uri is not configured.");
            var user = config.GetRequiredSection("Neo4j:User").Value
                           ?? throw new InvalidOperationException("Neo4j:User is not configured.");
            var password = config.GetRequiredSection("Neo4j:Password").Value
                           ?? throw new InvalidOperationException("Neo4j:Password is not configured.");

            _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));
            _logger.LogInformation("Neo4j driver initialised for {Uri}", uri);
        }

        //// Alternate constructor using Azure Key Vault for password retrieval.
        //public Neo4jDataAccess(IConfiguration config)
        //{
        //    var uri = config["Neo4j:Uri"]!;
        //    var user = config["Neo4j:User"]!;
        //
        //    var credential  = new DefaultAzureCredential();
        //    var client      = new SecretClient(new Uri(config["Neo4j:KeyVaultUrl"]!), credential);
        //    var password    = client.GetSecret(config["Neo4j:SecretName"]!).Value.Value;

        //    _driver = GraphDatabase.Driver(
        //        uri,
        //        AuthTokens.Basic(user, password)
        //    );
        //}

        public async Task<List<T>> ExecuteReadListAsync<T>(string query, object parameters, Func<IRecord, T> map)
        {
            await using var session = _driver.AsyncSession();
            return await session.ExecuteReadAsync(async tx =>
            {
                var cursor = await tx.RunAsync(query, parameters);
                var results = new List<T>();
                await foreach (var record in cursor)
                {
                    results.Add(map(record));
                }
                return results;
            });
        }

        public async Task ExecuteWriteAsync(string query, object parameters)
        {
            await using var session = _driver.AsyncSession();
            await session.ExecuteWriteAsync(tx => tx.RunAsync(query, parameters));
        }

        public void Dispose()
        {
            _driver?.Dispose();
            _logger.LogInformation("Neo4j driver disposed.");
        }
    }
}
