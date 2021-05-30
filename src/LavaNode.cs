using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Victoria.Payloads;
using Victoria.Payloads.Player;
using Victoria.Player;
using Victoria.Responses.Search;
using Victoria.WebSocket;
using Victoria.WebSocket.EventArgs;

namespace Victoria {
    /// <summary>
    /// Represents a single connection to a Lavalink server.
    /// </summary>
    public class LavaNode : LavaNode<LavaPlayer> {
        /// <inheritdoc />
        public LavaNode(DiscordSocketClient socketClient, NodeConfiguration nodeConfiguration)
            : base(socketClient, nodeConfiguration) { }

        /// <inheritdoc />
        public LavaNode(DiscordShardedClient shardedClient, NodeConfiguration nodeConfiguration)
            : base(shardedClient, nodeConfiguration) { }
    }

    /// <summary>
    ///     Represents a single connection to a Lavalink server with custom <typeparamref name="TPlayer"/>.
    /// </summary>
    /// <typeparam name="TPlayer">Where TPlayer is inherited from <see cref="LavaPlayer" /></typeparam>.
    public class LavaNode<TPlayer> : IAsyncDisposable
        where TPlayer : LavaPlayer {
        /// <summary>
        ///     Checks if the client has an active WebSocket connection.
        /// </summary>
        public bool IsConnected
            => Volatile.Read(ref _refConnected);

        /// <summary>
        ///     Collection of <typeparamref name="TPlayer" />.
        /// </summary>
        public IEnumerable<TPlayer> Players
            => _playerCache.Values;

        private readonly NodeConfiguration _nodeConfiguration;
        private readonly WebSocketClient _webSocketClient;
        private readonly BaseSocketClient _baseSocketClient;
        private readonly ConcurrentDictionary<ulong, TPlayer> _playerCache;
        private readonly ConcurrentDictionary<ulong, VoiceState> _voiceStates;

        private bool _refConnected;

        /// <inheritdoc />
        public LavaNode(DiscordSocketClient socketClient, NodeConfiguration nodeConfiguration)
            : this(socketClient as BaseSocketClient, nodeConfiguration) { }

        /// <inheritdoc />
        public LavaNode(DiscordShardedClient shardedClient, NodeConfiguration nodeConfiguration)
            : this(shardedClient as BaseSocketClient, nodeConfiguration) { }

        private LavaNode(BaseSocketClient socketClient, NodeConfiguration nodeConfiguration) {
            _nodeConfiguration = nodeConfiguration;

            _baseSocketClient = socketClient;
            socketClient.UserVoiceStateUpdated += OnUserVoiceStateUpdatedAsync;
            socketClient.VoiceServerUpdated += OnVoiceServerUpdatedAsync;

            _webSocketClient = new WebSocketClient(new Uri(_nodeConfiguration.SocketEndpoint));
            _webSocketClient.OnOpenAsync += OnOpenAsync;
            _webSocketClient.OnErrorAsync += OnErrorAsync;
            _webSocketClient.OnCloseAsync += OnCloseAsync;
            _webSocketClient.OnDataAsync += OnDataAsync;

            _playerCache = new ConcurrentDictionary<ulong, TPlayer>();
            _voiceStates = new ConcurrentDictionary<ulong, VoiceState>();
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync() {
            if (IsConnected) {
                await DisconnectAsync()
                    .ConfigureAwait(false);
            }

            await _webSocketClient.DisposeAsync()
                .ConfigureAwait(false);

            _playerCache.Clear();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///     Starts a WebSocket connection to the specified <see cref="NodeConfiguration.Hostname" />:<see cref="NodeConfiguration.Port" />
        ///     and hooks into <see cref="BaseSocketClient" /> events.
        /// </summary>
        /// <exception cref="InvalidOperationException">Throws if client is already connected.</exception>
        public async Task ConnectAsync() {
            if (Volatile.Read(ref _refConnected)) {
                throw new InvalidOperationException(
                    $"You must call {nameof(DisconnectAsync)} or {nameof(DisposeAsync)} before calling {nameof(ConnectAsync)}.");
            }

            if (_baseSocketClient?.CurrentUser == null || _baseSocketClient.CurrentUser.Id == 0) {
                throw new InvalidOperationException($"{nameof(_baseSocketClient)} is not in ready state.");
            }

            var shards = _baseSocketClient switch {
                DiscordSocketClient socketClient => await socketClient.GetRecommendedShardCountAsync()
                    .ConfigureAwait(false),
                DiscordShardedClient shardedClient => shardedClient.Shards.Count,
                _                                  => 1
            };

            _webSocketClient.AddHeader("Authorization", _nodeConfiguration.Authorization);
            _webSocketClient.AddHeader("Num-Shards", $"{shards}");
            _webSocketClient.AddHeader("User-Id", $"{_baseSocketClient.CurrentUser.Id}");

            if (_nodeConfiguration.EnableResume) {
                _webSocketClient.AddHeader("Resume-Key", _nodeConfiguration.ResumeKey);
            }

            if (!string.IsNullOrWhiteSpace(_nodeConfiguration.UserAgent)) {
                _webSocketClient.AddHeader("User-Agent", _nodeConfiguration.UserAgent);
            }

            await _webSocketClient.ConnectAsync()
                .ConfigureAwait(false);
        }

        /// <summary>
        ///     Disposes all players and closes websocket connection.
        /// </summary>
        /// <exception cref="InvalidOperationException">Throws if client isn't connected.</exception>
        public async Task DisconnectAsync() {
            if (!Volatile.Read(ref _refConnected)) {
                throw new InvalidOperationException("Can't disconnect when client isn't connected.");
            }

            foreach (var (_, value) in _playerCache) {
                await value.DisposeAsync()
                    .ConfigureAwait(false);
            }

            _playerCache.Clear();
            await _webSocketClient.DisconnectAsync()
                .ConfigureAwait(false);
        }

        /// <summary>
        ///     Joins the specified voice channel and returns the connected <typeparamref name="TPlayer" />.
        /// </summary>
        /// <param name="voiceChannel">An instance of <see cref="IVoiceChannel" />.</param>
        /// <param name="textChannel">An instance of <see cref="ITextChannel" />.</param>
        /// <returns>
        ///     <typeparamref name="TPlayer" />
        /// </returns>
        public async Task<TPlayer> JoinAsync(IVoiceChannel voiceChannel, ITextChannel textChannel = default) {
            if (voiceChannel == null) {
                throw new ArgumentNullException(nameof(voiceChannel));
            }

            if (_playerCache.TryGetValue(voiceChannel.GuildId, out var player)) {
                return player;
            }

            await voiceChannel.ConnectAsync(_nodeConfiguration.SelfDeaf, false, true)
                .ConfigureAwait(false);

            player = (TPlayer) Activator.CreateInstance(typeof(TPlayer), _webSocketClient, voiceChannel, textChannel);
            _playerCache.TryAdd(voiceChannel.GuildId, player);
            return player;
        }

        /// <summary>
        ///     Leaves the specified channel only if <typeparamref name="TPlayer" /> is connected to it.
        /// </summary>
        /// <param name="voiceChannel">An instance of <see cref="IVoiceChannel" />.</param>
        /// <exception cref="InvalidOperationException">Throws if client isn't connected.</exception>
        public async Task LeaveAsync(IVoiceChannel voiceChannel) {
            if (!Volatile.Read(ref _refConnected)) {
                throw new InvalidOperationException("Can't execute this operation when client isn't connected.");
            }

            if (!_playerCache.TryGetValue(voiceChannel.GuildId, out var player)) {
                return;
            }

            await player.DisposeAsync()
                .ConfigureAwait(false);

            await voiceChannel.DisconnectAsync()
                .ConfigureAwait(false);

            _playerCache.TryRemove(voiceChannel.GuildId, out _);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="searchType"></param>
        /// <param name="query"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public async Task<SearchResponse> SearchAsync(SearchType searchType, string query) {
            if (string.IsNullOrWhiteSpace(query)) {
                throw new ArgumentNullException(nameof(query));
            }

            var urlPath = searchType switch {
                SearchType.YouTube    => $"/loadtracks?identifier={WebUtility.UrlEncode($"scsearch:{query}")}",
                SearchType.SoundCloud => $"/loadtracks?identifier={WebUtility.UrlEncode($"ytsearch:{query}")}",
                SearchType.Direct     => $"/loadtracks?identifier={query}"
            };

            using var requestMessage =
                new HttpRequestMessage(HttpMethod.Get, $"{_nodeConfiguration.HttpEndpoint}{urlPath}") {
                    Headers = {
                        {"Authorization", _nodeConfiguration.Authorization}
                    }
                };

            var searchResponse = await Extensions.ReadAsJsonAsync<SearchResponse>(requestMessage);
            return searchResponse;
        }

        /// <summary>
        ///     Checks if <typeparamref name="TPlayer" /> exists for specified guild.
        /// </summary>
        /// <param name="guild">An instance of <see cref="IGuild" />.</param>
        /// <returns>
        ///     <see cref="bool" />
        /// </returns>
        public bool HasPlayer(IGuild guild) {
            return _playerCache.ContainsKey(guild.Id);
        }

        /// <summary>
        ///     Returns either an existing or null player.
        /// </summary>
        /// <param name="guild">An instance of <see cref="IGuild" />.</param>
        /// <param name="player">An instance of <typeparamref name="TPlayer" /></param>
        /// <returns>
        ///     <see cref="bool" />
        /// </returns>
        public bool TryGetPlayer(IGuild guild, out TPlayer player) {
            return _playerCache.TryGetValue(guild.Id, out player);
        }

        private Task OnOpenAsync() {
            Volatile.Write(ref _refConnected, true);
            if (!_nodeConfiguration.EnableResume) {
                return Task.CompletedTask;
            }

            return _webSocketClient.SendAsync(
                new ResumePayload(_nodeConfiguration.ResumeKey, _nodeConfiguration.ResumeTimeout));
        }

        private Task OnCloseAsync(CloseEventArgs arg) {
            Volatile.Write(ref _refConnected, false);
            return Task.CompletedTask;
        }

        private Task OnErrorAsync(ErrorEventArgs arg) {
            throw new NotImplementedException();
        }

        private async Task OnDataAsync(DataEventArgs arg) {
            if (arg.IsEmpty) {
                return;
            }

            JsonSerializer.Deserialize<object>(arg.Data);
        }

        private Task OnUserVoiceStateUpdatedAsync(SocketUser user, SocketVoiceState pastState,
                                                  SocketVoiceState currentState) {
            if (_baseSocketClient.CurrentUser?.Id != user.Id) {
                return Task.CompletedTask;
            }

            var voiceState = new VoiceState {
                UserId = user.Id,
                GuildId = (currentState.VoiceChannel ?? pastState.VoiceChannel).Guild.Id,
                SessionId = currentState.VoiceSessionId ?? pastState.VoiceSessionId,
                ChannelId = (currentState.VoiceChannel ?? pastState.VoiceChannel).Id
            };

            _voiceStates.TryUpdate(voiceState.GuildId, voiceState, default);
            return Task.CompletedTask;
        }

        private Task OnVoiceServerUpdatedAsync(SocketVoiceServer voiceServer) {
            if (!_voiceStates.TryGetValue(voiceServer.Guild.Id, out var voiceState)) {
                return Task.CompletedTask;
            }

            return _webSocketClient.SendAsync(new ServerUpdatePayload {
                SessionId = voiceState.SessionId,
                GuildId = $"{voiceServer.Guild.Id}",
                VoiceServerPayload = new VoiceServerPayload {
                    Endpoint = voiceServer.Endpoint,
                    Token = voiceServer.Token
                }
            });
        }
    }
}