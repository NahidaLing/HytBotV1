using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using MinecraftClient.Scripting.DynamicRun.Builder;
using static MinecraftClient.Settings;

namespace MinecraftClient.Scripting
{
    /// <summary>
    /// C# Script runner - Compile on-the-fly and run C# scripts
    /// </summary>
    class CSharpRunner
    {
        private static readonly Dictionary<ulong, byte[]> CompileCache = new();

        /// <summary>
        /// Run the specified C# script file
        /// </summary>
        /// <param name="apiHandler">ChatBot handler for accessing ChatBot API</param>
        /// <param name="lines">Lines of the script file to run</param>
        /// <param name="args">Arguments to pass to the script</param>
        /// <param name="localVars">Local variables passed along with the script</param>
        /// <param name="run">Set to false to compile and cache the script without launching it</param>
        /// <exception cref="CSharpException">Thrown if an error occured</exception>
        /// <returns>Result of the execution, returned by the script</returns>
        public static object? Run(ChatBot apiHandler, string[] lines, string[] args, bool run = true, string scriptName = "Unknown Script")
        {
            //Script compatibility check for handling future versions differently
            if (lines.Length < 1 || lines[0] != "//MCCScript 1.0")
                throw new CSharpException(CSErrorType.InvalidScript,
                    new InvalidDataException(Translations.exception_csrunner_invalid_head));

            //Script hash for determining if it was previously compiled
            ulong scriptHash = QuickHash(lines);
            byte[]? assembly = null;

            Compiler compiler = new();
            CompileRunner runner = new();

            //No need to compile two scripts at the same time
            lock (CompileCache)
            {
                ///Process and compile script only if not already compiled
                if (!Config.Main.Advanced.CacheScript || !CompileCache.ContainsKey(scriptHash))
                {
                    //Process different sections of the script file
                    bool scriptMain = true;
                    List<string> script = new();
                    List<string> extensions = new();
                    List<string> libs = new();
                    List<string> dlls = new();
                    foreach (string line in lines)
                    {
                        if (line.StartsWith("//using"))
                        {
                            libs.Add(line.Replace("//", "").Trim());
                        }
                        else if (line.StartsWith("//dll"))
                        {
                            dlls.Add(line.Replace("//dll ", "").Trim());
                        }
                        else if (line.StartsWith("//MCCScript"))
                        {
                            if (line.EndsWith("Extensions"))
                                scriptMain = false;
                        }
                        else if (scriptMain)
                            script.Add(line);
                        else extensions.Add(line);
                    }

                    //Add return statement if missing
                    if (script.All(line => !line.StartsWith("return ") && !line.Contains(" return ")))
                        script.Add("return null;");

                    //Generate a class from the given script
                    string code = string.Join("\n", new string[]
                    {
                        "using System;",
                        "using System.Collections.Generic;",
                        "using System.Text.RegularExpressions;",
                        "using System.Linq;",
                        "using System.Text;",
                        "using System.IO;",
                        "using System.Net;",
                        "using System.Threading;",
                        "using MinecraftClient;",
                        "using MinecraftClient.Scripting;",
                        "using MinecraftClient.Mapping;",
                        "using MinecraftClient.Inventory;",
                        string.Join("\n", libs),
                        "namespace ScriptLoader {",
                        "public class Script {",
                        "public CSharpAPI MCC;",
                        "public object __run(CSharpAPI __apiHandler, string[] args) {",
                            "this.MCC = __apiHandler;",
                            string.Join("\n", script),
                        "}",
                            string.Join("\n", extensions),
                        "}}",
                    });

                    ConsoleIO.WriteLogLine($"[Script] Starting compilation for {scriptName}...");

                    //Compile the C# class in memory using all the currently loaded assemblies
                    var result = compiler.Compile(code, Guid.NewGuid().ToString(), dlls);

                    //Process compile warnings and errors
                    if (result.Failures != null)
                    {

                        ConsoleIO.WriteLogLine("[Script] Compilation failed with error(s):");

                        foreach (var failure in result.Failures)
                        {
                            ConsoleIO.WriteLogLine($"[Script] Error in {scriptName}, line:col{failure.Location.GetMappedLineSpan()}: [{failure.Id}] {failure.GetMessage()}");
                        }

                        throw new CSharpException(CSErrorType.InvalidScript, new InvalidProgramException("Compilation failed due to error."));
                    }

                    ConsoleIO.WriteLogLine("[Script] Compilation done with no errors.");

                    //Retrieve compiled assembly
                    assembly = result.Assembly;
                    if (Config.Main.Advanced.CacheScript)
                        CompileCache[scriptHash] = assembly!;
                }
                else if (Config.Main.Advanced.CacheScript)
                    assembly = CompileCache[scriptHash];
            }

            //Run the compiled assembly with exception handling
            if (run)
            {
                try
                {
                    var compiled = runner.Execute(assembly!, args, apiHandler);
                    return compiled;
                }
                catch (Exception e) { throw new CSharpException(CSErrorType.RuntimeError, e); }
            }
            else return null;
        }

