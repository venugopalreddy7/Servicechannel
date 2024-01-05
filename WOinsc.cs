using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace WOinsc
{
	public static class Function
	{
		private static string serviceUsername;

		[FunctionName("WOinsc")]
		public static async Task<IActionResult> Run(
			[HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
			ILogger log)
		{
			// Initialize telemetry client
			TelemetryConfiguration telemetryConfig = TelemetryConfiguration.CreateDefault();
			telemetryConfig.InstrumentationKey = Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY");
			TelemetryClient telemetryClient = new TelemetryClient(telemetryConfig);

			log.LogInformation("C# HTTP trigger function processed a request.");

			// Obtain Access Token
			var accessToken = await GetAccessTokenAsync(log, telemetryClient);

			if (accessToken == null)
			{
				log.LogError("Failed to obtain access token.");
				telemetryClient.TrackTrace("Failed to obtain access token.");
				return new BadRequestObjectResult("Failed to obtain access token.");
			}

			// Create Work Order
			var workOrderResult = await CreateWorkOrderAsync(accessToken, log, telemetryClient);

			if (workOrderResult != null)
			{
				telemetryClient.TrackTrace("Work order created successfully.");
				return new OkObjectResult(workOrderResult);
			}
			else
			{
				log.LogError("Failed to create work order.");
				telemetryClient.TrackTrace("Failed to create work order.");
				return new BadRequestObjectResult("Failed to create work order.");
			}
		}

		static async Task<string> GetAccessTokenAsync(ILogger log, TelemetryClient telemetryClient)
		{
			var config = new ConfigurationBuilder()
				.SetBasePath(Directory.GetCurrentDirectory())
				.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
				.AddEnvironmentVariables()
				.Build();

			using (HttpClient httpClient = new HttpClient())
			{
				var uri = config["TokenUri"]; // Retrieve from local settings file or environment variables
				serviceUsername = config["serviceUsername"]; // Assign to serviceUsername field
				var password = config["Password"]; // Retrieve from local settings file or environment variables

				var creds = new Dictionary<string, string>
				{
					{ "username", serviceUsername },
					{ "password", password },
					{ "grant_type", "password" }
				};

				var basicToken = config["BasicToken"]; // Retrieve from local settings file or environment variables

				var accessTokenReq = new HttpRequestMessage(HttpMethod.Post, $"{uri}/oauth/token") { Content = new FormUrlEncodedContent(creds) };
				var plainTxt = Encoding.UTF8.GetBytes(basicToken);
				accessTokenReq.Headers.Add("Authorization", $"Basic {Convert.ToBase64String(plainTxt)}");
				httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-www-form-urlencoded"));

				HttpResponseMessage response = await httpClient.SendAsync(accessTokenReq);

				if (response.IsSuccessStatusCode)
				{
					string responseData = await response.Content.ReadAsStringAsync();
					var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(responseData);
					return tokenResponse?.AccessToken;
				}
				else
				{
					log.LogError($"Failed to obtain access token. Status code: {response.StatusCode}");
					telemetryClient.TrackTrace($"Failed to obtain access token. Status code: {response.StatusCode}");
					return null;
				}
			}
		}

		static async Task<string> CreateWorkOrderAsync(string accessToken, ILogger log, TelemetryClient telemetryClient)
		{
			var config = new ConfigurationBuilder()
				.SetBasePath(Directory.GetCurrentDirectory())
				.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
				.AddEnvironmentVariables()
				.Build();

			using (HttpClient httpClient = new HttpClient())
			{
				var workOrdersUri = config["WorkOrdersUri"]; // Retrieve from local settings file or environment variables
				var workOrdersRequest = new HttpRequestMessage(HttpMethod.Post, workOrdersUri)
				{
					Content = new StringContent(GetSampleData(), Encoding.UTF8, "application/json")
				};

				workOrdersRequest.Headers.Add("Authorization", $"Bearer {accessToken}");

				HttpResponseMessage workOrdersResponse = await httpClient.SendAsync(workOrdersRequest);

				if (workOrdersResponse.IsSuccessStatusCode)
				{
					string workOrdersData = await workOrdersResponse.Content.ReadAsStringAsync();
					return workOrdersData;
				}
				else
				{
					log.LogError($"Failed to create work order. Status code: {workOrdersResponse.StatusCode}");
					telemetryClient.TrackTrace($"Failed to create work order. Status code: {workOrdersResponse.StatusCode}");
					return null;
				}
			}
		}

		static string GetSampleData()
		{
			// Construct and return the sample data in JSON format
			return @"{
                ""ContractInfo"": {
                    ""LocationId"": 2006071467,
                    ""TradeName"": ""ELEVATORS""
                },
                ""Category"": ""REPAIR"",
                ""Priority"": ""P4 - 72 HOURS"",
                ""Nte"": 1000,
                ""CallDate"": ""2023-08-04T19:35:00Z"",
                ""Description"": ""ELEVATOR/ESCALATOR / Elevator / Freight Elevator / Freight Elevator Inspection"",
                ""ProblemCode"": ""Freight Elevator Inspection"",
                ""IssueRequestInfo"": {
                    ""AreaId"": 4935,
                    ""ExtendedAreaName"": ""ELEVATOR/ESCALATOR"",
                    ""ProblemType"": ""Elevator"",
                    ""AssetType"": ""Freight Elevator""
                },
                ""Attachments"": [
                    {
                        ""Name"": ""test attachment"",
                        ""Description"": ""for test only"",
                        ""Path"": ""201706/d8f0792e-81ef-4fcb-95df-1ae0c1be9cd9-test.txt""
                    }
                ]
            }";
		}

		public class TokenResponse
		{
			[JsonProperty("access_token")]
			public string AccessToken { get; set; }
		}
	}
}