using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicRow.PluginSystem;
using Newtonsoft.Json;

namespace ComicRow.Plugins.ComicVineScraper
{
    /// <summary>
    /// Comic Vine Scraper plugin for ComicRow.
    /// Scrapes comic metadata from Comic Vine (comicvine.gamespot.com).
    /// Implements IPluginApiProvider to expose REST API endpoints.
    /// </summary>
    public class ComicVineScraperPlugin : IComicContextMenuPlugin, ILibraryScanPlugin, IPluginApiProvider
    {
        private IPluginContext? _context;
        
        public string Name => "ComicVine Scraper";
        public string Version => "1.2.0";

        public void SetContext(IPluginContext context)
        {
            _context = context;
            _context.Info("ComicVine Scraper plugin initialized");
        }

        public void OnUnload()
        {
            _context?.Info("ComicVine Scraper plugin unloading");
        }

        #region IPluginApiProvider Implementation

        public IReadOnlyList<PluginApiEndpoint> GetApiEndpoints()
        {
            return new List<PluginApiEndpoint>
            {
                new PluginApiEndpoint
                {
                    Route = "search",
                    Methods = new[] { "POST" },
                    Summary = "Search ComicVine for comics",
                    Description = "Search the ComicVine database for comic issues matching the provided criteria",
                    Tags = new[] { "ComicVine" },
                    RequestBody = new PluginApiRequestBody
                    {
                        Description = "Search criteria",
                        Required = true,
                        Content = new Dictionary<string, PluginApiSchema>
                        {
                            ["application/json"] = new PluginApiSchema
                            {
                                Type = "object",
                                Properties = new Dictionary<string, PluginApiProperty>
                                {
                                    ["query"] = new PluginApiProperty { Type = "string", Description = "Search query (series name, title, etc.)" },
                                    ["issueNumber"] = new PluginApiProperty { Type = "string", Description = "Issue number (optional)", Nullable = true },
                                    ["year"] = new PluginApiProperty { Type = "integer", Description = "Publication year (optional)", Nullable = true },
                                    ["limit"] = new PluginApiProperty { Type = "integer", Description = "Max results (default: 10)", Nullable = true }
                                },
                                Example = new { query = "Batman", issueNumber = "1", limit = 10 }
                            }
                        }
                    },
                    Responses = new Dictionary<string, PluginApiResponseDef>
                    {
                        ["200"] = new PluginApiResponseDef 
                        { 
                            Description = "Search results",
                            Content = new Dictionary<string, PluginApiSchema>
                            {
                                ["application/json"] = new PluginApiSchema
                                {
                                    Type = "object",
                                    Properties = new Dictionary<string, PluginApiProperty>
                                    {
                                        ["results"] = new PluginApiProperty { Type = "array", Description = "Matching comic issues" },
                                        ["totalResults"] = new PluginApiProperty { Type = "integer", Description = "Total matches found" }
                                    }
                                }
                            }
                        },
                        ["400"] = new PluginApiResponseDef { Description = "Invalid request" },
                        ["401"] = new PluginApiResponseDef { Description = "API key not configured" }
                    }
                },
                new PluginApiEndpoint
                {
                    Route = "scrape",
                    Methods = new[] { "POST" },
                    Summary = "Scrape metadata for comics",
                    Description = "Scrape and apply ComicVine metadata to specified comics",
                    Tags = new[] { "ComicVine" },
                    RequestBody = new PluginApiRequestBody
                    {
                        Description = "Comics to scrape",
                        Required = true,
                        Content = new Dictionary<string, PluginApiSchema>
                        {
                            ["application/json"] = new PluginApiSchema
                            {
                                Type = "object",
                                Properties = new Dictionary<string, PluginApiProperty>
                                {
                                    ["comicIds"] = new PluginApiProperty { Type = "array", Description = "Array of comic IDs to scrape" },
                                    ["autoApplyThreshold"] = new PluginApiProperty { Type = "number", Description = "Confidence threshold (0-1)", Nullable = true },
                                    ["mergeMode"] = new PluginApiProperty { Type = "boolean", Description = "Only fill empty fields", Nullable = true }
                                },
                                Example = new { comicIds = new[] { "guid-here" }, autoApplyThreshold = 0.85, mergeMode = true }
                            }
                        }
                    },
                    Responses = new Dictionary<string, PluginApiResponseDef>
                    {
                        ["200"] = new PluginApiResponseDef { Description = "Scrape results" },
                        ["400"] = new PluginApiResponseDef { Description = "Invalid request" }
                    }
                },
                new PluginApiEndpoint
                {
                    Route = "issue/{issueId}",
                    Methods = new[] { "GET" },
                    Summary = "Get ComicVine issue details",
                    Description = "Fetch detailed information about a specific ComicVine issue",
                    Tags = new[] { "ComicVine" },
                    Parameters = new List<PluginApiParameter>
                    {
                        new PluginApiParameter
                        {
                            Name = "issueId",
                            In = "path",
                            Required = true,
                            Description = "ComicVine issue ID",
                            Type = "integer"
                        }
                    },
                    Responses = new Dictionary<string, PluginApiResponseDef>
                    {
                        ["200"] = new PluginApiResponseDef { Description = "Issue details" },
                        ["404"] = new PluginApiResponseDef { Description = "Issue not found" }
                    }
                },
                new PluginApiEndpoint
                {
                    Route = "match/{comicId}",
                    Methods = new[] { "GET" },
                    Summary = "Get potential matches for a comic",
                    Description = "Search ComicVine for potential matches based on the comic's existing metadata",
                    Tags = new[] { "ComicVine" },
                    Parameters = new List<PluginApiParameter>
                    {
                        new PluginApiParameter
                        {
                            Name = "comicId",
                            In = "path",
                            Required = true,
                            Description = "Comic ID to find matches for",
                            Type = "string"
                        }
                    },
                    Responses = new Dictionary<string, PluginApiResponseDef>
                    {
                        ["200"] = new PluginApiResponseDef { Description = "Potential matches with confidence scores" },
                        ["404"] = new PluginApiResponseDef { Description = "Comic not found" }
                    }
                },
                new PluginApiEndpoint
                {
                    Route = "apply",
                    Methods = new[] { "POST" },
                    Summary = "Apply ComicVine match to a comic",
                    Description = "Apply metadata from a specific ComicVine issue to a comic in the library",
                    Tags = new[] { "ComicVine" },
                    RequestBody = new PluginApiRequestBody
                    {
                        Description = "Match to apply",
                        Required = true,
                        Content = new Dictionary<string, PluginApiSchema>
                        {
                            ["application/json"] = new PluginApiSchema
                            {
                                Type = "object",
                                Properties = new Dictionary<string, PluginApiProperty>
                                {
                                    ["comicId"] = new PluginApiProperty { Type = "string", Description = "Comic ID to update" },
                                    ["comicVineId"] = new PluginApiProperty { Type = "integer", Description = "ComicVine issue ID to apply" },
                                    ["mergeMode"] = new PluginApiProperty { Type = "boolean", Description = "Only fill empty fields", Nullable = true }
                                },
                                Example = new { comicId = "guid-here", comicVineId = 123456, mergeMode = true }
                            }
                        }
                    },
                    Responses = new Dictionary<string, PluginApiResponseDef>
                    {
                        ["200"] = new PluginApiResponseDef { Description = "Metadata applied successfully" },
                        ["400"] = new PluginApiResponseDef { Description = "Invalid request" },
                        ["404"] = new PluginApiResponseDef { Description = "Comic or issue not found" }
                    }
                }
            };
        }

