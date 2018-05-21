﻿using Xunit;

namespace Manta.Projections.MsSql.Tests.Infrastructure
{
    [CollectionDefinition("Manta projections collection")]
    public class MantaTestsCollection : ICollectionFixture<LocalDbFixture>
    {
        // This class has no code, and is never created. Its purpose is simply
        // to be the place to apply [CollectionDefinition] and all the
        // ICollectionFixture<> interfaces.
    }
}