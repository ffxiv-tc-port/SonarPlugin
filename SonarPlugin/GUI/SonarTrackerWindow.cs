using ImGuiNET;
using Dalamud.Interface.Windowing;
using Dalamud.Logging;
using DryIoc;
using DryIoc.FastExpressionCompiler.LightExpression;
using DryIocAttributes;
using Lumina.Data.Parsing.Layer;
using Lumina.Excel.Sheets;
using Sonar.Data;
using Sonar.Data.Rows;
using Sonar.Relays;
using Sonar.Trackers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace SonarPlugin.GUI
{
    [ExportMany]
    [SingletonReuse]
    public sealed class SonarTrackerWindow : Window, IDisposable
    {
        private readonly IndexSelectionWidget _indexWidget;

        private WindowSystem Windows { get; }
        private IRelayTracker<HuntRelay> Hunts { get; }
        private IRelayTracker<FateRelay> Fates { get; }

        public SonarTrackerWindow(WindowSystem windows, IRelayTracker<HuntRelay> hunts, IRelayTracker<FateRelay> fates, Container container) : base("Sonar Tracker (UNDER DEVELOPMENT!!!)") // TODO
        {
            this._indexWidget = container.Resolve<IndexSelectionWidget>();
            this.Windows = windows;
            this.Hunts = hunts;
            this.Fates = fates;
            this.Windows.AddWindow(this);
        }

        public override void Draw()
        {
            this._indexWidget.DrawBreadcrumb();
            ImGui.Text($"Index key: {this._indexWidget.IndexKey}");
            ImGui.Text($"Hunts: {this.Hunts.Data.GetIndexStates(this._indexWidget.IndexKey).Count} | Fates: {this.Fates.Data.GetIndexStates(this._indexWidget.IndexKey).Count}");
            ImGui.Spacing();
            ImGui.Text("恭喜你發現了這個！");
            ImGui.TextWrapped("目前這個視窗還沒有什麼實用的功能，我會在下個版本繼續開發。在那之前，歡迎先體驗篩選選擇器以及狩獵/節慶任務的數量統計");
        }

        public void Dispose()
        {
            this.Windows.RemoveWindow(this);
        }
    }
}
