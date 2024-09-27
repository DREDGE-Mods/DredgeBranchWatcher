using CSharpDiscordWebhook.NET.Discord;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SteamKit2;
using System.Drawing;
using System.Net;

namespace DredgeBranchWatcher;

public class BranchInfo
{
	[JsonProperty("branchName")]
	public string BranchName = "";

	[JsonProperty("timeUpdated")]
	public int TimeUpdated;

	[JsonProperty("description")]
	public string Description = "";

	[JsonProperty("buildId")]
	public int BuildId = -1;

	[JsonProperty("pwdRequired")]
	public int PwdRequired = 0;
}

public class PriceInfo
{
	public int initialPrice = 0;
	public int currentPrice = 0;
	public int discountPercent = 0;
}

public class Program
{
	const string BUILDID = "buildid";
	const string DEPOTS = "depots";
	const string BRANCHES = "branches";
	const string TIMEUPDATED = "timeupdated";
	const string PWDREQUIRED = "pwdrequired";
	const string DESCRIPTION = "description";
	const string COMMON = "common";

    const int DREDGE_PACKAGE_ID = 824636;
    const string PACKAGE_NAME = "DREDGE";

    const int PACKAGE_ID_DELUXE = 899715;
    const string PACKAGE_NAME_DELUXE = "DREDGE - Deluxe Edition";

    const int PACKAGE_ID_DIGITAL_DELUXE = 1114568;
    const string PACKAGE_NAME_DIGITAL_DELUXE = "DREDGE - Digital Deluxe Edition";

    const int PACKAGE_ID_COMPLETE = 1093759;
    const string PACKAGE_NAME_COMPLETE = "DREDGE - Complete Edition";

    const int DREDGE_APP_ID = 1562430;
    const string APP_NAME = "DREDGE";

    const int APP_ID_PREORDER = 2198600;
    const string APP_NAME_PREORDER = "Custom Rod";

    const int APP_ID_BLACKSTONE = 2104240;
    const string APP_NAME_BLACKSTONE = "Blackstone Key";

    const int APP_ID_DLC_1 = 2561440;
    const string APP_NAME_DLC_1 = "The Pale Reach";

    const int APP_ID_DLC_2 = 2561450;
    const string APP_NAME_DLC_2 = "The Iron Rig";


