using Microsoft.AspNetCore.Mvc;
using SBB.EasyRide.TaxReport.Infrastructure;

namespace SBB.EasyRide.TaxReport.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            var testService = new TestService();
            var msg = testService.GetMessage();
            return Ok(new { status = "API is alive!", infra = msg });
        }
    }
}
