﻿using System;
using System.Runtime.InteropServices;

namespace Smartstore.Engine
{
    public class RuntimeInfo
    {
        public RuntimeInfo()
        {
            // Use the current host and the process id as two servers could run on the same machine
            EnvironmentIdentifier = Environment.MachineName + '-' + Environment.ProcessId;

            var processArchitecture = RuntimeInformation.ProcessArchitecture.ToString().ToLower();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                RID = "win-" + processArchitecture;
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                RID = "linux-" + processArchitecture;
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                RID = "osx-" + processArchitecture;
            else
                throw new InvalidOperationException($"Unsupported OS Platform {RuntimeInformation.OSDescription}.");
        }

        /// <summary>
        /// Gets the current machines's name.
        /// </summary>
        public string MachineName { get; } = Environment.MachineName;

        /// <summary>
        /// Gets a unique environment identifier.
        /// </summary>
        public string EnvironmentIdentifier { get; }

        /// <summary>
        /// Gets the full path to the entry assembly directory.
        /// </summary>
        public string BaseDirectory { get; } = AppContext.BaseDirectory;

        /// <summary>
        /// Gets the description of the operating system.
        /// </summary>
        public string OSDescription { get; } = RuntimeInformation.OSDescription;

        /// <summary>
        /// Gets the process architecture.
        /// </summary>
        public Architecture ProcessArchitecture { get; } = RuntimeInformation.ProcessArchitecture;

        /// <summary>
        /// Gets the version agnostic runtime identifier (RID), e.g. win-x64, linux-x64, osx-x64 etc.
        /// </summary>
        public string RID { get; }
    }
}