    public static void Main(params string[] args)
	{
		var user = args[0];
		var pass = args[1];
		var webhook = args[2];
		var priceWebhook = args[3];

		var steamClient = new SteamClient();
		var manager = new CallbackManager(steamClient);

		var steamUser = steamClient.GetHandler<SteamUser>();
		var appHandler = steamClient.GetHandler<SteamApps>();

		manager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
		manager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
		manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
		manager.Subscribe<SteamApps.PICSProductInfoCallback>(OnPICSProductInfo); 

		var isRunning = true;
		Console.WriteLine($"Trying to connect to Steam...");
		steamClient.Connect();

		while (isRunning)
		{
			manager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
		}

		void OnConnected(SteamClient.ConnectedCallback callback)
		{
			Console.WriteLine($"Connected to Steam. Logging on...");
			steamUser.LogOn(new SteamUser.LogOnDetails
			{
				Username = user,
				Password = pass,
			});
		}

		void OnDisconnected(SteamClient.DisconnectedCallback callback)
		{
			Console.WriteLine($"Disconnected from Steam.");
			isRunning = false;
		}

		async void OnLoggedOn(SteamUser.LoggedOnCallback callback)
		{
			if (callback.Result != EResult.OK)
			{
				Console.WriteLine($"Failed to log into Steam. Result:{callback.Result} ExtendedResult:{callback.Result}");
				isRunning = false;
				return;
			}

			Console.WriteLine($"Logged into Steam.");

			await appHandler.PICSGetProductInfo(new SteamApps.PICSRequest(DREDGE_APP_ID), null, false);
		}

		void OnPICSProductInfo(SteamApps.PICSProductInfoCallback callback)
		{
			Console.WriteLine($"Recieved PICS data.");
			var item = callback.Apps.Single();

			var KeyValues = item.Value.KeyValues;

			var depots = KeyValues[DEPOTS];
			var branches = depots[BRANCHES];

			var common = KeyValues[COMMON];

			var newBranchInfoArray = new BranchInfo[branches.Children.Count];

			for (var i = 0; i < branches.Children.Count; i++)
			{
				var child = branches.Children[i];

				var timeupdated = child[TIMEUPDATED];

				newBranchInfoArray[i] = new BranchInfo() { BranchName = child.Name, TimeUpdated = int.Parse(timeupdated.Value), BuildId = int.Parse(child[BUILDID].Value)};

				if (child[DESCRIPTION] != KeyValue.Invalid)
				{
					newBranchInfoArray[i].Description = child[DESCRIPTION].Value;
				}

				if (child[PWDREQUIRED] != KeyValue.Invalid)
				{
					newBranchInfoArray[i].PwdRequired = int.Parse(child[PWDREQUIRED].Value);
				}
			}

			var newBranches = new List<BranchInfo>();
			var deletedBranches = new List<BranchInfo>();
			var updatedBranches = new List<BranchInfo>();

			if (!File.Exists("branches.json"))
			{
				File.WriteAllText("branches.json", JsonConvert.SerializeObject(new BranchInfo[] {}));
			}

			var previous = JsonConvert.DeserializeObject<BranchInfo[]>(File.ReadAllText("branches.json"));

			File.WriteAllText("branches.json", JsonConvert.SerializeObject(newBranchInfoArray));

			foreach (var newBranchInfo in newBranchInfoArray)
			{
				var existingBranch = previous.FirstOrDefault(x => x.BranchName == newBranchInfo.BranchName);

				if (existingBranch == default)
				{
					newBranches.Add(newBranchInfo);
				}
				else if (existingBranch.TimeUpdated != newBranchInfo.TimeUpdated)
				{
					updatedBranches.Add(newBranchInfo);
				}
			}

			foreach (var oldBranch in previous)
			{
				if (!newBranchInfoArray.Any(x => x.BranchName == oldBranch.BranchName))
				{
					deletedBranches.Add(oldBranch);
				}
			}

			
			if (newBranches.Count > 0 || updatedBranches.Count > 0)
			{
				Console.WriteLine($"Found changes - {newBranches.Count} new branches, {deletedBranches.Count} deleted branches, {updatedBranches.Count} updated branches.");

				var hook = new DiscordWebhook
				{
					Uri = new Uri(webhook)
				};

				var messageList = new List<DiscordMessage>();

				messageList.Add(new DiscordMessage());

				foreach (var newBranch in newBranches)
				{
					var embed = new DiscordEmbed
					{
						Title = "New Branch",
						Color = new DiscordColor(Color.Green),
						Description = $"The branch `{newBranch.BranchName}` was added at <t:{newBranch.TimeUpdated}:F>.",
						Fields = new List<EmbedField>(),
						Footer = new EmbedFooter() { Text = APP_NAME }
					};

					embed.Fields.Add(new EmbedField()
					{
						Name = "Name",
						Value = newBranch.BranchName,
						Inline = true
					});

					if (newBranch.Description != "")
					{
						embed.Fields.Add(new EmbedField()
						{
							Name = "Description",
							Value = newBranch.Description,
							Inline = true
						});
					}

					embed.Fields.Add(new EmbedField()
					{
						Name = "Password Locked",
						Value = newBranch.PwdRequired == 1 ? "Yes" : "No",
						Inline = true
					});

					embed.Fields.Add(new EmbedField()
					{
						Name = "BuildId",
						Value = newBranch.BuildId.ToString(),
						Inline = true
					});

					if (messageList.Last().Embeds.Count >= 10)
					{
						messageList.Add(new DiscordMessage());
					}

					messageList.Last().Embeds.Add(embed);
				}

				foreach (var deletedBranch in deletedBranches)
				{
					var embed = new DiscordEmbed
					{
						Title = "Deleted Branch",
						Color = new DiscordColor(Color.Red),
						Description = $"The branch `{deletedBranch.BranchName}` was deleted.",
						Fields = new List<EmbedField>(),
						Footer = new EmbedFooter() { Text = APP_NAME }
					};

					if (messageList.Last().Embeds.Count >= 10)
					{
						messageList.Add(new DiscordMessage());
					}

					messageList.Last().Embeds.Add(embed);
				}

				foreach (var updatedBranch in updatedBranches)
				{
					var embed = new DiscordEmbed
					{
						Title = "Updated Branch",
						Color = new DiscordColor(Color.Orange),
						Description = $"The branch `{updatedBranch.BranchName}` was updated at <t:{updatedBranch.TimeUpdated}:F>.",
						Fields = new List<EmbedField>(),
						Footer = new EmbedFooter() { Text = APP_NAME }
					};

					embed.Fields.Add(new EmbedField()
					{
						Name = "Name",
						Value = updatedBranch.BranchName,
						Inline = true
					});

					if (updatedBranch.Description != "")
					{
						embed.Fields.Add(new EmbedField()
						{
							Name = "Description",
							Value = updatedBranch.Description,
							Inline = true
						});
					}

					embed.Fields.Add(new EmbedField()
					{
						Name = "Password Locked",
						Value = updatedBranch.PwdRequired == 1 ? "Yes" : "No",
						Inline = true
					});

					embed.Fields.Add(new EmbedField()
					{
						Name = "BuildId",
						Value = updatedBranch.BuildId.ToString(),
						Inline = true
					});

					if (messageList.Last().Embeds.Count >= 10)
					{
						messageList.Add(new DiscordMessage());
					}

					messageList.Last().Embeds.Add(embed);
				}

				foreach (var message in messageList)
				{
					hook.SendAsync(message);
				}
			}

            CheckPackagePrice(DREDGE_PACKAGE_ID, PACKAGE_NAME, priceWebhook);
            CheckPackagePrice(PACKAGE_ID_DELUXE, PACKAGE_NAME_DELUXE, priceWebhook);
            CheckPackagePrice(PACKAGE_ID_DIGITAL_DELUXE, PACKAGE_NAME_DIGITAL_DELUXE, priceWebhook);
            CheckPackagePrice(PACKAGE_ID_COMPLETE, PACKAGE_NAME_COMPLETE, priceWebhook);
            //CheckAppPrice(DREDGE_APP_ID, APP_NAME, priceWebhook);
            CheckAppPrice(APP_ID_PREORDER, APP_NAME_PREORDER, priceWebhook);
            CheckAppPrice(APP_ID_BLACKSTONE, APP_NAME_BLACKSTONE, priceWebhook);
            CheckAppPrice(APP_ID_DLC_1, APP_NAME_DLC_1, priceWebhook);
            CheckAppPrice(APP_ID_DLC_2, APP_NAME_DLC_2, priceWebhook);

            steamUser.LogOff();
		}
	}

