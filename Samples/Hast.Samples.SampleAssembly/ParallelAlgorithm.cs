﻿using Hast.Transformer.Abstractions.SimpleMemory;
using System.Threading.Tasks;

namespace Hast.Samples.SampleAssembly
{
    /// <summary>
    /// A massively parallel algorithm that is well suited to be accelerated with Hastlayer. Also see 
    /// <see cref="ParallelAlgorithmSampleRunner"/> on what to configure to make this work.
    /// </summary>
    public class ParallelAlgorithm
    {
        private const int MaxDegreeOfParallelism = 270;

        private const int Run_InputInt32Index = 0;
        private const int Run_OutputInt32Index = 0;


        public virtual void Run(SimpleMemory memory)
        {
            var input = memory.ReadInt32(Run_InputInt32Index);
            var tasks = new Task<int>[MaxDegreeOfParallelism];

            // Hastlayer will figure out how many Tasks you want to start if you kick them off in a loop like this.
            // If this is more involved then you'll need to tell Hastlayer the level of parallelism, see the comment in
            // ParallelAlgorithmSampleRunner.
            for (int i = 0; i < MaxDegreeOfParallelism; i++)
            {
                tasks[i] = Task.Factory.StartNew(
                    indexObject =>
                    {
                        var index = (int)indexObject;
                        int result = input + index * 2;

                        var even = true;
                        for (int j = 2; j < 9999999; j++)
                        {
                            if (even)
                            {
                                result += index;
                            }
                            else
                            {
                                result -= index;
                            }

                            even = !even;
                        }

                        return result;
                    },
                    i);
            }

            // Task.WhenAny() can be used too.
            Task.WhenAll(tasks).Wait();

            int output = 0;
            for (int i = 0; i < MaxDegreeOfParallelism; i++)
            {
                output += tasks[i].Result;
            }

            memory.WriteInt32(Run_OutputInt32Index, output);
        }

        public int Run(int input)
        {
            var memory = new SimpleMemory(1);
            memory.WriteInt32(Run_InputInt32Index, input);
            Run(memory);
            return memory.ReadInt32(Run_OutputInt32Index);
        }
    }
}
