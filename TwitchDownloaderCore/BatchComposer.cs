﻿using System;
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

                string videoOutputPath = $"{outputDirectory}\\{outputFilename}_raw{outputExtension}";
                string chatJsonOutputPath = $"{outputDirectory}\\{outputFilename}_chat.json";
                string chatOutputPath = $"{outputDirectory}\\{outputFilename}_chat{outputExtension}";
                string chatMaskOutputPath = $"{outputDirectory}\\{outputFilename}_chat_mask{outputExtension}";
                
                intermediateFiles = new string[] { videoOutputPath, chatJsonOutputPath, chatOutputPath, chatMaskOutputPath };

                if (IsVideoId(batchComposerOptions.Id))
                {
                    VideoDownloader videoDownloader = new VideoDownloader(GetVideoDownloaderOptions(batchComposerOptions, videoOutputPath));
                    await videoDownloader.DownloadAsync(progress, cancellationToken);
                }
                else
                {
                    ClipDownloader clipDownloader = new ClipDownloader(GetClipDownloaderOptions(batchComposerOptions, videoOutputPath));
                    await clipDownloader.DownloadAsync();
                }

                ChatDownloader chatDownloader = new ChatDownloader(GetChatDownloaderOptions(batchComposerOptions, chatJsonOutputPath));
                await chatDownloader.DownloadAsync(progress, cancellationToken);

                ChatRenderer chatRenderer = new ChatRenderer(GetChatRendererOptions(batchComposerOptions, chatJsonOutputPath, chatOutputPath));
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

        private VideoDownloadOptions GetVideoDownloaderOptions(BatchComposerOptions batchCompositionOptions, string videoOutputPath)
        {
            VideoDownloadOptions downloadOptions = new VideoDownloadOptions
            {
                Id = int.Parse(batchCompositionOptions.Id),
                Filename = videoOutputPath,
                Quality = batchCompositionOptions.Quality,
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
