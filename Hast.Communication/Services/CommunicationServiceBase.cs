using Hast.Communication.Models;
using Hast.Layer;
using Hast.Transformer.Abstractions.SimpleMemory;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Hast.Communication.Services
{
    public abstract class CommunicationServiceBase : ICommunicationService
    {
        protected  ILogger Logger { get; }

        abstract public string ChannelName { get; }
        public TextWriter TesterOutput { get; set; }


        protected CommunicationServiceBase(ILogger logger) => Logger = logger;


        abstract public Task<IHardwareExecutionInformation> Execute(
            SimpleMemory simpleMemory,
            int memberId,
            IHardwareExecutionContext executionContext);


        protected CommunicationStateContext BeginExecution()
        {
            return new CommunicationStateContext
            {
                Stopwatch = Stopwatch.StartNew(),
                HardwareExecutionInformation = new HardwareExecutionInformation()
            };
        }

        protected void EndExecution(CommunicationStateContext context)
        {
            context.Stopwatch.Stop();

            context.HardwareExecutionInformation.FullExecutionTimeMilliseconds = context.Stopwatch.ElapsedMilliseconds;

            Logger.LogInformation("Full execution time: {0}ms", context.Stopwatch.ElapsedMilliseconds);
        }

        protected void SetHardwareExecutionTime(
            CommunicationStateContext context,
            IHardwareExecutionContext executionContext,
            ulong executionTimeClockCycles,
            uint? clockFrequency = null)
        {
            if (clockFrequency == null)
            {
                clockFrequency = executionContext.HardwareRepresentation.DeviceManifest.ClockFrequencyHz;
            }

            var milliseconds = 1M / clockFrequency.Value * 1000 * executionTimeClockCycles;
            context.HardwareExecutionInformation.HardwareExecutionTimeMilliseconds = milliseconds;
            Logger.LogInformation($"Hardware execution took {milliseconds:0.0000}ms.");
        }


        protected class CommunicationStateContext
        {
            public Stopwatch Stopwatch { get; set; }

            public HardwareExecutionInformation HardwareExecutionInformation { get; set; }
        }
    }
}
