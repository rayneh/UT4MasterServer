﻿using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using UT4MasterServer.Authentication;
using UT4MasterServer.Models;
using UT4MasterServer.Other;
using UT4MasterServer.Services;

namespace UT4MasterServer.Controllers;

/// <summary>
/// ut-public-service-prod10.ol.epicgames.com
/// </summary>
[ApiController]
[Route("ut/api/game/v2/profile")]
[AuthorizeBearer]
[Produces("application/json")]
public class UnrealTournamentProfileController : JsonAPIController
{
	private readonly AccountService accountService;

	public UnrealTournamentProfileController(ILogger<SessionController> logger, AccountService accountService) : base(logger)
	{
		this.accountService = accountService;

	}

	[HttpPost("{id}/{clientKind}/QueryProfile")]
	public async Task<IActionResult> QueryProfile(string id,
		string clientKind,
		[FromQuery] string profileId,
		[FromQuery] string rvn)
	{
		if (User.Identity is not EpicUserIdentity user)
			return Unauthorized();

		bool isRequestSentFromClient = clientKind.ToLower() == "client";
		bool isRequestSentFromServer = clientKind.ToLower() == "dedicated_server";

		// TODO: I think "rvn" is revision number and it represents index of profile change entry.
		// negative values probably mean index from back-to-front in array.

		var body = await Request.BodyReader.ReadAsStringAsync(1024);
		var jsonBody = JObject.Parse(body);

		// game sends empty json object as body
		if (!(isRequestSentFromClient || isRequestSentFromServer) | profileId != "profile0" || rvn != "-1" || jsonBody != JObject.Parse("{}"))
		{
			logger.LogWarning($"QueryProfile received unexpected data! k:\"{clientKind}\" p:\"{profileId}\" rvn:\"{rvn}\" body:\"{body}\"");
		}

		var account = await accountService.GetAccountAsync(EpicID.FromString(id));
		if (account == null)
			return NotFound();

		// actual response example is in <repo_root>/OldReferenceCode/Server.cs line 750

		int revisionNumber = 3;

		JObject obj = new();
		obj.Add("profileRevision", revisionNumber);
		obj.Add("profileId", profileId);
		obj.Add("profileChangesBaseRevision", revisionNumber);
		JArray profileChanges = new JArray();
		// foreach {
		JObject profileChange = new();
		profileChange.Add("changeType", "fullProfileUpdate");
		JObject profile = new();
		profile.Add("_id", account.ID.ToString());
		profile.Add("created", account.CreatedAt.ToStringISO());
		profile.Add("updated", (account.LastLoginAt - TimeSpan.FromSeconds(10)).ToStringISO()); // we don't store this info, send an arbitrary one
		profile.Add("rvn", revisionNumber);
		profile.Add("wipeNumber", 1);
		profile.Add("accountId", account.ID.ToString());
		profile.Add("profileId", profileId);
		profile.Add("version", "ut_base");
		JObject items = new();
		// TODO !!!
		profile.Add("items", items);
		JObject stats = new();
		stats.Add("templateId", "profile_v2");
		JObject attributes = new();
		attributes.Add("CountryFlag", account.CountryFlag);
		attributes.Add("GoldStars", account.GoldStars);
		JObject login_rewards = new();
		login_rewards.Add("nextClaimTime", null);
		login_rewards.Add("level", 0);
		login_rewards.Add("totalDays", 0);
		attributes.Add("login_rewards", login_rewards);
		attributes.Add("Avatar", account.Avatar);
		attributes.Add("inventory_limit_bonus", 0);
		attributes.Add("daily_purchases", new JObject());
		attributes.Add("in_app_purchases", new JObject());
		attributes.Add("LastXPTime", (account.LastLoginAt - TimeSpan.FromSeconds(10)).ToUnixTimestamp()); // we don't store this info, send an arbitrary one
		attributes.Add("XP", account.XP);
		attributes.Add("Level", account.LevelStockLimited); // TODO: try values over 50
		attributes.Add("BlueStars", account.BlueStars);
		attributes.Add("RecentXP", account.XPLastMatch); // probably xp from last finished match
		attributes.Add("boosts", new JArray());
		attributes.Add("new_items", new JObject());
		stats.Add("attributes", attributes);
		profile.Add("stats", stats);
		profileChange.Add("profile", profile);
		profileChange.Add("commandRevision", revisionNumber + 1);
		// }
		profileChanges.Add(profileChange);
		obj.Add("profileChanges", profileChanges);
		obj.Add("profileCommandRevision", revisionNumber + 1);
		obj.Add("serverTime", DateTime.UtcNow.ToStringISO());
		obj.Add("responseVersion", 1);
		obj.Add("command", "QueryProfile");

		return Json(obj);
	}

