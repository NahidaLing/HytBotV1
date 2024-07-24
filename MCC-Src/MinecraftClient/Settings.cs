using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using MinecraftClient.Protocol;
using MinecraftClient.Proxy;
using Tomlet;
using Tomlet.Attributes;
using Tomlet.Models;
using static MinecraftClient.Settings.ChatBotConfigHealper;
using static MinecraftClient.Settings.ConsoleConfigHealper;
using static MinecraftClient.Settings.LoggingConfigHealper;
using static MinecraftClient.Settings.MainConfigHealper;
using static MinecraftClient.Settings.MainConfigHealper.MainConfig;
using static MinecraftClient.Settings.MainConfigHealper.MainConfig.AdvancedConfig;
using static MinecraftClient.Settings.MCSettingsConfigHealper;
using static MinecraftClient.Settings.SignatureConfigHelper;

namespace MinecraftClient
{
    public static class Settings
    {
        private const int CommentsAlignPosition = 45;
        private readonly static Regex CommentRegex = new(@"^(.*)\s?#\s\$([\w\.]+)\$\s*$$", RegexOptions.Compiled);

        public static GlobalConfig Config = new();

        public static class InternalConfig
        {
            public static string ServerIP = String.Empty;

            public static ushort ServerPort = 25565;

            public static AccountInfoConfig Account = new();

            public static string Username = string.Empty;

            public static string MinecraftVersion = string.Empty;

            public static bool InteractiveMode = true;

            public static bool GravityEnabled = true;

            public static bool KeepAccountSettings = false;

            public static bool KeepServerSettings = false;
        }

        public class GlobalConfig
        {
            public MainConfig Main
            {
                get { return MainConfigHealper.Config; }
                set { MainConfigHealper.Config = value; MainConfigHealper.Config.OnSettingUpdate(); }
            }

            [TomlPrecedingComment("$Signature$")]
            public SignatureConfig Signature
            {
                get { return SignatureConfigHelper.Config; }
                set { SignatureConfigHelper.Config = value; SignatureConfigHelper.Config.OnSettingUpdate(); }
            }

            [TomlPrecedingComment("$Logging$")]
            public LoggingConfig Logging
            {
                get { return LoggingConfigHealper.Config; }
                set { LoggingConfigHealper.Config = value; LoggingConfigHealper.Config.OnSettingUpdate(); }
            }

            public ConsoleConfig Console
            {
                get { return ConsoleConfigHealper.Config; }
                set { ConsoleConfigHealper.Config = value; ConsoleConfigHealper.Config.OnSettingUpdate(); }
            }

            [TomlPrecedingComment("$Proxy$")]
            public ProxyHandler.Configs Proxy
            {
                get { return ProxyHandler.Config; }
                set { ProxyHandler.Config = value; ProxyHandler.Config.OnSettingUpdate(); }
            }

            [TomlPrecedingComment("$MCSettings$")]
            public MCSettingsConfig MCSettings
            {
                get { return MCSettingsConfigHealper.Config; }
                set { MCSettingsConfigHealper.Config = value; MCSettingsConfigHealper.Config.OnSettingUpdate(); }
            }

            [TomlPrecedingComment("$ChatBot$")]
            public ChatBotConfig ChatBot
            {
                get { return ChatBotConfigHealper.Config; }
                set { ChatBotConfigHealper.Config = value; }
            }

        }

        public static Tuple<bool, bool> LoadFromFile(string filepath, bool keepAccountAndServerSettings = false)
        {
            bool keepAccountSettings = InternalConfig.KeepAccountSettings;
            bool keepServerSettings = InternalConfig.KeepServerSettings;
            if (keepAccountAndServerSettings)
                InternalConfig.KeepAccountSettings = InternalConfig.KeepServerSettings = true;

            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            TomlDocument document;
            try
            {
                document = TomlParser.ParseFile(filepath);
                Thread.CurrentThread.CurrentCulture = Program.ActualCulture;

                Config = TomletMain.To<GlobalConfig>(document);
            }
            catch (Exception ex)
            {
                Thread.CurrentThread.CurrentCulture = Program.ActualCulture;
                try
                {
                    // The old configuration file has been backed up as A.
                    string configString = File.ReadAllText(filepath);
                    if (configString.Contains("Some settings missing here after an upgrade?"))
                    {
                        string newFilePath = Path.ChangeExtension(filepath, ".old.ini");
                        File.Copy(filepath, newFilePath, true);
                        ConsoleIO.WriteLineFormatted("§c" + Translations.mcc_use_new_config);
                        ConsoleIO.WriteLineFormatted("§c" + string.Format(Translations.mcc_backup_old_config, newFilePath));
                        return new(false, true);
                    }
                }
                catch { }
                ConsoleIO.WriteLineFormatted("§c" + Translations.config_load_fail);
                ConsoleIO.WriteLine(ex.GetFullMessage());
                return new(false, false);
            }
            finally
            {
                if (!keepAccountSettings)
                    InternalConfig.KeepAccountSettings = false;
                if (!keepServerSettings)
                    InternalConfig.KeepServerSettings = false;
            }
            return new(true, false);
        }

