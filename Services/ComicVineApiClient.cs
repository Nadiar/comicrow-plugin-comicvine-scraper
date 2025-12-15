using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using ComicRow.PluginSystem;
using ComicRow.Plugins.ComicVineScraper.Models;

namespace ComicRow.Plugins.ComicVineScraper.Services
{
    /// <summary>
    /// Comic Vine API client with rate limiting, search, and metadata retrieval.
    /// Based on ComicTagger implementation with enhancements for volume-based search.
    /// 
    /// RATE LIMITS:
    /// - 200 requests per hour per endpoint
    /// - 1 second minimum between any requests
    /// - HTTP 420 response when rate limit exceeded
    /// </summary>
    public class ComicVineApiClient
    {
        private readonly IPluginContext _context;
        private string? _apiKey;
        private DateTime _lastRequestTime = DateTime.MinValue;
        
        // Per-endpoint rate limiting
        private readonly ConcurrentDictionary<string, EndpointRateLimit> _endpointLimits = new();
        
        private const string BaseUrl = "https://comicvine.gamespot.com/api/";
        private const int RateLimitDelayMs = 1000;
        private const int MaxRequestsPerHourPerEndpoint = 200;
        private static readonly TimeSpan RateLimitWindow = TimeSpan.FromHours(1);
        
        // Endpoint names
        private const string EndpointSearch = "search";
        private const string EndpointIssue = "issue";
        private const string EndpointIssues = "issues";
        private const string EndpointVolume = "volume";
        private const string EndpointVolumes = "volumes";
        
        public ComicVineApiClient(IPluginContext context)
        {
            _context = context;
        }
        
        public void SetApiKey(string apiKey) => _apiKey = apiKey;
        public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);
        
        #region Search Methods
        
        /// <summary>
        /// Search for issues directly using the search endpoint.
        /// </summary>
        public async Task<List<MetadataSearchResult>> SearchIssuesAsync(
            string series, 
            int? issueNumber = null, 
            int? year = null,
            CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
                throw new InvalidOperationException("API key not configured");
            
            await EnforceRateLimitAsync(cancellationToken, EndpointSearch);
            
            var queryText = series;
            if (issueNumber.HasValue)
                queryText += $" {issueNumber.Value}";
            
            var url = $"{BaseUrl}search?api_key={_apiKey}&format=json&resources=issue";
            url += $"&query={HttpUtility.UrlEncode(queryText)}";
            url += "&field_list=id,name,issue_number,volume,cover_date,image,site_detail_url,description";
            url += "&limit=25";
            
            _context.Debug($"[ComicVine] Searching issues: query=\"{queryText}\", API Key configured: {IsConfigured}");
            
            var response = await GetAsync(url, cancellationToken);
            var apiResponse = JsonSerializer.Deserialize<ComicVineResponse<List<ComicVineIssue>>>(response);
            
            if (apiResponse?.StatusCode != 1)
            {
                _context.Warning($"[ComicVine] API error: StatusCode={apiResponse?.StatusCode}");
                return new List<MetadataSearchResult>();
            }
            
            var results = (apiResponse.Results ?? new List<ComicVineIssue>())
                .Select(r => new MetadataSearchResult
                {
                    Id = r.Id.ToString(),
                    Series = r.Volume?.Name ?? "Unknown",
                    IssueNumber = r.IssueNumber,
                    Title = r.Name,
                    Year = ParseYear(r.CoverDate),
                    Month = ParseMonth(r.CoverDate),
                    Publisher = r.Volume?.Publisher?.Name,
                    CoverUrl = r.Image?.MediumUrl ?? r.Image?.SmallUrl,
                    Description = StripHtml(r.Description),
                    VolumeId = r.Volume?.Id.ToString(),
                    VolumeStartYear = r.Volume?.StartYear,
                    Count = r.Volume?.CountOfIssues,
                    MatchScore = CalculateMatchScore(series, issueNumber, year, r)
                })
                .OrderByDescending(x => x.MatchScore)
                .ToList();
            
            if (results.Count == 0)
                _context.Warning($"[ComicVine] No results found for \"{queryText}\" - Comic may not exist in ComicVine database");
            else
                _context.Debug($"[ComicVine] Found {results.Count} result(s) for \"{queryText}\"");
            
            return results;
        }
        
