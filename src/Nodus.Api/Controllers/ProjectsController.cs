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
    public async Task<IActionResult> CreateProject([FromBody] Project project, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (string.IsNullOrEmpty(project.Id)) project.Id = Guid.NewGuid().ToString();
        var result = await _db.SaveProjectAsync(project, ct);
        if (result.IsSuccess) return Created($"/api/projects/{project.Id}", project);
        return BadRequest(result.Error);
    }
        
        [HttpGet]
    public async Task<IActionResult> GetProjects(CancellationToken ct)
    {
        var result = await _db.GetAllProjectsAsync(ct);
        if (result.IsSuccess) return Ok(result.Value);
        return StatusCode(500, result.Error);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest("Project ID required.");
        var result = await _db.GetProjectAsync(id, ct);
        if (result.IsSuccess) return Ok(result.Value);
        return NotFound($"Project '{id}' not found.");
    }

    [HttpGet("event/{eventId}")]
    public async Task<IActionResult> GetByEvent(string eventId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return BadRequest("Event ID required.");
        var result = await _db.GetProjectsAsync(eventId, ct);
        if (result.IsSuccess) return Ok(result.Value);
        return StatusCode(500, result.Error);
    }
    }
}
