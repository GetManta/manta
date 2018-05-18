﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Jil;
using Manta.Sceleton;

namespace Benchmarks.Shared
{
    public class JilSerializer : ISerializer
    {
        private readonly Options _options;

        public JilSerializer()
        {
            _options = new Options(dateFormat: DateTimeFormat.ISO8601);
        }

        public object DeserializeMessage(string messageContractName, byte[] payload)
        {
            if (payload == null || payload.Length == 0) return null;

            var type = TestContracts.GetTypeByContractName(messageContractName);
            using (var output = new StreamReader(new MemoryStream(payload), Encoding.UTF8))
            {
                return JSON.Deserialize(output, type, _options);
            }
        }

        public Dictionary<string, object> DeserializeMetadata(byte[] payload)
        {
            if (payload == null || payload.Length == 0) return null;

            using (var output = new StreamReader(new MemoryStream(payload), Encoding.UTF8))
            {
                return JSON.Deserialize<Dictionary<string, object>>(output, _options);
            }
        }

        public ArraySegment<byte> SerializeMessage(object message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            using (var ms = new MemoryStream(256))
            using (var writer = new StreamWriter(ms))
            {
                JSON.Serialize(message, writer, _options);
                writer.Flush();
                return !ms.TryGetBuffer(out var buffer) ? new ArraySegment<byte>() : buffer;
            }
        }

        public ArraySegment<byte> SerializeMetadata(Dictionary<string, object> metadata)
        {
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));

            using (var ms = new MemoryStream(256))
            using (var writer = new StreamWriter(ms))
            {
                JSON.Serialize(metadata, writer, _options);
                writer.Flush();
                return !ms.TryGetBuffer(out var buffer) ? new ArraySegment<byte>() : buffer;
            }
        }
    }
}