        /// <summary>
        /// Enhanced search using volume-first approach (more reliable for finding exact issues).
        /// </summary>
        public async Task<List<MetadataSearchResult>> SearchWithVolumeEnhancementAsync(
            string series, 
            int? issueNumber = null, 
            int? year = null,
            CancellationToken cancellationToken = default)
        {
            _context.Info($"[ComicVine] SearchWithVolumeEnhancementAsync: series=\"{ series}\", issue=#{issueNumber}, year={year}");
            
            if (!issueNumber.HasValue)
            {
                _context.Info($"[ComicVine] No issue number, using direct search");
                return await SearchIssuesAsync(series, issueNumber, year, cancellationToken);
            }
            
            _context.Info($"[ComicVine] Starting volume-enhanced search");
            
            // Phase 1: Search for volumes
            var volumeResults = await SearchVolumesAsync(series, null, cancellationToken);
            
            if (volumeResults.Count == 0)
            {
            _context.Info($"[ComicVine] No volumes found, falling back to direct search");
                return await SearchIssuesAsync(series, issueNumber, year, cancellationToken);
            }
            
            _context.Info($"[ComicVine] Found {volumeResults.Count} volumes");
            
            // Score and filter volumes
            // NOTE: Don't filter by issue count - ComicVine's counts may be incomplete or incorrect
            // (e.g., Detective Comics 2016 shows 170 issues but actually has 1070+)
            var volumeScores = volumeResults
                .Select(v => new { Volume = v, Score = CalculateVolumeMatchScore(series, year, v, issueNumber) })
                .ToList();
            
            _context.Info($"[ComicVine] Top scored volumes: {string.Join(", ", volumeScores.OrderByDescending(x => x.Score).Take(3).Select(vs => $"{vs.Volume.Name}={vs.Score:P0}"))}");
            
            var scoredVolumes = volumeScores
                .OrderByDescending(x => x.Score)
                .Take(5)
                .ToList();
            
            if (scoredVolumes.Count == 0)
            {
                _context.Debug($"[ComicVine] No volumes have issue #{issueNumber} (max issue counts in results: {string.Join(", ", volumeResults.Select(v => $"{v.Name}({v.IssueCount ?? 0})"))}), falling back to direct issue search");
                return await SearchIssuesAsync(series, issueNumber, year, cancellationToken);
            }
            
            _context.Info($"[ComicVine] Testing {scoredVolumes.Count} top volumes");
            
            var volumeIssueMatches = new List<MetadataSearchResult>();
            
            // Phase 2: Get specific issue from each volume
            foreach (var sv in scoredVolumes)
            {
                _context.Debug($"[ComicVine] Trying volume \"{sv.Volume.Name}\" (ID: {sv.Volume.Id}, Score: {sv.Score:P0}) for issue #{issueNumber}...");
                
                var matchingIssue = await GetIssueByVolumeAndNumberAsync(
                    sv.Volume.Id, issueNumber.Value, cancellationToken);
                
                if (matchingIssue != null)
                {
                    _context.Debug($"[ComicVine] Found issue #{issueNumber} in volume \"{sv.Volume.Name}\" (ID: {sv.Volume.Id})");
                    
                    var yearBonus = 0m;
                    if (year.HasValue && matchingIssue.Year.HasValue)
                    {
                        var yearDiff = Math.Abs(year.Value - matchingIssue.Year.Value);
                        if (yearDiff == 0) yearBonus = 0.10m;
                        else if (yearDiff == 1) yearBonus = 0.05m;
                    }
                    
                    matchingIssue.MatchScore = Math.Min(1.0m, 0.30m + (sv.Score * 0.50m) + yearBonus + 0.10m);
                    matchingIssue.Series = sv.Volume.Name;
                    matchingIssue.VolumeId = sv.Volume.Id;
                    matchingIssue.Publisher = sv.Volume.Publisher;
                    matchingIssue.VolumeStartYear = sv.Volume.StartYear;
                    
                    volumeIssueMatches.Add(matchingIssue);
                    
                    if (sv.Score >= 0.9m)
                        break;
                }
                else
                {
                    _context.Debug($"[ComicVine] Issue #{issueNumber} not found in volume \"{sv.Volume.Name}\" (has ~{sv.Volume.IssueCount} issues)");
                }
            }
            
            if (volumeIssueMatches.Count > 0)
                return volumeIssueMatches.OrderByDescending(x => x.MatchScore).ToList();
            
            _context.Debug($"[ComicVine] No matching issue found in any volume, falling back to direct issue search");
            return await SearchIssuesAsync(series, issueNumber, year, cancellationToken);
        }
        