        public async Task<PluginApiResponse> HandleRequestAsync(PluginApiRequest request, CancellationToken cancellationToken = default)
        {
            if (_context == null)
                return PluginApiResponse.Error("Plugin context not initialized");

            // Check required permission for HTTP access
            if (!_context.HasPermission("http:comicvine.gamespot.com"))
            {
                _context.Warning("HTTP permission denied for comicvine.gamespot.com");
                return PluginApiResponse.Error("HTTP permission denied");
            }

            try
            {
                return request.Route switch
                {
                    "search" => await HandleSearchAsync(request, cancellationToken),
                    "scrape" => await HandleScrapeAsync(request, cancellationToken),
                    var r when r.StartsWith("issue/") => await HandleGetIssueAsync(request, cancellationToken),
                    var r when r.StartsWith("match/") => await HandleGetMatchesAsync(request, cancellationToken),
                    "apply" => await HandleApplyAsync(request, cancellationToken),
                    _ => PluginApiResponse.NotFound($"Unknown route: {request.Route}")
                };
            }
            catch (Exception ex)
            {
                _context.Error($"API error handling {request.Route}", ex);
                return PluginApiResponse.Error(ex.Message);
            }
        }

        private async Task<PluginApiResponse> HandleSearchAsync(PluginApiRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(request.Body))
                return PluginApiResponse.BadRequest("Request body required");

