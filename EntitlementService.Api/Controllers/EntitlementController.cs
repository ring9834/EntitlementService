using EntitlementService.Models;
using EntitlementService.Services;
using Microsoft.AspNetCore.Mvc;

namespace EntitlementService.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EntitlementController : ControllerBase
    {
        private readonly IEntitlementCheckService _checkService;
        public EntitlementController(IEntitlementCheckService checkService) => _checkService = checkService;

        /// <summary>
        /// Evaluates whether a subject holds a permission on a resource.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("check")]
        [ProducesResponseType(typeof(CheckResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CheckAccess([FromBody] CheckRequest request)
        {
            try
            {
                var result = await _checkService.CheckAccessAsync(request);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