        /// <summary>
        /// Search for volumes (series) matching the query.
        /// </summary>
        public async Task<List<VolumeSearchResult>> SearchVolumesAsync(
            string query,
            int? year = null,
            CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
                throw new InvalidOperationException("API key not configured");
            
            await EnforceRateLimitAsync(cancellationToken, EndpointSearch);
            
            var url = $"{BaseUrl}search?api_key={_apiKey}&format=json&resources=volume";
            url += $"&query={HttpUtility.UrlEncode(query)}";
            url += "&field_list=id,name,start_year,publisher,count_of_issues,image,description";
            url += "&limit=100";
            
            var response = await GetAsync(url, cancellationToken);
            var apiResponse = JsonSerializer.Deserialize<ComicVineResponse<List<ComicVineVolume>>>(response);
            
            if (apiResponse?.StatusCode != 1)
                return new List<VolumeSearchResult>();
            
            var results = (apiResponse.Results ?? new List<ComicVineVolume>())
                .Select(v => new VolumeSearchResult
                {
                    Id = v.Id.ToString(),
                    Name = v.Name ?? "Unknown",
                    StartYear = v.StartYear,
                    Publisher = v.Publisher?.Name,
                    IssueCount = v.CountOfIssues,
                    CoverUrl = v.Image?.MediumUrl,
                    Description = StripHtml(v.Description)
                })
                .ToList();
            
            if (year.HasValue)
                results = results.Where(v => v.StartYear == year.Value).ToList();
            
            return results;
        }
        
        /// <summary>
        /// Get a specific issue from a volume by issue number.
        /// </summary>
        private async Task<MetadataSearchResult?> GetIssueByVolumeAndNumberAsync(
            string volumeId,
            int issueNumber,
            CancellationToken cancellationToken = default)
        {
            _context.Info($"[ComicVine] GetIssueByVolumeAndNumberAsync: volume={volumeId}, issue=#{issueNumber}");
            
            await EnforceRateLimitAsync(cancellationToken, EndpointIssues);
            
            var url = $"{BaseUrl}issues/?api_key={_apiKey}&format=json";
            url += $"&filter=volume:{volumeId},issue_number:{issueNumber}";
            url += "&field_list=id,name,issue_number,volume,cover_date,image,description";
            url += "&limit=5";
            
            var response = await GetAsync(url, cancellationToken);
            var apiResponse = JsonSerializer.Deserialize<ComicVineResponse<List<ComicVineIssue>>>(response);
            
            if (apiResponse?.StatusCode != 1 || apiResponse.Results == null || apiResponse.Results.Count == 0)
            {
                _context.Info($"[ComicVine] Issue #{issueNumber} not found in volume {volumeId} (status={apiResponse?.StatusCode}, count={apiResponse?.Results?.Count ?? 0})");
                return null;
            }
            
            var r = apiResponse.Results[0];
            var result = new MetadataSearchResult
            {
                Id = r.Id.ToString(),
                Series = r.Volume?.Name ?? "Unknown",
                IssueNumber = r.IssueNumber,
                Title = r.Name,
                Year = ParseYear(r.CoverDate),
                Month = ParseMonth(r.CoverDate),
                Publisher = r.Volume?.Publisher?.Name,
                CoverUrl = r.Image?.MediumUrl ?? r.Image?.SmallUrl,
                Description = StripHtml(r.Description),
                VolumeId = r.Volume?.Id.ToString(),
                VolumeStartYear = r.Volume?.StartYear,
                MatchScore = 1.0m
            };
            
            // Verify cover exists (basic validation)
            if (!string.IsNullOrEmpty(result.CoverUrl))
            {
                if (await VerifyCoverAccessibleAsync(result.CoverUrl, cancellationToken))
                {
                    _context.Debug($"[ComicVine] Cover verified for issue #{issueNumber}");
                    return result;
                }
                else
                {
                    _context.Warning($"[ComicVine] Cover not accessible for issue #{issueNumber}, skipping");
                    return null;
                }
            }
            
            // If no cover URL, still return but log it
            _context.Debug($"[ComicVine] Issue #{issueNumber} has no cover image");
            return result;
        }
        
