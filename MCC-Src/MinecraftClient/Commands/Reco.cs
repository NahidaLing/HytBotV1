using System;
using Brigadier.NET;
using Brigadier.NET.Builder;
using MinecraftClient.CommandHandler;

namespace MinecraftClient.Commands
{
    public class Reco : Command
    {
        public override string CmdName { get { return "reco"; } }
        public override string CmdUsage { get { return "reco"; } }
        public override string CmdDesc { get { return Translations.cmd_reco_desc; } }

        public override void RegisterCommand(CommandDispatcher<CmdResult> dispatcher)
        {
            dispatcher.Register(l => l.Literal("help")
                .Then(l => l.Literal(CmdName)
                    .Executes(r => GetUsage(r.Source, string.Empty))
                )
            );

            dispatcher.Register(l => l.Literal(CmdName)
                .Executes(r => DoReconnect(r.Source))
                .Then(l => l.Literal("_help")
                    .Executes(r => GetUsage(r.Source, string.Empty))
                    .Redirect(dispatcher.GetRoot().GetChild("help").GetChild(CmdName)))
            );
        }

        private int GetUsage(CmdResult r, string? cmd)
        {
            return r.SetAndReturn(cmd switch
            {
#pragma warning disable format // @formatter:off
                _           =>  GetCmdDescTranslated(),
#pragma warning restore format // @formatter:on
            });
        }

        private int DoReconnect(CmdResult r)
        {
            Program.Restart(keepAccountAndServerSettings: true);
            return r.SetAndReturn(CmdResult.Status.Done);
        }

        internal static string DoReconnect(string command)
        {
            Program.Restart(keepAccountAndServerSettings: true);
            return String.Empty;
        }
    }
}
