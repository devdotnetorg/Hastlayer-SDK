﻿namespace Hast.Vitis.Abstractions.Models
{
    public class VitisBuildConfiguration
    {
        /// <summary>
        /// Only run Vivado synthesis to generate a report, don't build via v++. Exits after synthesis is complete.
        /// </summary>
        public bool SynthesisOnly { get; set; }

        /// <summary>
        /// If true, the device will be reset via `xbutil reset` before the first build.
        /// </summary>
        public bool ResetOnFirstRun { get; set; }
    }
}
