﻿using System;
using System.Threading.Tasks;
using NuGet.Common;

namespace Rocket.NuGet
{
    public class NuGetConsoleLogger : LoggerBase
    {
        public override void Log(ILogMessage message)
        {
            if (message.Level < LogLevel.Minimal)
                return;

            if (message.Message.Contains("Resolving dependency information took"))
                return;

            Console.WriteLine($"[{message.Level}] [NuGet] {message.Message}");
        }

        public override async Task LogAsync(ILogMessage message)
        {
            Log(message);
        }
    }
}