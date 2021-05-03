﻿using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using Victoria.Converters;

namespace Victoria.Player.Decoder {
    /// <summary>
    /// Helper class for decoding Lavalink's <see cref="LavaTrack"/> hash.
    /// </summary>
    public readonly struct TrackDecoder {
        /// <summary>
        ///     Decodes the hash for the specified track.
        /// </summary>
        /// <param name="trackHash">Track's hash.</param>
        /// <returns>
        ///     <see cref="LavaTrack" />
        /// </returns>
        public static LavaTrack Decode(string trackHash) {
            Span<byte> hashBuffer = stackalloc byte[trackHash.Length];
            Encoding.ASCII.GetBytes(trackHash, hashBuffer);
            Base64.DecodeFromUtf8InPlace(hashBuffer, out var bytesWritten);
            var javaReader = new JavaBinaryReader(hashBuffer[..bytesWritten]);

            // Reading header
            var header = javaReader.Read<int>();
            var flags = (int) ((header & 0xC0000000L) >> 30);
            var hasVersion = (flags & 1) != 0;
            var _ = hasVersion
                ? javaReader.Read<sbyte>()
                : 1;

            var track = new LavaTrack(
                trackHash,
                title: javaReader.ReadString(),
                author: javaReader.ReadString(),
                duration: javaReader.Read<long>(),
                id: javaReader.ReadString(),
                isStream: javaReader.Read<bool>(),
                url: javaReader.Read<bool>()
                    ? javaReader.ReadString()
                    : string.Empty,
                position: default,
                canSeek: true);

            return track;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="nodeConfiguration"></param>
        /// <param name="trackHash">Track's hash.</param>
        /// <returns>
        ///     <see cref="LavaTrack" />
        /// </returns>
        public static Task<LavaTrack> DecodeAsync(NodeConfiguration nodeConfiguration, string trackHash) {
            if (nodeConfiguration == null) {
                throw new ArgumentNullException(nameof(nodeConfiguration));
            }

            if (string.IsNullOrWhiteSpace(trackHash)) {
                throw new ArgumentNullException(nameof(trackHash));
            }

            var requestMessage = new HttpRequestMessage(HttpMethod.Get,
                $"{nodeConfiguration.HttpEndpoint}/decodeTrack?track={trackHash}") {
                Headers = {
                    {"Authroization", nodeConfiguration.Authorization}
                }
            };

            return Extensions.ReadAsJsonAsync<LavaTrack>(requestMessage);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="nodeConfiguration"></param>
        /// <param name="trackHashes"></param>
        /// <returns></returns>
        public static Task<IEnumerable<LavaTrack>> DecodeAsync(NodeConfiguration nodeConfiguration,
                                                               params string[] trackHashes) {
            if (nodeConfiguration == null) {
                throw new ArgumentNullException(nameof(nodeConfiguration));
            }

            if (trackHashes?.Length == 0) {
                throw new ArgumentNullException(nameof(trackHashes));
            }

            var requestMessage =
                new HttpRequestMessage(HttpMethod.Post, $"{nodeConfiguration.HttpEndpoint}/decodeTracks") {
                    Headers = {
                        {"Authorization", nodeConfiguration.Authorization}
                    },
                    Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(trackHashes))
                };

            return Extensions.ReadAsJsonAsync<IEnumerable<LavaTrack>>(requestMessage, new LavaTracksPropertyConverter());
        }
    }
}