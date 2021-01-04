using Victoria.Wrappers;

namespace Victoria {
    /// <summary>
    /// 
    /// </summary>
    public record NodeConfiguration {
        /// <summary>
        /// 
        /// </summary>
        public DiscordClientWrapper DiscordClient { get; init; }

        /// <summary>
        /// 
        /// </summary>
        public string Hostname { get; init; } = "127.0.0.1";

        /// <summary>
        /// 
        /// </summary>
        public int Port { get; init; } = 2333;

        /// <summary>
        /// 
        /// </summary>
        public bool IsSecure { get; init; } = false;

        /// <summary>
        /// 
        /// </summary>
        public string Authorization { get; init; } = "youshallnotpass";

        internal string SocketEndpoint
            => (IsSecure ? "wss" : "ws") + Endpoint;

        internal string HttpEndpoint
            => (IsSecure ? "https" : "http") + Endpoint;

        internal string Endpoint
            => $"://{Hostname}:{Port}/";
    }
}