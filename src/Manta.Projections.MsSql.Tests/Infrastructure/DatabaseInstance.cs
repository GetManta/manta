﻿using System;
using System.Data.SqlClient;
using System.Data.SqlLocalDb;
using System.IO;
using System.Threading.Tasks;
using Manta.MsSql;
using Manta.MsSql.Installer;
using Manta.Projections.MsSql.Installer;
using Manta.Sceleton;
using Manta.Sceleton.Installer;

namespace Manta.Projections.MsSql.Tests.Infrastructure
{
    public class DatabaseInstance : IDisposable
    {
        private readonly ISqlLocalDbInstance _localDbInstance;
        private readonly string _databaseName;
        private bool _databaseCreated;

        public DatabaseInstance(ISqlLocalDbInstance localDbInstance)
        {
            _localDbInstance = localDbInstance;
            _databaseName = "MantaTests_" + Guid.NewGuid().ToString("N");
            ConnectionString = CreateConnectionString(_localDbInstance, _databaseName);
        }

        private static string CreateConnectionString(ISqlLocalDbInstance localDbInstance, string databaseName)
        {
            var connectionStringBuilder = localDbInstance.CreateConnectionStringBuilder();
            connectionStringBuilder.MultipleActiveResultSets = true;
            connectionStringBuilder.IntegratedSecurity = true;
            connectionStringBuilder.InitialCatalog = databaseName;

            return connectionStringBuilder.ToString();
        }

        public string ConnectionString { get; }

        public async Task<Projector> GetProjector(Action<ProjectorBase> cfg)
        {
            if (!_databaseCreated) await CreateDatabase(GetLocation()).NotOnCapturedContext();
            var projector = new MsSqlProjector("uniqueProjectorName", ConnectionString, new JilSerializer());
            cfg?.Invoke(projector);
            return projector;
        }

        public async Task<IMessageStore> GetMessageStore(bool batching = true)
        {
            if (!_databaseCreated) await CreateDatabase(GetLocation()).NotOnCapturedContext();
            await ClearDatabase();
            return new MsSqlMessageStore(new MsSqlMessageStoreSettings(ConnectionString, batching));
        }

        public async Task ClearDatabase()
        {
            using (var connection = new SqlConnection(ConnectionString))
            {
                await connection.OpenAsync().NotOnCapturedContext();
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"TRUNCATE TABLE Streams;TRUNCATE TABLE StreamsProjectionCheckpoints;UPDATE StreamsStats SET MaxMessagePosition = 0, CountOfAllMessages = 0";
                    await cmd.ExecuteNonQueryAsync().NotOnCapturedContext();
                }
            }
        }

        private static string GetLocation()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "db");
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            return path;
        }

        private async Task CreateDatabase(string location = null)
        {
            var commandText = location == null
                ? $"CREATE DATABASE {_databaseName}"
                : $"CREATE DATABASE {_databaseName} ON (name = '{_databaseName}', filename = '{Path.Combine(location, _databaseName)}')";

            using (var connection = _localDbInstance.CreateConnection())
            {
                await connection.OpenAsync().NotOnCapturedContext();
                using (var command = new SqlCommand(commandText, connection))
                {
                    await command.ExecuteNonQueryAsync().NotOnCapturedContext();
                }
            }

            BaseInstaller installer = new MsSqlMessageStoreInstaller(ConnectionString);
            await installer.Execute();

            installer = new MsSqlProjectorsInstaller(ConnectionString);
            await installer.Execute();

            _databaseCreated = true;
        }

        public void Dispose()
        {
            DropDatabase();
        }

        public void DropDatabase()
        {
            using (var sqlConnection = new SqlConnection(ConnectionString))
            {
                // Fixes: "Cannot drop database because it is currently in use"
                SqlConnection.ClearPool(sqlConnection);
            }

            try
            {
                using (var cnn = _localDbInstance.CreateConnection())
                {
                    cnn.Open();
                    using (var command = new SqlCommand($"DROP DATABASE {_databaseName}", cnn))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch
            {
                // Nothing happened
            }

            try
            {
                var path = GetLocation();
                foreach (var file in Directory.GetFiles(path))
                {
                    File.Delete(file);
                }
                Directory.Delete(path, true);
            }
            catch
            {
                // nothing here
            }
            finally
            {
                _databaseCreated = false;
            }
        }
    }
}