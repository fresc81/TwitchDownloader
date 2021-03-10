using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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

        public async Task ComposeAsync(IProgress<ProgressReport> progress, CancellationToken cancellationToken)
        {
            try
            {
                //VideoDownloader videoDownloader = new VideoDownloader(GetVideoDownloaderOptions(batchComposerOptions));
                //await videoDownloader.DownloadAsync(progress, cancellationToken);

                string finalVideoOutputPath = batchComposerOptions.Filename;

                string outputDirectory = Path.GetDirectoryName(finalVideoOutputPath);
                string outputFilename = Path.GetFileNameWithoutExtension(finalVideoOutputPath);

                string videoOutputPath = $"{outputDirectory}\\{outputFilename}_raw.mp4";
                string chatJsonOutputPath = $"{outputDirectory}\\{outputFilename}_chat.json";
                string chatOutputPath = $"{outputDirectory}\\{outputFilename}_chat.mp4";
                string chatMaskOutputPath = $"{outputDirectory}\\{outputFilename}_chat_mask.mp4";

                ClipDownloader clipDownloader = new ClipDownloader(GetClipDownloaderOptions(batchComposerOptions, videoOutputPath));
                await clipDownloader.DownloadAsync();

                ChatDownloader chatDownloader = new ChatDownloader(GetChatDownloaderOptions(batchComposerOptions, chatJsonOutputPath));
                await chatDownloader.DownloadAsync(progress, cancellationToken);

                ChatRenderer chatRenderer = new ChatRenderer(GetChatRendererOptions(batchComposerOptions, chatJsonOutputPath, chatOutputPath));
                await chatRenderer.RenderVideoAsync(progress, cancellationToken);

                await ComposeVideoWithChatOverlay(progress, videoOutputPath, chatOutputPath, chatMaskOutputPath, cancellationToken);

                Cleanup();

            } catch
            {
                Cleanup();
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
                batchComposerOptions.ChatPosTop
            );

            string composerArgs = batchComposerOptions.ComposerArgs
                .Replace("{video}", videoOutputPath)
                .Replace("{chat}", chatOutputPath)
                .Replace("{chat_mask}", chatMaskOutputPath)
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

            Console.WriteLine(composerArgs);

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

        private ChatRenderOptions GetChatRendererOptions(BatchComposerOptions batchCompositionOptions, string chatJsonOutputPath, string chatOutputPath)
        {
            ChatRenderOptions renderOptions = new ChatRenderOptions
            {
                InputFile = chatJsonOutputPath,
                OutputFile = chatOutputPath,
                BackgroundColor = batchCompositionOptions.BackgroundColor,
                MessageColor = batchCompositionOptions.MessageColor,
                ChatHeight = batchCompositionOptions.ChatHeight,
                ChatWidth = batchCompositionOptions.ChatWidth,
                BttvEmotes = batchCompositionOptions.BttvEmotes,
                FfzEmotes = batchCompositionOptions.FfzEmotes,
                Outline = batchCompositionOptions.Outline,
                OutlineSize = batchCompositionOptions.OutlineSize,
                Font = batchCompositionOptions.Font,
                FontSize = batchCompositionOptions.FontSize,

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

        private ChatDownloadOptions GetChatDownloaderOptions(BatchComposerOptions batchCompositionOptions, string chatJsonOutputPath)
        {
            ChatDownloadOptions downloadOptions = new ChatDownloadOptions
            {
                IsJson = true,
                Id = batchCompositionOptions.Id.ToString(),
                CropBeginning = batchCompositionOptions.CropBeginning,
                CropBeginningTime = batchCompositionOptions.CropBeginningTime,
                CropEnding = batchCompositionOptions.CropEnding,
                CropEndingTime = batchCompositionOptions.CropEndingTime,
                Timestamp = batchCompositionOptions.Timestamp,

                EmbedEmotes = true, // TODO nessesary?

                Filename = chatJsonOutputPath
            };

            return downloadOptions;
        }

        private ClipDownloadOptions GetClipDownloaderOptions(BatchComposerOptions batchCompositionOptions, string videoOutputPath)
        {
            ClipDownloadOptions downloadOptions = new ClipDownloadOptions
            {
                Id = batchCompositionOptions.Id.ToString(),
                Filename = videoOutputPath,
                Quality = batchCompositionOptions.Quality
            };

            return downloadOptions;
        }

        private VideoDownloadOptions GetVideoDownloaderOptions(BatchComposerOptions batchCompositionOptions)
        {
            throw new NotImplementedException();
        }

        private void Cleanup()
        {
            
        }
    }
}
