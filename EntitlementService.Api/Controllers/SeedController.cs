using EntitlementService.Models;
using EntitlementService.Services;
using Microsoft.AspNetCore.Mvc;

namespace EntitlementService.Api.Controllers
{
    /// <summary>
    /// Populates the Neo4j database with demo data.
    /// Restricted to the Development environment.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public sealed class SeedController : ControllerBase
    {
        private readonly ISeedService _seedService;
        private readonly IWebHostEnvironment _env;

        public SeedController(ISeedService seedService, IWebHostEnvironment env)
        {
            _seedService = seedService;
            _env = env;
        }

        [HttpPost]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Seed()
        {
            if (!_env.IsDevelopment())
                return Forbid();

            await _seedService.SeedAsync();
            return Ok("Database seeded with demo data.");
        }
    }
}
