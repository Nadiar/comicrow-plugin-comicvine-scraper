using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ComicRow.Plugins.ComicVineScraper.Models
{
    /// <summary>
    /// Comic Vine API Response Structure - All responses follow this envelope format.
    /// </summary>
    internal class ComicVineResponse<T>
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }
        
        [JsonPropertyName("limit")]
        public int Limit { get; set; }
        
        [JsonPropertyName("offset")]
        public int Offset { get; set; }
        
        [JsonPropertyName("number_of_page_results")]
        public int NumberOfPageResults { get; set; }
        
        [JsonPropertyName("number_of_total_results")]
        public int NumberOfTotalResults { get; set; }
        
        /// <summary>1 = success, 100 = invalid API key, 101 = object not found, 102 = bad request</summary>
        [JsonPropertyName("status_code")]
        public int StatusCode { get; set; }
        
        [JsonPropertyName("results")]
        public T? Results { get; set; }
    }
    
    /// <summary>
    /// Basic issue data returned from search/issues endpoints.
    /// </summary>
    internal class ComicVineIssue
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        
        [JsonPropertyName("issue_number")]
        public string? IssueNumber { get; set; }
        
        [JsonPropertyName("cover_date")]
        public string? CoverDate { get; set; }
        
        [JsonPropertyName("description")]
        public string? Description { get; set; }
        
        [JsonPropertyName("site_detail_url")]
        public string? SiteDetailUrl { get; set; }
        
        [JsonPropertyName("volume")]
        public ComicVineVolume? Volume { get; set; }
        
        [JsonPropertyName("image")]
        public ComicVineImage? Image { get; set; }
    }
    
    /// <summary>
    /// Full issue details from /issue/4000-{id}/ endpoint with credits.
    /// </summary>
    internal class ComicVineIssueDetail : ComicVineIssue
    {
        [JsonPropertyName("person_credits")]
        public List<ComicVineCredit>? PersonCredits { get; set; }
        
        [JsonPropertyName("character_credits")]
        public List<ComicVineCredit>? CharacterCredits { get; set; }
        
        [JsonPropertyName("team_credits")]
        public List<ComicVineCredit>? TeamCredits { get; set; }
        
        [JsonPropertyName("location_credits")]
        public List<ComicVineCredit>? LocationCredits { get; set; }
        
        [JsonPropertyName("story_arc_credits")]
        public List<ComicVineCredit>? StoryArcCredits { get; set; }
    }
    
    /// <summary>
    /// Volume (series) data - Comic Vine calls what we call "series" a "volume".
    /// </summary>
    internal class ComicVineVolume
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        
        [JsonPropertyName("start_year")]
        [JsonConverter(typeof(NullableIntStringConverter))]
        public int? StartYear { get; set; }
        
        [JsonPropertyName("count_of_issues")]
        public int? CountOfIssues { get; set; }
        
        [JsonPropertyName("description")]
        public string? Description { get; set; }
        
        [JsonPropertyName("publisher")]
        public ComicVinePublisher? Publisher { get; set; }
        
        [JsonPropertyName("image")]
        public ComicVineImage? Image { get; set; }
    }
    
    internal class ComicVinePublisher
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
    
    internal class ComicVineImage
    {
        [JsonPropertyName("icon_url")]
        public string? IconUrl { get; set; }
        
        [JsonPropertyName("medium_url")]
        public string? MediumUrl { get; set; }
        
        [JsonPropertyName("small_url")]
        public string? SmallUrl { get; set; }
        
        [JsonPropertyName("super_url")]
        public string? SuperUrl { get; set; }
        
        [JsonPropertyName("original_url")]
        public string? OriginalUrl { get; set; }
    }
    
    internal class ComicVineCredit
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        
        [JsonPropertyName("role")]
        public string? Role { get; set; }
    }
    
    /// <summary>
    /// JSON converter for int values that may come as strings from Comic Vine API
    /// </summary>
    internal class NullableIntStringConverter : JsonConverter<int?>
    {
        public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;
            
            if (reader.TokenType == JsonTokenType.Number)
                return reader.GetInt32();
            
            if (reader.TokenType == JsonTokenType.String)
            {
                var str = reader.GetString();
                if (string.IsNullOrWhiteSpace(str))
                    return null;
                if (int.TryParse(str, out var value))
                    return value;
                return null;
            }
            
            return null;
        }
        
        public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
                writer.WriteNumberValue(value.Value);
            else
                writer.WriteNullValue();
        }
    }
    
    /// <summary>
    /// Search result returned to clients
    /// </summary>
    public class MetadataSearchResult
    {
        public string Id { get; set; } = "";
        public string Provider { get; set; } = "ComicVine";
        public string? Series { get; set; }
        public string? IssueNumber { get; set; }
        public string? Title { get; set; }
        public int? Year { get; set; }
        public int? Month { get; set; }
        public string? Publisher { get; set; }
        public string? CoverUrl { get; set; }
        public string? Description { get; set; }
        public string? VolumeId { get; set; }
        public int? VolumeStartYear { get; set; }
        public int? Count { get; set; } // Issue count of the volume
        public decimal MatchScore { get; set; }
    }
    
    /// <summary>
    /// Volume search result
    /// </summary>
    public class VolumeSearchResult
    {
        public string Id { get; set; } = "";
        public string Provider { get; set; } = "ComicVine";
        public string Name { get; set; } = "";
        public int? StartYear { get; set; }
        public string? Publisher { get; set; }
        public int? IssueCount { get; set; }
        public string? CoverUrl { get; set; }
        public string? Description { get; set; }
        public decimal Score { get; set; }
    }
    
    /// <summary>
    /// Full scraped issue metadata
    /// </summary>
    public class ScrapedIssueMetadata
    {
        public string Provider { get; set; } = "ComicVine";
        public string? SourceId { get; set; }
        public string? VolumeId { get; set; }
        public string? Series { get; set; }
        public string? Title { get; set; }
        public string? IssueNumber { get; set; }
        public int? Volume { get; set; }
        public int? Count { get; set; } // New field for issue count
        public string? Publisher { get; set; }
        public string? Imprint { get; set; }
        public string? Summary { get; set; }
        public int? Year { get; set; }
        public int? Month { get; set; }
        public int? Day { get; set; }
        public string? CoverUrl { get; set; }
        public string? CoverUrlSmall { get; set; }
        public string? Web { get; set; }
        
        public List<string> Writers { get; set; } = new();
        public List<string> Pencillers { get; set; } = new();
        public List<string> Inkers { get; set; } = new();
        public List<string> Colorists { get; set; } = new();
        public List<string> Letterers { get; set; } = new();
        public List<string> CoverArtists { get; set; } = new();
        public List<string> Editors { get; set; } = new();
        public List<string> Characters { get; set; } = new();
        public List<string> Teams { get; set; } = new();
        public List<string> Locations { get; set; } = new();
        public List<string> StoryArcs { get; set; } = new();
    }
    
    /// <summary>
    /// Rate limit status for an endpoint
    /// </summary>
    public class RateLimitStatus
    {
        public string Endpoint { get; set; } = "";
        public int Used { get; set; }
        public int Remaining { get; set; }
        public int Limit { get; set; }
        public DateTime? ResetTime { get; set; }
    }
}
