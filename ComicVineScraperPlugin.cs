using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicRow.PluginSystem;
using ComicRow.Plugins.ComicVineScraper.Models;
using ComicRow.Plugins.ComicVineScraper.Services;
using Newtonsoft.Json;

namespace ComicRow.Plugins.ComicVineScraper
{
    /// <summary>
    /// Comic Vine Scraper plugin for ComicRow.
    /// Scrapes comic metadata from Comic Vine (comicvine.gamespot.com).
    /// 
    /// This plugin provides the SOLE implementation of ComicVine integration.
    /// The built-in MetadataScraper has been deprecated in favor of this plugin-based approach.
    /// 
    /// Features:
    /// - Rate-limited API access (200 requests/hour/endpoint, 1s between requests)
    /// - Volume-enhanced search for better matching
    /// - Imprint-to-publisher mapping
    /// - Sophisticated string similarity matching
    /// - Full credit parsing (writers, artists, colorists, etc.)
    /// </summary>
    public class ComicVineScraperPlugin : IComicContextMenuPlugin, ILibraryScanPlugin, IScheduledTaskPlugin, IPluginApiProvider
    {
        private IPluginContext? _context;
        private ComicVineApiClient? _apiClient;
        
        public string Name => "ComicVine Scraper";
        public string Version => "2.0.0";

        public void SetContext(IPluginContext context)
        {
            _context = context;
            _apiClient = new ComicVineApiClient(context);
            _context.Info("ComicVine Scraper v2.0 initialized");
        }

        public void OnUnload()
        {
            _context?.Info("ComicVine Scraper plugin unloading");
            _apiClient = null;
        }

        #region IPluginApiProvider Implementation

        public IReadOnlyList<PluginApiEndpoint> GetApiEndpoints()
        {
            return new List<PluginApiEndpoint>
            {
                // Search endpoints
                CreateEndpoint("search", new[] { "POST" }, "Search ComicVine for comics",
                    "Search the ComicVine database for comic issues matching the provided criteria",
                    new Dictionary<string, PluginApiProperty>
                    {
                        ["query"] = new() { Type = "string", Description = "Search query (series name, title, etc.)" },
                        ["issueNumber"] = new() { Type = "string", Description = "Issue number (optional)", Nullable = true },
                        ["year"] = new() { Type = "integer", Description = "Publication year (optional)", Nullable = true },
                        ["limit"] = new() { Type = "integer", Description = "Max results (default: 25)", Nullable = true },
                        ["useVolumeSearch"] = new() { Type = "boolean", Description = "Use volume-enhanced search (recommended)", Nullable = true }
                    }),
                
                CreateEndpoint("search/volumes", new[] { "POST" }, "Search ComicVine for volumes (series)",
                    "Search for volumes/series to browse their issues",
                    new Dictionary<string, PluginApiProperty>
                    {
                        ["query"] = new() { Type = "string", Description = "Series name to search for" },
                        ["year"] = new() { Type = "integer", Description = "Start year filter (optional)", Nullable = true }
                    }),
                
                // Issue endpoints
                new PluginApiEndpoint
                {
                    Route = "issue/{issueId}",
                    Methods = new[] { "GET" },
                    Summary = "Get ComicVine issue details",
                    Description = "Fetch detailed metadata for a specific ComicVine issue including credits",
                    Tags = new[] { "ComicVine" },
                    Parameters = new List<PluginApiParameter>
                    {
                        new() { Name = "issueId", In = "path", Required = true, Description = "ComicVine issue ID", Type = "integer" }
                    }
                },
                
                // Volume endpoints
                new PluginApiEndpoint
                {
                    Route = "volume/{volumeId}/issues",
                    Methods = new[] { "GET" },
                    Summary = "Get issues in a volume",
                    Description = "List all issues in a ComicVine volume (series)",
                    Tags = new[] { "ComicVine" },
                    Parameters = new List<PluginApiParameter>
                    {
                        new() { Name = "volumeId", In = "path", Required = true, Description = "ComicVine volume ID", Type = "integer" }
                    }
                },
                
                // Match and apply endpoints
                new PluginApiEndpoint
                {
                    Route = "match/{comicId}",
                    Methods = new[] { "GET" },
                    Summary = "Get potential matches for a comic",
                    Description = "Search ComicVine for potential matches based on the comic's existing metadata",
                    Tags = new[] { "ComicVine" },
                    Parameters = new List<PluginApiParameter>
                    {
                        new() { Name = "comicId", In = "path", Required = true, Description = "Comic ID to find matches for", Type = "string" }
                    }
                },
                
                CreateEndpoint("apply", new[] { "POST" }, "Apply ComicVine match to a comic",
                    "Apply metadata from a specific ComicVine issue to a comic in the library",
                    new Dictionary<string, PluginApiProperty>
                    {
                        ["comicId"] = new() { Type = "string", Description = "Comic ID to update" },
                        ["comicVineId"] = new() { Type = "integer", Description = "ComicVine issue ID to apply" },
                        ["mergeMode"] = new() { Type = "boolean", Description = "Only fill empty fields", Nullable = true }
                    }),
                
                CreateEndpoint("scrape", new[] { "POST" }, "Batch scrape comics",
                    "Scrape and apply ComicVine metadata to multiple comics",
                    new Dictionary<string, PluginApiProperty>
                    {
                        ["comicIds"] = new() { Type = "array", Description = "Array of comic IDs to scrape" },
                        ["autoApplyThreshold"] = new() { Type = "number", Description = "Confidence threshold (0-1)", Nullable = true },
                        ["mergeMode"] = new() { Type = "boolean", Description = "Only fill empty fields", Nullable = true }
                    }),
                
                // Rate limit endpoint
                new PluginApiEndpoint
                {
                    Route = "ratelimits",
                    Methods = new[] { "GET" },
                    Summary = "Get rate limit status",
                    Description = "Get current rate limit usage for each API endpoint",
                    Tags = new[] { "ComicVine" }
                }
            };
        }
        