            var searchRequest = JsonConvert.DeserializeObject<SearchRequest>(request.Body);
            if (searchRequest == null || string.IsNullOrEmpty(searchRequest.Query))
                return PluginApiResponse.BadRequest("Query is required");

            var apiKey = await GetApiKeyAsync();
            if (string.IsNullOrEmpty(apiKey))
                return new PluginApiResponse { StatusCode = 401, Body = new { error = "Comic Vine API key not configured" } };

            var limit = searchRequest.Limit ?? 10;
            var searchUrl = $"https://comicvine.gamespot.com/api/search/?api_key={apiKey}&format=json&resources=issue&query={Uri.EscapeDataString(searchRequest.Query)}&limit={limit}";
            
            var content = await _context!.GetAsync(searchUrl);
            var result = JsonConvert.DeserializeObject<ComicVineSearchResult>(content);

            return PluginApiResponse.Ok(new
            {
                results = result?.Results ?? new List<ComicVineIssue>(),
                totalResults = result?.Results?.Count ?? 0
            });
        }

        private async Task<PluginApiResponse> HandleScrapeAsync(PluginApiRequest request, CancellationToken cancellationToken)
        {
            // Check write permission
            if (!_context!.HasPermission("comic:metadata"))
            {
                return PluginApiResponse.Error("Permission denied: comic:metadata");
            }

            if (string.IsNullOrEmpty(request.Body))
                return PluginApiResponse.BadRequest("Request body required");

            var scrapeRequest = JsonConvert.DeserializeObject<ScrapeRequest>(request.Body);
            if (scrapeRequest?.ComicIds == null || scrapeRequest.ComicIds.Length == 0)
                return PluginApiResponse.BadRequest("comicIds array is required");

            var result = await ExecuteAsync(scrapeRequest.ComicIds, cancellationToken);
            return PluginApiResponse.Ok(result);
        }

        private async Task<PluginApiResponse> HandleGetIssueAsync(PluginApiRequest request, CancellationToken cancellationToken)
        {
            if (!request.RouteValues.TryGetValue("issueId", out var issueIdStr) || !int.TryParse(issueIdStr, out var issueId))
                return PluginApiResponse.BadRequest("Invalid issue ID");

            var apiKey = await GetApiKeyAsync();
            if (string.IsNullOrEmpty(apiKey))
                return new PluginApiResponse { StatusCode = 401, Body = new { error = "Comic Vine API key not configured" } };

            var url = $"https://comicvine.gamespot.com/api/issue/4000-{issueId}/?api_key={apiKey}&format=json";
            var content = await _context!.GetAsync(url);
            var result = JsonConvert.DeserializeObject<ComicVineIssueResult>(content);

            if (result?.Results == null)
                return PluginApiResponse.NotFound($"Issue {issueId} not found");

            return PluginApiResponse.Ok(result.Results);
        }

        private async Task<PluginApiResponse> HandleGetMatchesAsync(PluginApiRequest request, CancellationToken cancellationToken)
        {
            if (!request.RouteValues.TryGetValue("comicId", out var comicIdStr) || !Guid.TryParse(comicIdStr, out var comicId))
                return PluginApiResponse.BadRequest("Invalid comic ID");

            // Check read permission
            if (!_context!.HasPermission("comic:read"))
            {
                return PluginApiResponse.Error("Permission denied: comic:read");
            }

            var comic = await _context.GetComicMetadataAsync(comicId);
            if (comic == null)
                return PluginApiResponse.NotFound($"Comic {comicId} not found");

            var apiKey = await GetApiKeyAsync();
            if (string.IsNullOrEmpty(apiKey))
                return new PluginApiResponse { StatusCode = 401, Body = new { error = "Comic Vine API key not configured" } };

            var searchTerm = !string.IsNullOrEmpty(comic.Series) ? comic.Series : comic.FileName;
            var searchUrl = $"https://comicvine.gamespot.com/api/search/?api_key={apiKey}&format=json&resources=issue&query={Uri.EscapeDataString(searchTerm)}&limit=10";
            
            var content = await _context.GetAsync(searchUrl);
            var result = JsonConvert.DeserializeObject<ComicVineSearchResult>(content);

            var matches = new List<object>();
            if (result?.Results != null)
            {
                foreach (var issue in result.Results)
                {
                    var confidence = CalculateConfidence(comic, issue);
                    matches.Add(new
                    {
                        comicVineId = issue.Id,
                        name = issue.Name,
                        issueNumber = issue.IssueNumber,
                        volume = issue.Volume?.Name,
                        publisher = issue.Volume?.Publisher?.Name,
                        coverDate = issue.CoverDate,
                        confidence
                    });
                }
            }

            return PluginApiResponse.Ok(new
            {
                comic = new { comic.Series, comic.Number, comic.FileName },
                matches = matches.OrderByDescending(m => ((dynamic)m).confidence).ToList()
            });
        }

