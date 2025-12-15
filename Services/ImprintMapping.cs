using System;
using System.Collections.Generic;

namespace ComicRow.Plugins.ComicVineScraper.Services
{
    /// <summary>
    /// Maps comic imprints to their parent publishers.
    /// Based on ComicTagger's imprint mapping for accurate publisher categorization.
    /// </summary>
    public static class ImprintMapping
    {
        private static readonly Dictionary<string, string> ImprintToPublisher = new(StringComparer.OrdinalIgnoreCase)
        {
            // DC Comics imprints
            { "Vertigo", "DC Comics" },
            { "Wildstorm", "DC Comics" },
            { "Homage Comics", "DC Comics" },
            { "Paradox Press", "DC Comics" },
            { "Zuda Comics", "DC Comics" },
            { "CMX", "DC Comics" },
            { "Black Label", "DC Comics" },
            { "DC Black Label", "DC Comics" },
            { "DC Vertigo", "DC Comics" },
            { "DC Ink", "DC Comics" },
            { "DC Zoom", "DC Comics" },
            { "Earth M", "DC Comics" },
            { "Earth One", "DC Comics" },
            { "Johnny DC", "DC Comics" },
            { "Minx", "DC Comics" },
            { "Piranha Press", "DC Comics" },
            { "Tangent Comics", "DC Comics" },
            { "All Star DC Comics", "DC Comics" },
            { "Helix", "DC Comics" },
            { "America's Best Comics", "DC Comics" },
            { "Milestone Media", "DC Comics" },
            
            // Marvel imprints
            { "Max", "Marvel" },
            { "Marvel Max", "Marvel" },
            { "Max Comics", "Marvel" },
            { "Marvel Knights", "Marvel" },
            { "Icon Comics", "Marvel" },
            { "Icon", "Marvel" },
            { "Ultimate Comics", "Marvel" },
            { "Soleil", "Marvel" },
            { "Marvel Music", "Marvel" },
            { "Marvel Age", "Marvel" },
            { "Marvel Soleil", "Marvel" },
            { "Epic Comics", "Marvel" },
            { "Epic", "Marvel" },
            { "Star Comics", "Marvel" },
            { "Curtis Magazines", "Marvel" },
            { "Atlas Comics", "Marvel" },
            { "Timely Comics", "Marvel" },
            { "MC2", "Marvel" },
            { "Marvel Adventures", "Marvel" },
            { "Marvel Illustrated", "Marvel" },
            { "Marvel Next", "Marvel" },
            { "Marvel Noir", "Marvel" },
            { "Marvel UK", "Marvel" },
            { "Razorline", "Marvel" },
            { "Paramount Comics", "Marvel" },
            { "Malibu Comics", "Marvel" },
            { "Malibu", "Marvel" },
            { "Ultraverse", "Marvel" },
            
            // Image imprints
            { "Cliffhanger", "Image" },
            { "Homage", "Image" },
            { "Skybound", "Image" },
            { "Skybound Entertainment", "Image" },
            { "Top Cow", "Image" },
            { "Top Cow Productions", "Image" },
            { "Todd McFarlane Productions", "Image" },
            { "Extreme Studios", "Image" },
            { "Highbrow Entertainment", "Image" },
            { "Shadowline", "Image" },
            { "WildStorm Productions", "Image" },
            
            // Dark Horse imprints
            { "Dark Horse Manga", "Dark Horse Comics" },
            { "Legend", "Dark Horse Comics" },
            { "Maverick", "Dark Horse Comics" },
            { "M Press", "Dark Horse Comics" },
            { "Rocket Comics", "Dark Horse Comics" },
            { "DH Press", "Dark Horse Comics" },
            { "Dark Horse Books", "Dark Horse Comics" },
            { "Dark Horse Deluxe", "Dark Horse Comics" },
            
            // IDW imprints
            { "Idea and Design Works", "IDW Publishing" },
            { "IDW", "IDW Publishing" },
            { "Top Shelf Productions", "IDW Publishing" },
            { "Top Shelf", "IDW Publishing" },
            { "Yoe Books", "IDW Publishing" },
            { "SLG Publishing", "IDW Publishing" },
            
            // Dynamite imprints
            { "Dynamite", "Dynamite Entertainment" },
            { "Harris Comics", "Dynamite Entertainment" },
            { "Chaos! Comics", "Dynamite Entertainment" },
            
            // Boom! Studios imprints
            { "Boom!", "Boom! Studios" },
            { "Boom! Box", "Boom! Studios" },
            { "Archaia", "Boom! Studios" },
            { "KaBOOM!", "Boom! Studios" },
            
            // Valiant imprints
            { "Acclaim Comics", "Valiant Entertainment" },
            { "Valiant", "Valiant Entertainment" },
            { "Valiant Comics", "Valiant Entertainment" },
            
            // Other notable imprints
            { "Humanoids", "Humanoids Publishing" },
            { "2000 AD", "Rebellion" },
            { "Fleetway", "Rebellion" },
            { "Quality Comics", "Rebellion" },
            { "First Comics", "Comics Legends" },
            { "CrossGen", "Disney" },
            { "Papercutz", "NBM Publishing" },
            { "Devil's Due Publishing", "IDW Publishing" },
            { "Zenescope", "Zenescope Entertainment" },
        };
        
        /// <summary>
        /// Try to get the parent publisher for an imprint.
        /// </summary>
        public static bool TryGetParent(string imprint, out string? parentPublisher)
        {
            return ImprintToPublisher.TryGetValue(imprint, out parentPublisher);
        }
        
        /// <summary>
        /// Check if a publisher name is an imprint.
        /// </summary>
        public static bool IsImprint(string publisher)
        {
            return ImprintToPublisher.ContainsKey(publisher);
        }
        
        /// <summary>
        /// Get parent publisher, or return original if not an imprint.
        /// </summary>
        public static string GetPublisher(string publisherOrImprint)
        {
            return ImprintToPublisher.TryGetValue(publisherOrImprint, out var parent) 
                ? parent 
                : publisherOrImprint;
        }
    }
}
