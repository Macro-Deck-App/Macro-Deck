using Newtonsoft.Json;
using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.Events;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.CustomControls.ButtonEditor;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.GUI.MainWindowContents;
using SuchByte.MacroDeck.Hotkeys;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Interfaces;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Profiles;
using SuchByte.MacroDeck.Server;
using SuchByte.MacroDeck.Variables;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace SuchByte.MacroDeck.GUI
{
    public partial class ButtonEditor : DialogForm
    {
        static JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            NullValueHandling = NullValueHandling.Ignore,
            Error = (sender, args) => { args.ErrorContext.Handled = true; }
        };


        private ActionButton.ActionButton actionButton;
        private ActionButton.ActionButton actionButtonEdited;
        private readonly Folders.MacroDeckFolder folder;
        private EventSelector eventSelector;
        private ActionSelectorOnPress actionSelectorOnPress;
        private ActionSelectorOnPress actionSelectorOnRelease;
        private ActionSelectorOnPress actionSelectorOnLongPress;
        private ActionSelectorOnPress actionSelectorOnLongPressRelease;

        public ActionButton.ActionButton ActionButton { get { return this.actionButton; } }

        public ButtonEditor(ActionButton.ActionButton actionButton, Folders.MacroDeckFolder folder)
        {
            InitializeComponent();
            this.lblAppearance.Text = LanguageManager.Strings.Appearance;
            this.lblState.Text = LanguageManager.Strings.ButtonState;
            this.lblButtonState.Text = LanguageManager.Strings.ButtonState;
            this.radioButtonOff.Text = LanguageManager.Strings.Off;
            this.radioButtonOn.Text = LanguageManager.Strings.On;
            this.lblCurrentStateLabel.Text = LanguageManager.Strings.CurrentState;
            this.lblStateBinding.Text = LanguageManager.Strings.StateBinding;
            this.radioOnPress.Text = LanguageManager.Strings.OnPress;
            this.radioOnRelease.Text = LanguageManager.Strings.OnRelease;
            this.radioOnLongPress.Text = LanguageManager.Strings.OnLongPress;
            this.radioOnLongPressRelease.Text = LanguageManager.Strings.OnLongPressRelease;
            this.radioOnEvent.Text = LanguageManager.Strings.OnEvent;
            this.btnApply.Text = LanguageManager.Strings.Save;
            this.btnOk.Text = LanguageManager.Strings.Ok;
            this.lblKeyBinding.Text = LanguageManager.Strings.Hotkey;

            this.folder = folder;
            this.actionButton = actionButton;
            
            using (InstalledFontCollection col = new InstalledFontCollection())
            {
                foreach (FontFamily fontFamily in col.Families)
                {
                    fonts.Items.Add(fontFamily.Name);
                }
            }
            this.listStateBinding.Items.Add("");
            foreach (Variable variable in VariableManager.Variables)
            {
                this.listStateBinding.Items.Add(variable.Name);
            }
            this.actionButton.StateChanged += this.OnStateChanged;
            this.hotkey.Click += Hotkey_Click;
            this.LoadButton();
        }

        private void ButtonEditor_Load(object sender, EventArgs e)
        {
            this.btnPreview.Radius = ProfileManager.CurrentProfile.ButtonRadius;
            this.UpdateLabel();
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            if (this == null || this.IsDisposed || this.Disposing) return;
            try
            {
                this.Invoke(new Action(() =>
                {
                
                        bool newState = ((ActionButton.ActionButton)sender).State;
                        this.lblCurrentState.Text = newState ? "On" : "Off";
                        this.radioButtonOff.Checked = !newState;
                        this.radioButtonOn.Checked = newState;
                }));
            }
            catch { }

        }

        private void UpdateLabel()
        {
            try
            {
                ButtonLabel buttonLabel = radioButtonOff.Checked && !radioButtonOn.Checked ? this.actionButtonEdited.LabelOff : this.actionButtonEdited.LabelOn;
                buttonLabel.LabelText = this.labelText.Text;
                buttonLabel.Size = (float)this.fontSize.Value;
                buttonLabel.FontFamily = this.fonts.Text;
                if (this.labelAlignTop.Checked)
                {
                    buttonLabel.LabelPosition = ButtonLabelPosition.TOP;
                }
                else if (this.labelAlignCenter.Checked)
                {
                    buttonLabel.LabelPosition = ButtonLabelPosition.CENTER;
                }
                else if (this.labelAlignBottom.Checked)
                {
                    buttonLabel.LabelPosition = ButtonLabelPosition.BOTTOM;
                }
                Task.Run(() =>
                {
                    this.btnForeColor.BackColor = buttonLabel.LabelColor;
                    this.btnForeColor.HoverColor = buttonLabel.LabelColor;
                    Bitmap labelBitmap = new Bitmap(250, 250);
                    string labelText = buttonLabel.LabelText.ToString();
                    labelText = VariableManager.RenderTemplate(labelText);

                    labelBitmap = (Bitmap)Utils.LabelGenerator.GetLabel(labelBitmap, labelText, buttonLabel.LabelPosition, new Font(buttonLabel.FontFamily, buttonLabel.Size), buttonLabel.LabelColor, Color.Black, new SizeF(2.0F, 2.0F));
                    buttonLabel.LabelBase64 = Utils.Base64.GetBase64FromImage(labelBitmap);
                    this.Invoke(new Action(() => {
                        if (this != null && this.Disposing == false && this.IsDisposed == false)
                        {
                            this.btnPreview.ForegroundImage = labelBitmap;
                        }
                    }));
                });
            } catch (Exception ex)
            {
                MacroDeckLogger.Error(GetType(), "Error while updating label: " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
        }

        private void RefreshLabel()
        {
            this.labelText.TextChanged -= this.LabelChanged;
            this.fontSize.ValueChanged -= this.LabelChanged;
            this.labelAlignTop.CheckedChanged -= this.LabelChanged;
            this.labelAlignCenter.CheckedChanged -= this.LabelChanged;
            this.labelAlignBottom.CheckedChanged -= this.LabelChanged;
            this.fonts.SelectedIndexChanged -= this.LabelChanged;
            
            try
            {
                ButtonLabel buttonLabel = radioButtonOff.Checked && !radioButtonOn.Checked ? this.actionButtonEdited.LabelOff : this.actionButtonEdited.LabelOn;

                this.labelText.Text = buttonLabel.LabelText;
                this.fontSize.Value = (int)buttonLabel.Size;
                this.fonts.Text = buttonLabel.FontFamily;

                switch (buttonLabel.LabelPosition)
                {
                    case ButtonLabelPosition.TOP:
                        this.labelAlignTop.Checked = true;
                        break;
                    case ButtonLabelPosition.CENTER:
                        this.labelAlignCenter.Checked = true;
                        break;
                    case ButtonLabelPosition.BOTTOM:
                        this.labelAlignBottom.Checked = true;
                        break;
                }

                if (string.IsNullOrWhiteSpace(this.labelText.Text))
                {
                    this.labelText.PlaceHolderText = LanguageManager.Strings.Label;
                }
            } catch (Exception ex) 
            {
                MacroDeckLogger.Error(GetType(), "Error while refreshing label: " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
            this.labelText.TextChanged += this.LabelChanged;
            this.fontSize.ValueChanged += this.LabelChanged;
            this.labelAlignTop.CheckedChanged += this.LabelChanged;
            this.labelAlignCenter.CheckedChanged += this.LabelChanged;
            this.labelAlignBottom.CheckedChanged += this.LabelChanged;
            this.fonts.SelectedIndexChanged += this.LabelChanged;
        }

        public void RefreshIcon()
        {
            try
            {
                string iconString = radioButtonOff.Checked && !radioButtonOn.Checked ? this.actionButtonEdited.IconOff : this.actionButtonEdited.IconOn;

                if (!string.IsNullOrWhiteSpace(iconString))
                {
                    var icon = IconManager.GetIconByString(iconString);
                    if (icon != null)
                        this.btnPreview.BackgroundImage = icon.IconImage;
                }
                else
                {
                    this.btnPreview.BackgroundImage = null;
                }

                ButtonLabel buttonLabel = radioButtonOff.Checked && !radioButtonOn.Checked ? this.actionButtonEdited.LabelOff : this.actionButtonEdited.LabelOn;

                if (buttonLabel != null && !string.IsNullOrWhiteSpace(buttonLabel.LabelBase64)) 
                {
                    Image label = Utils.Base64.GetImageFromBase64(buttonLabel.LabelBase64);
                    if (label != null)
                        this.btnPreview.ForegroundImage = label;
                }
                else
                {
                    this.btnPreview.ForegroundImage = null;
                }

                Color backColor = radioButtonOff.Checked && !radioButtonOn.Checked ? this.actionButtonEdited.BackColorOff : this.actionButtonEdited.BackColorOn;
                this.btnBackColor.BackColor = backColor;
                this.btnBackColor.HoverColor = backColor;
                this.btnPreview.BackColor = backColor;

                this.btnPreview.ShowGIFIndicator = this.btnPreview.BackgroundImage != null && this.btnPreview.BackgroundImage.RawFormat.ToString().ToLower() == "gif";
            } catch (Exception ex) 
            {
                MacroDeckLogger.Error(GetType(), "Error while refreshing icon: " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
        }

        private void Apply()
        {
            Debug.WriteLine("Apply");
            HotkeyManager.RemoveHotkey(this.actionButton);
            this.actionButton = this.actionButtonEdited;
            this.actionButton.EventListeners = new List<EventListener>();

            foreach (PluginAction pluginAction in this.actionButton.Actions)
            {
                pluginAction.SetActionButton(this.actionButton);
            }
            foreach (PluginAction pluginAction in this.actionButton.ActionsRelease)
            {
                pluginAction.SetActionButton(this.actionButton);
            }
            foreach (PluginAction pluginAction in this.actionButton.ActionsLongPress)
            {
                pluginAction.SetActionButton(this.actionButton);
            }
            foreach (PluginAction pluginAction in this.actionButton.ActionsLongPressRelease)
            {
                pluginAction.SetActionButton(this.actionButton);
            }

            foreach (EventItem eventItem in this.eventSelector.EventItems())
            {
                if (eventItem == null) continue;
                foreach (PluginAction pluginAction in eventItem.EventListener.Actions)
                {
                    pluginAction.SetActionButton(this.actionButton);
                }
                this.actionButton.EventListeners.Add(eventItem.EventListener);
            }

            foreach (ActionButton.ActionButton actionButton in this.folder.ActionButtons.FindAll(actionButton => actionButton.Position_Y == this.actionButton.Position_Y && actionButton.Position_X == this.actionButton.Position_X).ToArray())
            {
                this.folder.ActionButtons.Remove(actionButton);
            }
            this.folder.ActionButtons.Add(this.actionButton);
            ProfileManager.Save();
            MacroDeckServer.UpdateFolder(this.folder);
            ProfileManager.UpdateVariableLabels(this.actionButton);
            this.actionButton.UpdateBindingState();
            this.actionButton.UpdateHotkey();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            this.Apply();
        }


        private void BtnOk_Click(object sender, EventArgs e)
        {
            this.Apply();
            this.Close();
        }

        private void BtnPreview_Click(object sender, EventArgs e)
        {
            this.OpenIconSelector();
        }

        private void OpenIconSelector()
        {
            using (var iconSelector = new IconSelector())
            {
                if (iconSelector.ShowDialog() == DialogResult.OK)
                {
                    if (iconSelector.SelectedIconPack != null && iconSelector.SelectedIcon != null)
                    {
                        if (radioButtonOff.Checked && !radioButtonOn.Checked)
                        {
                            this.actionButtonEdited.IconOff = iconSelector.SelectedIconPack.Name + "." + iconSelector.SelectedIcon.IconId;
                        }
                        else
                        {
                            this.actionButtonEdited.IconOn = iconSelector.SelectedIconPack.Name + "." + iconSelector.SelectedIcon.IconId;
                        }
                        this.RefreshIcon();
                    }
                }
            }
        }

        public void LoadButton()
        {
            this.actionButtonEdited = JsonConvert.DeserializeObject<ActionButton.ActionButton>(JsonConvert.SerializeObject(this.actionButton, jsonSerializerSettings), jsonSerializerSettings); // Make a copy of the current action button

            bool currentState = this.actionButton.State;
            this.lblCurrentState.Text = currentState ? "On" : "Off";
            this.listStateBinding.Text = this.actionButtonEdited.StateBindingVariable;
            this.buttonGUIDLabel.Text = this.actionButtonEdited.Guid;

            if (this.actionButton.KeyCode != Keys.None)
            {
                this.hotkey.Text = this.actionButton.ModifierKeyCodes.ToString().Replace("Control", "CTRL").Replace("None", string.Empty).Replace(", ", " + ") + (!actionButton.ModifierKeyCodes.ToString().Equals("None") ? " + " : string.Empty) + this.actionButton.KeyCode.ToString();
            }

            this.RefreshLabel();
            this.RefreshIcon();
            this.actionSelectorOnPress = new ActionSelectorOnPress(this.actionButtonEdited.Actions);
            this.actionSelectorOnRelease = new ActionSelectorOnPress(this.actionButtonEdited.ActionsRelease);
            this.actionSelectorOnLongPress = new ActionSelectorOnPress(this.actionButtonEdited.ActionsLongPress);
            this.actionSelectorOnLongPressRelease = new ActionSelectorOnPress(this.actionButtonEdited.ActionsLongPressRelease);
            this.actionSelectorOnPress.RefreshActions();
            this.actionSelectorOnRelease.RefreshActions();
            this.actionSelectorOnLongPress.RefreshActions();
            this.actionSelectorOnLongPressRelease.RefreshActions();
            this.eventSelector = new EventSelector(this.actionButtonEdited);
            this.eventSelector.RefreshEventsList();
            this.SetSelector(actionSelectorOnPress);
        }

        

        private void LabelChanged(object sender, EventArgs e)
        {
            this.UpdateLabel();
        }


       
        private void RadioButton_CheckedChanged(object sender, EventArgs e)
        {
            this.RefreshLabel();
            this.RefreshIcon();
            this.UpdateLabel();
        }

        private void BtnEditIcon_Click(object sender, EventArgs e)
        {
            this.OpenIconSelector();
        }

        private void BtnRemoveIcon_Click(object sender, EventArgs e)
        {
            if (radioButtonOff.Checked && !radioButtonOn.Checked)
            {
                this.actionButtonEdited.IconOff = null;
            }
            else
            {
                this.actionButtonEdited.IconOn = null;
            }
            this.RefreshIcon();
        }

        private void BtnBackColor_Click(object sender, EventArgs e)
        {
            using (var colorDialog = new ColorDialog())
            {
                if (colorDialog.ShowDialog() == DialogResult.OK)
                {
                    if (radioButtonOff.Checked && !radioButtonOn.Checked)
                    {
                        this.actionButtonEdited.BackColorOff = colorDialog.Color;
                    }
                    else
                    {
                        this.actionButtonEdited.BackColorOn = colorDialog.Color;
                    }
                    this.RefreshIcon();
                }
            }

        }

        private void BtnClearLabelText_Click(object sender, EventArgs e)
        {
            this.labelText.Clear();
        }

        private void BtnForeColor_Click(object sender, EventArgs e)
        {
            using (var colorDialog = new ColorDialog())
            {
                if (colorDialog.ShowDialog() == DialogResult.OK)
                {
                    if (radioButtonOff.Checked && !radioButtonOn.Checked)
                    {
                        this.actionButtonEdited.LabelOff.LabelColor = colorDialog.Color;
                    }
                    else
                    {
                        this.actionButtonEdited.LabelOn.LabelColor = colorDialog.Color;
                    }
                    this.UpdateLabel();
                }
            }
            
        }

        private void BtnAddVariable_Click(object sender, EventArgs e)
        {
            this.variablesContextMenu.Items.Clear();
            foreach (Variable variable in VariableManager.Variables)
            {
                ToolStripMenuItem item = new ToolStripMenuItem
                {
                    ForeColor = Color.White,
                    Text = variable.Name,
                };
                item.Click += AddVariableContextMenuItemClick;
                this.variablesContextMenu.Items.Add(item);
            }
            this.variablesContextMenu.Show(PointToScreen(new Point(((PictureButton)sender).Bounds.Left, ((PictureButton)sender).Bounds.Bottom)));
        }

        private void AddVariableContextMenuItemClick(object sender, EventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            var selectionIndex = this.labelText.SelectionStart;
            this.labelText.Text = this.labelText.Text.Insert(selectionIndex, "{" + item.Text + "}");
            this.labelText.SelectionStart = selectionIndex + ("{" + item.Text + "}").Length;
        }

        private void ListStateBinding_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.actionButtonEdited.StateBindingVariable = this.listStateBinding.Text;
        }

        private void BtnDeleteStateBinding_Click(object sender, EventArgs e)
        {
            this.listStateBinding.Text = "";
        }

        private void RadioOnPress_CheckedChanged(object sender, EventArgs e)
        {
            if (radioOnPress.Checked)
            {
                this.SetSelector(actionSelectorOnPress);
            }
        }

        private void RadioOnRelease_CheckedChanged(object sender, EventArgs e)
        {
            if (radioOnRelease.Checked)
            {
                this.SetSelector(actionSelectorOnRelease);
            }
        }

        private void RadioOnLongPress_CheckedChanged(object sender, EventArgs e)
        {
            if (radioOnLongPress.Checked)
            {
                this.SetSelector(actionSelectorOnLongPress);
            }
        }

        private void RadioOnLongPressRelease_CheckedChanged(object sender, EventArgs e)
        {
            if (radioOnLongPressRelease.Checked)
            {
                this.SetSelector(actionSelectorOnLongPressRelease);
            }
        }

        private void RadioOnEvent_CheckedChanged(object sender, EventArgs e)
        {
            if (radioOnEvent.Checked)
            {
                this.SetSelector(eventSelector);
            }
        }

        private void SetSelector(Control control)
        {
            this.selectorPanel.Controls.Clear();
            this.selectorPanel.Controls.Add(control);
        }

        private void BtnOpenTemplateEditor_Click(object sender, EventArgs e)
        {
            using (var templateEditor = new TemplateEditor(this.labelText.Text))
            {
                if (templateEditor.ShowDialog() == DialogResult.OK)
                {
                    this.labelText.Text = templateEditor.Template;
                }
            }
        }

        private void BtnRemoveHotkey_Click(object sender, EventArgs e)
        {
            this.actionButtonEdited.ModifierKeyCodes = Keys.None;
            this.actionButtonEdited.KeyCode = Keys.None;
            HotkeyManager.RemoveHotkey(this.actionButtonEdited);
            this.hotkey.Text = string.Empty;
        }

        private void Hotkey_Click(object sender, EventArgs e)
        {
            using (var hotkeySelector = new HotkeySelector())
            {
                if (hotkeySelector.ShowDialog() == DialogResult.OK)
                {
                    this.hotkey.Text = hotkeySelector.ModifierKeys.ToString().Replace("Control", "CTRL").Replace("None", string.Empty).Replace(", ", " + ") + (!hotkeySelector.ModifierKeys.ToString().Equals("None") ? " + " : string.Empty) + hotkeySelector.Key.ToString();
                    this.actionButtonEdited.ModifierKeyCodes = hotkeySelector.ModifierKeys;
                    this.actionButtonEdited.KeyCode = hotkeySelector.Key;
                }
            }
        }

        private void BtnEditJson_Click(object sender, EventArgs e)
        {
            using (var jsonButtonEditor = new JsonButtonEditor(this.actionButtonEdited))
            {
                if (jsonButtonEditor.ShowDialog() == DialogResult.OK)
                {
                    jsonButtonEditor.ActionButton.Position_X = this.actionButtonEdited.Position_X;
                    jsonButtonEditor.ActionButton.Position_Y = this.actionButtonEdited.Position_Y;
                    jsonButtonEditor.ActionButton.Guid = this.actionButtonEdited.Guid;
                    this.actionButton = jsonButtonEditor.ActionButton;
                    this.LoadButton();
                    this.UpdateLabel();
                }
            }
        }

        private void label4_Click(object sender, EventArgs e)
        {

        }
    }
}
