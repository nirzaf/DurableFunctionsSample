using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace DurableFunctionsSample
{
    public static class OrchestrationSample
    {
        [FunctionName("OrchestrationSample")]
        public static async Task<string> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
        {
            string endpoint = context.GetInput<object>().ToString();
            var request = new DurableHttpRequest(HttpMethod.Get, new Uri(endpoint));
            DurableHttpResponse endpointResponse = await context.CallHttpAsync(request);
            if (endpointResponse.StatusCode != HttpStatusCode.OK)
            {
                throw new ArgumentException($"Failed to contact endpoint: {endpointResponse.StatusCode}: {endpointResponse.Content}");
            }
            log.LogInformation("Information retrieved from endpoint = {EndpointResponseContent}", endpointResponse.Content);

            string[] words = endpointResponse.Content.Split(" ");
            log.LogInformation("Words c{name}ount = {Count}", "ARG0", words.Count());

            var entityId = new EntityId("OrchestrationSample_Counter", "charCounter");
             context.SignalEntity(entityId, "reset");

            foreach (string word in words)
            {
                 context.SignalEntity(entityId, "Add", word);
            }

            await context.CallActivityAsync("OrchestrationSample_LogBlob", endpoint.Replace("/", "bar"));
            int count = await context.CallEntityAsync<int>(entityId, "Get");

            return $"Endpoint: {endpoint} has the total of {count} chars";
        }

        [FunctionName("OrchestrationSample_LogBlob")]
        public static async Task LogBlobAsync([ActivityTrigger] string name, [Blob("sample-blob/{name}", FileAccess.Write)] CloudBlockBlob blobStream, ILogger log)
        {
            await blobStream.UploadTextAsync(DateTime.UtcNow.ToString());
            log.LogInformation("Blob Created {UtcNow}", DateTime.UtcNow);
        }

        [FunctionName("OrchestrationSample_Counter")]
        public static void Counter([EntityTrigger] IDurableEntityContext ctx, ILogger log)
        {
            log.LogInformation("Entity operation= {CtxOperationName}", ctx.OperationName);

            switch (ctx.OperationName.ToLowerInvariant())
            {
                case "add":
                    var sum = ctx.GetInput<string>().Length - ctx.GetInput<string>().Count(char.IsWhiteSpace);
                    ctx.SetState(ctx.GetState<int>() + sum);
                    break;
                case "reset":
                    ctx.SetState(0);
                    break;
                case "get":
                    ctx.Return(ctx.GetState<int>());
                    break;
            }
        }

        [FunctionName("OrchestrationSample_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter, ILogger log)
        {
            var qs = req.RequestUri.ParseQueryString();
            object endpoint = qs.Get("endpointUri");

            string instanceId = await starter.StartNewAsync("OrchestrationSample", endpoint);
            log.LogInformation("Started orchestration with ID = \'{InstanceId}\'", instanceId);
            log.LogInformation("Endpoint = \'{Endpoint}\'", endpoint);

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
    }
}