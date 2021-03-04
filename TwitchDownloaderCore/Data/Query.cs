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

        public static async Task<SQLiteDataReader> OpenMessageReader(SQLiteConnection connection, int streamerId, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            SQLiteCommand command = connection.CreateCommand();
            command.CommandText = Resources.QueryMessages;

            SQLiteParameter paramStreamerId = command.CreateParameter();
            paramStreamerId.Value = streamerId;
            command.Parameters.Add(paramStreamerId);

            SQLiteParameter paramBegin = command.CreateParameter();
            paramBegin.Value = ToEpoch(begin) * 1000000;
            command.Parameters.Add(paramBegin);

            SQLiteParameter paramEnd = command.CreateParameter();
            paramEnd.Value = ToEpoch(end) * 1000000;
            command.Parameters.Add(paramEnd);

            return await command.ExecuteReaderAsync(cancellationToken) as SQLiteDataReader;
        }

        public static long ToEpoch(DateTime dateTime) => (long)(dateTime - new DateTime(1970, 1, 1)).TotalSeconds;

        public static DateTime FromEpoch(long epoch) => new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified).AddSeconds(epoch);

    }
}
