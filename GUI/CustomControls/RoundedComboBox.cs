using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    [DefaultEvent(nameof(SelectedIndexChanged))]
    [DefaultProperty(nameof(Items))]
    [DefaultBindingProperty(nameof(Text))]
    public partial class RoundedComboBox : UserControl
    {

        private int borderRadius = 8;
        private Image icon;


        public event EventHandler SelectedIndexChanged;

        public Image Icon
        {
            get { return icon; }
            set
            {
                icon = value;
                this.Padding = icon == null ? new Padding(8, 2, 8, 2) : new Padding(this.borderlessComboBox1.Height + 8 + 3, 2, 8, 2);
                this.Invalidate();
            }
        }

        public int FindStringExact(string str)
        {
            return this.borderlessComboBox1.FindStringExact(str);
        }

        public new bool Enabled
        {
            get
            {
                return this.borderlessComboBox1.Enabled;
            }
            set
            {
                this.borderlessComboBox1.Enabled = value;
            }
        }

        public void SetAutoCompleteCustomSource(AutoCompleteStringCollection autoCompleteStringCollection)
        {
            this.borderlessComboBox1.AutoCompleteCustomSource = autoCompleteStringCollection;
        }

        public void SetAutoCompleteMode(AutoCompleteMode autoCompleteMode)
        {
            this.borderlessComboBox1.AutoCompleteMode = autoCompleteMode;
        }

        public void SetAutoCompleteSource(AutoCompleteSource autoCompleteSource)
        {
            this.borderlessComboBox1.AutoCompleteSource = autoCompleteSource;
        }


        public System.Windows.Forms.ComboBoxStyle DropDownStyle
        {
            get
            {
                return this.borderlessComboBox1.DropDownStyle;
            }
            set
            {
                this.borderlessComboBox1.DropDownStyle = value;
            }
        }

        [System.ComponentModel.Browsable(true)]
        public System.Windows.Forms.ComboBox.ObjectCollection Items
        {
            get
            {
                return this.borderlessComboBox1.Items;
            }
        }

        [System.ComponentModel.Browsable(false)]
        public int SelectedIndex
        {
            get
            {
                return this.borderlessComboBox1.SelectedIndex;
            }
            set
            {
                this.borderlessComboBox1.SelectedIndex = value;
            }
        }

        [System.ComponentModel.Bindable(true)]
        [System.ComponentModel.Browsable(false)]
        public object SelectedItem
        {
            get
            {
                return this.borderlessComboBox1.SelectedItem;
            }
            set
            {
                this.borderlessComboBox1.SelectedItem = value;
            }
        }

        [System.ComponentModel.Bindable(true)]
        public override string Text
        {
            get
            {
                return this.borderlessComboBox1.Text;
            }
            set
            {
                this.borderlessComboBox1.Text = value;
            }
        }

        public override Font Font
        {
            get { return base.Font; }
            set
            {
                base.Font = value;
                borderlessComboBox1.Font = value;
                if (this.DesignMode)
                {
                    UpdateControlHeight();
                }
            }
        }

        public RoundedComboBox()
        {
            InitializeComponent();
            SetStyle(
                    ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.AllPaintingInWmPaint,
                    true);
            this.Cursor = Cursors.Hand;

            this.DoubleBuffered = true;
        }


        private GraphicsPath GetFigurePath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            float curveSize = radius * 2F;

            path.StartFigure();
            path.AddArc(rect.X, rect.Y, curveSize, curveSize, 180, 90);
            path.AddArc(rect.Right - curveSize, rect.Y, curveSize, curveSize, 270, 90);
            path.AddArc(rect.Right - curveSize, rect.Bottom - curveSize, curveSize, curveSize, 0, 90);
            path.AddArc(rect.X, rect.Bottom - curveSize, curveSize, curveSize, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void UpdateControlHeight()
        {
            this.Height = this.borderlessComboBox1.Height + this.Padding.Top + this.Padding.Bottom;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (this.DesignMode)
                UpdateControlHeight();
        }
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            UpdateControlHeight();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graph = e.Graphics;

            if (borderRadius > 1)
            {
                var rectBorderSmooth = this.ClientRectangle;
                var rectIcon = new Rectangle(this.ClientRectangle.X + 3, this.ClientRectangle.Y + (this.ClientRectangle.Height / 2) - (this.borderlessComboBox1.Height / 2), this.borderlessComboBox1.Height, this.borderlessComboBox1.Height);

                int smoothSize = 2;


                using (GraphicsPath pathBorderSmooth = GetFigurePath(rectBorderSmooth, borderRadius))
                using (Pen penBorderSmooth = new Pen(this.Parent.BackColor, smoothSize))
                {

                    this.Region = new Region(pathBorderSmooth);
                    if (icon != null)
                    {
                        graph.DrawImage(icon, rectIcon);
                    }
                    graph.SmoothingMode = SmoothingMode.AntiAlias;
                    graph.DrawPath(penBorderSmooth, pathBorderSmooth);
                }
            }
            else
            {
                this.Region = new Region(this.ClientRectangle);
            }
        }


        private void BorderlessComboBox1_MouseLeave(object sender, System.EventArgs e)
        {
            this.OnMouseLeave(e);

        }


        private void BorderlessComboBox1_LostFocus(object sender, System.EventArgs e)
        {
            this.OnLostFocus(e);
            if (!this.borderlessComboBox1.Items.Contains(this.borderlessComboBox1.Text))
            {
                this.borderlessComboBox1.Items.Add(this.borderlessComboBox1.Text);
            }
        }

        private void BorderlessComboBox1_KeyPress(object sender, System.Windows.Forms.KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Return)
            {
                if (!this.borderlessComboBox1.Items.Contains(this.borderlessComboBox1.Text))
                {
                    this.borderlessComboBox1.Items.Add(this.borderlessComboBox1.Text);
                }
                e.Handled = true;
            }

            this.OnKeyPress(e);
        }

        private void BorderlessComboBox1_GotFocus(object sender, System.EventArgs e)
        {
            this.OnGotFocus(e);
        }

        private void BorderlessComboBox1_Enter(object sender, System.EventArgs e)
        {
            this.OnEnter(e);
        }

        private void BorderlessComboBox1_Click(object sender, System.EventArgs e)
        {
            this.OnClick(e);
        }

        private void BorderlessComboBox1_TextChanged(object sender, EventArgs e)
        {
            this.OnTextChanged(e);
        }

        private void BorderlessComboBox1_SelectedIndexChanged(object sender, System.EventArgs e)
        {
            if (SelectedIndexChanged != null)
            {
                SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

    }
}