        private async Task<PluginApiResponse> HandleApplyAsync(PluginApiRequest request, CancellationToken cancellationToken)
        {
            // Check write permission
            if (!_context!.HasPermission("comic:metadata"))
            {
                return PluginApiResponse.Error("Permission denied: comic:metadata");
            }

            if (string.IsNullOrEmpty(request.Body))
                return PluginApiResponse.BadRequest("Request body required");

            var applyRequest = JsonConvert.DeserializeObject<ApplyRequest>(request.Body);
            if (applyRequest == null || !Guid.TryParse(applyRequest.ComicId, out var comicId))
                return PluginApiResponse.BadRequest("Valid comicId is required");

            var apiKey = await GetApiKeyAsync();
            if (string.IsNullOrEmpty(apiKey))
                return new PluginApiResponse { StatusCode = 401, Body = new { error = "Comic Vine API key not configured" } };

            // Fetch issue details
            var url = $"https://comicvine.gamespot.com/api/issue/4000-{applyRequest.ComicVineId}/?api_key={apiKey}&format=json";
            var content = await _context.GetAsync(url);
            var result = JsonConvert.DeserializeObject<ComicVineIssueResult>(content);

            if (result?.Results == null)
                return PluginApiResponse.NotFound($"ComicVine issue {applyRequest.ComicVineId} not found");

            var issue = result.Results;
            var update = new ComicMetadataUpdate
            {
                ComicId = comicId,
                Series = issue.Volume?.Name,
                Number = issue.IssueNumber,
                Title = issue.Name,
                Summary = StripHtml(issue.Description),
                Year = issue.CoverDate?.Year,
                Publisher = issue.Volume?.Publisher?.Name
            };

            var success = await _context.UpdateComicMetadataAsync(comicId, update);
            if (success)
            {
                _context.Info($"Applied ComicVine metadata from issue {applyRequest.ComicVineId} to comic {comicId}");
                return PluginApiResponse.Ok(new { message = "Metadata applied successfully", update });
            }
            else
            {
                return PluginApiResponse.Error("Failed to update comic metadata");
            }
        }

        private async Task<string?> GetApiKeyAsync()
        {
            return await _context!.GetValueAsync("api_key");
        }