        /// <summary>
        /// Quickly calculate a hash for the given script
        /// </summary>
        /// <param name="lines">script lines</param>
        /// <returns>Quick hash as unsigned long</returns>
        private static ulong QuickHash(string[] lines)
        {
            ulong hashedValue = 3074457345618258791ul;
            for (int i = 0; i < lines.Length; i++)
            {
                for (int j = 0; j < lines[i].Length; j++)
                {
                    hashedValue += lines[i][j];
                    hashedValue *= 3074457345618258799ul;
                }
                hashedValue += '\n';
                hashedValue *= 3074457345618258799ul;
            }
            return hashedValue;
        }
    }

    /// <summary>
    /// Describe a C# script error type
    /// </summary>
    public enum CSErrorType { FileReadError, InvalidScript, LoadError, RuntimeError };

    /// <summary>
    /// Describe a C# script error with associated error type
    /// </summary>
    public class CSharpException : Exception
    {
        private readonly CSErrorType _type;
        public CSErrorType ExceptionType { get { return _type; } }
        public override string Message { get { return InnerException!.Message; } }
        public override string ToString() { return InnerException!.ToString(); }
        public CSharpException(CSErrorType type, Exception inner)
            : base(inner != null ? inner.Message : "", inner)
        {
            _type = type;
        }
    }

    /// <summary>
    /// Represents the C# API object accessible from C# Scripts
    /// </summary>
    public class CSharpAPI : ChatBot
    {
        /// <summary>
        /// Create a new C# API Wrapper
        /// </summary>
        /// <param name="apiHandler">ChatBot API Handler</param>
        /// <param name="tickHandler">ChatBot tick handler</param>
        public CSharpAPI(ChatBot apiHandler)
        {
            SetMaster(apiHandler);
        }

        /* == Wrappers for ChatBot API with public visibility and call limit to one per tick for safety == */

        /// <summary>
        /// Write some text in the console. Nothing will be sent to the server.
        /// </summary>
        /// <param name="text">Log text to write</param>
        new public void LogToConsole(object text)
        {
            base.LogToConsole(text);
        }

        /// <summary>
        /// Send text to the server. Can be anything such as chat messages or commands
        /// </summary>
        /// <param name="text">Text to send to the server</param>
        /// <returns>TRUE if successfully sent (Deprectated, always returns TRUE for compatibility purposes with existing scripts)</returns>
        public bool SendText(object text)
        {
            return base.SendText(text is string str ? str : text.ToString() ?? string.Empty);
        }

        /// <summary>
        /// Perform an internal MCC command (not a server command, use SendText() instead for that!)
        /// </summary>
        /// <param name="command">The command to process</param>
        /// <returns>TRUE if the command was indeed an internal MCC command</returns>
        new public bool PerformInternalCommand(string command)
        {
            return base.PerformInternalCommand(command);
        }

        new public void ReconnectToTheServer(int extraAttempts = -999999, int delaySeconds = 0, bool keepAccountAndServerSettings = false)
        {
            if (extraAttempts == -999999)
                base.ReconnectToTheServer(delaySeconds: delaySeconds, keepAccountAndServerSettings: keepAccountAndServerSettings);
            else
                base.ReconnectToTheServer(extraAttempts, delaySeconds, keepAccountAndServerSettings);
        }

        /// <summary>
        /// Disconnect from the server and exit the program
        /// </summary>
        new public void DisconnectAndExit()
        {
            base.DisconnectAndExit();
        }

        /// <summary>
        /// Load the provided ChatBot object
        /// </summary>
        /// <param name="bot">Bot to load</param>
        new public void LoadBot(ChatBot bot)
        {
            base.LoadBot(bot);
        }

        /// <summary>
        /// Return the list of currently online players
        /// </summary>
        /// <returns>List of online players</returns>
        new public string[] GetOnlinePlayers()
        {
            return base.GetOnlinePlayers();
        }

        /// <summary>
        /// Get a dictionary of online player names and their corresponding UUID
        /// </summary>
        /// <returns>
        ///     dictionary of online player whereby
        ///     UUID represents the key
        ///     playername represents the value</returns>
        new public Dictionary<string, string> GetOnlinePlayersWithUUID()
        {
            return base.GetOnlinePlayersWithUUID();
        }

        /// <summary>
        /// Synchronously call another script and retrieve the result
        /// </summary>
        /// <param name="script">Script to call</param>
        /// <param name="args">Arguments to pass to the script</param>
        /// <returns>An object returned by the script, or null</returns>
        public object? CallScript(string script, string[] args)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(script, Encoding.UTF8);
            }
            catch (Exception e)
            {
                throw new CSharpException(CSErrorType.FileReadError, e);
            }
            return CSharpRunner.Run(this, lines, args, scriptName: script);
        }
    }
}
