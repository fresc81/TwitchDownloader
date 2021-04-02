using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TwitchDownloaderCore.Options;
using static TwitchDownloaderCore.FfmpegHelper;

namespace TwitchDownloaderCore
{

    public class BatchComposer
    {

        readonly BatchComposerOptions batchComposerOptions;

        public BatchComposer(BatchComposerOptions BatchCompositionOptions)
        {
            batchComposerOptions = BatchCompositionOptions;
        }

        private static bool IsVideoId(string id)
        {
            return int.TryParse(id, out _);
        }

        public async Task ComposeAsync(IProgress<ProgressReport> progress, CancellationToken cancellationToken)
        {
            string[] intermediateFiles = null;
            try
            {
                string finalVideoOutputPath = batchComposerOptions.Filename;

                string outputDirectory = Path.GetDirectoryName(finalVideoOutputPath);
                string outputFilename = Path.GetFileNameWithoutExtension(finalVideoOutputPath);
                string outputExtension = Path.GetExtension(finalVideoOutputPath);

                string videoOutputPath = $"{outputDirectory}{Path.DirectorySeparatorChar}{outputFilename}_raw{outputExtension}";
                string chatJsonOutputPath = $"{outputDirectory}{Path.DirectorySeparatorChar}{outputFilename}_chat.json";
                string chatOutputPath = $"{outputDirectory}{Path.DirectorySeparatorChar}{outputFilename}_chat{outputExtension}";
                string chatMaskOutputPath = $"{outputDirectory}{Path.DirectorySeparatorChar}{outputFilename}_chat_mask{outputExtension}";

                intermediateFiles = new string[] { videoOutputPath, chatJsonOutputPath, chatOutputPath, chatMaskOutputPath };

                if (IsVideoId(batchComposerOptions.Id))
                {
                    VideoDownloader videoDownloader = new VideoDownloader(GetVideoDownloaderOptions(videoOutputPath));
                    await videoDownloader.DownloadAsync(progress, cancellationToken);
                }
                else
                {
                    ClipDownloader clipDownloader = new ClipDownloader(GetClipDownloaderOptions(videoOutputPath));
                    await clipDownloader.DownloadAsync();
                }

                ChatDownloader chatDownloader = new ChatDownloader(GetChatDownloaderOptions(chatJsonOutputPath));
                await chatDownloader.DownloadAsync(progress, cancellationToken);

                ChatRenderer chatRenderer = new ChatRenderer(GetChatRendererOptions(chatJsonOutputPath, chatOutputPath));
                await chatRenderer.RenderVideoAsync(progress, cancellationToken);

                await ComposeVideoWithChatOverlay(progress, videoOutputPath, chatOutputPath, chatMaskOutputPath, cancellationToken);

                Cleanup(intermediateFiles);

            } catch
            {
                Cleanup(intermediateFiles);
                throw;
            }
        }

