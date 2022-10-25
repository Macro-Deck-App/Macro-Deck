using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.Events;
using SuchByte.MacroDeck.Folders;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.CustomControls.ButtonEditor;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Hotkeys;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Profiles;
using SuchByte.MacroDeck.Server;
using SuchByte.MacroDeck.Utils;
using SuchByte.MacroDeck.Variables;

namespace SuchByte.MacroDeck.GUI
{
    public partial class ButtonEditor : DialogForm
    {
        static JsonSerializerSettings jsonSerializerSettings = new()
        {
            TypeNameHandling = TypeNameHandling.Auto,
            NullValueHandling = NullValueHandling.Ignore,
            Error = (sender, args) => { args.ErrorContext.Handled = true; }
        };


        private ActionButton.ActionButton actionButton;
        private ActionButton.ActionButton actionButtonEdited;
        private readonly MacroDeckFolder folder;
        private EventSelector eventSelector;
        private ActionSelectorOnPress actionSelectorOnPress;
        private ActionSelectorOnPress actionSelectorOnRelease;
        private ActionSelectorOnPress actionSelectorOnLongPress;
        private ActionSelectorOnPress actionSelectorOnLongPressRelease;

        public ActionButton.ActionButton ActionButton => actionButton;

        public ButtonEditor(ActionButton.ActionButton actionButton, MacroDeckFolder folder)
        {
            InitializeComponent();
            lblAppearance.Text = LanguageManager.Strings.Appearance;
            lblState.Text = LanguageManager.Strings.ButtonState;
            lblButtonState.Text = LanguageManager.Strings.ButtonState;
            radioButtonOff.Text = LanguageManager.Strings.Off;
            radioButtonOn.Text = LanguageManager.Strings.On;
            lblCurrentStateLabel.Text = LanguageManager.Strings.CurrentState;
            lblStateBinding.Text = LanguageManager.Strings.StateBinding;
            radioOnPress.Text = LanguageManager.Strings.OnPress;
            radioOnRelease.Text = LanguageManager.Strings.OnRelease;
            radioOnLongPress.Text = LanguageManager.Strings.OnLongPress;
            radioOnLongPressRelease.Text = LanguageManager.Strings.OnLongPressRelease;
            radioOnEvent.Text = LanguageManager.Strings.OnEvent;
            btnApply.Text = LanguageManager.Strings.Save;
            btnOk.Text = LanguageManager.Strings.Ok;
            lblKeyBinding.Text = LanguageManager.Strings.Hotkey;

            this.folder = folder;
            this.actionButton = actionButton;
            
            using (var col = new InstalledFontCollection())
            {
                foreach (var fontFamily in col.Families)
                {
                    fonts.Items.Add(fontFamily.Name);
                }
            }
            listStateBinding.Items.Add("");
            foreach (var variable in VariableManager.ListVariables)
            {
                listStateBinding.Items.Add(variable.Name);
            }
            this.actionButton.StateChanged += OnStateChanged;
            hotkey.Click += Hotkey_Click;
        }

        private void ButtonEditor_Load(object sender, EventArgs e)
        {
            LoadButton();
            btnPreview.Radius = ProfileManager.CurrentProfile.ButtonRadius;
            UpdateLabel();
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            if (this == null || IsDisposed || Disposing) return;
            try
            {
                Invoke(() =>
                {
                
                    var newState = ((ActionButton.ActionButton)sender).State;
                    lblCurrentState.Text = newState ? "On" : "Off";
                    radioButtonOff.Checked = !newState;
                    radioButtonOn.Checked = newState;
                });
            }
            catch { }

        }

