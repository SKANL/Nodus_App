using ClosedXML.Excel;
using System.Text;
using Nodus.Shared.Abstractions;
using Microsoft.Extensions.Logging;

namespace Nodus.Server.Services;

public class ExportService
{
    private readonly IDatabaseService _db;
    private readonly ILogger<ExportService> _logger;

    public ExportService(IDatabaseService db, ILogger<ExportService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<byte[]> ExportToCsvAsync(string eventId)
    {
        var votesResult = await _db.GetAllVotesAsync();
        if (votesResult.IsFailure) return Array.Empty<byte>();

        var votes = votesResult.Value
            .Where(v => v.EventId == eventId)
            .ToList();

        var csv = new StringBuilder();
        csv.AppendLine("VoteId,ProjectId,JudgeId,Scores,CreatedAt,SyncedAt");

        foreach (var vote in votes)
        {
            // Escape quotes in payload
            var payload = vote.PayloadJson.Replace("\"", "\"\"");
            
            csv.AppendLine($"{vote.Id},{vote.ProjectId},{vote.JudgeId}," +
                          $"\"{payload}\",{DateTimeOffset.FromUnixTimeSeconds(vote.Timestamp).UtcDateTime}," +
                          $"{vote.SyncedAtUtc}");
        }

        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    public async Task<byte[]> ExportToExcelAsync(string eventId)
    {
        var votesResult = await _db.GetAllVotesAsync();
        if (votesResult.IsFailure) return Array.Empty<byte>();

        var votes = votesResult.Value
            .Where(v => v.EventId == eventId)
            .ToList();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Votes");

        // Headers
        worksheet.Cell(1, 1).Value = "Vote ID";
        worksheet.Cell(1, 2).Value = "Project ID";
        worksheet.Cell(1, 3).Value = "Judge ID";
        worksheet.Cell(1, 4).Value = "Scores";
        worksheet.Cell(1, 5).Value = "Created At";
        worksheet.Cell(1, 6).Value = "Synced At";

        // Data
        for (int i = 0; i < votes.Count; i++)
        {
            var vote = votes[i];
            var row = i + 2;

            worksheet.Cell(row, 1).Value = vote.Id;
            worksheet.Cell(row, 2).Value = vote.ProjectId;
            worksheet.Cell(row, 3).Value = vote.JudgeId;
            worksheet.Cell(row, 4).Value = vote.PayloadJson;
            worksheet.Cell(row, 5).Value = DateTimeOffset.FromUnixTimeSeconds(vote.Timestamp).UtcDateTime;
            worksheet.Cell(row, 6).Value = vote.SyncedAtUtc; // Might need nullable check
        }

        // Auto-fit columns
        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
