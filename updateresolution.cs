using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace updateresolution
{
	public static class ServiceBusTopicFunction
	{
		private static string TokenUri1;
		private static string AccessTokenUri;
		private static string serviceClientId;
		private static string serviceClientSecret;
		private static string serviceUsername;
		private static string Password;

		[FunctionName("updateresolution")]
		public static async Task Run(
			[ServiceBusTrigger("servicechanneltopicdemo", "servicechanneltopicsubscritiondemo", Connection = "ServiceBusConnection")] Message message,
			ILogger log)
		{
			var telemetryConfiguration = TelemetryConfiguration.CreateDefault();
			telemetryConfiguration.InstrumentationKey = Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY");

			var telemetryClient = new TelemetryClient(telemetryConfiguration);

			log.LogInformation($"Received message: SequenceNumber:{message.SystemProperties.SequenceNumber} Body:{Encoding.UTF8.GetString(message.Body)}");

			// Deserialize the message content
			var messageContent = Encoding.UTF8.GetString(message.Body);
			var workOrderData = JsonConvert.DeserializeObject<List<WorkOrderData>>(messageContent);

			// Assuming workOrderData is a list or an enumerable collection
			foreach (var item in workOrderData)
			{
				string workOrderNumber = item.WorkOrderNumber;
				string resolution = item.Resolution;

				// Obtain access token from Service Channel
				string accessToken = await GetAccessToken(log);

				// Update resolution in Service Channel
				bool resolutionUpdated = await UpdateResolutionInServiceChannel(accessToken, workOrderNumber, resolution, log);

				if (resolutionUpdated)
				{
					log.LogInformation($"Resolution updated successfully for Work Order Number: {workOrderNumber}");
					telemetryClient.TrackTrace($"Resolution updated successfully for Work Order Number: {workOrderNumber}");
				}
				else
				{
					log.LogError($"Failed to update resolution for Work Order Number: {workOrderNumber}");
					telemetryClient.TrackTrace($"Failed to update resolution for Work Order Number: {workOrderNumber}");
				}
			}
		}

		private static async Task<string> GetAccessToken(ILogger log)
		{
			try
			{
				var config = new ConfigurationBuilder()
					.SetBasePath(Environment.CurrentDirectory)
					.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
					.AddEnvironmentVariables()
					.Build();

				TokenUri1 = config["TokenUri1"];
				serviceClientId = config["serviceClientId"];
				serviceClientSecret = config["serviceClientSecret"];
				serviceUsername = config["serviceUsername"];
				Password = config["Password"];
				AccessTokenUri = config["AccessTokenUri"];

				using (HttpClient httpClient = new HttpClient())
				{
					var credentials = new Dictionary<string, string>
					{
						{ "username", serviceUsername },
						{ "password", Password },
						{ "grant_type", "password" }
					};

					var accessTokenRequest = new HttpRequestMessage(HttpMethod.Post, AccessTokenUri)
					{
						Content = new FormUrlEncodedContent(credentials)
					};

					var plainTextCredentials = $"{serviceClientId}:{serviceClientSecret}";
					var encodedCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(plainTextCredentials));

					accessTokenRequest.Headers.Add("Authorization", $"Basic {encodedCredentials}");
					accessTokenRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded");

					HttpResponseMessage response = await httpClient.SendAsync(accessTokenRequest);

					if (response.IsSuccessStatusCode)
					{
						string responseData = await response.Content.ReadAsStringAsync();
						var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(responseData);
						return tokenResponse?.access_token;
					}
					else
					{
						log.LogError($"Failed to obtain access token from Service Channel: {response.StatusCode}");
						return null;
					}
				}
			}
			catch (Exception ex)
			{
				log.LogError($"An error occurred while obtaining access token: {ex.Message}");
				return null;
			}
		}

		private static async Task<bool> UpdateResolutionInServiceChannel(string accessToken, string workOrderNumber, string resolution, ILogger log)
		{
			try
			{
				using (HttpClient client = new HttpClient())
				{
					client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
					client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

					string updateEndpoint = $"{TokenUri1}/{workOrderNumber}/resolution";

					var requestData = new { Resolution = resolution };
					var jsonContent = JsonConvert.SerializeObject(requestData);
					var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

					HttpResponseMessage response = await client.PostAsync(updateEndpoint, content);

					if (response.IsSuccessStatusCode)
					{
						log.LogInformation($"Resolution updated successfully for Work Order Number: {workOrderNumber}");
						return true;
					}
					else if (response.StatusCode == HttpStatusCode.Unauthorized)
					{
						string newAccessToken = await GetAccessToken(log);
						if (!string.IsNullOrEmpty(newAccessToken))
						{
							return await UpdateResolutionInServiceChannel(newAccessToken, workOrderNumber, resolution, log);
						}
					}

					string errorResponse = await response.Content.ReadAsStringAsync();
					log.LogError($"Failed to update resolution for WorkOrder Number: {workOrderNumber}. StatusCode: {response.StatusCode}, ReasonPhrase: {response.ReasonPhrase}, Error Response: {errorResponse}");
					return false;
				}
			}
			catch (Exception ex)
			{
				log.LogError($"An error occurred while updating resolution: {ex.Message}");
				return false;
			}
		}

		private class WorkOrderData
		{
			public string WorkOrderNumber { get; set; }
			public string Resolution { get; set; }
		}

		private class TokenResponse
		{
			public string access_token { get; set; }
		}
	}
}