    private static void CheckAppPrice(int appid, string appName, string webhook)
    {
        // check for price update
        var json = new WebClient().DownloadString($"https://store.steampowered.com/api/appdetails?appids={appid}&cc=us&filters=price_overview");

        var jObject = JObject.Parse(json);

        var footer = "Steam sale tracker";

        var success = (bool)jObject[$"{appid}"]["success"];
        if (!success)
        {
            Console.WriteLine($"Failed to get any price for {appName} ({appid})");
            return;
        }

        var priceOverview = jObject[$"{appid}"]["data"]["price_overview"];
        var initialPrice = (int)priceOverview["initial"];
        var currentPrice = (int)priceOverview["final"];
        var discountPercent = (int)priceOverview["discount_percent"];
        Console.WriteLine($"{appName} ({appid}): {{initial: {initialPrice}, current: {currentPrice}, discountPercent: {discountPercent}}}");

        var fileName = $"{appid}_price.json";

        var flagFileExists = true;
        if (!File.Exists(fileName))
        {
            flagFileExists = false;
            File.WriteAllText(fileName, JsonConvert.SerializeObject(new PriceInfo()));
        }

        var oldPrice = JsonConvert.DeserializeObject<PriceInfo>(File.ReadAllText(fileName));
        var isOnSale = flagFileExists && (currentPrice != oldPrice.currentPrice);

        var newPrice = new PriceInfo() { currentPrice = currentPrice, initialPrice = initialPrice, discountPercent = discountPercent };
        File.WriteAllText(fileName, JsonConvert.SerializeObject(newPrice));

        if (isOnSale)
        {
            var hook = new DiscordWebhook
            {
                Uri = new Uri(webhook)
            };

            var message = new DiscordMessage();

            if (oldPrice.discountPercent == 0)
            {
                var embed = new DiscordEmbed()
                {
                    Title = "Sale Started!",
                    Color = new DiscordColor(Color.LightBlue),
                    Description = $"{appName} is now on sale! From ${initialPrice / 100f:F2} to ${currentPrice / 100f:F2} ({discountPercent}% off).",
                    Footer = new EmbedFooter() { Text = footer }
                };
                message.Embeds.Add(embed);
            }
            else if (newPrice.discountPercent > oldPrice.discountPercent)
            {
                var embed = new DiscordEmbed()
                {
                    Title = "Sale Update",
                    Color = new DiscordColor(Color.LightBlue),
                    Description = $"The {appName.Replace("The ", string.Empty)} sale has increased! From ${oldPrice.currentPrice / 100f:F2} ({oldPrice.discountPercent}% off) to ${currentPrice / 100f:F2} ({discountPercent}% off).",
                    Footer = new EmbedFooter() { Text = footer }
                };
                message.Embeds.Add(embed);
            }
            else if (newPrice.discountPercent == 0)
            {
                var embed = new DiscordEmbed()
                {
                    Title = "Sale Ended",
                    Color = new DiscordColor(Color.LightBlue),
                    Description = $"The {appName.Replace("The ", string.Empty)} sale has ended. Back to ${initialPrice / 100f:F2}.",
                    Footer = new EmbedFooter() { Text = footer }
                };
                message.Embeds.Add(embed);
            }
            else if (newPrice.discountPercent < oldPrice.discountPercent)
            {
                var embed = new DiscordEmbed()
                {
                    Title = "Sale Update",
                    Color = new DiscordColor(Color.LightBlue),
                    Description = $"The {appName.Replace("The ", string.Empty)} sale has decreased. From ${oldPrice.currentPrice / 100f:F2} ({oldPrice.discountPercent}% off) to ${currentPrice / 100f:F2} ({discountPercent}% off).",
                    Footer = new EmbedFooter() { Text = footer }
                };
                message.Embeds.Add(embed);
            }

            hook.SendAsync(message);
        }
    }

