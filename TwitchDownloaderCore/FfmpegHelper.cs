using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchDownloaderCore
{
    public static class FfmpegHelper
    {
        public struct ImageDimension
        {
            public int Width { get; set; }

            public int Height { get; set; }

        }

        public struct MappedInputs
        {
            public string Inputs { get; set; }

            public string Mappings { get; set; }

        }

        private struct RenderPipeline
        {
            public List<string> Inputs { get; set; }

            public List<string> Filters { get; set; }

            public List<string> Outputs { get; set; }

            public override string ToString()
            {
                var inputs = Inputs.Select(input => $"[{input}]").Aggregate("", string.Concat);
                var filters = Filters.Aggregate("", MakeStringJoin(", "));
                var outputs = Outputs.Select(output => $"[{output}]").Aggregate("", string.Concat);
                return $"{inputs} {filters} {outputs}";
            }

        }

        private struct Filtergraph
        {
            public List<RenderPipeline> Pipelines { get; set; }

            public override string ToString()
            {
                var pipelines = Pipelines.Select(pipeline => pipeline.ToString()).Aggregate("", MakeStringJoin("; "));
                return pipelines;
            }
        }

        private static Func<string, string, string> MakeStringJoin(string seperator)
        {
            return (string curr, string next) => curr.Length == 0 ? next : $"{curr}{seperator}{next}";
        }

        public static MappedInputs BuildMappedInputs(string videoInputPath, string chatInputPath, string chatMaskInputPath, string backgroundInputPath, string borderInputPath)
        {
            StringBuilder sbInputs = new StringBuilder();
            StringBuilder sbMappings = new StringBuilder();

            int streamId = 0;
            sbInputs.Append($"-i \"{videoInputPath}\" ");
            sbMappings.Append($"[{streamId++}:v] null [video_input]; ");

            sbInputs.Append($"-i \"{chatInputPath}\" ");
            sbMappings.Append($"[{streamId++}:v] null [chat_color_input]; ");

            sbInputs.Append($"-i \"{chatMaskInputPath}\" ");
            sbMappings.Append($"[{streamId++}:v] null [chat_mask_input]; ");

            if (backgroundInputPath != null)
            {
                sbInputs.Append($"-i \"{backgroundInputPath}\" ");
                sbMappings.Append($"[{streamId++}:v] null [chat_background]; ");
            }

            if (borderInputPath != null)
            {
                sbInputs.Append($"-i \"{borderInputPath}\" ");
                sbMappings.Append($"[{streamId}:v] null [chat_border]; ");
            }

            return new MappedInputs { Inputs = sbInputs.ToString(), Mappings = sbMappings.ToString() };
        }

        public static string BuildFiltergraph(bool hasBackground, bool hasBorder, int width, int height, int chatX, int chatY)
        {
            string padding = $"{width}:{height}:{chatX}:{chatY}:0x00000000";
            
            var filtergraph = new Filtergraph
            {
                Pipelines = new List<RenderPipeline>
                {

                    new RenderPipeline
                    {
                        Inputs = new List<string>
                        {
                            "chat_color_input",
                            "chat_mask_input"
                        },
                        Filters = new List<string>
                        {
                            "alphamerge",
                            $"pad={padding}"
                        },
                        Outputs = new List<string>
                        {
                            "chat"
                        }
                    }

                }
            };

            if (hasBackground)
            {
                filtergraph.Pipelines.Add(new RenderPipeline
                {
                    Inputs = new List<string>
                    {
                        "chat_background"
                    },
                    Filters = new List<string>
                    {
                        $"pad={padding}"
                    },
                    Outputs = new List<string>
                    {
                        "background"
                    }
                });
                filtergraph.Pipelines.Add(new RenderPipeline
                {
                    Inputs = new List<string>
                    {
                        "video_input",
                        "background"
                    },
                    Filters = new List<string>
                    {
                        "overlay"
                    },
                    Outputs = new List<string>
                    {
                        "tmp1"
                    }
                });
            } else
            {
                filtergraph.Pipelines.Add(new RenderPipeline
                {
                    Inputs = new List<string>
                    {
                        "video_input"
                    },
                    Filters = new List<string>
                    {
                        "null"
                    },
                    Outputs = new List<string>
                    {
                        "tmp1"
                    }
                });
            }

            filtergraph.Pipelines.Add(new RenderPipeline
            {
                Inputs = new List<string>
                {
                    "tmp1",
                    "chat"
                },
                Filters = new List<string>
                {
                    "overlay"
                },
                Outputs = new List<string>
                {
                    "tmp2"
                }
            });

            if (hasBorder)
            {
                filtergraph.Pipelines.Add(new RenderPipeline
                {
                    Inputs = new List<string>
                    {
                        "chat_border"
                    },
                    Filters = new List<string>
                    {
                        $"pad={padding}"
                    },
                    Outputs = new List<string>
                    {
                        "border"
                    }
                });
                filtergraph.Pipelines.Add(new RenderPipeline
                {
                    Inputs = new List<string>
                    {
                        "tmp2",
                        "border"
                    },
                    Filters = new List<string>
                    {
                        "overlay"
                    },
                    Outputs = new List<string>
                    {
                        "output"
                    }
                });
            }
            else
            {
                filtergraph.Pipelines.Add(new RenderPipeline
                {
                    Inputs = new List<string>
                    {
                        "tmp2"
                    },
                    Filters = new List<string>
                    {
                        "null"
                    },
                    Outputs = new List<string>
                    {
                        "output"
                    }
                });
            }

            return filtergraph.ToString();
        }

        public static Task<ImageDimension> GetVideoDimensionAsync(string videoOutputFile, string ffmpegPath)
        {
            return Task.Run(() => {
                ImageDimension imageDimension = new ImageDimension { Width = 1920, Height = 1080 };

                using (var process = new Process
                {
                    StartInfo =
                    {
                        FileName = ffmpegPath,
                        Arguments = $"-v info -i \"{videoOutputFile}\" -f ffmetadata -",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardInput = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    }
                })
                {
                    try
                    {

                        process.Start();
                        process.BeginOutputReadLine();

                        Regex regex = new Regex("^\\s*Stream #0:.*Video:.*, ([0-9]+)x([0-9]+)( \\[.*\\])?, .*$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.Multiline);

                        while (!process.StandardError.EndOfStream)
                        {
                            string line = process.StandardError.ReadLine();
                            Match match = regex.Match(line);

                            if (match.Success)
                            {
                                imageDimension.Width = int.Parse(match.Groups[1].Value);
                                imageDimension.Height = int.Parse(match.Groups[2].Value);
                            }

                        }

                        process.WaitForExit();

                    }
                    catch
                    {
                        process.Kill();
                        throw;
                    }
                    finally
                    {
                        process.Close();
                    }
                }

                return imageDimension;
            });
        }
    }
}
