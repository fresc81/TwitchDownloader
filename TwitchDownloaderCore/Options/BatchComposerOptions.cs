using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TwitchDownloaderCore.Options
{
    public class BatchComposerOptions : ChatRenderOptions
    {
        /* download options... */
        public string Id { get; set; }
        public string PlaylistUrl { get; set; }
        public string Quality { get; set; }
        public string Filename { get; set; }
        public bool CropBeginning { get; set; }
        public double CropBeginningTime { get; set; }
        public bool CropEnding { get; set; }
        public double CropEndingTime { get; set; }
        public int DownloadThreads { get; set; }
        public string Oauth { get; set; }

        /* composition options */
        public int ChatPosTop { get; set; }
        public int ChatPosLeft { get; set; }

        public string ChatBorderImage { get; set; }

        public string ChatBackgroundImage { get; set; }

        public string ComposerArgs { get; set; }


    }
}
