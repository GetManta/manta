﻿using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using Manta.Sceleton;

namespace Manta.MsSql
{
    public class MsSqlMessageStoreAdvanced : IMessageStoreAdvanced
    {
        private readonly MsSqlMessageStoreSettings _settings;

        public MsSqlMessageStoreAdvanced(MsSqlMessageStoreSettings settings)
        {
            _settings = settings;
        }

        public Task TruncateStream(string stream, int toVersion, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public Task TruncateStream(string stream, DateTime toCreationDate, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public Task DeleteStream(string stream, int expectedVersion, bool hardDelete, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public async Task<RecordedMessage?> ReadMessage(string stream, int messageVersion, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (messageVersion <= ExpectedVersion.NoStream) throw new InvalidOperationException("Message version should be greater or equal 1.");

            _settings.Logger.Trace("Reading message {0} from stream '{1}'...", messageVersion, stream);

            using (var connection = new SqlConnection(_settings.ConnectionString))
            using (var cmd = connection.CreateCommandForReadMessage(stream, messageVersion))
            {
                await connection.OpenAsync(cancellationToken).NotOnCapturedContext();
                using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.SingleResult | CommandBehavior.SingleRow, cancellationToken).NotOnCapturedContext())
                {
                    if (!reader.HasRows)
                    {
                        _settings.Logger.Trace("Messages {0} for stream '{1}' not found.", messageVersion, stream);
                        return null;
                    }

                    _settings.Logger.Trace("Read message {0} for stream '{1}'.", messageVersion, stream);
                    await reader.ReadAsync(cancellationToken).NotOnCapturedContext();
                    return await reader.GetRecordedMessage(cancellationToken).NotOnCapturedContext();
                }
            }
        }

        public Task<MessageRecord> ReadSnapshot(string stream, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public Task SaveSnapshot(string stream, MessageRecord snapshot, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public Task<StreamMetadataResult> ReadStreamMetadata(string stream, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public Task SaveStreamMetadata(string stream, int expectedVersion, StreamMetadata metadata, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public async Task<long> ReadHeadMessagePosition(CancellationToken cancellationToken = default(CancellationToken))
        {
            _settings.Logger.Trace("Reading head message position...");

            using (var connection = new SqlConnection(_settings.ConnectionString))
            using (var cmd = connection.CreateCommandForReadHeadMessagePosition())
            {
                await connection.OpenAsync(cancellationToken).NotOnCapturedContext();
                var head = await cmd.ExecuteScalarAsync(cancellationToken).NotOnCapturedContext() ?? 0;
                if (head == DBNull.Value) head = 0;
                _settings.Logger.Trace("Read head message position as {0}.", head);
                return (long)head;
            }
        }
    }
}