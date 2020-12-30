using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Victoria.Interfaces;
using Victoria.Responses.Search;

namespace Victoria.Tests {
    [TestClass]
    public sealed class CastingTests {
        private readonly ILavaNode _lavaNode
            = new AbstractLavaNode(new NodeConfiguration());

        public sealed class CustomTrack : AbstractLavaTrack {
            public ulong Requester { get; set; }

            public CustomTrack(ILavaTrack lavaTrack) : base(lavaTrack) { }
        }

        [DataTestMethod]
        [DataRow("South Side Suicide")]
        [DataRow("Drake Passionfruit")]
        public async Task Is_Type_Of_ILavaTrack_Async(string query) {
            var searchResponse = await _lavaNode.SearchAsync(SearchType.YouTube, query);
            if (searchResponse.Status == SearchStatus.LoadFailed || searchResponse.Status == SearchStatus.NoMatches) {
                Assert.Fail("Failed search response.");
            }

            var lavaTrack = searchResponse.Tracks.FirstOrDefault();
            var customTrack = new CustomTrack(lavaTrack) {
                Requester = ulong.MaxValue
            };

            Assert.IsInstanceOfType(customTrack, typeof(ILavaTrack));
            Assert.IsInstanceOfType(customTrack, typeof(AbstractLavaTrack));
            Assert.IsNotNull(customTrack);
            Assert.IsNotNull(customTrack.Requester);
            Assert.IsTrue(customTrack.Requester is ulong.MaxValue);
        }
    }
}