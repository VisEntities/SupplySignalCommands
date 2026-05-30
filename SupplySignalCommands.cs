/*
 * Copyright (C) 2026 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Oxide.Core;
using Oxide.Core.Libraries;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Supply Signal Commands", "VisEntities", "1.6.0")]
    [Description("Runs custom commands when a player throws a supply signal.")]
    public class SupplySignalCommands : RustPlugin
    {
        #region Fields

        private static SupplySignalCommands _plugin;
        private static Configuration _config;
        private StoredData _data;

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Discord Webhook Url (Leave empty to disable Discord logging)")]
            public string DiscordWebhookUrl { get; set; }

            [JsonProperty("Log Triggers To Server Console")]
            public bool LogTriggersToServerConsole { get; set; }

            [JsonProperty("Supply Signals")]
            public List<SupplySignalSettings> SupplySignals { get; set; }
        }

        private class SupplySignalSettings
        {
            [JsonProperty("Custom Item Name (Leave empty to match by skin only)")]
            public string CustomItemName { get; set; }

            [JsonProperty("Skin Id (0 = default supply signal)")]
            public ulong SkinId { get; set; }

            [JsonProperty("Also Call Normal Supply Drop (false = run commands only, no airdrop)")]
            public bool AlsoCallNormalSupplyDrop { get; set; }

            [JsonProperty("Cooldown Seconds (0 = no cooldown)")]
            public float CooldownSeconds { get; set; }

            [JsonProperty("Command Delay Seconds (0 = instant)")]
            public float CommandDelaySeconds { get; set; }

            [JsonProperty("Run Only One Random Command (false = run all; true = pick one using its chance as weight)")]
            public bool RunOnlyOneRandomCommand { get; set; }

            [JsonProperty("Commands To Run")]
            public List<CommandSettings> CommandsToRun { get; set; }

            [JsonProperty("Global Message (Broadcast to all players; leave empty to disable)")]
            public string GlobalMessage { get; set; }

            [JsonProperty("Personal Message (Sent only to the thrower; leave empty to disable)")]
            public string PersonalMessage { get; set; }
        }

        private class CommandSettings
        {
            [JsonProperty("Type (Chat = player chat, Client = run on player, Server = run on server console)")]
            [JsonConverter(typeof(StringEnumConverter))]
            public CommandType Type { get; set; }

            [JsonProperty("Command (Supports placeholders like {PlayerName}, {Grid}, {ServerName})")]
            public string Command { get; set; }

            [JsonProperty("Chance Percent (0-100; in random mode this is the pick weight)")]
            public float Chance { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<Configuration>();

            if (string.Compare(_config.Version, Version.ToString()) < 0)
                UpdateConfig();

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void UpdateConfig()
        {
            PrintWarning("Config changes detected! Updating...");

            Configuration defaultConfig = GetDefaultConfig();

            if (string.Compare(_config.Version, "1.0.0") < 0)
                _config = defaultConfig;

            if (string.Compare(_config.Version, "1.6.0") < 0)
                _config = defaultConfig;

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                DiscordWebhookUrl = "",
                LogTriggersToServerConsole = false,
                SupplySignals = new List<SupplySignalSettings>
                {
                    new SupplySignalSettings
                    {
                        CustomItemName = "Reward Signal",
                        SkinId = 0,
                        AlsoCallNormalSupplyDrop = false,
                        CooldownSeconds = 300f,
                        CommandDelaySeconds = 1f,
                        RunOnlyOneRandomCommand = false,
                        CommandsToRun = new List<CommandSettings>
                        {
                            new CommandSettings
                            {
                                Type = CommandType.Server,
                                Command = "inventory.giveto {PlayerId} scrap 100",
                                Chance = 100f
                            },
                            new CommandSettings
                            {
                                Type = CommandType.Server,
                                Command = "inventory.giveto {PlayerId} lowgradefuel 50",
                                Chance = 50f
                            },
                            new CommandSettings
                            {
                                Type = CommandType.Chat,
                                Command = "{PlayerName} redeemed a reward signal in grid {Grid}!",
                                Chance = 100f
                            }
                        },
                        GlobalMessage = "",
                        PersonalMessage = "Your reward signal has been redeemed, {PlayerName}!"
                    }
                }
            };
        }

        #endregion Configuration

        #region Stored Data

        private class StoredData
        {
            [JsonProperty("Cooldowns")]
            public Dictionary<ulong, Dictionary<string, double>> Cooldowns { get; set; } = new Dictionary<ulong, Dictionary<string, double>>();
        }

        private void LoadData()
        {
            _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
            if (_data == null)
                _data = new StoredData();
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, _data);
        }

        #endregion Stored Data

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
            LoadData();
        }

        private void Unload()
        {
            SaveData();
            _config = null;
            _plugin = null;
        }

        private void OnExplosiveThrown(BasePlayer player, SupplySignal supplySignal, ThrownWeapon thrownWeapon)
        {
            if (player == null || supplySignal == null || thrownWeapon == null)
                return;

            Item item = thrownWeapon.GetItem();
            if (item == null)
                return;

            ulong skinId = item.skin;
            string name = item.name;

            SupplySignalSettings supplySignalConfig = _config.SupplySignals.FirstOrDefault(c =>
                c.SkinId == skinId
                && (string.IsNullOrEmpty(c.CustomItemName) || c.CustomItemName == name)
            );

            if (supplySignalConfig == null)
                return;

            string cooldownKey = GetCooldownKey(supplySignalConfig);

            if (supplySignalConfig.CooldownSeconds > 0)
            {
                if (OnCooldown(player, cooldownKey, supplySignalConfig.CooldownSeconds, out double remain))
                {
                    SendReplyLocalized(player, Lang.Cooldown_Active, FormatTime(remain));

                    RefundSupplySignal(player, supplySignalConfig);
                    supplySignal.CancelInvoke(supplySignal.Explode);
                    supplySignal.Kill();

                    return;
                }

                SetCooldown(player, cooldownKey);
            }

            if (!supplySignalConfig.AlsoCallNormalSupplyDrop)
            {
                supplySignal.CancelInvoke(supplySignal.Explode);
                supplySignal.Kill();
            }

            SendNotification(player, supplySignalConfig, name);

            if (!string.IsNullOrEmpty(supplySignalConfig.GlobalMessage))
            {
                string globalMsg = ReplacePlaceholders(supplySignalConfig.GlobalMessage, player);
                foreach (BasePlayer activePlayer in BasePlayer.activePlayerList)
                {
                    if (activePlayer == player)
                        continue;

                    SendRawReply(activePlayer, globalMsg);
                }
            }

            if (!string.IsNullOrEmpty(supplySignalConfig.PersonalMessage))
            {
                string personalMsg = ReplacePlaceholders(supplySignalConfig.PersonalMessage, player);
                SendRawReply(player, personalMsg);
            }

            if (supplySignalConfig.CommandDelaySeconds > 0f)
            {
                SupplySignalSettings capturedConfig = supplySignalConfig;
                timer.Once(supplySignalConfig.CommandDelaySeconds, () =>
                {
                    if (player != null)
                        RunCommands(player, capturedConfig);
                });
            }
            else
            {
                RunCommands(player, supplySignalConfig);
            }
        }

        #endregion Oxide Hooks

        #region Commands

        // Usage: supplysignalcommands.give [configIndex] [amount] [player]
        [ConsoleCommand("supplysignalcommands.give")]
        private void CmdGiveSupplySignal(ConsoleSystem.Arg arg)
        {
            BasePlayer caller = arg.Player();

            if (caller != null && !caller.IsAdmin)
            {
                arg.ReplyWith("You do not have permission to use this command.");
                return;
            }

            if (_config.SupplySignals == null || _config.SupplySignals.Count == 0)
            {
                arg.ReplyWith("No supply signals are configured.");
                return;
            }

            int index = 0;
            if (arg.HasArgs(1))
                index = arg.GetInt(0, 0);

            if (index < 0 || index >= _config.SupplySignals.Count)
            {
                arg.ReplyWith($"Invalid config index. Valid range: 0 to {_config.SupplySignals.Count - 1}.");
                return;
            }

            int amount = 1;
            if (arg.HasArgs(2))
                amount = arg.GetInt(1, 1);

            if (amount < 1)
                amount = 1;

            BasePlayer target;
            if (arg.HasArgs(3))
                target = BasePlayer.Find(arg.GetString(2));
            else
                target = caller;

            if (target == null)
            {
                arg.ReplyWith("Target player not found. Specify a player when running from the server console.");
                return;
            }

            SupplySignalSettings settings = _config.SupplySignals[index];

            Item signal = ItemManager.CreateByName("supply.signal", amount, settings.SkinId);
            if (signal == null)
            {
                arg.ReplyWith("Failed to create the supply signal item.");
                return;
            }

            if (!string.IsNullOrEmpty(settings.CustomItemName))
                signal.name = settings.CustomItemName;

            if (!target.inventory.GiveItem(signal))
                signal.Drop(target.GetDropPosition(), target.GetDropVelocity());

            arg.ReplyWith($"Gave {amount}x supply signal (config #{index}) to {target.displayName}.");
        }

        #endregion Commands

        #region Cooldown

        private string GetCooldownKey(SupplySignalSettings supplySignalConfig)
        {
            string displayName = supplySignalConfig.CustomItemName;
            if (displayName == null)
                displayName = "";

            return supplySignalConfig.SkinId + "|" + displayName;
        }

        private bool OnCooldown(BasePlayer player, string cooldownKey, float cooldownSeconds, out double timeRemaining)
        {
            timeRemaining = 0;

            if (!_data.Cooldowns.TryGetValue(player.userID, out Dictionary<string, double> dict))
                return false;

            if (!dict.TryGetValue(cooldownKey, out double lastUse))
                return false;

            double now = CurrentTime();
            double nextAvail = lastUse + cooldownSeconds;
            if (now < nextAvail)
            {
                timeRemaining = nextAvail - now;
                return true;
            }

            return false;
        }

        private void SetCooldown(BasePlayer player, string cooldownKey)
        {
            if (!_data.Cooldowns.TryGetValue(player.userID, out Dictionary<string, double> dict))
            {
                dict = new Dictionary<string, double>();
                _data.Cooldowns[player.userID] = dict;
            }

            dict[cooldownKey] = CurrentTime();
            SaveData();
        }

        private static double CurrentTime()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        #endregion Cooldown

        #region Helper Functions

        private string FormatTime(double seconds)
        {
            TimeSpan ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1)
            {
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            }
            else if (ts.TotalMinutes >= 1)
            {
                return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
            }
            else
            {
                return $"{(int)ts.TotalSeconds}s";
            }
        }

        #endregion Helper Functions

        #region Command Execution

        private enum CommandType
        {
            Chat,
            Server,
            Client
        }

        private void RunCommands(BasePlayer player, SupplySignalSettings supplySignalConfig)
        {
            if (supplySignalConfig.CommandsToRun == null || supplySignalConfig.CommandsToRun.Count == 0)
                return;

            if (supplySignalConfig.RunOnlyOneRandomCommand)
            {
                CommandSettings chosen = PickWeightedRandom(supplySignalConfig.CommandsToRun);
                if (chosen != null)
                    RunCommand(player, chosen.Type, chosen.Command);
            }
            else
            {
                foreach (CommandSettings commandConfig in supplySignalConfig.CommandsToRun)
                {
                    if (PassesChance(commandConfig.Chance))
                        RunCommand(player, commandConfig.Type, commandConfig.Command);
                }
            }
        }

        private bool PassesChance(float chance)
        {
            if (chance >= 100f)
                return true;
            if (chance <= 0f)
                return false;

            return UnityEngine.Random.Range(0f, 100f) <= chance;
        }

        private CommandSettings PickWeightedRandom(List<CommandSettings> commands)
        {
            float totalWeight = 0f;
            foreach (CommandSettings command in commands)
                totalWeight += Mathf.Max(0f, command.Chance);

            if (totalWeight <= 0f)
                return commands.GetRandom();

            float roll = UnityEngine.Random.Range(0f, totalWeight);
            float cumulative = 0f;
            foreach (CommandSettings command in commands)
            {
                cumulative += Mathf.Max(0f, command.Chance);
                if (roll <= cumulative)
                    return command;
            }

            return commands[commands.Count - 1];
        }

        private void RunCommand(BasePlayer player, CommandType type, string command)
        {
            string result = ReplacePlaceholders(command, player);

            switch (type)
            {
                case CommandType.Chat:
                    player.Command($"chat.say \"{result}\"");
                    break;
                case CommandType.Client:
                    player.Command(result);
                    break;
                case CommandType.Server:
                    Server.Command(result);
                    break;
            }
        }

        #endregion Command Execution

        #region Notifications

        private void SendNotification(BasePlayer player, SupplySignalSettings supplySignalConfig, string itemName)
        {
            if (!_config.LogTriggersToServerConsole && string.IsNullOrEmpty(_config.DiscordWebhookUrl))
                return;

            string label;
            if (!string.IsNullOrEmpty(supplySignalConfig.CustomItemName))
                label = supplySignalConfig.CustomItemName;
            else if (!string.IsNullOrEmpty(itemName))
                label = itemName;
            else
                label = "supply signal";

            string grid = MapHelper.PositionToString(player.transform.position);
            string message = $"{player.displayName} ({player.UserIDString}) triggered '{label}' at grid {grid}.";

            if (_config.LogTriggersToServerConsole)
                Puts(message);

            if (!string.IsNullOrEmpty(_config.DiscordWebhookUrl))
                SendDiscordMessage(message);
        }

        private void SendDiscordMessage(string content)
        {
            var payload = new Dictionary<string, string> { ["content"] = content };
            string body = JsonConvert.SerializeObject(payload);
            var headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" };

            webrequest.Enqueue(_config.DiscordWebhookUrl, body, (code, response) =>
            {
                if (code != 200 && code != 204)
                    PrintWarning($"Discord webhook request failed (code {code}): {response}");
            }, this, RequestMethod.POST, headers);
        }

        #endregion Notifications

        #region Placeholder Replacement

        private string ReplacePlaceholders(string text, BasePlayer player)
        {
            return text
                .Replace("{PlayerId}", player.UserIDString)
                .Replace("{PlayerName}", player.displayName)
                .Replace("{PositionX}", player.transform.position.x.ToString("F1"))
                .Replace("{PositionY}", player.transform.position.y.ToString("F1"))
                .Replace("{PositionZ}", player.transform.position.z.ToString("F1"))
                .Replace("{Grid}", MapHelper.PositionToString(player.transform.position))
                .Replace("{OnlinePlayers}", BasePlayer.activePlayerList.Count.ToString())
                .Replace("{ServerName}", ConVar.Server.hostname);
        }

        #endregion Placeholder Replacement

        #region Refund

        private void RefundSupplySignal(BasePlayer player, SupplySignalSettings supplySignalConfig)
        {
            if (player == null || supplySignalConfig == null)
                return;

            Item newSignal = ItemManager.CreateByName("supply.signal", 1, supplySignalConfig.SkinId);
            if (newSignal != null)
            {
                if (!string.IsNullOrEmpty(supplySignalConfig.CustomItemName))
                    newSignal.name = supplySignalConfig.CustomItemName;

                bool addedToInventory = player.inventory.GiveItem(newSignal);
                if (addedToInventory)
                {
                    SendReplyLocalized(player, Lang.Signal_Refunded);
                }
                else
                {
                    newSignal.Drop(player.GetDropPosition(), player.GetDropVelocity());
                    SendReplyLocalized(player, Lang.Signal_RefundedToGround);
                }
            }
        }

        #endregion Refund

        #region Localization

        private class Lang
        {
            public const string Cooldown_Active = "Cooldown.Active";
            public const string Signal_Refunded = "Signal.Refunded";
            public const string Signal_RefundedToGround = "Signal.RefundedToGround";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.Cooldown_Active] = "This supply signal is on cooldown. Try again in {0}.",
                [Lang.Signal_Refunded] = "Your supply signal was returned to your inventory because it's still on cooldown.",
                [Lang.Signal_RefundedToGround] = "Your inventory was full, so the supply signal was dropped at your feet (still on cooldown)."

            }, this, "en");
        }

        private static string GetLangText(BasePlayer player, string langKey, params object[] args)
        {
            string userId;
            if (player != null)
                userId = player.UserIDString;
            else
                userId = null;

            string message = _plugin.lang.GetMessage(langKey, _plugin, userId);

            if (args.Length > 0)
                message = string.Format(message, args);

            return message;
        }

        public static void SendReplyLocalized(BasePlayer player, string langKey, params object[] args)
        {
            string message = GetLangText(player, langKey, args);

            if (!string.IsNullOrWhiteSpace(message))
                _plugin.SendReply(player, message);
        }

        private void SendRawReply(BasePlayer player, string message)
        {
            if (player != null && !string.IsNullOrEmpty(message))
                SendReply(player, message);
        }

        #endregion Localization
    }
}