using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TwitchDownloaderCore.Properties;

namespace TwitchDownloaderCore.Data
{
    public static class Query
    {

        public static async Task<string> GetStreamerNameFromId(SQLiteConnection connection, int streamerId, CancellationToken cancellationToken)
        {
            SQLiteCommand command = connection.CreateCommand();
            command.CommandText = Resources.QueryStreamerName;

            SQLiteParameter parameter = command.CreateParameter();
            parameter.Value = streamerId;
            command.Parameters.Add(parameter);

            string roomName = await command.ExecuteScalarAsync(cancellationToken) as string;
            return roomName.TrimStart('#'); // trim IRC specific channel prefix
        }

        public static async Task<SQLiteDataReader> OpenMessageReader(SQLiteConnection connection, int streamerId, CancellationToken cancellationToken)
        {
            SQLiteCommand command = connection.CreateCommand();
            command.CommandText = Resources.QueryMessages;

            SQLiteParameter parameter = command.CreateParameter();
            parameter.Value = streamerId;
            command.Parameters.Add(parameter);

            return await command.ExecuteReaderAsync(cancellationToken) as SQLiteDataReader;
        }
    }
}
