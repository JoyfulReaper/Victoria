namespace Victoria.Wrappers {
    /// <summary>
    /// 
    /// </summary>
    public struct VoiceState {
        /// <summary>
        /// 
        /// </summary>
        public ulong UserId { get; init; }
        
        /// <summary>
        /// 
        /// </summary>
        public string OldSessionId { get; init; }
        
        /// <summary>
        /// 
        /// </summary>
        public string CurrentSessionId { get; init; }
    }
}