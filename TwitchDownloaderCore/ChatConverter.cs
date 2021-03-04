using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Data.SQLite;
using System.Data.SQLite.Generic;
using TwitchDownloaderCore.Options;
using System.IO;
using Newtonsoft.Json;
using TwitchDownloaderCore.Data;

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
            progress.Report(new ProgressReport { reportType = ReportType.Message, data = $"'{converterOptions.InputFile}' -> '{converterOptions.OutputFile}'" });

            // TODO provide start time and duration...


            // create SQL connection string...
            SQLiteConnectionStringBuilder connectionStringBuilder = new SQLiteConnectionStringBuilder
            {
                DataSource = converterOptions.InputFile,
                ForeignKeys = true,
                ReadOnly = true
            };

            // open database asynchronously...
            SQLiteConnection connection = new SQLiteConnection(connectionStringBuilder.ToString());
            await connection.OpenAsync(cancellationToken);

            try
            {
                int channelId = int.Parse(converterOptions.ChannelId);

                // prepare output JSON structure...
                List<Comment> comments = new List<Comment>();
                ChatRoot chatRoot = new ChatRoot
                {
                    streamer = new Streamer
                    {
                        id = channelId,
                        name = await Data.Query.GetStreamerNameFromId(connection, channelId, cancellationToken)
                    },
                    video = new VideoTime
                    {
                        start = 0.0,
                        end = Query.ToEpoch(converterOptions.EndTime) - Query.ToEpoch(converterOptions.StartTime)
                    },
                    comments = comments
                };

                // iterate messages from SQLite database...
                double currentTimeOffset = 0.0;
                SQLiteDataReader messageReader = await Data.Query.OpenMessageReader(connection, channelId, converterOptions.StartTime, converterOptions.EndTime, cancellationToken);
                while (await messageReader.ReadAsync(cancellationToken))
                {
                    int c = 0;
                    string messageId = await messageReader.GetFieldValueAsync<string>(c++, cancellationToken);

                    long timepoint = await messageReader.GetFieldValueAsync<long>(c++, cancellationToken);
                    double offsetTimepoint = await messageReader.IsDBNullAsync(c, cancellationToken) ? 0 : await messageReader.GetFieldValueAsync<double>(c, cancellationToken); ++c;

                    long tmiSent = await messageReader.GetFieldValueAsync<long>(c++, cancellationToken);

                    long userId = await messageReader.GetFieldValueAsync<long>(c++, cancellationToken);
                    string nickname = await messageReader.GetFieldValueAsync<string>(c++, cancellationToken);
                    string displayName = await messageReader.GetFieldValueAsync<string>(c++, cancellationToken);
                    string color = await messageReader.GetFieldValueAsync<string>(c++, cancellationToken);
                    
                    long mod = await messageReader.GetFieldValueAsync<long>(c++, cancellationToken);
                    long subscriber = await messageReader.GetFieldValueAsync<long>(c++, cancellationToken);
                    long turbo = await messageReader.GetFieldValueAsync<long>(c++, cancellationToken);

                    string badgeInfo = await messageReader.GetFieldValueAsync<string>(c++, cancellationToken);
                    string badges = await messageReader.GetFieldValueAsync<string>(c++, cancellationToken);

                    List<UserBadge> userBadges = new List<UserBadge>();
                    foreach (string badge in badges.Split(','))
                    {
                        string[] badgeParts = badge.Split('/');
                        userBadges.Add(new UserBadge { _id = badgeParts[0], version = badgeParts.Length > 1 ? badgeParts[1] : "" });
                    }
                    string emotes = await messageReader.GetFieldValueAsync<string>(c++, cancellationToken);

                    long roomId = await messageReader.GetFieldValueAsync<long>(c++, cancellationToken);
                    string target = await messageReader.GetFieldValueAsync<string>(c++, cancellationToken);
                    string text = await messageReader.GetFieldValueAsync<string>(c++, cancellationToken);

                    currentTimeOffset += offsetTimepoint;

                    // add message...
                    Comment comment = new Comment
                    {
                        channel_id = roomId.ToString(),
                        commenter = new Commenter { display_name = displayName.ToString(), name = nickname.ToString(), _id = userId.ToString(), type = "user" },
                        content_id = messageId.ToString(),
                        // integer based division followed by double based division
                        // gets rid of some unnessesary decimal places due the database storing microsecond precision
                        content_offset_seconds = currentTimeOffset,
                        content_type = "video",
                        created_at = Query.FromEpoch(tmiSent / 1000),
                        updated_at = Query.FromEpoch(tmiSent / 1000),
                        source = "chat",
                        state = "published",
                        message = new Message
                        {
                            bits_spent = 0,
                            body = text.ToString(),
                            emoticons = new List<Emoticon2>(), // TODO implement
                            fragments = new List<Fragment> { new Fragment { text = text.ToString() } }, // TODO implement
                            is_action = false,
                            user_badges = userBadges,
                            user_color = color == "" ? "#FFFFFF" : color
                        },
                        _id = messageId
                    };
                    comments.Add(comment);

                    // report for UI (not yet implemented)
                    // progress.Report(new ProgressReport { reportType = ReportType.Percent, data = messageReader.StepCount / 429438 * 100 /* percent */ });

                    if (Query.FromEpoch(timepoint / 1000000L) > converterOptions.EndTime)
                    {
                        break;
                    }
                }

                messageReader.Close();

                // write JSON file...
                using (TextWriter writer = File.CreateText(converterOptions.OutputFile))
                {
                    var serializer = new JsonSerializer();
                    serializer.Serialize(writer, chatRoot);
                }
            }
            catch
            {
                throw;
            }
            finally
            {
                // close connection...
                connection.Close();
                GC.Collect();
            }

        }
    }
}
