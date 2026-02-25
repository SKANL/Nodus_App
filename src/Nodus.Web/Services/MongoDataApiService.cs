using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Nodus.Shared.Config;
using Nodus.Shared.Models;
using Nodus.Shared.Common;

namespace Nodus.Web.Services;

public class MongoDataApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MongoDataApiService> _logger;
    private readonly string _baseUrl;

    public MongoDataApiService(HttpClient httpClient, ILogger<MongoDataApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _baseUrl = $"https://data.mongodb-api.com/app/{AppSecrets.MongoAtlasAppId}/endpoint/data/v1/action";
        
        // Data API requires this header
        if (!_httpClient.DefaultRequestHeaders.Contains("api-key"))
        {
            _httpClient.DefaultRequestHeaders.Add("api-key", AppSecrets.MongoAtlasApiKey);
        }
    }

    public async Task<Result<List<Project>>> GetProjectsAsync(string eventId)
    {
        return await ActionAsync<List<Project>>("find", new
        {
            dataSource = "Cluster0", 
            database = AppSecrets.MongoDatabaseName,
            collection = "projects",
            filter = new { eventId = eventId }
        });
    }

    public virtual async Task<Result<List<Event>>> GetEventsAsync()
    {
        return await ActionAsync<List<Event>>("find", new
        {
            dataSource = "Cluster0",
            database = AppSecrets.MongoDatabaseName,
            collection = "events",
            filter = new { } // Get all events
        });
    }

    public async Task<Result> SaveProjectAsync(Project project)
    {
        var response = await ActionAsync<JsonElement>("updateOne", new
        {
            dataSource = "Cluster0",
            database = AppSecrets.MongoDatabaseName,
            collection = "projects",
            filter = new { _id = project.Id },
            update = new { @set = project },
            upsert = true
        }, isWrite: true);

        return response.IsSuccess ? Result.Success() : Result.Failure(response.Error);
    }

    private async Task<Result<T>> ActionAsync<T>(string action, object body, bool isWrite = false)
    {
        try
        {
            var contentBody = JsonContent.Create(body);
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/{action}")
            {
                Content = contentBody
            };

            if (isWrite)
            {
                request.Headers.Add("X-Nodus-Event-Key", AppSecrets.NodusEventApiKey);
            }

            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadFromJsonAsync<DataApiResponse<T>>();
                if (content != null)
                {
                    if (action == "find") return Result<T>.Success(content.Documents);
                    return Result<T>.Success(content.Document);
                }
                return Result<T>.Failure("Empty response from Data API");
            }

            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Atlas Data API Error: {Status} - {Error}", response.StatusCode, error);
            return Result<T>.Failure($"Atlas Data API Error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call Atlas Data API");
            return Result<T>.Failure("Network error calling Atlas", ex);
        }
    }

    private class DataApiResponse<T>
    {
        [JsonPropertyName("documents")]
        public T? Documents { get; set; }

        [JsonPropertyName("document")]
        public T? Document { get; set; }
    }
}
