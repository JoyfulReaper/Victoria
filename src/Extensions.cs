using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Victoria.Enums;
using Victoria.Player;
using Victoria.Resolvers;

namespace Victoria {
    /// <summary>
    /// Additional extension methods to make workflow easier.
    /// </summary>
    public static class Extensions {
        private static readonly Regex TitleRegex
            = new(@"(ft).\s+\w+|\(.*?\)|(lyrics)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Lazy<HttpClient> LazyHttpClient = new();
        internal static readonly HttpClient HttpClient = LazyHttpClient.Value;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="requestMessage"></param>
        /// <param name="jsonConverter"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="NullReferenceException"></exception>
        /// <exception cref="HttpRequestException"></exception>
        public static async Task<T> ReadAsJsonAsync<T>(HttpRequestMessage requestMessage,
                                                       JsonConverter jsonConverter = default) {
            if (requestMessage == null) {
                throw new ArgumentNullException(nameof(requestMessage));
            }

            if (requestMessage.RequestUri == null) {
                throw new NullReferenceException(nameof(requestMessage.RequestUri));
            }

            using var responseMessage = await HttpClient.SendAsync(requestMessage);
            if (!responseMessage.IsSuccessStatusCode) {
                throw new HttpRequestException(responseMessage.ReasonPhrase);
            }

            using var content = responseMessage.Content;
            await using var stream = await content.ReadAsStreamAsync();

            var deserialized = await JsonSerializer.DeserializeAsync<T>(stream,
                jsonConverter == default
                    ? default
                    : new JsonSerializerOptions {
                        Converters = {jsonConverter}
                    });
            return deserialized;
        }

        /// <summary>
        ///     Shortcut method to add Victoria to <see cref="IServiceCollection" />.
        /// </summary>
        /// <param name="serviceCollection">
        ///     <see cref="IServiceProvider" />
        /// </param>
        /// <param name="action">LavaConfig action.</param>
        /// <returns>
        ///     <see cref="IServiceCollection" />
        /// </returns>
        public static IServiceCollection AddLavaNode(this IServiceCollection serviceCollection,
                                                     Action<NodeConfiguration> action = default) {
            var lavaConfig = new NodeConfiguration();
            action?.Invoke(lavaConfig);
            serviceCollection.AddSingleton(lavaConfig);
            serviceCollection.AddSingleton<LavaNode>();
            return serviceCollection;
        }

        /// <summary>
        ///     Shortcut method to use <see cref="LavaNode" /> from <see cref="IServiceProvider" />.
        /// </summary>
        /// <param name="serviceProvider">
        ///     <see cref="IServiceProvider" />
        /// </param>
        /// <exception cref="NullReferenceException">
        ///     Throws if <see cref="LavaNode" /> is null in <see cref="IServiceProvider" />
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Throws if <seealso cref="LavaNode{TPlayer}.IsConnected"/> is set to true.
        /// </exception>
        public static Task UseLavaNodeAsync(this IServiceProvider serviceProvider) {
            if (serviceProvider.GetService(typeof(LavaNode)) is not LavaNode lavaNode) {
                throw new NullReferenceException(nameof(LavaNode));
            }

            if (lavaNode.IsConnected) {
                throw new InvalidOperationException("A connection is already established with Lavalink.");
            }

            return lavaNode.ConnectAsync();
        }

        /// <summary>
        ///     Whether the next track should be played or not.
        /// </summary>
        /// <param name="trackEndReason">Track end reason given by Lavalink.</param>
        public static bool ShouldPlayNext(this TrackEndReason trackEndReason) {
            return trackEndReason is TrackEndReason.Finished or TrackEndReason.LoadFailed;
        }

        /// <summary>
        ///     Fetches artwork for Youtube, Twitch, SoundCloud and Vimeo.
        /// </summary>
        /// <param name="track"></param>
        /// <returns><see cref="string"/></returns>
        public static ValueTask<string> FetchArtworkAsync(this LavaTrack track) {
            return ArtworkResolver.FetchAsync(track);
        }

        /// <summary>
        ///     Fetches lyrics from Genius.
        /// </summary>
        /// <param name="track"></param>
        /// <returns><see cref="string"/></returns>
        public static ValueTask<string> FetchLyricsFromGeniusAsync(this LavaTrack track) {
            return LyricsResolver.SearchGeniusAsync(track);
        }

        /// <summary>
        ///     Fetches lyrics from OVH API.
        /// </summary>
        /// <param name="track"></param>
        /// <returns><see cref="string"/></returns>
        public static ValueTask<string> FetchLyricsFromOvhAsync(this LavaTrack track) {
            return LyricsResolver.SearchOVHAsync(track);
        }

        internal static string Encode(this string str) {
            return WebUtility.UrlEncode(str);
        }

        internal static (string Author, string Title) GetAuthorAndTitle(this LavaTrack lavaTrack) {
            var split = lavaTrack.Title.Split('-');

            if (split.Length is 1) {
                return (lavaTrack.Author, lavaTrack.Title);
            }

            var author = split[0];
            var title = split[1];

            while (TitleRegex.IsMatch(title)) {
                title = TitleRegex.Replace(title, string.Empty);
            }

            title = title.TrimStart().TrimEnd();
            return author switch {
                ""                                             => (lavaTrack.Author, title),
                _ when string.Equals(author, lavaTrack.Author) => (lavaTrack.Author, title),
                _                                              => (author, title)
            };
        }

        internal static string ParseGeniusHtml(Span<byte> bytes) {
            var start = Encoding.UTF8.GetBytes("<!--sse-->");
            var end = Encoding.UTF8.GetBytes("<!--/sse-->");

            bytes = bytes[bytes.LastIndexOf(start)..];
            bytes = bytes[..bytes.LastIndexOf(end)];

            var rawHtml = Encoding.UTF8.GetString(bytes);
            if (rawHtml.Contains("Genius.ads")) {
                return string.Empty;
            }

            var htmlRegex = new Regex("<[^>]*?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            return htmlRegex.Replace(rawHtml, string.Empty).TrimStart().TrimEnd();
        }
    }
}