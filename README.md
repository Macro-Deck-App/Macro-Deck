# Macro Deck 2
## More than just a macro pad!

- Open source
- Plugins
- Icon packs
- [Web client](http://web.macrodeck.org)
- Built in package manager (to download plugins and icon packs)
- Logic and global variables
- Multiple profiles
- Unlimited folders
- [Discord community](https://discord.gg/yr7TRaXum8)

# Companion App
[Repository](https://github.com/SuchByte/Macro-Deck-Client)

# Download
https://macrodeck.org/download

# Helping with the translation
All translation files are located under https://github.com/SuchByte/Macro-Deck/tree/master/Resources/Languages

# Using the plugin API
## Creating the project
1. Start by creating a new class library. Important: you need to use .NET Core 3.1 for this, not .NET Framework!
2. Import "Macro Deck 2.dll", which can be found in the installation directory, as a reference to the project.
3. Go into the properties of your project and set the name and the version of the plugin in the package category.
4. Make sure you add `<UseWindowsForms>true</UseWindowsForms>` to `<PropertyGroup>` in your project's configuration.
5. Now you can start with the main class :)

## Import the required references
```c#
using SuchByte.MacroDeck.Plugins;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
```

## Creating the main class
You may have to install the latest version of System.Drawing.Common from NuGet
```c#
public class Main : MacroDeckPlugin
{
  // A short description what the plugin can do
  public override string Description => "Example plugin";
  
  // You can add a image from your resources here
  public override Image Icon => Properties.Resources.ExampleIcon; 
  
  // Optional; If your plugin can be configured, set to "true". It'll make the "Configure" button appear in the package manager.
  public override bool CanConfigure => true; 

  // Gets called when the plugin is loaded
  public override void Enable()
  {
    this.Actions = new List<PluginAction>{
      // add the instances of your actions here
      new ExampleAction(),
    };
  }
  
  // Optional; Gets called when the user clicks on the "Configure" button in the package manager; If CanConfigure is not set to true, you don't need to add this
  public override void OpenConfigurator()
  {
    // Open your configuration form here
    using (var configurator = new Configurator()) {
      configurator.ShowDialog();
    }
  }
}
```

## Creating a action class
```c#
public class ExampleAction : PluginAction
{
  // The name of the action
  public override string Name => "Example action"; 
  
  // A short description what the action can do
  public override string Description => "Action description"; 
  
  // Optional; Add if this action can be configured. This will make the ActionConfigurator calling GetActionConfigurator();
  public override bool CanConfigure => true; 
  
  // Optional; Add if you added CanConfigure; Gets called when the action can be configured and the action got selected in the ActionSelector. You need to return a user control with the "ActionConfigControl" class as base class
  public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
  {
    return new ExampleActionConfigurator(this, actionConfigurator);
  }
  
  // Gets called when the action is triggered by a button press or an event
  public override void Trigger(string clientId, ActionButton actionButton)
  {
    
  }

  // Optional; Gets called when the action button gets deleted
  public override void OnActionButtonDelete()
  {
    
  }

  // Optional; Gets called when the action button is loaded
  public override void OnActionButtonLoaded()
  { 
  
  }
}
```

## Creating the ActionConfigurator class
If your action needs to be configured, your plugin needs a ActionConfigControl. Start with adding a user control to your project. Then you need to add the "ActionConfigControl" class as a base class.
```c#
public partial class ExampleActionConfigurator : ActionConfigControl
{
  // Add a variable for the instance of your action to get access to the Configuration etc.
  private PluginAction _macroDeckAction; 

  public ExampleActionConfigurator(PluginAction macroDeckAction, ActionConfigurator actionConfigurator)
  {
    this._macroDeckAction = macroDeckAction;
    InitializeComponent();
  }
}

```

## Saving the config of an action
If your action can be configured, you need to store the configuration as a string (can be json formatted) in the Configuration variable of your PluginAction. Macro Deck provides a boolean in the ActionConfigControl class which you can override. This gets called when the user closes the action configurator. You can force the user to configure the action by returning false if e.g a text box is not filled out.
```c#
public ExampleActionConfigurator(PluginAction macroDeckAction, ActionConfigurator actionConfigurator)
{
  this._macroDeckAction = macroDeckAction;
  InitializeComponent();
}

public override bool OnActionSave()
{
  if (String.IsNullOrWhiteSpace(this.exampleTextBox.Text)) {
    return false; // Return false if the user has not filled out the text box
  }
  try
  {
    JObject configuration = new JObject();
    configuration["example"] = this.exampleTextBox.Text;
    this._macroDeckAction.ConfigurationSummary = configuration["example"].toString(); // Set a summary of the configuration that gets displayed in the ButtonConfigurator item
    this._macroDeckAction.Configuration = configuration.ToString();
  } catch { }
  return true; // Return true if the action was configured successfully; This closes the ActionConfigurator
}

```

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

## Setting variables
Maybe your plugin gets data you want to store in a variable like temperatures, states, etc. If the variable don't exists, Macro Deck automatically creates it. You just need to set the value.
```c#
SuchByte.MacroDeck.Variables.VariableManager.SetValue(string name, object value, VariableType type, IMacroDeckPlugin plugin, bool save = true); // if your variable changes often, set save to false
```

## Compiling and adding the manifest
Compile your plugin and copy the output .dll file into "%Appdata%\Macro Deck\plugins\YOUR_NAME.YOUR_PLUGINNAME\"
After that you have to create a plugin manifest. Create a .xml file and call it "Plugin.xml" and place it in the same directory.
```xml
<?xml version="1.0"?>
<PluginManifest xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <Author>YOUR_NAME</Author>
  <MainFile>YOUR_PLUGIN.dll</MainFile>
</PluginManifest>
```

## Publish the plugin in the package manager
If you want your plugin to be published in the Macro Deck package manager, you have to follow these steps:
1. Upload the source code to GitHub. Plugins in the package manager must be open source because of safety reasons.
2. Add the proper license to your repository
3. Create a issue with the accordingly template here: https://github.com/SuchByte/Macro-Deck/issues/new/choose

# Third party licenses
Macro Deck uses some awesome 3rd party libraries:
- [Fleck (MIT license)](https://github.com/statianzo/Fleck)
- [GiphyDotNet (MIT license)](https://github.com/drasticactions/GiphyDotNet)
- [Magick.NET (Apache-2.0)](https://github.com/dlemstra/Magick.NET)
- [Newtonsoft.Json (MIT license)](https://www.newtonsoft.com/json)
- [RestSharp (Apache-2.0)](https://restsharp.dev/)
- [SQLiteNetExtensions (MIT license)](https://bitbucket.org/twincoders/sqlite-net-extensions/src/master/)
- [sqlite-net-pcl (MIT license)](https://github.com/praeclarum/sqlite-net)

Macro Deck also uses free icons from:
- [Material Design Icons](https://materialdesignicons.com/)
