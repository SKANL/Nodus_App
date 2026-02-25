using Microsoft.AspNetCore.Mvc;
using Nodus.Shared.Abstractions;

namespace Nodus.Api.Controllers;

/// <summary>Provides judge-related queries for event management.</summary>
[ApiController]
[Route("api/judges")]
public class JudgesController : ControllerBase
{
    private readonly IDatabaseService _db;
    private readonly ILogger<JudgesController> _logger;

    public JudgesController(IDatabaseService db, ILogger<JudgesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>GET /api/judges/event/{eventId} — List all judges for an event.</summary>
    [HttpGet("event/{eventId}")]
    public async Task<IActionResult> GetByEvent(string eventId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return BadRequest("Event ID is required.");
        var result = await _db.GetJudgesAsync(eventId, ct);
        if (result.IsSuccess) return Ok(result.Value);
        _logger.LogError("GetJudgesAsync failed for event {EventId}: {Error}", eventId, result.Error);
        return StatusCode(500, result.Error);
    }

    /// <summary>GET /api/judges/{judgeId}/votes — Get all votes cast by a specific judge.</summary>
    [HttpGet("{judgeId}/votes")]
    public async Task<IActionResult> GetVotesByJudge(string judgeId, [FromQuery] string? eventId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(judgeId)) return BadRequest("Judge ID is required.");

        var result = await _db.GetAllVotesAsync(ct);
        if (result.IsFailure) return StatusCode(500, result.Error);

        var votes = result.Value?
            .Where(v => v.JudgeId == judgeId)
            .Where(v => string.IsNullOrEmpty(eventId) || v.EventId == eventId)
            .ToList();

        return Ok(votes);
    }
}
