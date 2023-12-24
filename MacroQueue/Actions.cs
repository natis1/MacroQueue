using Lumina.Excel;
using Lumina.Excel.GeneratedSheets;
using System.Collections.Generic;
using Dalamud.Plugin.Services;

namespace MacroQueue {
    public class Actions {
        private ExcelSheet<Action> Sheet = null!;
        private IEnumerable<Action> Role = null!;
        private bool initialized = false;

        public Action? GetRow(uint id) => Sheet?.GetRow(id);

        public Actions(IDataManager dataManager) {
            Sheet = dataManager.GetExcelSheet<Action>()!;
        }
    }
}