        private static PluginApiEndpoint CreateEndpoint(string route, string[] methods, string summary, string description, Dictionary<string, PluginApiProperty> properties)
        {
            return new PluginApiEndpoint
            {
                Route = route,
                Methods = methods,
                Summary = summary,
                Description = description,
                Tags = new[] { "ComicVine" },
                RequestBody = new PluginApiRequestBody
                {
                    Description = description,
                    Required = true,
                    Content = new Dictionary<string, PluginApiSchema>
                    {
                        ["application/json"] = new PluginApiSchema { Type = "object", Properties = properties }
                    }
                }
            };
        }

        public async Task<PluginApiResponse> HandleRequestAsync(PluginApiRequest request, CancellationToken cancellationToken = default)
        {
            if (_context == null || _apiClient == null)
                return PluginApiResponse.Error("Plugin not initialized");

            _context.Debug($"[PLUGIN] HandleRequestAsync called - Route: {request.Route}, Method: {request.Method}");
            
            if (!_context.HasPermission("http:comicvine.gamespot.com"))
                return PluginApiResponse.Error("HTTP permission denied for comicvine.gamespot.com");

            try
            {
                // Ensure API key is configured
                await EnsureApiKeyAsync();
                
                return request.Route switch
                {
                    "search" => await HandleSearchAsync(request, cancellationToken),
                    "search/volumes" => await HandleSearchVolumesAsync(request, cancellationToken),
                    var r when r.StartsWith("issue/") => await HandleGetIssueAsync(request, cancellationToken),
                    var r when r.StartsWith("volume/") && r.EndsWith("/issues") => await HandleGetVolumeIssuesAsync(request, cancellationToken),
                    var r when r.StartsWith("match/") => await HandleGetMatchesAsync(request, cancellationToken),
                    "apply" => await HandleApplyAsync(request, cancellationToken),
                    "scrape" => await HandleScrapeAsync(request, cancellationToken),
                    "ratelimits" => HandleGetRateLimits(),
                    _ => PluginApiResponse.NotFound($"Unknown route: {request.Route}")
                };
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("API key"))
            {
                return new PluginApiResponse { StatusCode = 401, Body = new { error = ex.Message } };
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Rate limit"))
            {
                return new PluginApiResponse { StatusCode = 429, Body = new { error = ex.Message } };
            }
            catch (Exception ex)
            {
                _context.Error($"API error handling {request.Route}", ex);
                return PluginApiResponse.Error(ex.Message);
            }
        }

        private async Task EnsureApiKeyAsync()
        {
            if (_apiClient!.IsConfigured)
                return;
            
            // Try plugin's own setting first (from manifest.json settings.api_key)
            var apiKey = await _context!.GetValueAsync("api_key");
            
            // Fall back to legacy locations for backward compatibility
            if (string.IsNullOrEmpty(apiKey))
                apiKey = await _context.GetValueAsync("Credentials", "ComicVine:ApiKey");
            
            if (string.IsNullOrEmpty(apiKey))
                apiKey = await _context.GetValueAsync("MetadataScraper", "ComicVine:ApiKey");
            
            if (string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException("Comic Vine API key not configured. Go to Settings → Plugins → ComicVine Scraper to configure your API key. Get a free key at https://comicvine.gamespot.com/api/");
            
            _apiClient.SetApiKey(apiKey);
        }

        #region API Handlers

        private async Task<PluginApiResponse> HandleSearchAsync(PluginApiRequest request, CancellationToken cancellationToken)
        {
            var req = JsonConvert.DeserializeObject<SearchRequest>(request.Body ?? "{}");
            if (req == null || string.IsNullOrEmpty(req.Query))
                return PluginApiResponse.BadRequest("Query is required");

            int? issueNum = null;
            if (!string.IsNullOrEmpty(req.IssueNumber) && int.TryParse(req.IssueNumber, out var num))
                issueNum = num;

            var useVolumeSearch = req.UseVolumeSearch ?? true; // Default to volume-enhanced search
            
            var results = useVolumeSearch
                ? await _apiClient!.SearchWithVolumeEnhancementAsync(req.Query, issueNum, req.Year, cancellationToken)
                : await _apiClient!.SearchIssuesAsync(req.Query, issueNum, req.Year, cancellationToken);

            return PluginApiResponse.Ok(new { results, totalResults = results.Count });
        }

        private async Task<PluginApiResponse> HandleSearchVolumesAsync(PluginApiRequest request, CancellationToken cancellationToken)
        {
            var req = JsonConvert.DeserializeObject<VolumeSearchRequest>(request.Body ?? "{}");
            if (req == null || string.IsNullOrEmpty(req.Query))
                return PluginApiResponse.BadRequest("Query is required");

            var results = await _apiClient!.SearchVolumesAsync(req.Query, req.Year, cancellationToken);
            return PluginApiResponse.Ok(new { results, totalResults = results.Count });
        }

        private async Task<PluginApiResponse> HandleGetIssueAsync(PluginApiRequest request, CancellationToken cancellationToken)
        {
            if (!request.RouteValues.TryGetValue("issueId", out var issueId))
                return PluginApiResponse.BadRequest("Invalid issue ID");

            var metadata = await _apiClient!.GetIssueMetadataAsync(issueId, cancellationToken);
            if (metadata == null)
                return PluginApiResponse.NotFound($"Issue {issueId} not found");

            return PluginApiResponse.Ok(metadata);
        }

        private async Task<PluginApiResponse> HandleGetVolumeIssuesAsync(PluginApiRequest request, CancellationToken cancellationToken)
        {
            // Route: volume/{volumeId}/issues
            var parts = request.Route.Split('/');
            if (parts.Length < 2)
                return PluginApiResponse.BadRequest("Invalid volume ID");
            
            var volumeId = parts[1];
            var issues = await _apiClient!.GetVolumeIssuesAsync(volumeId, cancellationToken);
            return PluginApiResponse.Ok(new { issues, totalIssues = issues.Count });
        }

        private async Task<PluginApiResponse> HandleGetMatchesAsync(PluginApiRequest request, CancellationToken cancellationToken)
        {
            if (!request.RouteValues.TryGetValue("comicId", out var comicIdStr) || !Guid.TryParse(comicIdStr, out var comicId))
                return PluginApiResponse.BadRequest("Invalid comic ID");

            if (!_context!.HasPermission("comic:read"))
                return PluginApiResponse.Error("Permission denied: comic:read");

            var comic = await _context.GetComicMetadataAsync(comicId);
            if (comic == null)
                return PluginApiResponse.NotFound($"Comic {comicId} not found");

            var searchTerm = !string.IsNullOrEmpty(comic.Series) ? comic.Series : comic.FileName;
            int? issueNum = null;
            if (!string.IsNullOrEmpty(comic.Number) && int.TryParse(comic.Number, out var num))
                issueNum = num;

            var results = await _apiClient!.SearchWithVolumeEnhancementAsync(searchTerm, issueNum, comic.Year, cancellationToken);

            return PluginApiResponse.Ok(new
            {
                comic = new { comic.Series, comic.Number, comic.FileName, comic.Year },
                matches = results.Select(r => new
                {
                    comicVineId = r.Id,
                    name = r.Title,
                    issueNumber = r.IssueNumber,
                    series = r.Series,
                    publisher = r.Publisher,
                    year = r.Year,
                    coverUrl = r.CoverUrl,
                    confidence = r.MatchScore
                }).ToList()
            });
        }

        private async Task<PluginApiResponse> HandleApplyAsync(PluginApiRequest request, CancellationToken cancellationToken)
        {
            if (!_context!.HasPermission("comic:metadata:write"))
                return PluginApiResponse.Error("Permission denied: comic:metadata:write");

            var req = JsonConvert.DeserializeObject<ApplyRequest>(request.Body ?? "{}");
            if (req == null || !Guid.TryParse(req.ComicId, out var comicId))
                return PluginApiResponse.BadRequest("Valid comicId is required");

            var metadata = await _apiClient!.GetIssueMetadataAsync(req.ComicVineId.ToString(), cancellationToken);
            if (metadata == null)
                return PluginApiResponse.NotFound($"ComicVine issue {req.ComicVineId} not found");

            var update = new ComicMetadataUpdate
            {
                ComicId = comicId,
                Series = metadata.Series,
                Number = metadata.IssueNumber,
                Count = await IsIssueCountSyncEnabled() ? metadata.Count : null,
                Title = metadata.Title,
                Summary = metadata.Summary,
                Year = metadata.Year,
                Month = metadata.Month,
                Publisher = metadata.Publisher,
                Imprint = metadata.Imprint,
                Writer = string.Join(", ", metadata.Writers),
                Penciller = string.Join(", ", metadata.Pencillers),
                Inker = string.Join(", ", metadata.Inkers),
                Colorist = string.Join(", ", metadata.Colorists),
                Letterer = string.Join(", ", metadata.Letterers),
                CoverArtist = string.Join(", ", metadata.CoverArtists),
                Editor = string.Join(", ", metadata.Editors),
                // Note: Characters, Teams, Locations not supported by ComicMetadataUpdate
                // Consider adding them to the schema or storing via Tags
                StoryArc = string.Join(", ", metadata.StoryArcs),
                Web = metadata.Web
            };

            var success = await _context.UpdateComicMetadataAsync(comicId, update);
            if (success)
            {
                _context.Info($"Applied ComicVine metadata from issue {req.ComicVineId} to comic {comicId}");
                return PluginApiResponse.Ok(new { message = "Metadata applied successfully", update });
            }
            
            return PluginApiResponse.Error("Failed to update comic metadata");
        }

        private async Task<PluginApiResponse> HandleScrapeAsync(PluginApiRequest request, CancellationToken cancellationToken)
        {
            if (!_context!.HasPermission("comic:metadata:write"))
                return PluginApiResponse.Error("Permission denied: comic:metadata:write");

            var req = JsonConvert.DeserializeObject<ScrapeRequest>(request.Body ?? "{}");
            if (req?.ComicIds == null || req.ComicIds.Length == 0)
                return PluginApiResponse.BadRequest("comicIds array is required");

            var result = await ExecuteAsync(req.ComicIds, cancellationToken);
            return PluginApiResponse.Ok(result);
        }

        private PluginApiResponse HandleGetRateLimits()
        {
            var status = _apiClient!.GetRateLimitStatus();
            return PluginApiResponse.Ok(new
            {
                endpoints = status.Values.ToList(),
                message = "Rate limits are per endpoint, 200 requests/hour each"
            });
        }

        #endregion

        #endregion

        #region IComicContextMenuPlugin

        public async Task<PluginActionResult> ExecuteAsync(Guid[] comicIds, CancellationToken cancellationToken = default)
        {
            if (_context == null || _apiClient == null)
                return PluginActionResult.Fail("Plugin not initialized");

            if (!_context.HasPermission("http:comicvine.gamespot.com"))
                return PluginActionResult.Fail("HTTP permission denied for comicvine.gamespot.com");

            if (!_context.HasPermission("comic:metadata:write"))
                return PluginActionResult.Fail("Permission denied: comic:metadata:write");

            if (comicIds == null || comicIds.Length == 0)
                return PluginActionResult.Fail("No comics selected");

            try
            {
                await EnsureApiKeyAsync();
            }
            catch (InvalidOperationException ex)
            {
                return PluginActionResult.Fail(ex.Message);
            }

            _context.Info($"Starting Comic Vine scrape for {comicIds.Length} comics");

            var processed = 0;
            var failed = 0;
            var itemResults = new Dictionary<Guid, string>();

            var autoApplyThresholdStr = await _context.GetValueAsync("auto_apply_threshold");
            var autoApplyThreshold = string.IsNullOrEmpty(autoApplyThresholdStr) ? 0.85 : double.Parse(autoApplyThresholdStr);

            foreach (var comicId in comicIds)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    var comic = await _context.GetComicMetadataAsync(comicId);
                    if (comic == null)
                    {
                        itemResults[comicId] = "Comic not found";
                        failed++;
                        continue;
                    }

                    var searchTerm = !string.IsNullOrEmpty(comic.Series) ? comic.Series : comic.FileName;
                    int? issueNum = null;
                    if (!string.IsNullOrEmpty(comic.Number) && int.TryParse(comic.Number, out var num))
                        issueNum = num;

                    var results = await _apiClient.SearchWithVolumeEnhancementAsync(searchTerm, issueNum, comic.Year, cancellationToken);

                    if (results.Count == 0)
                    {
                        itemResults[comicId] = $"No results for: {searchTerm}";
                        failed++;
                        continue;
                    }

                    var bestMatch = results[0];
                    _context.Info($"[ComicVine] DEBUG: Found {results.Count} results, bestMatch.MatchScore={bestMatch.MatchScore}, threshold={autoApplyThreshold}");
                    if ((double)bestMatch.MatchScore >= autoApplyThreshold)
                    {
                        var metadata = await _apiClient.GetIssueMetadataAsync(bestMatch.Id, cancellationToken);
                        if (metadata != null)
                        {
                            var update = new ComicMetadataUpdate
                            {
                                ComicId = comicId,
                                Series = metadata.Series,
                                Number = metadata.IssueNumber,
                                Count = await IsIssueCountSyncEnabled() ? metadata.Count : null,
                                Title = metadata.Title,
                                Summary = metadata.Summary,
                                Year = metadata.Year,
                                Publisher = metadata.Publisher
                            };

                            _context.Info($"[ComicVine] DEBUG: About to call UpdateComicMetadataAsync");
                            var success = await _context.UpdateComicMetadataAsync(comicId, update);
                            _context.Info($"[ComicVine] DEBUG: UpdateComicMetadataAsync returned {success}");
                            if (success)
                            {
                                // Save ComicVine IDs as tags for future reference
                                _context.Info($"[ComicVine] DEBUG: About to set tags for comic {comic.FileName}");
                                await _context.SetComicTagAsync(comicId, "comicvine:issue", bestMatch.Id);
                                _context.Info($"[ComicVine] DEBUG: Set comicvine:issue={bestMatch.Id}");
                                if (!string.IsNullOrEmpty(bestMatch.VolumeId))
                                {
                                    await _context.SetComicTagAsync(comicId, "comicvine:volume", bestMatch.VolumeId);
                                    _context.Info($"[ComicVine] DEBUG: Set comicvine:volume={bestMatch.VolumeId}");
                                }
                                _context.Info($"[ComicVine] Saved tags for {comic.FileName}: issue={bestMatch.Id}, volume={bestMatch.VolumeId}");
                                
                                itemResults[comicId] = $"Updated: {comic.FileName}";
                                processed++;
                                continue;
                            }
                        }
                    }

                    itemResults[comicId] = $"No confident match for: {comic.FileName} (best: {bestMatch.MatchScore:P0})";
                    failed++;
                }
                catch (Exception ex)
                {
                    _context.Error($"Error processing comic {comicId}", ex);
                    itemResults[comicId] = $"Error: {ex.Message}";
                    failed++;
                }
            }

            _context.Info($"Comic Vine scrape complete: {processed} updated, {failed} failed");

            return new PluginActionResult
            {
                Success = processed > 0,
                Message = $"Processed {processed} comics, {failed} failed",
                ItemsProcessed = processed,
                ItemsFailed = failed,
                ItemResults = itemResults
            };
        }

