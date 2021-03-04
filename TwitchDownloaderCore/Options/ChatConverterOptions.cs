using System;
using System.Collections.Generic;
using System.Text;

namespace TwitchDownloaderCore.Options
{
    public class ChatConverterOptions
    {
        public string InputFile { get; set; }

        public string OutputFile { get; set; }

        public string ChannelId { get; set; }

        public DateTime StartTime { get; set; }

        public DateTime EndTime { get; set; }

    }
}
