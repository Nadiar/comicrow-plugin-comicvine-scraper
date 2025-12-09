# Comic Vine Scraper

A ComicRow plugin that scrapes comic metadata from [Comic Vine](https://comicvine.gamespot.com/).

## Features

- **Batch Processing**: Select multiple comics and scrape metadata for all at once
- **Smart Matching**: Automatically matches comics based on series name, issue number, and year
- **Confidence Thresholds**: Only auto-applies metadata when match confidence exceeds threshold
- **Merge Mode**: Optionally preserve existing metadata while filling gaps
- **Library Scan Integration**: Optionally scrape metadata during library imports

## Installation

### From Release
1. Download the latest `.crowplugin` file from [Releases](https://github.com/Nadiar/comicrow-plugin-comicvine/releases)
2. In ComicRow, go to **Settings → Plugins → Install**
3. Select the downloaded `.crowplugin` file

### From GitHub URL
1. In ComicRow, go to **Settings → Plugins → Install from GitHub**
2. Enter: `https://github.com/Nadiar/comicrow-plugin-comicvine`
3. Select the version and install

## Setup

1. Get a free API key from [Comic Vine API](https://comicvine.gamespot.com/api/)
2. In ComicRow, go to **Settings → Metadata Providers**
3. Enter your Comic Vine API key

## Usage

1. Select one or more comics in your library
2. Right-click and choose **Scrape from Comic Vine**
3. Review and confirm matches (for low-confidence results)

## Settings

| Setting | Default | Description |
|---------|---------|-------------|
| Auto-apply Threshold | 0.85 | Minimum confidence score (0-1) for auto-applying |
| Merge Mode | true | Only fill empty fields when true |
| Skip Existing | false | Skip comics with existing Series/Issue |
| Only Missing IDs | false | Only process comics without Comic Vine ID |
| Parse Filename | true | Parse series/issue from filename if metadata empty |
| Auto-scan on Import | false | Scrape during library imports (slow!) |

## Permissions

This plugin requires:
- **comic:read** - Read comic metadata for searching
- **http: comicvine.gamespot.com** - Access Comic Vine API
- **comic:metadata** - Store scraped metadata

Optional:
- **comic:write** - Update embedded ComicInfo.xml

## Development

```bash
# Build
dotnet build

# Create .crowplugin package
dotnet build -c Release /p:PackPluginOnBuild=true
```

## License

MIT - See [LICENSE](LICENSE)