    private static void CheckPackagePrice(int packageid, string packageName, string webhook)
    {
        // check for price update
        var json = new WebClient().DownloadString($"https://store.steampowered.com/api/packagedetails?packageids={packageid}&cc=us");

        var jObject = JObject.Parse(json);

        var footer = "Steam sale tracker";

        var success = (bool)jObject[$"{packageid}"]["success"];
        if (!success)
        {
            Console.WriteLine($"Failed to get any price for {packageName} ({packageid})");
            return;
        }

        var priceOverview = jObject[$"{packageid}"]["data"]["price"];
        var initialPrice = (int)priceOverview["initial"];
        var currentPrice = (int)priceOverview["final"];
        var discountPercent = (int)priceOverview["discount_percent"];
        Console.WriteLine($"{packageName} ({packageid}): {{initial: {initialPrice}, current: {currentPrice}, discountPercent: {discountPercent}}}");

        var fileName = $"{packageid}_price.json";

        var flagFileExists = true;
        if (!File.Exists(fileName))
        {
            flagFileExists = false;
            File.WriteAllText(fileName, JsonConvert.SerializeObject(new PriceInfo()));
        }

        var oldPrice = JsonConvert.DeserializeObject<PriceInfo>(File.ReadAllText(fileName));
        var isOnSale = flagFileExists && (currentPrice != oldPrice.currentPrice);

        var newPrice = new PriceInfo() { currentPrice = currentPrice, initialPrice = initialPrice, discountPercent = discountPercent };
        File.WriteAllText(fileName, JsonConvert.SerializeObject(newPrice));

        if (isOnSale)
        {
            var hook = new DiscordWebhook
            {
                Uri = new Uri(webhook)
            };

            var message = new DiscordMessage();

            if (oldPrice.discountPercent == 0)
            {
                var embed = new DiscordEmbed()
                {
                    Title = "Sale Started!",
                    Color = new DiscordColor(Color.LightBlue),
                    Description = $"{packageName} is now on sale! From ${initialPrice / 100f:F2} to ${currentPrice / 100f:F2} ({discountPercent}% off).",
                    Footer = new EmbedFooter() { Text = footer }
                };
                message.Embeds.Add(embed);
            }
            else if (newPrice.discountPercent > oldPrice.discountPercent)
            {
                var embed = new DiscordEmbed()
                {
                    Title = "Sale Update",
                    Color = new DiscordColor(Color.LightBlue),
                    Description = $"The {packageName.Replace("The ", string.Empty)} sale has increased! From ${oldPrice.currentPrice / 100f:F2} ({oldPrice.discountPercent}% off) to ${currentPrice / 100f:F2} ({discountPercent}% off).",
                    Footer = new EmbedFooter() { Text = footer }
                };
                message.Embeds.Add(embed);
            }
            else if (newPrice.discountPercent == 0)
            {
                var embed = new DiscordEmbed()
                {
                    Title = "Sale Ended",
                    Color = new DiscordColor(Color.LightBlue),
                    Description = $"The {packageName.Replace("The ", string.Empty)} sale has ended. Back to ${initialPrice / 100f:F2}.",
                    Footer = new EmbedFooter() { Text = footer }
                };
                message.Embeds.Add(embed);
            }
            else if (newPrice.discountPercent < oldPrice.discountPercent)
            {
                var embed = new DiscordEmbed()
                {
                    Title = "Sale Update",
                    Color = new DiscordColor(Color.LightBlue),
                    Description = $"The {packageName.Replace("The ", string.Empty)} sale has decreased. From ${oldPrice.currentPrice / 100f:F2} ({oldPrice.discountPercent}% off) to ${currentPrice / 100f:F2} ({discountPercent}% off).",
                    Footer = new EmbedFooter() { Text = footer }
                };
                message.Embeds.Add(embed);
            }

            hook.SendAsync(message);
        }
    }
}