        /// <summary>
        /// Verify that a cover image URL is accessible and valid.
        /// </summary>
        private Task<bool> VerifyCoverAccessibleAsync(string coverUrl, CancellationToken cancellationToken)
        {
            try
            {
                // Just verify it's a valid URL format - ComicVine covers are reliable
                // Don't do HEAD requests as some servers don't support them or have access restrictions
                if (string.IsNullOrEmpty(coverUrl))
                    return Task.FromResult(false);
                
                if (!Uri.TryCreate(coverUrl, UriKind.Absolute, out var uri))
                    return Task.FromResult(false);
                
                // Trust that ComicVine URLs are valid if they pass basic validation
                var isValid = uri.Scheme == "http" || uri.Scheme == "https";
                return Task.FromResult(isValid);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }
        
        #endregion
        
        #region Metadata Retrieval
        
        /// <summary>
        /// Get full issue metadata including credits.
        /// </summary>
        public async Task<ScrapedIssueMetadata?> GetIssueMetadataAsync(
            string issueId,
            CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
                throw new InvalidOperationException("API key not configured");
            
            await EnforceRateLimitAsync(cancellationToken, EndpointIssue);
            
            var url = $"{BaseUrl}issue/4000-{issueId}/?api_key={_apiKey}&format=json";
            url += "&field_list=id,name,issue_number,volume,cover_date,description,";
            url += "person_credits,character_credits,team_credits,location_credits,";
            url += "story_arc_credits,image,site_detail_url";
            
            var response = await GetAsync(url, cancellationToken);
            var apiResponse = JsonSerializer.Deserialize<ComicVineResponse<ComicVineIssueDetail>>(response);
            
            if (apiResponse?.StatusCode != 1 || apiResponse.Results == null)
                return null;
            
            var result = apiResponse.Results;
            var publisherName = result.Volume?.Publisher?.Name;
            string? imprintName = null;
            
            // Check if publisher is an imprint
            if (!string.IsNullOrEmpty(publisherName) && ImprintMapping.TryGetParent(publisherName, out var parentPublisher))
            {
                imprintName = publisherName;
                publisherName = parentPublisher;
            }
            
            var metadata = new ScrapedIssueMetadata
            {
                SourceId = issueId,
                VolumeId = result.Volume?.Id.ToString(),
                Series = result.Volume?.Name,
                Title = result.Name,
                IssueNumber = result.IssueNumber,
                Volume = result.Volume?.StartYear,
                Count = result.Volume?.CountOfIssues,
                Publisher = publisherName,
                Imprint = imprintName,
                Summary = StripHtml(result.Description),
                CoverUrl = result.Image?.SuperUrl ?? result.Image?.MediumUrl,
                CoverUrlSmall = result.Image?.SmallUrl,
                Web = result.SiteDetailUrl
            };
            
            // Parse cover date
            if (!string.IsNullOrEmpty(result.CoverDate) && DateTime.TryParse(result.CoverDate, out var date))
            {
                metadata.Year = date.Year;
                metadata.Month = date.Month;
                metadata.Day = date.Day;
            }
            
            // Parse credits
            if (result.PersonCredits != null)
            {
                foreach (var credit in result.PersonCredits)
                {
                    var name = credit.Name ?? "";
                    if (string.IsNullOrEmpty(name)) continue;
                    
                    var roles = (credit.Role ?? "").ToLowerInvariant().Split(',')
                        .Select(r => r.Trim())
                        .Where(r => !string.IsNullOrEmpty(r));
                    
                    foreach (var role in roles)
                    {
                        if (role.Contains("writer") && !metadata.Writers.Contains(name))
                            metadata.Writers.Add(name);
                        if (role.Contains("pencil") && !metadata.Pencillers.Contains(name))
                            metadata.Pencillers.Add(name);
                        if (role == "artist")
                        {
                            if (!metadata.Pencillers.Contains(name)) metadata.Pencillers.Add(name);
                            if (!metadata.Inkers.Contains(name)) metadata.Inkers.Add(name);
                        }
                        if (role.Contains("ink") && !metadata.Inkers.Contains(name))
                            metadata.Inkers.Add(name);
                        if (role.Contains("color") && !metadata.Colorists.Contains(name))
                            metadata.Colorists.Add(name);
                        if (role.Contains("letter") && !metadata.Letterers.Contains(name))
                            metadata.Letterers.Add(name);
                        if (role.Contains("cover") && !metadata.CoverArtists.Contains(name))
                            metadata.CoverArtists.Add(name);
                        if (role.Contains("edit") && !metadata.Editors.Contains(name))
                            metadata.Editors.Add(name);
                    }
                }
            }
            
            metadata.Characters = result.CharacterCredits?.Select(c => c.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>();
            metadata.Teams = result.TeamCredits?.Select(t => t.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>();
            metadata.Locations = result.LocationCredits?.Select(l => l.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>();
            metadata.StoryArcs = result.StoryArcCredits?.Select(s => s.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>();
            
            return metadata;
        }
        
        /// <summary>
        /// Get issues for a volume.
        /// </summary>
        public async Task<List<MetadataSearchResult>> GetVolumeIssuesAsync(
            string volumeId,
            CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
                throw new InvalidOperationException("API key not configured");
            
            await EnforceRateLimitAsync(cancellationToken, EndpointIssues);
            
            var url = $"{BaseUrl}issues/?api_key={_apiKey}&format=json";
            url += $"&filter=volume:{volumeId}";
            url += "&field_list=id,name,issue_number,cover_date,image";
            url += "&sort=issue_number:asc";
            url += "&limit=100";
            
            var response = await GetAsync(url, cancellationToken);
            var apiResponse = JsonSerializer.Deserialize<ComicVineResponse<List<ComicVineIssue>>>(response);
            
            if (apiResponse?.StatusCode != 1)
                return new List<MetadataSearchResult>();
            
            return (apiResponse.Results ?? new List<ComicVineIssue>())
                .Select(r => new MetadataSearchResult
                {
                    Id = r.Id.ToString(),
                    Series = r.Volume?.Name ?? "Unknown",
                    IssueNumber = r.IssueNumber,
                    Title = r.Name,
                    Year = ParseYear(r.CoverDate),
                    CoverUrl = r.Image?.SmallUrl,
                    VolumeId = volumeId,
                    Count = r.Volume?.CountOfIssues,
                    MatchScore = 1.0m
                })
                .ToList();
        }
        
        #endregion
        
        #region Rate Limiting
        
        public Dictionary<string, RateLimitStatus> GetRateLimitStatus()
        {
            var status = new Dictionary<string, RateLimitStatus>();
            var cutoff = DateTime.UtcNow - RateLimitWindow;
            
            foreach (var kvp in _endpointLimits)
            {
                lock (kvp.Value)
                {
                    while (kvp.Value.RequestTimes.Count > 0 && kvp.Value.RequestTimes.Peek() < cutoff)
                        kvp.Value.RequestTimes.Dequeue();
                    
                    var used = kvp.Value.RequestTimes.Count;
                    status[kvp.Key] = new RateLimitStatus
                    {
                        Endpoint = kvp.Key,
                        Used = used,
                        Remaining = MaxRequestsPerHourPerEndpoint - used,
                        Limit = MaxRequestsPerHourPerEndpoint,
                        ResetTime = kvp.Value.RequestTimes.Count > 0 
                            ? kvp.Value.RequestTimes.Peek() + RateLimitWindow 
                            : null
                    };
                }
            }
            
            return status;
        }
        
        private async Task EnforceRateLimitAsync(CancellationToken cancellationToken, string endpoint)
        {
            var limit = _endpointLimits.GetOrAdd(endpoint, _ => new EndpointRateLimit());
            
            lock (limit)
            {
                var cutoff = DateTime.UtcNow - RateLimitWindow;
                while (limit.RequestTimes.Count > 0 && limit.RequestTimes.Peek() < cutoff)
                    limit.RequestTimes.Dequeue();
                
                if (limit.RequestTimes.Count >= MaxRequestsPerHourPerEndpoint)
                {
                    var waitUntil = limit.RequestTimes.Peek() + RateLimitWindow;
                    var waitTime = waitUntil - DateTime.UtcNow;
                    
                    if (waitTime > TimeSpan.Zero)
                    {
                        throw new InvalidOperationException(
                            $"Rate limit exceeded for '{endpoint}'. Try again in {waitTime.TotalMinutes:F1} minutes.");
                    }
                }
            }
            
            var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
            if (timeSinceLastRequest.TotalMilliseconds < RateLimitDelayMs)
            {
                var delay = RateLimitDelayMs - (int)timeSinceLastRequest.TotalMilliseconds;
                await Task.Delay(delay, cancellationToken);
            }
            
            lock (limit)
            {
                limit.RequestTimes.Enqueue(DateTime.UtcNow);
            }
            _lastRequestTime = DateTime.UtcNow;
        }
        
        #endregion
        
        #region Scoring Methods
        
        private decimal CalculateMatchScore(string searchSeries, int? searchIssue, int? searchYear, ComicVineIssue result)
        {
            decimal score = 0;
            bool issueMatched = false;
            
            var seriesName = result.Volume?.Name ?? "";
            var seriesSimilarity = StringSimilarity(searchSeries, seriesName);
            score += seriesSimilarity * 0.35m;
            
            if (searchIssue.HasValue && !string.IsNullOrEmpty(result.IssueNumber))
            {
                if (int.TryParse(result.IssueNumber, out var issueNum) && issueNum == searchIssue.Value)
                {
                    score += 0.30m;
                    issueMatched = true;
                }
                else if (result.IssueNumber == searchIssue.Value.ToString())
                {
                    score += 0.30m;
                    issueMatched = true;
                }
                else
                {
                    score -= 0.05m;
                }
            }
            else if (!searchIssue.HasValue)
            {
                score += 0.15m;
            }
            
            if (searchYear.HasValue)
            {
                var issueCoverYear = ParseYear(result.CoverDate);
                if (issueCoverYear.HasValue)
                {
                    var yearDiff = Math.Abs(searchYear.Value - issueCoverYear.Value);
                    if (yearDiff == 0) score += 0.2m;
                    else if (yearDiff == 1) score += 0.15m;
                    else if (yearDiff == 2) score += 0.10m;
                }
            }
            else
            {
                score += 0.1m;
            }
            
            if (result.Image != null) score += 0.075m;
            if (!string.IsNullOrEmpty(result.Description)) score += 0.075m;
            if (issueMatched && seriesSimilarity >= 0.7m) score += 0.05m;
            
            // Check for format keywords in search query (e.g. TPB, Hardcover, Director's Cut)
            var formatKeywords = new[] { "Director's Cut", "TPB", "Trade Paperback", "Hardcover", "Annual" };
            foreach (var keyword in formatKeywords)
            {
                if (searchSeries.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    bool resultHasKeyword = (result.Name?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true) ||
                                            (result.Volume?.Name?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true);
                                            
                    if (resultHasKeyword)
                        score += 0.15m;
                    else
                        score -= 0.05m;
                }
            }

            return Math.Max(0, Math.Min(score, 1.0m));
        }
        
        private decimal CalculateVolumeMatchScore(string searchSeries, int? searchYear, VolumeSearchResult volume, int? issueNumber = null)
        {
            var score = StringSimilarity(searchSeries, volume.Name);
            
            // Format keyword matching for volumes
            var formatKeywords = new[] { "Director's Cut", "TPB", "Trade Paperback", "Hardcover" };
            foreach (var keyword in formatKeywords)
            {
                if (searchSeries.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    if (volume.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        score += 0.15m;
                    else
                        score -= 0.05m;
                }
            }
            
            if (searchYear.HasValue && volume.StartYear.HasValue)
            {
                var yearDiff = Math.Abs(searchYear.Value - volume.StartYear.Value);
                var volumeStartedBefore = volume.StartYear.Value <= searchYear.Value;
                
                if (yearDiff == 0) score = Math.Min(1.0m, score + 0.10m);
                else if (yearDiff <= 2 && volumeStartedBefore) score = Math.Min(1.0m, score + 0.08m);
                else if (yearDiff <= 5 && volumeStartedBefore) score = Math.Min(1.0m, score + 0.05m);
                else if (yearDiff <= 10 && volumeStartedBefore) score = Math.Min(1.0m, score + 0.03m);
                else if (yearDiff <= 10) score *= 0.95m;
                else if (yearDiff <= 20) score *= 0.85m;
                else if (yearDiff <= 40) score *= 0.70m;
                else score *= 0.50m;
            }
            
            // Don't use issue count for scoring - ComicVine's counts are unreliable/incomplete
            // (e.g., Detective Comics 2016 shows 170 issues but actually has 1072+)
            // Just rely on series name and year matching
            
            return score;
        }
        
        private static decimal StringSimilarity(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return 0;
            
            var normalizedA = NormalizeForComparison(a);
            var normalizedB = NormalizeForComparison(b);
            
            if (normalizedA == normalizedB) return 1.0m;
            
            if (normalizedA.Contains(normalizedB) || normalizedB.Contains(normalizedA))
            {
                var lenRatio = (decimal)Math.Min(normalizedA.Length, normalizedB.Length) / 
                               Math.Max(normalizedA.Length, normalizedB.Length);
                return 0.70m + (lenRatio * 0.15m);
            }
            
            var wordsA = normalizedA.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var wordsB = normalizedB.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var setA = wordsA.ToHashSet();
            var setB = wordsB.ToHashSet();
            
            var intersection = setA.Intersect(setB).Count();
            var union = setA.Union(setB).Count();
            
            if (union == 0) return 0;
            
            var jaccard = (decimal)intersection / union;
            var lcsLength = LongestCommonSubsequenceLength(wordsA, wordsB);
            var orderSimilarity = (decimal)lcsLength / Math.Max(wordsA.Length, wordsB.Length);
            
            return Math.Min(1.0m, (jaccard * 0.6m) + (orderSimilarity * 0.4m));
        }
        
        private static int LongestCommonSubsequenceLength(string[] a, string[] b)
        {
            var m = a.Length;
            var n = b.Length;
            var dp = new int[m + 1, n + 1];
            
            for (int i = 1; i <= m; i++)
                for (int j = 1; j <= n; j++)
                    dp[i, j] = a[i - 1] == b[j - 1] 
                        ? dp[i - 1, j - 1] + 1 
                        : Math.Max(dp[i - 1, j], dp[i, j - 1]);
            
            return dp[m, n];
        }
        
        private static string NormalizeForComparison(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            
            var sb = new System.Text.StringBuilder(input.Length);
            foreach (var c in input.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (char.IsWhiteSpace(c) || c == '-' || c == ':' || c == '\'' || c == '/') sb.Append(' ');
            }
            
            return Regex.Replace(sb.ToString().Trim(), @"\s+", " ");
        }
        
        #endregion
        
        #region Helpers
        
        private async Task<string> GetAsync(string url, CancellationToken cancellationToken)
        {
            var response = await _context.GetAsync(url);
            return response;
        }
        
        private static int? ParseYear(string? coverDate)
        {
            if (string.IsNullOrEmpty(coverDate)) return null;
            return DateTime.TryParse(coverDate, out var date) ? date.Year : null;
        }
        
        private static int? ParseMonth(string? coverDate)
        {
            if (string.IsNullOrEmpty(coverDate)) return null;
            return DateTime.TryParse(coverDate, out var date) ? date.Month : null;
        }
        
        private static string? StripHtml(string? html)
        {
            if (string.IsNullOrEmpty(html)) return null;
            var text = Regex.Replace(html, "<[^>]+>", " ");
            text = HttpUtility.HtmlDecode(text);
            text = Regex.Replace(text, @"\s+", " ").Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        
        #endregion
    }
    
    internal class EndpointRateLimit
    {
        public Queue<DateTime> RequestTimes { get; } = new();
    }
}
