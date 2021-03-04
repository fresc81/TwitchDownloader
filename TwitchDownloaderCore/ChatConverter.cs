using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Data.SQLite;
using System.Data.SQLite.Generic;
using TwitchDownloaderCore.Options;

namespace TwitchDownloaderCore
{
    public class ChatConverter
    {
        ChatConverterOptions converterOptions;

        public ChatConverter(ChatConverterOptions ConverterOptions)
        {
            converterOptions = ConverterOptions;
        }

        public async Task ConvertAsync(IProgress<ProgressReport> progress, CancellationToken cancellationToken)
        {
            // run conversion process asynchronously...
            try
            {

                progress.Report(new ProgressReport { reportType = ReportType.Message, data = $"'{converterOptions.InputFile}' -> '{converterOptions.OutputFile}'" });

                SQLiteConnectionStringBuilder connectionStringBuilder = new SQLiteConnectionStringBuilder
                {
                    DataSource = converterOptions.InputFile,
                    ForeignKeys = true,
                    ReadOnly = true
                };

                SQLiteConnection connection = new SQLiteConnection(connectionStringBuilder.ToString());
                await connection.OpenAsync(cancellationToken);

                SQLiteCommand command = connection.CreateCommand();
                command.CommandText = "SELECT messageId FROM message, user ON message.userId == user.userId LIMIT 10";

                SQLiteDataReader dataReader = await command.ExecuteReaderAsync(cancellationToken) as SQLiteDataReader;

                while (dataReader.Read())
                {
                    progress.Report(new ProgressReport { reportType = ReportType.Message, data = dataReader.GetFieldValue<string>(0) });
                }
                dataReader.Close();

                connection.Close();

            } catch
            {

                throw;

            } finally
            {

                GC.Collect();

            }
        }
    }
}
