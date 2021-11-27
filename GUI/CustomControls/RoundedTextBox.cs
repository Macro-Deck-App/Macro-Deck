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
    public partial class RoundedTextBox : UserControl
    {
        private int borderRadius = 8;
        private Color placeHolderColor = Color.Gray;
        private string placeHolderText = "";
        private bool isPlaceHolder = false;
        private bool isPasswordChar = false;
        private Image icon;

        public Image Icon
        {
            get { return icon; }
            set
            {
                icon = value;
                this.Padding = icon == null ? new Padding(8, 5, 8, 5) : new Padding(this.textBox1.Height + 8 + 3, 5, 8, 5);
                this.Invalidate();
            }
        }

        public ScrollBars ScrollBars
        {
            get { return this.textBox1.ScrollBars; }
            set
            {
                this.textBox1.ScrollBars = value;
            }
        }

        public bool ReadOnly
        {
            get { return textBox1.ReadOnly; }
            set
            {
                textBox1.ReadOnly = value;
            }
        }

        public HorizontalAlignment TextAlignment {
            get { return textBox1.TextAlign; }
            set
            {
                textBox1.TextAlign = value;
            }
        }
        
        public bool PasswordChar
        {
            get { return isPasswordChar; }
            set
            {
                isPasswordChar = value;
                if (!isPlaceHolder)
                    textBox1.UseSystemPasswordChar = value;
            }
        }

        public int MaxCharacters
        {
            get { return this.textBox1.MaxLength; }
            set
            {
                this.textBox1.MaxLength = value;
            }
        }

        public void SetAutoCompleteCustomSource(AutoCompleteStringCollection autoCompleteStringCollection)
        {
            textBox1.AutoCompleteCustomSource = autoCompleteStringCollection;
        }

        public void SetAutoCompleteMode(AutoCompleteMode autoCompleteMode)
        {
            textBox1.AutoCompleteMode = autoCompleteMode;
        }

        public void SetAutoCompleteSource(AutoCompleteSource autoCompleteSource)
        {
            textBox1.AutoCompleteSource = autoCompleteSource;
        }

        public bool Multiline
        {
            get { return textBox1.Multiline; }
            set { textBox1.Multiline = value; }
        }

        
        public override Color BackColor
        {
            get { return base.BackColor; }
            set
            {
                base.BackColor = value;
                textBox1.BackColor = value;
            }
        }

        
        public override Color ForeColor
        {
            get { return base.ForeColor; }
            set
            {
                base.ForeColor = value;
                textBox1.ForeColor = value;
            }
        }

        
        public override Font Font
        {
            get { return base.Font; }
            set
            {
                base.Font = value;
                textBox1.Font = value;
                if (this.DesignMode)
                {
                    UpdateControlHeight();
                }
            }
        }

        
        public override string Text
        {

            get { return (isPlaceHolder || textBox1.Text == this.placeHolderText) ? string.Empty : textBox1.Text; }
            set
            {
                if (value != null && value != this.placeHolderText && value.Length > 0 && value != "")
                {
                    RemovePlaceholder();
                }
                else if (value != null && value.Length == 0 || value != "")
                {
                    SetPlaceholder();
                    textBox1.Text = placeHolderText;
                }
                textBox1.Text = value;
            }
        }

        
        public Color PlaceHolderColor
        {
            get { return placeHolderColor; }
            set
            {
                placeHolderColor = value;
                if (isPlaceHolder)
                {
                    textBox1.ForeColor = value;
                }   
            }
        }

        
        public string PlaceHolderText
        {
            get { return placeHolderText; }
            set
            {
                placeHolderText = value;
                textBox1.Text = "";
                SetPlaceholder();
            }
        }

        public int SelectionStart
        {
            get { return textBox1.SelectionStart; }
            set
            {
                textBox1.SelectionStart = value;
            }
        }

        public void Clear()
        {
            textBox1.Clear();
        }

        public RoundedTextBox()
        {
            InitializeComponent();
        }

        private void SetPlaceholder()
        {
            if (string.IsNullOrWhiteSpace(textBox1.Text) && placeHolderText != "")
            {
                this.Font = new Font(this.Font, FontStyle.Italic);
                isPlaceHolder = true;
                textBox1.Text = placeHolderText;
                textBox1.ForeColor = placeHolderColor;
                if (isPasswordChar)
                    textBox1.UseSystemPasswordChar = false;
            }
        }
        private void RemovePlaceholder()
        {
            if (isPlaceHolder && placeHolderText != "")
            {
                this.Font = new Font(this.Font, FontStyle.Regular);
                isPlaceHolder = false;
                textBox1.Text = "";
                textBox1.ForeColor = this.ForeColor;
                if (isPasswordChar)
                    textBox1.UseSystemPasswordChar = true;
            }
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
            if (textBox1.Multiline == false)
            {
                int txtHeight = TextRenderer.MeasureText("Text", this.Font).Height + 1;
                textBox1.Multiline = true;
                textBox1.MinimumSize = new Size(0, txtHeight);
                textBox1.Multiline = false;

                this.Height = textBox1.Height + this.Padding.Top + this.Padding.Bottom;
            }
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
                var rectIcon = new Rectangle(this.ClientRectangle.X + 3, this.ClientRectangle.Y + (this.ClientRectangle.Height / 2) - (this.textBox1.Height / 2), this.textBox1.Height, this.textBox1.Height);

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

        private void TextBox1_LostFocus(object sender, EventArgs e)
        {
            SetPlaceholder();
        }

        private void TextBox1_GotFocus(object sender, EventArgs e)
        {
            RemovePlaceholder();
        }

        private void TextBox1_MouseEnter(object sender, System.EventArgs e)
        {
            this.OnMouseEnter(e);
        }

        private void TextBox1_MouseLeave(object sender, System.EventArgs e)
        {
            this.OnMouseLeave(e);
        }

        private void TextBox1_KeyPress(object sender, System.Windows.Forms.KeyPressEventArgs e)
        {
            this.OnKeyPress(e);
        }

        private void TextBox1_Enter(object sender, System.EventArgs e)
        {
            this.OnEnter(e);
        }

        private void TextBox1_TextChanged(object sender, System.EventArgs e)
        {
            if (isPlaceHolder) return;
            this.OnTextChanged(e);
        }

        private void TextBox1_Click(object sender, System.EventArgs e)
        {
            this.OnClick(e);
        }
    }
}
