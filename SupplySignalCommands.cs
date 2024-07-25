/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Collections.Generic;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("Supply Signal Commands", "VisEntities", "1.2.0")]
    [Description("Run commands when a supply signal is thrown.")]
    public class SupplySignalCommands : RustPlugin
    {
        #region Fields

        private static SupplySignalCommands _plugin;
        private static Configuration _config;

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
            [JsonProperty("Item Name")]
            public string ItemName { get; set; }

            [JsonProperty("Item Skin Id")]
            public ulong ItemSkinId { get; set; }

            [JsonProperty("Should Explode")]
            public bool ShouldExplode { get; set; }

            [JsonProperty("Commands To Run")]
            public List<CommandConfig> CommandsToRun { get; set; }

            [JsonProperty("Message Sent To Player")]
            public string MessageSentToPlayer { get; set; }
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
                    supplySignal.ItemName = "";
                }
            }

            if (string.Compare(_config.Version, "1.1.1") < 0)
            {
                foreach (SupplySignalConfig supplySignal in _config.SupplySignals)
                {
                    supplySignal.ItemName = "";
                    supplySignal.ItemSkinId = 0;
                }
            }

            if (string.Compare(_config.Version, "1.2.0") < 0)
            {
                foreach (SupplySignalConfig supplySignal in _config.SupplySignals)
                {
                    supplySignal.MessageSentToPlayer = "You just threw a supply signal at {grid}. Get ready for the airdrop!";
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
                        ItemName = "",
                        ItemSkinId = 0,
                        ShouldExplode = false,
                        CommandsToRun = new List<CommandConfig>
                        {
                            new CommandConfig
                            {
                                Type = CommandType.Chat,
                                Command = "Hello, my name is {playerName} and you can find me in grid {grid}."
                            },
                            new CommandConfig
                            {
                                Type = CommandType.Client,
                                Command = "heli.calltome"
                            },
                            new CommandConfig
                            {
                                Type = CommandType.Server,
                                Command = "inventory.giveto {playerId} scrap 50"
                            }
                        },
                        MessageSentToPlayer = "You just threw a supply signal at {grid}. Get ready for the airdrop!"
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

            SupplySignalConfig supplySignalConfig = _config.SupplySignals.FirstOrDefault(c => c.ItemSkinId == skinId && (string.IsNullOrEmpty(c.ItemName) || c.ItemName == name));
            if (supplySignalConfig != null)
            {
                if (!supplySignalConfig.ShouldExplode)
                {
                    supplySignal.CancelInvoke(supplySignal.Explode);
                }

                foreach (var commandConfig in supplySignalConfig.CommandsToRun)
                {
                    RunCommand(player, commandConfig.Type, commandConfig.Command);
                }

                if (!string.IsNullOrEmpty(supplySignalConfig.MessageSentToPlayer))
                {
                    SendFormattedMessage(player, supplySignalConfig.MessageSentToPlayer);
                }
            }
        }

        #endregion Oxide Hooks

        #region Message Formatting

        private void SendFormattedMessage(BasePlayer player, string message)
        {
            string withPlaceholdersReplaced = message
                .Replace("{playerId}", player.UserIDString)
                .Replace("{playerName}", player.displayName)
                .Replace("{positionX}", player.transform.position.x.ToString())
                .Replace("{positionY}", player.transform.position.y.ToString())
                .Replace("{positionZ}", player.transform.position.z.ToString())
                .Replace("{grid}", PhoneController.PositionToGridCoord(player.transform.position));

            SendReply(player, withPlaceholdersReplaced);
        }

        #endregion Message Formatting

        #region Command Execution

        private enum CommandType
        {
            Chat,
            Server,
            Client
        }

        private void RunCommand(BasePlayer player, CommandType type, string command)
        {
            string withPlaceholdersReplaced = command
                .Replace("{playerId}", player.UserIDString)
                .Replace("{playerName}", player.displayName)
                .Replace("{positionX}", player.transform.position.x.ToString())
                .Replace("{positionY}", player.transform.position.y.ToString())
                .Replace("{positionZ}", player.transform.position.z.ToString())
                .Replace("{grid}", PhoneController.PositionToGridCoord(player.transform.position));

            if (type == CommandType.Chat)
            {
                player.Command(string.Format("chat.say \"{0}\"", withPlaceholdersReplaced));
            }
            else if (type == CommandType.Client)
            {
                player.Command(withPlaceholdersReplaced);
            }
            else if (type == CommandType.Server)
            {
                Server.Command(withPlaceholdersReplaced);
            }
        }

        #endregion Command Execution
    }
}