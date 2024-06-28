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
        private const string MQRESET = "/mqr"; // Only allow macro queueing on the next action
        public static int MqStatus = 0;
        [PluginService] internal static DalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
        [PluginService] internal static IGameInteropProvider InteropProvider { get; private set; } = null!;
        [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
        [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
        [PluginService] internal static IClientState ClientState { get; private set; } = null!;
        

        public Configuration Configuration { get; init; }
        
        public WindowSystem WindowSystem = new("MacroQueue");
        private Actions Actions { get; } = null!;
        private unsafe delegate bool TryActionDelegate(IntPtr tp, ActionType t, uint id, ulong target, uint param, uint origin, uint unk, void* l);
        private readonly Hook<TryActionDelegate> tryActionHook = null!;

        private ConfigWindow ConfigWindow { get; init; }

        public Plugin()
        {
            this.Actions = new Actions(DataManager);

            this.Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            if (this.Configuration.QueueingEnabled) {
                MqOn("", "");
            }

            // you might normally want to embed resources and load them from the manifest stream
            var imagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "goat.png");

            ConfigWindow = new ConfigWindow(this);
            WindowSystem.AddWindow(ConfigWindow);

            CommandManager.AddHandler(MQON, new CommandInfo(MqOn)
            {
                HelpMessage = "Turn on macro queuing. Macros should include both commands in them."
            });
            
            CommandManager.AddHandler(MQOFF, new CommandInfo(MqOff)
            {
                HelpMessage = "Turn off macro queuing. Macros should include both commands in them."
            });

            CommandManager.AddHandler(MQRESET, new CommandInfo(MqReset)
            {
                HelpMessage = "Turn on macro queueing, but only for the next successfully queueable action. Afterwards, turn it off until reset again, or turned on."
            });

            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
            
            unsafe {
                tryActionHook = InteropProvider.HookFromAddress<TryActionDelegate>((IntPtr)ActionManager.MemberFunctionPointers.UseAction, TryActionCallback);
            }
            
            tryActionHook.Enable();
        }

        public void Dispose()
        {
            this.WindowSystem.RemoveAllWindows();
            
            ConfigWindow.Dispose();
            
            CommandManager.RemoveHandler(MQON);
            CommandManager.RemoveHandler(MQOFF);
            CommandManager.RemoveHandler(MQRESET);
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
            if (origin == 2 && MqStatus > 0) {
                // Actions placed on bars try to use their base action, so we need to get the upgraded version
                var adjustedId = ActionManager.MemberFunctionPointers.GetAdjustedActionId((ActionManager*)actionManager, id);
                // Check status, ignoring the GCD and just checking if the action is available or not.
                var status = ActionManager.MemberFunctionPointers.GetActionStatus((ActionManager*)actionManager, type, adjustedId, (uint)target, false, false, null);
                var status2 = ActionManager.MemberFunctionPointers.GetActionStatus((ActionManager*)actionManager, type, adjustedId, (uint)target, true, true, null);

                if (Configuration.EchoQueueingStatus) {
                    PrintBuffArray();

                    ChatGui.Print(new XivChatEntry
                    {
                        Message = "Macro attempting execute action ID: " + adjustedId +" with status: " + status + " and status2: " + status2,
                        Type = XivChatType.Echo
                    });
                }
                // Do NOT queue if it's unavailable.
                if (status == 572) {
                    return false;
                }
                // Subtract 1 from mqstatus, so that if it's set to 1, it will turn off macro queueing for future actions.
                // but only if the ability is NOT on cooldown.
                if (status2 != 582) {
                    MqStatus--;
                }
                origin = 0;
            }
            return tryActionHook.Original(actionManager, type, id, target, param, origin, unk, location);
        }

        private void MqOn(string command, string args)
        {
            MqStatus = 1073741824;
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
            MqStatus = 0;
            if (Configuration.EchoQueueingStatus)
            {
                ChatGui.Print(new XivChatEntry
                {
                    Message = "Macro Queueing Disabled.",
                    Type = XivChatType.Echo
                });
            }
        }

        private void MqReset(string command, string args)
        {
            MqStatus = 1;
            if (Configuration.EchoQueueingStatus)
            {
                ChatGui.Print(new XivChatEntry
                {
                    Message = "Macro Queueing reset to 1.",
                    Type = XivChatType.Echo
                });
            }
        }

        private void PrintBuffArray()
        {
            var buffs = ClientState.LocalPlayer.StatusList;
            for (var i = 0; i < buffs.Length; i++) {
                if (buffs[i].StatusId == 0) {
                    continue;
                }
                ChatGui.Print(new XivChatEntry
                {
                    Message = "Player has buff with ID: " + buffs[i].StatusId,
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
