using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

public class MyMessageModel
{
	public string id { get; set; }
}

public class TokenResponse
{
	public string access_token { get; set; }
	// Add other token properties if needed
}

public static class ServiceBusFunction
{
	private static TelemetryClient telemetryClient = new TelemetryClient();
	private static IConfigurationRoot configuration;

	static ServiceBusFunction()
	{
		var builder = new ConfigurationBuilder()
			.SetBasePath(Environment.CurrentDirectory)
			.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
			.AddEnvironmentVariables();

		configuration = builder.Build();
	}

	[FunctionName("createcrm ")]
	public static async Task Run([ServiceBusTrigger("servicechannel", Connection = "hello")] string myQueueItem, ILogger log)
	{
		try
		{
			if (telemetryClient == null)
			{
				telemetryClient = new TelemetryClient();
			}

			log.LogInformation($"C# ServiceBus queue trigger function processed message: {myQueueItem}");

			// Track the start of the function execution
			telemetryClient.TrackEvent("FunctionStart", new Dictionary<string, string> { { "Message", myQueueItem } });

			MyMessageModel message = JsonConvert.DeserializeObject<MyMessageModel>(myQueueItem);

			if (message != null && !string.IsNullOrEmpty(message.id))
			{
				string idValue = message.id;

				string accessToken = await GetAccessToken();

				if (!string.IsNullOrEmpty(accessToken))
				{
					await CreateRecordInCRM(accessToken, idValue);
				}
				else
				{
					log.LogError("Failed to retrieve access token for D365.");

					// Track an event for the error
					telemetryClient.TrackEvent("AccessTokenError", new Dictionary<string, string> { { "Message", myQueueItem } });
				}
			}
			else
			{
				log.LogError("Message does not contain a valid 'id' property.");

				// Track an event for the error
				telemetryClient.TrackEvent("InvalidMessageError", new Dictionary<string, string> { { "Message", myQueueItem } });
			}

			// Track the successful completion of the function
			telemetryClient.TrackEvent("FunctionComplete", new Dictionary<string, string> { { "Message", myQueueItem } });
		}
		catch (Exception ex)
		{
			log.LogError($"An error occurred: {ex.Message}");

			// Track an exception
			telemetryClient.TrackException(ex);
		}
	}

	static async Task<string> GetAccessToken()
	{
		try
		{
			var client = new HttpClient();
			var request = new HttpRequestMessage(HttpMethod.Post, configuration["TokenEndpoint"]);
			var collection = new List<KeyValuePair<string, string>>();
			collection.Add(new KeyValuePair<string, string>("client_id", configuration["ClientId"]));
			collection.Add(new KeyValuePair<string, string>("client_secret", configuration["ClientSecret"]));
			collection.Add(new KeyValuePair<string, string>("grant_type", "client_credentials"));
			collection.Add(new KeyValuePair<string, string>("scope", configuration["TokenScope"]));
			var content = new FormUrlEncodedContent(collection);
			request.Content = content;

			var response = await client.SendAsync(request);
			if (response.IsSuccessStatusCode)
			{
				var responseContent = await response.Content.ReadAsStringAsync();
				var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(responseContent);
				return tokenResponse?.access_token;
			}
			else
			{
				return null;
			}
		}
		catch (Exception ex)
		{
			// Track an exception for the error
			telemetryClient.TrackException(ex);
			return null;
		}
	}

	static async Task CreateRecordInCRM(string accessToken, string idValue)
	{
		try
		{
			var client = new HttpClient();
			var request = new HttpRequestMessage(HttpMethod.Post, configuration["CrmApiEndpoint"]);

			request.Headers.Add("Authorization", "Bearer " + accessToken);

			var jsonContent = new StringContent(
				"{\"msdyn_serviceaccount@odata.bind\": \"/accounts(88019079-E334-46C0-B2DA-27D8A73C51DC)\", \"crc62_workordernumber\":\"" + idValue + "\", \"msdyn_workordertype@odata.bind\": \"/msdyn_workordertypes(4FC79B0F-510B-EA11-A813-000D3A1B1808)\"}",
				System.Text.Encoding.UTF8,
				"application/json");

			request.Content = jsonContent;

			var response = await client.SendAsync(request);

			if (response.IsSuccessStatusCode)
			{
				var responseContent = await response.Content.ReadAsStringAsync();

				// Check if the status code is 202 and print the response content
				if (response.StatusCode == HttpStatusCode.Accepted)
				{
					Console.WriteLine($"Request accepted. Response content: {responseContent}");
				}
				else
				{
					Console.WriteLine(responseContent);
				}
			}
			else
			{
				Console.WriteLine($"Failed to create record in CRM. Status code: {response.StatusCode}");
			}
		}
		catch (Exception ex)
		{
			// Handle exceptions
			Console.WriteLine($"An exception occurred: {ex.Message}");
		}
	}
}