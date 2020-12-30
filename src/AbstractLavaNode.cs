using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Victoria.Interfaces;
using Victoria.Responses.Search;
using Victoria.WebSocket;
using Victoria.Wrappers;

namespace Victoria {
    /// <summary>
    /// 
    /// </summary>
    public class AbstractLavaNode : AbstractLavaNode<ILavaPlayer>, ILavaNode {
        /// <inheritdoc />
        public AbstractLavaNode(NodeConfiguration nodeConfiguration)
            : base(nodeConfiguration) { }
    }

    /// <inheritdoc />
    public class AbstractLavaNode<TLavaPlayer> : AbstractLavaNode<TLavaPlayer, ILavaTrack>
        where TLavaPlayer : ILavaPlayer<ILavaTrack> {
        /// <inheritdoc />
        public AbstractLavaNode(NodeConfiguration nodeConfiguration)
            : base(nodeConfiguration) { }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="TLavaPlayer"></typeparam>
    /// <typeparam name="TLavaTrack"></typeparam>
    public class AbstractLavaNode<TLavaPlayer, TLavaTrack> : ILavaNode<TLavaPlayer, TLavaTrack>
        where TLavaPlayer : ILavaPlayer<TLavaTrack>
        where TLavaTrack : ILavaTrack {
        /// <inheritdoc />
        public bool IsConnected
            => Volatile.Read(ref _isConnected);

        /// <inheritdoc />
        public IReadOnlyCollection<TLavaPlayer> Players
            => _players.Values as IReadOnlyCollection<TLavaPlayer>;

        private bool _isConnected;
        private readonly NodeConfiguration _nodeConfiguration;
        private readonly DiscordClientWrapper _discordClientWrapper;
        private readonly WebSocketClient _webSocketClient;
        private readonly ConcurrentDictionary<ulong, TLavaPlayer> _players;

        /// <summary>
        /// 
        /// </summary>
        public AbstractLavaNode(NodeConfiguration nodeConfiguration) {
            _nodeConfiguration = nodeConfiguration;
            _discordClientWrapper = nodeConfiguration.DiscordClient;

            _webSocketClient = new WebSocketClient(nodeConfiguration.Hostname, nodeConfiguration.Port, "ws");
            _players = new ConcurrentDictionary<ulong, TLavaPlayer>();
        }

        /// <inheritdoc />
        public async ValueTask ConnectAsync() {
            if (Volatile.Read(ref _isConnected)) {
                throw new InvalidOperationException(
                    $"A connection is already established. Please call {nameof(DisconnectAsync)} before calling {nameof(ConnectAsync)}.");
            }

            await _webSocketClient.ConnectAsync();
        }

        /// <inheritdoc />
        public async ValueTask DisconnectAsync() {
            if (!Volatile.Read(ref _isConnected)) {
                throw new InvalidOperationException("Cannot disconnect when no connection is established.");
            }

            await _webSocketClient.DisconnectAsync();
        }

        /// <inheritdoc />
        public async ValueTask JoinAsync() {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc />
        public async ValueTask LeaveAsync() {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc />
        public async ValueTask MoveAsync() {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc />
        public async ValueTask<SearchResponse> SearchAsync(SearchType searchType, string query) {
            if (string.IsNullOrWhiteSpace(query)) {
                throw new ArgumentNullException(nameof(query));
            }

            var path = searchType switch {
                SearchType.YouTube    => $"/loadtracks?identifier={WebUtility.UrlEncode($"scsearch:{query}")}",
                SearchType.SoundCloud => $"/loadtracks?identifier={WebUtility.UrlEncode($"ytsearch:{query}")}",
                SearchType.Direct     => $"/loadtracks?identifier={query}"
            };

            using var requestMessage =
                new HttpRequestMessage(HttpMethod.Get, $"{_nodeConfiguration.HttpEndpoint}{path}") {
                    Headers = {
                        {"Authorization", _nodeConfiguration.Authorization}
                    }
                };

            var searchResponse = await Extensions.HttpClient.ReadAsJsonAsync<SearchResponse>(requestMessage);
            return searchResponse;
        }

        /// <inheritdoc />
        public bool HasPlayer(ulong guildId) {
            return _players.ContainsKey(guildId);
        }

        /// <inheritdoc />
        public bool TryGetPlayer(ulong guildId, out TLavaPlayer lavaPlayer) {
            return _players.TryGetValue(guildId, out lavaPlayer);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync() {
            throw new System.NotImplementedException();
        }
    }
}