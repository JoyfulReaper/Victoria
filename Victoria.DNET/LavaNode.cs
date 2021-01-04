using System.Threading.Tasks;
using Discord.WebSocket;
using Victoria.Wrappers;

namespace Victoria.DNET {
    public class LavaNode : AbstractLavaNode {
        public LavaNode(NodeConfiguration nodeConfiguration, DiscordSocketClient socketClient)
            : this(nodeConfiguration, socketClient as BaseSocketClient) { }

        public LavaNode(NodeConfiguration nodeConfiguration, DiscordShardedClient shardedClient)
            : this(nodeConfiguration, shardedClient as BaseSocketClient) { }

        public LavaNode(NodeConfiguration nodeConfiguration, BaseSocketClient baseClient)
            : base(nodeConfiguration) {
            DiscordClient = new DiscordClient {
                UserId = baseClient.CurrentUser.Id,
                Shards = baseClient switch {
                    DiscordSocketClient                => 0,
                    DiscordShardedClient shardedClient => shardedClient.Shards.Count,
                    _                                  => 0
                }
            };

            baseClient.VoiceServerUpdated += OnVoiceServerUpdated;
            baseClient.UserVoiceStateUpdated += OnUserVoiceStateUpdated;
        }

        private Task OnVoiceServerUpdated(SocketVoiceServer voiceServer) {
            return DiscordClient.OnVoiceServerUpdated.Invoke(new VoiceServer {
                GuildId = voiceServer.Guild.Id,
                Endpoint = voiceServer.Endpoint,
                Token = voiceServer.Token
            }).AsTask();
        }

        private Task OnUserVoiceStateUpdated(SocketUser user, SocketVoiceState oldState,
                                             SocketVoiceState currentState) {
            return DiscordClient.OnUserVoiceStateUpdated.Invoke(new VoiceState {
                UserId = user.Id,
                SessionId = currentState.VoiceSessionId ?? oldState.VoiceSessionId,
                GuildId = (currentState.VoiceChannel ?? oldState.VoiceChannel).Guild.Id,
                ChannelId = (currentState.VoiceChannel ?? oldState.VoiceChannel).Id
            }).AsTask();
        }
    }
}