        public static void WriteToFile(string filepath, bool backupOldFile)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            string tomlString = TomletMain.TomlStringFrom(Config);
            Thread.CurrentThread.CurrentCulture = Program.ActualCulture;

            string[] tomlList = tomlString.Split('\n');
            StringBuilder newConfig = new();
            foreach (string line in tomlList)
            {
                Match matchComment = CommentRegex.Match(line);
                if (matchComment.Success && matchComment.Groups.Count == 3)
                {
                    string config = matchComment.Groups[1].Value, comment = matchComment.Groups[2].Value;
                    if (config.Length > 0)
                        newConfig.Append(config).Append(' ', Math.Max(1, CommentsAlignPosition - config.Length) - 1);
                    string? comment_trans = ConfigComments.ResourceManager.GetString(comment);
                    if (string.IsNullOrEmpty(comment_trans))
                        newConfig.Append("# ").AppendLine(comment.ReplaceLineEndings());
                    else
                        newConfig.Append("# ").AppendLine(comment_trans.Replace("\n", "\n# ").ReplaceLineEndings());
                }
                else
                {
                    newConfig.AppendLine(line);
                }
            }

            bool needUpdate = true;
            byte[] newConfigByte = Encoding.UTF8.GetBytes(newConfig.ToString());
            if (File.Exists(filepath))
            {
                try
                {
                    if (new FileInfo(filepath).Length == newConfigByte.Length)
                        if (File.ReadAllBytes(filepath).SequenceEqual(newConfigByte))
                            needUpdate = false;
                }
                catch { }
            }

