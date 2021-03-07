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
using TwitchDownloaderCore.TwitchObjects;
using SkiaSharp;
using System.Linq;

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
                        name = await Query.GetStreamerNameFromId(connection, channelId, cancellationToken)
                    },
                    video = new VideoTime
                    {
                        start = 0.0,
                        end = Query.ToEpoch(converterOptions.EndTime) - Query.ToEpoch(converterOptions.StartTime)
                    },
                    comments = comments
                };

                progress.Report(new ProgressReport { reportType = ReportType.Log, data = $"Crunching {chatRoot.video.end} seconds of data..." });

                // iterate messages from SQLite database...

                double lastPercent = 0.0;
                progress.Report(new ProgressReport { reportType = ReportType.Percent, data = lastPercent });

                double currentTimeOffset = 0.0;
                SQLiteDataReader messageReader = await Query.OpenMessageReader(connection, channelId, converterOptions.StartTime, converterOptions.EndTime, cancellationToken);

                while (await messageReader.ReadAsync(cancellationToken))
                {
                    try
                    {
                        int c = 0;
                        string messageId = messageReader.GetString(c++);

                        long timepoint = messageReader.GetInt64(c++);
                        double offsetTimepoint = messageReader.IsDBNull(c) ? 0 : messageReader.GetDouble(c); ++c;

                        long tmiSent = messageReader.GetInt64(c++);

                        long userId = messageReader.GetInt64(c++);
                        string nickname = messageReader.GetString(c++);
                        string displayName = messageReader.GetString(c++);
                        string color = messageReader.GetString(c++);

                        long mod = messageReader.GetInt64(c++);
                        long subscriber = messageReader.GetInt64(c++);
                        long turbo = messageReader.GetInt64(c++);

                        string badgeInfo = messageReader.GetString(c++);
                        string badges = messageReader.GetString(c++);

                        List<UserBadge> userBadges = new List<UserBadge>();
                        foreach (string badge in badges.Split(','))
                        {
                            string[] badgeParts = badge.Split('/');
                            userBadges.Add(new UserBadge { _id = badgeParts[0], version = badgeParts.Length > 1 ? badgeParts[1] : "" });
                        }

                        string emotes = messageReader.GetString(c++);
                        List<Emoticon2> userEmotes = GetEmoticons(emotes);

                        long roomId = messageReader.GetInt64(c++);
                        string target = messageReader.GetString(c++);
                        string text = messageReader.GetString(c++);

                        List<Fragment> messageFragments = GetMessageFragments(text, userEmotes);

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
                                emoticons = userEmotes,
                                fragments = messageFragments,
                                is_action = false,
                                user_badges = userBadges,
                                user_color = color == "" ? "#FFFFFF" : color
                            },
                            _id = messageId
                        };
                        comments.Add(comment);

                        // report for UI (not yet implemented)
                        double currentPercent = Math.Floor(currentTimeOffset / chatRoot.video.end * 100.0);
                        if (currentPercent > lastPercent)
                        {
                            progress.Report(new ProgressReport { reportType = ReportType.Percent, data = currentPercent });
                            lastPercent = currentPercent;
                        }

                        if (Query.FromEpoch(timepoint / 1000000L) > converterOptions.EndTime)
                        {
                            break;
                        }

                    } catch (Exception ex)
                    {

                        progress.Report(new ProgressReport { reportType = ReportType.Log, data = $"ERROR: {ex.Message}\n{ex.StackTrace}" });

                    }
                }

                messageReader.Close();

                progress.Report(new ProgressReport { reportType = ReportType.Log, data = $"Preparing emotes for {comments.Count} messages..." });

                chatRoot.emotes = new Emotes();
                List<FirstPartyEmoteData> firstParty = new List<FirstPartyEmoteData>();
                List<ThirdPartyEmoteData> thirdParty = new List<ThirdPartyEmoteData>();

                string cacheFolder = Path.Combine(Path.GetTempPath(), "TwitchDownloader", "cache");
                List<ThirdPartyEmote> thirdPartyEmotes = new List<ThirdPartyEmote>();
                List<KeyValuePair<string, SKBitmap>> firstPartyEmotes = new List<KeyValuePair<string, SKBitmap>>();

                await Task.Run(() => {
                    thirdPartyEmotes = TwitchHelper.GetThirdPartyEmotes(chatRoot.streamer.id, cacheFolder);
                    firstPartyEmotes = TwitchHelper.GetEmotes(comments, cacheFolder, null, true).ToList();
                });

                foreach (ThirdPartyEmote emote in thirdPartyEmotes)
                {
                    ThirdPartyEmoteData newEmote = new ThirdPartyEmoteData();
                    newEmote.id = emote.id;
                    newEmote.imageScale = emote.imageScale;
                    newEmote.data = emote.imageData;
                    newEmote.name = emote.name;
                    thirdParty.Add(newEmote);
                }

                foreach (KeyValuePair<string, SKBitmap> emote in firstPartyEmotes)
                {
                    FirstPartyEmoteData newEmote = new FirstPartyEmoteData();
                    newEmote.id = emote.Key;
                    newEmote.imageScale = 1;
                    newEmote.data = SKImage.FromBitmap(emote.Value).Encode(SKEncodedImageFormat.Png, 100).ToArray();
                    firstParty.Add(newEmote);
                }

                chatRoot.emotes.thirdParty = thirdParty;
                chatRoot.emotes.firstParty = firstParty;

                progress.Report(new ProgressReport { reportType = ReportType.Log, data = $"Writing JSON file to disk..." });

                // write JSON file...
                using (TextWriter writer = File.CreateText(converterOptions.OutputFile))
                {
                    var serializer = new JsonSerializer();
                    serializer.StringEscapeHandling = StringEscapeHandling.EscapeNonAscii;
                    serializer.Error += (object sender, Newtonsoft.Json.Serialization.ErrorEventArgs e) =>
                    {
                        progress.Report(new ProgressReport { reportType = ReportType.Log, data = $"ERROR: {e.ErrorContext.Path}\n{e.ErrorContext.Error.Message}\n{e.ErrorContext.Error.StackTrace}" });
                    };
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

        private static List<Emoticon2> GetEmoticons(string emotes)
        {
            List<Emoticon2> userEmotes = new List<Emoticon2>();
            foreach (string emote in emotes.Split('/'))
            {
                if (emote.Trim().Length > 0)
                {
                    string[] emoteParts = emote.Split(':', '-', ',');
                    string emoteIdPart = emoteParts[0];
                    for (int i = 1; i < emoteParts.Length; i+=2)
                    {
                        int emoteStart = int.Parse(emoteParts[i]);
                        int emoteEnd = int.Parse(emoteParts[i+1]);
                        userEmotes.Add(new Emoticon2 { _id = emoteIdPart, begin = emoteStart, end = emoteEnd });
                    }
                }
            }
            return userEmotes;
        }

        private static List<Fragment> GetMessageFragments(string text, List<Emoticon2> emoticons)
        {
            List<Fragment> fragments = new List<Fragment>();

            int lastFragmentStart = 0;
            emoticons.ForEach(emoticon =>
            {
                bool isBegin = lastFragmentStart == 0;
                if (!isBegin)
                {
                    int fragmentStart = lastFragmentStart;
                    int fragmentEnd = emoticon.begin;
                    int fragmentLength = Math.Max(fragmentEnd - fragmentStart, 0);
                    fragments.Add(new Fragment { text = text.Substring(fragmentStart, fragmentLength) });
                }

                fragments.Add(new Fragment { text = text.Substring(emoticon.begin, emoticon.end - emoticon.begin + 1), emoticon = new Emoticon { emoticon_id = emoticon._id, emoticon_set_id = "" } });

                lastFragmentStart = emoticon.end + 1;
            });

            string rest = text.Substring(lastFragmentStart);
            if (rest.Length > 0)
            {
                fragments.Add(new Fragment { text = rest });
            }

            return fragments;
        }
    }
}
