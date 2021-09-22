# Macro Deck 2
Macro Deck converts your phone, tablet or any other device with an up-to-date internet browser into an powerful remote macro pad to perform single actions or even multiple actions with just one tap.

# Using the plugin API
## Import the SuchByte.MacroDeck.Plugins namespace
```c#
using SuchByte.MacroDeck.Plugins;
```
---
## Add the IMacroDeckPlugin interface to your main class
You find more information here: https://github.com/SuchByte/Macro-Deck-Example-CSharp-Plugin/blob/main/Main.cs
```c#
public class Main : IMacroDeckPlugin
{
...
}
```
---
## Add the IMacroDeckAction interface to your action classes
You find more information here: https://github.com/SuchByte/Macro-Deck-Example-CSharp-Plugin/blob/main/ExampleAction.cs
```c#
public class ExampleAction : IMacroDeckAction
{
...
}
```
---
## Saving plugin settings
If you want to save some plugin wide settings, Macro Deck provides two simple functions for this

Set a string value with
```c#
SuchByte.MacroDeck.Plugins.PluginConfiguration.SetValue(IMacroDeckPlugin plugin, string key, string value);
```
or get a string with
```c#
var configuration = SuchByte.MacroDeck.Plugins.PluginConfiguration.GetValue(IMacroDeckPlugin plugin, string key);
```
If the key is not set, this will return ""
---
## Saving login credentials
Never save login credentials like passwords, tokens, etc. unencrypted! Macro Deck provides simple functions to store credentials encrypted.

Set credentials with
```c#
Dictionary<string, string> keyValuePairs = new Dictionary<string, string>();
keyValuePairs["email"] = "example@example.com";
keyValuePairs["password"] = "password1234";
SuchByte.MacroDeck.Plugins.PluginCredentials.SetCredentials(IMacroDeckPlugin plugin, Dictionary<string, string> keyValuePairs);
```
and get a list of all the credentials of the plugin which are already unencryped
```c#
List<Dictionary<string, string>> credentialsList = SuchByte.MacroDeck.Plugins.PluginCredentials.PluginCredentials.GetPluginCredentials(IMacroDeckPlugin plugin);
```
---
## Saving the config of an action
If your action can be configured, you need to store the configuration as a string (can be json formatted) in the Configuration variable of your IMacroDeckAction. Macro Deck provides a event when the user closes the action configurator. You need to add the ActionSave event in your ActionConfigControl class.
```c#
public ExampleConfigurator(IMacroDeckAction macroDeckAction, ActionConfigurator actionConfigurator)
{
  this._macroDeckAction = macroDeckAction;
  InitializeComponent();
  actionConfigurator.ActionSave += OnActionSave;
}

private void OnActionSave(object sender, EventArgs e)
{
  try
  {
    JObject configuration = new JObject();
    configuration["key"] = "value";
    this._macroDeckAction.DisplayName = this._macroDeckAction.Name + " -> " + configuration["key"].toString();
    this._macroDeckAction.Configuration = configuration.ToString();
  } catch { }
}
```
## Setting variables
Maybe your plugin gets data you want to store in a variable like temperatures, states, etc. If the variable don't exists, Macro Deck automatically creates it. You just need to set the value.
```c#
SuchByte.MacroDeck.Variables.VariableManager.SetValue(string name, object value, VariableType type, IMacroDeckPlugin plugin, bool save = true); // if your variable changes often, set save to false
```