            if (needUpdate)
            {
                bool backupSuccessed = true;
                if (backupOldFile && File.Exists(filepath))
                {
                    string backupFilePath = Path.ChangeExtension(filepath, ".backup.ini");
                    try { File.Copy(filepath, backupFilePath, true); }
                    catch (Exception ex)
                    {
                        backupSuccessed = false;
                        ConsoleIO.WriteLineFormatted("§c" + string.Format(Translations.config_backup_fail, backupFilePath));
                        ConsoleIO.WriteLine(ex.Message);
                    }
                }

                if (backupSuccessed)
                {
                    try { File.WriteAllBytes(filepath, newConfigByte); }
                    catch (Exception ex)
                    {
                        ConsoleIO.WriteLine(ex.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Load settings from the command line
        /// </summary>
        /// <param name="args">Command-line arguments</param>
        /// <exception cref="System.ArgumentException">Thrown on invalid arguments</exception>
        public static void LoadArguments(string[] args)
        {
            int positionalIndex = 0;
            bool skipPassword = false;

            foreach (string argument in args)
            {
                if (argument.StartsWith("--"))
                {
                    //Load settings as --setting=value and --section.setting=value
                    if (!argument.Contains('='))
                        throw new ArgumentException(string.Format(Translations.error_setting_argument_syntax, argument));
                    throw new NotImplementedException();
                }
                else if (argument.StartsWith("-") && argument.Length > 1)
                {
                    //Keep single dash arguments as unsupported for now (future use)
                    throw new ArgumentException(string.Format(Translations.error_setting_argument_syntax, argument));
                }
                else
                {
                    switch (positionalIndex)
                    {
                        case 0:
                            InternalConfig.Account.Login = argument;
                            InternalConfig.KeepAccountSettings = true;
                            break;
                        case 1:
                            if (!skipPassword)
                                InternalConfig.Account.Password = argument;
                            break;
                        case 2:
                            Config.Main.SetServerIP(new MainConfig.ServerInfoConfig(argument));
                            InternalConfig.KeepServerSettings = true;
                            break;
                        case 3:
                            // SingleCommand = argument; 
                            break;
                    }
                    positionalIndex++;
                }
            }
        }
        
        public static class MainConfigHealper
        {
            public static MainConfig Config = new();

            [TomlDoNotInlineObject]
            public class MainConfig
            {
                public GeneralConfig General = new();

                [TomlPrecedingComment("$Main.Advanced$")]
                public AdvancedConfig Advanced = new();


                [NonSerialized]
                public static readonly string[] AvailableLang =
                {
                    "af_za", "ar_sa", "ast_es", "az_az", "ba_ru", "bar", "be_by", "bg_bg",
                    "br_fr", "brb", "bs_ba", "ca_es", "cs_cz", "cy_gb", "da_dk", "de_at",
                    "de_ch", "de_de", "el_gr", "en_au", "en_ca", "en_gb", "en_nz", "en_pt",
                    "en_ud", "en_us", "enp", "enws", "eo_uy", "es_ar", "es_cl", "es_ec",
                    "es_es", "es_mx", "es_uy", "es_ve", "esan", "et_ee", "eu_es", "fa_ir",
                    "fi_fi", "fil_ph", "fo_fo", "fr_ca", "fr_fr", "fra_de", "fur_it", "fy_nl",
                    "ga_ie", "gd_gb", "gl_es", "haw_us", "he_il", "hi_in", "hr_hr", "hu_hu",
                    "hy_am", "id_id", "ig_ng", "io_en", "is_is", "isv", "it_it", "ja_jp",
                    "jbo_en", "ka_ge", "kk_kz", "kn_in", "ko_kr", "ksh", "kw_gb", "la_la",
                    "lb_lu", "li_li", "lmo", "lol_us", "lt_lt", "lv_lv", "lzh", "mk_mk",
                    "mn_mn", "ms_my", "mt_mt", "nah", "nds_de", "nl_be", "nl_nl", "nn_no",
                    "no_no", "oc_fr", "ovd", "pl_pl", "pt_br", "pt_pt", "qya_aa", "ro_ro",
                    "rpr", "ru_ru", "ry_ua", "se_no", "sk_sk", "sl_si", "so_so", "sq_al",
                    "sr_sp", "sv_se", "sxu", "szl", "ta_in", "th_th", "tl_ph", "tlh_aa",
                    "tok", "tr_tr", "tt_ru", "uk_ua", "val_es", "vec_it", "vi_vn", "yi_de",
                    "yo_ng", "zh_cn", "zh_hk", "zh_tw", "zlm_arab"
                };

                /// <summary>
                /// Load server information in ServerIP and ServerPort variables from a "serverip:port" couple or server alias
                /// </summary>
                /// <returns>True if the server IP was valid and loaded, false otherwise</returns>
                public bool SetServerIP(ServerInfoConfig serverInfo)
                {
                    string[] sip = serverInfo.Host.Split(new[] { ":", "：" }, StringSplitOptions.None);
                    string host = ToLowerIfNeed(sip[0]);
                    ushort port = 25565;

                    if (serverInfo.Port.HasValue)
                    {
                        port = serverInfo.Port.Value;
                    }
                    else if (sip.Length > 1)
                    {
                        try { port = Convert.ToUInt16(sip[1]); }
                        catch (FormatException) { return false; }
                    }

                    if (host == "localhost" || host.Contains('.'))
                    {
                        //Server IP (IP or domain names contains at least a dot)
                        if (sip.Length == 1 && !serverInfo.Port.HasValue && host.Contains('.') && host.Any(c => char.IsLetter(c)) &&
                            Settings.Config.Main.Advanced.ResolveSrvRecords != ResolveSrvRecordType.no)
                            //Domain name without port may need Minecraft SRV Record lookup
                            ProtocolHandler.MinecraftServiceLookup(ref host, ref port);
                        InternalConfig.ServerIP = host;
                        InternalConfig.ServerPort = port;
                        return true;
                    }

                    return false;
                }

                public void OnSettingUpdate()
                {
                    ConsoleIO.EnableTimestamps = Advanced.Timestamps;

                    InternalConfig.InteractiveMode = !Advanced.ExitOnFailure;

                    General.Account.Login ??= string.Empty;
                    General.Account.Password ??= string.Empty;
                    if (!InternalConfig.KeepAccountSettings)
                    {
                        InternalConfig.Account = General.Account;
                    }

                    General.Server.Host ??= string.Empty;

                    if (Advanced.MessageCooldown < 0)
                        Advanced.MessageCooldown = 0;

                    if (Advanced.TcpTimeout < 1)
                        Advanced.TcpTimeout = 1;

                    if (Advanced.MovementSpeed < 1)
                        Advanced.MovementSpeed = 1;

                    if (!InternalConfig.KeepServerSettings)
                    {
                        if (!string.IsNullOrWhiteSpace(General.Server.Host))
                        {
                            string[] sip = General.Server.Host.Split(new[] { ":", "：" }, StringSplitOptions.None);
                            General.Server.Host = sip[0];
                            InternalConfig.ServerIP = General.Server.Host;

                            if (sip.Length > 1)
                            {
                                try { General.Server.Port = Convert.ToUInt16(sip[1]); }
                                catch (FormatException) { }
                            }
                        }

                        if (General.Server.Port.HasValue)
                            InternalConfig.ServerPort = General.Server.Port.Value;
                        else
                            SetServerIP(General.Server);
                    }

                    if (Advanced.MinTerminalWidth < 1)
                        Advanced.MinTerminalWidth = 1;
                    if (Advanced.MinTerminalHeight < 1)
                        Advanced.MinTerminalHeight = 1;

                    if (Advanced.TemporaryFixBadpacket && !Advanced.TerrainAndMovements)
                    {
                        Advanced.TerrainAndMovements = true;
                        ConsoleIO.WriteLineFormatted("§c[Settings]You need to enable TerrainAndMovements before enabling TemporaryFixBadpacket.");
                    }
                }

                [TomlDoNotInlineObject]
                public class GeneralConfig
                {
                    [TomlInlineComment("$Main.General.account$")]
                    public AccountInfoConfig Account = new(string.Empty, string.Empty);

                    [TomlInlineComment("$Main.General.login$")]
                    public ServerInfoConfig Server = new(string.Empty);

                    [TomlInlineComment("$Main.General.server_info$")]
                    public LoginType AccountType = LoginType.microsoft;

                    [TomlInlineComment("$Main.General.method$")]
                    public LoginMethod Method = LoginMethod.mcc;

                    public enum LoginType { mojang, microsoft };

                    public enum LoginMethod { mcc, browser };
                }

                [TomlDoNotInlineObject]
                public class AdvancedConfig
                {
                    [TomlInlineComment("$Main.Advanced.internal_cmd_char$")]
                    public InternalCmdCharType InternalCmdChar = InternalCmdCharType.slash;

                    [TomlInlineComment("$Main.Advanced.message_cooldown$")]
                    public double MessageCooldown = 1.0;

                    [TomlInlineComment("$Main.Advanced.mc_version$")]
                    public string MinecraftVersion = "auto";

                    [TomlInlineComment("$Main.Advanced.mc_forge$")]
                    public ForgeConfigType EnableForge = ForgeConfigType.no;

                    [TomlInlineComment("$Main.Advanced.brand_info$")]
                    public BrandInfoType BrandInfo = BrandInfoType.mcc;

                    [TomlInlineComment("$Main.Advanced.chatbot_log_file$")]
                    public string ChatbotLogFile = "";

                    [TomlInlineComment("$Main.Advanced.private_msgs_cmd_name$")]
                    public string PrivateMsgsCmdName = "tell";

                    [TomlInlineComment("$Main.Advanced.show_system_messages$")]
                    public bool ShowSystemMessages = true;

                    [TomlInlineComment("$Main.Advanced.show_xpbar_messages$")]
                    public bool ShowXPBarMessages = true;

                    [TomlInlineComment("$Main.Advanced.show_chat_links$")]
                    public bool ShowChatLinks = true;

                    [TomlInlineComment("$Main.Advanced.show_inventory_layout$")]
                    public bool ShowInventoryLayout = true;

                    [TomlInlineComment("$Main.Advanced.terrain_and_movements$")]
                    public bool TerrainAndMovements = false;

                    [TomlInlineComment("$Main.Advanced.move_head_while_walking$")]
                    public bool MoveHeadWhileWalking = true;

                    [TomlInlineComment("$Main.Advanced.movement_speed$")]
                    public int MovementSpeed = 2;

                    [TomlInlineComment("$Main.Advanced.temporary_fix_badpacket$")]
                    public bool TemporaryFixBadpacket = false;

                    [TomlInlineComment("$Main.Advanced.inventory_handling$")]
                    public bool InventoryHandling = false;

                    [TomlInlineComment("$Main.Advanced.entity_handling$")]
                    public bool EntityHandling = false;

                    [TomlInlineComment("$Main.Advanced.session_cache$")]
                    public CacheType SessionCache = CacheType.disk;

                    [TomlInlineComment("$Main.Advanced.profilekey_cache$")]
                    public CacheType ProfileKeyCache = CacheType.disk;

                    [TomlInlineComment("$Main.Advanced.resolve_srv_records$")]
                    public ResolveSrvRecordType ResolveSrvRecords = ResolveSrvRecordType.fast;

                    [TomlInlineComment("$Main.Advanced.exit_on_failure$")]
                    public bool ExitOnFailure = false;

                    [TomlInlineComment("$Main.Advanced.script_cache$")]
                    public bool CacheScript = true;

                    [TomlInlineComment("$Main.Advanced.timestamps$")]
                    public bool Timestamps = false;

                    [TomlInlineComment("$Main.Advanced.auto_respawn$")]
                    public bool AutoRespawn = false;

                    [TomlInlineComment("$Main.Advanced.timeout$")]
                    public int TcpTimeout = 30;

                    [TomlInlineComment("$Main.Advanced.enable_emoji$")]
                    public bool EnableEmoji = true;

                    [TomlInlineComment("$Main.Advanced.MinTerminalWidth$")]
                    public int MinTerminalWidth = 16;

                    [TomlInlineComment("$Main.Advanced.MinTerminalHeight$")]
                    public int MinTerminalHeight = 10;

                    [TomlInlineComment("$Main.Advanced.ignore_invalid_playername$")]
                    public bool IgnoreInvalidPlayerName = true;

                    public enum InternalCmdCharType { none, slash, backslash };

                    public enum BrandInfoType { mcc, vanilla, empty };

                    public enum CacheType { none, memory, disk };

                    public enum ResolveSrvRecordType { no, fast, yes };

                    public enum ForgeConfigType { no, auto, force };
                }

                public struct AccountInfoConfig
                {
                    public string Login = string.Empty, Password = string.Empty;

                    public AccountInfoConfig(string Login)
                    {
                        this.Login = Login;
                        this.Password = "-";
                    }

                    public AccountInfoConfig(string Login, string Password)
                    {
                        this.Login = Login;
                        this.Password = Password;
                    }
                }

                public struct ServerInfoConfig
                {
                    public string Host = string.Empty;
                    public ushort? Port = null;

                    public ServerInfoConfig(string Host)
                    {
                        string[] sip = Host.Split(new[] { ":", "：" }, StringSplitOptions.None);
                        this.Host = sip[0];

                        if (sip.Length > 1)
                        {
                            try { this.Port = Convert.ToUInt16(sip[1]); }
                            catch (FormatException) { }
                        }
                    }

                    public ServerInfoConfig(string Host, ushort Port)
                    {
                        this.Host = Host.Split(new[] { ":", "：" }, StringSplitOptions.None)[0];
                        this.Port = Port;
                    }
                }
            }
        }

        public static class SignatureConfigHelper
        {
            public static SignatureConfig Config = new();

            [TomlDoNotInlineObject]
            public class SignatureConfig
            {
                [TomlInlineComment("$Signature.LoginWithSecureProfile$")]
                public bool LoginWithSecureProfile = true;

                [TomlInlineComment("$Signature.SignChat$")]
                public bool SignChat = true;

                [TomlInlineComment("$Signature.SignMessageInCommand$")]
                public bool SignMessageInCommand = true;

                [TomlInlineComment("$Signature.MarkLegallySignedMsg$")]
                public bool MarkLegallySignedMsg = true;

                [TomlInlineComment("$Signature.MarkModifiedMsg$")]
                public bool MarkModifiedMsg = true;

                [TomlInlineComment("$Signature.MarkIllegallySignedMsg$")]
                public bool MarkIllegallySignedMsg = true;

                [TomlInlineComment("$Signature.MarkSystemMessage$")]
                public bool MarkSystemMessage = true;

                [TomlInlineComment("$Signature.ShowModifiedChat$")]
                public bool ShowModifiedChat = true;

                [TomlInlineComment("$Signature.ShowIllegalSignedChat$")]
                public bool ShowIllegalSignedChat = true;

                public void OnSettingUpdate() { }
            }
        }

        public static class LoggingConfigHealper
        {
            public static LoggingConfig Config = new();

            [TomlDoNotInlineObject]
            public class LoggingConfig
            {
                [TomlInlineComment("$Logging.DebugMessages$")]
                public bool DebugMessages = false;

                [TomlInlineComment("$Logging.ChatMessages$")]
                public bool ChatMessages = true;

                [TomlInlineComment("$Logging.InfoMessages$")]
                public bool InfoMessages = true;

                [TomlInlineComment("$Logging.WarningMessages$")]
                public bool WarningMessages = true;

                [TomlInlineComment("$Logging.ErrorMessages$")]
                public bool ErrorMessages = true;

                [TomlInlineComment("$Logging.ChatFilter$")]
                public string ChatFilterRegex = @".*";

                [TomlInlineComment("$Logging.DebugFilter$")]
                public string DebugFilterRegex = @".*";

                [TomlInlineComment("$Logging.FilterMode$")]
                public FilterModeEnum FilterMode = FilterModeEnum.disable;

                [TomlInlineComment("$Logging.LogToFile$")]
                public bool LogToFile = false;

                [TomlInlineComment("$Logging.LogFile$")]
                public string LogFile = @"console-log.txt";

                [TomlInlineComment("$Logging.PrependTimestamp$")]
                public bool PrependTimestamp = false;

                [TomlInlineComment("$Logging.SaveColorCodes$")]
                public bool SaveColorCodes = false;

                public void OnSettingUpdate() { }

                public enum FilterModeEnum { disable, blacklist, whitelist }
            }
        }

        public static class ConsoleConfigHealper
        {
            public static ConsoleConfig Config = new();

            [TomlDoNotInlineObject]
            public class ConsoleConfig
            {
                public MainConfig General = new();

                [TomlPrecedingComment("$Console.CommandSuggestion$")]
                public CommandSuggestionConfig CommandSuggestion = new();

                public void OnSettingUpdate()
                {
                    // Reader
                    ConsoleInteractive.ConsoleReader.DisplayUesrInput = General.Display_Input;

                    // Writer
                    ConsoleInteractive.ConsoleWriter.EnableColor = General.ConsoleColorMode != ConsoleColorModeType.disable;

                    ConsoleInteractive.ConsoleWriter.UseVT100ColorCode = General.ConsoleColorMode != ConsoleColorModeType.legacy_4bit;

                    // Buffer
                    General.History_Input_Records =
                        ConsoleInteractive.ConsoleBuffer.SetBackreadBufferLimit(General.History_Input_Records);

                    // Suggestion
                    if (General.ConsoleColorMode == ConsoleColorModeType.disable)
                        CommandSuggestion.Enable_Color = false;

                    ConsoleInteractive.ConsoleSuggestion.EnableColor = CommandSuggestion.Enable_Color;

                    ConsoleInteractive.ConsoleSuggestion.Enable24bitColor = General.ConsoleColorMode == ConsoleColorModeType.vt100_24bit;

                    ConsoleInteractive.ConsoleSuggestion.UseBasicArrow = CommandSuggestion.Use_Basic_Arrow;

                    CommandSuggestion.Max_Suggestion_Width =
                        ConsoleInteractive.ConsoleSuggestion.SetMaxSuggestionLength(CommandSuggestion.Max_Suggestion_Width);

                    CommandSuggestion.Max_Displayed_Suggestions =
                        ConsoleInteractive.ConsoleSuggestion.SetMaxSuggestionCount(CommandSuggestion.Max_Displayed_Suggestions);

                    // Suggestion color settings
                    {
                        if (!CheckColorCode(CommandSuggestion.Text_Color))
                        {
                            ConsoleIO.WriteLine(string.Format(Translations.config_commandsuggestion_illegal_color, "CommandSuggestion.TextColor", CommandSuggestion.Text_Color));
                            CommandSuggestion.Text_Color = "#f8fafc";
                        }
                        if (!CheckColorCode(CommandSuggestion.Text_Background_Color))
                        {
                            ConsoleIO.WriteLine(string.Format(Translations.config_commandsuggestion_illegal_color, "CommandSuggestion.TextBackgroundColor", CommandSuggestion.Text_Background_Color));
                            CommandSuggestion.Text_Background_Color = "#64748b";
                        }
                        if (!CheckColorCode(CommandSuggestion.Highlight_Text_Color))
                        {
                            ConsoleIO.WriteLine(string.Format(Translations.config_commandsuggestion_illegal_color, "CommandSuggestion.HighlightTextColor", CommandSuggestion.Highlight_Text_Color));
                            CommandSuggestion.Highlight_Text_Color = "#334155";
                        }
                        if (!CheckColorCode(CommandSuggestion.Highlight_Text_Background_Color))
                        {
                            ConsoleIO.WriteLine(string.Format(Translations.config_commandsuggestion_illegal_color, "CommandSuggestion.HighlightTextBackgroundColor", CommandSuggestion.Highlight_Text_Background_Color));
                            CommandSuggestion.Highlight_Text_Background_Color = "#fde047";
                        }
                        if (!CheckColorCode(CommandSuggestion.Tooltip_Color))
                        {
                            ConsoleIO.WriteLine(string.Format(Translations.config_commandsuggestion_illegal_color, "CommandSuggestion.TooltipColor", CommandSuggestion.Tooltip_Color));
                            CommandSuggestion.Tooltip_Color = "#7dd3fc";
                        }
                        if (!CheckColorCode(CommandSuggestion.Highlight_Tooltip_Color))
                        {
                            ConsoleIO.WriteLine(string.Format(Translations.config_commandsuggestion_illegal_color, "CommandSuggestion.HighlightTooltipColor", CommandSuggestion.Highlight_Tooltip_Color));
                            CommandSuggestion.Highlight_Tooltip_Color = "#3b82f6";
                        }
                        if (!CheckColorCode(CommandSuggestion.Arrow_Symbol_Color))
                        {
                            ConsoleIO.WriteLine(string.Format(Translations.config_commandsuggestion_illegal_color, "CommandSuggestion.ArrowSymbolColor", CommandSuggestion.Arrow_Symbol_Color));
                            CommandSuggestion.Arrow_Symbol_Color = "#d1d5db";
                        }

                        ConsoleInteractive.ConsoleSuggestion.SetColors(
                            CommandSuggestion.Text_Color, CommandSuggestion.Text_Background_Color,
                            CommandSuggestion.Highlight_Text_Color, CommandSuggestion.Highlight_Text_Background_Color,
                            CommandSuggestion.Tooltip_Color, CommandSuggestion.Highlight_Tooltip_Color,
                            CommandSuggestion.Arrow_Symbol_Color);
                    }
                }

                private static bool CheckColorCode(string? input)
                {
                    if (string.IsNullOrWhiteSpace(input))
                        return false;
                    if (input.Length < 6 || input.Length > 7)
                        return false;
                    if (input.Length == 6 && input[0] == '#')
                        return false;
                    if (input.Length == 7 && input[0] != '#')
                        return false;
                    try
                    {
                        Convert.ToInt32(input.Length == 7 ? input[1..] : input, 16);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }

                [TomlDoNotInlineObject]
                public class MainConfig
                {
                    [TomlInlineComment("$Console.General.ConsoleColorMode$")]
                    public ConsoleColorModeType ConsoleColorMode = ConsoleColorModeType.vt100_24bit;

                    [TomlInlineComment("$Console.General.Display_Input$")]
                    public bool Display_Input = true;

                    [TomlInlineComment("$Console.General.History_Input_Records$")]
                    public int History_Input_Records = 32;
                }

                [TomlDoNotInlineObject]
                public class CommandSuggestionConfig
                {
                    [TomlInlineComment("$Console.CommandSuggestion.Enable$")]
                    public bool Enable = true;

                    public bool Enable_Color = true;

                    [TomlInlineComment("$Console.CommandSuggestion.Use_Basic_Arrow$")]
                    public bool Use_Basic_Arrow = false;

                    public int Max_Suggestion_Width = 30;

                    public int Max_Displayed_Suggestions = 6;

                    public string Text_Color = "#f8fafc";

                    public string Text_Background_Color = "#64748b";

                    public string Highlight_Text_Color = "#334155";

                    public string Highlight_Text_Background_Color = "#fde047";

                    public string Tooltip_Color = "#7dd3fc";

                    public string Highlight_Tooltip_Color = "#3b82f6";

                    public string Arrow_Symbol_Color = "#d1d5db";
                }

                public enum ConsoleColorModeType { disable, legacy_4bit, vt100_4bit, vt100_8bit, vt100_24bit };
            }
        }
        

        public static class MCSettingsConfigHealper
        {
            public static MCSettingsConfig Config = new();

            [TomlDoNotInlineObject]
            public class MCSettingsConfig
            {
                [TomlInlineComment("$MCSettings.Enabled$")]
                public bool Enabled = true;

                [TomlInlineComment("$MCSettings.Locale$")]
                public string Locale = "en_US";

                [TomlInlineComment("$MCSettings.RenderDistance$")]
                public byte RenderDistance = 8;

                [TomlInlineComment("$MCSettings.Difficulty$")]
                public DifficultyType Difficulty = DifficultyType.peaceful;

                [TomlInlineComment("$MCSettings.ChatMode$")]
                public ChatModeType ChatMode = ChatModeType.enabled;

                [TomlInlineComment("$MCSettings.ChatColors$")]
                public bool ChatColors = true;

                [TomlInlineComment("$MCSettings.MainHand$")]
                public MainHandType MainHand = MainHandType.left;

                public SkinInfo Skin = new();

                public void OnSettingUpdate() { }

                public enum DifficultyType { peaceful, easy, normal, difficult };

                public enum ChatModeType { enabled, commands, disabled };

                public enum MainHandType { left, right };

                public struct SkinInfo
                {
                    public bool Cape = true, Hat = true, Jacket = false;
                    public bool Sleeve_Left = false, Sleeve_Right = false;
                    public bool Pants_Left = false, Pants_Right = false;

                    public SkinInfo() { }

                    public SkinInfo(bool Cape, bool Hat, bool Jacket, bool Sleeve_Left, bool Sleeve_Right, bool Pants_Left, bool Pants_Right)
                    {
                        this.Cape = Cape;
                        this.Hat = Hat;
                        this.Jacket = Jacket;
                        this.Sleeve_Left = Sleeve_Left;
                        this.Sleeve_Right = Sleeve_Right;
                        this.Pants_Left = Pants_Left;
                        this.Pants_Right = Pants_Right;
                    }

                    public byte GetByte()
                    {
                        return (byte)(
                              ((Cape ? 1 : 0) << 0)
                            | ((Jacket ? 1 : 0) << 1)
                            | ((Sleeve_Left ? 1 : 0) << 2)
                            | ((Sleeve_Right ? 1 : 0) << 3)
                            | ((Pants_Left ? 1 : 0) << 4)
                            | ((Pants_Right ? 1 : 0) << 5)
                            | ((Hat ? 1 : 0) << 6)
                        );
                    }
                }
            }
        }

        public static class ChatBotConfigHealper
        {
            public static ChatBotConfig Config = new();

            [TomlDoNotInlineObject]
            public class ChatBotConfig
            {
                [TomlPrecedingComment("$ChatBot.AutoRelog$")]
                public ChatBots.AutoRelog.Configs AutoRelog
                {
                    get { return ChatBots.AutoRelog.Config; }
                    set { ChatBots.AutoRelog.Config = value; ChatBots.AutoRelog.Config.OnSettingUpdate(); }
                }

                [TomlPrecedingComment("$ChatBot.ChatLog$")]
                public ChatBots.ChatLog.Configs ChatLog
                {
                    get { return ChatBots.ChatLog.Config; }
                    set { ChatBots.ChatLog.Config = value; ChatBots.ChatLog.Config.OnSettingUpdate(); }
                }


                [TomlPrecedingComment("$ChatBot.Map$")]
                public ChatBots.Map.Configs Map
                {
                    get { return ChatBots.Map.Config; }
                    set { ChatBots.Map.Config = value; ChatBots.Map.Config.OnSettingUpdate(); }
                }
              
                [TomlPrecedingComment("$ChatBot.WebSocketBot$")]
                public ChatBots.WebSocketBot.Configs WebSocketBot
                {
                    get { return ChatBots.WebSocketBot.Config!; }
                    set { ChatBots.WebSocketBot.Config = value; }
                }
            }
        }

        public static string GetDefaultGameLanguage()
        {
            return "en_us";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static string ToLowerIfNeed(string str)
        {
            const string lookupStringL = "---------------------------------!-#$%&-()*+,-./0123456789:;<=>?@abcdefghijklmnopqrstuvwxyz[-]^_`abcdefghijklmnopqrstuvwxyz{|}~-";

            bool needLower = false;
            foreach (Char c in str)
            {
                if (Char.IsUpper(c))
                {
                    needLower = true;
                    break;
                }
            }

            if (needLower)
            {
                StringBuilder sb = new(str);
                for (int i = 0; i < str.Length; ++i)
                    if (char.IsUpper(sb[i]))
                        sb[i] = lookupStringL[sb[i]];
                return sb.ToString();
            }
            else
            {
                return str;
            }
        }

        public static int DoubleToTick(double time)
        {
            time = Math.Min(int.MaxValue / 10, time);
            return (int)Math.Round(time * 10);
        }
    }

    public static class InternalCmdCharTypeExtensions
    {
        public static char ToChar(this InternalCmdCharType type)
        {
            return type switch
            {
                InternalCmdCharType.none => ' ',
                InternalCmdCharType.slash => '/',
                InternalCmdCharType.backslash => '\\',
                _ => '/',
            };
        }

        public static string ToLogString(this InternalCmdCharType type)
        {
            return type switch
            {
                InternalCmdCharType.none => string.Empty,
                InternalCmdCharType.slash => @"/",
                InternalCmdCharType.backslash => @"\",
                _ => @"/",
            };
        }
    }

    public static class BrandInfoTypeExtensions
    {
        public static string? ToBrandString(this BrandInfoType info)
        {
            return info switch
            {
                BrandInfoType.mcc => "Minecraft-Console-Client/" + Program.Version,
                BrandInfoType.vanilla => "vanilla",
                BrandInfoType.empty => null,
                _ => null,
            };
        }
    }

    public static class ExceptionExtensions
    {
        public static string GetFullMessage(this Exception ex)
        {
            string msg = ex.Message.Replace("+", "->");
            return ex.InnerException == null
                 ? msg
                 : msg + "\n --> " + ex.InnerException.GetFullMessage();
        }
    }
}
