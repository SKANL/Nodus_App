using Microsoft.AspNetCore.Mvc;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Models;

namespace Nodus.Api.Controllers;

/// <summary>Manages vote registration and retrieval for the Nodus system.</summary>
[ApiController]
[Route("api/votes")]
public class VotesController : ControllerBase
{
    private readonly IDatabaseService _db;
    private readonly ILogger<VotesController> _logger;

    public VotesController(IDatabaseService db, ILogger<VotesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>POST /api/votes — Register a new vote.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Vote vote, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (string.IsNullOrEmpty(vote.Id))
            vote.Id = Guid.NewGuid().ToString();

        var result = await _db.SaveVoteAsync(vote, ct);
        if (result.IsSuccess)
        {
            _logger.LogInformation("Vote {VoteId} registered for project {ProjectId}", vote.Id, vote.ProjectId);
            return Created($"/api/votes/{vote.Id}", vote);
        }
        _logger.LogError("Failed to save vote: {Error}", result.Error);
        return StatusCode(500, result.Error);
    }

    /// <summary>GET /api/votes — Get all votes.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await _db.GetAllVotesAsync(ct);
        if (result.IsSuccess) return Ok(result.Value);
        return StatusCode(500, result.Error);
    }

    /// <summary>GET /api/votes/project/{projectId} — All votes for a project.</summary>
    [HttpGet("project/{projectId}")]
    public async Task<IActionResult> GetByProject(string projectId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectId)) return BadRequest("Project ID is required.");
        var result = await _db.GetVotesAsync(projectId, ct);
        if (result.IsSuccess) return Ok(result.Value);
        return StatusCode(500, result.Error);
    }

    /// <summary>GET /api/votes/event/{eventId} — All votes for an event (filtered client-side).</summary>
    [HttpGet("event/{eventId}")]
    public async Task<IActionResult> GetByEvent(string eventId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return BadRequest("Event ID is required.");
        // Get all votes and filter by eventId
        var result = await _db.GetAllVotesAsync(ct);
        if (result.IsFailure) return StatusCode(500, result.Error);
        var filtered = result.Value?.Where(v => v.EventId == eventId).ToList();
        return Ok(filtered);
    }
}