        private double CalculateConfidence(ComicMetadata comic, ComicVineIssue issue)
        {
            double score = 0.0;
            
            // Series name match
            if (!string.IsNullOrEmpty(comic.Series) && !string.IsNullOrEmpty(issue.Volume?.Name))
            {
                if (comic.Series.Equals(issue.Volume.Name, StringComparison.OrdinalIgnoreCase))
                    score += 0.5;
                else if (comic.Series.IndexOf(issue.Volume.Name, StringComparison.OrdinalIgnoreCase) >= 0 ||
                         issue.Volume.Name.IndexOf(comic.Series, StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 0.3;
            }

            // Issue number match
            if (comic.Number == issue.IssueNumber)
                score += 0.4;

            // Year match
            if (comic.Year.HasValue && issue.CoverDate?.Year == comic.Year.Value)
                score += 0.1;

            return Math.Min(score, 1.0);
        }

        #endregion

        #region Request/Response DTOs

        private class SearchRequest
        {
            [JsonProperty("query")]
            public string? Query { get; set; }
            [JsonProperty("issueNumber")]
            public string? IssueNumber { get; set; }
            [JsonProperty("year")]
            public int? Year { get; set; }
            [JsonProperty("limit")]
            public int? Limit { get; set; }
        }

        private class ScrapeRequest
        {
            [JsonProperty("comicIds")]
            public Guid[]? ComicIds { get; set; }
            [JsonProperty("autoApplyThreshold")]
            public double? AutoApplyThreshold { get; set; }
            [JsonProperty("mergeMode")]
            public bool? MergeMode { get; set; }
        }

        private class ApplyRequest
        {
            [JsonProperty("comicId")]
            public string? ComicId { get; set; }
            [JsonProperty("comicVineId")]
            public int ComicVineId { get; set; }
            [JsonProperty("mergeMode")]
            public bool? MergeMode { get; set; }
        }

        #endregion

        /// <summary>
        /// Execute metadata scraping on selected comics (IComicContextMenuPlugin).
        /// </summary>
        public async Task<PluginActionResult> ExecuteAsync(Guid[] comicIds, CancellationToken cancellationToken = default)
        {
            if (_context == null)
                return PluginActionResult.Fail("Plugin context not initialized");

            // Check permissions
            if (!_context.HasPermission("http:comicvine.gamespot.com"))
            {
                return PluginActionResult.Fail("HTTP permission denied for comicvine.gamespot.com");
            }
            if (!_context.HasPermission("comic:read"))
            {
                return PluginActionResult.Fail("Permission denied: comic:read");
            }
                
            if (comicIds == null || comicIds.Length == 0)
            {
                return PluginActionResult.Fail("No comics selected");
            }

            _context.Info($"Starting Comic Vine scrape for {comicIds.Length} comics");

            var processed = 0;
            var failed = 0;
            var itemResults = new Dictionary<Guid, string>();

            // Get settings
            var autoApplyThresholdStr = await _context.GetValueAsync("auto_apply_threshold");
            var autoApplyThreshold = string.IsNullOrEmpty(autoApplyThresholdStr) ? 0.85 : double.Parse(autoApplyThresholdStr);
            
            var mergeModeStr = await _context.GetValueAsync("merge_mode");
            var mergeMode = string.IsNullOrEmpty(mergeModeStr) || mergeModeStr == "true";

            var apiKey = await GetApiKeyAsync();
            if (string.IsNullOrEmpty(apiKey))
            {
                return PluginActionResult.Fail("Comic Vine API key not configured. Go to Settings → Plugins → ComicVine Scraper.");
            }

            foreach (var comicId in comicIds)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    // Read comic metadata
                    var comic = await _context.GetComicMetadataAsync(comicId);
                    if (comic == null)
                    {
                        itemResults[comicId] = "Comic not found";
                        failed++;
                        continue;
                    }

                    // Build search query
                    var searchTerm = !string.IsNullOrEmpty(comic.Series) ? comic.Series : comic.FileName;

                    var searchUrl = $"https://comicvine.gamespot.com/api/search/?api_key={apiKey}&format=json&resources=issue&query={Uri.EscapeDataString(searchTerm)}";
                    
                    var content = await _context.GetAsync(searchUrl);
                    var searchResult = JsonConvert.DeserializeObject<ComicVineSearchResult>(content);

                    if (searchResult?.Results == null || searchResult.Results.Count == 0)
                    {
                        itemResults[comicId] = $"No results for: {searchTerm}";
                        failed++;
                        continue;
                    }

                    // Find best match
                    var bestMatch = FindBestMatch(comic, searchResult.Results, autoApplyThreshold);
                    
                    if (bestMatch != null)
                    {
                        // Check write permission before updating
                        if (!_context.HasPermission("comic:metadata"))
                        {
                            itemResults[comicId] = "Permission denied: comic:metadata";
                            failed++;
                            continue;
                        }

                        var update = new ComicMetadataUpdate
                        {
                            ComicId = comicId,
                            Series = bestMatch.Volume?.Name,
                            Number = bestMatch.IssueNumber,
                            Title = bestMatch.Name,
                            Summary = StripHtml(bestMatch.Description),
                            Year = bestMatch.CoverDate?.Year,
                            Publisher = bestMatch.Volume?.Publisher?.Name
                        };

                        var success = await _context.UpdateComicMetadataAsync(comicId, update);
                        if (success)
                        {
                            itemResults[comicId] = $"Updated: {comic.FileName}";
                            processed++;
                        }
                        else
                        {
                            itemResults[comicId] = $"Failed to update: {comic.FileName}";
                            failed++;
                        }
                    }
                    else
                    {
                        itemResults[comicId] = $"No confident match for: {comic.FileName}";
                        failed++;
                    }
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

        #region ILibraryScanPlugin Implementation

        public async Task OnScanStartAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (_context == null) return;
            
            var autoScanStr = await _context.GetValueAsync("scan_on_library_import");
            var autoScan = autoScanStr == "true";
            
            if (autoScan)
            {
                _context.Info($"Comic Vine Scraper: Library scan starting in {directoryPath}");
            }
        }

        public async Task OnComicScannedAsync(ScanComicContext context, CancellationToken cancellationToken = default)
        {
            if (_context == null) return;
            
            var autoScanStr = await _context.GetValueAsync("scan_on_library_import");
            var autoScan = autoScanStr == "true";
            
            if (!autoScan || !context.IsNew || context.ComicId == null)
                return;

            // Only process new comics during scan if auto-scan is enabled
            await ExecuteAsync(new[] { context.ComicId.Value }, cancellationToken);
        }

        public Task OnFileScannedAsync(ScanFileContext context, CancellationToken cancellationToken = default)
        {
            // Not used - we process comics, not raw files
            return Task.CompletedTask;
        }

        public Task OnScanCompleteAsync(ScanResultSummary result, CancellationToken cancellationToken = default)
        {
            _context?.Info($"Comic Vine Scraper: Library scan complete - {result.ComicsAdded} new comics");
            return Task.CompletedTask;
        }

        #endregion

        #region Private Helpers

        private ComicVineIssue? FindBestMatch(ComicMetadata comic, List<ComicVineIssue>? results, double threshold)
        {
            if (results == null || results.Count == 0)
                return null;
                
            // Simple matching logic - production code should use more sophisticated matching
            foreach (var result in results)
            {
                // Check series name similarity
                if (result.Volume?.Name != null && 
                    !string.IsNullOrEmpty(comic.Series) &&
                    comic.Series.IndexOf(result.Volume.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Check issue number
                    if (comic.Number == result.IssueNumber)
                    {
                        return result;
                    }
                }
            }
            
            // Return first result if no exact match (let user confirm)
            return results.Count > 0 ? results[0] : null;
        }

        private string? StripHtml(string? html)
        {
            if (string.IsNullOrEmpty(html))
                return html;
                
            // Simple HTML stripping - remove tags
            return System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", string.Empty);
        }

        #endregion
    }

    #region Comic Vine API Models

    public class ComicVineSearchResult
    {
        [JsonProperty("results")]
        public List<ComicVineIssue>? Results { get; set; }
        
        [JsonProperty("error")]
        public string? Error { get; set; }
        
        [JsonProperty("status_code")]
        public int StatusCode { get; set; }
    }

    public class ComicVineIssueResult
    {
        [JsonProperty("results")]
        public ComicVineIssue? Results { get; set; }
        
        [JsonProperty("error")]
        public string? Error { get; set; }
        
        [JsonProperty("status_code")]
        public int StatusCode { get; set; }
    }

    public class ComicVineIssue
    {
        [JsonProperty("id")]
        public int Id { get; set; }
        
        [JsonProperty("name")]
        public string? Name { get; set; }
        
        [JsonProperty("issue_number")]
        public string? IssueNumber { get; set; }
        
        [JsonProperty("description")]
        public string? Description { get; set; }
        
        [JsonProperty("cover_date")]
        public DateTime? CoverDate { get; set; }
        
        [JsonProperty("volume")]
        public ComicVineVolume? Volume { get; set; }
    }

    public class ComicVineVolume
    {
        [JsonProperty("id")]
        public int Id { get; set; }
        
        [JsonProperty("name")]
        public string? Name { get; set; }
        
        [JsonProperty("publisher")]
        public ComicVinePublisher? Publisher { get; set; }
    }

    public class ComicVinePublisher
    {
        [JsonProperty("id")]
        public int Id { get; set; }
        
        [JsonProperty("name")]
        public string? Name { get; set; }
    }

    #endregion
}