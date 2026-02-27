using Microsoft.AspNetCore.Mvc;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Models;

namespace Nodus.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProjectsController : ControllerBase
    {
        private readonly IDatabaseService _db;

        public ProjectsController(IDatabaseService db)
        {
            _db = db;
        }

        [HttpPost]
        public async Task<IActionResult> CreateProject([FromBody] Project project)
        {
            var result = await _db.SaveProjectAsync(project);
            if (result.IsSuccess) return Ok(project);
            return BadRequest(result.Error);
        }
        
        [HttpGet]
        public async Task<IActionResult> GetProjects()
        {
            var result = await _db.GetAllProjectsAsync();
            if (result.IsSuccess) return Ok(result.Value);
            return BadRequest(result.Error);
        }

        [HttpGet("event/{eventId}")]
        public async Task<IActionResult> GetProjectsByEvent(string eventId)
        {
            var result = await _db.GetProjectsAsync(eventId);
            if (result.IsSuccess) return Ok(result.Value);
            return BadRequest(result.Error);
        }
    }
}