        private void UpdateLabel()
        {
            try
            {
                var buttonLabel = radioButtonOff.Checked && !radioButtonOn.Checked ? actionButtonEdited.LabelOff : actionButtonEdited.LabelOn;
                buttonLabel.LabelText = this.labelText.Text;
                buttonLabel.Size = (float)fontSize.Value;
                buttonLabel.FontFamily = fonts.Text;
                if (labelAlignTop.Checked)
                {
                    buttonLabel.LabelPosition = ButtonLabelPosition.TOP;
                }
                else if (labelAlignCenter.Checked)
                {
                    buttonLabel.LabelPosition = ButtonLabelPosition.CENTER;
                }
                else if (labelAlignBottom.Checked)
                {
                    buttonLabel.LabelPosition = ButtonLabelPosition.BOTTOM;
                }
                Task.Run(() =>
                {
                    btnForeColor.BackColor = buttonLabel.LabelColor;
                    btnForeColor.HoverColor = buttonLabel.LabelColor;
                    var labelBitmap = new Bitmap(250, 250);
                    var labelText = buttonLabel.LabelText;
                    labelText = VariableManager.RenderTemplate(labelText);

                    labelBitmap = (Bitmap)LabelGenerator.GetLabel(labelBitmap, labelText, buttonLabel.LabelPosition, new Font(buttonLabel.FontFamily, buttonLabel.Size), buttonLabel.LabelColor, Color.Black, new SizeF(2.0F, 2.0F));
                    buttonLabel.LabelBase64 = Base64.GetBase64FromImage(labelBitmap);
                    Invoke(() => {
                        if (this != null && Disposing == false && IsDisposed == false)
                        {
                            btnPreview.ForegroundImage = labelBitmap;
                        }
                    });
                });
            } catch (Exception ex)
            {
                MacroDeckLogger.Error(GetType(), "Error while updating label: " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
        }

        private void RefreshLabel()
        {
            labelText.TextChanged -= LabelChanged;
            fontSize.ValueChanged -= LabelChanged;
            labelAlignTop.CheckedChanged -= LabelChanged;
            labelAlignCenter.CheckedChanged -= LabelChanged;
            labelAlignBottom.CheckedChanged -= LabelChanged;
            fonts.SelectedIndexChanged -= LabelChanged;
            
            try
            {
                var buttonLabel = radioButtonOff.Checked && !radioButtonOn.Checked ? actionButtonEdited.LabelOff : actionButtonEdited.LabelOn;

                labelText.Text = buttonLabel.LabelText;
                fontSize.Value = (int)buttonLabel.Size;
                fonts.Text = buttonLabel.FontFamily;

                switch (buttonLabel.LabelPosition)
                {
                    case ButtonLabelPosition.TOP:
                        labelAlignTop.Checked = true;
                        break;
                    case ButtonLabelPosition.CENTER:
                        labelAlignCenter.Checked = true;
                        break;
                    case ButtonLabelPosition.BOTTOM:
                        labelAlignBottom.Checked = true;
                        break;
                }

                if (string.IsNullOrWhiteSpace(labelText.Text))
                {
                    labelText.PlaceHolderText = LanguageManager.Strings.Label;
                }
            } catch (Exception ex) 
            {
                MacroDeckLogger.Error(GetType(), "Error while refreshing label: " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
            labelText.TextChanged += LabelChanged;
            fontSize.ValueChanged += LabelChanged;
            labelAlignTop.CheckedChanged += LabelChanged;
            labelAlignCenter.CheckedChanged += LabelChanged;
            labelAlignBottom.CheckedChanged += LabelChanged;
            fonts.SelectedIndexChanged += LabelChanged;
        }

        public void RefreshIcon()
        {
            try
            {
                var iconString = radioButtonOff.Checked && !radioButtonOn.Checked ? actionButtonEdited.IconOff : actionButtonEdited.IconOn;

                if (!string.IsNullOrWhiteSpace(iconString))
                {
                    var icon = IconManager.GetIconByString(iconString);
                    if (icon != null)
                        btnPreview.BackgroundImage = icon.IconImage;
                }
                else
                {
                    btnPreview.BackgroundImage = null;
                }

                var buttonLabel = radioButtonOff.Checked && !radioButtonOn.Checked ? actionButtonEdited.LabelOff : actionButtonEdited.LabelOn;

                if (buttonLabel != null && !string.IsNullOrWhiteSpace(buttonLabel.LabelBase64)) 
                {
                    var label = Base64.GetImageFromBase64(buttonLabel.LabelBase64);
                    if (label != null)
                        btnPreview.ForegroundImage = label;
                }
                else
                {
                    btnPreview.ForegroundImage = null;
                }

                var backColor = radioButtonOff.Checked && !radioButtonOn.Checked ? actionButtonEdited.BackColorOff : actionButtonEdited.BackColorOn;
                btnBackColor.BackColor = backColor;
                btnBackColor.HoverColor = backColor;
                btnPreview.BackColor = backColor;

                btnPreview.ShowGIFIndicator = btnPreview.BackgroundImage != null && btnPreview.BackgroundImage.RawFormat.ToString().ToLower() == "gif";
            } catch (Exception ex) 
            {
                MacroDeckLogger.Error(GetType(), "Error while refreshing icon: " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
        }

        private void Apply()
        {
            HotkeyManager.RemoveHotkey(this.actionButton);
            this.actionButton = actionButtonEdited;
            this.actionButton.EventListeners = new List<EventListener>();

            foreach (var pluginAction in this.actionButton.Actions)
            {
                pluginAction.SetActionButton(actionButton);
            }
            foreach (var pluginAction in this.actionButton.ActionsRelease)
            {
                pluginAction.SetActionButton(actionButton);
            }
            foreach (var pluginAction in this.actionButton.ActionsLongPress)
            {
                pluginAction.SetActionButton(actionButton);
            }
            foreach (var pluginAction in this.actionButton.ActionsLongPressRelease)
            {
                pluginAction.SetActionButton(actionButton);
            }

            foreach (var eventItem in eventSelector.EventItems())
            {
                if (eventItem == null) continue;
                foreach (var pluginAction in eventItem.EventListener.Actions)
                {
                    pluginAction.SetActionButton(actionButton);
                }
                actionButton.EventListeners.Add(eventItem.EventListener);
            }

            foreach (var actionButton in folder.ActionButtons.FindAll(actionButton => actionButton.Position_Y == this.actionButton.Position_Y && actionButton.Position_X == this.actionButton.Position_X).ToArray())
            {
                folder.ActionButtons.Remove(actionButton);
            }
            folder.ActionButtons.Add(this.actionButton);
            ProfileManager.Save();
            MacroDeckServer.UpdateFolder(folder);
            ProfileManager.UpdateVariableLabels(this.actionButton);
            this.actionButton.UpdateBindingState();
            this.actionButton.UpdateHotkey();
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            Apply();
        }


        private void BtnOk_Click(object sender, EventArgs e)
        {
            Apply();
            Close();
        }

        private void BtnPreview_Click(object sender, EventArgs e)
        {
            OpenIconSelector();
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
                            actionButtonEdited.IconOff = iconSelector.SelectedIconPack.Name + "." + iconSelector.SelectedIcon.IconId;
                        }
                        else
                        {
                            actionButtonEdited.IconOn = iconSelector.SelectedIconPack.Name + "." + iconSelector.SelectedIcon.IconId;
                        }
                        RefreshIcon();
                    }
                }
            }
        }

        public void LoadButton()
        {
            actionButtonEdited = JsonConvert.DeserializeObject<ActionButton.ActionButton>(JsonConvert.SerializeObject(actionButton, jsonSerializerSettings), jsonSerializerSettings); // Make a copy of the current action button

            var currentState = actionButton.State;
            lblCurrentState.Text = currentState ? "On" : "Off";
            listStateBinding.Text = actionButtonEdited.StateBindingVariable;
            buttonGUIDLabel.Text = actionButtonEdited.Guid;

            if (actionButton.KeyCode != Keys.None)
            {
                hotkey.Text = actionButton.ModifierKeyCodes.ToString().Replace("Control", "CTRL").Replace("None", string.Empty).Replace(", ", " + ") + (!actionButton.ModifierKeyCodes.ToString().Equals("None") ? " + " : string.Empty) + actionButton.KeyCode;
            }

            RefreshLabel();
            RefreshIcon();
            actionSelectorOnPress = new ActionSelectorOnPress(actionButtonEdited.Actions, this);
            actionSelectorOnRelease = new ActionSelectorOnPress(actionButtonEdited.ActionsRelease, this);
            actionSelectorOnLongPress = new ActionSelectorOnPress(actionButtonEdited.ActionsLongPress, this);
            actionSelectorOnLongPressRelease = new ActionSelectorOnPress(actionButtonEdited.ActionsLongPressRelease, this);
            actionSelectorOnPress.RefreshActions();
            actionSelectorOnRelease.RefreshActions();
            actionSelectorOnLongPress.RefreshActions();
            actionSelectorOnLongPressRelease.RefreshActions();
            eventSelector = new EventSelector(actionButtonEdited);
            eventSelector.RefreshEventsList();
            SetSelector(actionSelectorOnPress);
        }

        public void SetBindableVariable(string variable)
        {
            listStateBinding.Text = variable;
        }

        private void LabelChanged(object sender, EventArgs e)
        {
            UpdateLabel();
        }


       
        private void RadioButton_CheckedChanged(object sender, EventArgs e)
        {
            RefreshLabel();
            RefreshIcon();
            UpdateLabel();
        }

        private void BtnEditIcon_Click(object sender, EventArgs e)
        {
            OpenIconSelector();
        }

        private void BtnRemoveIcon_Click(object sender, EventArgs e)
        {
            if (radioButtonOff.Checked && !radioButtonOn.Checked)
            {
                actionButtonEdited.IconOff = null;
            }
            else
            {
                actionButtonEdited.IconOn = null;
            }
            RefreshIcon();
        }

        private void BtnBackColor_Click(object sender, EventArgs e)
        {
            using (var colorDialog = new ColorDialog
                   {
                Color = radioButtonOff.Checked && !radioButtonOn.Checked ? actionButtonEdited.BackColorOff : actionButtonEdited.BackColorOn,
                FullOpen = true,
                CustomColors = new[] { ColorTranslator.ToOle(Color.FromArgb(35, 35, 35)) }
            })
            {
                if (colorDialog.ShowDialog() == DialogResult.OK)
                {
                    if (radioButtonOff.Checked && !radioButtonOn.Checked)
                    {
                        actionButtonEdited.BackColorOff = colorDialog.Color;
                    }
                    else
                    {
                        actionButtonEdited.BackColorOn = colorDialog.Color;
                    }
                    RefreshIcon();
                }
            }

        }

        private void BtnClearLabelText_Click(object sender, EventArgs e)
        {
            labelText.Clear();
        }

        private void BtnForeColor_Click(object sender, EventArgs e)
        {
            using (var colorDialog = new ColorDialog())
            {
                if (colorDialog.ShowDialog() == DialogResult.OK)
                {
                    if (radioButtonOff.Checked && !radioButtonOn.Checked)
                    {
                        actionButtonEdited.LabelOff.LabelColor = colorDialog.Color;
                    }
                    else
                    {
                        actionButtonEdited.LabelOn.LabelColor = colorDialog.Color;
                    }
                    UpdateLabel();
                }
            }
            
        }

        private void BtnAddVariable_Click(object sender, EventArgs e)
        {
            variablesContextMenu.Items.Clear();
            foreach (var variable in VariableManager.ListVariables)
            {
                var item = new ToolStripMenuItem
                {
                    ForeColor = Color.White,
                    Text = variable.Name,
                };
                item.Click += AddVariableContextMenuItemClick;
                variablesContextMenu.Items.Add(item);
            }
            variablesContextMenu.Show(PointToScreen(new Point(((PictureButton)sender).Bounds.Left, ((PictureButton)sender).Bounds.Bottom)));
        }

        private void AddVariableContextMenuItemClick(object sender, EventArgs e)
        {
            var item = (ToolStripMenuItem)sender;
            var selectionIndex = labelText.SelectionStart;
            labelText.Text = labelText.Text.Insert(selectionIndex, "{" + item.Text + "}");
            labelText.SelectionStart = selectionIndex + ("{" + item.Text + "}").Length;
        }

        private void ListStateBinding_SelectedIndexChanged(object sender, EventArgs e)
        {
            actionButtonEdited.StateBindingVariable = listStateBinding.Text;
        }

        private void BtnDeleteStateBinding_Click(object sender, EventArgs e)
        {
            listStateBinding.Text = "";
        }

        private void RadioOnPress_CheckedChanged(object sender, EventArgs e)
        {
            if (radioOnPress.Checked)
            {
                SetSelector(actionSelectorOnPress);
            }
        }

        private void RadioOnRelease_CheckedChanged(object sender, EventArgs e)
        {
            if (radioOnRelease.Checked)
            {
                SetSelector(actionSelectorOnRelease);
            }
        }

        private void RadioOnLongPress_CheckedChanged(object sender, EventArgs e)
        {
            if (radioOnLongPress.Checked)
            {
                SetSelector(actionSelectorOnLongPress);
            }
        }

        private void RadioOnLongPressRelease_CheckedChanged(object sender, EventArgs e)
        {
            if (radioOnLongPressRelease.Checked)
            {
                SetSelector(actionSelectorOnLongPressRelease);
            }
        }

        private void RadioOnEvent_CheckedChanged(object sender, EventArgs e)
        {
            if (radioOnEvent.Checked)
            {
                SetSelector(eventSelector);
            }
        }

        private void SetSelector(Control control)
        {
            selectorPanel.Controls.Clear();
            selectorPanel.Controls.Add(control);
        }

        private void BtnOpenTemplateEditor_Click(object sender, EventArgs e)
        {
            using (var templateEditor = new TemplateEditor(labelText.Text))
            {
                if (templateEditor.ShowDialog() == DialogResult.OK)
                {
                    labelText.Text = templateEditor.Template;
                }
            }
        }

        private void BtnRemoveHotkey_Click(object sender, EventArgs e)
        {
            actionButtonEdited.ModifierKeyCodes = Keys.None;
            actionButtonEdited.KeyCode = Keys.None;
            HotkeyManager.RemoveHotkey(actionButtonEdited);
            hotkey.Text = string.Empty;
        }

        private void Hotkey_Click(object sender, EventArgs e)
        {
            using (var hotkeySelector = new HotkeySelector())
            {
                if (hotkeySelector.ShowDialog() == DialogResult.OK)
                {
                    hotkey.Text = hotkeySelector.ModifierKeys.ToString().Replace("Control", "CTRL").Replace("None", string.Empty).Replace(", ", " + ") + (!hotkeySelector.ModifierKeys.ToString().Equals("None") ? " + " : string.Empty) + hotkeySelector.Key;
                    actionButtonEdited.ModifierKeyCodes = hotkeySelector.ModifierKeys;
                    actionButtonEdited.KeyCode = hotkeySelector.Key;
                }
            }
        }

        private void BtnEditJson_Click(object sender, EventArgs e)
        {
            using (var jsonButtonEditor = new JsonButtonEditor(actionButtonEdited))
            {
                if (jsonButtonEditor.ShowDialog() == DialogResult.OK)
                {
                    jsonButtonEditor.ActionButton.Position_X = actionButtonEdited.Position_X;
                    jsonButtonEditor.ActionButton.Position_Y = actionButtonEdited.Position_Y;
                    jsonButtonEditor.ActionButton.Guid = actionButtonEdited.Guid;
                    actionButton = jsonButtonEditor.ActionButton;
                    LoadButton();
                    UpdateLabel();
                }
            }
        }

        private void label4_Click(object sender, EventArgs e)
        {

        }
    }
}
