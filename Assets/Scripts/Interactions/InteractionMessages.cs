using System.Collections.Generic;

public static class InteractionMessages
{
    public static readonly Dictionary<string, string> Messages = new Dictionary<string, string>
    {
        // Power Box messages
        {"PowerOn", "Power on!"},
        {"PowerOff", "Power off"},
        {"PowerSwitchPrompt", "Press E to use lever"},

        // Light messages
        {"LightsPowerOff", "Hmm, the power seems to be off..."},
        {"LightSwitchPrompt", "Press E to use light switch"},
        {"LightSwitchNoPower", "Press E to use light switch"},
        {"LightsOnPowerOn", "Lights on"},
        {"LightsOffPowerOn", "Lights off"},

        // Music messages
        {"MusicPowerOff", "This mixing desk requires power to function - NOT USED"},
        {"PowerRequired", "This doesn't seem to work..."},
        {"MusicSystemPrompt", "Press E to play music"},
        {"MusicSystemNoPower", "Press E to use mixing desk"},
        {"MusicOnPowerOn", "Play"},
        {"MusicOffPowerOn", "Stop"},

        // General messages
        {"Interacting", "Press E to {0}"} // {0} will be replaced with the action text
    };

    public static string GetMessage(string key, params object[] args)
    {
        if (Messages.TryGetValue(key, out string message))
        {
            return string.Format(message, args);
        }
        return $"Message not found for key: {key}";
    }
}