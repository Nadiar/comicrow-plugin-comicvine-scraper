using System.Threading.Tasks;
using Xunit;

namespace ComicVine.Tests2
{
    public class ApiTests
    {
        [Fact]
        public async Task HandleSearch_Returns_401_When_ApiKey_Not_Configured()
        {
            var ctx = new TestFakePluginContext();
            var plugin = new ComicVineScraperPlugin();
            plugin.SetContext(ctx);

            var req = new PluginApiRequest { Route = "search", Method = "POST", Body = "{}" };
            var res = await plugin.HandleRequestAsync(req);

            Assert.Equal(401, res.StatusCode);
        }

        [Fact]
        public async Task HandleSearch_Returns_BadRequest_When_Query_Missing()
        {
            var ctx = new TestFakePluginContext();
            ctx.Values["api_key"] = "FAKE_KEY"; // satisfy api key
            var plugin = new ComicVineScraperPlugin();
            plugin.SetContext(ctx);

            var req = new PluginApiRequest { Route = "search", Method = "POST", Body = "{}" };
            var res = await plugin.HandleRequestAsync(req);

            Assert.Equal(400, res.StatusCode);
        }
    }
}
