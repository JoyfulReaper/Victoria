using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Victoria.Interfaces;
using Victoria.Payloads;
using Victoria.Responses.Search;
using Victoria.WebSocket;
using Victoria.WebSocket.EventArgs;
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

        /// <summary>
        /// 
        /// </summary>
        protected DiscordClient DiscordClient { get; set; }

        private bool _isConnected;
        private readonly NodeConfiguration _nodeConfiguration;
        private readonly WebSocketClient _webSocketClient;
        private readonly ConcurrentDictionary<ulong, TLavaPlayer> _players;
        private readonly ConcurrentDictionary<ulong, VoiceState> _voiceStates;

        /// <summary>
        /// 
        /// </summary>
        public AbstractLavaNode(NodeConfiguration nodeConfiguration) {
            _nodeConfiguration = nodeConfiguration;

            _webSocketClient = new WebSocketClient(nodeConfiguration.Hostname, nodeConfiguration.Port, "ws");
            _players = new ConcurrentDictionary<ulong, TLavaPlayer>();
            _voiceStates = new ConcurrentDictionary<ulong, VoiceState>();

            _webSocketClient.OnOpenAsync += OnOpenAsync;
            _webSocketClient.OnCloseAsync += OnCloseAsync;
            _webSocketClient.OnErrorAsync += OnErrorAsync;
            _webSocketClient.OnMessageAsync += OnMessageAsync;

            DiscordClient.OnVoiceServerUpdated = OnVoiceServerUpdated;
            DiscordClient.OnUserVoiceStateUpdated = OnUserVoiceStateUpdated;
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
        public async ValueTask<TLavaPlayer> JoinAsync(VoiceChannel voiceChannel) {
            if (_players.TryGetValue(voiceChannel.GuildId, out var player)) {
                return player;
            }

            player = (TLavaPlayer) Activator.CreateInstance(typeof(TLavaPlayer), _webSocketClient, voiceChannel);
            _players.TryAdd(voiceChannel.GuildId, player);

            return player;
        }

        /// <inheritdoc />
        public async ValueTask LeaveAsync(VoiceChannel voiceChannel) {
            if (!Volatile.Read(ref _isConnected)) {
                throw new InvalidOperationException("Cannot execute this action when no connection is established");
            }

            if (!_players.TryRemove(voiceChannel.GuildId, out var player)) {
                return;
            }

            await player.DisposeAsync();
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
            foreach (var (_, player) in _players) {
                await player.DisposeAsync();
            }

            await DisconnectAsync();
        }

        private ValueTask OnUserVoiceStateUpdated(VoiceState state) {
            if (DiscordClient.UserId != state.UserId) {
                return ValueTask.CompletedTask;
            }

            _voiceStates.TryUpdate(state.GuildId, state, default);
            return ValueTask.CompletedTask;
        }

        private ValueTask OnVoiceServerUpdated(VoiceServer server) {
            if (_voiceStates.TryGetValue(server.GuildId, out var state))
                return _webSocketClient.SendAsync(new ServerUpdatePayload {
                    Data = new VoiceServerData(server.Token, server.Endpoint),
                    SessionId = state.SessionId,
                    GuildId = $"{server.GuildId}"
                });

            _voiceStates.TryAdd(server.GuildId, default);
            return ValueTask.CompletedTask;
        }

        private ValueTask OnOpenAsync() {
            Volatile.Write(ref _isConnected, true);
            if (!_nodeConfiguration.EnableResume) {
                return ValueTask.CompletedTask;
            }

            return _webSocketClient.SendAsync(
                new ResumePayload(_nodeConfiguration.ResumeKey, _nodeConfiguration.ResumeTimeout));
        }

        private ValueTask OnCloseAsync(CloseEventArgs arg) {
            throw new NotImplementedException();
        }

        private ValueTask OnErrorAsync(ErrorEventArgs arg) {
            throw new NotImplementedException();
        }

        private async ValueTask OnMessageAsync(MessageEventArgs arg) {
            if (arg.Data.Length == 0) {
                return;
            }
        }
    }
}