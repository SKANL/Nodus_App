using Microsoft.AspNetCore.Mvc;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Models;

namespace Nodus.Api.Controllers;

/// <summary>Manages Nodus events — creation, retrieval, and rubric access.</summary>
[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly IDatabaseService _db;
    private readonly ILogger<EventsController> _logger;

    public EventsController(IDatabaseService db, ILogger<EventsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>POST /api/events — Create or update an event (idempotent upsert).</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Event evt, CancellationToken ct)
    {
        if (evt == null || string.IsNullOrWhiteSpace(evt.Name))
            return BadRequest("Event name is required.");

        if (string.IsNullOrWhiteSpace(evt.Id))
            evt.Id = $"EVENT-{evt.Name.Replace(" ", "")}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        var result = await _db.SaveEventAsync(evt, ct);
        if (result.IsSuccess) return Ok(evt);

        _logger.LogError("SaveEventAsync failed for event {EventName}: {Error}", evt.Name, result.Error);
        return StatusCode(500, result.Error);
    }

    /// <summary>GET /api/events — List all events.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await _db.GetEventsAsync(ct);
        if (result.IsSuccess) return Ok(result.Value);
        _logger.LogError("GetAll events failed: {Error}", result.Error);
        return StatusCode(500, result.Error);
    }

    /// <summary>GET /api/events/{id} — Get a specific event by ID.</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest("Event ID is required.");
        var result = await _db.GetEventAsync(id, ct);
        if (result.IsSuccess) return Ok(result.Value);
        if (result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true)
            return NotFound($"Event '{id}' not found.");
        return StatusCode(500, result.Error);
    }

    /// <summary>GET /api/events/{id}/rubric — Get evaluation categories for an event.</summary>
    [HttpGet("{id}/rubric")]
    public async Task<IActionResult> GetRubric(string id, CancellationToken ct)
    {
        var result = await _db.GetEventAsync(id, ct);
        if (result.IsFailure) return NotFound($"Event '{id}' not found.");

        var evt = result.Value!;
        var categories = (evt.RubricJson ?? "{}")
            .TrimStart()
            .StartsWith("{")
            ? System.Text.Json.JsonDocument.Parse(evt.RubricJson!)
                .RootElement.EnumerateObject()
                .Select(p => new { Name = p.Name, MaxScore = 10 })
                .ToList<object>()
            : (evt.RubricJson ?? "Software, Hardware, Innovation")
                .Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
                .Select(c => (object)new { Name = c, MaxScore = 10 })
                .ToList();

        return Ok(new { EventId = id, Categories = categories });
    }
}