        #endregion

        #region IScheduledTaskPlugin

        public IReadOnlyList<ScheduledTaskDefinition> GetScheduledTasks()
        {
            return new List<ScheduledTaskDefinition>
            {
                new ScheduledTaskDefinition
                {
                    TaskId = "auto-scrape-new-comics",
                    Name = "Auto-Scrape New Comics",
                    Description = "Automatically scrape metadata from Comic Vine for newly imported comics",
                    TriggerType = TaskTriggerType.LibraryScanPerBook,
                    SortOrder = 50,
                    EnabledByDefault = false,
                    CanDisable = true,
                    AllowManualRun = false,
                    Category = "Metadata",
                    ProcessOnlyNewBooks = true
                },
                new ScheduledTaskDefinition
                {
                    TaskId = "scrape-backlog",
                    Name = "Scrape Backlog via Smart List",
                    Description = "Scrape metadata for comics in the configured Backlog Smart List",
                    TriggerType = TaskTriggerType.TimeBased,
                    DefaultCron = "0 0 * * *", // Daily at midnight
                    EnabledByDefault = false,
                    CanDisable = true,
                    AllowManualRun = true,
                    Category = "Metadata"
                }
            };
        }

        public async Task<PluginActionResult> ExecuteTaskAsync(string taskId, CancellationToken cancellationToken = default)
        {
            if (_context == null) return PluginActionResult.Fail("Plugin not initialized");

            if (taskId == "scrape-backlog")
            {
                var smartListNameOrId = await _context.GetValueAsync("backlog_smart_list");
                if (string.IsNullOrWhiteSpace(smartListNameOrId))
                {
                    _context.Info("Backlog scraping skipped: 'backlog_smart_list' setting is empty.");
                    return PluginActionResult.Ok("Skipped: No backlog smart list configured");
                }

                _context.Info($"Fetching comics from backlog smart list: {smartListNameOrId}");
                
                try 
                {
                    var comicIds = await _context.GetComicsInSmartListAsync(smartListNameOrId);
                    
                    if (comicIds.Count == 0)
                    {
                        _context.Info("No comics found in backlog smart list.");
                        return PluginActionResult.Ok("No comics to process");
                    }

                    _context.Info($"Found {comicIds.Count} comics in backlog. Starting scrape...");
                    
                    // Re-use the existing scrape logic
                    return await ExecuteAsync(comicIds.ToArray(), cancellationToken);
                }
                catch (Exception ex)
                {
                    _context.Error("Failed to execute backlog scrape", ex);
                    return PluginActionResult.Fail($"Error: {ex.Message}");
                }
            }

            return PluginActionResult.Ok($"Task {taskId} is a per-book task or unknown");
        }

