/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Supply Signal Commands", "VisEntities", "1.4.0")]
    [Description("Run commands when a supply signal is thrown.")]
    public class SupplySignalCommands : RustPlugin
    {
        #region Fields

        private static SupplySignalCommands _plugin;
        private static Configuration _config;
        private Dictionary<ulong, Dictionary<int, double>> _lastUseTimes = new Dictionary<ulong, Dictionary<int, double>>();

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Supply Signals")]
            public List<SupplySignalConfig> SupplySignals { get; set; }
        }

        private class SupplySignalConfig
        {
            [JsonProperty("Supply Signal Display Name")]
            public string DisplayName { get; set; }

            [JsonProperty("Supply Signal Skin Id")]
            public ulong SupplySignalSkinId { get; set; }

            [JsonProperty("Should Explode")]
            public bool ShouldExplode { get; set; }

            [JsonProperty("Cooldown Seconds")]
            public float CooldownSeconds { get; set; }

            [JsonProperty("Run Random Command ")]
            public bool RunRandomCommand { get; set; }

            [JsonProperty("Commands To Run")]
            public List<CommandConfig> CommandsToRun { get; set; }

            [JsonProperty("Global Message (Sent to Everyone)")]
            public string GlobalMessage { get; set; }

            [JsonProperty("Personal Message (Sent to Thrower)")]
            public string PersonalMessage { get; set; }
        }

        private class CommandConfig
        {
            [JsonProperty("Type")]
            [JsonConverter(typeof(StringEnumConverter))]
            public CommandType Type { get; set; }

            [JsonProperty("Command")]
            public string Command { get; set; }
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

            if (string.Compare(_config.Version, "1.1.0") < 0)
            {
                foreach (SupplySignalConfig supplySignal in _config.SupplySignals)
                {
                    supplySignal.DisplayName = "";
                }
            }

            if (string.Compare(_config.Version, "1.1.1") < 0)
            {
                foreach (SupplySignalConfig supplySignal in _config.SupplySignals)
                {
                    supplySignal.DisplayName = "";
                    supplySignal.SupplySignalSkinId = 0;
                }
            }

            if (string.Compare(_config.Version, "1.3.0") < 0)
            {
                foreach (SupplySignalConfig supplySignal in _config.SupplySignals)
                {
                    supplySignal.RunRandomCommand = false;
                }
            }

            if (string.Compare(_config.Version, "1.4.0") < 0)
            {
                foreach (SupplySignalConfig supplySignal in _config.SupplySignals)
                {
                    supplySignal.DisplayName = "";
                    supplySignal.SupplySignalSkinId = 0;
                    supplySignal.CooldownSeconds = 60f;
                    supplySignal.GlobalMessage = "";
                    supplySignal.PersonalMessage = "You triggered a special supply signal, {PlayerName}!";
                }
            }

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                SupplySignals = new List<SupplySignalConfig>
                {
                    new SupplySignalConfig
                    {
                        DisplayName = "",
                        SupplySignalSkinId = 0,
                        ShouldExplode = false,
                        CooldownSeconds = 60f,
                        RunRandomCommand = false,
                        CommandsToRun = new List<CommandConfig>
                        {
                            new CommandConfig
                            {
                                Type = CommandType.Chat,
                                Command = "Hello, my name is {PlayerName} and you can find me in grid {Grid}."
                            },
                            new CommandConfig
                            {
                                Type = CommandType.Client,
                                Command = "heli.calltome"
                            },
                            new CommandConfig
                            {
                                Type = CommandType.Server,
                                Command = "inventory.giveto {PlayerId} scrap 50"
                            }
                        },
                        GlobalMessage = "",
                        PersonalMessage = "You triggered a special supply signal, {PlayerName}!"
                    }
                }
            };
        }

        #endregion Configuration

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
        }

        private void Unload()
        {
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

            SupplySignalConfig supplySignalConfig = _config.SupplySignals.FirstOrDefault(c =>
                c.SupplySignalSkinId == skinId
                && (string.IsNullOrEmpty(c.DisplayName) || c.DisplayName == name)
            );

            if (supplySignalConfig != null)
            {
                if (supplySignalConfig.CooldownSeconds > 0)
                {
                    int configIndex = _config.SupplySignals.IndexOf(supplySignalConfig);

                    if (OnCooldown(player, configIndex, supplySignalConfig.CooldownSeconds, out double remain))
                    {
                        string nicelyFormatted = FormatTime(remain);
                        MessagePlayer(player, Lang.OnCooldown, nicelyFormatted);
                        return;
                    }

                    if (!_lastUseTimes.TryGetValue(player.userID, out var dict))
                    {
                        dict = new Dictionary<int, double>();
                        _lastUseTimes[player.userID] = dict;
                    }
                    dict[configIndex] = Time.realtimeSinceStartup;
                }

                if (!supplySignalConfig.ShouldExplode)
                {
                    supplySignal.CancelInvoke(supplySignal.Explode);
                }

                if (supplySignalConfig.RunRandomCommand && supplySignalConfig.CommandsToRun.Any())
                {
                    CommandConfig randomCommand = supplySignalConfig.CommandsToRun.GetRandom();
                    RunCommand(player, randomCommand.Type, randomCommand.Command);
                }
                else
                {
                    foreach (CommandConfig commandConfig in supplySignalConfig.CommandsToRun)
                    {
                        RunCommand(player, commandConfig.Type, commandConfig.Command);
                    }
                }

                if (!string.IsNullOrEmpty(supplySignalConfig.GlobalMessage))
                {
                    string globalMsg = ReplacePlaceholders(supplySignalConfig.GlobalMessage, player);
                    foreach (var activePlayer in BasePlayer.activePlayerList)
                    {
                        MessagePlayer(activePlayer, globalMsg);
                    }
                }

                if (!string.IsNullOrEmpty(supplySignalConfig.PersonalMessage))
                {
                    string personalMsg = ReplacePlaceholders(supplySignalConfig.PersonalMessage, player);
                    MessagePlayer(player, personalMsg);
                }
            }
        }

        #endregion Oxide Hooks

        #region Helper Functions

        private bool OnCooldown(BasePlayer player, int configIndex, float cooldownSeconds, out double timeRemaining)
        { 
            timeRemaining = 0;
            if (!_lastUseTimes.TryGetValue(player.userID, out var dict))
                return false;

            if (!dict.TryGetValue(configIndex, out double lastUse))
                return false;

            double now = Time.realtimeSinceStartup;
            double nextAvail = lastUse + cooldownSeconds;
            if (now < nextAvail)
            {
                timeRemaining = nextAvail - now;
                return true;
            }
            return false;
        }

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

        #region Placeholder Replacement

        private string ReplacePlaceholders(string text, BasePlayer player)
        {
            return text
                .Replace("{PlayerId}", player.UserIDString)
                .Replace("{PlayerName}", player.displayName)
                .Replace("{PositionX}", player.transform.position.x.ToString("F1"))
                .Replace("{PositionY}", player.transform.position.y.ToString("F1"))
                .Replace("{PositionZ}", player.transform.position.z.ToString("F1"))
                .Replace("{Grid}", MapHelper.PositionToString(player.transform.position));
        }

        #endregion Placeholder Replacement

        #region Localization

        private class Lang
        {
            public const string OnCooldown = "OnCooldown";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.OnCooldown] = "You must wait {0} before the commands on this supply signal can be triggered again!",

            }, this, "en");
        }

        private static string GetMessage(BasePlayer player, string messageKey, params object[] args)
        {
            string message = _plugin.lang.GetMessage(messageKey, _plugin, player.UserIDString);
            if (args.Length > 0)
                message = string.Format(message, args);

            return message;
        }

        public static void MessagePlayer(BasePlayer player, string messageKey, params object[] args)
        {
            string message = GetMessage(player, messageKey, args);
            _plugin.SendReply(player, message);
        }

        #endregion Localization
    }
}