	[HttpPost("{id}/{clientKind}/SetAvatarAndFlag")]
	public async Task<IActionResult> SetAvatarAndFlag(string id, string clientKind, [FromQuery] string profileId, [FromQuery] string rvn)
	{
		if (User.Identity is not EpicUserIdentity user)
			return Unauthorized();

		if (user.Session.AccountID != EpicID.FromString(id))
			return Unauthorized();

		// TODO: Permission: "Sorry your login does not posses the permissions 'ut:profile:{id_from_params}:commands ALL' needed to perform the requested operation"

		JObject obj = JObject.Parse(await Request.BodyReader.ReadAsStringAsync(1024));

		string? avatar = obj["newAvatar"]?.ToObject<string>();
		string? flag = obj["newFlag"]?.ToObject<string>();

		int revisionNumber = 3;

		obj = new JObject();
		obj.Add("profileRevision", revisionNumber);
		obj.Add("profileId", "profile0");
		obj.Add("profileChangesBaseRevision", revisionNumber - 1);
		JArray profileChanges = new JArray();
		if (avatar != null || flag != null)
		{
			var acc = await accountService.GetAccountAsync(user.Session.AccountID);
			if (acc == null)
			{
				logger.LogError("Account is null");
				return StatusCode(StatusCodes.Status500InternalServerError); // should never happen
			}

			if (avatar != null)
			{
				acc.Avatar = avatar;
				JObject profileChange = new()
				{
					{ "changeType", "statModified" },
					{ "name", "Avatar" },
					{ "value", acc.Avatar }
				};
				profileChanges.Add(profileChange);
			}

			if (flag != null)
			{
				acc.CountryFlag = flag;
				JObject profileChange = new()
				{
					{ "changeType", "statModified" },
					{ "name", "CountryFlag" },
					{ "value", acc.CountryFlag }
				};
				profileChanges.Add(profileChange);
			}

			await accountService.UpdateAccountAsync(acc);
		}
		obj.Add("profileChanges", profileChanges);
		obj.Add("profileCommandRevision", revisionNumber - 1);
		obj.Add("serverTime", DateTime.UtcNow.ToStringISO());
		obj.Add("responseVersion", 1);
		obj.Add("command", "SetAvatarAndFlag");

		/* input:
		{
			"newAvatar": "UT.Avatar.0",
			"newFlag": "Algeria"
		}

		*/
		// response: {"profileRevision":3,"profileId":"profile0","profileChangesBaseRevision":2,"profileChanges":[{"changeType":"statModified","name":"Avatar","value":"UT.Avatar.0"},{"changeType":"statModified","name":"CountryFlag","value":"Algeria"}],"profileCommandRevision":2,"serverTime":"2022-12-20T18:31:46.948Z","responseVersion":1,"command":"SetAvatarAndFlag"}
		return Json(obj);
	}

	[HttpPost("{id}/{clientKind}/GrantXP")]
	public async Task<IActionResult> GrantXP(string id, string clientKind, [FromQuery] string profileId, [FromQuery] string rvn)
	{
		// only known to be sent by dedicated_server so far
		bool isRequestSentFromClient = clientKind.ToLower() == "client";
		bool isRequestSentFromServer = clientKind.ToLower() == "dedicated_server";

		return NotFound(); // TODO: unknown response
	}

	[HttpPost("{id}/{clientKind}/SetStars")]
	public async Task<IActionResult> SetStars(string id, string clientKind, [FromQuery] string profileId, [FromQuery] string rvn)
	{
		// only known to be sent by client so far
		bool isRequestSentFromClient = clientKind.ToLower() == "client";
		bool isRequestSentFromServer = clientKind.ToLower() == "dedicated_server";

		return NotFound(); // TODO: unknown response
	}
}
