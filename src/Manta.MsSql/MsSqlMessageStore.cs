﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using Manta.Sceleton;

namespace Manta.MsSql
{
    /// <inheritdoc />
    public class MsSqlMessageStore : IMessageStore
    {
        private readonly MsSqlMessageStoreSettings _settings;

        public MsSqlMessageStore(MsSqlMessageStoreSettings settings)
        {
            _settings = settings;
            CheckForBatchingAvailability();
            Advanced = new MsSqlMessageStoreAdvanced(_settings);
        }

        private void CheckForBatchingAvailability()
        {
            if (SqlClientSqlCommandSet.IsSqlCommandSetAvailable)
            {
                _settings.Logger.Info("Batching is not available.");
            }
        }

        /// <inheritdoc />
        public async Task<RecordedStream> ReadStreamForward(string stream, int fromVersion, CancellationToken token = default(CancellationToken))
        {
            _settings.Logger.Trace("Reading stream '{0}' forward from version {1}...", stream, fromVersion);

            using (var connection = new SqlConnection(_settings.ConnectionString))
            using (var cmd = connection.CreateCommandForReadStreamForward(stream, fromVersion))
            {
                await connection.OpenAsync(token).NotOnCapturedContext();
                using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult, token).NotOnCapturedContext())
                {
                    if (!reader.HasRows)
                    {
                        _settings.Logger.Trace("Read 0 messages for '{0}' stream from version {1}.", stream, fromVersion);
                        return RecordedStream.Empty();
                    }

                    var messages = new List<RecordedMessage>(20); // 20? How many will be enough?
                    while (await reader.ReadAsync(token).NotOnCapturedContext())
                    {
                        messages.Add(reader.GetRecordedMessage());
                    }
                    _settings.Logger.Trace("Read {0} messages for '{1}' stream from version {2}.", messages.Count, stream, fromVersion);
                    return new RecordedStream(messages.ToArray());
                }
            }
        }

        /// <inheritdoc />
        public async Task AppendToStream(string stream, int expectedVersion, UncommittedMessages data, CancellationToken token = default(CancellationToken))
        {
            if (stream.IsNullOrEmpty()) throw new ArgumentNullException(nameof(stream));
            if (expectedVersion < ExpectedVersion.Any) throw new ArgumentException("Expected version must be greater or equal 1, or 'Any', or 'NoStream'.", nameof(expectedVersion));
            if (data == null) throw new ArgumentNullException(nameof(data));

            try
            {
                switch (expectedVersion)
                {
                    default:
                        _settings.Logger.Trace("Appending {0} messages to stream '{1}' with expected version {2}...", data.Messages.Length, stream, expectedVersion);
                        await AppendToStreamWithExpectedVersion(stream, expectedVersion, data, token).NotOnCapturedContext();
                        break;
                    case ExpectedVersion.Any:
                        _settings.Logger.Trace("Appending {0} messages to stream '{1}' with any version...", data.Messages.Length, stream);
                        await AppendToStreamWithAnyVersion(stream, data, token).NotOnCapturedContext();
                        break;
                    case ExpectedVersion.NoStream:
                        _settings.Logger.Trace("Appending {0} messages to stream '{1}' where stream not existed yet...", data.Messages.Length, stream);
                        await AppendToStreamWithExpectedVersion(stream, ExpectedVersion.NoStream, data, token).NotOnCapturedContext();
                        break;
                }

                _settings.Linearizer?.Start();
            }
            catch (WrongExpectedVersionException)
            {
                throw;
            }
            catch (Exception e)
            {
                _settings.Logger.Error(e.ToString());
            }
        }

        private async Task AppendToStreamWithExpectedVersion(string stream, int expectedVersion, UncommittedMessages data, CancellationToken token)
        {
            if (SqlClientSqlCommandSet.IsSqlCommandSetAvailable && _settings.Batching && data.Messages.Length > 1)
            {
                using (var connection = new SqlConnection(_settings.ConnectionString))
                using (var batch = new SqlClientSqlCommandSet(connection))
                {
                    var messageVersion = expectedVersion;
                    foreach (var msg in data.Messages)
                    {
                        messageVersion += 1;

                        var cmd = expectedVersion == 0 && messageVersion == 1
                            ? connection.CreateCommandToAppendingWithNoStream(stream, data, msg)
                            : connection.CreateCommandToAppendingWithExpectedVersion(stream, data, msg, messageVersion);

                        batch.Append(cmd);
                    }

                    await connection.OpenAsync(token).NotOnCapturedContext();
                    using (var tran = connection.BeginTransaction(IsolationLevel.ReadCommitted))
                    {
                        try
                        {
                            batch.Transaction = tran;
                            await batch.ExecuteNonQueryAsync(token).NotOnCapturedContext();
                            tran.Commit();
                        }
                        catch (SqlException e)
                        {
                            tran.Rollback();

                            if (e.IsUniqueConstraintViolation() || e.IsWrongExpectedVersionRised())
                            {
                                throw new WrongExpectedVersionException($"Appending {data.Messages.Length} messages to stream '{stream}' with expected version {ExpectedVersion.Parse(expectedVersion)} failed.", e);
                            }
                            throw;
                        }
                    }
                }
            }
            else
            {
                using (var connection = new SqlConnection(_settings.ConnectionString))
                {
                    await connection.OpenAsync(token).NotOnCapturedContext();
                    using (var tran = connection.BeginTransaction(IsolationLevel.ReadCommitted))
                    {
                        try
                        {
                            var messageVersion = expectedVersion;
                            foreach (var msg in data.Messages)
                            {
                                messageVersion += 1;
                                using (var cmd = expectedVersion == 0 && messageVersion == 1
                                    ? connection.CreateCommandToAppendingWithNoStream(stream, data, msg)
                                    : connection.CreateCommandToAppendingWithExpectedVersion(stream, data, msg, messageVersion))
                                {
                                    cmd.Transaction = tran;
                                    await cmd.ExecuteNonQueryAsync(token).NotOnCapturedContext();
                                }
                            }
                            tran.Commit();
                        }
                        catch (SqlException e)
                        {
                            tran.Rollback();

                            if (e.IsUniqueConstraintViolation() || e.IsWrongExpectedVersionRised())
                            {
                                throw new WrongExpectedVersionException($"Appending {data.Messages.Length} messages to stream '{stream}' with expected version {ExpectedVersion.Parse(expectedVersion)} failed.", e);
                            }
                            throw;
                        }
                    }
                }
            }
        }

        private async Task AppendToStreamWithAnyVersion(string stream, UncommittedMessages data, CancellationToken token)
        {
            _settings.Logger.Trace("Appending {0} messages to stream '{1}' with any version...", data.Messages.Length, stream);

            if (SqlClientSqlCommandSet.IsSqlCommandSetAvailable && _settings.Batching && data.Messages.Length > 1)
            {
                using (var connection = new SqlConnection(_settings.ConnectionString))
                using (var batch = new SqlClientSqlCommandSet(connection))
                {
                    foreach (var msg in data.Messages)
                    {
                        batch.Append(connection.CreateCommandToAppendingWithAnyVersion(stream, data, msg));
                    }

                    await connection.OpenAsync(token).NotOnCapturedContext();
                    using (var tran = connection.BeginTransaction(IsolationLevel.ReadCommitted))
                    {
                        try
                        {
                            batch.Transaction = tran;
                            await batch.ExecuteNonQueryAsync(token).NotOnCapturedContext();
                            tran.Commit();
                        }
                        catch (SqlException e)
                        {
                            tran.Rollback();

                            if (e.IsUniqueConstraintViolation() || e.IsWrongExpectedVersionRised())
                            {
                                throw new WrongExpectedVersionException($"Appending {data.Messages.Length} messages to stream '{stream}' with any version failed.", e);
                            }
                            throw;
                        }
                    }
                }
            }
            else
            {
                using (var connection = new SqlConnection(_settings.ConnectionString))
                {
                    await connection.OpenAsync(token).NotOnCapturedContext();
                    using (var tran = connection.BeginTransaction(IsolationLevel.ReadCommitted))
                    {
                        try
                        {
                            foreach (var msg in data.Messages)
                            {
                                using (var cmd = connection.CreateCommandToAppendingWithAnyVersion(stream, data, msg))
                                {
                                    cmd.Transaction = tran;
                                    await cmd.ExecuteNonQueryAsync(token).NotOnCapturedContext();
                                }
                            }
                            tran.Commit();
                        }
                        catch (SqlException e)
                        {
                            tran.Rollback();

                            if (e.IsUniqueConstraintViolation() || e.IsWrongExpectedVersionRised())
                            {
                                throw new WrongExpectedVersionException($"Appending {data.Messages.Length} messages to stream '{stream}' with any version failed.", e);
                            }
                            throw;
                        }
                    }
                }
            }
        }

        /// <inheritdoc />
        public IMessageStoreAdvanced Advanced { get; }
    }
}
