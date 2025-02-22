﻿namespace Hast.Vitis.Abstractions.Models
{
    public class AzurePollPostData : AzurePostData
    {
        public string OrchestrationId { get; set; }

        public AzurePollPostData(AzureAttestationConfiguration configuration, string orchestrationId)
            : base(configuration) =>
            OrchestrationId = orchestrationId;
    }
}
