using Microsoft.AspNetCore.Mvc;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Models;

namespace Nodus.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EventsController : ControllerBase
    {
        private readonly IDatabaseService _db;

        public EventsController(IDatabaseService db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> GetEvents()
        {
            var result = await _db.GetEventsAsync();
            if (result.IsSuccess) return Ok(result.Value);
            return BadRequest(result.Error);
        }
    }
}
