﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hast.Layer;
using Hast.Samples.SampleAssembly;

namespace Hast.Samples.Consumer.SampleRunners
{
    internal static class LoopbackSampleRunner
    {
        public static void Configure(HardwareGenerationConfiguration configuration)
        {
            configuration.AddHardwareEntryPointType<Loopback>();
        }

        public static async Task Run(IHastlayer hastlayer, IHardwareRepresentation hardwareRepresentation, IProxyGenerationConfiguration configuration)
        {
            var loopback = await hastlayer.GenerateProxy(hardwareRepresentation, new Loopback(), configuration ?? ProxyGenerationConfiguration.Default);

            var memoryConfig = (hastlayer as Hastlayer).CreateMemoryConfiguration(hardwareRepresentation);
            var output1 = loopback.Run(123, memoryConfig);
            var output2 = loopback.Run(1234, memoryConfig);
            var output3 = loopback.Run(-9, memoryConfig);
            var output4 = loopback.Run(0, memoryConfig);
            var output5 = loopback.Run(-19, memoryConfig);
            var output6 = loopback.Run(1, memoryConfig);
        }
    }
}
