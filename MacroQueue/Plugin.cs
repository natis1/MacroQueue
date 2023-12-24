using System;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Game.Text;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using MacroQueue.Windows;

namespace MacroQueue
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "Sample Plugin";
        private const string MQON = "/mqon";
        private const string MQOFF = "/mqoff";
        public static bool MqStatus = false;
        
        

        private DalamudPluginInterface PluginInterface { get; init; }
        private IGameInteropProvider InteropProvider { get; init; }
        private ICommandManager CommandManager { get; init; }
        private IChatGui ChatGui { get; init; }

        public Configuration Configuration { get; init; }
        
        public WindowSystem WindowSystem = new("MacroQueue");
        private Actions Actions { get; } = null!;
        private unsafe delegate bool TryActionDelegate(IntPtr tp, ActionType t, uint id, ulong target, uint param, uint origin, uint unk, void* l);
        private readonly Hook<TryActionDelegate> tryActionHook = null!;

        private ConfigWindow ConfigWindow { get; init; }

        public Plugin(
            [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
            [RequiredVersion("1.0")] ICommandManager commandManager,
            [RequiredVersion("1.0")] IGameInteropProvider interopProvider,
            [RequiredVersion("1.0")] IDataManager dataManager, [RequiredVersion("1.0")] IChatGui chatGui)
        {
            this.PluginInterface = pluginInterface;
            this.CommandManager = commandManager;
            this.InteropProvider = interopProvider;
            this.Actions = new Actions(dataManager);
            this.ChatGui = chatGui;

            this.Configuration = this.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            this.Configuration.Initialize(this.PluginInterface);

            // you might normally want to embed resources and load them from the manifest stream
            var imagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "goat.png");

            ConfigWindow = new ConfigWindow(this);
            WindowSystem.AddWindow(ConfigWindow);

            this.CommandManager.AddHandler(MQON, new CommandInfo(MqOn)
            {
                HelpMessage = "Turn on macro queuing. Macros should include both commands in them."
            });
            
            this.CommandManager.AddHandler(MQOFF, new CommandInfo(MqOff)
            {
                HelpMessage = "Turn off macro queuing. Macros should include both commands in them."
            });

            this.PluginInterface.UiBuilder.Draw += DrawUI;
            this.PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
            
            unsafe {
                tryActionHook = InteropProvider.HookFromAddress<TryActionDelegate>((IntPtr)ActionManager.MemberFunctionPointers.UseAction, TryActionCallback);
            }
            
            tryActionHook.Enable();
        }

        public void Dispose()
        {
            this.WindowSystem.RemoveAllWindows();
            
            ConfigWindow.Dispose();
            
            this.CommandManager.RemoveHandler(MQON);
            this.CommandManager.RemoveHandler(MQOFF);
            tryActionHook?.Dispose();
        }

        private unsafe bool TryActionCallback(
            IntPtr actionManager, ActionType type, uint id, ulong target, uint param, uint origin, uint unk,
            void* location)
        {
            // Most of this code basically ripped from Redirect lol.
            // This is NOT the same classification as the action's ActionCategory
            if (type != ActionType.Action) {
                return tryActionHook.Original(actionManager, type, id, target, param, origin, unk, location);
            }

            // The action row for the originating ID
            var origRow = Actions.GetRow(id);

            // The row should never be null here, unless the function somehow gets a bad ID
            // Regardless, this makes the compiler happy and we can avoid PVP handling at the same time
            if (origRow is null || origRow.IsPvP) {
                return tryActionHook.Original(actionManager, type, id, target, param, origin, unk, location);
            }
            
            // Macro queueing with instant exit if it cannot be cast
            // Known origins : 0 - bar, 1 - queue, 2 - macro
            if (origin == 2 && MqStatus) {
                // Actions placed on bars try to use their base action, so we need to get the upgraded version
                var adjustedId = ActionManager.MemberFunctionPointers.GetAdjustedActionId((ActionManager*)actionManager, id);
                // Check status, ignoring all cooldowns, just checking if it's available or not.
                var status = ActionManager.MemberFunctionPointers.GetActionStatus((ActionManager*)actionManager, type, adjustedId, (uint)target, false, false, null);
                // Do NOT queue if it's unavailable.
                if (status == 572) {
                    return false;
                }
                origin = 0;
            }
            return tryActionHook.Original(actionManager, type, id, target, param, origin, unk, location);
        }

        private void MqOn(string command, string args)
        {
            MqStatus = true;
            if (Configuration.EchoQueueingStatus)
            {
                
                ChatGui.Print(new XivChatEntry
                {
                    Message = "Macro Queueing Enabled.",
                    Type = XivChatType.Echo
                });
            }
        }
        
        private void MqOff(string command, string args)
        {
            MqStatus = false;
            if (Configuration.EchoQueueingStatus)
            {
                ChatGui.Print(new XivChatEntry
                {
                    Message = "Macro Queueing Disabled.",
                    Type = XivChatType.Echo
                });
            }
        }

        private void DrawUI()
        {
            this.WindowSystem.Draw();
        }

        public void DrawConfigUI()
        {
            ConfigWindow.IsOpen = true;
        }
    }
}