        public async Task<PluginActionResult> ExecutePerBookTaskAsync(string taskId, ScannedBookContext context, CancellationToken cancellationToken = default)
        {
            if (taskId != "auto-scrape-new-comics")
                return PluginActionResult.Fail($"Unknown task: {taskId}");

            if (_context == null || context.ComicId == null || !context.IsNew)
                return PluginActionResult.Ok("Skipped - not a new comic");

            try
            {
                _context.Info($"Auto-scraping Comic Vine for: {context.FileName}");
                var result = await ExecuteAsync(new[] { context.ComicId.Value }, cancellationToken);
                
                return result.Success && result.ItemsProcessed > 0
                    ? PluginActionResult.Ok($"Successfully scraped {context.FileName}")
                    : PluginActionResult.Ok($"No match found for {context.FileName}");
            }
            catch (Exception ex)
            {
                _context.Error($"Error scraping {context.FileName}: {ex.Message}");
                return PluginActionResult.Fail(ex.Message);
            }
        }

        #endregion

        #region ILibraryScanPlugin (Legacy)

        public Task OnScanStartAsync(string directoryPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task OnComicScannedAsync(ScanComicContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task OnFileScannedAsync(ScanFileContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task OnScanCompleteAsync(ScanResultSummary result, CancellationToken cancellationToken = default) => Task.CompletedTask;

        #endregion

        #region Request DTOs

        private async Task<bool> IsIssueCountSyncEnabled()
        {
            if (_context == null) return false;
            var value = await _context.GetValueAsync("enable_issue_count_sync");
            return value?.ToLower() == "true";
        }

        private class SearchRequest
        {
            [JsonProperty("query")] public string? Query { get; set; }
            [JsonProperty("issueNumber")] public string? IssueNumber { get; set; }
            [JsonProperty("year")] public int? Year { get; set; }
            [JsonProperty("limit")] public int? Limit { get; set; }
            [JsonProperty("useVolumeSearch")] public bool? UseVolumeSearch { get; set; }
        }

        private class VolumeSearchRequest
        {
            [JsonProperty("query")] public string? Query { get; set; }
            [JsonProperty("year")] public int? Year { get; set; }
        }

        private class ApplyRequest
        {
            [JsonProperty("comicId")] public string? ComicId { get; set; }
            [JsonProperty("comicVineId")] public int ComicVineId { get; set; }
            [JsonProperty("mergeMode")] public bool? MergeMode { get; set; }
        }

        private class ScrapeRequest
        {
            [JsonProperty("comicIds")] public Guid[]? ComicIds { get; set; }
            [JsonProperty("autoApplyThreshold")] public double? AutoApplyThreshold { get; set; }
            [JsonProperty("mergeMode")] public bool? MergeMode { get; set; }
        }

        #endregion
    }
}
