﻿using System;
using Manta.MsSql.Tests.Infrastructure;
using Manta.Sceleton;
using Xunit;
// ReSharper disable PossibleNullReferenceException

namespace Manta.MsSql.Tests
{
    public class TruncateStreamToCreationDate : TestsBase
    {
        public TruncateStreamToCreationDate(LocalDbFixture fixture) : base(fixture) { }

        [Fact]
        public async void Truncate_stream_with_expected_version_doesnt_throws()
        {
            var store = await GetMessageStore();
            const string streamName = "test-123";
            var data = GetUncommitedMessages();

            await store.AppendToStream(streamName, ExpectedVersion.NoStream, data).NotOnCapturedContext();

            var exception = await Record.ExceptionAsync(async () => await store.Advanced.TruncateStream(streamName, data.Messages.Length, DateTime.Now.AddSeconds(-5)).NotOnCapturedContext());

            Assert.Null(exception);
        }

        [Fact]
        public async void Truncating_stream_when_creationDate_is_greater_than_expected_version_throws()
        {
            var store = await GetMessageStore();
            const string streamName = "test-123";
            var data = GetUncommitedMessages();

            await store.AppendToStream(streamName, ExpectedVersion.NoStream, data).NotOnCapturedContext();

            var exception = await Record.ExceptionAsync(async () => await store.Advanced.TruncateStream(streamName, expectedVersion: 1, toCreationDate: DateTime.Now.AddSeconds(5)).NotOnCapturedContext());

            Assert.NotNull(exception);
        }

        [Fact]
        public async void Truncating_stream_with_NoStream_expected_version_throws_InvalidOperationException()
        {
            var store = await GetMessageStore();
            const string streamName = "test-123";

            await Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                {
                    await store.Advanced.TruncateStream(streamName, expectedVersion: ExpectedVersion.NoStream, toCreationDate: DateTime.Now.AddSeconds(-5)).NotOnCapturedContext();
                });
        }

        private static UncommittedMessages GetUncommitedMessages()
        {
            return new UncommittedMessages(
                Guid.NewGuid(),
                new[]
                {
                    new MessageRecord(Guid.NewGuid(), 0, new byte[]{ 1, 2, 3 }),
                    new MessageRecord(Guid.NewGuid(), 1, new byte[]{ 1, 2, 3 }),
                    new MessageRecord(Guid.NewGuid(), 0, new byte[]{ 1, 2, 3 })
                });
        }
    }
}