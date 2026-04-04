using System.Collections.Generic;

namespace Waystation.Creator.TileEditor.Tools
{
    public class ToolOptionBar
    {
        private readonly Dictionary<string, Dictionary<string, string>> _toolOptionState
            = new Dictionary<string, Dictionary<string, string>>();

        public string GetOption(string toolName, string optionId, string defaultValue)
        {
            if (_toolOptionState.TryGetValue(toolName, out var opts))
                if (opts.TryGetValue(optionId, out var val))
                    return val;
            return defaultValue;
        }

        public void SetOption(string toolName, string optionId, string value)
        {
            if (!_toolOptionState.ContainsKey(toolName))
                _toolOptionState[toolName] = new Dictionary<string, string>();
            _toolOptionState[toolName][optionId] = value;
        }

        public List<ToolOption> GetOptionsForTool(TileTool tool)
        {
            return tool?.GetToolOptions();
        }
    }
}