        private async Task ComposeVideoWithChatOverlay(IProgress<ProgressReport> progress, string videoOutputPath, string chatOutputPath, string chatMaskOutputPath, CancellationToken cancellationToken)
        {

            ImageDimension videoDimension = await GetVideoDimensionAsync(videoOutputPath, batchComposerOptions.FfmpegPath);

            MappedInputs mappedInputs = BuildMappedInputs(videoOutputPath, chatOutputPath, chatMaskOutputPath, batchComposerOptions.ChatBackgroundImage, batchComposerOptions.ChatBorderImage);

            string filtergraph = BuildFiltergraph(
                batchComposerOptions.ChatBackgroundImage != null,
                batchComposerOptions.ChatBorderImage != null,
                videoDimension.Width,
                videoDimension.Height,
                batchComposerOptions.ChatPosLeft,
                batchComposerOptions.ChatPosTop,
                batchComposerOptions.Framerate
            );

            string composerArgs = batchComposerOptions.ComposerArgs
                .Replace("{video}", videoOutputPath)
                .Replace("{chat}", chatOutputPath)
                .Replace("{chat_mask}", chatMaskOutputPath)
                .Replace("{fps}", batchComposerOptions.Framerate.ToString())
                .Replace("{border}", batchComposerOptions.ChatBorderImage)
                .Replace("{background}", batchComposerOptions.ChatBackgroundImage)
                .Replace("{input_files}", mappedInputs.Inputs)
                .Replace("{input_mappings}", mappedInputs.Mappings)
                .Replace("{filtergraph}", filtergraph)
                .Replace("{save_path}", batchComposerOptions.Filename)
                .Replace("{chat_top}", batchComposerOptions.ChatPosTop.ToString())
                .Replace("{chat_left}", batchComposerOptions.ChatPosLeft.ToString())
                .Replace("{video_width}", videoDimension.Width.ToString())
                .Replace("{video_height}", videoDimension.Height.ToString());

            var process = new Process
            {
                StartInfo =
                {
                    FileName = batchComposerOptions.FfmpegPath,
                    Arguments = $"{composerArgs}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };

            try
            {

                process.Start();
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();

                while (!process.StandardError.EndOfStream)
                {
                    Console.WriteLine(process.StandardError.ReadLine());
                }

                process.WaitForExit();

                stopwatch.Stop();
                progress.Report(new ProgressReport() { reportType = ReportType.Log, data = $"FINISHED. RENDER TIME: {(int)stopwatch.Elapsed.TotalSeconds}s" });

            } catch
            {
                process.Kill();
                throw;

            } finally
            {
                process.Close();
            }
        }

        private ChatRenderOptions GetChatRendererOptions(string chatJsonOutputPath, string chatOutputPath)
        {
            ChatRenderOptions renderOptions = new ChatRenderOptions
            {
                InputFile = chatJsonOutputPath,
                OutputFile = chatOutputPath,
                BackgroundColor = batchComposerOptions.BackgroundColor,
                MessageColor = batchComposerOptions.MessageColor,
                ChatHeight = batchComposerOptions.ChatHeight,
                ChatWidth = batchComposerOptions.ChatWidth,
                BttvEmotes = batchComposerOptions.BttvEmotes,
                FfzEmotes = batchComposerOptions.FfzEmotes,
                Outline = batchComposerOptions.Outline,
                OutlineSize = batchComposerOptions.OutlineSize,
                Font = batchComposerOptions.Font,
                FontSize = batchComposerOptions.FontSize,

                MessageFontStyle = batchComposerOptions.MessageFontStyle,
                UsernameFontStyle = batchComposerOptions.UsernameFontStyle,
                UpdateRate = batchComposerOptions.UpdateRate,
                PaddingLeft = batchComposerOptions.PaddingLeft,
                Framerate = batchComposerOptions.Framerate,
                GenerateMask = batchComposerOptions.GenerateMask,
                InputArgs = batchComposerOptions.InputArgs,
                OutputArgs = batchComposerOptions.OutputArgs,
                FfmpegPath = batchComposerOptions.FfmpegPath,
                TempFolder = batchComposerOptions.TempFolder,
                SubMessages = batchComposerOptions.SubMessages
            };

            return renderOptions;
        }

        private ChatDownloadOptions GetChatDownloaderOptions(string chatJsonOutputPath)
        {
            ChatDownloadOptions downloadOptions = new ChatDownloadOptions
            {
                IsJson = true,
                Id = batchComposerOptions.Id.ToString(),
                CropBeginning = batchComposerOptions.CropBeginning,
                CropBeginningTime = batchComposerOptions.CropBeginningTime,
                CropEnding = batchComposerOptions.CropEnding,
                CropEndingTime = batchComposerOptions.CropEndingTime,
                Timestamp = batchComposerOptions.Timestamp,

                EmbedEmotes = true, // TODO nessesary?

                Filename = chatJsonOutputPath
            };

            return downloadOptions;
        }

        private ClipDownloadOptions GetClipDownloaderOptions(string videoOutputPath)
        {
            ClipDownloadOptions downloadOptions = new ClipDownloadOptions
            {
                Id = batchComposerOptions.Id.ToString(),
                Filename = videoOutputPath,
                Quality = batchComposerOptions.Quality
            };

            return downloadOptions;
        }

        private VideoDownloadOptions GetVideoDownloaderOptions(string videoOutputPath)
        {
            VideoDownloadOptions downloadOptions = new VideoDownloadOptions
            {
                Id = int.Parse(batchComposerOptions.Id),
                Filename = videoOutputPath,
                Quality = batchComposerOptions.Quality,
                CropBeginning = batchComposerOptions.CropBeginning,
                CropBeginningTime = batchComposerOptions.CropBeginningTime,
                CropEnding = batchComposerOptions.CropEnding,
                CropEndingTime = batchComposerOptions.CropEndingTime,
                DownloadThreads = batchComposerOptions.DownloadThreads,
                FfmpegPath = batchComposerOptions.FfmpegPath,
                Oauth = batchComposerOptions.Oauth,
                PlaylistUrl = batchComposerOptions.PlaylistUrl,
                TempFolder = batchComposerOptions.TempFolder
            };

            return downloadOptions;
        }

        private void Cleanup(string[] intermediateFiles)
        {
            if (!batchComposerOptions.KeepIntermediate)
            {

                if (intermediateFiles != null)
                {
                    foreach (var intermediateFile in intermediateFiles)
                    {
                        File.Delete(intermediateFile);
                    }
                }

            }
        }
    }
}
