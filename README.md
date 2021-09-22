# Macro Deck 2
Macro Deck converts your phone, tablet or any other device with an up-to-date internet browser into an powerful remote macro pad to perform single actions or even multiple actions with just one tap.

# Using the plugin API
## Import the SuchByte.MacroDeck.Plugins namespace
```c#
using SuchByte.MacroDeck.Plugins;
```
---
## Creating the main class
```c#
public class Main : IMacroDeckPlugin
{
  
  public string Name => Assembly.GetExecutingAssembly().GetName().Name; // Important! Don't change

  public string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(); // Important! Don't change
  
  public string Author => "Your name here";
  
  public string Description => "Example plugin";

  public List<IMacroDeckAction> Actions { get; set; } // Important! Don't change

  public Image Icon => null; // You can add a image from your resources here

  public bool CanConfigure => false; // If your plugin can be configured, set to "true". It'll make the "Configure" button appear in the package manager

  // Gets called when the plugin is loaded
  public void Enable()
  {
    this.Actions = new List<IMacroDeckAction>{
      // add the instances of your actions here
      new ExampleAction(),
    };
  }
  
  // Gets called when the user clicks on the "Configure" button in the package manager
  public void OpenConfigurator()
  {
    // Open your configuration form here
    using (var configurator = new Configurator()) {
      configurator.ShowDialog();
    }
  }
}
```
---
## Creating a action class
You find more information here: https://github.com/SuchByte/Macro-Deck-Example-CSharp-Plugin/blob/main/ExampleAction.cs
```c#
public class ExampleAction : IMacroDeckAction
{
  public string Name => "Example action"; // The name of the action
  
  public string Description => "Action description"; // eg. what does this action do?
  
  public string DisplayName { get; set; } = "Example action"; // The display name of the action. Can be changed later based on configuration
  
  public string Configuration { get; set; } = ""; // Just leave it
  
  public bool CanConfigure => false; // Set "true" if this action can be configured. This will make the ActionConfigurator calling GetActionConfigurator();
  
  // Gets called when the action can be configured and the action got selected in the ActionSelector. You need to return a user control with the "ActionConfigControl" class as base class
  public ActionConfigControl GetActionConfigurator(ActionConfigurator actionConfigurator)
  {
    return new ExampleActionConfigurator(this, actionConfigurator);
  }
  
  // Gets called when the action is triggered by a button press or an event
  public void Trigger(string clientId, ActionButton actionButton)
  {
    
  }
}
```
---
## Creating the ActionConfigurator class
If your action needs to be configured, your plugin needs a ActionConfigControl. Start with adding a user control to your project. Then you need to add the "ActionConfigControl" class as a base class.
```c#
public partial class ExampleActionConfigurator : ActionConfigControl
{
  private IMacroDeckAction _macroDeckAction; // Add a variable for the instance of your action to get access to the Configuration etc.

  public ExampleActionConfigurator(IMacroDeckAction macroDeckAction)
  {
    this._macroDeckAction = macroDeckAction;
    InitializeComponent();
  }
}

```
---
## Saving the config of an action
If your action can be configured, you need to store the configuration as a string (can be json formatted) in the Configuration variable of your IMacroDeckAction. Macro Deck provides a event when the user closes the action configurator. You need to add the ActionSave event in your ActionConfigControl class.
```c#
public ExampleActionConfigurator(IMacroDeckAction macroDeckAction, ActionConfigurator actionConfigurator)
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
## Setting variables
Maybe your plugin gets data you want to store in a variable like temperatures, states, etc. If the variable don't exists, Macro Deck automatically creates it. You just need to set the value.
```c#
SuchByte.MacroDeck.Variables.VariableManager.SetValue(string name, object value, VariableType type, IMacroDeckPlugin plugin, bool save = true); // if your variable changes often, set save to false
```
