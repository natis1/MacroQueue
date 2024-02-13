using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace MacroQueue.Windows;

public class ConfigWindow : Window, IDisposable
{
    private Configuration Configuration;

    public ConfigWindow(Plugin plugin) : base(
        "MQ configuration",
        ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.Size = new Vector2(232, 100);
        this.SizeCondition = ImGuiCond.Always;

        this.Configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        // can't ref a property, so use a local copy
        var configValue = this.Configuration.QueueingEnabled;
        var debugQueue = this.Configuration.EchoQueueingStatus;
        if (ImGui.Checkbox("Default queueing state", ref configValue))
        {
            if (configValue) {
                Plugin.MqStatus = 1073741824;
            } else {
                Plugin.MqStatus = 0;
            }
            this.Configuration.QueueingEnabled = configValue;
            this.Configuration.Save();
        }
        if (ImGui.Checkbox("Echo queueing status", ref debugQueue))
        {
            this.Configuration.EchoQueueingStatus = debugQueue;
            this.Configuration.Save();
        }
    }
}
